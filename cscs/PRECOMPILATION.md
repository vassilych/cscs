# CSCS Precompilation

`cfunction` (and `dllfunction`) translate a CSCS function into C#, compile it, and call the
compiled delegate instead of interpreting. This document covers how that works after the
2026 rework. The original design is described in
[Compiling Scripts to Get Compiled-Language Performance](https://www.codemag.com/Article/2001071/Compiling-Scripts-to-Get-Compiled-Language-Performance);
what follows is what changed.

## The compiler backend is Roslyn, not CodeDom

`Precompiler.Compile()` used `CSharpCodeProvider.CompileAssemblyFromSource` from
`System.CodeDom`. That API throws `PlatformNotSupportedException` on .NET Core and .NET 5+,
so every `cfunction` failed at declaration time once the hosts moved to `net9.0` — quietly,
because the exception surfaced only as a script-level parsing error.

Compilation now goes through `RoslynCompiler` (`Precompiler.Roslyn.cs`). Besides working,
it reports real diagnostics: error ID, line and column, against a line-numbered dump of the
generated C#.

## Generated code only syncs to the interpreter when it has to

Compiled code used to mirror every variable write back into the interpreter:

```csharp
for (i = 0; i < __varInt[0]; i++) {
  __interpreter.AddGlobalOrLocalVariable("i", new GetVarFunction(Variable.ConvertToVariable(i)));
  r += (i*3.0 + 7.0) / (i + 1.0) - (i % 5);
  __interpreter.AddGlobalOrLocalVariable("r", new GetVarFunction(Variable.ConvertToVariable(r)));
}
```

The write-back is necessary when compiled code calls a CSCS function, because CSCS call
arguments are passed as a *string* that the interpreter re-parses and resolves by name. But
it costs far more than the surrounding arithmetic — measured at roughly 700x for a tight
numeric loop — and a function that never calls back into the interpreter does not need it.

`GetCSharpCode()` therefore converts twice. The first pass assumes no interpreter callback
and omits the write-back; if that pass turns out to emit one (`GetCSCSFunction` or
`GetCSCSVariable`), it converts again with the write-back restored. Functions that only do
arithmetic and call `System.Math` come out as plain C#.

| Loop runs | Interpreted | Compiled (before) | Compiled (after) |
|-----------|-------------|-------------------|------------------|
| 100       | 17 ms       | 3 ms              | 1 ms             |
| 1,000     | 70 ms       | 2 ms              | < 1 ms           |
| 10,000    | 569 ms      | 14 ms             | 1 ms             |
| 50,000    | 1517 ms     | 54 ms             | 1 ms             |

## Compiled assemblies are cached

`RoslynCompiler` keys each compilation on a hash of the generated source plus a fingerprint
of the referenced assemblies, caching the emitted assembly in memory and on disk. A cold
compile costs about a second; a cached one about 50 ms.

- `RoslynCompiler.CacheDirectory` — defaults to `<temp>/cscs-precompiled`.
- `RoslynCompiler.CacheEnabled` — set false to always compile.
- `RoslynCompiler.ClearCache()`, `CacheHits`, `CacheMisses`.
- `RoslynCompiler.ResetReferences()` — call after loading assemblies that compiled scripts
  need to see, so the reference set is rebuilt.

A corrupt cache entry is never fatal: it is deleted and the source is recompiled.

## Any cfunction runs, whether or not it can be translated

The translator handles a subset of CSCS. It used to be a hard subset: a construct outside
it threw at the declaration and killed the script. Now a cfunction whose body cannot be
translated is registered as an ordinary interpreted function instead, so it still runs --
just without the speedup.

That inverts the risk. A gap in the translator costs performance rather than breaking a
script, and coverage can be widened without any construct being a landmine in the meantime.

- `Precompiler.FallbackToInterpreter` -- true by default; set false to make translation
  failures throw (the tests do this to measure the translator itself).
- `Precompiler.Fallbacks` -- function name to the reason it could not be compiled.
- `Precompiler.DidFallBack(name)`, `Precompiler.ClearFallbacks()`.

From a script, `is_precompiled("name")` returns true when the named cfunction was really
translated to C# and false when it fell back. Behaviour is identical either way, so this
is the only way to tell -- use it to assert that something is actually being compiled
rather than quietly interpreted, as `Scripts/Samples/test.cscs` does.

Check `Fallbacks` when a cfunction is slower than expected: it is the difference between
"compiled" and "silently interpreted".

## What actually compiles

`PrecompilerCoverageFixture` writes each construct twice -- once as a `cfunction`, once as
a plain interpreted function with an identical body -- and compares the results. The
interpreter is the reference implementation, which is the only definition of correct a
cfunction has.

Of 54 constructs covered: **51 compile to C#, 3 fall back to the interpreter, 0 behave
differently from the interpreter.**

Compiled today: arithmetic and compound assignment, `if`/`else`, `while`, `for`, `break`,
`continue`, nested loops, `%`, comparison and logical operators, `System.Math` calls,
string concatenation/`Length`/`Upper`/`Substring`/`IndexOf`/`Replace`, integer division (literals, numeric locals and `int` arguments are all widened so `3/2`
is 1.5 as CSCS requires), string-number coercion, `++`/`--`, multiple `return`s, bool-valued variables, `x = {}` followed by
`.Add`, `elif`, the ternary `?:` with either numeric or string branches, and `!` applied
to a parenthesised expression.

Calls to CSCS functions compile, including recursion, several calls in one expression, and
calls nested inside each other. A call inside a larger expression emits statements, which
cannot sit in the middle of an expression, so they are hoisted ahead of the statement and
the expression keeps a reference to the value. Each call gets its own temporary: a shared
one would be overwritten by a later call before the expression read it.

Array literals compile: `{1,2,3}` builds a `Variable` directly in the generated code, and
`a[i]`, `a.Size` and `Split` results work through indexers added to `Variable`. Building the
collection in C# rather than asking the interpreter to evaluate the literal matters -- the
interpreter cannot evaluate a bare `{1,2,3}` fragment on its own.

Map literals compile too, and indexed assignment (`a[1] = 99`, `m["k"] = v`) goes through
`Variable.SetVariable`, which mirrors the interpreter's own `ExtendArrayHelper`: a key with
no index becomes a new map entry and a numeric index past the end extends the array.

`throw "..."` becomes `throw new ArgumentException(...)`, and the caught variable binds to
`Exception.Message` -- not `ToString()`, which would yield
`"System.ArgumentException: boom"` plus a stack trace where the interpreter gives `"boom"`.

`.Add` on a collection maps to `Variable.AddVariable`, which is what the interpreter calls.
That one is worth more than it looks: it usually sits inside a loop, so the callback it
replaced was paid on every iteration.

Two string members map to direct C#: `.Upper` and `.Lower`, on arguments and on locals
alike. A local's type is not tracked, but mapping it is still safe -- if it turns out not to
be a string the generated call simply does not compile and the function falls back, so the
guess can cost performance but never correctness. That list is short on purpose.
`.Size` is `0` on a string in CSCS rather than its length, and `.Contains`/`.StartsWith`
yield `1`/`0` rather than C#'s `True`/`False`, so mapping either would change results once
the value reaches a string. `.Length`, `.Substring`, `.IndexOf` and `.Replace` need no
mapping at all -- a string argument is a real C# string, so they already compile natively.

`**` falls back too: C# has no exponentiation operator. Rewriting `a**b` to `Math.Pow`
needs precedence-aware operand extraction — a naive split turns `2**3*4` into 4096 instead
of 32 — and `pow(a, b)` already compiles natively, so the operator form stays interpreted (the
coverage suite still checks compiled and interpreted agree on the value).

`x = {}` now builds a real local rather than deferring its declaration, so a collection
created empty and filled later compiles like a literal one. An index read inside a numeric
expression converts with `AsDouble()` — safe only there, because `IsKnownExpression` rejects
anything string-typed — and `.Size` on a local resolves to `Variable.Size` instead of an
interpreter callback.

`TokenizeStatement` is bracket-aware: operators inside an index belong to the index
expression, so `a[a.Size-1]` stays one token instead of being split at the `-` into three
that no later stage could reassemble.

String comparisons compile: a condition holding a string literal is not a "known
expression", so its arguments used to go unresolved and `if (s == "ab")` emitted a bare `s`.

Assigning to an argument writes the argument slot. It used to declare a shadowing local
while every *read* of the name still resolved to the slot, so `s = "x"; return s;` quietly
returned the original argument and `s = s + "!";` did not compile at all. Numeric arguments
were already handled; string ones were not, because they take a different path. An argument
is now never treated as an ordinary local, wherever it appears.

Still interpreted: reading an element into arithmetic
(`a[i] + n`). The last one needs `Variable + number`, and the interpreter's own addition
lives in a private `Parser.Merge` that needs a ParsingScript -- reimplementing it here would
risk compiled and interpreted arithmetic disagreeing on string/number mixes, so it is left
alone. `x = {}` followed by `x["k"] = v` also falls back, because the declaration of an
empty literal is deferred until its type is known.

`switch` does compile, but not to a C# `switch`. CSCS falls through from one non-empty case
to the next -- `Scripts/Samples/test.cscs` relies on it -- and C# forbids that, so it becomes
a matched-flag inside `do { } while (false)`:

```csharp
do {
  var  __swVal1   = <expression>;
  bool __swMatch1 = false;
  if (__swMatch1 || __swVal1 == <label>) { __swMatch1 = true; ...body... }
  ...
  { ...default body... }
} while (false);
```

The flag carries the fall-through, the loop gives `break` something to break out of, and
default runs unless an earlier `break` already left the block.

One case stays interpreted deliberately: in CSCS a `break` inside a switch exits the
*enclosing loop*, not just the switch, so
`for (i=0;i<4;i++) { switch(i) { case 0: ..; break; } }` stops after the first iteration.
The `do`/`while` gives `break` a nearer target, so a breaking switch inside a function that
loops is left alone rather than translated into something that quietly differs.

One cfunction calling another does compile, but only while the call is the whole returned
expression: `return other(x);` compiles, `return other(x) * 2;` falls back. Similarly a
method or property on a *local* in return position (`return local.Upper;`) falls back,
though the same thing on an argument (`return arg.Upper;`) compiles, as does assigning it
to a local first.

Note that `do { } while (...)` is not a CSCS construct at all -- the interpreter rejects
it too -- and CSCS `switch` requires an explicit `break` and does not accept `return`
inside a `case`.

## Ahead-of-time compilation (MAUI, iOS)

iOS forbids runtime code generation, so no amount of fixing the runtime compiler makes
`cfunction` work there. Instead, generate the C# on a desktop build and compile it into the
app.

**1. Generate**, from a normal desktop run of the script:

```
collect_comp_csharp(true);

cfunction double calcAot(int n) { ... }
cfunction string greetAot(string name) { ... }

write_comp_csharp("Generated/CscsPrecompiled.cs");
```

The script behaves exactly as usual; the generated C# is captured as a side effect.
`write_comp_csharp(path, className, namespace)` emits every collected function into one
file, plus a `RegisterAll()` method.

**2. Compile** the generated file into the app like any other source file.

**3. Register** during startup, before any script runs:

```csharp
CscsPrecompiled.RegisterAll();
```

`Precompiler.Compile()` checks `PrecompiledRegistry` first and uses the registered delegate
when it finds one, so the `cfunction` declaration in the script generates no code at all.
The script itself does not change between desktop and device.

Implementations can also be registered by hand:

```csharp
PrecompiledRegistry.Register("mySquare", (interp, str, num, ints, arrStr, arrNum,
    arrInt, mapStr, mapNum, vars) => new Variable(num[0] * num[0]));
```

Async mode (`Precompiler.AsyncMode`) is not covered by the registry and still compiles at
runtime.

## Tests

`cscs.Tests/IntegrationTests/Precompiler/PrecompilerFixture.cs` guards this path — that the
backend works at all, that compiled and interpreted results agree, that compile errors are
reported with detail, and that the AOT registry short-circuits code generation.

`Scripts/Samples/test.cscs` is the broader regression (350 assertions). Run it with a
second argument so the debugger server stays off:

```bash
dotnet run --project CscsScript/CscsScript.csproj -- Scripts/Samples/test.cscs nodebugger
```

## Known limits

- The translator is a token-level transpiler, not a parser. Unusual statement shapes fall
  back to the interpreter rather than compiling; Roslyn says exactly where when the
  fallback is disabled.
- The fallback triggers on translation failure, so it cannot catch generated C# that
  compiles and then misbehaves at run time. The coverage fixture compares results rather
  than just checking that things compile, which is what catches that class.
- Each `cfunction` is still its own compilation and its own assembly. Assemblies cannot be
  unloaded, so a process declaring very many distinct `cfunction`s grows monotonically. The
  cache keeps repeat runs cheap but does not address this.
- Script preprocessing (`Utils.GetSubscript()`) extracts declarations by keyword, so the
  caller's token set has to list every declaration keyword it cares about. Leaving out
  `cfunction` means the scanner walks into `cfunction` bodies and pulls statements out of
  them; including `return` in the token set makes that worse.
