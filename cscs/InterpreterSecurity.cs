using System;

namespace SplitAndMerge
{
    /// <summary>
    /// Process-wide switches for running scripts that are not trusted -- a public playground, say.
    ///
    /// The interpreter reaches .NET in two ways that no function allowlist can close, because
    /// both sit inside things a script needs: "new" looks a class name up among all loaded .NET
    /// types before it looks among CSCS classes, so "new System.Diagnostics.Process()" builds a
    /// real Process; and any Variable holding a .NET object -- a CSCS class instance included --
    /// answers "v.Name(...)" by calling that public .NET method through reflection. From
    /// "p.GetType()" onward that is the whole of reflection.
    ///
    /// AllowDotNet = false shuts both: .NET types are never found by name, and reflected property
    /// reads, writes and method calls return "not found" instead of running. CSCS classes, their
    /// fields and methods are unaffected -- they never go through this path.
    ///
    /// Default true, so every existing host behaves as before. A host sets it once, before the
    /// first script runs, in a process of its own.
    /// </summary>
    public static class InterpreterSecurity
    {
        public static bool AllowDotNet { get; set; } = true;

        /// <summary>
        /// The deepest a chain of CSCS function calls may go, or 0 for no limit (the default).
        /// Past it a call throws an ordinary, catchable error. Without a limit, runaway recursion
        /// ends in a .NET stack overflow, which no catch block can intercept: the whole process
        /// dies. Only meaningful together with a thread stack large enough to reach the limit --
        /// roughly 3 KB of native stack per simple CSCS call.
        /// </summary>
        public static int MaxCallDepth { get; set; } = 0;
    }

    /// <summary>What the precompiler made of one cfunction, in explain mode.</summary>
    public sealed class PrecompileReport
    {
        public string FunctionName { get; set; } = "";
        public string ReturnType { get; set; } = "";
        public string[] Arguments { get; set; } = new string[0];
        /// <summary>The translator produced C# for the body.</summary>
        public bool Translated { get; set; }
        /// <summary>Roslyn compiled that C# without errors. The result was never loaded.</summary>
        public bool Compiles { get; set; }
        /// <summary>Why it would fall back to the interpreter: a refusal or the compile errors.</summary>
        public string Reason { get; set; }
        public string CSharpCode { get; set; }
    }

    /// <summary>
    /// Explain mode for cfunction, for hosts that must not run code nobody has vetted.
    ///
    /// A cfunction normally becomes C# that is compiled, loaded into the process and run -- outside
    /// every protection the interpreter has, and the translator copies some tokens it does not
    /// recognise straight into that C#. With Enabled on, a cfunction definition is translated and
    /// compiled in memory only, never loaded; the result goes to Reports, and the function is
    /// registered as an ordinary interpreted function, exactly as a failed compile falls back.
    /// RoslynCompiler.Compile, the only path that loads compiled code, refuses outright meanwhile.
    ///
    /// Process-wide and default off. A host sets it once, before the first script runs.
    /// </summary>
    public static class PrecompileExplainer
    {
        static readonly object s_lock = new object();
        static readonly System.Collections.Generic.List<PrecompileReport> s_reports =
            new System.Collections.Generic.List<PrecompileReport>();

        public static bool Enabled { get; set; }

        public static System.Collections.Generic.List<PrecompileReport> Reports
        {
            get { lock (s_lock) { return new System.Collections.Generic.List<PrecompileReport>(s_reports); } }
        }

        internal static void Add(PrecompileReport report)
        {
            lock (s_lock) { s_reports.Add(report); }
        }
    }
}
