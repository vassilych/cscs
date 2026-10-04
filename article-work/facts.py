"""Facts pass over the revised article (run after revise.py): numbers and claims brought to
the code as of October 4, 2026. Measurements: Release build, Apple M1 Pro, pstime (CPU ms),
best of four runs of bench/table1.cscs; cold compile from probe/one.cscs vs none.cscs."""
import re
import sys
from xml.sax.saxutils import escape

path = sys.argv[1]
xml = open(path, encoding='utf-8').read()

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


def paragraphs():
    return re.findall(r'<w:p[ >].*?</w:p>', xml[xml.index('<w:body>'):], re.S)


def text(p):
    return ''.join(re.findall(r'<w:t[^>]*>([^<]*)</w:t>', p)).replace('&amp;', '&') \
        .replace('&lt;', '<').replace('&gt;', '>')


def replace_para(starts, new_xml):
    """The one paragraph whose text starts so, replaced whole."""
    global xml
    found = [p for p in paragraphs() if text(p).startswith(starts)]
    assert len(found) == 1, (starts, len(found))
    assert xml.count(found[0]) == 1, starts
    xml = xml.replace(found[0], new_xml)


def replace_text(old, new, count=1):
    """Text inside one run (escaped as the XML has it)."""
    global xml
    old_x, new_x = escape(old), escape(new)
    assert xml.count(old_x) == count, (old, xml.count(old_x))
    xml = xml.replace(old_x, new_x)


# Cold compile: 1.13 s with one cfunction against 0.11 s without (three runs each).
replace_text("a cold compile adds about 1.1 seconds to a run", "a cold compile adds about a second to a run")

# The generated code for compound() is split three ways since typed direct calls.
COMPOUND_CS_OLD_FIRST = "public static Variable compound("
COMPOUND_CS = '''
public static Variable compound(Interpreter __interpreter,
    List<string> __varStr, List<double> __varNum,
    List<int> __varInt /* , six more typed lists */)
{
    return compound__body(__interpreter,
        __varNum[0], __varNum[1], __varInt[0]);
}

public static Variable compound__body(Interpreter __interpreter,
    double __p0, double __p1, int __p2)
{
    double total = __p0;
    int i;
    for (i = 0; i < __p2; i++) {
        total = total * (1 + __p1);
    }
    return Variable.ConvertToVariable(Math.Round(total, 2));
}'''
paras = paragraphs()
start = next(i for i, p in enumerate(paras) if text(p).startswith("And the C# method generated for it"))
code_start = start + 2
assert text(paras[code_start]) == COMPOUND_CS_OLD_FIRST, text(paras[code_start])
code_end = code_start
while 'CodeSnippet' in paras[code_end + 1]:
    code_end += 1
block_from = xml.index(paras[start])
block_to = block_from
for k in range(start, code_end + 1):
    block_to = xml.index(paras[k], block_to) + len(paras[k])
old_block = xml[block_from:block_to]
assert xml.count(old_block) == 1
new_block = EMPTY.join([
    body_para("And the C# generated for it, reformatted, with seven unused temporaries omitted. The "
              "entry point the interpreter calls unpacks the typed lists it is handed; the body takes "
              "typed parameters, and the loop becomes a plain C# loop:"),
    snippet(COMPOUND_CS),
    body_para("A third method, compound__direct, lets other compiled functions call the body "
              "without going through the interpreter. The section on recursion below shows why that "
              "matters."),
])
xml = xml.replace(old_block, new_block)

# Coverage, the real-code measurements, the suites and the audits.
replace_text("Today it measures 1,142: 1,129 compile to C#, 12 fall back, and none disagree.",
             "Today it measures 1,315: 1,310 compile to C# and agree, one falls back, none disagree, "
             "and the remaining four are cases the interpreter itself rejects.")
replace_text("so the code people actually write was measured too: of the 468 cfunctions in the "
             "repository's own scripts, translated with their real signatures, 465 compile. Of the three "
             "that don't, two are refused on purpose and one guards a case where compiling once gave a "
             "wrong answer. Two script-level suites back it up. test_compiled.cscs has 1,016 assertions "
             "that run each case compiled and interpreted. test.cscs has 630 assertions on the language "
             "itself.",
             "so the code people actually write was measured too. All 468 cfunctions in the "
             "repository's own scripts compile with their real signatures, and so do 554 of 566 "
             "functions taken from other CSCS projects (mobile apps, a MAUI app, and web projects) and "
             "declared as cfunctions. Two script-level suites back it up. test_compiled.cscs has 1,266 "
             "assertions that run each case compiled and interpreted, and test.cscs has 721 assertions "
             "on the language itself. On top of those, 21 generated audits declare each shape twice, "
             "compiled and interpreted, and compare the two answers over many kinds of value, through "
             "both the synchronous and the asynchronous interpreter.")

# The shift example: CSCS has real shifts now, which compile.
replace_para("Of the 11 fallbacks, most are deliberate.", body_para(
    "Some fallbacks used to be deliberate. CSCS had no shift operator, so the interpreter answered 0 "
    "for 3 << 2, where C#'s shift gives 12. Compiling the expression would have given a {i}better{/i} "
    "answer, but a different one, so the translator refused it. That became the project's rule: "
    "{b}a divergence is worse than a fallback, even when the compiled answer is the more sensible "
    "one.{/b} The shift was then fixed in the right order: the interpreter gained real shifts first, "
    "and only after that did the translator compile them."))

# The loops: the counter change, and integer "%".
replace_para("The loops are what the 2020 article promised, and more.", body_para(
    "The loops are what the 2020 article promised, and more. For the numeric loop, the improvement "
    "came in two steps. Replacing CodeDom with Roslyn got the loop compiled at all. It then took 54 ms "
    "for 50,000 iterations, against 1,517 ms interpreted. Removing the redundant write-back described "
    "earlier brought it down to 1 ms. The integer loop gained from two later changes. A loop counter "
    "used to be declared double, so that / stays real division. It is now declared int whenever that "
    "provably can't change an answer: the counter is never divided, never multiplied outside an index, "
    "never assigned, and never copied into a new local. And % between two integers no longer goes "
    "through floating point. It uses the integer remainder, and handles separately the two cases where "
    "that would answer differently from the interpreter: a zero divisor, which gives NaN, and a "
    "negative dividend with no remainder, which gives -0. That change alone made a ten-million-"
    "iteration loop with % twenty times faster."))

# fib with typed returns, today.
replace_text("fib(30) now takes 60 ms, and fib(20) takes 1 ms, against 390 ms interpreted.",
             "fib(30) now takes 20 ms, and fib(20) takes 1 ms, against 415 ms interpreted.")

# The playground's allowlist keeps the Math module's bare names too since October 4.
replace_text("an allowlist of 113 kept functions (93 are removed);", "an allowlist of 140 kept functions (93 are removed);")

# Sizes.
replace_text("no change to a 12,000-line translator could have been accepted with confidence.",
             "no change to a 16,000-line precompiler could have been accepted with confidence.")
replace_text("The translator that compiles the same language is about twelve thousand, roughly a third "
             "of all the C# in the project.",
             "The precompiler that compiles the same language is about sixteen thousand, roughly 40 "
             "percent of the interpreter's C#.")
replace_para("Make it write things down.", body_para(
    "{b}Make it write things down.{/b} The most valuable artifact is not the code. It is "
    "PRECOMPILATION.md, 3,500 lines recording what was tried, measured, and reverted, and why. Several "
    "times a \"fix\" was rejected because the notes showed that the same idea had been measured before "
    "and cost something the tests couldn't see."))

# Table 1 (rows measured today; the two historical fib(20) rows are left as measured then).
table = re.search(r'<w:tbl>.*?</w:tbl>', xml, re.S).group(0)
new_table = table
for old, new in [(">2,558 ms<", ">1,720 ms<"), (">4 ms<", ">1 ms<"), (">~640×<", ">~1,700×<"),
                 (">1,055 ms<", ">1,110 ms<"), (">~1,000×<", ">~1,100×<"), (">11 ms<", ">19 ms<"),
                 (">390 ms<", ">415 ms<"), (">~390×<", ">~400×<"), ("→ 60 ms<", "→ 20 ms<")]:
    assert new_table.count(old) == 1, (old, new_table.count(old))
    new_table = new_table.replace(old, new)
xml = xml.replace(table, new_table)

# The sidebar.
replace_para("Precompiler coverage:", re.sub(r'<w:r>.*</w:r>', lambda m: runs(
    "Precompiler coverage: 1,310 of 1,315 constructs compile to C#, 1 falls back, 0 diverge from the "
    "interpreter, and 4 are cases the interpreter itself rejects (39 of 45 when this article was "
    "proposed); all 468 cfunctions in the repository’s own scripts compile, and 554 of 566 functions "
    "from other CSCS projects."), [p for p in paragraphs() if text(p).startswith("Precompiler coverage:")][0], flags=re.S))
replace_para("Regression suites:", re.sub(r'<w:r>.*</w:r>', lambda m: runs(
    "Regression suites: 1,266 compiled-versus-interpreted assertions, 721 language assertions, and 21 "
    "generated audits run through both the synchronous and the asynchronous interpreter."),
    [p for p in paragraphs() if text(p).startswith("Regression suites:")][0], flags=re.S))
replace_text("Cold Roslyn compile: about 1.1 s;", "Cold Roslyn compile: about 1 s;")
replace_para("Code size:", re.sub(r'<w:r>.*</w:r>', lambda m: runs(
    "Code size: Parser.cs 1,092 lines, of which the precedence engine is about 100; the precompiler "
    "about 16,000; all of the interpreter’s C# about 39,000."),
    [p for p in paragraphs() if text(p).startswith("Code size:")][0], flags=re.S))

open(path, 'w', encoding='utf-8').write(xml)
print('facts applied')
