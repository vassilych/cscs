using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitAndMerge;

namespace cscs.Tests.IntegrationTests.Precompiler
{
    /// <summary>
    /// The generated differential audits in cscs.Tests/Audits: each declares a shape twice -- as a
    /// cfunction and as a plain function -- calls both over many kinds of value, and prints a line
    /// starting "DIFF" for every value on which the two answer differently, then a "DONE" line.
    /// A difference is a bug: compiled code must answer what the interpreter answers, or fall back.
    ///
    /// Each audit runs twice, through Interpreter.Process and through Interpreter.ProcessAsync, so
    /// the interpreter's async paths are held to the same answers as its synchronous ones.
    ///
    /// They take several minutes together, so they run only when CSCS_RUN_AUDITS is set:
    ///   CSCS_RUN_AUDITS=1 dotnet test cscs.Tests/cscs.Tests.csproj --filter TestCategory=Audit
    /// </summary>
    [TestClass]
    public class AuditFixture
    {
        static readonly string[] Audits =
        {
            "audit5", "audit6", "audit7", "audit8", "audit9", "audit10", "audit12", "audit13",
            "audit14", "audit15", "audit16", "audit17", "audit18", "audit19", "audit20", "audit21",
            "audit22", "audit23", "audit24", "audit25", "semantics",
        };

        public static IEnumerable<object[]> AuditNames => Audits.Select(audit => new object[] { audit });

        [TestMethod, TestCategory("Audit")]
        [DynamicData(nameof(AuditNames))]
        public void Compiled_Agrees_With_Interpreted(string audit)
        {
            Check(audit, false);
        }

        [TestMethod, TestCategory("Audit")]
        [DynamicData(nameof(AuditNames))]
        public void Compiled_Agrees_With_Interpreted_Async(string audit)
        {
            Check(audit, true);
        }

        static void Check(string audit, bool async)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CSCS_RUN_AUDITS")))
            {
                Assert.Inconclusive("Set CSCS_RUN_AUDITS=1 to run the audits (several minutes).");
            }
            var path = Path.Combine(AppContext.BaseDirectory, "Audits", audit + ".cscs");
            var output = new StringBuilder();
            var interpreter = new Interpreter();
            interpreter.InitStandalone();
            new CSCSMath.CscsMathModule().CreateInstance(interpreter);
            interpreter.OnOutput += (sender, e) => output.Append(e.Output);

            var script = File.ReadAllText(path);
            if (async)
            {
                interpreter.ProcessAsync(script, path, true).GetAwaiter().GetResult();
            }
            else
            {
                interpreter.Process(script, path, true);
            }

            var lines = output.ToString().Split('\n').Select(line => line.Trim()).ToList();
            Assert.IsTrue(lines.Any(line => line.Contains("DONE")), audit + " did not run to the end:\n" + output);
            var diffs = lines.Where(line => line.StartsWith("DIFF")).ToList();
            Assert.AreEqual(0, diffs.Count, audit + (async ? " (async)" : "") + ":\n" +
                string.Join("\n", diffs.Take(20)));
        }
    }
}
