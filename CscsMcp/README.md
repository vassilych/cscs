# CSCS Playground MCP server

An MCP server that lets any MCP client — Claude (desktop, code, claude.ai), ChatGPT connectors,
Cursor, VS Code — run small CSCS scripts. It is public by design: anyone can give CSCS a try from
the assistant they already use.

| Tool | What it does |
|---|---|
| `cscs_guide` | A short CSCS guide with examples that all run in the sandbox, plus the exact list of allowed functions. Assistants are told to read it first — no model has seen much CSCS. |
| `run_cscs` | Runs a script and returns its printed output, the value of its last statement, and any error with its line. |
| `explain_cscs` | For scripts with `cfunction`s: the C# the precompiler generates for each, whether it compiles or why it falls back, then the run's output. The C# is compiled in memory and never loaded. |

Three projects in this repository:

- `CscsMcp/` — the MCP server (ASP.NET Core, `ModelContextProtocol.AspNetCore`, stateless HTTP).
  It never runs CSCS itself and references no CSCS code.
- `CscsSandbox/` — runs exactly one script per process and exits. All CSCS execution happens here.
- `CscsMcp.Tests/` — 42 tests that start real sandbox processes: escape attempts, limits, the
  allowlist, explain mode, and every example in the guide.

## Security model

Running a stranger's script is remote code execution unless every path out is closed. CSCS has
more of those than it first appears, so the defence is layered, innermost first.

**1. No .NET access** (`InterpreterSecurity.AllowDotNet = false`, in the interpreter core).
Two routes to .NET sit inside things every script needs, so no function allowlist can close them:

- `new X()` looks `X` up among *all loaded .NET types* before CSCS classes, so
  `new System.Diagnostics.Process()` built a real `Process`.
- Any `Variable` holding a .NET object — a CSCS class instance included — answers `v.Name(...)` by
  calling that public .NET method through reflection. From `p.GetType()` onwards that is all of
  reflection, including static methods on any type.

The switch makes .NET types unfindable by name and turns every reflected read, write and call into
"not found". CSCS classes are unaffected. It defaults to `true`, so every other CSCS host behaves as
before.

**2. A function allowlist** (`CscsSandbox/Allowlist.cs`), applied after everything has registered,
so a function added to CSCS later stays out until someone lets it in. 113 functions are kept and
93 removed, among them `cfunction`/`csfunction`/`dllfunction`/`importdll` (compile or load .NET
code), `include`/`import`, every file, directory, process and socket function, `webRequest`,
`download`, `env`/`setenv`, threads and timers, `typeRef`, the debugger, `marshal`, `quit`/`exit`.
The list is written against the `Constants` symbols, so a renamed function breaks the build instead
of silently changing the sandbox. `CscsSandbox --list-functions` prints both lists.

**3. Limits inside the worker.**
- A watchdog thread: 5 s of wall clock, counted from when the interpreter is ready, and 256 MB of
  managed memory. Over either, it returns the output printed so far.
- Output capped at 16,000 characters.
- Recursion capped at 2,000 nested calls (`InterpreterSecurity.MaxCallDepth`), on a 64 MB thread
  stack. Without it, runaway recursion is a .NET stack overflow, which nothing can catch and which
  kills the process. With it, recursion is an ordinary, catchable CSCS error. The cap is low on
  purpose: an error thrown that deep is slow to unwind, and at 10,000 levels it outlasted the time
  limit.

**4. Isolation and limits outside it** (`CscsMcp/SandboxRunner.cs`).
- One process per script, started with an empty temporary working directory that is deleted
  afterwards.
- No inherited environment beyond what the runtime needs to start.
- `DOTNET_GCHeapHardLimit` (384 MB) as a backstop, and `DOTNET_EnableDiagnostics=0`.
- A hard kill of the process tree at the time limit plus 3 s.
- Stdout read up to a cap; stderr drained and discarded, since a crash dump names local paths.

**5. Server limits.** At most 4 scripts at once, 20 runs a minute per client (keyed on Cloudflare's
`CF-Connecting-IP`), 240 a minute in total, 20,000 characters per script, and optional API keys.

**Explain mode** (`PrecompileExplainer.Enabled`, in the interpreter core). A `cfunction` normally
becomes C# that is compiled, **loaded into the process and run**, outside every protection above,
and the translator copies some tokens it does not recognise straight into that C#. A test proves the
point: a body calling `System.IO.File.WriteAllText` compiles. In explain mode the definition is
translated and compiled in memory only for the report, `RoslynCompiler.Compile`, the one method
that loads compiled code, throws, and the function is registered as an ordinary interpreted
function. `run_cscs` does not accept `cfunction` at all. Explain runs get 10 seconds, because a cold
Roslyn compile costs 2–3 s per process.

**6. The operating system.** Run the service under its own low-privilege account with no access to
anything else on the machine — see below. This is the layer that still holds if everything above
it had a hole.

**7. AppContainer + Job Object** (optional, Windows only; `Cscs:Isolation = "windows"`). With this on,
each worker is started inside a Windows **AppContainer** — the sandbox Store apps and the Edge
renderer run in — with no capabilities, so the OS itself denies it **all network** and **all file
access** except the two places granted once at deploy time (its own binaries, read+execute, and the
per-run working directory). It is placed in a **Job Object** that caps job memory, forbids child
processes (`JobMaxProcesses = 1`), and is killed the instant the run ends, so a worker can never
outlive its request. Interpreted scripts are already contained by layers 1–3; this is the layer that
would still hold if a bug ever let compiled or native code run. It is off by default because it
needs the published native exe and the deploy-time grants below. The worker talks over
`request.json` / `response.json` in the working directory instead of pipes, since granting a
container one directory is far simpler and more robust than handing pipe handles into it.
`/health` reports `"isolation":"windows"` when it is actually active.

Script output goes back to the calling assistant inside a code fence longer than any run of
backticks in it, so a script cannot print its way out of the fence to pose as the tool.

## Run it locally

```bash
dotnet build CscsSandbox/CscsSandbox.csproj
dotnet build CscsMcp/CscsMcp.csproj
Cscs__Urls=http://127.0.0.1:17578 \
Cscs__SandboxPath=$PWD/CscsSandbox/bin/Debug/net9.0/CscsSandbox \
Cscs__TrustForwardedHeaders=false \
dotnet CscsMcp/bin/Debug/net9.0/CscsMcp.dll
```

Then `curl http://127.0.0.1:17578/health`, or connect a client to `http://127.0.0.1:17578/mcp`.

Tests: `dotnet test CscsMcp.Tests/CscsMcp.Tests.csproj`.

## Deploy on the Windows server (next to BrainPingPong)

It is a separate Windows Service on its own port (17578; BrainPingPong uses 17575, ChatCompare
MCP 17576, and the AgeFace connector inside the ChatCompare service 17577). It shares nothing with
them: no database, no settings file, no secrets.

**The short way (install and every update).** On any machine with the .NET SDK:

```bash
CscsMcp/deploy/publish-windows.sh
```

It builds `CscsMcp/bin/CscsMcp-win-x64.zip`: server and worker self-contained for win-x64 (the
server needs no .NET install), plus `install-cscs.ps1`. Copy the zip to the server, unzip it, and in
an elevated PowerShell in that folder run
`powershell -ExecutionPolicy Bypass -File .\install-cscs.ps1`. The script does steps 1, 2 and the
firewall part of 3 below: it copies the files to `C:\Services\CscsMcp`, creates the service under
`NT SERVICE\CscsMcp` (restart on failure), grants its folder, denies it `C:\Services\BrainPingPong`
and `C:\Services\ChatCompareMcp`, opens 17578 to Cloudflare's IPv4 ranges only, starts it and
checks `/health`. Running it again updates the binaries and keeps the server's `appsettings.json`.
Keep the zip out of git: it is about 90 MB.

With this repository cloned on the server and the .NET 9 SDK installed there, skip the zip: after
`git pull`, `CscsMcp\deploy\update-from-source.ps1` (elevated) builds the same package locally and
runs `install-cscs.ps1` on it.

The steps it automates, for reference:

**1. Publish** (from this repository). ReadyToRun matters here, because every run starts a new
process:

```powershell
dotnet publish CscsSandbox\CscsSandbox.csproj -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o C:\Services\CscsMcp\sandbox
dotnet publish CscsMcp\CscsMcp.csproj -c Release -r win-x64 --self-contained false -o C:\Services\CscsMcp
```

The server finds the worker at `sandbox\CscsSandbox.exe` next to itself (`Cscs:SandboxPath`
overrides that).

**2. Create the service under its own virtual account**, so the sandbox — which inherits the
service's identity — runs with none of your rights:

```powershell
sc.exe create CscsMcp binPath= "C:\Services\CscsMcp\CscsMcp.exe" start= auto DisplayName= "CSCS Playground MCP"
sc.exe config CscsMcp obj= "NT SERVICE\CscsMcp"
icacls C:\Services\CscsMcp /grant "NT SERVICE\CscsMcp:(OI)(CI)RX"
icacls C:\Services\BrainPingPong /deny "NT SERVICE\CscsMcp:(OI)(CI)F"
sc.exe start CscsMcp
```

The `deny` line matters most: `C:\Services\BrainPingPong\appsettings.json` holds the MySQL
connection string and the AI provider keys. Deny the account any other folder that holds secrets
the same way.

**3. Network: publish it as `cscs.brainpingpong.com`**, the same way `mcp.brainpingpong.com` reaches
port 17576:

- In Cloudflare DNS for `brainpingpong.com`, add `cscs`, proxied (orange cloud), with the same
  target as the `mcp` record — or add a public hostname `cscs.brainpingpong.com` →
  `http://localhost:17578` if the other services come in through a Cloudflare Tunnel.
- `mcp.` reaches 17576 through the Origin Rule `MCP Port`; add a rule `CSCS Port` the same way:
  hostname equals `cscs.brainpingpong.com` → destination port 17578. Without it Cloudflare connects
  to port 80/443 and reaches the wrong service or nothing.
- Give `cscs.` whatever SSL/TLS mode and WAF / bot-protection exceptions `mcp.` has. Claude's
  connector calls come from Anthropic's servers, not a browser, so a challenge page breaks them.
- Let Windows Firewall accept 17578 only from localhost or Cloudflare's IP ranges, like 17575/17576.
  Keep `Cscs:TrustForwardedHeaders` `true` only while the port is unreachable directly; otherwise
  anyone can forge `CF-Connecting-IP` to dodge the per-client limit.

No API keys are configured (`Cscs:ApiKeys` is empty), so the playground is public as soon as the
hostname resolves.

**4. Check it**: `https://cscs.brainpingpong.com/health` should report `"status": "ok"`,
`"sandboxFound": true` and `"apiKeyRequired": false`.

## Turn on AppContainer + Job Object isolation (recommended, Windows)

This adds OS-enforced containment (layer 7 above) on top of the interpreter's own. It is worth
doing even though compiled code is never run, because it means a stranger's *interpreted* script is
also denied network and disk by the kernel, not only by the allowlist.

Running on the production server since 2026-09-24: normal runs, classes and exceptions, the time,
memory and recursion limits, the .NET escape attempt, `explain_cscs` (Roslyn inside the container)
and four parallel runs all behave as without isolation.

**1. Grant the AppContainer (`ALL_APPLICATION_PACKAGES`) the two accesses it needs.**
`install-cscs.ps1` does this on every install; by hand it is:

```powershell
icacls C:\Services\CscsMcp\sandbox /grant "*S-1-15-2-1:(OI)(CI)(RX)"
mkdir C:\Services\CscsMcp\runs
icacls C:\Services\CscsMcp\runs /grant "*S-1-15-2-1:(OI)(CI)(M)"
```

`S-1-15-2-1` is the well-known SID for all app packages, which every AppContainer belongs to. The
worker binaries need read+execute; the runs directory needs modify (it writes `response.json`
there). The package is self-contained, so the .NET runtime is inside `sandbox\` and covered by
the first grant.

**2. Point the service at that runs directory and turn isolation on** in `appsettings.json`:

```json
"Cscs": {
  "Isolation": "windows",
  "RunsDir": "C:/Services/CscsMcp/runs"
}
```

Restart the service. It must be the **native** `CscsSandbox.exe` (the publish step above), not
`dotnet CscsSandbox.dll` — isolation refuses the `.dll` form.

**3. Self-check.** `https://cscs.brainpingpong.com/health` must now show `"isolation":"windows"`
(not `windows-requested-inactive`) and `"isolationError": null`, and a run of `print(1)` must still
return `1`. If a contained worker cannot start, the run fails closed -- it is never retried without
isolation -- and both the tool result and `isolationError` name the Win32 error or exit code; the
Windows event log usually has nothing, since a virtual service account cannot register an event
source. To switch it off again, set `Isolation` to `none` and restart.

Two things the service does for the container that are easy to miss when changing this code:

- **LOCALAPPDATA.** Windows redirects a contained process's TEMP/LOCALAPPDATA to
  `%LOCALAPPDATA%\Packages\<name>\AC` and refuses to start it (Win32 error 203,
  `ERROR_ENVVAR_NOT_FOUND`) if the variable is missing, as it is from the minimal worker environment.
  The worker gets LOCALAPPDATA pointing into its own run directory.
- **Window station and desktop.** The worker's Windows DLLs connect to the service's hidden window
  station and desktop while they initialize; their DACL does not include AppContainers, so the
  worker died with `0xC0000142` (`STATUS_DLL_INIT_FAILED`). When it creates the container, the
  service grants that container's SID access to both. They belong to this service alone and show
  no windows.

## Connecting a client

- **Claude Code**: `claude mcp add --transport http cscs https://cscs.brainpingpong.com/mcp`
- **claude.ai / Claude Desktop**: add a custom connector with the URL `https://cscs.brainpingpong.com/mcp`
  (no authentication).
- **Anything else that speaks MCP over HTTP**: the endpoint is `https://cscs.brainpingpong.com/mcp`.
- **Private mode**: list keys in `Cscs:ApiKeys` in `appsettings.json`. Clients then send
  `Authorization: Bearer <key>` or `X-Api-Key: <key>`.

## Configuration (`appsettings.json`, section `Cscs`)

| Setting | Default | |
|---|---|---|
| `Urls` | `http://0.0.0.0:17578` | listen address |
| `SandboxPath` | `sandbox/CscsSandbox(.exe)` | worker location |
| `TimeLimitMs` / `MaxOutputChars` / `MaxScriptChars` | 5000 / 16000 / 20000 | per run; the worker also caps what may be asked for |
| `HeapHardLimitMb` | 384 | runtime heap limit for the worker |
| `MaxConcurrentRuns` | 4 | each run can use about one core and 384 MB for 5 s |
| `RunsPerMinutePerClient` / `RunsPerMinuteTotal` | 20 / 240 | |
| `TrustForwardedHeaders` | true | see the network step above |
| `ApiKeys` | empty (public) | |
| `Isolation` | `none` | `windows` adds AppContainer + Job Object (needs the grants above) |
| `RunsDir` | `runs/` beside the service | per-run dirs in `windows` isolation |
| `JobMaxProcesses` | 1 | job active-process cap; 1 blocks any child process |
| `CpuHardCapPercent` | 0 (off) | optional hard CPU cap for the job |
| `AppContainerName` | `cscs.playground.sandbox` | profile name, ≤ 64 chars |

## Known limits

- Recursion through a class *method* can use enough memory per call to stop at the 256 MB limit
  before the 2,000-call cap. It still ends as a clean error.
- Without `Isolation: windows` the heap limit covers managed memory only; with it, the Job Object
  adds an OS-enforced memory cap (and an optional CPU cap). CSCS scripts have no native allocation
  path left in the sandbox either way, and the per-process time limit bounds CPU.
- Compiled `cfunction` code never runs here; `explain_cscs` shows it instead. Running it would
  need every symbol the generated C# uses checked against a strict allowlist before loading.
