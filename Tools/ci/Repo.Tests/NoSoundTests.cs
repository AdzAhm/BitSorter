using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BitSorter.Ci.Tests
{
    /// <summary>
    /// BitSorter has had no sound since Part 16b (Ahmad, 2026-11-03: "remove the music and anything related to it"), and
    /// the player ships with Unity's audio system switched off. The sound's own tests went with it -- the scene's
    /// one-<c>AudioListener</c> test among them -- and left nothing holding what was decided: a clip, a mixer, an audio
    /// component in the scene the build carries or a script that makes one could come back unseen, and with the system
    /// off would do nothing but cost the download. These read the files a build is made from, as the audio audit of
    /// Part 16 did by hand.
    /// </summary>
    /// <remarks>
    /// <c>Assets/Scenes/SampleScene.unity</c> and the URP 2D scene template, <c>Assets/Settings/Scenes</c>, keep their
    /// camera's <c>AudioListener</c>: they are Unity's scaffold from the project's first commit, in no build, and the
    /// template is what File, New Scene starts from. So the scenes held here are the build list's, not every scene.
    /// The built-in audio module stays in the package manifest, since the director, video and web-request audio modules
    /// depend on it, and the AI assistant package that drives the editor from a session offers a tool that edits clips.
    /// </remarks>
    public class NoSoundTests
    {
        /// <summary>A serialized object's header and the type under it, as a scene or a prefab writes every one.</summary>
        private static readonly Regex SerializedObject = new Regex(
            @"^--- !u!\d+ &-?\d+[^\n]*\n(\w+):", RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>Unity's audio API by its names: every type is Audio-something, and the rest are named here.</summary>
        private static readonly Regex AudioApi = new Regex(
            @"\bAudio[A-Z]\w*|\bUnity(Engine|Editor)\.Audio\b|\bMicrophone\b|\bOnAudioFilterRead\b|\bDownloadHandlerAudioClip\b|\bUnityWebRequestMultimedia\b",
            RegexOptions.Compiled);

        /// <summary>What Unity imports as an audio clip, and a mixer.</summary>
        private static readonly string[] AudioExtensions =
        {
            ".wav", ".mp3", ".ogg", ".aif", ".aiff", ".aifc", ".flac", ".mod", ".it", ".s3m", ".xm", ".mixer",
        };

        private static IEnumerable<string> AudioObjectsIn(string yaml) =>
            SerializedObject.Matches(yaml.Replace("\r\n", "\n")).Select(m => m.Groups[1].Value)
                .Where(type => type.StartsWith("Audio", StringComparison.Ordinal));

        private static IEnumerable<string> AudioApiIn(string code)
        {
            string bare = Regex.Replace(code, @"/\*.*?\*/", "", RegexOptions.Singleline);
            bare = Regex.Replace(bare, "//[^\n]*", "");
            return AudioApi.Matches(bare).Select(m => m.Value);
        }

        private static string[] BuildScenes()
        {
            string settings = File.ReadAllText(RepoRoot.Combine("ProjectSettings/EditorBuildSettings.asset")).Replace("\r\n", "\n");
            return Regex.Matches(settings, @"^\s*- enabled: 1\n\s*path: (\S+)$", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value)
                .ToArray();
        }

        [Test]
        public void TheAudioSystem_IsSwitchedOff()
        {
            string settings = File.ReadAllText(RepoRoot.Combine("ProjectSettings/AudioManager.asset"));

            StringAssert.IsMatch(@"(?m)^\s*m_DisableAudio: 1\s*$", settings,
                "the player ships with audio disabled (Part 16b): AudioManager.asset's m_DisableAudio is not 1");
        }

        [Test]
        public void NoClipNorMixer_IsUnderAssets()
        {
            string[] found = Directory.EnumerateFiles(RepoRoot.Combine("Assets"), "*", SearchOption.AllDirectories)
                .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Select(f => Path.GetRelativePath(RepoRoot.Path, f))
                .ToArray();

            Assert.IsEmpty(found, "BitSorter has no sound and no audio files: " + string.Join(", ", found));
        }

        [Test]
        public void TheBuildsScenes_AndEveryPrefab_HoldNoAudioComponent()
        {
            string[] scenes = BuildScenes();
            Assert.IsNotEmpty(scenes, "EditorBuildSettings.asset lists no scene the build carries, so this read nothing");

            IEnumerable<string> prefabs = Directory.EnumerateFiles(RepoRoot.Combine("Assets"), "*.prefab", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(RepoRoot.Path, f).Replace('\\', '/'));

            var found = new List<string>();
            foreach (string file in scenes.Concat(prefabs))
                found.AddRange(AudioObjectsIn(File.ReadAllText(RepoRoot.Combine(file))).Select(type => file + ": " + type));

            Assert.IsEmpty(found, "the player ships with audio disabled, so nothing it carries plays or hears sound:\n" +
                string.Join("\n", found));
        }

        [Test]
        public void NoScript_UsesTheAudioApi()
        {
            var found = new List<string>();
            foreach (string file in Directory.EnumerateFiles(RepoRoot.Combine("Assets/Scripts"), "*.cs", SearchOption.AllDirectories))
                found.AddRange(AudioApiIn(File.ReadAllText(file)).Distinct().Select(name => Path.GetFileName(file) + ": " + name));

            Assert.IsEmpty(found, "BitSorter has no sound since Part 16b:\n" + string.Join("\n", found));
        }

        /// <summary>
        /// Paired with the three above, each of which asserts an absence: the readers they use find sound where there is
        /// some, in the shapes Unity writes and a script would use, and pass over a comment that names it.
        /// </summary>
        [Test]
        public void TheReaders_FindSoundWhereThereIsSome()
        {
            const string camera =
                "--- !u!1 &10\nGameObject:\n  m_Name: Main Camera\n" +
                "--- !u!81 &11\nAudioListener:\n  m_Enabled: 1\n" +
                "--- !u!82 &12 stripped\nAudioSource:\n  m_Enabled: 1\n" +
                "--- !u!20 &13\nCamera:\n  m_Enabled: 1\n";
            CollectionAssert.AreEqual(new[] { "AudioListener", "AudioSource" }, AudioObjectsIn(camera).ToArray());
            CollectionAssert.AreEqual(new[] { "AudioListener", "AudioSource" }, AudioObjectsIn(camera.Replace("\n", "\r\n")).ToArray());

            const string script =
                "// an AudioSource was here\n/* and an AudioClip */\n" +
                "var source = gameObject.AddComponent<AudioSource>();\nusing UnityEngine.Audio;\n";
            CollectionAssert.AreEqual(new[] { "AudioSource", "UnityEngine.Audio" }, AudioApiIn(script).ToArray());

            StringAssert.EndsWith("HalfAdderDemo.unity", BuildScenes().First(), "the build list's reader reads it");
        }
    }
}
