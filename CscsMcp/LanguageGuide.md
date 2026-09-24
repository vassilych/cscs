# CSCS in five minutes

CSCS ("Customized Scripting in C#") is a small scripting language whose interpreter is built on the
Split-and-Merge parsing algorithm. It looks like C or JavaScript, but it is dynamically typed and has
its own conventions. Source: https://github.com/vassilych/cscs

Every example below runs as-is in this sandbox. Pass a whole script to `run_cscs`.

## Essentials

- Statements end with `;`. Blocks use `{ }` — always use braces, even for one-line bodies.
- No declarations: assigning a name creates it. Types are decided at run time.
- `print(a, b, c)` prints its arguments separated by one space, then a newline.
- Truth values are numbers: comparisons give `1` or `0`, and they print that way.
- `run_cscs` also returns the value of the script's last statement.
- Names of functions are case-insensitive (`print`, `Print`, `PRINT`).
- Members are called on variables, not on literals: write `s = "ab"; s.Upper()`, not `"ab".Upper()`.

## Values, loops and functions

```cscs
x = 10;
y = x * 2 + 1;
name = "CSCS";
print("Hello from", name, ":", y);
```

```cscs
function fib(n) {
  if (n < 2) { return n; }
  return fib(n - 1) + fib(n - 2);
}
print("fib(20) =", fib(20));
```

```cscs
total = 0;
for (i = 1; i <= 10; i++) { total += i; }
print("sum 1..10 =", total);
n = 0;
while (n < 3) { n++; }
do { n--; } while (n > 0);
print("n =", n);
```

Also available: `break`, `continue`, the ternary `cond ? a : b`, `+= -= *= /= %=`, `++` and `--`.

## Collections

Arrays are written with braces and indexed from 0.

```cscs
nums = {5, 3, 8, 1};
nums.Add(7);
nums.Sort();
print(nums, "size=", nums.Size, "first=", nums[0]);
squares = {};
for (v in nums) { squares.Add(v * v); }
print(squares.Join(", "));
```

Dictionaries use string keys. Walk their `Keys`:

```cscs
ages = {};
ages["ann"] = 31;
ages["bob"] = 27;
keys = ages.Keys;
for (i = 0; i < keys.Size; i++) { print(keys[i], "is", ages[keys[i]]); }
point = {"x": 3, "y": 4};
print("x =", point["x"]);
```

## Strings

```cscs
s = "Split-and-Merge";
print(s.Length, s.Upper(), s.Lower());
print(s.Substring(0, 5), s.IndexOf("and"), s.Replace("-", " "));
parts = s.Split("-");
print(parts.Size, "parts, last =", parts[2]);
print("contains Merge:", s.Contains("Merge"));
```

`+` joins a string with anything. Convert explicitly with `int("42")`, `double("1.5")`, `string(7)`.

## Classes

```cscs
class Shape {
  name = "";
  Shape(n) { name = n; }
  function Describe() { return name + " with area " + Area(); }
}
class Circle : Shape {
  r = 0;
  Circle(radius) { name = "circle"; r = radius; }
  function Area() { return Math.Round(Math.PI * r * r, 2); }
}
c = new Circle(2);
print(c.Describe());
```

Fields are declared with a default value; a constructor has the class's name. Inside methods, fields
are used by name (or `this.field`). Objects can live in collections:

```cscs
class Counter {
  count = 0;
  function Inc(k) { count += k; return count; }
}
c = new Counter();
c.Inc(2);
print("count =", c.Inc(3));
list = {new Counter(), new Counter()};
list[1].count += 10;
print("second =", list[1].count);
```

## Errors and switch

```cscs
function check(v) {
  if (v < 0) { throw "negative: " + v; }
  return Math.Sqrt(v);
}
try { check(-4); } catch (e) { print("caught:", e); }
switch (3) {
  case 1: case 2: print("small"); break;
  case 3: print("three"); break;
  default: print("other");
}
```

`case` labels fall through until a `break`, as in C.

## Math, conversions, JSON and regular expressions

```cscs
print(Math.Pow(2, 10), Math.Abs(-5), Math.Max(3, 9), Math.Floor(7.8));
print(int("42") + 1, double("1.5") * 2, string(7) + "!");
r = Math.Random();
print("random in [0,1):", r >= 0 && r < 1);
```

```cscs
v = 7;
print(v % 2 == 0 ? "even" : "odd");
data = GetVariableFromJson("{\"langs\": [\"CSCS\", \"C#\"]}");
print(data["langs"][0]);
m = regex("(\\d+)-(\\d+)", "range 10-20");
print(m);
```

## The last value

A script's final statement is returned even without `print`:

```cscs
function square(x) { return x * x; }
square(12);
```

## Precompiled functions: `cfunction` and `explain_cscs`

A `cfunction` is a function CSCS translates to C# and compiles, so it runs as native code instead
of being interpreted. It declares a return type and typed parameters (`int`, `double`, `string`):

```cscs
cfunction double hypot(double a, double b) { return Math.Sqrt(a * a + b * b); }
cfunction string parity(int n) {
  if (n % 2 == 0) { return "even"; }
  return "odd";
}
cfunction double sumTo(int n) {
  total = 0;
  for (i = 1; i <= n; i++) { total += i; }
  return total;
}
print(hypot(3, 4), parity(7), sumTo(100));
```

The translator handles most of the language. When it cannot translate a body, or the C# it
produces does not compile, the function quietly falls back to the interpreter and gives the same
answer, only without the speed-up:

```cscs
class Box {
  w = 0;
  Box(x) { w = x; }
  function Area() { return w * w; }
}
cfunction double boxOfArea(int n) {
  b = new Box(new Box(n).Area());
  return b.w;
}
print("boxOfArea(3) =", boxOfArea(3));
```

Pass scripts with `cfunction`s to **`explain_cscs`**, not `run_cscs`: it shows the generated C# for
each one, whether it compiles, or the exact reason it falls back — here an object built inside
another constructor's arguments, which the translator leaves to the interpreter. Chained
comparisons (`1 < n < 10`), `iff(...)`, and `continue` inside a `switch` all compile; the report
shows what they become. In this sandbox that C# is compiled but never executed, so every function runs
interpreted; `run_cscs` does not accept `cfunction` at all.

## What this sandbox does not allow

The playground runs untrusted scripts, so the interpreter is cut down: no files, directories,
processes, network, environment variables, threads, timers, `include`/`import`, debugger, or any
.NET access (`new` only creates CSCS classes). Compiled `cfunction` code is never executed here —
use `explain_cscs` to see it. Each run is limited to 5 seconds, 16,000 characters of output, 20,000
characters of script and 2,000 nested function calls; the full CSCS interpreter has none of these
restrictions.
