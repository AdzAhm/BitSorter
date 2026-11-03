using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace BitSorter.Ci.Tests
{
    /// <summary>
    /// <c>Packages/manifest.json</c> and <c>Packages/packages-lock.json</c>, which nothing else under dotnet reads. The
    /// Package Manager writes the lock as what the manifest reaches -- every package it names at depth 0, and each
    /// other one at its shortest distance from them -- and every lock it wrote here obeys that (the scaffold's, the AI
    /// Assistant's and analytics'), so a lock edited by hand, as Part 16b's were with no editor to resolve them, is held
    /// to it too. And a few packages stay on purpose though no script of the app names them; a later tidy would find
    /// them unused by the same search that took the rest out (see the 16b bullet in <c>Docs/architecture-5.md</c>).
    /// </summary>
    public class PackageLockTests
    {
        private static JsonElement Dependencies(string file)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(RepoRoot.Combine(file)));
            return document.RootElement.GetProperty("dependencies").Clone();
        }

        /// <summary>
        /// What is wrong with a lock for a manifest: a package the manifest names that the lock lacks, or holds at
        /// another version or below the top; a dependency the lock does not resolve; a package nothing reaches; one at
        /// a depth other than its shortest distance from the manifest.
        /// </summary>
        internal static List<string> LockProblems(JsonElement manifest, JsonElement locked)
        {
            var problems = new List<string>();
            var entries = locked.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
            var depth = new Dictionary<string, int>();
            var queue = new Queue<string>();

            foreach (JsonProperty named in manifest.EnumerateObject())
            {
                depth[named.Name] = 0;
                queue.Enqueue(named.Name);

                if (!entries.TryGetValue(named.Name, out JsonElement entry))
                    problems.Add(named.Name + " is in the manifest but not the lock");
                else if (entry.GetProperty("version").GetString() != named.Value.GetString())
                    problems.Add(named.Name + " is " + named.Value.GetString() + " in the manifest but "
                        + entry.GetProperty("version").GetString() + " in the lock");
            }

            while (queue.Count > 0)
            {
                string name = queue.Dequeue();
                if (!entries.TryGetValue(name, out JsonElement entry) || !entry.TryGetProperty("dependencies", out JsonElement needs))
                    continue;

                foreach (JsonProperty need in needs.EnumerateObject())
                {
                    if (!entries.ContainsKey(need.Name))
                        problems.Add(name + " depends on " + need.Name + ", which the lock does not resolve");

                    if (!depth.ContainsKey(need.Name))
                    {
                        depth[need.Name] = depth[name] + 1;
                        queue.Enqueue(need.Name);
                    }
                }
            }

            foreach (KeyValuePair<string, JsonElement> entry in entries)
            {
                int written = entry.Value.GetProperty("depth").GetInt32();

                if (!depth.TryGetValue(entry.Key, out int shortest))
                    problems.Add(entry.Key + " is in the lock but nothing in the manifest reaches it");
                else if (written != shortest)
                    problems.Add(entry.Key + " is at depth " + written + " in the lock, but " + shortest + " from the manifest");
            }

            return problems;
        }

        [Test]
        public void TheLock_IsWhatTheManifestReaches()
        {
            List<string> problems = LockProblems(Dependencies("Packages/manifest.json"), Dependencies("Packages/packages-lock.json"));

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        /// <summary>Paired with the test above, which asserts that none of these is there: each is found where it is.</summary>
        [Test]
        public void ALockOutOfStep_IsFoundOut()
        {
            using JsonDocument manifest = JsonDocument.Parse(@"{ ""a"": ""1.0.0"", ""b"": ""2.0.0"", ""c"": ""1.0.0"" }");
            using JsonDocument locked = JsonDocument.Parse(@"{
                ""a"": { ""version"": ""1.0.0"", ""depth"": 0, ""dependencies"": { ""d"": ""1.0.0"", ""gone"": ""1.0.0"" } },
                ""b"": { ""version"": ""2.1.0"", ""depth"": 0, ""dependencies"": {} },
                ""d"": { ""version"": ""1.0.0"", ""depth"": 2, ""dependencies"": {} },
                ""orphan"": { ""version"": ""1.0.0"", ""depth"": 1, ""dependencies"": {} }
            }");

            List<string> problems = LockProblems(manifest.RootElement, locked.RootElement);

            CollectionAssert.AreEquivalent(new[]
            {
                "c is in the manifest but not the lock",
                "b is 2.0.0 in the manifest but 2.1.0 in the lock",
                "a depends on gone, which the lock does not resolve",
                "d is at depth 2 in the lock, but 1 from the manifest",
                "orphan is in the lock but nothing in the manifest reaches it",
            }, problems);
        }

        /// <summary>
        /// Kept though nothing in <c>Assets</c> names them. Each reason is the 16b bullet's, where the rest of what
        /// stays is written down.
        /// </summary>
        [TestCase("com.unity.ai.assistant", TestName = "AiAssistant_Stays_ItRunsTheMcpBridgeTheSessionsDriveUnityThrough")]
        [TestCase("com.unity.modules.vectorgraphics", TestName = "VectorGraphics_Stays_ItImportsTheSvgIconsOfShaderGraphAndAiAssistant")]
        [TestCase("com.unity.modules.unityanalytics", TestName = "UnityAnalytics_Stays_WhatIsCollectedIsAskedFirst")]
        public void APackageKeptOnPurpose_IsInTheManifest(string package)
        {
            JsonElement manifest = Dependencies("Packages/manifest.json");

            Assert.IsTrue(manifest.TryGetProperty(package, out _), package + " has been taken out of Packages/manifest.json");
        }
    }
}
