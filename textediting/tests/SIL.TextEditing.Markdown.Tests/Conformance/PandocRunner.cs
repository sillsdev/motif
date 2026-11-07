using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Conformance
{
    /// <summary>Runs Pandoc, the other reader every FieldWorks Markdown file must mean the same to (ADR 0028, spec 6.4). Pandoc is on the PATH in CI (the workflow
    /// installs a pinned version) and on a developer's machine when they have it. A test that needs it is skipped when it is missing, except where the
    /// environment says it runs in CI (<c>CI</c> or <c>GITHUB_ACTIONS</c>): there a missing Pandoc fails the test, so a broken install cannot leave CI green.</summary>
    internal static class PandocRunner
    {
        /// <summary>The format the profile is read with: every other Pandoc extension stays at its default (spec 6.4).</summary>
        public const string Format = "markdown-smart-implicit_figures-implicit_header_references-task_lists";

        static string? _version;
        static bool _probed;

        public static bool InCi
            => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")) || string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

        /// <summary>The first line of <c>pandoc --version</c>, or null when Pandoc is not on the PATH.</summary>
        public static string? Version
        {
            get
            {
                if (_probed) return _version;
                _probed = true;
                try
                {
                    string output = Run("--version", string.Empty);
                    _version = output.Split('\n')[0].Trim();
                }
                catch (Exception) { _version = null; }
                return _version;
            }
        }

        public static bool Available => Version != null;

        /// <summary>Pandoc's reading of the text as JSON.</summary>
        public static string ToJson(string markdown)
        {
            return Run("-f " + Format + " -t json", markdown);
        }

        static string Run(string arguments, string input)
        {
            var info = new ProcessStartInfo("pandoc", arguments)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };
            using (var process = Process.Start(info)!)
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                var bytes = new UTF8Encoding(false).GetBytes(input);
                process.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                process.StandardInput.Close();
                if (!process.WaitForExit(30000))
                {
                    #if NET5_0_OR_GREATER
                    process.Kill(true);
#else
                    process.Kill();
#endif
                    string shown = input.Length > 200 ? input.Substring(0, 200) + "..." : input;
                    throw new TimeoutException("pandoc did not finish in 30 seconds on " + input.Length + " characters of input, which starts: " + shown);
                }
                Task.WaitAll(output, error);
                if (process.ExitCode != 0) throw new InvalidOperationException("pandoc failed: " + error.Result);
                return output.Result;
            }
        }
    }

    /// <summary>A theory that needs Pandoc: skipped when it is missing, unless the tests run in CI, where it fails instead.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PandocTheoryAttribute : TheoryAttribute
    {
        public PandocTheoryAttribute()
        {
            if (!PandocRunner.Available && !PandocRunner.InCi) Skip = "pandoc is not on the PATH (it is required in CI)";
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PandocFactAttribute : FactAttribute
    {
        public PandocFactAttribute()
        {
            if (!PandocRunner.Available && !PandocRunner.InCi) Skip = "pandoc is not on the PATH (it is required in CI)";
        }
    }
}
