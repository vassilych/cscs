using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSCSMath;
using SplitAndMerge;

namespace CscsSandbox;

/// <summary>
/// Runs ONE untrusted CSCS script, prints ONE JSON line, and exits.
///
///   stdin   {"script": "...", "mode": "run", "timeLimitMs": 5000, "maxOutputChars": 16000}
///   stdout  {"ok": true, "output": "...", "result": "...", "error": null, "timedOut": false, ...}
///
/// mode "explain" also reports what the precompiler makes of each cfunction -- the generated C#,
/// whether it compiles, or why it would fall back -- with PrecompileExplainer on, so that C# is
/// compiled in memory only and never loaded; the functions themselves run interpreted.
///
/// Defence in depth, innermost first:
///   1. InterpreterSecurity.AllowDotNet = false -- no .NET type by name, no reflection.
///   2. A function allowlist (Allowlist.cs) applied after everything has registered.
///   3. A watchdog in this process: wall clock and managed memory, answered with partial output.
///   4. The server that starts this process: a hard kill, a heap limit through the runtime's
///      environment, an empty working directory, no inherited environment, a concurrency cap.
/// A script can still crash this process -- a stack overflow cannot be caught in .NET -- which is
/// exactly why it runs in a process of its own; the server reports that as a crash.
/// </summary>
public static class Program
{
    public const int ExitOk = 0;
    public const int ExitScriptError = 1;
    public const int ExitBadRequest = 2;
    public const int ExitTimeout = 3;
    public const int ExitMemory = 4;

    // Limits a request may lower but never raise.
    const int MaxTimeLimitMs = 10_000;
    const int DefaultTimeLimitMs = 5_000;
    const int MaxOutputChars = 64_000;
    const int DefaultOutputChars = 16_000;
    const int MaxScriptChars = 20_000;
    // Generated C# returned per function; a long body's code is cut, not the report.
    const int MaxCSharpChars = 24_000;
    const long MaxManagedBytes = 256L * 1024 * 1024;
    // Runaway recursion must end as an error, not a stack overflow that takes the process down.
    // The depth is kept low because an error thrown that deep is slow to unwind -- every CSCS level
    // catches and rethrows, and the cost grows faster than the depth (measured, uncaught: 0.1 s at
    // 500 levels, 0.5 s at 2000, 1 s at 3000, well past the time limit at 10000). A simple call
    // takes ~3 KB of native stack, so 64 MB leaves ~32 KB per level; reserved, not committed.
    const int MaxCallDepth = 2_000;
    const int ScriptStackBytes = 64 * 1024 * 1024;

    static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    static readonly object s_lock = new();
    static readonly StringBuilder s_output = new();
    static bool s_truncated;
    static int s_outputLimit = DefaultOutputChars;
    static int s_finished; // 0 until the one result line has been written
    static StreamWriter s_stdout = null!;
    // When set (--io-dir DIR), the request comes from DIR/request.json and the one result line goes
    // to DIR/response.json, not through stdin/stdout. That is what the Windows AppContainer launcher
    // uses: it can grant the container access to one directory far more simply than it can hand
    // inheritable pipe handles into the container, and no console is involved.
    static string? s_ioDir;

    public static int Main(string[] args)
    {
        // Only the result line may reach stdout. The interpreter prints warnings and exception
        // traces to Console on its own, and one stray line would break the JSON the server reads.
        s_stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);

        s_ioDir = ArgValue(args, "--io-dir");

        InterpreterSecurity.AllowDotNet = false;
        InterpreterSecurity.MaxCallDepth = MaxCallDepth;

        if (args.Contains("--list-functions"))
        {
            return ListFunctions();
        }

        SandboxRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<SandboxRequest>(ReadRequest(MaxScriptChars * 4), s_json);
        }
        catch (Exception e)
        {
            return Finish(new SandboxResult { Error = "Bad request: " + e.Message }, ExitBadRequest);
        }
        if (request == null || string.IsNullOrWhiteSpace(request.Script))
        {
            return Finish(new SandboxResult { Error = "Bad request: no script." }, ExitBadRequest);
        }
        if (request.Script.Length > MaxScriptChars)
        {
            return Finish(new SandboxResult { Error = $"Script too long ({request.Script.Length} characters, limit {MaxScriptChars})." }, ExitBadRequest);
        }

        var explain = string.Equals(request.Mode, "explain", StringComparison.OrdinalIgnoreCase);
        if (explain)
        {
            PrecompileExplainer.Enabled = true;
            Allowlist.ExplainMode = true;
        }

        var timeLimit = Math.Clamp(request.TimeLimitMs ?? DefaultTimeLimitMs, 100, MaxTimeLimitMs);
        s_outputLimit = Math.Clamp(request.MaxOutputChars ?? DefaultOutputChars, 100, MaxOutputChars);

        // On a thread of its own, for the stack: the main thread gets 1 MB on Windows, which a
        // few hundred nested CSCS calls exhaust long before MaxCallDepth could stop them.
        var exitCode = ExitScriptError;
        var script = request.Script;
        var runner = new Thread(() => exitCode = RunScript(script, timeLimit), ScriptStackBytes)
        {
            Name = "cscs-sandbox-script",
        };
        runner.Start();
        runner.Join();
        return exitCode;
    }

    static int RunScript(string script, int timeLimitMs)
    {
        // The script's time starts once the interpreter exists. Building it is this program's own
        // fixed work, and on a cold start -- a freshly deployed binary, a busy server -- it took
        // seconds once in testing, which counted against the script and timed out a 30 ms run.
        // The server's hard kill still bounds the whole process, start-up included.
        var clock = new Stopwatch();
        try
        {
            var interpreter = CreateInterpreter();
            interpreter.OnOutput += (_, e) => AppendOutput(e.Output);

            clock.Start();
            StartWatchdog(clock, timeLimitMs);
            Variable? value = interpreter.Process(script);

            var result = Snapshot(clock);
            result.Ok = true;
            var text = value?.AsString();
            if (!string.IsNullOrEmpty(text) && value!.Type != Variable.VarType.NONE)
            {
                result.Result = text.Length > 2_000 ? text[..2_000] + "…" : text;
            }
            return Finish(result, ExitOk);
        }
        catch (Exception e) when (IsOutOfMemory(e))
        {
            var result = Snapshot(clock);
            result.Error = $"Memory limit reached ({MaxManagedBytes / (1024 * 1024)} MB).";
            return Finish(result, ExitMemory);
        }
        catch (Exception e)
        {
            var result = Snapshot(clock);
            result.Error = Describe(e);
            return Finish(result, ExitScriptError);
        }
    }

    /// <summary>
    /// The interpreter wraps what a statement throws in its own ParsingException, so an allocation
    /// that fails deep inside a script arrives as that, with the OutOfMemoryException inside it. A
    /// doubling string can also jump past the watchdog's sampled check straight into the runtime's
    /// heap limit, which is how this shows up at all.
    /// </summary>
    static bool IsOutOfMemory(Exception? e)
    {
        for (; e != null; e = e.InnerException)
        {
            if (e is OutOfMemoryException || e.Message.Contains(nameof(OutOfMemoryException), StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>An interpreter with the Math module, then cut down to the allowlist.</summary>
    static Interpreter CreateInterpreter()
    {
        var interpreter = new Interpreter();
        new CscsMathModule().CreateInstance(interpreter);
        interpreter.RestrictFunctions(Allowlist.Keep);
        return interpreter;
    }

    static int ListFunctions()
    {
        var interpreter = new Interpreter();
        new CscsMathModule().CreateInstance(interpreter);
        var all = interpreter.FunctionNames.OrderBy(n => n, StringComparer.Ordinal).ToList();
        interpreter.RestrictFunctions(Allowlist.Keep);
        var kept = interpreter.FunctionNames.ToHashSet();
        WriteResult(JsonSerializer.Serialize(new
        {
            kept = all.Where(kept.Contains).ToList(),
            removed = all.Where(n => !kept.Contains(n)).ToList(),
        }, s_json));
        return ExitOk;
    }

    static void AppendOutput(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        lock (s_lock)
        {
            var room = s_outputLimit - s_output.Length;
            if (room <= 0)
            {
                s_truncated = true;
                return;
            }
            if (text.Length > room)
            {
                s_output.Append(text, 0, room);
                s_truncated = true;
                return;
            }
            s_output.Append(text);
        }
    }

    /// <summary>
    /// Watches the wall clock and the managed heap from its own thread. Over either limit it writes
    /// the result with whatever output the script has produced so far and ends the process -- a
    /// script in "while (1) {}" never returns to let the main thread do it.
    /// </summary>
    static void StartWatchdog(Stopwatch clock, int timeLimitMs)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(25);
                if (Volatile.Read(ref s_finished) != 0)
                {
                    return;
                }
                if (clock.ElapsedMilliseconds > timeLimitMs)
                {
                    var result = Snapshot(clock);
                    result.TimedOut = true;
                    result.Error = $"Time limit reached ({timeLimitMs} ms).";
                    Environment.Exit(Finish(result, ExitTimeout));
                }
                if (GC.GetTotalMemory(false) > MaxManagedBytes)
                {
                    var result = Snapshot(clock);
                    result.Error = $"Memory limit reached ({MaxManagedBytes / (1024 * 1024)} MB).";
                    Environment.Exit(Finish(result, ExitMemory));
                }
            }
        })
        {
            IsBackground = true,
            Name = "cscs-sandbox-watchdog",
        };
        thread.Start();
    }

    static SandboxResult Snapshot(Stopwatch clock)
    {
        lock (s_lock)
        {
            return new SandboxResult
            {
                Output = s_output.ToString(),
                OutputTruncated = s_truncated,
                ElapsedMs = clock.ElapsedMilliseconds,
                Precompile = PrecompileExplainer.Enabled ? PrecompileExplainer.Reports.Select(ToDto).ToList() : null,
            };
        }
    }

    /// <summary>Writes the one result line. Only the first caller wins; the rest just get the code.</summary>
    static int Finish(SandboxResult result, int exitCode)
    {
        if (Interlocked.Exchange(ref s_finished, 1) != 0)
        {
            return exitCode;
        }
        lock (s_lock)
        {
            WriteResult(JsonSerializer.Serialize(result, s_json));
        }
        return exitCode;
    }

    /// <summary>Writes the one result line to response.json (file mode) or stdout (default).</summary>
    static void WriteResult(string json)
    {
        if (s_ioDir == null)
        {
            s_stdout.WriteLine(json);
            s_stdout.Flush();
            return;
        }
        // Write then rename, so the server never reads a half-written file: it waits for response.json
        // to appear, and the rename is atomic within the directory.
        var final = Path.Combine(s_ioDir, "response.json");
        var temp = final + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));
        File.Move(temp, final, overwrite: true);
    }

    static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    static PrecompileDto ToDto(PrecompileReport r) => new()
    {
        Function = r.FunctionName,
        ReturnType = r.ReturnType,
        Arguments = r.Arguments,
        Translated = r.Translated,
        Compiles = r.Compiles,
        Reason = r.Reason is { Length: > 4_000 } reason ? reason[..4_000] + "…" : r.Reason,
        GeneratedCode = r.CSharpCode is { Length: > MaxCSharpChars } code ? code[..MaxCSharpChars] + "\n// … (cut)" : r.CSharpCode,
    };

    static string Describe(Exception e)
    {
        // ParsingException already carries the line and the script excerpt in its message.
        var message = e.Message;
        if (string.IsNullOrWhiteSpace(message) && e.InnerException != null)
        {
            message = e.InnerException.Message;
        }
        return message.Length > 4_000 ? message[..4_000] + "…" : message;
    }

    static string ReadRequest(int maxChars)
    {
        if (s_ioDir != null)
        {
            var path = Path.Combine(s_ioDir, "request.json");
            var text = File.ReadAllText(path, Encoding.UTF8);
            if (text.Length > maxChars)
            {
                throw new InvalidDataException($"request larger than {maxChars} characters");
            }
            return text;
        }
        using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
        var buffer = new char[maxChars + 1];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = reader.Read(buffer, total, buffer.Length - total)) > 0)
        {
            total += read;
        }
        if (total > maxChars)
        {
            throw new InvalidDataException($"request larger than {maxChars} characters");
        }
        return new string(buffer, 0, total);
    }
}

public sealed class SandboxRequest
{
    public string? Script { get; set; }
    /// <summary>"run" (the default) or "explain".</summary>
    public string? Mode { get; set; }
    public int? TimeLimitMs { get; set; }
    public int? MaxOutputChars { get; set; }
}

public sealed class SandboxResult
{
    public bool Ok { get; set; }
    public string Output { get; set; } = "";
    public bool OutputTruncated { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
    public bool TimedOut { get; set; }
    public long ElapsedMs { get; set; }
    /// <summary>Explain mode only: one entry per cfunction the script defined.</summary>
    public List<PrecompileDto>? Precompile { get; set; }
}

public sealed class PrecompileDto
{
    public string Function { get; set; } = "";
    public string ReturnType { get; set; } = "";
    public string[] Arguments { get; set; } = [];
    public bool Translated { get; set; }
    public bool Compiles { get; set; }
    public string? Reason { get; set; }
    /// <summary>The C# the translator produced, cut at MaxCSharpChars.</summary>
    public string? GeneratedCode { get; set; }
}
