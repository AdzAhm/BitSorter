using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BitSorter.Ci.Tests
{
    /// <summary>
    /// What a return visit to the browser build downloads (5.1, "a faster first launch"). Unity's loader keeps only the data file
    /// in IndexedDB and fetches the rest with "no-store", so every launch downloaded the 7.5 MB wasm again; the page's template
    /// asks it to keep the code and the framework too, revalidated against the host, which Pages answers with a 304 while a file
    /// is the same. Read as text, since nothing but a browser runs the page (<c>Docs/distribution.md</c>, "A faster launch").
    /// </summary>
    public class BrowserLaunchTests
    {
        private static string Template() => File.ReadAllText(RepoRoot.Combine("Assets/WebGLTemplates/BitSorter/index.html"));

        private static Match Rule(string page) => Regex.Match(page,
            @"config\.cacheControl\s*=\s*function\s*\(\s*url\s*\)\s*\{\s*return\s+/(?<pattern>[^/\n]+)/\.test\(url\)\s*\?\s*""must-revalidate""\s*:\s*""no-store""\s*;\s*\}\s*;");

        [Test]
        public void TheTemplate_KeepsTheWasmTheFrameworkAndTheData_RevalidatedOnTheNextVisit()
        {
            Match rule = Rule(Template());
            Assert.IsTrue(rule.Success, "the template sets no config.cacheControl that revalidates what it keeps: every launch "
                + "downloads the wasm again");

            var kept = new Regex(rule.Groups["pattern"].Value);
            foreach (string file in new[] { "Build/WebGL.wasm.unityweb", "Build/WebGL.framework.js.unityweb", "Build/WebGL.data.unityweb",
                         "Build/WebGL.wasm", "Build/WebGL.framework.js", "Build/WebGL.data" })
                Assert.IsTrue(kept.IsMatch(file), file + " would be fetched afresh on every launch");
            Assert.IsFalse(kept.IsMatch("StreamingAssets/UnityServicesProjectConfiguration.json"),
                "a file the loader need not keep is kept");
        }

        /// <summary>The rule is set before the loader reads the config, inside the script that builds it, not after the instance is made.</summary>
        [Test]
        public void TheRule_IsSetBeforeTheLoaderIsAskedForTheInstance()
        {
            string page = Template();
            int rule = Rule(page).Index, create = page.IndexOf("createUnityInstance(canvas, config", System.StringComparison.Ordinal);
            Assert.Greater(create, 0, "sanity: the page creates the instance from config");
            Assert.Less(rule, create, "config.cacheControl is set after the loader has read the config");
        }
    }
}
