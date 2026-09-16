using System.ComponentModel;
using System.Reflection;
using System.Text;
using ModelContextProtocol.Server;

namespace CscsMcp.Tools;

/// <summary>
/// The CSCS Playground tools. run_cscs executes a script in the sandbox; cscs_guide teaches the
/// calling assistant the language first, since no model has seen much CSCS.
///
/// A script's output is returned to the calling assistant as data. It is whatever the script
/// printed -- which the assistant, or whoever wrote the script, controls -- so it is fenced and
/// labelled rather than mixed into the tool's own words.
/// </summary>
[McpServerToolType]
public sealed class CscsTools
{
    static readonly Lazy<string> s_guide = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CscsMcp.LanguageGuide.md")
            ?? throw new InvalidOperationException("LanguageGuide.md is not embedded");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    static IReadOnlyList<string>? s_functions;
    static readonly SemaphoreSlim s_functionsLock = new(1, 1);

    readonly SandboxRunner _runner;
    readonly IHttpContextAccessor _http;

    public CscsTools(SandboxRunner runner, IHttpContextAccessor http)
    {
        _runner = runner;
        _http = http;
    }

    [McpServerTool(Name = "run_cscs", Title = "Run a CSCS script", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Runs a CSCS script in an isolated sandbox and returns everything it printed, the value of its last statement, and any error with the line it happened on. CSCS is a small C-like scripting language built on the Split-and-Merge parsing algorithm; call cscs_guide first if you have not written CSCS in this conversation. The sandbox has no file, network, process or .NET access. Limits: 5 seconds, 16,000 characters of output, 20,000 characters of script, 2,000 nested calls.")]
    public async Task<string> RunCscs(
        [Description("The CSCS script to run. Use print(...) to show values; the value of the last statement is returned as well.")] string script,
        CancellationToken cancellationToken)
    {
        var outcome = await _runner.RunAsync(script ?? "", ClientKey(), cancellationToken);
        return Format(outcome, _runner.Options);
    }

    [McpServerTool(Name = "explain_cscs", Title = "Explain CSCS precompilation", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Shows what the CSCS precompiler does with each cfunction in a script: the C# it generates, whether that C# compiles, or exactly why the function would fall back to the interpreter. The script is then run as well, so you also see its output. In this sandbox the generated C# is compiled but never loaded or executed, and every function runs interpreted -- the full CSCS runtime would run the compiled C# instead. A cfunction declares a return type and typed parameters, e.g. cfunction double area(double r) { return 3.14159 * r * r; }. Limits as run_cscs, with 10 seconds.")]
    public async Task<string> ExplainCscs(
        [Description("A CSCS script that defines one or more cfunctions and usually calls them.")] string script,
        CancellationToken cancellationToken)
    {
        var outcome = await _runner.RunAsync(script ?? "", ClientKey(), cancellationToken, explain: true);
        return FormatExplain(outcome, _runner.Options);
    }

    [McpServerTool(Name = "cscs_guide", Title = "CSCS language guide", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns a short guide to the CSCS scripting language -- syntax, collections, strings, classes, errors -- with examples that all run in this sandbox, plus the exact list of functions the sandbox allows. Read it before writing CSCS for run_cscs.")]
    public async Task<string> CscsGuide(CancellationToken cancellationToken)
    {
        var sb = new StringBuilder(s_guide.Value);
        var functions = await AllowedFunctionsAsync(cancellationToken);
        sb.AppendLine();
        sb.AppendLine("## Functions available in this sandbox");
        sb.AppendLine();
        if (functions.Count == 0)
        {
            sb.AppendLine("(The list could not be read from the sandbox right now.)");
        }
        else
        {
            var math = functions.Where(f => f.StartsWith("math.", StringComparison.Ordinal)).ToList();
            var rest = functions.Except(math).ToList();
            sb.AppendLine("Names are case-insensitive. Language and built-in functions:");
            sb.AppendLine();
            sb.AppendLine(string.Join(", ", rest));
            sb.AppendLine();
            sb.AppendLine("Math (write them as Math.Sqrt, Math.PI, ...):");
            sb.AppendLine();
            sb.AppendLine(string.Join(", ", math.Select(m => "Math." + m.Substring("math.".Length))));
        }
        return sb.ToString();
    }

    async Task<IReadOnlyList<string>> AllowedFunctionsAsync(CancellationToken cancellationToken)
    {
        if (s_functions != null)
        {
            return s_functions;
        }
        await s_functionsLock.WaitAsync(cancellationToken);
        try
        {
            if (s_functions == null)
            {
                try
                {
                    s_functions = await _runner.ListFunctionsAsync(cancellationToken);
                }
                catch
                {
                    return Array.Empty<string>(); // retried on the next call
                }
            }
            return s_functions;
        }
        finally
        {
            s_functionsLock.Release();
        }
    }

    static string Format(RunOutcome outcome, SandboxOptions options)
    {
        var sb = new StringBuilder();
        if (outcome.Refused)
        {
            sb.AppendLine("Not run: " + outcome.Error);
            return sb.ToString();
        }

        sb.AppendLine(outcome.Ok ? "Status: completed" : outcome.TimedOut ? "Status: stopped (time limit)" : "Status: error");
        sb.AppendLine();
        if (outcome.Output.Length > 0)
        {
            sb.AppendLine("Output printed by the script:");
            sb.AppendLine(Fence(outcome.Output));
            if (outcome.OutputTruncated)
            {
                sb.AppendLine($"(Output was cut off at {options.MaxOutputChars} characters.)");
            }
        }
        else
        {
            sb.AppendLine("Output printed by the script: (none)");
        }
        if (!string.IsNullOrEmpty(outcome.Result))
        {
            sb.AppendLine();
            sb.AppendLine("Value of the last statement:");
            sb.AppendLine(Fence(outcome.Result));
        }
        if (!string.IsNullOrEmpty(outcome.Error))
        {
            sb.AppendLine();
            sb.AppendLine("Error:");
            sb.AppendLine(Fence(outcome.Error));
        }
        sb.AppendLine();
        sb.AppendLine($"Time: {outcome.ElapsedMs} ms");
        return sb.ToString();
    }

    static string FormatExplain(RunOutcome outcome, SandboxOptions options)
    {
        if (outcome.Refused)
        {
            return "Not run: " + outcome.Error + "\n";
        }

        var sb = new StringBuilder();
        var entries = outcome.Precompile ?? [];
        if (entries.Count == 0)
        {
            sb.AppendLine("Precompilation: the script defines no cfunction, so there was nothing to precompile.");
            sb.AppendLine("Declare one with a return type and typed parameters, e.g. `cfunction double area(double r) { return 3.14159 * r * r; }`.");
        }
        else
        {
            var compiled = entries.Count(e => e.Compiles);
            sb.AppendLine($"Precompilation: {compiled} of {entries.Count} cfunction(s) compile to C#; " +
                          $"{entries.Count - compiled} would fall back to the interpreter.");
            sb.AppendLine("(Here the C# is compiled but never loaded or run, and every function runs interpreted.)");
        }

        foreach (var e in entries)
        {
            sb.AppendLine();
            sb.AppendLine($"### cfunction {e.ReturnType} {e.Function}({string.Join(", ", e.Arguments)})");
            if (e.Compiles)
            {
                sb.AppendLine("Result: compiles to C#. The full CSCS runtime runs this function as the code below.");
            }
            else if (e.Translated)
            {
                sb.AppendLine("Result: falls back to the interpreter -- the translator produced C#, but it does not compile:");
                sb.AppendLine(Fence(e.Reason ?? "(no compiler message)"));
            }
            else
            {
                sb.AppendLine("Result: falls back to the interpreter -- the translator refused this body:");
                sb.AppendLine(Fence(e.Reason ?? "(no reason given)"));
            }
            if (!string.IsNullOrEmpty(e.GeneratedCode))
            {
                sb.AppendLine("Generated C#:");
                sb.AppendLine(Fence(e.GeneratedCode, "csharp"));
            }
        }

        sb.AppendLine();
        sb.AppendLine("## The run");
        sb.AppendLine();
        sb.Append(Format(outcome, options));
        return sb.ToString();
    }

    /// <summary>A fence longer than any backtick run in the text, so printed text cannot close it.</summary>
    static string Fence(string text, string language = "text")
    {
        var longest = 0;
        var run = 0;
        foreach (var ch in text)
        {
            run = ch == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }
        var fence = new string('`', Math.Max(3, longest + 1));
        return fence + language + "\n" + text.TrimEnd('\n', '\r') + "\n" + fence;
    }

    string ClientKey()
    {
        var context = _http.HttpContext;
        return context == null ? "unknown" : ClientAddress.Of(context, _runner.Options.TrustForwardedHeaders);
    }
}
