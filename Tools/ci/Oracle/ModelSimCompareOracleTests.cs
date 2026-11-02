using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.ViewModel;
using NUnit.Framework;

namespace BitSorter.Ci.Oracle
{
    /// <summary>
    /// COMPARE WITH MODELSIM held to ModelSim itself (Part 15i-5): every sample on the start page exported exactly as EXPORT
    /// PROJECT writes it, unzipped, its own run.do run under ModelSim as a student types <c>do run.do</c>, and BitSorter's
    /// run held to the modelsim.vcd it writes by the comparison the workbench uses -- agreeing on every signal, by the names
    /// the two elaborate the testbench with, the traffic light and the sequence detector after their start. Skipped where
    /// ModelSim is not installed, as on CI.
    /// </summary>
    public class ModelSimCompareOracleTests
    {
        private static IEnumerable<string> Samples() => WorkbenchSamples.All.Select(s => s.Name);

        [TestCaseSource(nameof(Samples))]
        public void ASample_ExportedAndRunUnderModelSim_AgreesWithItsRunHere(string name)
        {
            ModelSim modelSim = ModelSim.Require();
            Project project = WorkbenchSamples.All.Single(s => s.Name == name).Build();
            double now = 0;
            var session = new WorkbenchSession(project, () => now += 0.01);
            session.Finish();

            string folder = Path.Combine(Path.GetTempPath(), "bitsorter-compare", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                using (var zip = new ZipArchive(new MemoryStream(ProjectExport.Zip(project, new DateTime(2026, 11, 2))), ZipArchiveMode.Read))
                    zip.ExtractToDirectory(folder);
                string exported = Path.Combine(folder, ProjectExport.FolderOf(project));

                ProcessResult run = modelSim.RunDo(exported);
                Assert.IsTrue(run.Succeeded && !run.StandardOutput.Contains("# ** Error"), run.StandardOutput + run.StandardError);
                Dictionary<string, VcdTrace> theirs;
                using (var vcd = new StreamReader(Path.Combine(exported, ProjectExport.ModelSimDump)))
                    theirs = VcdReader.Read(vcd);

                WorkbenchModel model = session.Run.Model;
                RunComparison comparison = RunComparison.Of(model.Session.Design, model.Session.History, model.End, theirs);
                TestContext.WriteLine($"{name}: {comparison.Compared} signals compared to t = {comparison.Until}, {comparison.Missing.Count} missing, " +
                    $"differing at the start: {string.Join(", ", comparison.DifferAtStart)}");
                Assert.IsTrue(comparison.Agree, $"{comparison.Signal} at {comparison.Time}: {comparison.Ours} here, {comparison.Theirs} in ModelSim");
                Assert.Greater(comparison.Compared, 2);
                CollectionAssert.IsEmpty(comparison.Missing, "every signal of the run in ModelSim's dump, by the same name");
            }
            finally
            {
                try { Directory.Delete(folder, recursive: true); }
                catch (IOException) { /* cleared with the temp folder */ }
                catch (UnauthorizedAccessException) { /* ModelSim's library held a moment longer */ }
            }
        }
    }
}
