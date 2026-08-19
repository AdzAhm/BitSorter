<#
.SYNOPSIS
    Publishes Build/WebGL to the gh-pages branch on GitHub.

.DESCRIPTION
    Build the player first with BitSorter -> Build WebGL Player, then run this.

    The branch is rebuilt from nothing on every run and force-pushed, so its
    history never grows. That is deliberate: a 14 MB WebGL build barely deltas
    against the previous one, so appending commits would add roughly the whole
    build to the repository every time. Nobody needs last week's binaries.

    The work happens in a scratch repository under $env:TEMP, created with
    git init rather than git clone. A fresh repository's first commit has no
    parent, which is exactly what an orphan branch is -- so there is no
    "empty the working tree" step to get wrong, and nothing ever runs git rm
    inside the project folder while Unity is watching it.

.PARAMETER Branch
    Branch to publish to. Defaults to gh-pages.

.PARAMETER KeepTemp
    Leave the scratch repository in place afterwards, to look at what was sent.

.EXAMPLE
    .\Tools\publish-pages.ps1
#>

[CmdletBinding()]
param(
    [string] $Branch = 'gh-pages',
    [switch] $KeepTemp
)

$ErrorActionPreference = 'Stop'

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)] [string[]] $Arguments,
        [string] $In
    )

    if ($In) { $all = @('-C', $In) + $Arguments } else { $all = $Arguments }

    & git @all
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Get-PagesUrl {
    param([string] $RemoteUrl)

    # github.com/OWNER/REPO(.git), in either the https or the ssh spelling.
    if ($RemoteUrl -match 'github\.com[:/](?<owner>[^/]+)/(?<repo>[^/]+?)(\.git)?$') {
        $owner = $Matches['owner']
        $repo = $Matches['repo']
        return "https://$($owner.ToLower()).github.io/$repo/"
    }

    return $null
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$webglPath = Join-Path $repoRoot 'Build\WebGL'
$indexPath = Join-Path $webglPath 'index.html'

# ---------------------------------------------------------------------
# Is there a build to publish, and is it the current one?
# ---------------------------------------------------------------------

if (-not (Test-Path $indexPath)) {
    throw "No WebGL build at $webglPath. Run BitSorter -> Build WebGL Player first."
}

foreach ($required in @('Build', 'TemplateData')) {
    if (-not (Test-Path (Join-Path $webglPath $required))) {
        throw "$webglPath is missing its $required folder. The build did not finish."
    }
}

# The newest file in the output, not index.html. Unity leaves index.html alone when the
# template renders identically, so a rebuild that changes only code leaves its timestamp
# hours behind -- which made this report a stale time and then wrongly accuse a current
# build of being stale.
$buildTime = (Get-ChildItem -Path $webglPath -Recurse -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1).LastWriteTime

Write-Host "Build:  $webglPath" -ForegroundColor Cyan
Write-Host "Built:  $buildTime"

$assetsPath = Join-Path $repoRoot 'Assets'

# Only things that actually end up in the player. Editor and test code is compiled
# into assemblies a WebGL build never contains, so counting them would raise a
# warning on every publish that followed a tooling change -- and a warning that
# cries wolf is one nobody reads.
$newestAsset = Get-ChildItem -Path $assetsPath -Recurse -File -Include '*.cs', '*.json', '*.unity' |
    Where-Object { $_.FullName -notmatch '\\(Editor|Tests)\\' } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($newestAsset -and $newestAsset.LastWriteTime -gt $buildTime) {
    Write-Host ''
    Write-Host "WARNING: $($newestAsset.Name) changed after this build was made." -ForegroundColor Yellow
    Write-Host "         You are about to publish a stale build." -ForegroundColor Yellow
    Write-Host "         Rebuild with BitSorter -> Build WebGL Player, or continue anyway." -ForegroundColor Yellow
    Write-Host ''
}

# ---------------------------------------------------------------------
# Label the deploy with the source it came from
# ---------------------------------------------------------------------

$remoteUrl = & git -C $repoRoot remote get-url origin
if ($LASTEXITCODE -ne 0) { throw "Could not read the origin remote of $repoRoot." }
$remoteUrl = $remoteUrl.Trim()

$sourceCommit = (& git -C $repoRoot rev-parse --short HEAD).Trim()
$dirty = & git -C $repoRoot status --porcelain

if ([string]::IsNullOrWhiteSpace($dirty)) {
    $sourceLabel = $sourceCommit
} else {
    # Worth recording: the build may contain changes that are not in any commit.
    $sourceLabel = "$sourceCommit+dirty"
}

$commitMessage = "Deploy WebGL build from $sourceLabel"

Write-Host "Source: $sourceLabel"
Write-Host "Remote: $remoteUrl"
Write-Host ""

# ---------------------------------------------------------------------
# Assemble the branch in a scratch repository and force-push it
# ---------------------------------------------------------------------

$tempRepo = Join-Path $env:TEMP 'bitsorter-pages'

if (Test-Path $tempRepo) { Remove-Item -Recurse -Force $tempRepo }
New-Item -ItemType Directory -Path $tempRepo | Out-Null

try {
    Invoke-Git -Arguments @('init', '-b', $Branch, '--quiet') -In $tempRepo
    Invoke-Git -Arguments @('remote', 'add', 'origin', $remoteUrl) -In $tempRepo

    # Publish the bytes that were built, not a rewritten copy of them. With the
    # global core.autocrlf=true this machine has, git would normalise line endings
    # in index.html and style.css on commit. It happens to be a no-op here because
    # Unity emits LF -- but "happens to be" is not a guarantee, and a mangled
    # payload would show up as a blank page with a decompression error.
    Invoke-Git -Arguments @('config', 'core.autocrlf', 'false') -In $tempRepo

    Copy-Item -Path (Join-Path $webglPath '*') -Destination $tempRepo -Recurse -Force

    # Skips Jekyll entirely. Not strictly needed for this file set -- nothing here
    # starts with an underscore -- but it removes a build step and a class of surprise.
    New-Item -ItemType File -Path (Join-Path $tempRepo '.nojekyll') | Out-Null

    if (-not (Test-Path (Join-Path $tempRepo 'index.html'))) {
        throw "index.html is not at the root of $tempRepo. Pages would serve nothing."
    }

    Invoke-Git -Arguments @('add', '-A') -In $tempRepo
    Invoke-Git -Arguments @('commit', '-m', $commitMessage, '--quiet') -In $tempRepo

    $published = (& git -C $tempRepo rev-list --count HEAD).Trim()
    if ($published -ne '1') {
        throw "Expected a single parentless commit on $Branch, found $published."
    }

    Write-Host "Pushing $Branch to origin (force)..." -ForegroundColor Cyan
    Invoke-Git -Arguments @('push', '--force', 'origin', $Branch) -In $tempRepo

    $fileCount = (& git -C $tempRepo ls-files | Measure-Object -Line).Lines

    Write-Host ""
    Write-Host "Pushed $fileCount files to $Branch." -ForegroundColor Green

    $pagesUrl = Get-PagesUrl -RemoteUrl $remoteUrl
    if ($pagesUrl) {
        Write-Host "Live in a minute or two at $pagesUrl" -ForegroundColor Green
    }
}
finally {
    if ($KeepTemp) {
        Write-Host ""
        Write-Host "Scratch repository left at $tempRepo"
    } elseif (Test-Path $tempRepo) {
        Remove-Item -Recurse -Force $tempRepo
    }
}
