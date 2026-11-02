using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.ViewModel;
using NUnit.Framework;

namespace BitSorter.Ci.Oracle
{
    /// <summary>
    /// COMPARE WITH MODELSIM held to ModelSim itself (Part 15i-5): every sample on the start page exported as EXPORT PROJECT
    /// writes it, run under ModelSim with the run.do's own dump, and BitSorter's run held to that dump by the comparison the
    /// workbench uses -- agreeing on every signal, by the names the two elaborate the testbench with. Skipped where ModelSim
    /// is not installed, as on CI.
    /// </summary>
    public class ModelSimCompareOracleTests
    {
        private static IEnumerable<string> Samples() => WorkbenchSamples.All.Select(s => s.Name);

        [TestCaseSource(nameof(Samples))]
        public void ASample_RunUnderModelSim_AgreesWithItsRunHere(string name)
        {
            ModelSim modelSim = ModelSim.Require();
            Project project = WorkbenchSamples.All.Single(s => s.Name == name).Build();
            double now = 0;
            var session = new WorkbenchSession(project, () => now += 0.01);
            session.Finish();

            CompiledProject compiled = BoardCompiler.Compile(project);
            Assert.IsTrue(compiled.Succeeded, string.Join("\n", compiled.Diagnostics));
            string folder = Path.Combine(Path.GetTempPath(), "bitsorter-compare", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var sources = new List<string>();
                foreach (PrintedSource source in BoardPrinter.Print(compiled))
                {
                    string path = Path.Combine(folder, source.Path);
                    File.WriteAllText(path, source.Text);
                    sources.Add(path);
                }
                var data = new List<string>();
                foreach (ProjectFile file in project.Files)
                {
                    string path = Path.Combine(folder, file.Name);
                    File.WriteAllText(path, file.Text ?? "");
                    if (!file.Name.EndsWith(".sv", StringComparison.OrdinalIgnoreCase) && !file.Name.EndsWith(".svh", StringComparison.OrdinalIgnoreCase))
                        data.Add(path);
                }

                using ModelSimRun run = modelSim.Simulate(sources, compiled.Top, new[] { folder }, TimeSpan.FromMinutes(3), data);
                Assert.IsTrue(run.Succeeded, run.Explain());
                Dictionary<string, VcdTrace> theirs;
                using (var vcd = new StreamReader(Path.Combine(run.Folder, "oracle.vcd")))
                    theirs = VcdReader.Read(vcd);

                WorkbenchModel model = session.Run.Model;
                RunComparison comparison = RunComparison.Of(model.Session.Design, model.Session.History, model.End, theirs);
                TestContext.WriteLine($"{name}: {comparison.Compared} signals compared to t = {comparison.Until}, {comparison.Missing.Count} missing, differing at the start: {string.Join(", ", comparison.DifferAtStart)}");
                Assert.IsTrue(comparison.Agree, $"{comparison.Signal} at {comparison.Time}: {comparison.Ours} here, {comparison.Theirs} in ModelSim");
                Assert.Greater(comparison.Compared, 2);
                CollectionAssert.IsEmpty(comparison.Missing, "every signal of the run in ModelSim's dump, by the same name");
            }
            finally
            {
                try { Directory.Delete(folder, recursive: true); }
                catch (IOException) { /* cleared with the temp folder */ }
            }
        }
    }
}
