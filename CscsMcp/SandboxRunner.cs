using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;

namespace CscsMcp;

/// <summary>Limits and locations, from the "Cscs" configuration section.</summary>
public sealed class SandboxOptions
{
    /// <summary>The CscsSandbox executable (or its .dll, run with "dotnet").</summary>
    public string SandboxPath { get; set; } = "";
    public int MaxScriptChars { get; set; } = 20_000;
    public int TimeLimitMs { get; set; } = 5_000;
    /// <summary>explain_cscs compiles with Roslyn inside the worker, which costs 2-3 s cold.</summary>
    public int ExplainTimeLimitMs { get; set; } = 10_000;
    public int MaxOutputChars { get; set; } = 16_000;
    /// <summary>Runtime heap limit for the worker. Above the worker's own 256 MB check, so that one
    /// answers first with a readable message and this only catches what gets past it.</summary>
    public int HeapHardLimitMb { get; set; } = 384;
    public int MaxConcurrentRuns { get; set; } = 4;
    public int RunsPerMinutePerClient { get; set; } = 20;
    public int RunsPerMinuteTotal { get; set; } = 240;
    /// <summary>Behind Cloudflare the client is in CF-Connecting-IP. Off when the port is reachable
    /// directly, where anyone could set that header to dodge the per-client limit.</summary>
    public bool TrustForwardedHeaders { get; set; }

    /// <summary>
    /// "none" (default): the worker runs as a normal child process, isolated only by the interpreter
    /// and the OS account. "windows": each worker also runs inside an AppContainer (no network, no
    /// file access beyond the runs directory and its own binaries) placed in a Job Object (hard
    /// memory cap, no child processes, killed when the run ends). Ignored off Windows.
    /// </summary>
    public string Isolation { get; set; } = "none";

    /// <summary>
    /// Where per-run working directories are created in "windows" isolation. Its ACL must grant the
    /// AppContainer (ALL_APPLICATION_PACKAGES) modify rights, once, at deploy time — see README.
    /// Empty means a "runs" folder beside the service. In "none" isolation the system temp is used.
    /// </summary>
    public string RunsDir { get; set; } = "";

    /// <summary>Job-object active-process cap. 1 blocks the worker from starting any child process.</summary>
    public int JobMaxProcesses { get; set; } = 1;

    /// <summary>Optional hard CPU cap for the job, as a percent of total capacity (0 = off).</summary>
    public int CpuHardCapPercent { get; set; }

    /// <summary>AppContainer profile name (&lt;= 64 chars, no backslash).</summary>
    public string AppContainerName { get; set; } = "cscs.playground.sandbox";
}

/// <summary>What one run produced, as the worker reported it -- or as this class had to describe it.</summary>
public sealed class RunOutcome
{
    public bool Ok { get; set; }
    public string Output { get; set; } = "";
    public bool OutputTruncated { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
    public bool TimedOut { get; set; }
    public long ElapsedMs { get; set; }
    /// <summary>Set when the run never started: a limit refused it. The script was not executed.</summary>
    public bool Refused { get; set; }
    /// <summary>Explain mode only: one entry per cfunction.</summary>
    public List<PrecompileEntry>? Precompile { get; set; }
}

/// <summary>What the precompiler made of one cfunction (explain mode).</summary>
public sealed class PrecompileEntry
{
    public string Function { get; set; } = "";
    public string ReturnType { get; set; } = "";
    public string[] Arguments { get; set; } = [];
    public bool Translated { get; set; }
    public bool Compiles { get; set; }
    public string? Reason { get; set; }
    public string? GeneratedCode { get; set; }
}

/// <summary>
/// Starts one CscsSandbox process per script and enforces the limits that have to live OUTSIDE it:
///   - a hard kill of the whole process tree after the time limit plus a grace period, for the case
///     the worker's own watchdog cannot answer (a script stuck in native code, a crash mid-exit);
///   - a runtime heap limit, set through the worker's environment before it starts;
///   - an empty working directory per run, deleted afterwards, and no inherited environment
///     variables beyond what the .NET runtime needs to start;
///   - DOTNET_EnableDiagnostics=0, so nothing can attach a debugger or profiler to it;
///   - a cap on how many run at once, and per-client and overall rates;
///   - a cap on how much of the worker's stdout is read.
/// A worker that dies without writing its result line -- a stack overflow, say -- is reported as a
/// crash. Its stderr is drained and discarded: a runtime crash dump names local paths.
/// </summary>
public sealed class SandboxRunner : IDisposable
{
    const int MaxStdoutChars = 200_000;
    const int GraceMs = 3_000;

    static readonly JsonSerializerOptions s_json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    readonly SandboxOptions _options;
    readonly ILogger<SandboxRunner> _log;
    readonly SemaphoreSlim _concurrency;
    readonly PartitionedRateLimiter<string> _perClient;
    readonly RateLimiter _total;

    public SandboxRunner(SandboxOptions options, ILogger<SandboxRunner> log)
    {
        _options = options;
        _log = log;
        _concurrency = new SemaphoreSlim(Math.Max(1, options.MaxConcurrentRuns));
        _perClient = PartitionedRateLimiter.Create<string, string>(client =>
            RateLimitPartition.GetFixedWindowLimiter(client, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, options.RunsPerMinutePerClient),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
        _total = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, options.RunsPerMinuteTotal),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    }

    public SandboxOptions Options => _options;

    public string ResolvedSandboxPath => ResolvePath(_options.SandboxPath);

    /// <param name="explain">Also report what the precompiler makes of each cfunction. The worker
    /// compiles that C# in memory only and never loads it; the functions run interpreted.</param>
    public async Task<RunOutcome> RunAsync(string script, string client, CancellationToken cancellationToken, bool explain = false)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return Refuse("The script is empty.");
        }
        if (script.Length > _options.MaxScriptChars)
        {
            return Refuse($"The script is {script.Length} characters long; the limit is {_options.MaxScriptChars}.");
        }

        using var clientLease = _perClient.AttemptAcquire(client);
        if (!clientLease.IsAcquired)
        {
            return Refuse($"Rate limit: at most {_options.RunsPerMinutePerClient} runs per minute. Try again shortly.");
        }
        using var totalLease = _total.AttemptAcquire();
        if (!totalLease.IsAcquired)
        {
            return Refuse("The playground is busy right now. Try again in a minute.");
        }
        if (!await _concurrency.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken))
        {
            return Refuse("The playground is busy right now. Try again in a moment.");
        }

        try
        {
            var outcome = UseWindowsIsolation
                ? await StartAndWaitIsolatedAsync(script, explain, cancellationToken)
                : await StartAndWaitAsync(script, explain, cancellationToken);
            _log.LogInformation("{Mode} client={Client} chars={Chars} ok={Ok} timedOut={TimedOut} ms={Ms} error={Error}",
                explain ? "explain" : "run", client, script.Length, outcome.Ok, outcome.TimedOut, outcome.ElapsedMs, Shorten(outcome.Error, 160));
            return outcome;
        }
        finally
        {
            _concurrency.Release();
        }
    }

    /// <summary>Asks the worker which functions its allowlist keeps (for the language guide).</summary>
    public async Task<IReadOnlyList<string>> ListFunctionsAsync(CancellationToken cancellationToken)
    {
        var workDir = CreateWorkDir();
        try
        {
            using var process = new Process { StartInfo = StartInfo(workDir, "--list-functions") };
            process.Start();
            process.StandardInput.Close();
            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await process.WaitForExitAsync(timeout.Token);
            using var doc = JsonDocument.Parse(stdout.Trim());
            return doc.RootElement.GetProperty("kept").EnumerateArray().Select(e => e.GetString() ?? "").ToList();
        }
        finally
        {
            DeleteWorkDir(workDir);
        }
    }

    async Task<RunOutcome> StartAndWaitAsync(string script, bool explain, CancellationToken cancellationToken)
    {
        var timeLimitMs = explain ? Math.Max(_options.TimeLimitMs, _options.ExplainTimeLimitMs) : _options.TimeLimitMs;
        var workDir = CreateWorkDir();
        var clock = Stopwatch.StartNew();
        using var process = new Process { StartInfo = StartInfo(workDir) };
        try
        {
            process.Start();
        }
        catch (Exception e)
        {
            DeleteWorkDir(workDir);
            _log.LogError(e, "could not start the sandbox at {Path}", process.StartInfo.FileName);
            return new RunOutcome { Error = "The playground could not start its interpreter. This is a server problem, not your script." };
        }

        try
        {
            var stdoutTask = ReadCappedAsync(process.StandardOutput, MaxStdoutChars);
            var stderrTask = ReadCappedAsync(process.StandardError, 16_000);

            var request = JsonSerializer.Serialize(new
            {
                script,
                mode = explain ? "explain" : "run",
                timeLimitMs,
                maxOutputChars = _options.MaxOutputChars,
            }, s_json);
            try
            {
                await process.StandardInput.WriteAsync(request);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The worker is already gone; its exit code and stdout say why.
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeLimitMs + GraceMs);
            var killed = false;
            try
            {
                await process.WaitForExitAsync(deadline.Token);
            }
            catch (OperationCanceledException)
            {
                killed = true;
                Kill(process);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            var stdout = await stdoutTask;
            await stderrTask;

            if (TryParse(stdout, out var outcome))
            {
                return outcome;
            }
            if (killed)
            {
                return new RunOutcome
                {
                    TimedOut = true,
                    ElapsedMs = clock.ElapsedMilliseconds,
                    Error = $"Time limit reached ({timeLimitMs} ms); the interpreter had to be stopped.",
                };
            }
            return new RunOutcome
            {
                ElapsedMs = clock.ElapsedMilliseconds,
                Error = $"The interpreter crashed (exit code {process.ExitCode}) before it could report a result. " +
                        "The usual cause is a stack overflow from very deep recursion inside one expression.",
            };
        }
        finally
        {
            if (!process.HasExited)
            {
                Kill(process);
            }
            DeleteWorkDir(workDir);
        }
    }

    /// <summary>True when each run should go through an AppContainer + Job Object. Windows only.</summary>
    public bool UseWindowsIsolation =>
        OperatingSystem.IsWindows() &&
        string.Equals(_options.Isolation, "windows", StringComparison.OrdinalIgnoreCase);

    /// <summary>When and why the last isolated launch failed; null while none has. Shown by /health.</summary>
    public string? LastIsolationError { get; private set; }

    Windows.WindowsAppContainer? _container;
    readonly object _containerLock = new();

    async Task<RunOutcome> StartAndWaitIsolatedAsync(string script, bool explain, CancellationToken cancellationToken)
    {
        // Unreachable off Windows (UseWindowsIsolation gates it), and it keeps the platform analyzer
        // satisfied that the Windows-only body below only runs on Windows.
        if (!OperatingSystem.IsWindows())
        {
            return await StartAndWaitAsync(script, explain, cancellationToken);
        }
        return await StartAndWaitWindowsAsync(script, explain, cancellationToken);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    async Task<RunOutcome> StartAndWaitWindowsAsync(string script, bool explain, CancellationToken cancellationToken)
    {
        var timeLimitMs = explain ? Math.Max(_options.TimeLimitMs, _options.ExplainTimeLimitMs) : _options.TimeLimitMs;
        var exe = ResolvedSandboxPath;
        if (exe.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            _log.LogError("windows isolation needs the native worker exe, not {Path}", exe);
            return new RunOutcome { Error = "The playground is misconfigured: isolation requires the published CscsSandbox.exe. This is a server problem, not your script." };
        }

        var workDir = "";
        var clock = Stopwatch.StartNew();
        Windows.WindowsAppContainer container;
        Windows.WindowsJobObject job;
        try
        {
            workDir = Path.Combine(IsolationRunsDir(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);
            container = GetContainer();
            job = new Windows.WindowsJobObject(
                (long)_options.HeapHardLimitMb * 1024 * 1024, _options.JobMaxProcesses, _options.CpuHardCapPercent);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastIsolationError = $"{DateTime.UtcNow:u} {ex.Message}";
            _log.LogError(ex, "AppContainer or Job Object setup failed");
            DeleteWorkDir(workDir);
            return new RunOutcome
            {
                Error = "The playground's OS sandbox could not start this run. This is a server problem, not your script: " + ex.Message,
            };
        }
        Windows.WindowsAppContainer.LaunchedProcess? proc = null;
        try
        {
            var request = JsonSerializer.Serialize(new
            {
                script,
                mode = explain ? "explain" : "run",
                timeLimitMs,
                maxOutputChars = _options.MaxOutputChars,
            }, s_json);
            await File.WriteAllTextAsync(Path.Combine(workDir, "request.json"), request, cancellationToken);

            try
            {
                proc = container.Launch(exe, $"--io-dir \"{workDir}\"", workDir, WorkerEnvironment(workDir), job);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Fail closed with the reason: never fall back to running the script unisolated.
                LastIsolationError = $"{DateTime.UtcNow:u} {ex.Message}";
                _log.LogError(ex, "isolated worker launch failed");
                return new RunOutcome
                {
                    ElapsedMs = clock.ElapsedMilliseconds,
                    Error = "The playground's OS sandbox could not start this run. This is a server problem, not your script: " + ex.Message,
                };
            }

            var deadline = timeLimitMs + GraceMs;
            var exited = await Task.Run(() => proc.WaitForExit(deadline), cancellationToken);

            var responsePath = Path.Combine(workDir, "response.json");
            if (File.Exists(responsePath))
            {
                var json = await File.ReadAllTextAsync(responsePath, cancellationToken);
                if (TryParse(json, out var outcome))
                {
                    return outcome;
                }
            }
            if (!exited)
            {
                return new RunOutcome
                {
                    TimedOut = true,
                    ElapsedMs = clock.ElapsedMilliseconds,
                    Error = $"Time limit reached ({timeLimitMs} ms); the interpreter had to be stopped.",
                };
            }
            return new RunOutcome
            {
                ElapsedMs = clock.ElapsedMilliseconds,
                Error = $"The interpreter crashed (exit code {proc.ExitCode}) before it could report a result. " +
                        "The usual cause is a stack overflow from very deep recursion inside one expression.",
            };
        }
        finally
        {
            proc?.Terminate();
            proc?.Dispose();
            job.Dispose(); // kills anything still in the job
            DeleteWorkDir(workDir);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    Windows.WindowsAppContainer GetContainer()
    {
        lock (_containerLock)
        {
            return _container ??= Windows.WindowsAppContainer.Create(
                _options.AppContainerName, "CSCS Playground", "Sandbox for the public CSCS playground");
        }
    }

    string IsolationRunsDir()
    {
        var dir = string.IsNullOrWhiteSpace(_options.RunsDir)
            ? Path.Combine(AppContext.BaseDirectory, "runs")
            : _options.RunsDir;
        Directory.CreateDirectory(dir);
        return dir;
    }

    ProcessStartInfo StartInfo(string workDir, string? argument = null)
    {
        var path = ResolvedSandboxPath;
        var info = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workDir,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            info.FileName = "dotnet";
            info.ArgumentList.Add(path);
        }
        else
        {
            info.FileName = path;
        }
        if (argument != null)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment.Clear();
        foreach (var (key, value) in WorkerEnvironment(workDir))
        {
            info.Environment[key] = value;
        }
        return info;
    }

    /// <summary>
    /// The only variables the worker starts with: nothing inherited but what the runtime needs. The
    /// service's own environment can hold anything, and the worker has no business seeing it even
    /// though no allowed function can read it today. Shared by the normal and the isolated launch.
    /// </summary>
    Dictionary<string, string> WorkerEnvironment(string workDir)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "PATH", "SystemRoot", "windir", "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT(x86)", "ProgramFiles" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
            {
                env[name] = value;
            }
        }
        env["TEMP"] = workDir;
        env["TMP"] = workDir;
        env["HOME"] = workDir;
        env["USERPROFILE"] = workDir;
        env["DOTNET_GCHeapHardLimit"] = "0x" + ((long)_options.HeapHardLimitMb * 1024 * 1024).ToString("X");
        env["DOTNET_EnableDiagnostics"] = "0";
        env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        env["DOTNET_NOLOGO"] = "1";
        return env;
    }

    static bool TryParse(string stdout, out RunOutcome outcome)
    {
        outcome = new RunOutcome();
        var line = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        if (string.IsNullOrEmpty(line) || line[0] != '{')
        {
            return false;
        }
        try
        {
            outcome = JsonSerializer.Deserialize<RunOutcome>(line, s_json) ?? new RunOutcome();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Reads up to maxChars and keeps draining after that, so the child never blocks on a full pipe.</summary>
    static async Task<string> ReadCappedAsync(StreamReader reader, int maxChars)
    {
        var sb = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            var room = maxChars - sb.Length;
            if (room > 0)
            {
                sb.Append(buffer, 0, Math.Min(read, room));
            }
        }
        return sb.ToString();
    }

    static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }

    static string CreateWorkDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cscs-sandbox", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void DeleteWorkDir(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // A worker still releasing a handle; the temp folder is cleaned eventually either way.
        }
    }

    static string ResolvePath(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.IsPathRooted(configured) ? configured : Path.Combine(AppContext.BaseDirectory, configured);
        }
        var exe = OperatingSystem.IsWindows() ? "CscsSandbox.exe" : "CscsSandbox";
        return Path.Combine(AppContext.BaseDirectory, "sandbox", exe);
    }

    static RunOutcome Refuse(string message) => new() { Refused = true, Error = message };

    static string? Shorten(string? text, int max) =>
        text == null || text.Length <= max ? text : text[..max] + "…";

    public void Dispose()
    {
        _concurrency.Dispose();
        _perClient.Dispose();
        _total.Dispose();
        if (OperatingSystem.IsWindows())
        {
            _container?.Dispose();
        }
    }
}
