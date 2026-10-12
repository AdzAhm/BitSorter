using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace BitSorter.Ci
{
    /// <summary>What a finished process said, and whether it finished at all.</summary>
    public readonly struct ProcessResult
    {
        public readonly int ExitCode;
        public readonly string StandardOutput;
        public readonly string StandardError;
        public readonly bool TimedOut;

        public ProcessResult(int exitCode, string standardOutput, string standardError, bool timedOut)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput;
            StandardError = standardError;
            TimedOut = timedOut;
        }

        public bool Succeeded => !TimedOut && ExitCode == 0;
    }

    /// <summary>
    /// Runs a command line tool -- git, iverilog, vvp -- with its input closed and a time limit.
    /// </summary>
    /// <remarks>
    /// **Input is always closed at once.** <c>iverilog -V</c> with no source files sits waiting on
    /// standard input, and a test that inherits the console's input would hang there forever: the
    /// first thing found when Icarus was installed (2026-10-11). Closing it makes every such tool see
    /// end of file immediately.
    ///
    /// **Every run has a limit, and running past it kills the whole process tree** and reports
    /// <see cref="ProcessResult.TimedOut"/> rather than throwing, so the test that asked can say what
    /// it was doing when time ran out.
    ///
    /// Arguments go through <see cref="ProcessStartInfo.ArgumentList"/>, never a joined string, so a
    /// path with a space in it -- this repository lives under "Unity projects2" -- arrives whole.
    /// </remarks>
    public static class ProcessRunner
    {
        public static ProcessResult Run(
            string fileName, IEnumerable<string> arguments, string workingDirectory, TimeSpan timeout)
        {
            var info = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            foreach (string argument in arguments)
                info.ArgumentList.Add(argument);

            using var process = new Process { StartInfo = info };
            var output = new StringBuilder();
            var error = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (error) error.AppendLine(e.Data); };

            process.Start();
            process.StandardInput.Close();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            bool finished = process.WaitForExit((int)timeout.TotalMilliseconds);

            if (!finished)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* it finished between the two calls */ }

                process.WaitForExit();
                return new ProcessResult(-1, output.ToString(), error.ToString(), timedOut: true);
            }

            // The parameterless wait drains the asynchronous readers, which the timed one does not.
            process.WaitForExit();
            return new ProcessResult(process.ExitCode, output.ToString(), error.ToString(), timedOut: false);
        }
    }
}
