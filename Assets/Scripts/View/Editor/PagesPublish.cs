using System.IO;
using UnityEditor;
using UnityEngine;

// System.Diagnostics is deliberately not imported: it carries a Debug of its own,
// which would make every Debug.Log in this file an ambiguous reference against
// UnityEngine.Debug. Process and ProcessStartInfo are spelled out below instead.

namespace BitSorter.View.Editor
{
    /// <summary>
    /// Menu wrapper around Tools/publish-pages.ps1, which pushes Build/WebGL to the
    /// gh-pages branch.
    /// </summary>
    /// <remarks>
    /// The git work lives in the script rather than here, and this only launches it.
    /// Two reasons, both learned the tedious way round.
    ///
    /// It runs in a console window the player can see, instead of piping the output
    /// into the Unity console. A push has to authenticate, and if the credential
    /// helper ever decides to prompt, a redirected process would sit there waiting on
    /// stdin with the editor frozen behind it and nothing on screen to explain why.
    /// A real window shows the prompt, and the upload progress of a 14 MB build.
    ///
    /// It also means the publish is runnable without Unity open, which matters when
    /// the thing being fixed is the page rather than the game.
    /// </remarks>
    public static class PagesPublish
    {
        private const string ScriptPath = "Tools/publish-pages.ps1";
        private const string BuildPath = "Build/WebGL";

        [MenuItem("BitSorter/Publish WebGL to GitHub Pages")]
        public static void Publish()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string script = Path.Combine(root, ScriptPath);
            string build = Path.Combine(root, BuildPath);

            if (!File.Exists(script))
            {
                Debug.LogError($"BitSorter: cannot publish, {ScriptPath} is missing.");
                return;
            }

            if (!File.Exists(Path.Combine(build, "index.html")))
            {
                EditorUtility.DisplayDialog(
                    "No WebGL build",
                    $"There is no build at {BuildPath}.\n\n" +
                    "Run BitSorter > Build WebGL Player first.",
                    "OK");
                return;
            }

            // Force-pushing gh-pages replaces what the public URL serves, so ask first.
            // The script's own staleness check is the second guard, and it runs after this.
            bool go = EditorUtility.DisplayDialog(
                "Publish to GitHub Pages",
                $"Force-push the current contents of {BuildPath} to the gh-pages branch?\n\n" +
                "This replaces whatever the live page serves now. A console window will " +
                "open with the result.",
                "Publish",
                "Cancel");

            if (!go)
            {
                return;
            }

            // -NoExit leaves the window up so the result stays readable. Bypass applies to
            // this one invocation only and changes no machine policy; without it the default
            // policy refuses an unsigned local script.
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoExit -NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
                WorkingDirectory = root,
                UseShellExecute = true,
            };

            System.Diagnostics.Process.Start(startInfo);

            Debug.Log("BitSorter: publishing to GitHub Pages in a separate console window.");
        }
    }
}
