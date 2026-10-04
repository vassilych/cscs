[0] BodyText
The Parser That Couldn't Compile Itself

[2] BodyText
Ten years ago I wrote an expression parser that builds no syntax tree. This year I pointed an AI at it, and at the compiler I had bolted onto it in 2020. The compiler turned out to have been dead for years. Rebuilding it taught me what that tree-less design costs, and how to change old code you no longer fully remember without breaking it.

[5] BodyText
Some years ago I started writing about Split-and-Merge, an algorithm for evaluating expressions that I came up with while building a scripting language in C#. The language became CSCS (Customized Scripting in C#). It is a small, dynamically typed, C-like language that you can embed in a .NET application, including MAUI apps on iOS and Android, and debug from VS Code with the CSCS Debugger extension.

[7] BodyText
I never expected what happened next. The lead of Microsoft Maquette, a virtual-reality prototyping tool, read a few of my CODE Magazine articles and emailed me to propose a collaboration. CSCS became Maquette's first scripting layer, chosen for its flexibility and because it was easy to sandbox, and I worked with the team as a contractor to adapt it. Maquette later moved to JavaScript running on Jint. Stefan Landvogt and I described that collaboration, and the reasons for the move, in "Prototyping with Microsoft Maquette: A New Virtual Reality Tool" (CODE Magazine, Sep/Oct 2020). The tooling outlived the runtime swap: Microsoft's Maquette JavaScript Integration extension for VS Code states on its Marketplace page that it is "based on the CSCS Debugger extension written by Vassili Kaplan."

[9] BodyText
In the Jan/Feb 2020 issue I described how CSCS could compile a function to C# at run time ("Compiling Scripts to Get Compiled-Language Performance"). You declare a function with cfunction instead of function, and the interpreter translates its body to C#, compiles it, and from then on calls native code.

[11] BodyText
This article returns to both pieces of work. I didn't come back because of a new idea. This year I read the old code properly for the first time, with Claude Opus, an AI model from Anthropic, as the second reader. It found a real bug in the most elegant few lines I ever wrote. It also found that the compiler from my 2020 article had not worked for years, and it rebuilt that compiler with me. My thesis fits in one sentence: {b}the decision that makes the interpreter elegant is the same decision that makes the language hard to compile.{/b} I can now show the receipts.

[15] BodyText
Split-and-Merge evaluates an expression in two passes. The {i}split{/i} pass walks the text once and produces a flat list of {i}cells{/i}. Each cell fuses a value with the operator that follows it. The expression 3 + 4 * 2 - 1 splits into four cells:

[19] BodyText
The last cell carries a null action, which has the lowest priority of all. Nothing is ever parsed into a tree. Parentheses and function calls are handled during the split by calling the whole algorithm recursively, so by the time the list exists, every cell holds a plain value.

[21] BodyText
The {i}merge{/i} pass then walks the list from left to right and merges each cell into the one after it, combining their values with the left cell's operator. If the right cell's operator has a higher priority, the merge defers: it merges the cells further to the right first. {b}Figure 1{/b} shows the steps for 3 + 4 * 2 - 1, and {b}Listing 1{/b} shows the heart of the code, from Parser.cs.

[26] BodyText
When two cells merge, the result inherits the operator of the right cell, so it can go on merging with whatever comes next. GetPriority is a switch statement with eleven levels, from ** down to =. Together with MergeList, the whole precedence engine is about a hundred lines of C#. I used to say sixty; the AI counted.

[29] BodyText
Look at CanMergeCells again. Associativity is not written down anywhere. It is implied by the >=: when two neighbouring operators have equal priority, the left one merges first, so every operator is left-associative. That is correct for +, -, *, and /. It is wrong for exponentiation. Until this year, CSCS evaluated 2 ** 3 ** 2 as (2 ** 3) ** 2, which is 64. Mathematics, Python, Ruby, F#, and JavaScript all read it as 2 ** (3 ** 2), which is 512.

[31] BodyText
There is a reason for that convention, and most textbooks skip it. Left-associative exponentiation is redundant, because (a ** b) ** c always equals a ** (b * c). Only the right-associative reading gives you a value that one exponent cannot express. The convention is not universal, though: MATLAB, Octave, and Excel read it from the left. So this is a convention, not a law. But it is the one a CSCS user coming from Python or JavaScript expects.

[33] BodyText
The fix is a two-line guard in front of the >=: if both cells carry **, refuse the merge, so the right-hand pair merges first. It went in on September 6, with regression tests on both the interpreted and the compiled side.

[35] BodyText
The fused cell has a second consequence. A cell fuses an operand with the operator that {i}follows{/i} it, so a prefix operator has nowhere to live but on the operand itself. Unary minus is attached to the value while the cell is being built, before any merging happens. That is why -n ** 2 is 9 in CSCS when n is 3, and -9 in Python. Two surprising behaviours, both traced to one design decision, make an argument rather than an anecdote. The second one is still there. Changing it would silently change the answers of existing scripts, and I haven't decided whether to.

[38] BodyText
I invented Split-and-Merge without having read Pratt's paper, so I asked Claude to place it. The standard options are Dijkstra's shunting-yard algorithm, which uses an operator stack and an output queue; classic operator-precedence parsing; Pratt parsing (top-down operator precedence), where each token knows how to parse what follows it; and precedence climbing, a recursive loop that consumes operators while their precedence stays above a threshold.

[40] BodyText
Split-and-Merge computes what precedence climbing computes. The recursive Merge(next, ..., mergeOneOnly: true) call in Listing 1 does the same job as precedence climbing's recursive call for a higher-priority operator. What is unusual is the route. The tokens are evaluated into values during the split, before any precedence decision is made, and each operator lives on the value to its left instead of in a node or on a stack. Claude's summary was that it had not seen this formulation described elsewhere, and that the formulation is why the engine is so short. I can't claim more than that, and I won't. What I can say is that there is no tree to allocate, walk, or keep, which is a good property for an interpreter embedded in a phone app.

[42] BodyText
It is a bad property for a compiler, as the rest of this article shows.

[45] BodyText
The 2020 precompiler worked like this. The interpreter handed a cfunction's body to a translator. The translator worked token by token and produced a C# method. CodeDom's CSharpCodeProvider.CompileAssemblyFromSource then compiled that method into an in-memory assembly. The first thing Claude found was that none of it had run for years. On .NET Core and .NET 5 and later, CompileAssemblyFromSource throws PlatformNotSupportedException. The interpreter caught the exception and reported it as a parsing error in the script. When my hosts moved to net9.0, every cfunction failed at declaration time, and nobody noticed.

[47] BodyText
The backend was the easy part to replace. The new RoslynCompiler calls CSharpCompilation.Create(...).Emit, and it reports real diagnostics: the error ID, line, and column against a line-numbered dump of the generated C#. It caches every assembly under a hash of the generated source plus a fingerprint of the referenced assemblies. On my machine, a cold compile adds about 1.1 seconds to a run, and a cached one adds almost nothing. For iOS, where generating code at run time is forbidden, there is now an ahead-of-time path. You run the script once on a desktop with collect_comp_csharp(true), write every translated function into one C# file with write_comp_csharp(...), compile that file into the app, and call CscsPrecompiled.RegisterAll() at startup. The script is the same on the desktop and on the device. The cfunction declaration simply finds its implementation already registered.

[49] BodyText
{b}Listing 2{/b} shows what the translator produces for a small cfunction: the typed parameters arrive in typed lists, and the loop becomes a plain C# loop.

[51] BodyText
The hard part was the translator, and every hard problem came back to the same fact: an interpreter that never builds a tree has no types, no scopes, and no structure for a translator to read. A translator working token by token has to recover all three from text. Here are the six failures that taught me that. Each one has a structural cause, not a typo behind it.

[53] NumberedBodyText
{b}Every CSCS number is a double.{/b} Copy 3/2 into C# verbatim and you get integer division: 1 instead of 1.5. The code compiles cleanly and returns the wrong number. Literals next to a division are now widened, and locals are declared double.

[54] NumberedBodyText
{b}Integers overflow; CSCS numbers don't.{/b} An int argument used in arithmetic wrapped around. n * n * n for n = 100000 came out as -1,530,494,976, where the interpreter says 10^15. An int argument now reads as a double next to *, +, and -.

[55] NumberedBodyText
{b}Truth values are numbers.{/b} A CSCS comparison yields 1 or 0, so "v=" + (a > b) renders as v=1. C# renders a bool as True. Comparisons can also be chained: in CSCS, 1 < n < 10 means (1 < n), which is 1 or 0, compared with 10. In C# the same text is bool < int, which does not compile.

[56] NumberedBodyText
{b}Operators decide the type at run time.{/b} return helper(n) + helper(n) returned 0 when helper returned strings, because the translator had guessed that + meant numeric addition. It now calls the same + the interpreter uses whenever the operand types are unknown.

[57] NumberedBodyText
{b}Calls go through text.{/b} Calling a CSCS function passes the arguments as a {i}string{/i} that the interpreter parses again and resolves by name. So the translator mirrored every variable write back into the interpreter, just in case. In a tight loop, that write-back cost roughly 700 times as much as the arithmetic around it.

[58] NumberedBodyText
{b}Evaluation order is part of the meaning.{/b} To call a CSCS function inside an expression, the translator hoisted the call into a statement of its own. Hoisted out of while (i < 10 && f(i) < n), the call ran even when i < 10 was false, and the loop returned 10 where the interpreter returns 4.

[60] BodyText
Failures 1, 2, 4, and 6 are the dangerous kind: the generated C# compiles, runs, and returns a different answer. No compiler warns about that. Something else has to catch it.

[63] BodyText
CSCS has no formal specification. It has an interpreter, and that turns out to be enough. If you compile a language that already has an interpreter, then the interpreter {i}is{/i} the specification, and differential testing gives you a correctness oracle for free.

[65] BodyText
The test that does this is PrecompilerCoverageFixture. It writes every construct twice with an identical body, once as a cfunction and once as a plain function, runs both, and compares the results. A construct can end up in one of three states: it compiles and agrees with the interpreter; it falls back to the interpreter (see the next section); or it compiles and {i}disagrees{/i}, which the fixture counts as broken and treats as the one unacceptable outcome. When I wrote the proposal for this article, the fixture measured 45 constructs. Today it measures 1,142: 1,129 compile to C#, 12 fall back, and none disagree. A fixture is written to exercise the translator, though, so I also measured the code people actually write: of the 468 cfunctions in the repository's own scripts, translated with their real signatures, 465 compile. Of the three that don't, two are refused on purpose and one guards a case where compiling once gave a wrong answer. Two script-level suites back it up. test_compiled.cscs has 1,016 assertions that run each case compiled and interpreted. test.cscs has 630 assertions on the language itself.

[67] BodyText
Of the 11 fallbacks, most are deliberate. CSCS has no shift operator, so the interpreter answers 0 for 3 << 2, where C#'s shift gives 12. Compiling the expression would give a {i}better{/i} answer, but a different one, so the translator refuses it. That became the project's rule: {b}a divergence is worse than a fallback, even when the compiled answer is the more sensible one.{/b}

[71] BodyText
The oracle has a catch: it can be wrong. Building the compiler meant reading the interpreter more closely than I ever had, and it turned up real interpreter bugs. A switch nested inside a case handed its labels to the outer switch, so the outer switch ran the inner default. That one was found the day I wrote this paragraph. a[0].v = 9 was silently lost. p.x += 5 created a new variable literally named p.x. a[0] = a[1] = 7 overflowed the stack and killed the process. A leak in the call-stack bookkeeping made function calls quadratic: 40,000 calls took 17.4 seconds before the fix and take 1.5 seconds now. We settled on an order of work for these cases: {b}fix the interpreter first, then compile the construct.{/b} Otherwise the fixture would reward the compiler for copying a bug.

[74] BodyText
The old precompiler had one failure mode: if the translator couldn't handle a body, the script died. The new one registers such a function as an ordinary interpreted function and records why:

[80] BodyText
This inverts the risk. A gap in the translator now costs performance, not correctness, and coverage can grow one construct at a time without breaking anyone's script. JIT compilers work the same way, with an interpreter behind every compiled tier. It is rarely done in small, hand-written scripting engines, and it should be. The tests switch the fallback off to measure the translator itself, and Fallbacks is what tells "compiled" apart from "silently interpreted."

[82] BodyText
Fallback does have a limit. It catches translation failures, but it cannot catch generated code that compiles and then behaves differently at run time. The differential fixture exists for exactly that.

[84] BodyText
Many of this year's coverage gains came from one cheap trick: rewrite the CSCS source into a form the translator already handles, before translating it, and leave the interpreted version of the function untouched. iff(c, a, b) becomes a ternary, because the interpreter's iff evaluates only the branch it picks, exactly as a ternary does. A chained comparison is rewritten into the form CSCS actually evaluates:

[89] BodyText
The second line matters. CSCS ranks < above ==, as C# does, and the rewrite has to respect those levels. An earlier entry in the precompiler's design notes warned that "a mistaken rewrite would answer differently instead, which is worse." It was right. The rewrite went in only after we checked it against GetPriority and against all seven chained shapes the notes had documented.

[91] BodyText
The other trick runs the opposite way. Some comparisons have no C# operator at all: a Variable holding whatever the caller passed, compared with text, as in if (request == "stock"). Rather than teach every part of the translator about them, I let the compiler say where they are. When the generated C# fails with CS0019, Roslyn's semantic model identifies exactly which comparisons failed and which side is a Variable, and only those are rewritten into a call that hands both values to the interpreter's own operator code. The answer cannot differ from the script's, and code that already compiled is never touched.

[94] BodyText
Here are measurements from the current code: a Release build on an Apple M1 Pro, CPU time in milliseconds.

[99] BodyText
The loops are what the 2020 article promised, and more. For the numeric loop, the improvement came in two steps. Replacing CodeDom with Roslyn got the loop compiled at all. It then took 54 ms for 50,000 iterations, against 1,517 ms interpreted. Removing the redundant write-back described earlier brought it down to 1 ms. The integer loop is faster still because of a change made this month. A loop counter used to be declared double, so that / stays real division. It is now declared int whenever that provably can't change an answer: the counter is never divided, never multiplied outside an index, never assigned, and never copied into a new local.

[101] BodyText
Recursion is the counterexample. When I first measured it for this article, fib compiled and gave the right answer, but gained only about 4.6 times, and fib(30) took ten seconds. {b}Listing 3{/b} shows why. This is the C# that was generated for return fibC(n - 1) + fibC(n - 2);.

[103] BodyText
The call is compiled, but what it compiles to is: publish n to the interpreter, build the argument as the text "n-1", and have the interpreter parse that text and look up fibC by name. That is failure 5 from the list, sitting in the hot path of every recursive call. "It compiles" is not the same as "it is fast," and a report that lists compiled functions without saying what they call back into is misleading.

[105] BodyText
Writing that paragraph is what got it fixed. A call with plain arguments now evaluates them in C# and hands the values to the function directly, as {b}Listing 4{/b} shows. The call still goes through the same entry point an interpreted call uses, so the argument frame, the type conversions, and the call-depth guard don't change, and the differential fixture confirmed that no answer did. fib(20) dropped from 90 ms to 27 ms, and fib(30) from ten seconds to 2.2. What remains per call is the frame and the boxing of every value into a Variable. Removing those takes typed, direct calls between compiled functions, and that is the first change that needs something the tree-less design never had: a signature the translator can trust.

[107] BodyText
That change has now been made, in two steps. The first is typed, direct calls. A compiled function that needs nothing from the interpreter and takes only scalar arguments gets a typed entry point, and a call to it from compiled code skips the argument frame, the lists, and the lookup. A call to {i}another{/i} function still looks the name up on every call, so if the script has redefined that function in the meantime, the new definition runs, exactly as in the interpreter. The call-depth guard counts these calls too, so deep recursion still stops with the interpreter's own error instead of crashing the process. fib(30) went from 2.2 seconds to 350 ms.

[109] BodyText
The second step is typed returns. For a function that calls itself, the translator writes a twin of the body that returns a C# double, shown in {b}Listing 5{/b}. Whether every value a function returns is a number is exactly the kind of fact a tree-less translator can't know, so the translator doesn't decide it. It hands the twin to the C# compiler, where CscsDirect.Number accepts only a double or an int. If any path returns text, the twin fails to compile, and the untyped body is used instead. The type checker the design never had is borrowed from C#. fib(30) now takes 60 ms, and fib(20) takes 1 ms, against 390 ms interpreted.

[112] BodyText
To let readers try all of this without installing anything, I set up a public CSCS playground as an MCP (Model Context Protocol) server. Add it to Claude as a custom connector, or to Claude Code, Cursor, VS Code, or any other client that speaks MCP over HTTP:

[119] BodyText
It offers three tools. cscs_guide teaches the assistant CSCS before it writes any, and every example in the guide is checked by a test. run_cscs runs a script and returns its output, its last value, and any error. explain_cscs shows, for each cfunction, the C# the precompiler generates and whether it compiles or why it falls back. Ask your assistant to "model two bank accounts in CSCS and run it," or to "show what the precompiler does with a loop," and it will call the tools by itself.

[121] BodyText
Running strangers' code safely took more than a function allowlist, and the reasons belong in this article. Two escape routes live inside features every script needs. new X() looked up {i}all{/i} loaded .NET types before CSCS classes, so new System.Diagnostics.Process() built a real process. And any value holding a .NET object, a CSCS class instance included, answered v.GetType() through reflection. No allowlist can close those, so the interpreter core gained a switch, InterpreterSecurity.AllowDotNet = false. {b}Figure 2{/b} shows the layers around it. Working outward from the interpreter, they are:

[123] BullettedBodyText
an allowlist of 113 kept functions (93 are removed);

[124] BullettedBodyText
a watchdog: 5 seconds, 256 MB, and a cap of 2,000 nested calls, which turns a .NET stack overflow into a catchable CSCS error;

[125] BullettedBodyText
one process per script, with a low-privilege Windows account;

[126] BullettedBodyText
a Windows AppContainer with no capabilities, inside a Job Object, so the operating system itself denies the script any network access or disk access.

[131] BodyText
explain_cscs compiles the generated C# in memory and never loads it. One test proves why that matters: a cfunction whose body calls System.IO.File.WriteAllText translates {i}and compiles{/i}, because the translator passes unknown .NET calls through. On an open server, that would be remote code execution. In the playground, the report says "compiles" and the file never appears.

[134] BodyText
Here is how the work actually went, since that is the question I get asked. I didn't hand over the repository and ask for a better compiler. The work happened in sessions, over a few weeks. Claude read code, proposed a change, made it, and ran the gates. I decided what to keep. Three things made it work.

[138] BodyText
{b}Give the model an oracle.{/b} Every claim about the compiler could be checked by running the same body twice. A model that can check its own work against a reference can change code nobody fully remembers, and you can {i}verify{/i} the result instead of trusting it. Without the interpreter as a reference, I would not have accepted a single change to a 12,000-line translator.

[140] BodyText
{b}Expect it to be wrong, and to catch it.{/b} The model was wrong often, and the process caught it. Here are some examples:

[142] BullettedBodyText
It diagnosed several interpreter bugs from the symptom first and got the cause wrong, until a smaller reproduction showed where the fault really was.

[143] BullettedBodyText
For a whole day it ran the unit tests against a stale compiled library, because the solution build doesn't rebuild the test project. A test that has passed for too long now makes us suspicious.

[144] BullettedBodyText
The proposal for this article claimed that x = -n had never parsed. The draft was checked against the code, and it has parsed since 2018. The real prefix-operator symptom turned out to be the -n ** 2 above.

[145] BullettedBodyText
During one edit, a script matched the wrong end marker and deleted seven thousand lines of the translator. Version control restored them byte for byte, and the diff showed nothing had changed. The guardrails were ordinary, but they were in place.

[147] BodyText
{b}Make it write things down.{/b} The most valuable artifact is not the code. It is PRECOMPILATION.md, 2,600 lines recording what was tried, measured, and reverted, and why. Several times a "fix" was rejected because the notes showed that the same idea had been measured before and cost something the tests couldn't see.

[149] BodyText
The division of labour was simple. The model did the reading, the hypothesising, and the typing, at a pace I can't match. I supplied the intent and the judgement about what CSCS {i}should{/i} do. Readers with a dormant project of their own will recognise the shape of it. The code you wrote years ago contains decisions you no longer remember making. A careful second reader with a test oracle is the fastest way I know to find out which of those decisions still hold.

[152] BodyText
I would keep the tree-less evaluation. For an interpreter embedded in an app, where short startup, a small footprint, and an easily read core matter most, evaluating in two flat passes is still defensible. The core really is about a hundred lines, and it is what made CSCS small enough for Microsoft to embed.

[154] BodyText
Here is the bill, stated plainly. The interpreter's precedence engine is about a hundred lines. The translator that compiles the same language is about twelve thousand, roughly a third of all the C# in the project. That is the cost of recovering types, scopes, and structure that the interpreter never needed. If I were starting again with compilation in mind, I would still split and merge, but I would give the result a small typed tree, so that the compiler has something to read other than text. I would also give prefix operators a place of their own, instead of attaching them to the operand.

[158] BodyText
The source code is on GitHub at github.com/vassilych/cscs, and the playground is at cscs.brainpingpong.com. Try it, break it, and tell me what diverges.
