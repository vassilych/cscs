using CscsMcp;
using CscsMcp.Tools;
using ModelContextProtocol.Protocol;

// CSCS Playground MCP server.
// Lets any MCP client run small CSCS scripts. It holds no data and runs no CSCS itself: every script
// goes to a fresh CscsSandbox process (SandboxRunner), so a hostile or broken script can only ever
// take down its own worker. Deployed as a Windows Service next to BrainPingPong; see README.md.

// Windows Services start in C:\Windows\System32, so pin the content root to the install folder.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.AddWindowsService(options => options.ServiceName = "CscsMcp");

var sandboxOptions = builder.Configuration.GetSection("Cscs").Get<SandboxOptions>() ?? new SandboxOptions();
builder.Services.AddSingleton(sandboxOptions);
builder.Services.AddSingleton<SandboxRunner>();
builder.Services.AddHttpContextAccessor();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "cscs-playground", Title = "CSCS Playground", Version = "1.0.0" };
        options.ServerInstructions =
            "CSCS Playground runs scripts in CSCS, a small C-like scripting language built on the Split-and-Merge " +
            "parsing algorithm (github.com/vassilych/cscs). Call cscs_guide before writing CSCS — the language has " +
            "its own conventions and a restricted function set here — then run_cscs to execute a script. To see how " +
            "CSCS precompiles a cfunction to C# (the generated code, whether it compiles, or why it falls back), use " +
            "explain_cscs. Scripts run in an isolated sandbox with no file, network, process or .NET access.";
    })
    // Stateless: each tool call is one HTTP request, so a restart never strands a client session.
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<CscsTools>();

var urls = builder.Configuration["Cscs:Urls"] ?? "http://0.0.0.0:17578";
builder.WebHost.UseUrls(urls);

var app = builder.Build();

var apiKeyRequired = (app.Configuration.GetSection("Cscs:ApiKeys").Get<string[]>() ?? [])
    .Any(k => !string.IsNullOrWhiteSpace(k));
app.UseMiddleware<ApiKeyMiddleware>();

app.MapMcp("/mcp");

app.MapGet("/health", (SandboxRunner runner) =>
{
    var path = runner.ResolvedSandboxPath;
    return Results.Ok(new
    {
        status = File.Exists(path) ? "ok" : "sandbox-missing",
        service = "cscs-playground-mcp",
        sandboxFound = File.Exists(path),
        apiKeyRequired,
        // "windows" only takes effect on Windows; anywhere else it reports as requested-but-inactive.
        isolation = runner.UseWindowsIsolation ? "windows"
            : string.Equals(runner.Options.Isolation, "windows", StringComparison.OrdinalIgnoreCase) ? "windows-requested-inactive"
            : "none",
        // The last failure to start an isolated worker, so a broken deploy can be diagnosed from
        // outside: a virtual service account often cannot write to the Windows event log.
        isolationError = runner.LastIsolationError,
        limits = new
        {
            runner.Options.TimeLimitMs,
            runner.Options.MaxOutputChars,
            runner.Options.MaxScriptChars,
            runner.Options.MaxConcurrentRuns,
            runner.Options.RunsPerMinutePerClient,
        },
        time = DateTime.UtcNow,
    });
});

var log = app.Services.GetRequiredService<ILogger<Program>>();
var sandbox = app.Services.GetRequiredService<SandboxRunner>().ResolvedSandboxPath;
if (!File.Exists(sandbox))
{
    log.LogWarning("CscsSandbox not found at {Path} -- every run will fail until it is published there (see README.md)", sandbox);
}
if (string.Equals(app.Services.GetRequiredService<SandboxRunner>().Options.Isolation, "windows", StringComparison.OrdinalIgnoreCase)
    && !app.Services.GetRequiredService<SandboxRunner>().UseWindowsIsolation)
{
    log.LogWarning("Cscs:Isolation is 'windows' but this is not Windows -- workers run WITHOUT AppContainer/Job isolation");
}
log.LogInformation("CSCS Playground MCP listening on {Urls}, endpoint /mcp, sandbox {Path}, api key {Required}",
    urls, sandbox, apiKeyRequired ? "required" : "not required (public)");

app.Run();

public partial class Program;
