"""Applies the article feedback to the draft's word/document.xml.

1. CS0019 explained where it first comes up, with the generated code before and after.
2. Listings 1-5 moved into the text as CodeSnippet blocks (at most 20 lines each), the
   listings section at the end removed.
3. First-person narration ("I thought, I did, I changed") rewritten around what the reader
   can take away.

Every paragraph not named here is left exactly as it is.
"""
import re
import shutil
import sys
from xml.sax.saxutils import escape

SRC = 'd/word/document.xml'
xml = open(SRC, encoding='utf-8').read()
body_start = xml.index('<w:body>')
body = xml[body_start:]
paras = re.findall(r'<w:p[ >].*?</w:p>|<w:tbl>.*?</w:tbl>', body, re.S)

SZ = '<w:sz w:val="20"/><w:szCs w:val="20"/>'
EMPTY = '<w:p><w:pPr><w:pStyle w:val="BodyText"/></w:pPr></w:p>'


def runs(markup):
    out = []
    for part in re.split(r'(\{b\}.*?\{/b\}|\{i\}.*?\{/i\})', markup):
        if not part:
            continue
        rpr = SZ
        if part.startswith('{b}'):
            part, rpr = part[3:-4], '<w:b/><w:bCs/>' + SZ
        elif part.startswith('{i}'):
            part, rpr = part[3:-4], '<w:i/><w:iCs/>' + SZ
        out.append('<w:r><w:rPr>' + rpr + '</w:rPr><w:t xml:space="preserve">' +
                   escape(part) + '</w:t></w:r>')
    return ''.join(out)


def body_para(markup, style='BodyText'):
    return ('<w:p><w:pPr><w:pStyle w:val="' + style + '"/><w:rPr>' + SZ + '</w:rPr></w:pPr>' +
            runs(markup) + '</w:p>')


def snippet(code):
    lines = code.strip('\n').split('\n')
    assert len(lines) <= 20, (len(lines), lines[0])
    cs = '<w:color w:val="222222"/>' + SZ
    return ''.join('<w:p><w:pPr><w:pStyle w:val="CodeSnippet"/><w:rPr>' + cs + '</w:rPr></w:pPr>' +
                   ('<w:r><w:rPr>' + cs + '</w:rPr><w:t xml:space="preserve">' + escape(line) +
                    '</w:t></w:r>' if line else '') + '</w:p>' for line in lines)


def block(*items):
    """Paragraphs separated as the template separates them: an empty BodyText between."""
    return EMPTY.join(items)


def text_of(i):
    return ''.join(re.findall(r'<w:t[^>]*>([^<]*)</w:t>', paras[i]))


replacements = {}


def replace(i, new_xml, starts):
    assert text_of(i).replace('&amp;', '&').startswith(starts), (i, text_of(i)[:60])
    assert body.count(paras[i]) == 1, i
    replacements[i] = new_xml


# ---- Snippets (from the listings at the end, condensed only where noted) ----

MERGE = '''
private static Variable Merge(Variable current, ref int index,
    List<Variable> listToMerge, ParsingScript script,
    bool mergeOneOnly = false)
{
    while (index < listToMerge.Count)
    {
        Variable next = listToMerge[index++];
        while (!CanMergeCells(current, next))
        {
            // 1+2*3: merge 2*3 first, getting 6, then 1+6.
            Merge(next, ref index, listToMerge, script,
                  true /* mergeOneOnly */);
        }
        MergeCells(current, next, script);
        if (mergeOneOnly) { break; }
    }
    return current;
}'''

CAN_MERGE = '''
static bool CanMergeCells(Variable leftCell,
                          Variable rightCell)
{
    return GetPriority(leftCell.Action) >=
           GetPriority(rightCell.Action);
}'''

COMPOUND_CSCS = '''
cfunction double compound(double principal,
                          double rate, int years) {
  total = principal;
  for (i = 0; i < years; i++) {
    total = total * (1 + rate);
  }
  return Math.Round(total, 2);
}'''

COMPOUND_CS = '''
public static Variable compound(
    Interpreter __interpreter,
    List<string> __varStr, List<double> __varNum,
    List<int> __varInt,
    List<List<string>> __varArrStr,
    List<List<double>> __varArrNum,
    List<List<int>> __varArrInt,
    List<Dictionary<string, string>> __varMapStr,
    List<Dictionary<string, double>> __varMapNum,
    List<Variable> __varVar)
{
    double total = __varNum[0];
    int i;
    for (i = 0; i < __varInt[0]; i++) {
        total = total * (1 + __varNum[1]);
    }
    return Variable.ConvertToVariable(
        Math.Round(total, 2));
}'''

CS0019_BEFORE = '''
if (__p0 == "stock")
// error CS0019: Operator '==' cannot be applied to
// operands of type 'Variable' and 'string'
'''

CS0019_AFTER = '''
if (CscsOps.Compare(__interpreter, (object)(__p0), "==",
                    (object)("stock")))'''

CALL_TEXT = '''
__interpreter.AddCompiledLocalVariable("n",
    new GetVarFunction(
        Variable.ConvertToVariable(__varInt[0])));
__argsTempStr = "n-1";
__scriptTempVar = new ParsingScript(__interpreter,
    __argsTempStr, true);
__funcTempVar = new ParserFunction(__scriptTempVar,
    "fibC", '(', ref __actionTempVar);
__varTempVar = __funcTempVar.GetValue(__scriptTempVar);'''

CALL_VALUES = '''
__varTempVar = CscsCalls.Call(__interpreter, "fibC",
    (double)(__varInt[0]) - 1);
Variable __varTempVar1 = __varTempVar;
__varTempVar = CscsCalls.Call(__interpreter, "fibC",
    (double)(__varInt[0]) - 2);
Variable __varTempVar2 = __varTempVar;
return Variable.ConvertToVariable(
    __varTempVar1 + __varTempVar2);'''

TYPED = '''
public static double fibC__numbody(
    Interpreter __interpreter, int __p0) {
  if (__p0 < 2) {
    return CscsDirect.Number(__p0);
  }
  double __varTempVar1 = fibC__numdirect(__interpreter,
      CscsDirect.Int((double)(__p0) - 1));
  double __varTempVar2 = fibC__numdirect(__interpreter,
      CscsDirect.Int((double)(__p0) - 2));
  return CscsDirect.Number(__varTempVar1 + __varTempVar2);
}'''

# ---- The paragraphs ----

replace(2, body_para(
    "An expression parser that builds no syntax tree is small, quick to start, and easy to embed. "
    "It is also hard to compile. This article follows one such parser, written ten years ago, and the "
    "compiler bolted onto it in 2020, which turned out to have been dead for years. Rebuilding that "
    "compiler, with an AI as a second reader, shows what a tree-less design costs, and how to change "
    "old code that nobody fully remembers without breaking it."),
    "Ten years ago")

replace(5, body_para(
    "Split-and-Merge is an algorithm for evaluating expressions that I came up with while building a "
    "scripting language in C#, and it has appeared in this magazine several times since. The language "
    "became CSCS (Customized Scripting in C#). It is a small, dynamically typed, C-like language that "
    "you can embed in a .NET application, including MAUI apps on iOS and Android, and debug from VS "
    "Code with the CSCS Debugger extension."),
    "Some years ago")

replace(7, body_para(
    "Those articles led somewhere unexpected. The lead of Microsoft Maquette, a virtual-reality "
    "prototyping tool, read a few of them and proposed a collaboration. CSCS became Maquette's first "
    "scripting layer, chosen for its flexibility and because it was easy to sandbox, and I adapted it "
    "for the team as a contractor. Maquette later moved to JavaScript running on Jint. Stefan Landvogt "
    "and I described that collaboration, and the reasons for the move, in \"Prototyping with Microsoft "
    "Maquette: A New Virtual Reality Tool\" (CODE Magazine, Sep/Oct 2020). The tooling outlived the "
    "runtime swap: Microsoft's Maquette JavaScript Integration extension for VS Code states on its "
    "Marketplace page that it is \"based on the CSCS Debugger extension written by Vassili Kaplan.\""),
    "I never expected")

replace(9, body_para(
    "The Jan/Feb 2020 issue described how CSCS could compile a function to C# at run time (\"Compiling "
    "Scripts to Get Compiled-Language Performance\"). You declare a function with cfunction instead of "
    "function, and the interpreter translates its body to C#, compiles it, and from then on calls "
    "native code."),
    "In the Jan/Feb 2020")

replace(11, body_para(
    "This article returns to both pieces of work. What prompted it was not a new idea but a careful "
    "rereading of the old code, with Claude Opus, an AI model from Anthropic, as the second reader. The "
    "rereading found a real bug in the parser's most elegant few lines. It also found that the 2020 "
    "compiler had not worked for years, and the compiler was rebuilt in the sessions that followed. The "
    "thesis fits in one sentence: {b}the decision that makes the interpreter elegant is the same "
    "decision that makes the language hard to compile.{/b} The sections below make the case: what the "
    "design is, what it costs a compiler, and which techniques keep a compiler faithful to an "
    "interpreter it cannot fully read."),
    "This article returns")

replace(21, block(body_para(
    "The {i}merge{/i} pass then walks the list from left to right and merges each cell into the one "
    "after it, combining their values with the left cell's operator. If the right cell's operator has a "
    "higher priority, the merge defers: it merges the cells further to the right first. {b}Figure 1{/b} "
    "shows the steps for 3 + 4 * 2 - 1. The heart of the code, the merge loop from Parser.cs, is short "
    "enough to read in full:"), snippet(MERGE)),
    "The merge pass")

replace(26, body_para(
    "When two cells merge, the result inherits the operator of the right cell, so it can go on merging "
    "with whatever comes next. GetPriority is a switch statement with eleven levels, from ** down to =. "
    "Together with MergeList, the whole precedence engine is about a hundred lines of C#. (The estimate "
    "used to be sixty, until the AI counted.)"),
    "When two cells merge")

replace(29, block(body_para(
    "Whether a cell merges now or defers is decided by CanMergeCells, which is the entire precedence "
    "rule:"), snippet(CAN_MERGE), body_para(
    "Associativity is not written down anywhere. It is implied by the >=: when two neighbouring "
    "operators have equal priority, the left one merges first, so every operator is left-associative. "
    "That is correct for +, -, *, and /. It is wrong for exponentiation. Until this year, CSCS evaluated "
    "2 ** 3 ** 2 as (2 ** 3) ** 2, which is 64. Mathematics, Python, Ruby, F#, and JavaScript all read "
    "it as 2 ** (3 ** 2), which is 512.")),
    "Look at CanMergeCells again")

replace(35, body_para(
    "The fused cell has a second consequence. A cell fuses an operand with the operator that "
    "{i}follows{/i} it, so a prefix operator has nowhere to live but on the operand itself. Unary minus "
    "is attached to the value while the cell is being built, before any merging happens. That is why "
    "-n ** 2 is 9 in CSCS when n is 3, and -9 in Python. Two surprising behaviours, both traced to one "
    "design decision, make an argument rather than an anecdote. The second one is still there. Changing "
    "it would silently change the answers of existing scripts, which makes it a decision about the "
    "language's users rather than about its parser, and that decision is still open."),
    "The fused cell")

replace(38, body_para(
    "Split-and-Merge was designed without reference to Pratt's paper or the other classic techniques, "
    "so it is worth placing it among them. The standard options are Dijkstra's shunting-yard algorithm, "
    "which uses an operator stack and an output queue; classic operator-precedence parsing; Pratt "
    "parsing (top-down operator precedence), where each token knows how to parse what follows it; and "
    "precedence climbing, a recursive loop that consumes operators while their precedence stays above a "
    "threshold."),
    "I invented Split-and-Merge")

replace(40, body_para(
    "Split-and-Merge computes what precedence climbing computes. The recursive Merge(next, ..., "
    "mergeOneOnly: true) call in the merge loop does the same job as precedence climbing's recursive "
    "call for a higher-priority operator. What is unusual is the route. The tokens are evaluated into "
    "values during the split, before any precedence decision is made, and each operator lives on the "
    "value to its left instead of in a node or on a stack. Asked to place it, Claude had not seen this "
    "formulation described elsewhere, and named it as the reason the engine is so short. That is a "
    "model's impression, not a literature search, and it should be read as one. What is certain is that "
    "there is no tree to allocate, walk, or keep, which is a good property for an interpreter embedded "
    "in a phone app."),
    "Split-and-Merge computes")

replace(45, body_para(
    "The 2020 precompiler worked like this. The interpreter handed a cfunction's body to a translator. "
    "The translator worked token by token and produced a C# method. CodeDom's "
    "CSharpCodeProvider.CompileAssemblyFromSource then compiled that method into an in-memory assembly. "
    "The first thing Claude found was that none of it had run for years. On .NET Core and .NET 5 and "
    "later, CompileAssemblyFromSource throws PlatformNotSupportedException. The interpreter caught the "
    "exception and reported it as a parsing error in the script. When the host applications moved to "
    "net9.0, every cfunction failed at declaration time, and nobody noticed. A feature that fails "
    "quietly is a feature nobody is testing."),
    "The 2020 precompiler")

replace(47, body_para(
    "The backend was the easy part to replace. The new RoslynCompiler calls "
    "CSharpCompilation.Create(...).Emit, and it reports real diagnostics: the error ID, line, and column "
    "against a line-numbered dump of the generated C#. It caches every assembly under a hash of the "
    "generated source plus a fingerprint of the referenced assemblies. On an Apple M1 Pro, a cold "
    "compile adds about 1.1 seconds to a run, and a cached one adds almost nothing. For iOS, where "
    "generating code at run time is forbidden, there is now an ahead-of-time path. You run the script "
    "once on a desktop with collect_comp_csharp(true), write every translated function into one C# file "
    "with write_comp_csharp(...), compile that file into the app, and call CscsPrecompiled.RegisterAll() "
    "at startup. The script is the same on the desktop and on the device. The cfunction declaration "
    "simply finds its implementation already registered."),
    "The backend was the easy part")

replace(49, block(body_para(
    "Here is what the translator produces for a small cfunction. The CSCS source:"),
    snippet(COMPOUND_CSCS), body_para(
    "And the C# method generated for it, reformatted, with seven unused temporaries omitted. The typed "
    "parameters arrive in typed lists, and the loop becomes a plain C# loop:"),
    snippet(COMPOUND_CS)),
    "Listing 2")

replace(51, body_para(
    "The hard part was the translator, and every hard problem came back to the same fact: an "
    "interpreter that never builds a tree has no types, no scopes, and no structure for a translator to "
    "read. A translator working token by token has to recover all three from text. Six failures show "
    "what that means in practice. Each one has a structural cause, not a typo, behind it, and each is a "
    "trap waiting for anyone who compiles a dynamic language to a static one."),
    "The hard part was the translator")

replace(65, body_para(
    "The test that does this is PrecompilerCoverageFixture. It writes every construct twice with an "
    "identical body, once as a cfunction and once as a plain function, runs both, and compares the "
    "results. A construct can end up in one of three states: it compiles and agrees with the "
    "interpreter; it falls back to the interpreter (see the next section); or it compiles and "
    "{i}disagrees{/i}, which the fixture counts as broken and treats as the one unacceptable outcome. "
    "When this article was proposed, the fixture measured 45 constructs. Today it measures 1,142: 1,129 "
    "compile to C#, 12 fall back, and none disagree. A fixture is written to exercise the translator, "
    "though, so the code people actually write was measured too: of the 468 cfunctions in the "
    "repository's own scripts, translated with their real signatures, 465 compile. Of the three that "
    "don't, two are refused on purpose and one guards a case where compiling once gave a wrong answer. "
    "Two script-level suites back it up. test_compiled.cscs has 1,016 assertions that run each case "
    "compiled and interpreted. test.cscs has 630 assertions on the language itself."),
    "The test that does this")

replace(71, body_para(
    "The oracle has a catch: it can be wrong. Building the compiler meant reading the interpreter more "
    "closely than ever before, and that turned up real interpreter bugs. A switch nested inside a case "
    "handed its labels to the outer switch, so the outer switch ran the inner default; that one turned "
    "up while this paragraph was being written. a[0].v = 9 was silently lost. p.x += 5 created a new "
    "variable literally named p.x. a[0] = a[1] = 7 overflowed the stack and killed the process. A leak "
    "in the call-stack bookkeeping made function calls quadratic: 40,000 calls took 17.4 seconds before "
    "the fix and take 1.5 seconds now. The order of work for such cases follows from the oracle: "
    "{b}fix the interpreter first, then compile the construct.{/b} Otherwise the fixture would reward "
    "the compiler for copying a bug."),
    "The oracle has a catch")

replace(89, body_para(
    "The second line matters. CSCS ranks < above ==, as C# does, and the rewrite has to respect those "
    "levels. An earlier entry in the precompiler's design notes warned that \"a mistaken rewrite would "
    "answer differently instead, which is worse.\" It was right. The rewrite went in only after it was "
    "checked against GetPriority and against all seven chained shapes the notes had documented."),
    "The second line matters")

replace(91, block(body_para(
    "The other trick runs the opposite way. Some comparisons have no C# operator at all: a Variable "
    "holding whatever the caller passed, compared with text, as in if (request == \"stock\"). Rather "
    "than teach every part of the translator about them, the translator lets the C# compiler say where "
    "they are. It emits the comparison as written, and the compile fails:"),
    snippet(CS0019_BEFORE), body_para(
    "CS0019 is the C# compiler's error for an operator that is not defined for the types of its "
    "operands. The CSCS code is fine; C# simply has no == between a Variable and a string. The error "
    "comes with the exact position of the expression, so Roslyn's semantic model can tell which "
    "comparisons failed and which side is a Variable. Only those are rewritten, into a call that hands "
    "both values to the interpreter's own operator code:"),
    snippet(CS0019_AFTER), body_para(
    "The answer cannot differ from the script's, and code that already compiled is never touched. The "
    "technique generalizes: when a translator cannot know the types, let the target language's compiler "
    "find the places where they matter, and repair only those.")),
    "The other trick runs")

replace(101, block(body_para(
    "Recursion is the counterexample. Measured for the first draft of this article, fib compiled and "
    "gave the right answer, but gained only about 4.6 times, and fib(30) took ten seconds. The reason "
    "is in the C# generated for the first of the two calls in return fibC(n - 1) + fibC(n - 2):"),
    snippet(CALL_TEXT)),
    "Recursion is the counterexample")

replace(105, block(body_para(
    "Writing that paragraph is what got it fixed. A call with plain arguments now evaluates them in C# "
    "and hands the values to the function directly. Here are both calls:"),
    snippet(CALL_VALUES), body_para(
    "The call still goes through the same entry point an interpreted call uses, so the argument frame, "
    "the type conversions, and the call-depth guard don't change, and the differential fixture "
    "confirmed that no answer did. fib(20) dropped from 90 ms to 27 ms, and fib(30) from ten seconds to "
    "2.2. What remains per call is the frame and the boxing of every value into a Variable. Removing "
    "those takes typed, direct calls between compiled functions, and that is the first change that "
    "needs something the tree-less design never had: a signature the translator can trust.")),
    "Writing that paragraph")

replace(109, block(body_para(
    "The second step is typed returns. For a function that calls itself, the translator writes a twin "
    "of the body that returns a C# double:"),
    snippet(TYPED), body_para(
    "Whether every value a function returns is a number is exactly the kind of fact a tree-less "
    "translator can't know, so the translator doesn't decide it. It hands the twin to the C# compiler, "
    "where CscsDirect.Number accepts only a double or an int. If any path returns text, the twin fails "
    "to compile, and the untyped body is used instead. The type checker the design never had is "
    "borrowed from C#. fib(30) now takes 60 ms, and fib(20) takes 1 ms, against 390 ms interpreted.")),
    "The second step is typed returns")

replace(112, body_para(
    "To try all of this without installing anything, use the public CSCS playground, which runs as an "
    "MCP (Model Context Protocol) server. Add it to Claude as a custom connector, or to Claude Code, "
    "Cursor, VS Code, or any other client that speaks MCP over HTTP:"),
    "To let readers try")

replace(134, body_para(
    "How does this kind of work go in practice? Not by handing over a repository and asking for a "
    "better compiler. It happened in sessions, over a few weeks. Claude read code, proposed a change, "
    "made it, and ran the test gates; I decided what to keep. Three practices made it work, and they "
    "carry over to any project of this kind."),
    "Here is how the work actually went")

replace(138, body_para(
    "{b}Give the model an oracle.{/b} Every claim about the compiler could be checked by running the "
    "same body twice. A model that can check its own work against a reference can change code nobody "
    "fully remembers, and you can {i}verify{/i} the result instead of trusting it. Without the "
    "interpreter as a reference, no change to a 12,000-line translator could have been accepted with "
    "confidence."),
    "Give the model an oracle")

replace(143, body_para(
    "For a whole day it ran the unit tests against a stale compiled library, because the solution "
    "build doesn't rebuild the test project. Since then, a test that has passed for too long is a "
    "reason for suspicion, not comfort.", 'BullettedBodyText'),
    "For a whole day")

replace(149, body_para(
    "The division of labour was simple. The model did the reading, the hypothesising, and the typing, "
    "at a pace no person can match. I supplied the intent and the judgement about what CSCS "
    "{i}should{/i} do. If you have a dormant project of your own, the shape will be familiar. The code "
    "you wrote years ago contains decisions you no longer remember making, and a careful second reader "
    "with a test oracle is the fastest way to find out which of those decisions still hold."),
    "The division of labour")

replace(152, body_para(
    "The tree-less evaluation is worth keeping. For an interpreter embedded in an app, where short "
    "startup, a small footprint, and an easily read core matter most, evaluating in two flat passes is "
    "still defensible. The core really is about a hundred lines, and it is what made CSCS small enough "
    "for Microsoft to embed."),
    "I would keep the tree-less")

replace(154, body_para(
    "Here is the bill, stated plainly. The interpreter's precedence engine is about a hundred lines. "
    "The translator that compiles the same language is about twelve thousand, roughly a third of all "
    "the C# in the project. That is the cost of recovering types, scopes, and structure that the "
    "interpreter never needed. For a new language that is meant to be compiled, the advice is "
    "different: split and merge by all means, but give the result a small typed tree, so that the "
    "compiler has something to read other than text, and give prefix operators a place of their own "
    "instead of attaching them to the operand."),
    "Here is the bill")

# The heading of the last section loses its "I".
heading = paras[151]
assert 'What I Would Keep' in heading
new_heading = heading.replace('What I Would Keep, and What I Would Change', 'What to Keep, and What to Change')
assert new_heading != heading, 'heading text is split across runs'
replacements[151] = new_heading

# ---- Apply ----
new_body = body
for i, new_xml in replacements.items():
    assert new_body.count(paras[i]) == 1, i
    new_body = new_body.replace(paras[i], new_xml)

# The listings section: from the empty paragraph after the bio to the section properties.
bio_end = new_body.index(paras[204]) + len(paras[204])
assert new_body[bio_end:].startswith(paras[205])
assert 'Listing 1:' in new_body[bio_end:new_body.index('<w:sectPr')]
new_body = new_body[:bio_end] + new_body[new_body.index('<w:sectPr'):]

assert 'Listing' not in re.sub(r'<[^>]+>', '', new_body), 'a reference to a listing remains'
open(sys.argv[1], 'w', encoding='utf-8').write(xml[:body_start] + new_body)
print('replaced', len(replacements), 'paragraphs')
