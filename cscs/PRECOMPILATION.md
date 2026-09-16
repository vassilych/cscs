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

Of 906 constructs covered: **882 compile to C#, 24 fall back to the interpreter, 0 behave
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

`+` is the one operator whose meaning depends on the operand types: CSCS concatenates when
either side is a string and adds when both are numbers, and an interpreter callback's type
is not known until it runs. Translating a callback result to a fixed `AsDouble()` or
`AsString()` therefore had to guess, and a wrong guess did not fail to compile -- it quietly
changed the answer, so `return helper(n) + helper(n);` over strings came back as `0`. The
generated code now hands the `Variable` to `Variable`'s own `+`, which applies the
interpreter's rule at run time. Every other operator is numeric in CSCS, so the conversion
still applies there.

Compound assignment on a collection element goes through `Variable`'s own `+` for `+=`, for
the same reason the callback results above do: forcing the current value to a number read a
string element as 0, so `a[0] += "y"` on `{"x"}` produced `"0y"`. The element is also
addressed by the index `Variable` rather than by `AsInt()`, so a map key works as well as an
array position.

A quote inside a subscript no longer makes an expression look string-typed. `IsString` only
looks for a quote anywhere in the token, so `m["a"] * 100` counted as a string operand and
the subscript stopped being converted to a number -- the key says nothing about the type of
the expression around it.

Subscripts carry no cast any more. `Variable` has an indexer for each type an index can
arrive as -- `int`, `double` (loop counters are declared double so `/` is real division),
`string`, and `Variable`, which is what `m[keys[i]]` produces. Casting the index to `int`
covered the numeric cases and made the other two impossible to compile.

A map literal compiles wherever an expression is expected -- `m["x"] = {"y" : 5}`, or as an
argument -- through `Variable.NewMap`, which builds it with `SetHashVariable` exactly as the
interpreter does. Bitwise compound assignment (`&=`, `|=`, `^=`) compiles as the
read-modify-write it stands for, truncating with `(int)` as `Parser.MergeNumbers` does;
`<<=` and `>>=` are absent because CSCS itself does not have them. `!` applied to a bool
local after `&&` compiles: the tokenizer splits on `&&` and not on the `!`, so the `!`
arrived glued to the name and the whole token was looked up as a function.

`==` and `!=` between a string and a number compile. CSCS compares the two `AsString()`
forms -- `"5" == 5` is true but `"05" == 5` is false -- and C# has no operator for the pair
at all, so these go through `CompareCscs` overloads that format the number through
`Variable` rather than through C#. Only a mixed pair is rewritten; string/string and
number/number already compiled and are left alone.

`===` and `!==` compile. A pair of strings compares ordinally -- which is what the interpreter
does once its type check passes -- and a pair of numbers behaves exactly as `==` does, so the
operator is simply swapped. The swap has to be written without spaces around it, for the same
reason the `**` rewrite does: statements reach the translator with their whitespace stripped,
and adding any back makes the operands resolve differently. A mixed pair is still left to the
interpreter rather than folded to a constant false, since the undefined cases make that
unsafe.

`Variable` carries the members a script can ask of any value -- `Length`, `StartsWith`,
`EndsWith`, `IndexOf`, `Substring`, `Replace`, `Contains` -- so a collection element behaves
as a string variable does, delegating to `CscsStringMembers` for the case rules and
`Substring`'s clamping. Two bugs surfaced while proving that: `GetFunctionName` overwrote a
token's suffix when the name was also a subscript, so `words[i].Length` came back as
`words[i]` and an assignment stored the element instead of its length; and the
collection-local lookup did not trim, so a name after "=" arrived as `" counts"` and its
subscript went out without the numeric conversion.

Assigning a collection element into a local compiles. The declaration follows the first
assignment, so `hits = 0` made a `double` and the later `hits = counts[key]` had a `Variable`
with nowhere to go. A pre-pass over the function's statements now finds the locals that are
assigned an element anywhere in the body and declares those as `Variable` throughout, with
every assignment to them going through one builder so that a number, a string and an element
all fit the same declaration. Widening the local cannot change an answer: a `Variable` holds
what the interpreter would hold, and the operators above apply the interpreter's rules.

A nested literal assigned straight to a local (`a = {{1,2},{3,4}}`) compiles. Adding one to
a collection already did; the statement form had its own element loop, which emitted the
inner braces verbatim. Both loops now share the one element builder.

Class fields compile. An instance is a `Variable`, which has no member named after the
script's field, so a field is read through the same property lookup the interpreter uses.
`new` itself stays an interpreter callback, and had two things missing: the action variable
it passes by reference was never initialised, and the instance name -- which `new` reads off
the script being parsed, and which compiled code has to hand over explicitly, since its
script holds only the arguments. Without the name the constructor threw a
NullReferenceException at run time rather than failing to compile.

A field reads the same way whether the expression is arithmetic or a string concatenation.
Those take different paths through the translator -- a string literal makes the expression
not a "known expression" -- and each had to learn the lookup separately; there are three such
member sites in all.

A method on a subscript result compiles -- `bag[0].Sum()`. It is the one construct here with
no entry in the coverage suite, and deliberately so: the interpreter runs it at the top level
but not inside an interpreted function ("Couldn't find variable [bag[0]]"), so there is no
interpreted twin to compare against. The compiled result matches what the interpreter gives at
the top level.

That asymmetry is an interpreter bug, and its cause is known. `Interpreter.GetArrayFunction`
resolves the array's name with `GetVariable`, which begins:

    if (!force && script != null && script.TryPrev() == Constants.START_ARG)
        return GetFunction(name);

With `bag[0].Val()` the pointer sits just past the `(` of the call, so `bag` is looked up
among the *functions* rather than the variables. A global is found either way, which is why
this only ever fails inside a function, where `bag` is a local. Passing `force: true` there
fixes the in-function case and breaks the top-level one, so the real fix has to tell the two
roles of that `(` apart -- the one that belongs to the name from the one that belongs to a
call after the subscript -- rather than just bypassing the check.

A `switch` on a field compiles. The value holds a `Variable`, which cannot be compared to the
labels with `==`, so the comparison is made explicitly by the rule the interpreter uses: text
as text, numbers as numbers. Written as a call rather than as an `==` operator on `Variable`,
which would change what every existing reference comparison in the interpreter means.

Writing a field compiles too, through the interpreter's own property setter. A field reads off
a collection element as readily as off a named local.

`new` compiles only as the whole right-hand side of an assignment, which is where the
statement form fits. Building one in an expression -- as an argument, or added to a collection
-- was tried and withdrawn: this interpreter resolves a method's own fields through the named
instance, and an instance built in an expression has no name to be resolved through, so it
could read its fields but threw on any call to one of its own methods. Fields working while
methods did not is what made that easy to miss. Anything that constructs an instance in an
expression therefore stays interpreted.

One interpreter fix survives from that attempt: an instance need not be named. The constructor
read the name from the assignment being parsed and threw on a null rather than producing an
instance.

A member chain on a field compiles -- `p.name.Upper`, `p.kid.v` -- since each property read
yields a `Variable`, so the next segment simply reads from that one. The pass that decides
which locals hold a `Variable` repeats until it stops finding new ones, so `q = p.Kid()` makes
`q` one as soon as `p` is known to be, whichever order the two appear in.

`p.v++` and `p.v += 2` are deliberately *not* compiled: CSCS itself rejects both
("Variable or function [p.v] doesn't exist"), so building them would let compiled code succeed
where the language does not. `p.v = p.v + 1` is the form that works, and it compiles.

A member on the result of a method call compiles too -- `p.Kid().v`, `p.Kid().tag.Upper` --
since the call yields a `Variable` and the chain simply carries on from it. Both the
expression path and the token loop needed it, as every member change so far has.

`a.Add(...)` now takes the fast path even when the argument is quoted. It used to hand those
to the interpreter, on the grounds that an already-escaped argument cannot be re-resolved; the
raw text between the parentheses is used instead. That mattered for correctness, not just
coverage: the callback appends to the interpreter's own copy of the collection while compiled
code reads the local one, so `a.Add(new Node(1, "x"))` quietly added nothing. Instances can
therefore be collected and iterated.

A method call compiles as well. Running one needs an argument list -- that is what tells the
instance a method is wanted rather than a property -- so the call is built rather than read as
a member, and its parentheses are consumed where the token is resolved rather than emitted as
a separator. Both the expression path and the token loop needed it, for the same reason the
field lookup did.


A compound assignment on a global (`g += 5`, `g++`) is emitted as the read-modify-write it
stands for: a global is not a C# local, so there is nowhere for `+=` to read the old value
from. A prefix step on an element (`--a[1]`) becomes the postfix form, which as a statement
means the same thing -- the two differ only in the value they yield, and a statement
discards it.

`t += gcount`, where a local accumulates a global, still falls back. Widening the local the
way a subscript does would be the fix, but the pre-pass runs before any local is known, so a
name that also happens to be a global -- `i`, which scripts use at the top level -- would be
read as one and widen locals that are nothing of the sort. That cost 288 assertions in
test.cscs when tried, and the subscript rule is safe only because it is purely syntactic.

The pre-pass takes a local whose value merely *contains* a subscript, not only one that is a
subscript: `v = v + a[i]` leaves `v` holding whatever the element was, exactly as `v = a[i]`
does. Such a local keeps the script's spelling for its members -- `Variable` answers to
`Length`, `Upper`, `Lower`, `StartsWith` and the rest -- rather than being mapped to the C#
string member, which would emit `v.ToUpper()` on something that is not a string. `Variable`
carries the C# spellings too, since the mapping happens before the receiver's type is known.
Comparisons against a string literal work the same way, ordering by `string.Compare` as the
interpreter does whenever either side is text.

Getting a literal to work in a ternary branch also needed a fix in the interpreter, not just
the translator: `Utils.SkipRestExpr`, which skips the branch that is not taken, counted
parentheses and quotes but not braces. A comma inside `{1, 2, 3}` ended the skip inside the
literal, and a map literal's `":"` was read as the ternary separator.

A literal as a ternary branch compiles. Braces end a statement, so the literal has to be kept
whole after `?` and `:` the way it already was after `=`, `(` and `,`; each branch is then
built rather than emitted as braces C# cannot parse.

A subscript is not converted to a number when the expression adds. A collection can hold
mixed types, so an element's type is not known until it runs, and `+` is the one operator
whose meaning depends on it -- converting first read a string element as 0, so
`a[1] + a[0]` over `{1,"two",3}` came back as `1` rather than `"two1"`. `+=` and `++` still
convert: those accumulate into a local that is already declared.

A condition that is a value rather than a comparison -- `if (a[0])`, `if (m["t"])` -- becomes
a test on the number. CSCS counts a value as true only when its number is not zero, and
nothing else: even the string `"true"` is false there, which is why this does not call
`AsBool`, whose rule is different.

`Variable` carries the rest of the arithmetic and relational operators for the same reason
it carries `+`: precompiled code holds values whose type is only known when it runs -- a
global, a callback result -- and C# has no operators for those. Each mirrors
`Parser.MergeNumbers` and `Parser.MergeStrings`, including the cases the interpreter refuses:
`*` concatenates trimmed strings, while `-`, `/` and `%` on a string raise the error the
interpreter raises. `==` and `!=` are deliberately absent; those go through the comparison
rewrite instead.

A `for` loop initialised from an argument compiles: the initialiser was emitted verbatim, so
`for (i = n; i > 0; i--)` referred to an `n` the generated method does not have.

A global compiles. A name the function never declared, which the interpreter already holds
as a variable, is read with `__interpreter.GetVariableValue` -- `GetFunction` finds only
functions, so the variable tables have to be asked directly. Without it `g = g + 1` came out
as `var g = g + 1`, a local referring to itself. The write-back is the
`AddGlobalOrLocalVariable` the assignment already emits, so the global stays in step; the
coverage suite checks the value left behind, not just the one returned.

A CSCS call in an `if` condition compiles. A numeric condition is resolved without going
through the token loop, which is what converts calls, so the call came out as a bare name.
The call is emitted ahead of the statement, which an `if` makes exact -- it evaluates its
condition once either way. The hoist is suppressed when the condition has more than one
clause, since moving a call out of `g() && h()` would run `h()` even when `g()` is false,
and for `while`, where it would run once instead of on every iteration. Both of those still
work when the call is not the first operand: it then goes through the token loop and becomes
a callback, as it always did.

`m["k"].Size` compiles: the numeric conversion a subscript normally gets is skipped when a
member follows it, since the member needs the `Variable` and decides the type itself.

A `try` nested inside a `try` compiles. Every catch block used to lose two characters of
indent -- it emits its own `{` while the matching `}` arrives as a statement of its own and
decrements the depth -- and nesting drove the count below zero. The caught variable is now
bound to a `Variable` holding the message, under a private name for the exception itself:
leaving the script's name on the C# `Exception` meant any use other than returning it, such
as `throw "outer:" + e`, concatenated `System.ArgumentException: inner` and a stack trace.
`x - -3` is not a coverage gap: CSCS itself rejects it ("Can't process last token"), so
there is nothing to compile.

The shorthand forms of a global update -- `g += 5`, `g++` -- are left to the interpreter.
The long form `g = g + 1` compiles.

Accumulating a global into a numeric local (`t += g`) is left to the interpreter: it would
need an implicit `Variable`-to-`double` conversion, which would in turn make the `+`
overloads ambiguous.

`**` compiles. It is not a C# operator, so it becomes `Math.Pow`, grouped to the right as the
interpreter groups it -- `2**3**2` is 512, not 64, which falls out of replacing the last
`**` first. Only the operands either side are taken, not the whole expression, so the
precedence around them survives: `1 + n ** 2`, `n ** 3 + 1` and `Math.Abs(n) ** 2` all
compile.

A parenthesised base compiles. The statement arrives with its whitespace stripped, so
`return (x + 1) ** 2` is `return(x+1)**2` and the backwards scan for the operand walked
straight over the keyword, making the base `return(x+1)`; the scan now stops at a reserved
word, and the call is not glued onto whatever precedes it.

An element as the base compiles too. The conversion that makes a subscript numeric is
suppressed when the expression adds -- an element's type is not known until it runs -- but
that only applies at the element's own level: in `Math.Pow(a[1], 2) + n` the element is an
argument, and is numeric whatever the addition outside the call does.

A chain with a variable in it (`2**3**n`) compiles too. It resisted for several rounds, and
the cause was a single space: the rewrite emitted `Math.Pow(2, Math.Pow(3, n))` while the
statements that reach the translator have had their whitespace stripped, and the space after
the comma made the inner argument resolve differently. Printing the statement at the point
of tokenization for both origins is what found it -- the two strings had looked identical in
every earlier comparison.

A nested call as a later argument -- `Math.Max(1, Math.Min(n, 5))` -- compiles. The arguments
were split with a plain `Split(',')`, which tore the inner call apart; they are split at the
top level now.

A local that accumulates a global (`t += gcount`) compiles. Which names are external is
decided from the function's own text -- its parameters, loop variables and anything it
assigns are local, everything else is not -- rather than by asking the interpreter, which
would answer for names like `i` that scripts also use at the top level and would widen locals
that are nothing of the sort.

A `switch` inside a loop compiles. A `break` inside a switch ends the switch and nothing
else, as it does in C# and JavaScript, so the `do/while(false)` wrapper that gives `break`
a nearer target is always emitted -- inside a loop as much as outside one. `continue` still
belongs to the enclosing loop and the wrapper would capture it, which is why a body
containing one is not translated. That test matches `continue` as a word anywhere in the
statement rather than as the whole of it: a clause carries whatever follows its label, so
`case 1: continue;` is a single statement, and comparing the whole of it let that one
through to be compiled into a loop that ran an extra pass.

The interpreter used to read that `break` as leaving the enclosing *loop*, so
`for (i=0;i<4;i++) { switch(i) { case 0: ..; break; } }` ran a single pass, and the
translator mirrored it. Three things were wrong with the old `ProcessSwitch` and all three
are fixed: a clause that stopped in its middle -- on `return`, `break` or `continue`, and
`default`, which is never read to its end -- left the pointer nowhere near the closing
brace, so the scan for the next `case` ran off the end of the script; `break` travelled out
and broke the enclosing loop; and a clause's own last value escaped, so
`case 0: t += 1; } t += 100;` tried to make a single expression of 1 and 100. The switch now
ends by moving the pointer past its own body, exactly as `ProcessIf` does, and hands out
only `return` and `continue`.

`do { ... } while (cond);` compiles. C# has the same loop, so the body needed no special
handling -- only the trailing `while`, whose text is indistinguishable from a new loop, so it
is recognised by its position instead: on reaching a `do`, the statement that closes the body
is found and the one after it is recorded as that loop's tail. Recorded as a set rather than
a single value, so nested do-loops each keep their own.

A call in a `while` condition compiles. Hoisting it above the loop, as an `if` allows, would
run it once instead of on every iteration, so the loop is turned inside out instead:

    while (true) { <the call> if (!(condition)) { break; } <body> }

which evaluates the call exactly where the condition did. `continue` re-runs it and `break`
leaves, both of which the coverage suite checks by counting calls rather than only comparing
results -- a hoisted call would still return the right answer while calling once. As with
`if`, a multi-clause condition is left alone, so short-circuiting is never lost.

`for (v in a)` compiles. It is one statement rather than three, so it never reached the
three-part `for` handling and was copied through untranslated. It becomes an indexed loop
rather than a C# `foreach` over `Tuple`, so that it runs over anything a script can iterate
-- `for (k in m.Keys)` included -- with `Variable`'s indexer doing the work. The loop
variable holds an element, so it is a `Variable`, and the pre-pass widens whatever it feeds;
that is what made this workable, since accumulating an element into a `double` local cannot
compile.

String comparisons (`<`, `>`, `<=`, `>=`) compile. C# has no relational operators on
strings, so the comparison is rewritten into a `CompareCscs` call compared against 0 -- the
same shape `Parser.MergeStrings` uses. The rewrite runs on the whole statement rather than
in the token loop, because the tokenizer splits on the relational operator and by then the
two sides are in different tokens. It emits the operands as they were written and lets the
normal pipeline resolve them; resolving them inside the rewrite handed generated C# back to
the token loop, which tried to resolve it a second time. The receiver has to be a name, so a
literal on the left swaps sides and flips the operator, and a dotted receiver (`s.Upper >
"ABC"`) still falls back. Nothing happens unless one side is provably a string, so numeric
comparisons are untouched -- including a numeric clause sitting next to a string one.

Accumulating elements into a local with `+=` keeps them `Variable`s. Whether `+` joins or
adds is only known once it runs, so `r += a[i]` over `{"a","b","c"}` built `"000"` when the
elements were read as numbers first, where the interpreter concatenates to `"abc"`. The
`+=` form is excluded from the plain-`+` rule -- it is normally numeric accumulation into a
declared local -- so the target being a local that holds a `Variable` is what decides it.
An argument list is still the exception: `t += Math.Abs(a[i])` needs a number.

Equality on a value whose type is settled only when it runs -- a collection element, a
local holding one, a `for (v in a)` variable -- compiles, against a literal or against
another such value. C# reads `==` on a `Variable` as reference equality, so `c[0] == c[1]`
over `{"a","a"}` answered **false** where the interpreter answers true. Giving `Variable` an
`==` would change reference equality everywhere it is used, so the comparison becomes
`Variable.SameValue`, which applies the interpreter's own rule: as text if either side is a
string, by value otherwise. That is what a `switch` label already does, and it matches on
every mixed pair -- `5 == "5"` both ways round, and `"abc" == 0` false, since text against a
number is compared as text.

`SameValue` is deliberately not treated as a call when deciding whether an element needs
converting to a number: its whole purpose is to compare runtime types, so an element inside
one stays a `Variable` rather than arriving as a `double`. It is also listed alongside
`CompareCscs` as generated C# rather than a CSCS member, or the token loop read the *second*
one in `a == x && b == y` as a call to an unknown function and replaced it with an
interpreter callback -- only the first clause survived. The clauses of `&&` and `||` are
joined without spaces around the connective for the same reason every other rewrite is:
statements arrive with their whitespace stripped, and putting any back leaves what follows
unresolved.

The comparison compiles in every position it can take: both sides of `&&` and `||`, a
ternary's condition, and assigned to a local. The last needed two more things. The builder
for a local that holds a `Variable` runs *before* the comparison rewrite, so it applies the
rewrite to its own value; and testing that local on its own (`if (matched)`) reads it as a
number, since C# has no truth value for a `Variable`. That reading now applies to a negated
term as well -- `if (!found)`, `if (!(found))`, `if (!a[0])` -- and to a `while` over one.

One shape there still falls back: a bare `Variable` in a condition that is *otherwise*
numeric, as in `r && n < 5`. Such a condition is a "known expression" and never reaches the
token loop, which is where the reading happens. Rewriting it a statement earlier was tried
and taken back out: the `.AsDouble() != 0` then goes back through the pipeline as source
text, and statements that already worked stopped resolving.

`a = b = value` compiles, as the two statements it stands for, innermost first. Only plain
names are accepted as targets, so an element or a field keeps its own path.

A conversion inside a compound value compiles -- `r += q + string(m[q])`, the shape a
character tally is written in. The compound builder resolves its value with the argument
resolver, which asks for a call and gets one complete with its arguments, and then emits
those arguments a second time; the token loop consumes them instead. Only a value that
mentions one of the mapped conversions takes that route. Working it out generally, by asking
whether the value is a "known expression", was tried first and cost two constructs that the
resolver had been handling.

`Split` on a local that holds a `Variable` compiles -- `for (row in rows) { cols =
row.Split(","); }`, which is how a CSV is read. Like `Sort`, it was being read as a method of
a class and threw at run time. `Variable.Split` delegates to the interpreter's own
`TokenizeFunction`, with its defaults, rather than reimplementing the splitting. The one
thing left out is the interpreter's folding of an immediate `[i]` into the call, since C#
indexes the returned collection itself.

`Sort` and `Reverse` on a local that holds a `Variable` compile -- `k = m.Keys; k.Sort();`.
They were being read as methods of a *class* and sent to `Variable.CallMethod`, which threw
"Not a class instance" at run time, where the interpreter sorts the list. `Reverse` was added
to `Variable` for it; `Sort` was already there.

Only those two are listed there. Listing `Add` and its siblings alongside them took the
existing `a.Add(...)` handling away and six constructs fell back.

They are handled a step earlier instead: a member that is a *collection's* method rather than
a class's is kept off the CallMethod path altogether, and becomes an interpreter callback --
which is what a collection local's own methods already use. So `k = m.Keys; k.Add("z");` and
`k.Remove("a")` work, and nothing here has to restate what those mean. `Insert` is in the
list too, though the interpreter has no such member: both sides then fail the same way.

A local whose name is a C# keyword compiles. `out` is an ordinary name in a script, and the
generated code writes it as `@out`, which C# accepts. The name itself is untouched, so the
interpreter is still told about `out` and a callback can find it.

That is done once over the finished code rather than at each of the dozen places a name is
emitted -- the first attempt patched them one at a time and only reached about half. The
pass skips string literals, which is where the script's own names live, and skips a name
preceded by `.`, which would be somebody else's member. The keyword list is deliberately not
every C# keyword: the type names and modifiers are left out because the generated code
writes them itself, and escaping by name across the whole text turned the `int` of a
parameter declaration into `@int` and nothing compiled. So a script variable called `object`
or `int` still falls back -- loudly, as before.

Equality reaches an expression *over* a runtime-typed value as well as the value itself:
`q % 2 == 0` inside a `for (q in a)` yields a `Variable`, and C# has no `==` between that and
a number.

`*` between strings compiles, as the join it is: `MergeStrings` concatenates the trimmed
text for it exactly as it does for `+`, and C# has no `*` on strings at all. Numbers are left
alone. The generated call had to be marked as generated code, alongside `CompareCscs` and
`SameValue` -- otherwise the token loop read the *second* `ConvertToVariable(` in the
rewritten text as a call to an unknown function and replaced it with an interpreter callback,
which compiled and then threw at run time.

A call in an `elif` condition stays interpreted, for the same reason a `while` does: hoisting
it would run the call ahead of the whole chain, so it would happen even when the preceding
`if` was true.

Two divergences found this way were the *interpreter's*, not the translator's, and they were
the same fault. A `for (x in ...)` whose variable already existed in the function never
rebound it, and neither did a `catch (e)`: both called AddGlobalOrLocalVariable without a
script, and with no script the binding has nowhere to go when a local of that name is already
there, so it was dropped. `k = "z"; for (k in keys) { ... }` gave `z` every pass, a group-by
keyed on such a variable quietly returned the same key twice, and
`e = "pre"; try { throw "boom"; } catch (e)` caught the exception and then read `"pre"`. Both
now pass their script, as an assignment does. Every one of the 35 call sites was then checked
by parsing out its argument list: all of them pass a script now, and every construct that
binds a name -- a parameter, a canonical `for`, a `for (x in ...)`, a nested one of the same
name, a caught exception, one caught inside a loop -- was run against a local of that name
already existing. All of them agree with the compiled form, and test.cscs pins the family.

The compiled side has a matching limit: it always declares the loop variable, so a script
that used that name earlier does not compile and falls back. Declaring it only when the name
is new was tried and is wrong -- a name first declared inside another block is out of scope
by the time a later loop wants it -- and tracking which block each declaration was made in
did not settle it either.

## What a sweep for divergences found

Generating every operator over every pair of value kinds -- number, numeric string, text,
zero, empty string -- and running the compiled and interpreted forms in separate processes
turned up seven families of silent wrong answers, all in code that compiled cleanly. Each is
now pinned by a construct and by an assertion in test.cscs.

The root of most of them: `Parser.MergeCells` takes the numeric path only when **both** sides
are numbers and sends everything else to `MergeStrings`. `Variable`'s own operators read the
*left* side alone, so a number against a string went numeric where the interpreter
concatenates and compares as text.

- **Arithmetic.** `5 * "3"` is `"53"`, not 15 -- `MergeStrings` concatenates for `*` as well
  as `+`, and throws for `-`, `/` and `%`.
- **Comparison.** `5 < "abc"` is true, because `"5"` sorts before `"a"`. Reading the left
  side alone answered false.
- **Rendering a comparison.** `string(a > b)` is `1` or `0`, not `True` or `False`.
- **Truth.** The interpreter tests `Convert.ToBoolean` of the *numeric field*, so every
  string is false -- `"5"` included -- while `AsDouble()` parsed it to 5 and called it true.
  And `!x` is not the opposite: it is true only for a number that is zero, so a string is
  false both ways round. That is why there are two helpers, `IsTrue` and `IsFalse`, rather
  than a negation of one.
- **Compound assignment.** `r += v` is not `r = r + v`: it dispatches on the left type and
  takes the right side's *numeric field*, so `r = 5; r += "3"` leaves 5 where `r = r + "3"`
  gives `"53"`. Generated code calls the interpreter's own operator now, which is the only
  way to keep every corner of it in step.
- **Stepping.** `a[0]++` is not `a[0] += 1` either: it steps the numeric field, so `"5"++`
  is 1 while `"5" += 1` concatenates to `"51"`.
- **`.Size`.** It is the element count of a collection and 0 for anything else -- a number,
  a string, an instance. `Variable.Size` answered 1 for those, since it delegated to `Count`.
  Nothing in the interpreter reads that property; it exists for precompiled code, so it now
  matches what a script sees.

A CSCS call inside a literal compiles -- `{f(n), 2}` and `{"a": f(n)}`. The literal builder
returns straight to ProcessStatement rather than going through the token loop, so it
collects the statements the call produces itself. A call in a `for` initialiser compiles too, run once ahead of the loop. The loop variable's
declaration is left bare when it has one: `GetFunctionName` splits the call off the
initialiser, so the declaration would otherwise read `double i = helper`, and the loop's own
initialiser does the assigning either way. One value position still falls back loudly, a
call as a subscript index (`a[f(0)]`): that shape is not a "known expression" -- the call
does not resolve -- so it never reaches the resolver where the subscript is built, and
hoisting there does nothing.

`++` and `--` on a local that holds a `Variable` compile, through operators that step the
numeric field -- so the element `"7"` becomes 1, not 8, which is what the interpreter gives,
and the same rule unary minus follows. Without them a local seeded from a call (`t = f(n)`,
which makes it a `Variable`) could not be stepped at all.

A CSCS call as the value stored into an element compiles -- `a[0] = f(n)`, `a[0] += f(n)`,
`m["k"] = f(n)`, `g[0][1] = f(n)`. A call becomes several statements plus a temporary, which
neither the argument resolver nor the literal builder can produce, so the same hoist a
condition uses runs first and the store refers to the temporary. It keeps the temporary as a
`Variable` rather than reading it as a number, which a condition does: the element has to
hold whatever the call returned, so a string stays a string instead of becoming 0.

`for (ch in s)` over a string compiles, one character at a time and each as a string of its
own, which is what the interpreter yields. A string reaches C# as a `string` rather than a
`Variable`, so it has `Length` rather than `Size` and its indexer gives a `char`; the
element is wrapped back into a `Variable` so the body sees what it would from a collection.

A subscript straight after a call indexes what the call returned, on both sides.
`build()[1]` is the second element, not the whole collection: the interpreter applied the
subscript only for `Split`, which does it for its own result, and ignored it everywhere else.
The compiled side had the mirror-image fault -- it converted the result to text first and
then took the second *character*, so `t.Split(",")[1]` came back as a quote mark. Both are
fixed, and both directions are pinned.

A call inside a subscript compiles -- `a[f(0)]`, `a[int("1")]`, `a[f(0)] = 9`, and a map key
from a call. The index is worked out in a statement of its own first, so the subscript is
built from a plain name: the resolver builds the index itself and cannot produce the
statements a call needs. A `Math` call is left where it is, since that is C# already.

A source that *calls* something -- `for (w in s.Split(" "))`, `for (q in build())` -- is
assigned to a temporary first, so the whole pipeline builds the call. The resolver alone
emitted the call's text verbatim with the argument names in it unresolved. Only a call takes
that route: a subscript or a member reads perfectly well where it stands, and sending those
through a temporary cost three constructs that already worked.

Everything else the loop is given is asked what it holds at run time, through
`CscsConvert.AsItems`: a collection walks its elements, a string its characters, a scalar
itself once -- the three things the interpreter does. Reading `.Size` straight off the value
was wrong once `.Size` started answering 0 for a string, as the interpreter does: the inner
loop of `for (row in rows) { for (ch in row) ... }` then ran no passes at all and quietly
produced nothing.
Comparing that element for equality compiles too -- see below.

`s.Length` on a string argument counts as a number, so `if (i < s.Length)` and the same
test in a `while` compile. Judging the token by its owner made the whole expression look
string-typed, which sent it to the token loop -- where a member inside a condition came out
as an interpreter callback with its parentheses unbalanced, and nothing compiled at all.
The same applies to `s.IndexOf(...)`; every other string member yields a string or a bool.

Writing an element of a global compiles: `garr[0] = 9`, `gmap["k"] = 7` (a new key
included), and `grid[0][1] = 9`. Reading one already worked; writing one was read as a
declaration of a local and came out as `double garr[0]=9`. It now goes through the same
`SetVariable` a local collection uses, and the global is written back afterwards so the
interpreter sees the change -- the same shape a compound assignment to a global takes. A
local of the same name wins, since that is a real C# variable and needs no callback.

Stepping one rather than storing to it compiles too -- `garr[0] += 5`, `gmap["k"] += 4`,
`garr[0]++` -- through `Variable`'s own arithmetic, so `+` joins or adds by the runtime type
the way the interpreter does. The last index is read into a local first, since a compound
assignment uses it twice and an index that is itself an expression would otherwise be worked
out twice.

A CSCS call on the right of one compiles too (`garr[0] = helper(n)`), hoisted the same way
it is for a local element.

Both of those depended on fixing a subscript being read as part of a *name*. A bare read --
`return garr[0];`, with no arithmetic around it to send it down the expression path -- handed
the interpreter a variable called `garr[0]`, which answered the whole collection: `[4,1,3]`
where the interpreter answers `4`. The subscript is now applied to the value the way
`ResolveToken` has always applied it inside a larger expression. That also settled
`return gmap["k"];`, which failed to compile because the key's quotes closed the generated C#
string literal early -- there is no literal any more, since only `gmap` is named in it.

A class instance built anywhere but as the whole right-hand side of an assignment to a
plain name compiles: `a.Add(new Point(5, 6))`, `p.kid = new Named(6, "b")`, and a collection
of instances walked with `for (q in a)`. Only the one form is translated, and it is the
interpreter that builds the instance, under the name being assigned -- which is how a method
of the class later resolves its own fields. Anywhere else the `new` reached C# verbatim, as
`new Named(...)`, naming a type the generated code does not have. So the `new` is moved into
a statement of its own first and the rest of the statement refers to the temporary, which is
a real local registered with the interpreter like any other. Building the instance *without*
a name was tried instead, as a `Variable.NewInstance`, and every method call on it threw.

Two shapes stay interpreted. A branch of a ternary is not moved out, since only one of the
two runs and building both ahead of the test would run a constructor the script never asked
for. And one `new` inside another one's arguments is declined outright: left alone it
reaches the interpreter with its whitespace stripped and looks for a function called
`newPoint`, which throws at run time instead of falling back, while moving the inner one out
does not help either -- the temporary is registered with the interpreter, but the sub-script
that builds the outer instance does not see it, so the method call on it resolves a field of
the wrong class. Declining makes it fall back, which is right every time.

Fixing the interpreter bug behind the *first* of those -- an unnamed instance taking its
name from a stale assignment -- did not fix this one; they turned out to be separate.

Bit operations on elements compile. C# has none on `double` and an element arrives as one,
so the operand takes an `(int)` cast that no other operand needs -- which also matches the
interpreter, where `6.9 & 3` is `6 & 3`. A member or an argument list decides its own type,
so neither takes the cast, and `Math.Abs(a[i]) & 3` still falls back.

A comparison whose receiver is a *member* compiles: `s.Upper > "AA"` as well as
`s.Substring(0,2) > "aa"`. The rewrite turns the first into `s.Upper.CompareCscs("AA")`, and
reading that as a single member name matched nothing -- a property can be followed by the
rest of a chain, so the mapping now splits at the dot before the parenthesis and maps each
member in turn. `s.Length` and `s.IndexOf(...)` are excluded from being read as strings at
all, since both yield numbers.

Unary minus on an element compiles, through an operator on `Variable` that mirrors the
interpreter: it negates the numeric field rather than the parsed text, so `-a[0]` over the
element `"7"` is `-0`, exactly as interpreting it gives.

A comparison between two collection elements compiles. The element is left a `Variable`
so its own relational operators pick the rule from the runtime type, which is what the
interpreter does -- reading both sides as numbers first answered `false` for `c[0] > c[1]`
over `{"pear","fig"}`, since a string orders by text and only the value knows that. The
same reasoning already applied to `+`; the difference is that a condition is tokenized on
its relational operator, so each side reaches the resolver alone and cannot see the
comparison it belongs to. A statement-level flag carries that across. An argument list is
the exception: `Math.Abs(a[i])` needs a number whatever the comparison outside the call
does, so the resolver tracks which open parentheses belong to a call -- `if (` does not,
and neither does plain grouping, while a dotted name like `Math.Abs(` does.

A comparison also compiles inside a ternary and on the right of an assignment. The
condition of a ternary ends at its top-level `?`; without that boundary the whole ternary
was read as the right-hand operand, so `s > "m" ? "hi" : "lo"` became a comparison against
`("m" ? "hi" : "lo")`. All three parts are rewritten, so a branch holding another
comparison works too, and the colon that closes the ternary is found by nesting so a
branch holding another ternary is not cut in half.

The `int()`, `double()`, `long()`, `bool()` and `string()` conversions take a map or
collection subscript. Two things were in the way: the argument arrives with its quotes
escaped, so `string(m["a"])` reached the resolver as `m[\"a\"]` and its odd-backslash
branch turned the key into `'\''a`; and `Convert`'s methods need an `IConvertible`, which
a `Variable` is not, so unescaping alone made the code compile and then throw. They now go
through `CscsConvert`, which reads a `Variable` directly and passes everything else to
`Convert` -- `int("5.7")` still parses and `int(3.7)` still truncates toward zero.

"Provably a string" now covers locals as well as arguments. A pre-pass collects every
local whose *every* assignment is a string -- a string literal, or an argument declared
`string` -- and only those join the rewrite. The test is deliberately all-or-nothing: a
local written once as a string and once as a number (`w = s; w = 3;`) is excluded and the
function falls back, because there is no single C# type for it and guessing `string` would
order 3 against 20 as text. The asymmetry matters because `CompareCscs` has a
`(double, string)` overload, so a numeric local admitted by mistake would compile quietly
into a string comparison rather than failing to build.

`Math.Ceil` compiles as `Math.Ceiling`, which is the only spelling C# has. The interpreter
now accepts both spellings.

`First` and `Last` compile on both a string and a collection, through members added to
`Variable` that mirror the interpreter's properties.

Nested literals compile: a literal handed straight to a function (`a.Add({1,2})`) and a
literal made of literals (`a.Add({{1,2},{3,4}})`) both build their `Variable` inline, and a
chain of subscripts -- reading `a[0][1][0]` or assigning `a[0][1] = 9` -- resolves the whole
chain rather than stopping at the first bracket.

String members inside a condition compile, which they previously did not: a string-typed
condition is not a "known expression", so `s.Length > 2` arrived as one token that resolved
to neither half and left the argument name undeclared. `Contains`, `StartsWith`, `EndsWith`,
`IndexOf`, `Substring` and `Equals` go through `CscsStringMembers` rather than the C#
members of the same name. Most of these match case, as the C# members do, but they also take
CSCS's optional `"no_case"` argument, which the C# members have nowhere to put; `Substring`
clamps instead of throwing; and `Equals` compares by the current culture where
`string.Equals(string)` compares ordinally. `Size` and `Trim` are deliberately left to fall back: the interpreter
returns 0 for `Size` on a string, and its `Trim` does not return the trimmed string, so
compiling either would diverge.

Bitwise `~` compiles. It is a prefix rather than one of the interpreter's actions, so the
statement tokenizer had to be taught to split it off -- otherwise `~n` stayed glued together
and was looked up as a variable by that name.

A `do`-loop is deliberately **not** translated. Emitting an interpreter callback for it
handed `ProcessDoWhile` a fragment with no body to run, which first crashed and then, once
the crash was guarded, looped forever on a script that never advanced. The whole function
falls back instead, which is the tier that exists for exactly this.

`Contains` and `Keys` on a collection local compile through members added to `Variable` that
mirror the interpreter's implementations exactly, and bitwise `|`, `^`, `<<` and `>>` are
treated as arithmetic -- without that, an expression containing one looked "unknown" and the
precompiler stopped resolving argument names inside it.

Map literals compile too, and indexed assignment (`a[1] = 99`, `m["k"] = v`) goes through
`Variable.SetVariable`, which mirrors the interpreter's own `ExtendArrayHelper`: a key with
no index becomes a new map entry and a numeric index past the end extends the array.

`throw "..."` becomes `throw new ArgumentException(...)`, and the caught variable binds to
`Exception.Message` -- not `ToString()`, which would yield
`"System.ArgumentException: boom"` plus a stack trace where the interpreter gives `"boom"`.

`int()`, `double()`, `string()` and `bool()` compile. They return an *expression*: as
statements they spliced into the middle of an expression, so `int(n) + 1` emitted a dangling
`+1` and silently dropped it. `int()` is a cast rather than `Convert.ToInt32`, because CSCS
truncates toward zero -- `int(2.5)` is 2, `int(-3.7)` is -3 -- while `Convert` rounds.

`&` is bitwise AND and compiles directly. `MergeNumbers` had always implemented it; the
operator was simply unreachable while `&` was registered as the reference operator, which
now uses `@`.

Iterating a collection compiles -- `for (i = 0; i < a.Size; i++) { t += a[i]; }` -- once
the index is cast. Loop counters are declared `double` so that `/` is not integer division,
and `Variable`'s indexer takes an `int`; the interpreter truncates the same way, in
`GetArrayIndex`. Compound assignment through an index (`a[i] += v`, `a[i]++`) becomes an
explicit read-modify-write, since C# cannot compound-assign through an indexer, with the
index evaluated once into a temp.

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

The flag carries the fall-through, the loop gives `break` something to break out of -- the
switch itself, never the loop around it -- and default runs unless an earlier `break`
already left the block.

A call to a script function compiles inside a larger expression -- `return helper(n) * 2;`,
and a cfunction calling itself twice, `return fib(n-1) + fib(n-2);` -- and so does a member
on a local in return position (`return local.Upper;`), and grouped calls: `x = (f(n)) * 2;`,
`(f(n)) + (g(1))`. What still falls back is a call as an argument to a `Math` function,
`Math.Max(f(n), 5)`: the call's value is a Variable, and `Math.Max` wants a number.

Note that `do { } while (...)` is not a CSCS construct at all -- the interpreter rejects
it too. CSCS `switch` now accepts `return` inside a `case`, and a clause without a `break`
simply falls through to the next one.

## What running small programs found

Linear and binary search, bubble and selection sort, GCD, primes, FizzBuzz, a palindrome
check, string reversal, a Caesar shift and a character tally -- written as they would be
anywhere else -- turned up two more silent wrong answers, three interpreter bugs, and a few
shapes that fell back for no good reason. Each is pinned by a construct.

- **Every function call leaked a level of locals.** `RegisterArguments` pushes a *copy* of
  the function's stack level, and `Clone()` gives the copy an id of its own; the pop used
  the original's id, matched nothing, and popped nothing. Five calls in a loop left five
  levels on the stack. Interpreted bodies read their locals through their own script and
  never noticed, but a compiled caller publishes its arguments into whatever level is on
  top -- the first recursive call's leftovers -- so `fib(10)` came back as -80. The pop
  also copies the whole stack when it misses, so the leak made calls quadratic: 40,000
  calls took 17.4 s and now take 1.5 s. `RegisterArguments` returns the level it pushed
  and each caller pops that one.
- **`return (n);` returned nothing.** It arrives as the single token `return(n)`, which was
  counted as a bare `return` -- whenever the group had no operator inside to split it into
  more tokens. No compile error, just an empty result.
- **Escapes other than `\n`.** The interpreter's literal parser turns only `\\`, `\"`,
  `\'` and `\n` into characters; `"\t"` stays a backslash and a letter (`Print` is what
  shows it as a tab). C# made it one character, so `Length` and comparisons disagreed. The
  finished code gets every other escape's backslash doubled.
- **A group holding a lone string** (interpreter). `UpdateAction` stops at `)` without
  moving past it; a number goes on to leave the loop, which consumes it, but a string
  takes the early "done" return, which does not. The `)` left behind then ended the
  expression around the group: `(t) + "a"` dropped `+ "a"` inside a function and failed
  outright at the top level. The identity function now steps over its own `)` if the
  nested parse left it.
- **A minus in front of a group** (interpreter). `-(n + 1)` looked `-` up as a function.
  A bare `-` token now evaluates the group and negates it, as `-n` already did. `a - -b` is
  a different, older limit: with whitespace stripped it reads as `--`. Write `a - (-b)`.

Shapes that compile now: `s.At(i)` (CSCS strings are read with `At` -- `s[i]` is an error
there -- and it maps onto `AtCscs`, a one-letter string or an empty one past the end); an
element compared with an argument or a typed local (`a[i] == n` was `Variable == int`); a
member after a call returning a string (`s.At(0).Upper`, `s.Substring(0, 2).Upper`); a
`for` initialiser over a member (`i = s.Length - 1` used to declare `double i = s`); and a
script call inside a group, `1 + (helper(n))`.

`n = n / 2` on an `int` argument used to be left interpreted -- the interpreter makes it
13.5, and int division would say 13. It compiles now: see "Arguments" below.

## Where a call runs, and what it returns

Text processing, maps of lists, classes in loops, and helper-heavy code turned up two
problems with how a call to a script function is placed in compiled code, and four more
interpreter bugs.

**Where it runs.** A script call becomes statements, so the translator hoisted it ahead of
the statement using its value. That is only right where the value is always needed:

- inside `&&`, `||` and `?:` the call ran whether or not the operator reached it. With a
  guard -- `a.Size > 0 && f(a[0]) > 1` -- the compiled code read `a[0]` of an empty array
  and threw where the interpreter returned. A call with a side effect ran when it should
  not have;
- in a loop's condition it ran once, before the loop: `while (i < 10 && f(i) < n)` tested a
  stale value for ever and returned 10 where the interpreter returns 4.

Those places now get an *inline* call, `CscsCalls.Call(__interpreter, "f", args...)`, an
expression that runs the function where it stands, so C#'s own short-circuiting and branch
selection apply. `for` conditions and steps always use it, and it is what lets a call
appear inside a conversion (`int(f(n))`) or a `SameValue` comparison.

**What it returns.** The call's value was read with `AsDouble()` wherever the translator
guessed a number, so a string result became 0: `return f(n) * 2` gave 0 where the
interpreter joins `"s2"`, and `r += f(i)` in a cfunction without a declared return type
built `"000"`. The value now stays a Variable, whose operators apply the interpreter's own
rules; `==` goes through `SameValue`, as it does for an element. A local assigned from a
call is a Variable local, and so is anything computed from one.

**Interpreter bugs:**

- `Variable.EmptyInstance` was one shared, mutable object. An operation that updated a
  missing element in place updated *it*, and from then on every "nothing here" in the
  process was the string `"1"`: list literals ran past their closing brace and swallowed
  the statements after them, in that script and every later one. It is a new value each
  time now.
- `new` kept every space after it, as a shell command does, so `new Point(1, 2 + 3)` split
  its arguments at the spaces, matched no constructor, and silently kept the default
  field values. It keeps only the space after the keyword now, as `return` does.
- A brace literal as a map value, `{"x": {1, 2}}`, failed with "Couldn't find operand []".
- A method on an element of a function's own local -- `a[0].Substring(1)` -- could not see
  the local and failed; the same thing at the top level worked.

`Interpreter.Process` also trims the local stack back when an exception leaves it, as a
CSCS `catch` does; without that, the next script ran on top of a failed function's locals.

## Arguments, and locals that outlive their block

Lists and maps passed as arguments, `int` arguments, and locals reused across blocks.

**List and map arguments** (`list<int>`, `list<double>`, `list<string>`, `map<string,double>`,
`map<string,string>`) were almost untested, and most uses fell back or went wrong. The
runtime hands the function a typed C# copy, which became an opaque object whenever it met
the interpreter: `return a` answered with a C# type name, and `a[1].Upper` with the whole
list. The function now starts with the caller's own Variable -- the one the interpreter has
already put in the function's argument level -- and uses it as any collection local, so
`foreach`, subscripts, `Contains`, `Keys` and assignment all compile, and a change made in
the function reaches the caller exactly as it does when the function is interpreted.

**`int` arguments are C# ints.** Next to `*`, `+` or `-` one now reads as a double, as it
already did next to `/`: `n * n * n` for 100000 wrapped to -1530494976 where the interpreter
has 10^15. An `int` argument the body *assigns to* becomes a double local started from its
slot, since what the interpreter would put there need not be an int -- `n = n / 2` is 13.5,
and `n = 3 * n + 1` outgrows an int. That is what lets the Collatz-style loops compile.
`SubstringCscs` and `AtCscs` take a double position and truncate it as `GetSafeInt` does.
`Math.Round(x, n + 1)` is left interpreted: its digits must be an int.

**Locals that outlive their block.** C# scopes a local to the block it is declared in; CSCS
does not. A local used outside the block it is first assigned in -- two loops both using `c`,
an `if`/`else` that both set `r` which is read afterwards -- is declared at the top of the
function instead, with the type all of its assignments agree on (a literal or `new` makes it
a Variable, text a string, a comparison a bool, anything else a double). Where they do not
agree -- text in one branch, a number in the other -- it is left alone and falls back.

**Interpreter:** a member after the second of two chained calls,
`s.Replace(a, b).Replace(c, d).Length`, was left behind: "Couldn't find variable [.Length]"
at the top level, and silently dropped inside a function, which then returned the string.

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

`Scripts/Samples/test_compiled.cscs` and `Scripts/Samples/test.cscs` are the broader
regression: 987 assertions in test_compiled.cscs and 626 in test.cscs. Run them with a
second argument so the debugger server stays off:

```bash
dotnet run --project CscsScript/CscsScript.csproj -- Scripts/Samples/test_compiled.cscs nodebugger
dotnet run --project CscsScript/CscsScript.csproj -- Scripts/Samples/test.cscs nodebugger
```

The two together are the script-level regression suite. `test_compiled.cscs` holds every
`cfunction` case -- each written twice where it can be, once compiled and once interpreted,
with the two results compared -- and `test.cscs` keeps the tests of the language itself and
declares no `cfunction` at all. A clean run is 987 assertions in test_compiled.cscs and 626 in
test.cscs, 1613 together, with no
"ERROR. Test failed" and a "Finished." at the end of each.

Each file ends by printing its own totals, so the count no longer has to be grepped:

```
test_compiled.cscs: 534 OK, 0 Failed, 534 total.
test.cscs: 534 OK, 0 Failed, 534 total.
```

`test()` in `Scripts/Samples/functions.cscs` keeps the counts and prints the summary
(`testSummary`). A failing test throws by default, which `Scripts/Samples/stepin_tests.cscs`
relies on to raise an exception; both regression files set `testsContinueOnFail = 1` right
after their `include("functions.cscs")`, which reports each failure in red and carries on, so
one broken assertion no longer hides the rest of the run. The counts live in a `testCounts`
map rather than in two numbers on purpose: a function called from inside
`namespace ns1 { ... }` writes a *scalar* global into the namespace instead of the global
scope, which silently lost four of test.cscs's assertions, while an element write reaches the
collection from either scope.

## Arguments with defaults, values with formats

A `cfunction` signature may give an argument a default, and the parser that reads one took the
name to be the last space-separated word: `int n = 5` declared an argument called `5`, which
the first call rejected as an illegal name, and registered no default, so `f()` did not match
the signature either. The name is now taken from the left of the `=` and the default is kept on
the argument, which is where `CustomFunction` already reads one for an interpreted function.
The compiled path fills in the arguments a call leaves out before it checks the count.

The same array of argument names went to the translator and then to the function object, and
the translator stripped the defaults off it in place, so the function object saw none. It takes
a copy now.

`string(x, format)` -- a date above all -- compiles. Two things were in the way. The second
argument never reached `CscsConvert.ToText`, which had no overload taking a format; each
argument of a conversion is now converted on its own. And the format literal was taken apart:
`Utils.SplitToken` splits on `+ - * /` and paid no attention to quotes, so `"yyyy/MM/dd"` came
apart on the slashes and its middle piece was resolved as a variable. The literal went out as
`"yyyy/__varTempVar1/dd"`, which .NET then formatted as a date -- rendering the `m` in the
temporary's name as the value's minutes, so `2001/09/11` came back as `2001/__varTe31pVar1/11`.
`SplitToken` now skips separators inside literals, and a token that is one whole literal is
returned untouched.

Also compiling now: inheritance (a derived class's own members and its base's), a namespace's
functions and variables, comments in bodies, three-dimensional literals, `m["k"]++`,
`continue` in a `do` loop, string `<=`, a local collection passed to an interpreted function
that changes it, and the plain-function forms of the built-ins -- `Size`, `Substring`,
`Tokenize`, `StrBetween`, `StrUpper`, `DeepCopy`, `GetKeys`, `find_index`, `typeOf`, `Sort`,
`Reverse`, `AddUnique`, `Remove`, and `Math.Log`/`Sin`/`Cos`/`Atan2`.

Deliberately left to the interpreter, because compiling them would answer differently or
needs the script itself:

- `<<` and `>>`. The interpreter implements no shift -- its merge has no case for one, so
  `3 << 2` is 0 there, where C# gives 12. A statement containing one is refused.
- `iff(c, a, b)`. A statement in the interpreter, which needs the script around it: called
  back with only its arguments it threw "Couldn't skip expression".
- An enum's members, an assignment inside a condition, `Contains(collection, x)`, and
  `Math.Round(x, n + 1)` -- the widened `int` argument is a double, and the digits must be an
  `int`.

## finally, nested collections, and what a compiled function leaves behind

A `finally` clause compiles. "finally" is not one of `Constants.RESERVED`, so it never reached
the clause handling that `catch` goes through: it was resolved as a name, became a callback to
a function called "finally", and threw at run time.

`m["a"].Add(x)` and `a[i].Add(x)` compile. An element is a `Variable`, which has no `Add` of
its own; the runtime adds one, and since the element is the same object the collection holds,
the append is visible through it. Writing through two subscripts and building a nested map
from nothing already worked and are now covered.

Also compiling: a constructor's own default arguments and a class's `ToString` (in a
concatenation and called by name), a `for` header with a section left out, text outside ASCII,
and division by zero -- an infinity or NaN in both, printed the same way.

### Side effects on the interpreter's variables

Two differences here were invisible to the coverage fixture, which compares what a function
*returns*. Both are fixed:

- **An assignment to a name the interpreter already holds now reaches it.** The write-back is
  only emitted once something in the body needs the interpreter, so `g = "set"` in a function
  that touched nothing else left the variable at its old value, where the interpreted twin
  wrote it. Assigning to a known global now asks for that pass.
- **A compiled function's locals are no longer published as globals.** The generated
  write-back called `AddGlobalOrLocalVariable` with no script, which sent every one of them to
  a global. A local that happened to share a name with a global overwrote it for good -- and a
  later *interpreted* function assigning to that name then wrote the global too, so its own
  recursion overwrote its values and answered 6 where 144 was right. `Interpreter`'s
  `AddCompiledLocalVariable` writes the function's own level instead, which the callbacks of
  the body resolve and which is popped when the call returns; a global of that name is still
  written, because that is what the interpreter does.

Loop, `foreach` and `catch` variables take `AddCompiledLocalOnlyVariable`: a script never
assigned them, and writing one to a global of the same name left that global holding the
counter's value from inside the loop rather than the one the loop exited on.

Left to the interpreter: `null` as an operand (unmapped, so the C# does not compile, which
falls back cleanly), an instance assigned through two members (`p.kid.kid = new X()`), a `for`
header with every section empty, a ternary inside a literal (the statement tokenizer treats
"?" and ":" as separators, so the literal is in pieces before the builder sees it), and
`NameExists`.

## Arguments by name, and members in any case

An argument may be given by name -- `f(second = 2)` -- and the compiled path got it wrong:
it filled the declared defaults itself, appending one for the last position, *before*
`RegisterArguments` reordered the named arguments. `f(b = 2)` on `f(int a = 5, int b = 7)`
then gave `a` the default belonging to `b`: 72, where the interpreter answers 52. Only "too
many arguments" is checked here now; what is missing is bound by `RegisterArguments`, the same
code an interpreted function uses, which also reports an argument that has no default.

CSCS names are case-insensitive, and a member went into the generated C# exactly as the script
spelt it, so `m.keys`, `a.sort()` and `a.size` did not compile while `m.Keys` did. Every member
`Variable` provides is now written with its C# spelling wherever the member is emitted. Two
things followed from the same gap:

- A local holding what a call returned is a Variable local. `r = Regex(...); r.size` kept the
  script's case, and `r.contains("12")` took the *string* member mapping and came out as
  `ContainsCscs`, which `Variable` has no trace of.
- `type` was missing from the members `Variable` answers to.

Also compiling: JSON parsed into a collection (`GetVariableFromJSON`), a value through
`Marshal`/`Unmarshal`, a property assigned on an imported C# object, `Keys` sorted, and
`Math.Abs`/`Math.Max` together.

Left to the interpreter: `type` in lower case on the expression path (any capitalisation of
`Type` compiles), `Type` of a numeric local (a C# double has none), one collection plus
another (the interpreter answers 0), `StrEqual` and `StrContains` (no C# counterpart taking
those arguments), and an enum declared inside a function. Three defaults with one given by
name is an interpreter limitation -- it asks for every argument in `arg=value` form -- so
both sides fail alike.

## Across the boundary, and a return that does not return

Compiled and interpreted code call each other freely, and that now includes exceptions: a
compiled function throwing into an interpreted caller, and an interpreted function's throw
caught inside a compiled one. Two compiled functions calling each other recursively work as
well, as does handing a class instance to an interpreted function.

A `return` inside a `try` means something else in CSCS when the function also has a
`finally`: it runs the finally and **carries on with the statement after it**, so

```
cfunction int f(int n) { r = 0; try { return n * 2; } catch (e) { return -1; } finally { r = 99; } return r; }
```

answers 99, where C#'s `return` would leave with 8. Nothing in C# expresses that, so such a
function is refused and goes to the interpreter whole. The ordinary shapes are unaffected and
still compile: a return *after* the try/catch/finally, a return inside a `try` with no
finally, and a return inside a `catch` with one.

An argument declared `variable` is a Variable as much as a local holding one, so a field on it
-- `v.x`, `v.tag.Upper` -- reads through the same property lookup. Only the method form
(`v.Sum()`, which goes to the interpreter) worked before.

Also compiling: a ternary yielding collections, instances compared and iterated out of a
collection, iterating a map (which walks its **values** -- `m.Keys` is what gives the keys),
`~` and `&`/`^` on negative numbers, very large and very small numbers reaching text,
assignment inside a ternary branch, five levels of nesting, a local named after a function the
script defines, and a body with many locals.

Left to the interpreter: chained comparisons (`1 < n < 10`, which C# reads as bool against
int) and text times a number inside a compound assignment.

## References, and try inside a loop

CSCS collections and instances are references. A collection assigned to another name, reached
through a subscript or a map key, handed to a `foreach` variable, or passed as an argument is
the *same* collection: a change through either name shows through both. Scalars and text copy
instead. Compiled code holds the same `Variable` objects the interpreter does, so all of that
already held -- what is new is that the fixture pins it, since it is the kind of thing a later
change to the write-back or to argument preparation could quietly break.

`break`, `continue` and `return` inside a `try` or its `catch` compile, as does a `throw` from
inside a loop caught outside it. Also compiling: a method reading its own field without naming
it, three hundred levels of recursion, a twenty-element literal, and `Print` from a compiled
body.

### Three interpreter bugs found here, and fixed

Probing the translator turned these up in the interpreter itself -- they failed the same way
whether or not a function was compiled. Each one was first described wrongly from its symptom,
so the trigger is spelt out here:

- **An assignment after a `try`/`catch` failed** with "Can't process last token [2]".
  `ProcessTry` handed back the value of its block's last statement -- a number -- where
  `ProcessIf` and `ProcessWhile` return `Variable.EmptyInstance`, and that emptiness is what
  tells the parser the statement is over. So the parser stayed inside the try's expression and
  merged the next statement into it. It had nothing to do with loops or with the catch being
  empty (the first guesses): a `;` right after the `}`, or a call rather than an assignment,
  hid it. Both `ProcessTry` and `ProcessTryAsync` now end as the other block statements do,
  still passing a return, break or continue through.

- **`this.field` and `this.Method()` threw** "Sequence contains no elements". `this` was
  registered once, globally, as an empty array and never bound to anything, so a property
  lookup on it reached `First()` on an empty dictionary. `RegisterArguments` now binds `this`
  to the instance a method runs on, for the duration of the call.

- **A method calling a sibling method of its own class failed**, but only when the instance
  had been created inside a function. `GetObjectFunction` rewrote the bare name to
  `<instance name>.<method>` and resolved that *name* as a variable; an instance built inside
  a function is a local there and invisible from the lookup. It now rewrites to
  `this.<method>`, which is the instance itself. Sibling calls on an instance created at the
  top level always worked, which is why this looked at first like "sibling calls are broken".

## Known limits

- A loop counter that shares its name with a global stays local in compiled code, while the
  interpreted form leaves the global at the value the loop exited on. The write-back sits
  inside the loop body, so the exit value is not available to it.
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
- A truth value takes part in arithmetic, and that compiles. CSCS has no boolean type: a
  comparison yields the number 1 or 0, while a comparison-valued local is C# declared `bool`,
  so `b + 1`, `b * 2`, `b % 2`, `-b` and `a[b + 0]` did not compile. The operand is now
  converted to `(b ? 1 : 0)` where it sits next to arithmetic, inside
  `ReplaceArgsInString`'s funnel beside `AsDoubleNextToDivision`, which already sees the
  adjacent separators. Conditions, `&&`, `!` and `?:` want the bool itself and are left alone.

  **The two earlier attempts failed for a reason worth keeping.** They cost
  `int_arg_collatz` (`n = n % 2 == 0 ? n / 2 : 3 * n + 1`), `int_asg_while` and
  `assign_in_tern` (`y = n > 2 ? (x = 5) : (x = 7)`) -- and the cause was not the hook site
  but the *type record*: `AssignedValueType` asks `YieldsBool`, which merely looks for a
  comparison character anywhere in the value, so both of those ternaries were recorded as
  holding a bool when they hold numbers. A trace of `m_localTypes` showed it outright:
  `[c:double, n:bool]` and `[x:double, y:bool]`. `CollectLocalTypes` now records nothing for
  a value containing a top-level `?` (nor for widened int and collection arguments), and with
  that the coercion keeps every one of those constructs compiling.

  Still open, with their emitters identified: `t += b` gives `double += bool` and comes out of
  the plain assignment path (`lhs + tokens[1] + rhs`), not the `CscsConvert.Compound`
  builders; `b == 1` comes from the condition path; and `"v=" + b` still answers `v=True`
  against the interpreter's `v=1` -- a divergence -- because a statement holding a quote is
  rejected by `IsKnownExpression` and reaches neither funnel. `"t=" + x.Type` falls back for
  the same reason.
- **`c = a + b` on two collections compiles** (2026-09-12), and the two-part shape of the fix is
  the point. Joining two collections yields their *text* -- the interpreter answers `[1, 2][3]`,
  whose `.Type` is STRING, `.Length` 9 and `.Size` 0, and a pair of maps gives
  `["k" : 1]["j" : 2]`. Returning `a + b` directly always compiled, as did `"v=" + a` and
  `a + "!"`; it was the **assignment** that failed, with `CS0029: cannot convert Variable to
  double`, because the local was declared `double`.

  Two halves were needed, and **either one alone is worse than the fallback**:

  1. *The declaration.* At the assignment site,
     `result += YieldsBool(rhs) || rhs.Contains("__interpreter.GetVariableValue") || JoinsCollections(rhs) ? "var " : "double "`.
     `JoinsCollections` splits on a top-level `+` and asks whether any operand is a collection
     this function holds (`m_collectionLocals` or `m_collectionArgs`).
  2. *The recorded type.* `.Type` is answered at **translation** time out of `m_localTypes` (see
     `TryMapTypeMember`), so with only half 1 the compiled code returned the literal `NUMBER`
     where the interpreter says `STRING` -- four silent divergences, which is why the first
     attempt was reverted the same day. `CollectLocalTypes` now overrides the type to `string`
     when the value joins with a top-level `+` and an operand is already recorded `Variable`.

  **Why the override lives in `CollectLocalTypes` and not in `AssignedValueType`** (that was
  tried twice and was inert both times): `m_collectionLocals` is filled by
  `TryBuildLiteralAssignment`/`TryBuildTernaryLiteral` *while statements are processed*, which is
  after the collecting passes -- `CollectVariableLocals`, `DeclareBlockCrossingLocals`,
  `CollectLocalTypes` -- have all run. So no collection test can work inside that pass. What does
  work is the pass's **own** `agreed` dictionary: a `{...}` literal reaches it as the statement
  `a =` with an empty value, which `AssignedValueType`'s first branch types `"Variable"`, and the
  literals are always assigned before the join that reads them.

  It discriminates rather than stringing everything that adds: `v + w` and `a[0] + a[1]` stay
  NUMBER, a bare literal stays ARRAY, and the joins -- collection, map, three-way, and a
  collection beside a string on either side -- are all STRING, matching the interpreter in all
  eleven measured shapes. Fixture 617 -> 618 (`arr_plus_arr`), probes 627 -> 628.
- **A Variable or a bool as a `Math.*` argument compiles** (2026-09-12). `Math.Max(helper(n), 5)`
  emitted `Math.Max(CscsCalls.Call(__interpreter, "helper", __varInt[0]), 5)`, and a global read
  emitted `GetVariableValue(...)` -- both `Variable`. C# then finds no `Math` overload at all and
  reports the *first* one it has, which is why the diagnostics named `byte`, `short` and
  `decimal` rather than `double`: the target type in the message is noise, the cause is the
  argument type.

  **`ReplaceMathArgs` is the wrong place to fix it, and an edit there is inert.** It has a single
  call site -- the `m_knownExpression && tokens.Count == 1` branch -- and
  `ProcessReturnStatement` returns before that, so for `return Math.Max(..)` it is never entered
  at all. A trace printed nothing for a failing *or* a working shape. Wrapping arguments there
  changed not one row.

  The fix is `NumberMathArgs`, a late pass over the finished code beside `CastRoundDigits` and
  `MapChainedStringMembers` -- the same reason those exist: the operand path has no idea it sits
  inside a `Math` call. For each `Math.<Name>(` outside quotes it splits the arguments at the top
  level and wraps one in `CscsConvert.ToNumber` only when the generated text carries
  `CscsCalls.Call(`, `__interpreter.GetVariableValue(` or the shared Variable temp. `ToNumber`
  unwraps a `Variable` through `AsDouble` and converts anything else, so it is safe on every
  operand type.

  **Ordering matters.** It runs *before* `CastRoundDigits` and skips an argument that already
  carries `.AsDouble()`, `CscsConvert.ToNumber(` or a leading `(int)`, and for a two-argument
  `Math.Round` it wraps only the value -- never the digit count, which has to stay an `int`.
  Converting that to a double is precisely the `Round(decimal, int)` failure `CastRoundDigits`
  exists to prevent. All 17 fixture constructs calling `Math.*` still compile, `int_arg_round_expr`
  and `big_precision` among them.

  Ten shapes gained, each value-checked: `Math.Max(helper(n),5)` 6, `Math.Max(gcount,5)` 10,
  `Math.Max(helper(n),helper(1))` 6, `Math.Min` 5, `Math.Abs` 6, `Math.Round(helper(n))` 6,
  `Math.Pow(helper(n),2)` 36, `Math.Sqrt` 3, `Math.Max(helper(n)+1,5)` 7,
  `Math.Max(gmap["k"],5)` 5, plus the assignment form. Untouched and still compiling: an element
  (`Math.Max(a[0],5)` -- already read as a number), a scalar argument, a literal, an arithmetic
  expression, a mapped string member (`Math.Max(s.Length,5)`) and a nested `Math` call.

  **A truth value as the argument compiles too.** `b = n > 1; Math.Max(b, 5)` failed as
  `cannot convert from 'bool' to 'byte'`: the argument text is the bare name `b` -- the
  declaration `var b=__varInt[0]>1;` sits on an earlier line -- so the text alone cannot tell,
  and `YieldsBool("b")` is correctly false. `NeedsNumericArgument` asks `IsBoolLocal`, which
  reads the type `CollectLocalTypes` recorded for the name; that is the one reason this pass is
  an instance method rather than static, as `CastRoundDigits` is. Answers 5, as interpreted, and
  none of the fifteen bool constructs (`bool_arith_*`, `bool_compound_*`, `bool_eq_*`) moved.

- **`Substring` with computed arguments compiles, and so does assigning a string-returning call**
  (2026-09-13). `s.Substring(n - 1, n + 1)` was declined earlier because the generated code showed
  both arguments collapsed into one -- `new ParserFunction(..., "1,n", ...)` then
  `SubstringCscs(__varInt[0]-__varTempVar1+1)`. The statement tokenizer splits on operators but not
  on `,`, so the tokens were `s.Substring(n`, `-`, `1,n`, `+`, `1)`. `IsKnownExpression` could not
  place `1,n`, the statement went to the token loop, and that read `1,n` as a call name. A single
  computed argument, or a computed one beside a literal, always worked: `1,2` passed only because
  `Double.TryParse` reads `,` as a thousands separator.

  `IsKnownExpression` now judges each side of a top-level comma as its own token. The first version
  of that was inert -- each piece was judged alone, and the method answers "known *and* numeric",
  so an int argument by itself came back false. A trace of the token verdicts showed it; each piece
  is now judged beside an operator.

  That exposed a pre-existing gap one step further: a known expression declared `t = s.Substring(n
  - 1)`, `t = s.At(n - 1)` a `double` (CS0029). The declaration site now picks `var` when the value
  contains one of `s_stringResults`, the generated calls certain to return a string. Sixteen shapes
  value-checked, including a later `t = t + n` (`ello2`) and `t.Type` (`STRING`); the hazard forms
  -- `Math.Max(n - 1, n + 1)`, `helper(n - 1) + helper(n + 1)`, a literal `{n - 1, n + 1}`, a map
  argument -- still compile.

  **Beside a string literal too.** A quote makes the statement unknown, so
  `if (s.Substring(n - 1, n + 1) == "ell")` goes to the token loop, which met `1,n` as a token of its
  own; with no `(` of its own `ProcessFunction` built it as an interpreter call and the arguments
  collapsed again. `ProcessToken` now emits a token made only of plain operands either side of a
  top-level comma -- numbers, arguments, declared locals -- through `ReplaceArgsInString`. A quote,
  brace, bracket or call in it keeps the old path. Thirteen shapes value-checked: `==`, `!=`,
  concatenation, assignment, `Replace` beside it, arguments built from a local, and the keepers
  (`"m" + Math.Max(n - 1, n + 1)`, a mixed array, a map argument, two script calls).

- **A crossing local whose assignments disagree on a type compiles** (2026-09-12).
  `if (n > 0) { v = "text"; } else { v = 5; } return v;` fell back with
  `CS0103: The name 'v' does not exist`: `v` is block-crossing, but `DeclareBlockCrossingLocals`
  only declared a name when every plain assignment agreed on one C# type. Now a real
  disagreement declares it `Variable` and registers it in `m_variableLocals`. No assignment
  rewriting is needed -- the existing Variable-local machinery converts the stores, `v.Size` uses
  `Variable`'s members, `v + "!"` uses `operator +(Variable, string)`, and `v == "text"` goes
  through `TryRewriteStringComparison` -> `Variable.SameValue` (never an `==` operator, which
  recurses -- see above). Seven shapes, value-checked: both orders (`text`, `5`), the else path
  (5), `elif` (`a`), a collection branch (`.Size` 2), the concatenated read (`text!`), the `==`
  read (1). `scope_mixed_types` and six `cross_mixed_*` constructs now compile.

  **The first attempt aborted test.cscs and was reverted; the precise flaw is worth keeping.**
  It declared a `Variable` whenever `type == null` -- but `type == null` has two meanings. It
  means the assignments disagreed, *and* it means no plain assignment was found at all:
  `AssignedName("double result = 0")` is null because `"double result"` is not a plain name,
  and `result += x` is skipped as compound. `dllfunction RunCycle` in test.cscs is a C#-form body
  with exactly that shape, so the pass emitted `Variable result = null;` beside the body's own
  `double result=0;` inside `DoWork1` -- `CS0128: ... 'result' is already defined in this scope`,
  before the suite's first assertion. The kept version sets a `disagreed` flag in the one branch
  where two types really differ, and also requires `!m_scriptInCSharp`, since a C#-form body
  declares its own locals. Names with no plain assignment keep the old behaviour exactly.

  **Why no other gauge saw it:** the coverage fixture and the probe harness only compile CSCS-form
  bodies, one at a time. test.cscs is the only gate exercising `dllfunction`, so run it *first*.

- **A local re-assigned a different type after its first assignment compiles** (2026-09-12).
  `v = 0; if (n > 0) { v = "text"; } return v;` failed as
  `CS0029: Cannot implicitly convert type 'string' to 'double'`: the first assignment declared the
  local, and the later store could not convert. Two of the shapes never cross a block
  (`v = 0; v = "text"; return v;`), so the crossing fix above could not reach them.

  `DeclareBlockCrossingLocals` now makes a second pass over every plain-assigned local that is
  still undeclared: if its plain assignments really disagree on a type, it is declared
  `Variable ... = null;` at the top and registered in `m_newVariables` and `m_variableLocals` --
  the same treatment a disagreeing crossing local gets, and again with no assignment rewriting.
  Same exclusions (parameters, loop variables, collection and widened arguments, globals) and
  `!m_scriptInCSharp`. **Any name with a ternary assignment is skipped**: `AssignedValueType`
  reads `n > 2 ? 1 : 2` as a bool because `YieldsBool` only looks for a comparison character, so
  `v = 0; v = n > 2 ? 1 : 2` would otherwise count as a disagreement, become a Variable, and fail
  on the int store -- a regression of a shape that compiles today (`redecl_ternary_keep` pins it).

  Value-checked: number then text (`text`, or 0 when the branch is not taken), text then number
  (5, or `z`), the top-level pairs (`text`, 5), the loop (`s`), a collection (`.Size` 2).
  `cross_predeclared` and `loc_mixed_cmp` began compiling, plus `.Type` on a mixed local in two probe
  sets (`type_mixed_local`, `qt_mixed_type` -- both NUMBER, as interpreted). test.cscs, run first, stayed 534/534.

- **A ternary inside a collection literal compiles** (2026-09-12). `a = {n > 2 ? 10 : 20, 5}`,
  the ternary as the second or only element, string branches, a nested literal and a reassignment
  of the literal all compile, value-checked (15, 15, 13, `ac`, 4, 15). Two different failures had
  one cause: the ternary's `:` was read as a map key separator. With a second element the literal
  was never built (`CS0103: 'a' does not exist`); alone, it was built as the map entry
  `n > 2 ? 10` -> 20, which only failed to compile because `?10` is not a whole expression -- a
  ternary that happened to parse would have silently produced a map.

  `IsMapEntry` already existed for exactly this -- its comment quotes `{n > 2 ? 10 : 20, 5}` -- but
  only the `{`-token path in `ProcessFunction` called it. `TryBuildLiteralAssignment` and
  `TryBuildArrayLiteral` still tested `SplitTopLevel(element, ':').Count > 1`; both use
  `IsMapEntry` now. A real map literal still builds as a map. When a fix lands through a helper,
  grep for every other site doing the same test by hand. `m = {"k": n > 2 ? 1 : 2}` is not a
  construct: the interpreter itself throws "Unknown index [k] for tuple of size 1" on it.

- **A string or bool condition in `elif` / `else if` compiles** (2026-09-13). Every such `elif`
  fell back with `The name 'elseif' does not exist` -- a string argument, `<` on text, an
  element, a global, a bool local, and the `else if` spelling alike -- while a numeric `elif`
  compiled. Two small defects:

  1. `else if (...)` tokenizes as `else`, ` `, `if(...` and `ProcessToken` drops the lone space
     as whitespace, so the reserved-word branch wrote `else` and `if(` back to back. A numeric
     condition is a known expression and takes the branch that writes the statement whole, which
     is why only non-numeric conditions broke. The branch now writes `else ` with its space.
  2. `TryRewriteStatementComparison` handled `if`, `while` and `return` only, so an `elif`
     condition was never rewritten into `CompareCscs` / `SameValue`. It now takes `elif` and
     `else if` too, keeping the keyword as written so the later `elif` -> `else if` step applies.

  Found while testing the global-vs-text fix, whose `elif` form failed differently from its `if`
  form -- a trace of the token list (`else| |if(s|==|"ab")`) located it in one run. Twenty-two
  shapes checked, including nested `elif`, an `else` after it, `!=`, a call in the condition, and
  the branch not taken; the nine `elif` forms that compiled before still do.

- **Strict equality on an int argument or a numeric local compiles** (2026-09-13). `===` and
  `!==` are not C#; `TryRewriteStringComparison` rewrote them to `==` / `!=` only when both sides
  passed `IsNumericOperand`, which accepts an argument declared `double` but not `int`, and no
  local at all -- so `n === 5` over `int n`, `return n === 5`, `v === 6` over a numeric local and
  `n === m` reached C# verbatim (`CS1525: Invalid expression term '='`). `IsStrictNumeric` adds an
  `int` argument and a local `CollectLocalTypes` agreed is `double` (not a Variable or collection
  local). Still falling back, deliberately: an element (`a[0] === 5`), a string against a number
  (interpreted 0 -- the existing comment declines to fold that to a constant), and a local whose
  assignments conflict. An earlier note of mine recording `strict_eq` as compiled was a misread;
  it had been a fallback throughout.

- **A field written through a chain of instances compiles** (2026-09-13). `p.kid.v = 9` and the
  three-deep `p.kid.kid = new Named(..)` failed with two unrelated-looking error clusters
  (`CS1003`/`CS0128`, and `CS0131`) that were one cause. `TryBuildFieldAssignment` required a plain
  owner and a plain field, so a dotted target fell to the ordinary assignment path, which declared
  `double p.kid.v=9;`. The `new` form goes the same way, since `TryHoistNewInstance` rewrites it to
  `p.kid.kid = __newInstN` first. Now the middle segments build the owner as the same
  `GetProperty` chain a read of `p.kid.v` already used, and the last one is the `SetProperty`.

  The write reaches the live nested instance, not a copy -- measured, not assumed: read back
  directly (9), through an alias `q = p.kid` (9), with the parent untouched (109), accumulated in a
  loop (6), three deep (`zz`), and with a literal value. A Variable member in the middle
  (`p.tag.Size = 5`) still falls back.

- **A method called on what another method returned compiles** (2026-09-13). `p.Kid().Kid().v`
  failed with `CS1061: 'Variable' does not contain a definition for 'Kid'`: both builders of an
  instance call built only the first one. `ProcessFunction` appended the rest verbatim, and
  `ReplaceArgsInString` called `AppendMemberChainAfter`, which stops at a call, so `.Kid()` went out
  as ordinary text. Each builder now loops: member reads, then a further `.Method(args)` wraps
  everything since the chain began in `Variable.CallMethod(...)`; a Variable, string or
  collection member ends the chain as before.

  It took both. The statement-path loop alone was partly inert -- a control run on the previous
  build showed it had fixed the string and bare-return forms, while every chain inside arithmetic,
  a comparison or an assignment is a known expression and goes through the other builder. Twelve
  shapes value-checked: plain read (3), string field (`c`), arithmetic (32), condition (1),
  assignment (3), three deep (4), `while` (3), a field between calls (3), arguments, a string
  member at the end (2). A construct family with one builder per path needs the fix in each.

- **An enum declared inside a cfunction compiles** (2026-09-13). `var Local = Enum {X, Y};` arrives
  as four statements (`var Local = Enum`, `{`, `X, Y`, `}`), and each of `var`, `Local` and `Enum`
  became an interpreter call of its own, their temporaries glued into
  `__varTempVar1__varTempVar2`. `TryBuildLocalEnum` now recognises the four-statement shape -- only
  for the statement the loop is on, since the names come from the statements after it -- and builds
  the enum exactly as `EnumFunction` does: `new Variable(VarType.ENUM)` with `SetEnumProperty(name,
  new Variable(i))`. A member read goes through a `CscsEnums.Member` overload taking the local.

  **Only declared members compile, and the first version proved why that must be enforced on
  every path.** The interpreter's own answers are idiosyncratic: `Local.Type` is `NONE` (looked
  up as a member that does not exist) and `Local.Y.Type` is `Y`. The member rule was in place in
  `ResolveToken`, but a single-token `return Local.Type` goes through `ProcessToken`, where the
  local counted as a Variable and `.Type` became C#'s `Variable.Type` -- `ENUM`, a silent wrong
  answer the probe caught. `ProcessToken` now sends any token whose owner is a local enum through
  `ReplaceArgsInString`, so the one rule governs both paths. Value-checked: read 2, first/last 2,
  comparison 1, loop 6, assignment 2, concatenation `v=1`, two enums 12, negation -1, a
  parenthesised read, an `if` returning text; `.Type` and `.Y.Type` fall back.

- **A number as a whole condition, and an assignment in grouping parentheses, compile** (2026-09-13).
  `if (b)` over a double local, `if ((b = n + 2))` and `while ((t = t - 1))` failed with
  `CS0029: double -> bool`. The interpreter tests `Convert.ToBoolean(Value)`, which for a double is
  exactly `Value != 0` (NaN included), so `TryRewriteStatementComparison` rewrites such an `if` /
  `elif` / `while` to `(x!=0)` -- but only when the term is certainly a number (`IsNumericConditionTerm`):
  a numeric argument, a double local, or `(name = ...)` to a name typed double. A bool, a Variable
  or an element keeps its own handling; a string (always false there) keeps falling back.

  **A copy of another name is excluded, found by a regression.** `b = n > 1; c = b; if (c)` compiled
  before; the type record calls `c` a double because `c = b` copies a name, but C# holds a bool, and
  `c != 0` did not compile. A local assigned a bare name is skipped now, so `if (c)` goes back to the
  handling that already worked. (`r27` caught it; the fixture and suites did not track it.)

  The condition-assignment declaration pass also scans statements that are not conditions --
  `x = ((b = 7))`, `x = (b = n * 2) + 1` -- but **never inside a call's arguments**, and that rule
  came from a silent wrong answer: `Math.Max((q = n * 2), 5) + q` declared `q`, the math path did not
  carry the assignment out, and compiled code answered 5 where the interpreter says 9. Only grouping
  parentheses outside every call count now (a keyword's own condition parenthesis is not a call),
  and that form falls back again. `f(a = 1)` named arguments are never read as assignments. Still
  open: `return (b = n * 2) + b` and an assignment inside a call's arguments.

  **Beside `!`, `&&` and `||` too.** `if (!b)`, `if (b && n < 5)`, `b || n > 1`, two numbers joined by
  `&&`, `while (t && k < 10)` and `!n` on an int argument failed with `CS0023` / `CS0019`.
  `TryRewriteNumericTerms` splits the condition at the top level on `||`, then `&&`, and rewrites only
  the clauses `IsNumericConditionTerm` certifies -- `(x!=0)`, or `(x==0)` under `!`, which is the
  interpreter's rule ("`!x` is true only for a number that is zero"). Every other clause and the
  connectives stay as written, and when no clause qualifies nothing changes.

  **A clause whose type is settled when it runs joins in too**: an element, a local holding a
  Variable, a string -- `a[1] && b`, `a[0] || a[1]`, `v && n > 1`, `s && n > 1`, `!a[0] && n > 1`.
  Those must not become `!= 0`: the interpreter tests the numeric field, so an element holding
  `"5"` is false where `AsDouble()` would call it true. They are read through
  `CscsConvert.IsTrue` / `IsFalse`, and both names join the generated markers the token loop
  passes through.

- **A string as a whole condition compiles** (2026-09-13). `if (s)`, `if (t)` on a string local,
  `if ((t = s + "x"))` -- where the assignment still runs and only the test is false -- and the
  negated forms `if (!s)` and `!s && 1 == 1`. All are false in the interpreter, `"5"` included.
  Two things made this work. `AsCondition` already emitted `CscsConvert.IsTrue`, but that helper
  took a `Variable` while a compiled string term is a C# `string` (`CS1503`); **object overloads**
  of `IsTrue`/`IsFalse` fix that without changing what either means. And the clause rewriter now
  accepts a lone string term rather than only a joined one.

  **`IsTrue` and `IsFalse` are a pair, not one negated** -- the trap this change walked into.
  `!x` is true only for a *number* that is zero, so a string is false **both ways round**:
  `"5"` is false and `!"5"` is false too. Emitting `!IsTruthy(term)` for the negated case made
  `truthyCompiled` in test_compiled.cscs return 1110 where the interpreter says 1100. Keep the
  two helpers; a single truth test plus `!` is wrong for CSCS. A `Variable.IsTruthy` that invited
  exactly that mistake was removed again.

- **A numeric member as the whole condition compiles** (2026-09-13). `if (s.Length)`,
  `if (a.Size)`, the negated forms, joined with `&&`/`||`, in a `while`, and on a collection, a
  map, a call result or a local holding a Variable -- 11 shapes. CSCS reads `.Length` and `.Size`
  as numbers and C# as `int`s, and the two agree on every type: `Variable.Size` is 0 for anything
  but an array, exactly as the interpreter reports 0 for a string, and `Variable.Length` is
  `GetLength()`. A C# condition needs a `bool`, so these were `CS0029` until the numeric rewrite
  learned to certify them and read them as `!= 0`. `s.Size` on a string local has no such member
  in C# and still falls back, which is the right answer -- the interpreter says 0 there.

- **A member off a Variable-holding local as a condition compiles** (2026-09-13). `if (p.y)`,
  `if (p.x)` on a zero, the negated form, joined, in a `while`, and on a member holding text --
  which is false, like every string. The value is a `Variable` when it runs, so it goes through
  `CscsConvert.IsTrue`/`IsFalse` rather than a C# conversion (`CS0029`). The lone-term form needed
  the clause rewriter's "only when joined" gate widened as well as the term accepted: the joined
  form compiled first and `if (p.y)` on its own still fell back, the same two-paths lesson as
  before.

- **A method on a collection element compiles** (2026-09-13). `return a[1].Sum()`, the first
  element, one taking an argument, an argument or expression as the subscript, and one inside a
  loop. The expression builder has always built these -- as `Variable.CallMethod(a[0],"Sum")` --
  but its element branch runs only for a **known expression**, and that one word is the whole
  story: `return a[0].Sum() + a[1].Sum();` has an operator and qualified, while the lone
  `return a[1].Sum();` did not and went out as a C# `.Sum()` on a `Variable` (`CS1929`). A lone
  call now takes the same path with the flag set for that conversion.

  Keyed subscripts compile too -- `m["p"].Sum()`, a variable key, a map built by assignment --
  but only after the interpreter bug below was fixed. Until then this path was **restricted to a
  numeric subscript**, because with keyed reads allowed
  `m = {"p" : new Point(1,2)}; return m["p"].Sum();` answered 3 where the interpreter threw.
  `IsStrictNumeric` was what decided that, and it is worth remembering that it tests `VarType.INT`
  for arguments: asking `m_argsMap` for `VarType.NUMBER` instead missed every int parameter.

- **A member read off an element compiles** (2026-09-13). `a[0].x`, `m["p"].x`, a field on a
  class instance in a collection, and `.Size`, `.Length`, `.Type`, `.First`, `.Upper` -- 15
  shapes. Same cause as the element methods above, one layer along: the expression path already
  emitted `a[0].GetProperty("x")`, but only for a **known expression**, so `a[0].x + a[0].y`
  compiled while the lone `a[0].x` went out as C# and failed with `CS1061`.

  **A property written as a call compiles too** -- `a[0].Upper()`, `.Size()`, `.Length()`,
  `.First()`, `.Lower()`. The empty parentheses are dropped, because `Variable.Upper` is a
  property in C# (`.Upper()` was `CS1955`) and because the interpreter now reads the property and
  consumes the `()` itself. Only an **empty** pair, and only for the members that really are
  properties: `Sort`, `Replace`, `Contains` and the rest are methods that keep their call. The
  rule sits in one helper used by all three places that can meet it -- the token-loop guard, the
  element-member branch, and the member chain after a call -- because a rule that holds on one
  path and not another is how the enum-member divergence happened.

- **Interpreter bug found and FIXED** (2026-09-14), in `AssignFunction.ProcessObject`. **A write
  to a member through a subscript was silently lost.** `a[0].v = 9` left the element at its old
  value and raised nothing: the owner `a[0]` is not a variable of that name, so the lookup found
  nothing, a fresh Variable was made, the property set on that, and the result registered under
  the name "a[0]" -- a phantom beside the collection. Same for a map element and a nested one.
  `items[i].count = 5` is ordinary code, which makes this the most costly of the six.

  The element is a live reference inside the collection, so the fix resolves the subscript and
  sets the property on the element itself. One trap on the way: reading `b[0].v` **before**
  assigning made the fix throw `Object [v] doesn't exist`, because a `GetVarFunction` remembers
  the property it last read, and asking the collection for its value re-ran that `.v` -- on the
  collection, which has no such field. Taking the stored `Value` rather than calling `GetValue`
  avoids re-triggering it. That sequence is now the first assertion of the nine in `test.cscs`.

  Across 956 probes no interpreted value changed. Compiled code still refuses the shape
  (`CS0650`), so the two agree.

- **Interpreter bug found and FIXED** (2026-09-14): **an error thrown inside a class method read
  "One or more errors occurred. (message)"** wherever it surfaced: caught outside, uncaught, from an
  element (`list[0].Deposit(-1)`), inside an expression, rethrown, and in compiled code too. A plain
  function and a constructor were never affected. A method body runs asynchronously
  (`ClassInstance.GetProperty` → `CustomFunction.RunAsync`), and `Variable.GetProperty` waited for it
  with `task.Result`, which wraps any exception in an `AggregateException`. The interpreter then
  took *that* message as the script's error text.

  Every blocking wait on script code running through a class instance now uses
  `GetAwaiter().GetResult()`: method calls, `Variable.CallMethod` (the compiled-code path), custom
  property setters, object property reads, async class constructors, and the member-compound paths.
  It blocks the same way but rethrows the original exception. Nothing in the repository catches
  `AggregateException`. The debugger's `.Result` calls are untouched. `Interpreter.Run` has the same
  pattern, but it is host-facing API where the thrown type is part of the contract, so it is left
  alone deliberately. Across 972 probes no interpreted value changed. `test.cscs` pins seven call
  shapes, and test_compiled.cscs pins the compiled twin.

- **Interpreter bug found and FIXED** (2026-09-14), in `OperatorAssignFunction` and
  `IncrementDecrementFunction`: the **compound and increment forms through a subscript**.
  `a[0].v += 3` was a silent no-op and `a[1].v++` threw `Object [v] doesn't exist`, on array
  elements, map elements and nested ones alike. `Utils.GetArrayIndices` rewrites `a[0].v` to plain
  `a` and drops the `.v` entirely, so the operator applied to a copy that was written back to a
  phantom name.

  Both now resolve the target first, through one shared `TryResolveMemberTarget`, before
  `GetArrayIndices` can rewrite the name. It accepts `p.x`, `a[0].v` and `a[0][1].v`, returns the
  live element, and replaces the named-owner branches the earlier member fix had added to each
  function. The root is read through `GetVarFunction.Value` for the same stale-property reason as
  in `ProcessObject`. A control build showed that read-then-compound on a *named* owner
  (`r = p.x; p.x += 5`) already worked. Only the subscript form was broken. Across 961 probes
  exactly one interpreted value changed, `a[0].v += 3` going from 1 to 4. `test.cscs` pins the
  family with 10 assertions.

- **A member write through a subscript compiles** (2026-09-14): `a[0].v = 9`, `a[0].v += 3`,
  `a[0].v++`/`--`, a string field, an argument or expression as the index, a call as the value, a
  map element, and all of it inside a loop. Only reachable once the two interpreter fixes above
  landed. The value is worked out first and the subscript second, which is the order the
  interpreter uses: `a[i].v = (i = 1) + 10` writes `a[0]` on both sides. The element comes from
  `CscsConvert.ElementForWrite`. The DeepClone before `Compound` stays, since an unset field is
  still the shared class default.

  **`Variable.EmptyInstance` is a new object on every read** (`=> new Variable()`), not a
  singleton. The first version of `ElementForWrite` tested `ReferenceEquals(element,
  EmptyInstance)` to catch a missing element, which can never be true, so `a[5].v = 1` quietly
  wrote to a throwaway object and carried on, where the interpreter stops with `Unknown index`. It
  now repeats the interpreter's own bounds check from `ExtractArrayElement`, message included, and
  the three out-of-range probes throw identically on both sides. Never compare against
  `EmptyInstance` by reference.

  Kept on the interpreter, though the write itself compiles: functions that also *read* an element
  member in a shape the reader does not handle yet, i.e. a nested `e[0][0].v` or an assignment
  `r = a[0].v`.

- **Interpreter bug found and FIXED** (2026-09-14), in `OperatorAssignFunction.ProcessOperator`
  and `IncrementDecrementFunction.ProcessAction`. **A compound assignment to a member did
  nothing, silently.** `p.x += 5` read the field correctly and then wrote the result back with
  `AddGlobalOrLocalVariable` under the dotted name -- creating a variable called "p.x" beside the
  object. The field never moved and nothing failed. Where that name had not been written before,
  the read found nothing either and it threw `Object [p.y] doesn't exist`, so the same defect
  showed as a no-op or as an error depending on what had run before it.

  The same hole ran through the family: `p.x++` and `p.x--`, `this.x += 2` inside a method
  (threw), and the implicit `x += 3` on a field inside a method (**silently updated a local**).
  All of them now read the property, apply the operator and set it back, the way
  `AssignFunction.ProcessObject` handles the plain `p.x = 9` -- which always worked, and is what
  made the gap so easy to miss. A method's own locals are untouched: the instance path is taken
  only when `PropertyExists` says the field is real.

  Across 921 probes no interpreted value changed. `test.cscs` pins it with 16 assertions.

- **A compound assignment to a member compiles** (2026-09-14), and so do `p.x++` / `p.x--`.
  `+=`, `-=`, `*=`, `/=`, `%=`, a string field, a field plus a number, an argument, a call or an
  element as the value, and the whole thing inside a `for` or `while`. C# has neither operator for
  a member of a `Variable` (`CS1061`/`CS1059`), so each becomes the read-apply-write the
  interpreter does, through the same `CscsConvert.Compound` helper an element compound already
  used: `p.SetProperty("x", CscsConvert.Compound(p.GetProperty("x").DeepClone(), 5, "+="), null)`.

  **That `DeepClone` is the whole subtlety.** `GetProperty` hands the field back **by reference**,
  and for a field the constructor never sets, that reference is the class's own default, shared by
  every instance. `Compound` mutates what it is given, so without the clone the default itself
  moved: `p.tag += "z"` over three calls read back `tzzz` -- and the same value from all three,
  because each returned Variable aliased that one default. The interpreter clones for exactly this
  reason. **No probe could see it**: each probe calls its function once. It took the regression
  suite, which calls the compiled function and its interpreted twin in turn, to expose it.

  Kept on the interpreter: the prefix form `return ++p.x` (worth the field's new value, which the
  statement shape does not produce), a member of an element (`a[0].v += 3`), and the deeper
  `p.kid.v += 1` -- which the interpreter itself still refuses, so both sides throw alike.

- **A truth-valued assignment group beside arithmetic compiles** (2026-09-14).
  `return (b = n > 2) + b`, the false case, `* 5`, and the same group in a plain assignment. The
  group is 1 or 0 in CSCS but reaches C# as a `bool`, and `bool + int` does not compile
  (`CS0019`). The bare `b` next to it was already read as a number; the group is not a single
  token, so there was nothing for that rule to convert.

  Hoisted instead, into a statement of its own -- the assignment runs first either way, which is
  the order CSCS evaluates in -- leaving a plain name the bool-to-number rule then handles. Three
  details had to be right, each found by reading the generated C# rather than reasoning about it:
  the hoist belongs in `ProcessStatement`, which has the statement text (joining the token list
  back together produced `return(b=(b=n>2)+b`, since that list is not a plain split); the name
  needs a **leading** space, or `return(b=n>2)+b` becomes `returnb+b`, one token and a callback to
  a function called `returnb`; and it must have **no trailing** space, because the bool-to-number
  rule compares the resolved token against its own trimmed text, and `"b "` is not `"b"`.

  Kept on the interpreter: two groups in one expression (`(b = n > 2) + (c = n > 1) + b + c`),
  where only the first is hoisted and the second stays a C# bool.

- **A switch with consecutive case labels compiles** (2026-09-14). `case 1: case 2: return 12;`,
  three labels in a row, string labels, a mix of single and grouped labels, a grouped clause with
  a body and a `break`, and `case 1: default:`. The labels arrive on one line, so the second was
  left inside the first clause's body and went out as a C# `case` in the middle of an `if`
  (`CS1003`).

  Each label now starts its own clause with an empty body, which needed no new machinery: an
  empty clause is exactly what fall-through already is in this translation -- the first sets
  `__swMatch` and the next clause's body runs. Real fall-through, where the first clause has a
  body of its own and no `break`, compiles and answers as the interpreter does.

- **Interpreter bug found and FIXED** (2026-09-13), in `AssignFunction`. **`a[0] = a[1] = 7`
  overflowed the stack and killed the process.** An assignment to an element returned the
  *collection* rather than the value assigned, so `b = a[1] = 7` set `b` to the whole `[1, 7]`,
  and chaining stored the array inside its own element. That cycle made the next `AsString`
  recurse until the stack died -- about 7569 frames, and a stack overflow cannot be caught, so it
  took the whole process with it. Plain `x = y = 7` and `a[0] = 7` were always fine.

  Both the sync and async paths ended `ExtendArray(...); return array;` where every other
  assignment path returns `varValue.DeepClone()`; they now do the same. Across 896 probes no
  interpreted value changed -- nothing had used an element assignment as a value, which is why
  this survived so long. `test.cscs` pins it with 9 assertions, and a return of the bug shows up
  as the whole file crashing rather than one failure.

  Compiled code refused these shapes cleanly at first (`CS0131`/`CS0200`: a `Variable`'s indexer
  is read only) -- and **they compile now**, see below.

- **A chained assignment onto elements compiles** (2026-09-13). `a[0] = a[1] = 7`,
  `b = a[1] = 7`, a map (`m["a"] = m["b"] = 3`), a three-target chain, an argument or expression
  as the index, nested targets (`e[0][1] = e[1][0] = 5`), a variable key, a global collection, and
  the whole thing inside a loop. Only reachable once the interpreter stopped returning the
  collection from an element assignment -- before that these shapes crashed the interpreter, so
  there was nothing to match.

  No new machinery: `TryBuildChainedAssignment` already unrolled `x = y = 7` into one statement
  per target, writing the innermost first and then giving each target the one to its right, which
  is the right-to-left order CSCS assigns in. It simply required every target to be a plain name.
  Allowing an element target is the whole change.

  Kept on the interpreter: an index that can change something (`a[i++] = a[0] = 7`), because the
  unrolling mentions each target's index twice -- once as a target, once as the value beside it.

- **The same chain onto members compiles** (2026-09-14). `p.x = p.y = 5`, `v = p.y = 6`, a member
  beside an element (`a[0] = p.y = 4`), two objects, three targets, a string member, and the whole
  thing in a loop. `p.x = 9` and reading `p.x` both compiled already, so the unrolling had
  somewhere to land; only the target test needed widening.

  The owner must be a plain name, for the same reason an index must be inert: the unrolling
  mentions each target twice. So a deeper member (`q.kid.x`) and a member of an element
  (`a[0].v`) are left to the interpreter, both falling back cleanly.

- **Interpreter bug found and FIXED** (2026-09-13), in `Parser.CheckConsistencyAndSign`. **A map
  literal whose value is a `new` was not a map.** `m = {"p" : new Point(1,2)}` came out as a plain
  one-element tuple, so `m["p"]` threw "Unknown index [p] for tuple of size 1", while the same map
  built by assignment worked and `{"p" : 5}`, `{"p" : f(3)}`, `{"p" : {1,2}}` were all fine.

  The cause is a one-line heuristic: reaching a `CONTROL_FLOW` token with cells already collected
  clears them, on the guess that a `;` was forgotten. `NEW` is in that list -- and it is the only
  entry that can legitimately follow other tokens, since `x = new Point(1,2)` and
  `{"p" : new Point(1,2)}` are ordinary expressions. So on reaching `new` the parser threw away
  the `"p" :` it had already collected, and the entry lost its key. That also explains why
  `{"a" : 1, "p" : new Point(1,2)}` half worked: each entry is parsed by its own call, so only the
  `new` one lost its key, and `TrySetAsMap` decides whether the whole literal is a map by looking
  at **the first element only**.

  The fix excludes `new` from that clearing rule. Across 874 probes no interpreted value changed,
  `test.cscs` pins it with 8 assertions, and the precompiler restriction above was lifted
  afterwards -- the same order as the `s.Upper()` fix: correct the interpreter, then compile the
  shape.

- **Interpreter bug found and FIXED** (2026-09-13), in `Variable.GetCoreProperty`. **A property
  written as a call left its `()` behind.** `s.Upper()` on its own looked right, but
  `s.Upper() == "AB"` was **false**, `s.Upper() + "!"` threw `Couldn't find variable []`, and
  `s.Upper().Trim()` threw `Couldn't find function [.Trim]`. It was never about `Upper`: every
  value property broke the same way -- `s.Size()`, `s.Length()`, `a.Size()`, `a.First()`,
  `a.Length()`, `s.Lower()`. Even the standalone form was quietly wrong, passing `print` an extra
  empty argument.

  The properties that take arguments consume their own list -- `Substring` and `IndexOf` read it,
  `Sort` and `Reverse` call `GetFunctionArgs` purely to eat it -- but a value property never did.
  The token loop has already taken the `(` as the action character that ended the name, so what
  waits at the pointer is the argument list itself; left there, the empty group was parsed as an
  expression of its own. The fix consumes it, guarded twice: `script.Prev == START_ARG` says the
  list is this property's (in `print(s.Upper)` the pointer also sits on a `)`, but that one
  belongs to `print`), and only an **immediately empty** pair is taken. The second guard was not
  optional -- consuming unconditionally ate `Replace`'s two arguments and aborted
  test_compiled.cscs with "Expecting 2 arguments but got 0 in replace". A third guard skips the
  case where the name was not a property at all, so a class method's own `()` is left alone.
  **Correction (same day):** as first written, that third guard compared the result against
  `EmptyInstance`, which is a fresh object on every read, so it passed every case and did
  nothing. A name the lookup does not know actually comes back as `null`, because the failed
  `TryGetValue` overwrites the default, so the guard now tests `!= null`. Until then the empty-pair
  guard alone was protecting `Replace`'s arguments. Method calls with parentheses behaved the same
  before and after the correction.

  Across 857 probes **no interpreted value changed**: the fix only turns shapes that threw into
  shapes that work. `test.cscs` pins it with 14 assertions.

- **`s.Upper()` compiles** (2026-09-13), once that fix landed. The member form `s.Upper` always
  did; the call form came out as `.ToUpper()()` (`CS0149`) because the mapping added a pair of
  parentheses while the caller was already copying the script's own through. Threading a
  "a call opens right after this token" flag from `ResolveToken` through
  `ProcessArray`/`ProcessStringMember`/`MapStringMember` fixes that, and now that the interpreter
  agrees, `s.Upper()`, `s.Upper() == "AB"`, `s.Upper().Trim()` and `s.Lower()` all compile.
  **This exact change was written and reverted an hour earlier**: with the interpreter still
  leaving the parentheses behind, it made two probes diverge. The order mattered, not the patch.
  `s.Trim` (the member form of a method the interpreter does not trim there) still falls back.

- **An assignment inside a `return` compiles** (2026-09-13). `return (b = n * 2) + b`, the bare
  `return (b = n * 2)`, `* b`, the string form, two of them in one expression, a doubled
  parenthesis, and one with a plain assignment before it. The declaration pass that already
  handled `while ((x = n - t) > 2)` was skipping these: whitespace is stripped by the time it
  runs, so `return(b=n*2)+b` is shaped **exactly like a call** to a function named `return`, and
  the guard that refuses assignments inside a call's argument list swallowed it. A keyword's own
  parenthesis is now found by looking at what directly follows the keyword, rather than taking
  the first `(` in the statement -- in `return helper((q = n * 2))` the first one is the call's,
  and that one must keep counting as a call.

  Kept on the interpreter: `return (b = n > 2) + b` (a C# `bool` plus an `int`, `CS0019`) and
  `return helper((q = n * 2)) + q`, where the call builder does not carry the assignment out of
  the argument list and emits malformed C#.

- **Interpreter bug found and FIXED** (2026-09-13), in `ReturnStatement.Evaluate` /
  `EvaluateAsync` (`Functions.Flow.cs`). Inside a block the interpreter **truncated a return whose
  expression opens with a parenthesised group**: `if (n > 0) { return (n * 2) + n; }` answered
  **6**, not 9, and `(s + "x") + "y"` gave `ax`. Nothing had to be assigned for it to happen.

  The cause is a method whose name reads like the opposite of what it does.
  `script.FromPrev(Constants.RETURN.Length)` was meant to ask "is the pointer just past the word
  `return`?", and the answer decided whether to step back over the character the dispatcher had
  eaten. But `FromPrev(backChars, maxChars)` starts `backChars` before the pointer and then reads
  **forward** up to `maxChars`, which defaults to ~40 characters -- so `Contains("return")` was
  satisfied by the *next* `return` in the function. The step back was skipped, parsing began
  **inside** the group, and it ended at the `)`, which is one of `NEXT_OR_END_ARRAY`.

  That is why the oddities lined up: correct at the top level and with the group later in the
  expression (the pointer was already right), correct in an `else` and when nothing follows the
  block (no second `return` within reach), wrong everywhere a later `return` sat in the window.
  The fix passes the length twice -- `FromPrev(RETURN.Length, RETURN.Length)` -- so only the six
  characters before the pointer are read.

  **It had been a silent divergence**: compiled code answered 9, the arithmetic, and a control
  build confirmed it predated the coverage rounds. `RefuseTruncatedGroupReturn`, written earlier
  the same day to refuse the family, is gone again, and the four shapes compile. Across 837
  probes exactly four interpreted values changed -- the ones meant to. `test.cscs` pins the
  interpreter side with 11 assertions (`retGroupIf`, `retGroupWhile`, `retGroupNested`,
  `retGroupFor`, `retGroupElse`, `retGroupStr`, `retGroupAsg`, and the three shapes that always
  worked), and test_compiled.cscs pins that the compiled side agrees.

- **Known limit: chained comparisons** (`1 < n < 10`). Seven shapes, all clean value-matching
  fallbacks, and the semantics reward care rather than a quick rewrite: with `n = 5`, `1<n<10`
  is 1, `20<n<10` is **1**, `1<n<0` is **0**, `1>n>10` is 0, `n<10<20` is 1, `1<n==1` is 1 and
  the four-way chain is 1 -- left-to-right, each comparison collapsing to 1 or 0 before the
  next. C# rejects the shape outright (`bool < int`), so today it falls back cleanly; a
  mistaken rewrite would answer differently instead, which is worse.
- **An assignment inside a condition compiles** (2026-09-13). `while ((x = n - t) > 2)`,
  `if ((y = n * 2) > 5)`, the `!=` form, a string (`(t = s + "!") == "a!"`), both used-after forms,
  a nested pair, and the `elif` form -- the seven shapes the half-fix left waiting, plus `&&`.
  After the earlier `keywordStatement` guard these failed only with `CS0103`, because nothing
  declared the name: it is not a statement of its own, so `AssignedName` never saw it, and the
  form declared beforehand always compiled.

  A third pass in `DeclareBlockCrossingLocals` scans `if` / `while` / `elif` statements (outside
  quotes) for `(name =` where the `=` is not part of `==`, takes the value up to that
  parenthesis's matching `)`, and declares the name at the top with the type every assignment to
  it agrees on -- the condition's and any plain ones. It refuses, and so leaves a clean fallback
  for: a ternary value (`YieldsBool` misreads it), a `Variable`-typed value, a disagreement
  (`if ((v = n * 2) > 5) { v = "big"; }`, tracked as `cond_asg_conflict`), and a **truth value**.

  The bool refusal is deliberate parity: `if ((b = n > 2))` throws a NullReferenceException in the
  interpreter itself. Declared as a bool, compiled code answered 1 -- a better answer, but a
  different one from the reference, so it stays on the interpreter's path.

  **That interpreter crash is fixed (2026-09-13), and the refusal is lifted**: a bool condition
  assignment compiles as a bool local, and its declared type is recorded in `m_localTypes` so a later
  `b + 10` gets the bool-to-number conversion (11, as interpreted). `if`, `while`, `elif`, `else`,
  `==` and `&&` values, and the branch not taken, all match. The cause of the crash was not `if` at all: `AssignFunction.Assign` read its value with
  `Utils.GetItem(script)`, whose default `eatLast` consumed a second `)` after the one `Split`
  already took, and `MoveBackIfPrevious` gave back only one. Whenever an assignment was the whole of
  a group followed by another `)` -- `if ((b = 5))`, `while ((b = t < n))`, `f((q = n * 2))` -- the
  group closed on the outer parenthesis and the condition swallowed the rest of the function;
  `ProcessIf` then dereferenced a null block. Any other character after the group (`== 1`, `;`)
  left nothing for `eatLast`, which is why those forms worked. Fixed by reading the value with
  `eatLast: false` in `Assign` and `AssignAsync`.

- **A global as the right-hand operand of `==`/`!=` compiles** (2026-09-12). The tokenizer
  splits the statement on the operator and leaves the condition's closing parenthesis glued to
  the operand, so `if (1 == gcount)` handed `ProcessFunction` the token `gcount)`. With no `(`
  of its own (`paramStart < 0`) the whole token was taken as a function name and emitted as a
  call -- the generated code contained
  `new ParserFunction(__scriptTempVar, "gcount)", '(', ref __actionTempVar)` plus a hoisted
  `Variable __varTempVar1`, and the condition went out as `if(1==__varTempVar1` without its
  `)`: `CS1026: ) expected`. Relational operators reach a different path, which is why
  `5 > gcount` and `gcount == 1` always compiled while `1 == gcount` did not.

  The fix is a guard at the top of `ProcessFunction`, before `argsStr` is built: when
  `paramStart < 0` and the name ends in `)`, strip the unbalanced parens with
  `SplitClosingParens` and, if what remains is a plain name that the interpreter knows as a
  *variable* and not as a function, emit `__interpreter.GetVariableValue("<name>")` followed by
  those parens. A genuine call can never enter it -- every call has a `(` of its own -- and a
  name the interpreter holds as a function is left to the call path, so `15 == helper(n)` and
  `build().Size` are untouched.

  Eight shapes gained, each value-checked against the interpreter: `1 == gcount` 0,
  `99 != gcount` 1, `gcount == gcount` 1, `n == gcount` 1, `v == gcount` 0,
  `((1 == gcount))` 0 (two unbalanced parens -- `SplitClosingParens` strips only those),
  `while (t == gcount)` 0, `return 1 == gcount` 0. They were **untracked** by the fixture and
  the probe collection -- the only global-equality construct was `global_eq_num`
  (`gcount == 1`), which already compiled -- so the gauges did not move when this landed; the
  eight are now in both, plus 17 assertions in test_compiled.cscs.

  Diagnosis notes, so nobody repeats the wrong turns: `GetCSCSVariable` is not the emitter (its
  only caller is the indexed-assignment path); `HoistConditionCalls` is not either (it requires
  a `(` after the name, and a trace proved `gcount)` never reaches it); and `ResolveToken`'s
  bare-global branch never sees the token at all (traced -- the only names it saw were `if` and
  the preamble's own functions), so patching that branch would have been inert.

  **`null` as an operand compiles too, and the divergence is gone** (2026-09-13). The interpreter
  compares `==` as text whenever the sides are not both numbers (`Parser.MergeCells`), and `null`
  is `Variable.EmptyInstance`, whose text is `""`. So `v == null` means "renders as the empty
  string": true for a null local, `""`, a map value of `""` and an empty argument; false for `0`,
  `{}`, `"gs"`, `10`. `TryRewriteStringComparison` now emits `Variable.SameValue(v,"")` when exactly
  one side is the literal `null` -- the same comparison, step for step. Mapping `null` to `0.0`, the
  hazard noted here before, would have got every `0` row wrong. This removed `null == x`
  (interpreted 1, compiled 0 as a C# reference test) -- **the last BROKEN row in the probe set** --
  and compiled `x == null`, `m["a"] == null`, `y != null` and 19 further shapes. `null == null`
  stays on the interpreter: `SameValue` answers false for a C# null.

  **Known limit, and an operator is NOT the way to fix it:** `"gs" == gstr` no longer loses its
  paren but fails as `CS0019: Operator '==' cannot be applied to operands of type 'string' and
  'Variable'`. Eight shapes ride on it, all answering 1 interpreted: `"gs" == gstr`,
  `gstr == "gs"`, `gstr != "zz"`, `s == gstr`, `gstr == s`, the `&&` form, the `return` form and
  the ternary. Tracked as `glob_eq_str_lit`.

  Adding `==`/`!=` for `(Variable, string)` and `(string, Variable)` -- mirroring the `<`, `>`,
  `<=`, `>=` pairs that already take a string -- **crashes the interpreter with a stack
  overflow** (tried and reverted, 2026-09-12):

  ```
  op_Inequality(Variable, String) -> Compare -> BothNumbers -> op_Inequality(Variable, String)
  ```

  repeated 22,713 times. The mechanism is `null` itself: `BothNumbers` is

  ```csharp
  return left != null && right != null && left.Type == VarType.NUMBER && ...
  ```

  and `null` converts to `string`, so with a `(Variable, string)` operator in scope
  `right != null` stops being a reference test and resolves to the new operator, which calls
  `Compare`, which calls `BothNumbers` again. Every gauge died: all fifteen string nets, the
  whole probe collection, and **both regression suites failed to finish** -- the only change all
  session that broke the interpreter rather than a compile.

  So this is not merely similar to the `(Variable, Variable)` prohibition documented beside those
  operators, it is the **same** hazard: any overload whose second parameter accepts `null`
  captures every `Variable != null` in the codebase -- 270 `!= null` sites and 229 `== null`
  ones, 72 and 45 of them inside Variable.cs itself. Only
  `(Variable, double)` / `(double, Variable)` are safe, because `null` does not convert to
  `double`. The ordering operators (`<`, `>`, `<=`, `>=`) take a string safely for the same
  reason in reverse: nothing writes `variable < null`.

  The only safe route for these eight shapes is to make the **translator** emit
  `Variable.SameValue(left, right)` -- which already exists, already applies the interpreter's
  rule, and is already what a switch label uses -- rather than giving `Variable` another
  operator.

  **Landed that way (2026-09-13).** `TryRewriteStringComparison`'s `==`/`!=` branch now has one
  more case: a plain-name global on one side (`IsInterpreterVariable`) and `IsStringOperand` on
  the other -- a literal, a string argument or a string local -- becomes
  `Variable.SameValue(left,right)`. Restricted to a string on the other side, so a global beside a
  number keeps the numeric operator it already compiled through. All eight shapes compile and
  match; case still matters (`gstr == "GS"` is 0, `!=` is 1), `gcount == "10"` is 1 as
  interpreted, and a `while` with `&&` works. `glob_eq_str_lit` and seven `glob_str_*` constructs
  compile, plus six probes in unrelated sets.
- A Variable-valued expression compared with a number compiles: `Colors.Green == 1`,
  `Colors.Green != 0`, the same inside `&&`, `return Colors.Green == 1`, and `gcount == 1` on a
  global. `Variable` had `<`, `>`, `<=`, `>=` against a `double` and a `string` but **no
  equality operators at all**, and `Compare`'s switch had no `==`/`!=` cases -- its `default`
  answered `>=`, so routing equality through it would have made `==` true for anything. Both
  were added together (`cscs/Variable.cs`).

  **Only against a number, never `(Variable, Variable)`.** `Compare` itself tests
  `left == null`, and the codebase has some 207 `== null` checks on Variable-typed names: an
  overload for two Variables would reroute every one of them from a reference test into value
  comparison, recursively in `Compare`'s own case. Two Variables therefore keep reference
  equality, exactly as before. The build produces no CS0660/CS0661, so no `Equals`/`GetHashCode`
  work is implied.

  Not fixed by this, and a separate defect: `1 == Colors.Green` and `Colors.Green ==
  Colors.Green` still fail with `CS1026: ) expected`. That is a **paren-loss at emission**, not
  an operator problem -- `1 == gcount`, `gcount == gcount` and the whole `null_*` family share
  the signature, while `1 == helper(n)` and `1 == n` compile. One fix there would likely cover
  four tracked shapes.
- A member of an enum the interpreter holds compiles: `Colors.Green + n`, `Colors.Blue`,
  `Colors.Blue > Colors.Red`, `c = Colors.Green`, `Colors.Blue * 2`, and two reads in one
  expression. An enum is a `Variable` of type `ENUM` whose member names live in its own map, so
  no generated C# name can stand for `Colors.Green` -- it used to reach C# verbatim as
  "The name 'Colors' does not exist". The member-on-global branch could not help: it fires only
  for a member `Variable` itself has, and `Green` is not one. `CscsEnums.Member` now reads it
  through the interpreter, and the translator routes a member of an **enum-typed global** there
  (`IsEnumGlobal`).

  The helper builds a throwaway `ParsingScript`, which is safe for a reason worth writing down:
  `Variable.GetEnumProperty` dereferences the script only to spot the call form `Colors(x)` --
  it tests `script.Prev`, and a freshly built script answers `Constants.EMPTY` there, so a plain
  member read falls through to the name lookup. Checked against the interpreter before the edit:
  `Red` 0, `Green` 1, `Blue` 2.

  Two shapes still fall back, and the reason changed with this work: `Colors.Green == 1` and the
  same inside `&&` now fail as `Variable == int` rather than an unknown name, because the helper
  returns a `Variable` and a comparison with a number needs `.AsDouble()`. Also still falling
  back: `enum_in_cf_local` -- an enum *declared inside* a compiled function mangles the
  temporaries (`__varTempVar1__varTempVar2`), which is a separate defect. Note `Colors.Green.Type`
  answers `Green`, not `NUMBER`, so an enum member must never be routed through the `.Type`
  mapping.
- A built-in call as the whole condition compiles: `if (StrEqual(s, "AB"))`,
  `if (StrContains(s, "ELL"))`, `if (Contains(a, 2))`, `if (NameExists("x"))`, and
  `if (helper(n))` with no comparison after it. `HoistConditionCalls` used to hoist only a
  function a *script* defined, so a built-in registered in C# stayed a bare name and did not
  compile. It now hoists any name the interpreter knows, with two guards that are the whole
  difference between this working and the earlier attempt that cost fourteen constructs:
  - **A member call is never hoisted.** `s.Contains("BC")` and `a.Contains(2)` compile as
    members already, and the interpreter answers the two forms *differently* --
    `Contains(a, 2)` is 0 where `a.Contains(2)` is 1 -- so the distinction is semantic.
    Hoisting the free form to an interpreter callback is what keeps its own answer.
  - **The hoisted temporary is tested with `CscsConvert.IsTrue` only when the call is the
    whole condition**, and never when the statement holds a comparison. The tokenizer splits
    on the comparison, so `if (f(n) < "t")` arrives as `"(f(n)"` with the `< "t"` nowhere in
    the text -- measuring what this method receives cannot see it, and wrapping the value then
    put a `bool` where a string comparison wanted the string (it cost `callres_str_lt`).
    `m_statementRelational`, computed for the whole statement before it is torn up, is what
    makes the difference visible.

  Still falling back in this family, all of them fallbacks before this change as well:
  `!helper(n)` and `!StrEqual(...)` (`!` on a double or a Variable), `StrEqual(...) == 1`, and
  a hoisted built-in inside `&&`.
- A truth value beside a compound operator or compared with a number compiles, and
  `Math.Round` accepts a computed digit count. Three shapes that each needed the operator's
  own context:
  - `t += b` was `double += bool`, and `b == 1` / `b != 0` were `bool == int`. The right-hand
    side reaches the operand funnel as a bare token with **no** adjacent separator, so the
    conversion there cannot see the context; the assignment emission site is the only place
    `lhs`, the operator and `rhs` are visible together, and `AsCscsNumberBeside` converts
    whichever side is a bool there. Not when **both** sides are bools (`b == c` compiles and
    already agrees with the interpreter), and only for the arithmetic compound operators --
    `OPER_ACTIONS` also holds `->` and `:`.
  - `Math.Round(x, d)` and `Math.Round(x, k + 1)` reached C# with a `double` digit count, so
    the compiler chose `Round(decimal, int)` and refused both arguments. The argument is
    emitted by the general operand path, which has no idea it sits inside a Round, so
    `CastRoundDigits` fixes it over the finished code the way `MapChainedStringMembers` does.
    Round only and two arguments only: `Pow`, `Max`, `Min` and `Atan2` take two doubles and
    `Round(x)` has no count to cast. Verified against a nested first argument, an expression
    first argument, two Rounds in one expression and a Round inside a concatenation.

  Still falling back, all untracked by the fixture and the probe collection: `return b == 1`
  (a return never reaches the assignment site), `1 == b` and `1 != b` (a number on the left),
  and `b += c` / `b += 1` where the target itself is a bool.
- `.Type` beside a string literal compiles too, which is a different path from the plain
  `return x.Type;`. A statement holding a quote is rejected by `IsKnownExpression`, so it
  never reaches `ReplaceArgsInString`; it is emitted token by token by `ProcessToken`, whose
  last branch copies a local through verbatim. `"t=" + x.Type` therefore reached C# unchanged
  and did not compile, while `"t=" + m.Type` did -- a collection is a `Variable`, which has
  that member. The same branch now answers `.Type` through `TryMapTypeMember`, covering a
  number, a string, a truth value, an argument, an element, a map and lower-case `type`.

  **That branch serves every bare local in a quoted statement, so it is a careful place to
  touch.** Coercing bools there as well -- for the `"v=" + b` divergence below -- was tried in
  the same edit and cost four constructs (`scope_bool_later`, `bool_flag`, `bool_and_chain`,
  `bool_assign_bool`): each *assigns* or *tests* a truth value (`ok = n > 2`, `found = true`,
  `c = b`, `return a && b && c`) where C# needs the bool itself, and the conversion was applied
  with a faked arithmetic context. Bisecting the two halves showed the `.Type` half is clean on
  its own. A bool conversion here has to require a real `+` among the neighbouring tokens.
- `.Type` on a local compiles, in either case. The interpreter answers with the name of the
  CSCS type, and a C# `double`, `string` or `bool` has no such member -- so `x = n + 1;
  return x.Type;` used to fall back, as did `a.type` in lower case on a collection. The
  translator now records, for every local, the C# type all of its assignments agree on
  (`CollectLocalTypes`, reusing `AssignedValueType`) and writes the answer out: `"NUMBER"`
  for a number or a truth value -- a bool is a NUMBER to the interpreter -- `"STRING"` for
  text, and `Variable.Type` for a collection, which answers `ARRAY` for a map as well. A
  local whose assignments **disagree** is left out of that record, so it keeps falling back
  rather than being given a guessed answer: `x = n; if (n > 100) { x = "big"; } x.Type` is
  still interpreted. Arguments answer from their declared type.
- **Known divergence: `.Type` and `.ToString()` on a class instance.** Both predate the
  work above (verified by reverting it) and neither is reached by the coverage fixture, the
  probe collection or either script suite. For an instance the interpreter answers
  `SplitAndMerge.CSCSClass+ClassInstance: Point` for `.Type`, while compiled code answers
  `OBJECT`; `p.ToString()` gives `point.p[x=2,y=2]` interpreted and an empty string compiled.
  `string(p)` and `"v=" + p` are correct in both. A guard was attempted and abandoned: the
  read is not emitted by `TryMapTypeMember`, nor by the `IsVariableMember` mapping on the
  expression path, nor by the one on the token path, nor through `ConvertTokenIfNeeded` /
  `CreateReturnStatement` -- a trace shows those never see it. It comes out of
  `ProcessReturnStatement`'s three-token branch, which calls `ProcessToken` directly and
  bypasses all of them; that is where a fix belongs. Guarding `.Type` must stay
  member-specific: every other member of `Variable` answers the same on an instance as
  elsewhere (`p.Size` is 0 either way), so excluding instances wholesale would cost
  constructs that compile correctly today.
- A `for` header with sections left out compiles, including `for (;;)`. It used to fall back,
  and the cause was in the statement walk rather than in the emitted text: the tokenizer drops
  the empty text between the two semicolons, so `for (;;)` arrives as
  `"for ("` `";"` `";"` `")"` `"{"` -- one slot shorter than a full header. Advancing two
  statements along then read the block's `{` as the step and skipped the `)`, emitting
  `for(;;{ {`. The condition slot holds `";"` itself (not an empty string, which is why
  testing for blankness changed nothing), so an absent condition emits its own semicolon and
  the walk advances by one instead of two. Every shape now compiles and matches the
  interpreter: `for (;;)` with `break`, with `continue`, with a `return` out of the loop,
  nested in another, accumulating text, and `for (init;;)`.
- Text times a number falls back inside a compound assignment. `"ab" * 2` joins rather than
  multiplies, and it compiles on its own -- `s = "ab" * 2` gives `ab2`, `2 * "ab"` gives
  `2ab`, `"ab" * n` gives `ab3` -- but `acc += "ab" * 2` reaches C# as `string * int` and
  falls back with the interpreter's value. Rewriting the right-hand side in `ProcessRHS`
  does not fix it and should not be retried: the tokenizer delivers the statement as the
  single token `acc += "ab" * 2`, and `IsKnownExpression` answers false for any token holding
  a quote, so a compound assignment of text never takes that branch. Tried on 2026-09-12, it
  changed none of the target cases and made four already-compiling string comparisons fall
  back (`str_two_args`, `eq_str_num`, `eq_str_pad`, `ne_str_num`), taking the coverage
  fixture from 587 to 583. A fix belongs in the path that handles statements
  `IsKnownExpression` rejects.
