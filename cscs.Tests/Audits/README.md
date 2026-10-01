# Differential audits

Generated scripts that hold compiled code (`cfunction`) to the interpreter's answers. Each file
declares every shape twice -- `cfunction auc1(variable x) { ... }` and `function aui1(x) { ... }`
-- calls both over a list of values (numbers, zero, fractions, negatives, text, empty text,
numeric text, `"0"`, `"false"`, lists, maps, ...) and prints `DIFF <shape> on <value> -> [compiled]
vs [interpreted]` for every value on which they disagree, then a `DONE` line with the count of
shapes that compiled. An error counts as an answer: both sides must fail, or neither.

`AuditFixture` (cscs.Tests/IntegrationTests/Precompiler) runs each file through
`Interpreter.Process` and `Interpreter.ProcessAsync` and fails on any `DIFF` line:

```bash
CSCS_RUN_AUDITS=1 dotnet test cscs.Tests/cscs.Tests.csproj --filter TestCategory=Audit
```

| File | What it covers |
|---|---|
| audit5, audit6 | built-in members and operators on untyped values, as values, in text, in conditions |
| audit7 | statements: compounds, steps, element writes |
| audit8 | typed arguments and returns |
| audit9 | control flow: switch, loops, break and continue |
| audit10 | maps and collections |
| audit12 | globals read and written from compiled code |
| audit13 | aliases: a local sharing a global's collection |
| audit14 | recursion, direct and across functions |
| audit15 | typed arithmetic and text conversion |
| audit16 | the CSCS playground guide's features: classes, JSON, regex, Math |
| audit17, audit18 | `return ++x` / `x++`, steps on fields |
| audit19 | the truth rule, `&&`/`||` as 1 or 0, `!`, compounds and steps as `x = x op y` |
| audit20 | text indexing, `Size`/`Trim`, mixed `?:` branches, command-named calls, chains |
| audit21 | typed arguments feeding locals whose type a compound or step changes |
| audit22 | `list<int>`, `list<string>`, `map<string,int>` arguments |
| audit23 | class instances, fields holding collections, element writes and steps on fields |
| audit24 | control flow over text and lists, bodies without braces, try/finally in loops |
| audit25 | calls into script functions with typed parameters, defaults, command names |
| semantics | the interpreter's own rules settled in September 2026, copied from test.cscs, so they are checked through `ProcessAsync` as well |

They were generated as cross products of shapes and values; see `cscs/PRECOMPILATION.md` for what
each one found. A new shape goes in by appending the same four lines per shape the files use.
