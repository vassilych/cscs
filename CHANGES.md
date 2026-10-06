# Changes

## September 2026: interpreter semantics

The interpreter now follows one set of rules everywhere, and `cfunction` compiles to the same
answers. Most of the old behavior was a quirk that no script would rely on on purpose, but a few
changes can change what an existing script prints. Each item below is pinned in
`Scripts/Samples/test.cscs`. `cscs/PRECOMPILATION.md` has the details.

### Changes that can alter a running script's results

These scripts ran before and now give a different answer.

| Change | Before | Now |
|---|---|---|
| **One truth rule**, used by `if`, `while`, `for`, `?:`, `!`, `&&`, `||` | A condition read the value's numeric field, so all text was false (unless an operation had left a number under it), and an empty list was true | A number is true unless it is 0 or NaN. Text is true unless it is empty, `"0"` or `"false"` (in any case). A list or map is true unless it is empty. `null` is never true |
| **`&&` and `\|\|` give 1 or 0** | `3 \|\| 0` was 3 and `1 < 2 && 5` was 5 | 1 and 1 |
| `0 && x \|\| 1` | 0: a false `&&` skipped the whole rest | 1: a false `&&` skips only its own right side |
| **Compound assignment is `x = x op y`** | Text counted as 0 | `5 += "3"` is `"53"`; `s = "7"; s += 1` is `"71"`; `"5" -= 3` is the same error that `"5" - 3` gives |
| **`++`/`--`** | `"7"++` was 1; `++` on an unset element was NaN | `"7"++` is 8 (numeric text counts as its number); anything else is `x + 1`; a postfix step returns the old value itself |
| **`return` and `finally`** | A `return` in `try` or `catch` was ignored after `finally`; an uncaught exception won over a `return` in `finally` | As in C#: a `return` in `try` or `catch` stands, and `finally` overrides it only by returning, breaking or continuing itself |
| **`Size` of text** | 0 | The text's length. `Size("abc")` takes a value (before, `"abc"` was read as a variable name) |
| **Class fields initialized in the class body** | `items = {}` was one list shared by every instance, so `b1.items.Add(x)` also showed up in `b2` | Each instance gets its own copy |
| **Enums** | `Colors.Type` was `NONE`; `Colors.Red.Type` was the member's name; `Colors.ToString(2)` was 1 | `ENUM`; `NUMBER` (use `.Name` for the name); `"Blue"` |
| **Named arguments** | `f(s = 1)` bound `s` and also created or overwrote a variable `s` in the caller | `f(s = 1)` only binds the parameter. Parentheses around the assignment (`f((s = 1))`), or a name that is not a parameter, still assigns |
| **A script function named like a command** (`show`, `copy`, `run`, ...) | Received its arguments as text: `show(n+1)` got `"n+1"` | Receives the values |
| **Loop bodies without braces** (`for`, `while`, `do`, `foreach`) | The rest of the script ran as the body, and `do x++; while (...)` hung | The body is one statement; an `if`/`else` chain counts as one statement |
| **Iterating a literal list** | `for (c : {"ab"})` went over the characters of `"ab"` | Goes over the elements, with `:`, `in` or `of` |
| **A list literal as the first operand** | `{1} \|\| 0` was `[1]`: the rest of the expression was dropped | 1 |
| **A bare `Trim`, `Sort`, `Reverse` or `DeepCopy` followed by an operator** | Took the rest of the expression as its arguments, and it was lost | Leaves the rest of the expression alone |
| **A member after a subscript** | `m[0].Size` made the next plain read of `m` return `m.Size` | Stays on that one expression |
| **Console host preprocessing** (`Utils.GetSubscript`, which moves declarations ahead of the script) | Went through a `class` or `namespace` body one statement at a time. A method could be hoisted out as a global function, or the script stopped with "Unbalanced curly braces" | The body is skipped whole. **A function declared inside a `namespace` is no longer also defined globally before the script runs**; call it through its namespace |

### New features (these were errors before)

- `try` without `catch`. An exception nobody catches passes on after `finally`.
- `if`/`else` without braces, including `else if` chains.
- Shifts `<<`, `>>`, `<<=`, `>>=`. Both sides are truncated to int, and the precedence is C#'s:
  after `+ -`, before the comparisons (`1 << 2 + 1` is 8).
- Typed parameters in plain functions, converted as a `cfunction` converts them:
  `function f(int n, string s = "d")`.
- Text indexing: `s[i]` is the character at `i`, on a variable or directly on a literal (`"abc"[1]`).
- Bare Math names: `Sqrt`, `Round`, `Pow` and the rest, next to `Math.*`.
- A caught value has `.Message` and `.Stack`: `catch (e) { print(e.Message); }`.
- `NameExists(x)` inside a larger expression, such as `NameExists(q) + 5`.
- Subscripts inside subscripts: `a[b[1]]`.
- Elements of an object's fields can be read, written, compounded and stepped:
  `b.l[1] = 5`, `b.l[1] += 2`, `++b.l[1]`.
- Methods of a field's collection: `obj.items.Add(x)`.
- `?:` with a list-literal condition, and nested ternaries in either branch.

### Console host

- `CSCS_SHARED_SCRIPT=0` (or `off`, `false`) skips downloading and running the shared script at
  startup.
- `CSCS_SHARED_SCRIPT_TIMEOUT` sets the download timeout in seconds. The default is 3, and a slow
  or unreachable server no longer holds up startup.

### Checking a script against the new rules

Run the script under the old build and the new one, and compare the output. Across the 65
scripts in this repository, the only change was `ooSamples.cscs`, which now runs further. The
mobile `unitTests.cscs` gave the same 133 passes and 3 errors under both builds. Its only
difference was `++` on an unset element, which is now 1 instead of NaN.

To see whether a `cfunction` compiles, and why it does not, use `explain_cscs` in the CSCS
Playground MCP.
