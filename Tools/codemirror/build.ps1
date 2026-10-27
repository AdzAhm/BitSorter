# Rebuilds Assets/Plugins/WebGL/codemirror.jspre from the pinned packages in package-lock.json (Part 12f).
# npm ci installs exactly the locked versions, and --ignore-scripts runs none of their install scripts.
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    npm ci --ignore-scripts --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed" }
    # The editor's own tests (Part 14g), under Node's test runner: what the game puts in is no step of the editor's undo.
    node --test "test/*.test.mjs"
    if ($LASTEXITCODE -ne 0) { throw "the editor's tests failed" }
    node build.mjs
    if ($LASTEXITCODE -ne 0) { throw "the bundle did not build" }
}
finally {
    Pop-Location
}
