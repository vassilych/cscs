using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitAndMerge;

namespace cscs.Tests.IntegrationTests.Precompiler
{
    /// <summary>
    /// Measures how much of the CSCS language survives precompilation.
    ///
    /// Each case is written twice -- once as a cfunction, once as a plain interpreted
    /// function with an identical body -- and the two results are compared. That makes the
    /// interpreter the reference implementation, which is the only definition of "correct"
    /// a cfunction has: the same script must behave the same way whether or not it is
    /// compiled.
    ///
    /// Two properties are enforced:
    ///   * a construct that compiles must never return a different answer than the
    ///     interpreter (silently wrong output is worse than a compile error);
    ///   * a construct that is known to work must keep working.
    /// </summary>
    [TestClass]
    public class PrecompilerCoverageFixture
    {
        class Construct
        {
            public string Name;
            public string TypedSig;   // cfunction needs declared argument types
            public string PlainSig;   // interpreted function does not
            public string Body;
            public string Call;

            public Construct(string name, string typedSig, string plainSig, string body, string call)
            {
                Name = name; TypedSig = typedSig; PlainSig = plainSig; Body = body; Call = call;
            }
        }

        // "SELF" is replaced by the name of the function being defined, so a case can recurse.
        static readonly Construct[] Constructs =
        {
            new Construct("arith",         "(double n)", "(n)", "return n*2 + 1 - 3/2;", "(10)"),
            new Construct("compound",      "(double n)", "(n)", "x=n; x+=5; x*=2; x-=1; x/=3; return x;", "(10)"),
            new Construct("if_else",       "(double n)", "(n)", "if (n > 5) { return 1; } else { return 2; }", "(10)"),
            new Construct("else_if",       "(double n)", "(n)", "if (n>100) {return 1;} elif (n>5) {return 2;} else {return 3;}", "(10)"),
            new Construct("while",         "(int n)",    "(n)", "i=0; t=0; while(i<n){t+=i; i++;} return t;", "(5)"),
            new Construct("for",           "(int n)",    "(n)", "t=0; for(i=0;i<n;i++){t+=i;} return t;", "(5)"),
            new Construct("break",         "(int n)",    "(n)", "t=0; for(i=0;i<n;i++){ if(i==3){break;} t+=i;} return t;", "(10)"),
            new Construct("continue",      "(int n)",    "(n)", "t=0; for(i=0;i<n;i++){ if(i%2==0){continue;} t+=i;} return t;", "(10)"),
            new Construct("nested_loop",   "(int n)",    "(n)", "t=0; for(i=0;i<n;i++){ for(j=0;j<n;j++){ t+=1; } } return t;", "(4)"),
            new Construct("ternary",       "(double n)", "(n)", "return n > 5 ? 100 : 200;", "(10)"),
            new Construct("logical",       "(double n)", "(n)", "if (n>1 && n<100 || n==0) { return 1; } return 0;", "(10)"),
            new Construct("not",           "(double n)", "(n)", "if (!(n>100)) { return 1; } return 0;", "(10)"),
            new Construct("int_div",       "(int a, int b)", "(a, b)", "return a/b;", "(3, 2)"),
            new Construct("modulo",        "(double n)", "(n)", "return n % 3;", "(10)"),
            // Conversions must be expressions, not statements: as statements they spliced
            // into the middle of an expression and "int(n) + 1" dropped the "+ 1".
            // int() truncates toward zero -- Convert.ToInt32 rounds, which diverged.
            new Construct("convert_int",  "(double n)", "(n)", "return int(n) + 1;", "(3.7)"),
            new Construct("convert_half", "(double n)", "(n)", "return int(n);", "(2.5)"),
            new Construct("convert_str",  "(double n)", "(n)", "return string(n) + \"!\";", "(5)"),
            // & is bitwise AND now that the reference operator moved to @.
            new Construct("bitwise",      "(int n)",    "(n)", "return n & 3;", "(6)"),
            // Exponentiation is right-associative: 2**3**2 is 2**(3**2) = 512, not 64.
            // Falls back rather than compiling: C# has no ** operator, so this guards
            // that compiled and interpreted still agree on the value.
            new Construct("power_assoc", "(double n)", "(n)", "return 2**3**n;", "(2)"),
            new Construct("power_chain",  "(double n)", "(n)", "return n ** 2 ** 2;", "(2)"),
            // "**" is not C#; it becomes Math.Pow, grouped to the right as the interpreter
            // groups it. A chain of three with a variable in it still falls back, but the
            // literal chain shows the grouping: left-grouping would give 64, not 512.
            new Construct("power_two",    "(double n)", "(n)", "return n ** 2;", "(3)"),
            new Construct("power_lits",   "(double n)", "(n)", "return 2 ** 3 ** 2;", "(0)"),
            new Construct("power_assign", "(double n)", "(n)", "r = n ** 3; return r;", "(2)"),
            // Mixed with another operator it is left alone: picking the operands out means
            // honouring the precedence of everything around them.
            new Construct("power_mixed",  "(double n)", "(n)", "return 1 + n ** 2;", "(3)"),
            new Construct("power_tail",   "(double n)", "(n)", "r = n ** 3 + 1; return r;", "(2)"),
            new Construct("power_call",   "(double n)", "(n)", "return Math.Abs(n) ** 2;", "(3)"),
            // A parenthesised base, and an element as the base with an addition outside the
            // call: both used to be mis-handled by the operand scan.
            new Construct("power_elem",   "(double n)", "(n)", "a={2,3}; return a[1] ** 2 + n;", "(1)"),
            new Construct("power_paren",  "(double n)", "(n)", "return (n + 1) ** 2;", "(3)"),
            new Construct("power_pboth",  "(double n)", "(n)", "return (n + 1) ** (n - 1);", "(3)"),
            // A nested call as a later argument: a plain Split(',') used to tear it apart.
            new Construct("math_nested2", "(double n)", "(n)", "return Math.Max(1, Math.Min(n, 5));", "(3)"),
            new Construct("string_concat", "(string s)", "(s)", "return s + \"-suffix\";", "(\"abc\")"),
            // A condition holding a string literal is not a "known expression", so its
            // arguments used to go unresolved and "s" was emitted bare.
            new Construct("string_eq",     "(string s)", "(s)", "if (s == \"ab\") { return 1; } return 0;", "(\"ab\")"),
            new Construct("string_ne",     "(string s)", "(s)", "if (s != \"ab\") { return 1; } return 0;", "(\"zz\")"),
            // Assigning to an argument once declared a shadowing local while every read
            // still resolved to the argument slot, so this silently returned the original
            // value -- and, in a loop, never terminated.
            new Construct("arg_reassign",  "(string s)", "(s)", "r=\"\"; while (s != \"\") { r += \"x\"; s = \"\"; } return r;", "(\"q\")"),
            new Construct("string_len",    "(string s)", "(s)", "return s.Length;", "(\"abcde\")"),
            new Construct("string_upper",  "(string s)", "(s)", "return s.Upper;", "(\"abc\")"),
            // Same member on a local rather than an argument, where the type is not tracked.
            new Construct("local_upper",   "(string s)", "(s)", "t = s; return t.Upper;", "(\"AbC\")"),
            new Construct("string_sub",    "(string s)", "(s)", "return s.Substring(1,3);", "(\"abcde\")"),
            new Construct("string_idx",    "(string s)", "(s)", "return s.IndexOf(\"c\");", "(\"abcde\")"),
            new Construct("string_repl",   "(string s)", "(s)", "return s.Replace(\"a\",\"z\");", "(\"abcde\")"),
            new Construct("string_split",  "(string s)", "(s)", "a = s.Split(\",\"); return a[1];", "(\"x,y,z\")"),
            new Construct("math_calls",    "(double n)", "(n)", "return Math.Round(Math.Sqrt(n) + Math.Abs(-2.5), 3);", "(16)"),
            new Construct("array_lit",     "(int n)",    "(n)", "a = {1,2,3}; return a[n];", "(1)"),
            new Construct("array_add",     "(int n)",    "(n)", "a = {}; for(i=0;i<n;i++){ a.Add(i); } return a.Size;", "(4)"),
            // .Add on a collection local maps to Variable.AddVariable. It sits inside a
            // loop, so the callback it replaced was paid on every iteration.
            new Construct("array_add_expr","(int n)",   "(n)", "a={}; for(i=0;i<n;i++){ a.Add(i*2); } return a[2]+n;", "(5)"),
            new Construct("array_size",    "(int n)",    "(n)", "a = {1,2,3,4}; return a.Size + n;", "(1)"),
            // Iterating a collection: the loop counter is declared double so "/" is not
            // integer division, and Variable's indexer takes an int.
            new Construct("array_iterate","(int n)",    "(n)", "a={1,2,3}; t=0; for(i=0;i<a.Size;i++){ t+=a[i]; } return t+n;", "(0)"),
            // Read-modify-write through the indexer, which C# cannot compound-assign.
            new Construct("elem_compound","(int n)",    "(n)", "a={10,20}; a[0] -= 3; a[1] /= 2; a[0]++; return a[0]+a[1]+n;", "(1)"),
            new Construct("array_assign",  "(int n)",    "(n)", "a = {1,2,3}; a[1] = 99; return a[1] + n;", "(1)"),
            new Construct("map_lit",       "(string s)", "(s)", "m = {\"k1\":\"v1\",\"k2\":\"v2\"}; return m[s];", "(\"k2\")"),
            new Construct("map_assign",    "(string s)", "(s)", "m = {}; m[\"a\"]=1; m[\"b\"]=2; return m[s];", "(\"b\")"),
            // Indexed assignment into a literal-initialised map. The map_assign case below
            // starts from "m = {}", whose declaration is deferred, so it exercises a
            // different path and still falls back.
            new Construct("map_set",       "(string s)", "(s)", "m = {\"z\":\"0\"}; m[\"a\"]=1; m[\"b\"]=2; return m[s];", "(\"b\")"),
            new Construct("try_catch",     "(double n)", "(n)", "try { throw \"boom\"; } catch(e) { return 42; } return 0;", "(1)"),
            // Guards the caught-variable binding: the interpreter binds it to the thrown
            // string, so compiled code must use Exception.Message, not ToString().
            new Construct("catch_value",   "(double n)", "(n)", "try { throw \"boom\"; } catch(e) { return e; } return \"no\";", "(1)"),
            new Construct("recursion",     "(int n)",    "(n)", "if (n<=1) { return 1; } return n * SELF(n-1);", "(5)"),
            new Construct("call_cscs_fn",  "(double n)", "(n)", "return helper(n) + 1;", "(10)"),
            // These three shapes exposed a silent divergence: the argument scan consumed
            // every remaining token, so "helper(n*2) + 1" compiled cleanly and dropped the
            // "+ 1" from the answer.
            new Construct("call_expr_arg", "(double n)", "(n)", "return helper(n*2) + 1;", "(10)"),
            new Construct("call_twice",    "(double n)", "(n)", "return helper(n) + helper(n*2);", "(10)"),
            new Construct("call_nested",   "(double n)", "(n)", "return helper(helper(n)) * 2;", "(10)"),
            // "+" is the one operator whose meaning depends on the operand types, and a
            // call's type is not known until it runs. Translating it to a fixed AsDouble()
            // or AsString() had to guess, and guessing wrong did not fail to compile -- it
            // quietly changed the answer. These four pin every combination down.
            new Construct("add_str_str",   "(string s)", "(s)", "return strHelper(1) + strHelper(2);", "(\"a\")"),
            new Construct("add_str_num",   "(string s)", "(s)", "return strHelper(1) + 1;", "(\"a\")"),
            new Construct("add_num_num",   "(string s)", "(s)", "return helper(2) + helper(2);", "(\"a\")"),
            new Construct("add_lit_call",  "(string s)", "(s)", "return \"a\" + strHelper(1);", "(\"a\")"),
            // A string member with no C# equivalent becomes a callback, so its result lands
            // in the same ambiguous "+".
            new Construct("add_members",   "(string s)", "(s)", "return s.First + s.Last;", "(\"abcd\")"),
            new Construct("coll_ends",     "(int n)",    "(n)", "a={7,8,9}; return a.First + a.Last + n;", "(0)"),
            new Construct("coll_ends_str", "(string s)", "(s)", "a={\"p\",\"q\",\"r\"}; return a.First + a.Last;", "(\"a\")"),
            // C# has no "<" or ">" on strings, so these become CompareCscs calls. The
            // rewrite runs on the whole statement: the tokenizer splits on the relational
            // operator, so by the token loop the two sides are in different tokens.
            new Construct("str_gt",        "(string s)", "(s)", "if (s > \"aa\") { return 1; } return 0;", "(\"abcd\")"),
            new Construct("str_gt_false",  "(string s)", "(s)", "if (s > \"zz\") { return 1; } return 0;", "(\"abcd\")"),
            // The receiver has to be a name, so a literal on the left swaps sides and flips
            // the operator.
            new Construct("str_lit_left",  "(string s)", "(s)", "if (\"zz\" > s) { return 1; } return 0;", "(\"abcd\")"),
            new Construct("str_two_args",  "(string s, string t)", "(s, t)", "if (s < t) { return 1; } return 0;", "(\"abcd\", \"b\")"),
            new Construct("str_range",     "(string s)", "(s)", "if (s >= \"abcd\" && s <= \"z\") { return 1; } return 0;", "(\"abcd\")"),
            // A numeric comparison beside a string one must stay numeric.
            new Construct("str_mixed_cmp", "(string s, double n)", "(s, n)", "if (n > 1 && s > \"aa\" && n < 9) { return 1; } return 0;", "(\"abcd\", 5)"),
            new Construct("str_while_cmp", "(string s)", "(s)", "r=0; while (s < \"b\") { s = \"z\"; r++; } return r;", "(\"abcd\")"),
            // Math.Ceil is the CSCS spelling; C# only has Math.Ceiling.
            new Construct("math_ceil",     "(double x)", "(x)", "return Math.Ceil(x) + Math.Ceiling(x) + Math.Ceiling(-x);", "(1.2)"),
            // A subscript read out of another collection is a Variable, so the index cannot
            // be cast to int -- Variable now has an indexer for each type a subscript can
            // arrive as instead.
            new Construct("map_by_key",    "(int n)",    "(n)", "m={\"a\":1,\"b\":2}; t=0; ks=m.Keys; for(i=0;i<ks.Size;i++){ t += m[ks[i]]; } return t+n;", "(0)"),
            // A map literal in an expression position, which needs building rather than
            // emitting its braces verbatim.
            new Construct("map_literal_rhs","(int n)",   "(n)", "m={}; m[\"x\"] = {\"y\" : 5}; return m[\"x\"][\"y\"] + n;", "(0)"),
            // Locals are declared double so "/" is real division, and C# has no bitwise
            // compound assignment on a double.
            new Construct("bit_compound",  "(int n)",    "(n)", "y=12; y&=10; z=5; z|=2; w=6; w^=3; return y*100 + z*10 + w + n;", "(0)"),
            // "!b" after "&&" arrives glued to the name, because the tokenizer splits on
            // "&&" and not on the "!".
            new Construct("not_bool_var",  "(int n)",    "(n)", "a=true; b=false; if (a && !b) { return 1; } return n;", "(0)"),
            // for (v in a) is left to the interpreter: the loop variable is a Variable, and
            // accumulating one into a numeric local needs an implicit conversion that would
            // make the "+" overloads ambiguous.
            new Construct("foreach_in",    "(int n)",    "(n)", "a={1,2,3}; t=0; for (v in a) { t += v; } return t+n;", "(0)"),
            // "a[0] += \"y\"" on a string element: forcing the current value to a number read
            // it as 0, so the result was "0y" rather than "xy" -- a wrong answer, not a
            // failure to compile.
            new Construct("elem_str_add",  "(int n)",    "(n)", "a={\"x\"}; a[0] += \"y\"; return a[0];", "(0)"),
            // A quote inside a subscript is a map key, not a string operand, so the
            // expression around it is still arithmetic.
            new Construct("map_arith",     "(int n)",    "(n)", "m={\"a\":1}; return m[\"a\"]*100 + n;", "(0)"),
            new Construct("map_elem_ops",  "(int n)",    "(n)", "m={\"a\":1}; m[\"a\"] += 5; m[\"a\"]++; return m[\"a\"]*10 + n;", "(0)"),
            // "==" between a string and a number: CSCS compares the two AsString() forms, so
            // "05" == 5 is false while "5" == 5 is true. C# has no operator for the pair at
            // all, and neither side alone settles what the comparison means.
            new Construct("eq_str_num",    "(string s)", "(s)", "if (s == 5) { return 1; } return 0;", "(\"5\")"),
            new Construct("eq_str_pad",    "(string s)", "(s)", "if (s == 5) { return 1; } return 0;", "(\"05\")"),
            new Construct("eq_num_str",    "(double n)", "(n)", "if (n == \"5\") { return 1; } return 0;", "(5)"),
            new Construct("ne_str_num",    "(string s)", "(s)", "if (s != 5) { return 1; } return 0;", "(\"9\")"),
            // A try nested inside a try. Every catch block used to lose two characters of
            // indent, because it emits its own "{" while the matching "}" is a statement of
            // its own, and nesting drove the depth below zero.
            new Construct("nested_try",    "(int n)",    "(n)", "r=0; try { try { throw \"x\"; } catch(e2) { r=1; } } catch(e) { r=2; } return r+n;", "(0)"),
            // The caught variable holds the thrown string, not the C# exception: using it
            // anywhere but a return -- rethrowing it, here -- otherwise concatenated
            // "System.ArgumentException: inner" and a stack trace.
            new Construct("catch_rethrow", "(int n)",    "(n)", "try { try { throw \"inner\"; } catch(a) { throw \"outer:\" + a; } } catch(b) { return b; } return \"no\";", "(0)"),
            new Construct("try_in_loop",   "(int n)",    "(n)", "t=0; for(i=0;i<3;i++){ try { if(i==1){ throw \"e\"; } t+=1; } catch(e){ t+=10; } } return t+n;", "(0)"),
            // A call in an "if" condition. A numeric condition is resolved without going
            // through the token loop, so the call came out as a bare name.
            new Construct("call_in_cond",  "(double n)", "(n)", "if (helper(n) > 5) { return 1; } return 0;", "(2)"),
            // The hoist is suppressed for both of these. Moving the call out of a "&&" would
            // run it even when the left side is false, and out of a "while" would run it once
            // instead of on every iteration. The "&&" form still compiles -- the call is not
            // the first operand, so it goes through the token loop and becomes a callback --
            // while the "while" form falls back.
            new Construct("call_cond_and", "(double n)", "(n)", "if (n > 100 && helper(n) > 5) { return 1; } return 0;", "(2)"),
            new Construct("call_in_while", "(double n)", "(n)", "i=0; while (helper(i) < n) { i++; } return i;", "(10)"),
            // "m[\"k\"].Size" needs the Variable, so the numeric conversion the subscript
            // normally gets must not be applied when a member follows it.
            new Construct("map_of_array",  "(int n)",    "(n)", "m={}; m[\"k\"]={1,2,3}; return m[\"k\"][1] + m[\"k\"].Size + n;", "(0)"),
            // "===" is not C#. Two strings compare ordinally, which is what the interpreter
            // does once its type check passes.
            new Construct("strict_eq",     "(string s)", "(s)", "if (s === \"a\") { return 1; } return 0;", "(\"a\")"),
            new Construct("strict_eq_case","(string s)", "(s)", "if (s === \"A\") { return 1; } return 0;", "(\"a\")"),
            new Construct("strict_ne",     "(string s)", "(s)", "if (s !== \"a\") { return 1; } return 0;", "(\"b\")"),
            // "===" on numbers falls back: it behaves like "==" there, but rewriting the
            // operator puts the statement back through a path that no longer resolves the
            // argument names.
            new Construct("strict_eq_num", "(double n)", "(n)", "if (n === 5) { return 1; } return 0;", "(5)"),
            new Construct("strict_ne_num", "(double n)", "(n)", "if (n !== 5) { return 1; } return 0;", "(9)"),
            // Mixed types stay false, and must not be folded to a constant.
            new Construct("strict_mixed", "(string s)", "(s)", "if (s === 5) { return 1; } return 0;", "(\"5\")"),
            // A global read through the interpreter, so that "g = g + 1" is not a local
            // referring to itself. The write-back is the sync the assignment already emits.
            new Construct("global_read",   "(int n)",    "(n)", "return gcount + n;", "(1)"),
            new Construct("global_mutate", "(int n)",    "(n)", "gcount = gcount + 1; return gcount + n;", "(0)"),
            // A loop counted down from an argument. The initialiser was emitted verbatim, so
            // the argument name in it was never declared.
            new Construct("for_from_arg",  "(int n)",    "(n)", "t=0; for(i=n;i>0;i--){ t+=i; } return t;", "(4)"),
            // Globals in the shapes the read has to survive: a comparison, a subscript and a
            // member. Variable carries the operators and members these need.
            new Construct("global_cmp",    "(int n)",    "(n)", "if (gcount > 5) { return 1; } return n;", "(0)"),
            new Construct("global_index",  "(int n)",    "(n)", "return garr[1] + garr.Size + n;", "(0)"),
            new Construct("global_string", "(int n)",    "(n)", "return gstr + \"!\";", "(0)"),
            // A map inside a map, assigned through two subscripts. The receiver chain still
            // cast its index to int, so a string key came out as "m[(int)(\"a\")]", and the
            // indexed target was also being registered as if it were a variable name.
            new Construct("map_in_map",    "(int n)",    "(n)", "m={}; m[\"a\"]={}; m[\"a\"][\"b\"]={\"c\":9}; return m[\"a\"][\"b\"][\"c\"] + n;", "(0)"),
            // Left interpreted: accumulating a global into a numeric local would need an
            // implicit Variable-to-double conversion, which would make the "+" overloads
            // ambiguous.
            new Construct("global_accum",  "(int n)",    "(n)", "t=0; for(i=0;i<n;i++){ t += gcount; } return t;", "(3)"),
            // A collection can hold mixed types, so an element's type is not known until it
            // runs. Converting a subscript to a number to add it read a string element as 0.
            new Construct("mixed_add",     "(int n)",    "(n)", "a={1,\"two\",3}; return a[1] + a[0];", "(0)"),
            // A condition that is a value rather than a comparison. CSCS counts a value as
            // true only when its number is not zero -- even the string "true" is false --
            // which is why the test is on the number and not AsBool.
            new Construct("truthy_elem",   "(int n)",    "(n)", "a={1,0,\"abc\",\"\"}; t=0; for(i=0;i<4;i++){ if (a[i]) { t+=1; } } return t+n;", "(0)"),
            new Construct("truthy_key",    "(int n)",    "(n)", "m={\"t\":true,\"f\":false}; t=0; if (m[\"t\"]) { t+=1; } if (m[\"f\"]) { t+=10; } return t+n;", "(0)"),
            // Left interpreted: the shorthand forms of a global update. The long form
            // "g = g + 1" compiles.
            new Construct("global_step",   "(int n)",    "(n)", "gcount += 5; gcount++; return gcount + n;", "(0)"),
            // A literal as a ternary branch. Braces end a statement, so the literal has to be
            // kept whole after "?" and ":" the way it already was after "=", "(" and ",".
            new Construct("tern_literal",  "(int n)",    "(n)", "a = n > 0 ? {1,2,3} : {4}; return a.Size + a[0] + n;", "(1)"),
            new Construct("tern_lit_else", "(int n)",    "(n)", "a = n > 5 ? {7} : {8,9}; return a.Size + a[0] + n;", "(1)"),
            // The branch that is not taken has to be skipped over, and a literal there holds
            // commas -- and, for a map, the ":" the skip is looking for.
            new Construct("tern_lit_skip", "(int n)",    "(n)", "a = n > 0 ? {1,2,3} : {4}; return a.Size + a[0] + n;", "(0)"),
            new Construct("tern_map_skip", "(int n)",    "(n)", "m = n > 0 ? {\"a\":1,\"b\":2} : {\"c\":3}; return m.Size + n;", "(0)"),
            // The whole toolkit at once, to catch a regression that only shows up when the
            // pieces combine: Split, string members on a collection element, chained
            // subscripts into nested literals, a literal in a ternary branch and bitwise
            // compound assignment.
            // A local that holds a collection element. The declaration follows the first
            // assignment, so "v = 0" made a double and the later "v = m[key]" had nowhere to
            // put a Variable; such locals are Variable throughout instead.
            new Construct("elem_to_local", "(string k)", "(k)", "m={}; m[\"x\"]=7; v=0; v=m[k]; return v + 1;", "(\"x\")"),
            new Construct("elem_to_str",   "(int n)",    "(n)", "a={\"p\",\"q\"}; v=\"\"; v=a[1]; return v + \"!\";", "(0)"),
            new Construct("elem_in_loop",  "(int n)",    "(n)", "a={5,6,7}; v=0; for(i=0;i<a.Size;i++){ v=a[i]; } return v + n;", "(1)"),
            new Construct("elem_then_math","(int n)",    "(n)", "a={5,6}; v=0; v=a[0]; v=v*2+1; return v + n;", "(1)"),

            // A local holding a Variable keeps the script's spelling for members, compares
            // against a string, and accumulates elements whose type is not known until it
            // runs.
            new Construct("elem_member",   "(int n)",    "(n)", "a={\"hello\"}; v=\"\"; v=a[0]; return v.Length + n;", "(0)"),
            new Construct("elem_upper",    "(string s)", "(s)", "p=s.Split(\",\"); v=\"\"; v=p[1]; return v.Upper;", "(\"a,bc,d\")"),
            // Accumulating elements into a string local. Reading them as numbers first built
            // "000" for {"a","b","c"}, where the interpreter concatenates.
            new Construct("acc_str",       "(int n)",    "(n)", "c1={\"a\",\"b\",\"c\"}; r=\"\"; for (i=0;i<3;i++) { r += c1[i]; } return r;", "(0)"),
            new Construct("acc_mixed",     "(int n)",    "(n)", "c1={1,\"x\",2}; r=\"\"; for (i=0;i<3;i++) { r += c1[i]; } return r;", "(0)"),
            new Construct("acc_num",       "(int n)",    "(n)", "c1={4,1,3}; t=0; for (i=0;i<3;i++) { t += c1[i]; } return t + n;", "(0)"),
            new Construct("acc_call",      "(int n)",    "(n)", "c1={4,-1,3}; t=0; for (i=0;i<3;i++) { t += Math.Abs(c1[i]); } return t + n;", "(0)"),
            // A string argument's Length is a number, whatever its owner is.
            new Construct("cond_strlen",   "(string s)", "(s)", "i=0; if (i < s.Length) { return 1; } return 0;", "(\"abcd\")"),
            new Construct("while_strlen",  "(string s)", "(s)", "i=0; t=0; while (i < s.Length) { t++; i++; } return t;", "(\"abcd\")"),
            // Unary minus on an element: the interpreter negates the numeric field, so a
            // string element is -0 rather than the parsed text.
            new Construct("neg_elem",      "(int n)",    "(n)", "c1={4,1,3}; return -c1[1] + n;", "(0)"),
            new Construct("neg_elem_str",  "(int n)",    "(n)", "c1={\"xy\",1}; r = -c1[0]; return string(r);", "(0)"),
            // Two elements compared. Reading both as numbers first answered false for
            // "pear" > "fig": a string orders by text, and only the runtime type knows which
            // rule applies.
            new Construct("cmp_elem",      "(int n)",    "(n)", "c1={\"pear\",\"fig\"}; if (c1[0] > c1[1]) { return 1; } return 0;", "(0)"),
            new Construct("cmp_elstr2",    "(int n)",    "(n)", "c1={\"fig\",\"pear\"}; if (c1[1] > c1[0]) { return 1; } return 0;", "(0)"),
            new Construct("cmp_elle",      "(int n)",    "(n)", "c1={\"fig\",\"pear\"}; if (c1[0] <= c1[1]) { return 1; } return 0;", "(0)"),
            new Construct("cmp_elnum",     "(int n)",    "(n)", "c1={4,1,3}; if (c1[0] > c1[1]) { return 1; } return 0;", "(0)"),
            new Construct("cmp_elmath",    "(double n)", "(n)", "c1={4,1,3}; if (c1[0] * 2 > 7) { return 1; } return 0;", "(0)"),
            // An argument list still needs a number, whatever the comparison around it does.
            new Construct("cmp_elcall",    "(int n)",    "(n)", "c1={4,1,3}; if (Math.Abs(c1[1]) > 0.5) { return 1; } return 0;", "(0)"),
            new Construct("cmp_elwhile",   "(int n)",    "(n)", "c1={4,1,3}; i=0; t=0; while (c1[i] > 0) { t++; i++; if (i>2) { break; } } return t;", "(0)"),
            // A comparison in a ternary's condition, and one in a nested branch.
            new Construct("tern_cmp",      "(string s)", "(s)", "return s > \"m\" ? \"hi\" : \"lo\";", "(\"z\")"),
            new Construct("tern_cmp_loc",  "(string s)", "(s)", "w=s; r = w > \"m\" ? 1 : 2; return r;", "(\"z\")"),
            new Construct("tern_cmp_nest", "(string s)", "(s)", "return s > \"m\" ? (s > \"y\" ? \"hi\" : \"mid\") : \"lo\";", "(\"z\")"),
            new Construct("tern_cmp_num",  "(double n)", "(n)", "return n > 5 ? 10 : 20;", "(9)"),
            // A comparison assigned rather than tested.
            new Construct("asg_cmp",       "(string s)", "(s)", "r = s > \"m\"; if (r) { return 1; } return 0;", "(\"z\")"),
            new Construct("asg_cmp_and",   "(string s)", "(s)", "r = s > \"a\" && s < \"zz\"; if (r) { return 1; } return 0;", "(\"m\")"),
            // A conversion whose argument is a map subscript: the quotes around the key used
            // to be mangled, and Convert cannot take the Variable a subscript yields.
            new Construct("conv_map_str",  "(int n)",    "(n)", "m={\"a\":3}; return string(m[\"a\"]);", "(0)"),
            new Construct("conv_map_int",  "(int n)",    "(n)", "m={\"a\":3}; return int(m[\"a\"]) + n;", "(0)"),
            new Construct("conv_map_dbl",  "(int n)",    "(n)", "m={\"a\":3}; return double(m[\"a\"]) + n;", "(0)"),
            new Construct("loc_str_cmp",   "(string s)", "(s)", "w=s; if (w > \"aa\") { return 1; } return 0;", "(\"abcd\")"),
            new Construct("loc_str_lit",   "(int n)",    "(n)", "w=\"pear\"; if (w < \"plum\") { return 1; } return 0;", "(0)"),
            new Construct("loc_num_cmp",   "(double x)", "(x)", "w=x; if (w > 9) { return 1; } return 0;", "(10)"),
            // A local given a string and later a number has no single C# type, so it stays
            // interpreted: guessing "string" here would order 3 against 20 as text.
            new Construct("loc_mixed_cmp", "(string s)", "(s)", "w=s; w=3; if (w > 20) { return 1; } return 0;", "(\"7\")"),
            new Construct("elem_str_cmp",  "(int n)",    "(n)", "a={\"pear\",\"fig\"}; v=\"\"; v=a[0]; if (v > \"grape\") { return 1; } return 0;", "(0)"),
            new Construct("elem_sum",      "(int n)",    "(n)", "a={1,2,3}; v=0; for(i=0;i<a.Size;i++){ v = v + a[i]; } return v + n;", "(0)"),
            new Construct("elem_swap",     "(int n)",    "(n)", "a={1,2}; t=0; t=a[0]; a[0]=a[1]; a[1]=t; return a[0]*10+a[1]+n;", "(0)"),
            new Construct("elem_ternary",  "(int n)",    "(n)", "a={4,9}; v=0; v = n > 0 ? a[1] : a[0]; return v + n;", "(1)"),

            // A nested literal assigned straight to a local. The statement form had its own
            // element loop, which emitted the inner braces verbatim.
            // The loop is turned inside out so the call still runs on every iteration.
            // "continue" must re-run it and "break" must leave, which these two check.
            // The body runs once even when the condition is false to begin with, which is
            // what distinguishes this from a while loop, and nested do-loops each have to
            // find their own closing "while".
            // A break inside a switch leaves the enclosing loop, so the wrapper that gives
            // break a nearer target is left out when there is a real loop to reach. Without
            // a loop the wrapper stays, and execution continues after the switch.
            // Writing an element of a global. Reading one already worked; writing was read
            // as a declaration of a local and emitted "double garr[0]=9".
            new Construct("gelem_arr",    "(int n)",     "(n)", "garr[0] = 9; return garr[0] + garr[1] + n;", "(0)"),
            new Construct("gelem_map",    "(int n)",     "(n)", "gmap[\"k\"] = 7; return gmap[\"k\"] + n;", "(0)"),
            new Construct("gelem_newkey", "(int n)",     "(n)", "gmap[\"z\"] = 5; return gmap[\"z\"] + n;", "(0)"),
            new Construct("gelem_2d",     "(int n)",     "(n)", "grid[0][1] = 9; return grid[0][1] + grid[1][0] + n;", "(0)"),
            new Construct("gelem_idx",    "(int n)",     "(n)", "garr[n] = 9; return garr[1] + n;", "(1)"),
            new Construct("gelem_loop",   "(int n)",     "(n)", "for (i=0;i<3;i++) { garr[i] = i; } return garr[0]+garr[1]+garr[2]+n;", "(0)"),
            // A local of the same name wins: it is a real C# variable and needs no callback.
            new Construct("gelem_shadow", "(int n)",     "(n)", "garr = {7,8}; garr[0] = 1; return garr[0] + garr[1] + n;", "(0)"),
            // A subscript straight after a call indexes what the call returned.
            new Construct("call_idx",     "(int n)",     "(n)", "return build()[1] + n;", "(0)"),
            new Construct("call_idx_str", "(string s)",  "(s)", "return s.Split(\",\")[1];", "(\"a,b\")"),
            // Trim, which is C#'s own in the interpreter. Only the call form is mapped.
            new Construct("trim_cmp",     "(string s)",  "(s)", "if (s.Trim() == \"ab\") { return 1; } return 0;", "(\" ab \")"),
            new Construct("trim_ret",     "(string s)",  "(s)", "return \"[\" + s.Trim() + \"]\";", "(\"  ab  \")"),
            new Construct("trim_len",     "(string s)",  "(s)", "return s.Trim().Length;", "(\" abc \")"),
            new Construct("trim_asg",     "(string s)",  "(s)", "w = s.Trim(); return w + \"!\";", "(\" ab \")"),
            // A loop variable that shadows an existing local, read through a subscript.
            new Construct("fe_shadow",    "(string s)",  "(s)", "m2={}; for (w in s.Split(\",\")) { k=w.Substring(0,1); if (m2.Contains(k)) { m2[k] += \",\" + w; } else { m2[k] = w; } } ks2=m2.Keys; ks2.Sort(); r=\"\"; for (k in ks2) { r += k + \":\" + m2[k] + \";\"; } return r;", "(\"ax,by,az\")"),
            // A call inside a subscript: the index is worked out first, so that the subscript
            // is built from a plain name.
            new Construct("idx_call",     "(int n)",     "(n)", "c1={5,6,7}; return c1[helper(0)];", "(0)"),
            new Construct("idx_convert",  "(int n)",     "(n)", "c1={5,6,7}; return c1[int(\"1\")] + n;", "(0)"),
            new Construct("idx_call_asg", "(int n)",     "(n)", "c1={5,6,7}; c1[helper(0)] = 9; return c1[0] + n;", "(0)"),
            // A Math call needs none of that: it is C# already.
            new Construct("idx_math",     "(int n)",     "(n)", "c1={5,6,7}; return c1[Math.Abs(n)] + n;", "(0)"),
            // A source that calls something is assigned to a temporary first, so that the
            // whole pipeline builds the call rather than the resolver alone.
            new Construct("fe_split",     "(string s)",  "(s)", "r=\"\"; for (w in s.Split(\" \")) { r += w + \"|\"; } return r;", "(\"a b\")"),
            new Construct("fe_call",      "(int n)",     "(n)", "t=0; for (q in build()) { t += q; } return t + n;", "(0)"),
            // "for (x in v)" where what v holds is only known at run time: a collection walks
            // its elements, a string its characters, a scalar itself once.
            new Construct("fe_nested",    "(string s)",  "(s)", "r=\"\"; rows=s.Split(\";\"); for (row in rows) { for (ch in row) { r += ch; } r += \"|\"; } return r;", "(\"ab;cd\")"),
            new Construct("fe_elem_str",  "(int n)",     "(n)", "c1={\"ab\"}; r=\"\"; for (ch in c1[0]) { r += ch + \".\"; } return r;", "(0)"),
            new Construct("fe_elem_num",  "(int n)",     "(n)", "c1={5}; r=\"\"; for (q in c1[0]) { r += string(q) + \".\"; } return r;", "(0)"),
            // Add and Remove on a local that holds a Variable, for the same reason: they are
            // a collection's methods, not a class's, so they must not go to CallMethod.
            new Construct("keys_add",     "(int n)",     "(n)", "m1={\"a\":1}; k=m1.Keys; k.Add(\"z\"); return k.Size + n;", "(0)"),
            new Construct("keys_remove",  "(int n)",     "(n)", "m1={\"a\":1,\"b\":2}; k=m1.Keys; k.Remove(\"a\"); return k.Size + n;", "(0)"),
            // Split on a local that holds a Variable -- a row of a CSV, say. It went to
            // CallMethod and threw "Not a class instance" at run time.
            new Construct("split_rows",   "(string s)",  "(s)", "rows=s.Split(\";\"); r=\"\"; for (row in rows) { cols=row.Split(\",\"); r += cols[0] + \"|\"; } return r;", "(\"a,b;c,d\")"),
            // A conversion in a compound value: the token loop consumes a call's arguments,
            // and the resolver emitted them a second time after the call it had built.
            new Construct("tally_str",    "(string s)",  "(s)", "m1={}; for (ch in s) { if (m1.Contains(ch)) { m1[ch] += 1; } else { m1[ch] = 1; } } k=m1.Keys; r=\"\"; for (q in k) { r += q + string(m1[q]); } return r;", "(\"aab\")"),
            new Construct("acc_convert",  "(int n)",     "(n)", "c1={1,2}; r=\"\"; for (q in c1) { r += string(q) + \",\"; } return r;", "(0)"),
            new Construct("acc_int",      "(int n)",     "(n)", "c1={\"3\",\"4\"}; t=0; for (q in c1) { t += int(q); } return t + n;", "(0)"),
            // A collection method on a local that holds a Variable. Reading it as a method of
            // a class sent it to CallMethod, which threw "Not a class instance" at run time.
            new Construct("keys_sort",    "(int n)",     "(n)", "m1={\"b\":1,\"a\":2}; k=m1.Keys; k.Sort(); return k[0]+k[1];", "(0)"),
            new Construct("keys_reverse", "(int n)",     "(n)", "m1={\"b\":1,\"a\":2}; k=m1.Keys; k.Reverse(); return k[0]+k[1];", "(0)"),
            // A local whose name is a C# keyword: "out" is an ordinary name in a script.
            new Construct("kw_local",     "(int n)",     "(n)", "out = 5; out += 2; return out + n;", "(1)"),
            new Construct("kw_coll",      "(int n)",     "(n)", "out = {1,2}; out.Add(3); return out.Size + out[0] + n;", "(0)"),
            new Construct("kw_foreach",   "(int n)",     "(n)", "src={1,2}; t=0; for (out in src) { t += out; } return t + n;", "(0)"),
            new Construct("kw_string",    "(int n)",     "(n)", "event = \"hi\"; return event + \"!\";", "(0)"),
            new Construct("kw_catch",     "(int n)",     "(n)", "try { throw \"x\"; } catch (params) { return params + \"!\"; } return \"no\";", "(0)"),
            new Construct("kw_filter",    "(int n)",     "(n)", "src={1,2,3,4}; out={}; for (q in src) { if (q % 2 == 0) { out.Add(q); } } return out.Size + out[0];", "(0)"),
            // "*" on a string joins rather than multiplies, on the trimmed text.
            new Construct("mul_str",      "(string s)",  "(s)", "return s * \"!\";", "(\"ab\")"),
            new Construct("mul_str_num",  "(string s)",  "(s)", "return s * 2;", "(\"ab\")"),
            new Construct("mul_str_trim", "(int n)",     "(n)", "return \"a \" * \" b\";", "(0)"),
            new Construct("mul_str_asg",  "(string s)",  "(s)", "r = s * \"!\"; return r;", "(\"ab\")"),
            new Construct("mul_num",      "(double x)",  "(x)", "return x * 3;", "(4)"),
            // Mixed-type operands, found by sweeping every operator over every pair of value
            // kinds. MergeCells takes the numeric path only when *both* sides are numbers, so
            // a number against a string concatenates and compares as text.
            new Construct("mix_mul",      "(int n)",     "(n)", "c1={5,\"3\"}; r = c1[0] * c1[1]; return string(r);", "(0)"),
            new Construct("mix_lt",       "(int n)",     "(n)", "c1={5,\"abc\"}; r = c1[0] < c1[1]; return string(r);", "(0)"),
            new Construct("mix_gt_lit",   "(int n)",     "(n)", "c1={3}; r = \"5\" > c1[0]; return string(r);", "(0)"),
            new Construct("mix_zero_str", "(int n)",     "(n)", "c1={0,\"\"}; r = c1[0] > c1[1]; return string(r);", "(0)"),
            // A comparison rendered as text is 1 or 0, not True or False.
            new Construct("bool_text",    "(int n)",     "(n)", "c1={3}; r = c1[0] > 1; return string(r);", "(0)"),
            // Every string is false, and "!x" is true only for a number that is zero -- so a
            // string is false both ways round.
            new Construct("truthy_str",   "(int n)",     "(n)", "c1={\"5\"}; if (c1[0]) { return 1; } return 0;", "(0)"),
            new Construct("truthy_notstr","(int n)",     "(n)", "c1={\"5\"}; if (!c1[0]) { return 1; } return 0;", "(0)"),
            new Construct("truthy_notnum","(int n)",     "(n)", "c1={0}; if (!c1[0]) { return 1; } return 0;", "(0)"),
            // A compound assignment is not "x = x + v": it dispatches on the left type and
            // takes the right side's numeric field, so a string on the right contributes 0.
            new Construct("cmp_asg_str",  "(int n)",     "(n)", "r = 5; c1={\"3\"}; r += c1[0]; return string(r);", "(0)"),
            new Construct("cmp_elem_str", "(int n)",     "(n)", "c1={5,\"3\"}; c1[0] += c1[1]; return string(c1[0]);", "(0)"),
            new Construct("cmp_elem_sub", "(int n)",     "(n)", "c1={\"5\",3}; c1[0] -= c1[1]; return string(c1[0]);", "(0)"),
            // A step is not "+= 1": it steps the numeric field, so "5"++ is 1 and not "51".
            new Construct("step_elem_str","(int n)",     "(n)", "c1={\"5\"}; c1[0]++; return string(c1[0]);", "(0)"),
            new Construct("plus_elem_str","(int n)",     "(n)", "c1={\"5\"}; c1[0] += 1; return string(c1[0]);", "(0)"),
            // ".Size" is the element count of a collection and 0 for anything else.
            new Construct("size_scalar",  "(int n)",     "(n)", "c1={5}; r = c1[0].Size; return string(r);", "(0)"),
            new Construct("size_coll",    "(int n)",     "(n)", "c1={1,2,3}; r = c1.Size; return string(r);", "(0)"),
            // A CSCS call in a "for" initialiser runs once, ahead of the loop.
            new Construct("for_fn",       "(int n)",     "(n)", "t=0; for (i = helper(n); i < 10; i++) { t++; } return t;", "(2)"),
            new Construct("for_fn_down",  "(int n)",     "(n)", "t=0; for (i = helper(n); i > 0; i--) { t++; } return t;", "(1)"),
            // A CSCS call inside a literal, and one seeding a local that is then stepped.
            new Construct("lit_fn",       "(int n)",     "(n)", "c1 = {helper(n), 2}; return c1[0] + c1[1];", "(2)"),
            new Construct("maplit_fn",    "(int n)",     "(n)", "m1 = {\"a\": helper(n)}; return m1[\"a\"];", "(2)"),
            new Construct("seed_fn",      "(int n)",     "(n)", "t = helper(n); while (t < 10) { t++; } return t;", "(2)"),
            // "++" steps the numeric field, so the element "7" becomes 1 rather than 8.
            new Construct("var_step",     "(int n)",     "(n)", "c1={5}; a = c1[0]; a++; return a + n;", "(0)"),
            new Construct("var_step_dn",  "(int n)",     "(n)", "c1={5}; a = c1[0]; a--; return a + n;", "(0)"),
            new Construct("var_step_str", "(int n)",     "(n)", "c1={\"7\"}; a = c1[0]; a++; return a + n;", "(0)"),
            // A bare read of a global element. The subscript is not part of the name, but it
            // was handed to the interpreter as one -- "return garr[0];" answered the whole
            // collection where the interpreter answers its first element.
            new Construct("gread_bare",   "(int n)",     "(n)", "return garr[0];", "(0)"),
            new Construct("gread_key",    "(int n)",     "(n)", "return gmap[\"k\"];", "(0)"),
            new Construct("gread_2d",     "(int n)",     "(n)", "return grid[1][0];", "(0)"),
            // A CSCS call as the value stored into a global element, or added to a collection.
            new Construct("gelem_fn",     "(int n)",     "(n)", "garr[0] = helper(n); return garr[0];", "(2)"),
            new Construct("gelem_fn_cmp", "(int n)",     "(n)", "garr[0] += helper(n); return garr[0];", "(2)"),
            new Construct("add_fn",       "(int n)",     "(n)", "c1={}; c1.Add(helper(n)); return c1[0];", "(2)"),
            // A CSCS call as the value stored into an element: it becomes several statements
            // plus a temporary, which has to be run ahead of the store.
            new Construct("elem_asg_fn",  "(int n)",     "(n)", "c1={1,2}; c1[0] = helper(n); return c1[0] + n;", "(2)"),
            new Construct("elem_cmp_fn",  "(int n)",     "(n)", "c1={1,2}; c1[0] += helper(n); return c1[0];", "(2)"),
            new Construct("map_asg_fn",   "(int n)",     "(n)", "m1={\"a\":1}; m1[\"a\"] = helper(n); return m1[\"a\"];", "(2)"),
            new Construct("elem_asg_2d",  "(int n)",     "(n)", "g1={{1,2},{3,4}}; g1[0][1] = helper(n); return g1[0][1];", "(2)"),
            // The value keeps whatever type the call returned, so a string stays a string.
            new Construct("elem_asg_str", "(int n)",     "(n)", "c1={1,2}; c1[0] = strHelper(n); return c1[0];", "(2)"),
            // A Variable negated, and one tested through a group.
            new Construct("not_var",      "(int n)",     "(n)", "c1={\"a\"}; r = c1[0] == \"z\"; if (!r) { return 1; } return 0;", "(0)"),
            new Construct("not_var_par",  "(int n)",     "(n)", "c1={\"a\"}; r = c1[0] == \"z\"; if (!(r)) { return 1; } return 0;", "(0)"),
            new Construct("not_elem",     "(int n)",     "(n)", "c1={0,5}; if (!c1[0]) { return 1; } return 0;", "(0)"),
            new Construct("var_in_while", "(int n)",     "(n)", "c1={1,1,0}; i=0; t=0; r=c1[0]; while (r) { t++; i++; r=c1[i]; } return t + n;", "(0)"),
            // "a = b = value" assigns right to left, so it stands for two statements.
            new Construct("chain_assign", "(int n)",     "(n)", "a1 = b1 = 3; return a1 + b1 + n;", "(0)"),
            new Construct("chain_three",  "(int n)",     "(n)", "a1 = b1 = c2 = 2; return a1 + b1 + c2 + n;", "(0)"),
            new Construct("chain_str",    "(int n)",     "(n)", "a1 = b1 = \"hi\"; return a1 + b1;", "(0)"),
            new Construct("chain_expr",   "(double x)",  "(x)", "a1 = b1 = x * 2; return a1 + b1;", "(3)"),
            // The same comparison in every position it can take.
            new Construct("eq_and",       "(int n)",     "(n)", "c1={\"a\",\"b\"}; if (c1[0] == \"a\" && c1[1] == \"b\") { return 1; } return 0;", "(0)"),
            new Construct("eq_and_num",   "(int n)",     "(n)", "c1={\"a\",\"b\"}; if (n < 5 && c1[0] == \"a\") { return 1; } return 0;", "(0)"),
            new Construct("eq_or",        "(int n)",     "(n)", "c1={\"a\",\"b\"}; if (c1[0] == \"z\" || c1[1] == \"b\") { return 1; } return 0;", "(0)"),
            new Construct("eq_ternary",   "(int n)",     "(n)", "c1={\"a\"}; return c1[0] == \"a\" ? \"yes\" : \"no\";", "(0)"),
            new Construct("eq_assigned",  "(int n)",     "(n)", "c1={\"a\"}; r = c1[0] == \"a\"; if (r) { return 1; } return 0;", "(0)"),
            // A value whose type is settled only at run time, compared for equality. C# reads
            // "==" on a Variable as reference equality, so two equal elements answered false.
            new Construct("eq_two_elems", "(int n)",     "(n)", "c1={\"a\",\"a\"}; if (c1[0] == c1[1]) { return 1; } return 0;", "(0)"),
            new Construct("ne_two_elems", "(int n)",     "(n)", "c1={\"a\",\"b\"}; if (c1[0] != c1[1]) { return 1; } return 0;", "(0)"),
            new Construct("eq_elem_lit",  "(int n)",     "(n)", "c1={\"a\",\"b\"}; if (c1[1] == \"b\") { return 1; } return 0;", "(0)"),
            new Construct("eq_lit_elem",  "(int n)",     "(n)", "c1={\"a\",\"b\"}; if (\"b\" == c1[1]) { return 1; } return 0;", "(0)"),
            new Construct("eq_loop_var",  "(int n)",     "(n)", "c1={\"a\",\"b\"}; t=0; for (q in c1) { if (q == \"b\") { t=1; } } return t + n;", "(0)"),
            new Construct("eq_elem_num",  "(int n)",     "(n)", "c1={5,\"5\"}; if (c1[1] == 5) { return 1; } return 0;", "(0)"),
            new Construct("eq_num_text",  "(int n)",     "(n)", "c1={5,\"5\"}; if (c1[0] == \"5\") { return 1; } return 0;", "(0)"),
            // Text compared to a number is compared as text, so "abc" is not 0.
            new Construct("eq_text_zero", "(int n)",     "(n)", "c1={\"abc\"}; if (c1[0] == 0) { return 1; } return 0;", "(0)"),
            // The same element, stepped rather than stored.
            new Construct("gelem_plus",   "(int n)",     "(n)", "garr[0] += 5; return garr[0] + n;", "(0)"),
            new Construct("gelem_times",  "(int n)",     "(n)", "garr[0] -= 1; garr[1] *= 3; return garr[0] + garr[1] + n;", "(0)"),
            new Construct("gelem_step",   "(int n)",     "(n)", "garr[0]++; garr[1]--; return garr[0] + garr[1] + n;", "(0)"),
            new Construct("gelem_mapadd", "(int n)",     "(n)", "gmap[\"k\"] += 4; return gmap[\"k\"] + n;", "(0)"),
            new Construct("gelem_2dadd",  "(int n)",     "(n)", "grid[0][1] += 5; return grid[0][1] + n;", "(0)"),
            new Construct("gelem_ixadd",  "(int n)",     "(n)", "garr[n] += 2; return garr[1] + n;", "(1)"),
            // Walking a string yields one character at a time, each a string of its own.
            new Construct("foreach_chars","(string s)",  "(s)", "t=0; for (ch in s) { t++; } return t;", "(\"abc\")"),
            new Construct("foreach_cat",  "(string s)",  "(s)", "r=\"\"; for (ch in s) { r += ch + \"|\"; } return r;", "(\"abc\")"),
            new Construct("foreach_upper","(string s)",  "(s)", "r=\"\"; for (ch in s) { r += ch.Upper; } return r;", "(\"abc\")"),
            // Bit operations on elements: C# has none on double, and the interpreter
            // truncates toward zero, so the operand takes a cast the others do not.
            new Construct("bit_elem",     "(int n)",     "(n)", "c1={6,3}; return c1[0] & c1[1];", "(0)"),
            new Construct("bit_elem_or",  "(int n)",     "(n)", "c1={6,3}; return c1[0] | c1[1];", "(0)"),
            new Construct("bit_elem_xor", "(int n)",     "(n)", "c1={6,3}; return (c1[0] ^ c1[1]) + n;", "(0)"),
            new Construct("bit_elem_trun","(int n)",     "(n)", "c1={6.9,3}; return c1[0] & c1[1];", "(0)"),
            // A comparison whose receiver is a member, and a chain of members after one.
            new Construct("cmp_member",   "(string s)",  "(s)", "if (s.Upper > \"AA\") { return 1; } return 0;", "(\"bb\")"),
            new Construct("cmp_member2",  "(string s)",  "(s)", "if (s.Lower < \"mm\") { return 1; } return 0;", "(\"AB\")"),
            new Construct("cmp_sub",      "(string s)",  "(s)", "if (s.Substring(0,2) > \"aa\") { return 1; } return 0;", "(\"bcd\")"),
            new Construct("cmp_indexof",  "(string s)",  "(s)", "if (s.IndexOf(\"b\") > 0) { return 1; } return 0;", "(\"abc\")"),
            new Construct("chain_member", "(string s)",  "(s)", "return s.Upper.Replace(\"A\", \"b\");", "(\"ax\")"),
            // "break" ends the switch and nothing else, so the loop around it runs every
            // pass; "return" and "continue" still belong to the function and to the loop.
            new Construct("sw_break_loop","(int n)",     "(n)", "t=0; for (i=0;i<3;i++) { switch (i) { case 1: t+=10; break; default: t+=1; } } return t;", "(0)"),
            new Construct("sw_break_whl", "(int n)",     "(n)", "t=0; i=0; while (i<3) { switch (i) { case 1: t+=10; break; default: t+=1; } i++; } return t;", "(0)"),
            new Construct("sw_ret_loop",  "(int n)",     "(n)", "t=0; for (i=0;i<4;i++) { switch (i) { case 2: return t; } t+=1; } return -1;", "(0)"),
            // A clause that falls off its end leaves nothing behind for the next statement.
            new Construct("sw_no_break",  "(int n)",     "(n)", "t=0; for (i=0;i<3;i++) { switch (i) { case 0: t+=1; } t+=100; } return t;", "(0)"),
            new Construct("sw_deflt_only","(int n)",     "(n)", "t=0; for (i=0;i<3;i++) { switch (i) { default: t+=1; } } return t;", "(0)"),
            // "continue" belongs to the enclosing loop, and the do/while(false) wrapper the
            // translation needs would capture it, so this one stays interpreted.
            new Construct("sw_continue",  "(int n)",     "(n)", "t=0; for (i=0;i<4;i++) { switch (i) { case 1: continue; default: t+=1; } t+=100; } return t;", "(0)"),
            new Construct("switch_after", "(int n)",     "(n)", "r=\"\"; switch(n) { case 1: r+=\"a;\"; break; default: r+=\"d;\"; } r+=\"after\"; return r;", "(1)"),
            new Construct("switch_deflt", "(int n)",     "(n)", "r=\"\"; switch(n) { case 1: r+=\"a;\"; break; default: r+=\"d;\"; } r+=\"after\"; return r;", "(9)"),

            new Construct("do_once",      "(int n)",     "(n)", "i=0; do { i++; } while (i < n); return i;", "(0)"),
            new Construct("do_break",     "(int n)",     "(n)", "i=0; do { i++; if (i == 2) { break; } } while (i < n); return i;", "(9)"),
            new Construct("do_nested",    "(int n)",     "(n)", "t=0; a=0; do { b=0; do { b++; t++; } while (b < 2); a++; } while (a < n); return t;", "(3)"),

            new Construct("while_cont",   "(double n)",  "(n)", "i=0; t=0; while (helper(i) < n) { i++; if (i == 2) { continue; } t += i; } return t;", "(10)"),
            new Construct("while_break",  "(double n)",  "(n)", "i=0; while (helper(i) < n) { i++; if (i == 2) { break; } } return i;", "(90)"),

            new Construct("foreach_str",  "(int n)",     "(n)", "a={\"x\",\"y\"}; r=\"\"; for (w in a) { r = r + w; } return r;", "(0)"),
            new Construct("foreach_map",  "(int n)",     "(n)", "m={\"a\":1,\"b\":2}; t=0; for (k in m.Keys) { t += m[k]; } return t + n;", "(0)"),

            new Construct("nested_assign","(int n)",     "(n)", "a={{1,2},{3,4}}; t=0; for(i=0;i<a.Size;i++){ t += a[i].Size; } return t + a[1][0] + n;", "(0)"),
            // Left interpreted: a class instance is a Variable, which has no member of the
            // field's name, and a method on one needs the interpreter.
            new Construct("class_field", "(int n)",     "(n)", "p = new Point(3, 4); return p.x + p.y + n;", "(0)"),
            new Construct("class_two",   "(int n)",     "(n)", "p = new Point(3, 4); q = new Point(10, 20); return p.x + q.y + n;", "(0)"),
            // A field concatenated into a string, which takes a different member path.
            new Construct("class_str",   "(int n)",     "(n)", "p = new Point(3, 4); return \"x=\" + p.x;", "(0)"),
            // A method: running one needs an argument list, which is what tells the
            // instance a method is wanted rather than a property.
            new Construct("class_method","(int n)",     "(n)", "p = new Point(3, 4); return p.Sum() + n;", "(0)"),
            new Construct("class_marg",  "(int n)",     "(n)", "p = new Point(3, 4); return p.Scale(2) + n;", "(0)"),
            new Construct("class_mstr",  "(int n)",     "(n)", "p = new Point(3, 4); return p.Tag(\"x=\");", "(0)"),
            // Writing a field compiles. Building an instance in an *expression* does not:
            // such an instance has no name, and this interpreter resolves a method's fields
            // through the named instance, so one built that way can read its fields but
            // cannot run its own methods. Everything below that constructs one in an
            // expression therefore stays interpreted.

            // A switch on a field. The value holds a Variable, which cannot be compared to
            // the labels with "==", so the comparison is made explicitly -- text as text and
            // numbers as numbers, as the interpreter does.
            new Construct("switch_fld",  "(int n)",     "(n)", "p = new Named(2, \"b\"); r=\"\"; switch (p.tag) { case \"a\": r=\"A\"; break; case \"b\": r=\"B\"; break; default: r=\"?\"; } return r;", "(0)"),
            new Construct("switch_fnum", "(int n)",     "(n)", "p = new Named(2, \"b\"); r=0; switch (p.v) { case 1: r=10; break; case 2: r=20; break; default: r=99; } return r + n;", "(0)"),
            new Construct("switch_fdef", "(int n)",     "(n)", "p = new Named(2, \"z\"); r=\"\"; switch (p.tag) { case \"a\": r=\"A\"; break; default: r=\"?\"; } return r;", "(0)"),

            new Construct("class_write", "(int n)",     "(n)", "p = new Point(3, 4); p.x = 9; return p.x + n;", "(0)"),
            // A "new" anywhere but as the whole right-hand side of an assignment to a plain
            // name is moved into a statement of its own first.
            new Construct("class_two_add","(int n)",     "(n)", "a = {}; a.Add(new Point(1,2)); a.Add(new Point(3,4)); return a[0].x + a[1].y + n;", "(0)"),
            // One "new" inside another one's arguments stays interpreted: moving the inner one
            // out works on its own, but once other compiled functions have built instances of
            // their own the method call on the temporary resolves a field of the wrong class.
            new Construct("class_in_new", "(int n)",     "(n)", "p = new Named(new Point(1,2).Sum(), \"z\"); return p.v + n;", "(0)"),
            // Not out of a ternary branch either: only one of the two runs.
            new Construct("class_tern",   "(int n)",     "(n)", "p = n > 0 ? new Point(1,2) : new Point(3,4); return p.x + n;", "(1)"),
            new Construct("class_inarr", "(int n)",     "(n)", "a = {}; a.Add(new Point(5, 6)); return a[0].x + n;", "(0)"),
            new Construct("class_newarg","(int n)",     "(n)", "return helper(new Point(5, 6).x) + n;", "(0)"),
            // A member on a field, and instances iterated out of a collection: the "Add"
            // fast path has to take them, since a callback would append to the interpreter's
            // own copy of the collection while the compiled code reads the local one.
            new Construct("class_fldmem","(int n)",     "(n)", "p = new Named(3, \"abcd\"); return p.tag.Length + n;", "(0)"),
            new Construct("class_fldstr","(int n)",     "(n)", "p = new Named(3, \"abc\"); return p.tag.Upper;", "(0)"),
            // A field holding another instance, and a local fed from a method: both are
            // Variables, and the pre-pass has to reach the second through the first.
            new Construct("class_nested","(int n)",     "(n)", "p = new Named(3, \"a\"); p.kid = new Named(6, \"b\"); return p.kid.v + p.v + n;", "(0)"),
            new Construct("class_fromm", "(int n)",     "(n)", "p = new Named(3, \"a\"); p.kid = new Named(6, \"b\"); q = p.Kid(); return q.v + n;", "(0)"),
            // A member on the result of a method call: the result is a Variable, so the
            // chain continues from it. Both the expression path and the token loop.
            new Construct("class_methfld","(int n)",    "(n)", "p = new Named(3, \"a\"); p.kid = new Named(6, \"b\"); return p.Kid().v + n;", "(0)"),
            new Construct("class_mfstr", "(int n)",     "(n)", "p = new Named(3, \"a\"); p.kid = new Named(6, \"b\"); return \"k=\" + p.Kid().v;", "(0)"),

            new Construct("class_iter",  "(int n)",     "(n)", "a = {}; a.Add(new Named(1, \"x\")); a.Add(new Named(2, \"y\")); t=0; for (q in a) { t += q.v; } return t + n;", "(0)"),

            new Construct("mix_grid",      "(int n)",    "(n)", "g={}; g.Add({{1,2},{3,4}}); t=0; for(r=0;r<g[0].Size;r++){ for(c=0;c<g[0][r].Size;c++){ t+=g[0][r][c]; } } m={}; m[\"k\"]={\"t\":t}; s = n>0 ? {10} : {1}; return m[\"k\"][\"t\"]*s[0]+n;", "(1)"),
            // Left interpreted: a prefix step on an element. The postfix form compiles.
            new Construct("elem_predec",   "(int n)",    "(n)", "a={1,5}; --a[1]; return a[1] + n;", "(0)"),
            new Construct("multi_return",  "(double n)", "(n)", "if(n>5){return 1;} if(n>2){return 2;} return 3;", "(10)"),
            new Construct("bool_var",      "(double n)", "(n)", "b = n > 5; if (b) { return 1; } return 0;", "(10)"),
            new Construct("string_num",    "(double n)", "(n)", "s = \"val=\" + n; return s;", "(10)"),
            new Construct("increment",     "(int n)",    "(n)", "x=n; x++; ++x; x--; return x;", "(5)"),
            // CSCS switch needs an explicit break and does not accept return inside a case.
            new Construct("switch",        "(int n)",    "(n)", "r=0; switch(n) { case 1: r=10; break; case 2: r=20; break; default: r=30; } return r;", "(2)"),
            // CSCS falls through from one non-empty case to the next, so the translation
            // uses a matched-flag inside do{}while(false) rather than a C# switch.
            new Construct("switch_fall",   "(int n)",    "(n)", "r=\"\"; switch(n) { case 1: r+=\"a;\"; case 2: r+=\"b;\"; default: r+=\"d;\"; } return r;", "(1)"),
            // break inside a switch exits the enclosing loop in CSCS, so this one is
            // deliberately left to the interpreter rather than translated differently.
            new Construct("switch_in_loop","(int n)",    "(n)", "r=\"\"; for(i=0;i<n;i++){ switch(i) { case 0: r+=\"z;\"; break; default: r+=\"m;\"; } } return r;", "(4)"),
            new Construct("neg_index",     "(int n)",    "(n)", "a={5,6,7}; return a[a.Size-1] + n;", "(1)"),
            // Index expressions containing operators. The statement tokenizer used to split
            // on the operator inside the brackets, tearing "a[a.Size-1]" into three tokens.
            new Construct("idx_expr",      "(int n)",    "(n)", "a={5,6,7}; return a[n+1] * 2;", "(1)"),
            new Construct("idx_assign_expr","(int n)",   "(n)", "a={1,2,3}; a[n-1] = 50; return a[0] + n;", "(1)"),
            // Exercises the fallback path with statements before the untranslatable part:
            // a cfunction body is captured raw, so the fallback has to convert it before
            // handing it to the interpreter or the leftover whitespace breaks parsing.
            new Construct("fallback_mix", "(double n)", "(n)", "p = n > 5; q = n == 10; try { throw \"x\"; } catch(e) { } if (p && !q) { return 1; } return 0;", "(50)"),
            new Construct("nested_call",   "(double n)", "(n)", "return Math.Max(Math.Min(n, 20), 5);", "(10)"),
            // A brace literal passed as an argument: the statement tokenizer has to keep
            // the literal whole instead of ending the statement at the first "{".
            new Construct("lit_arg",       "(int n)",    "(n)", "a={}; a.Add({1,2}); a.Add(9); return a[1] + n;", "(1)"),
            // Literals nested inside literals, then read back through a chained subscript.
            new Construct("lit_nested",    "(int n)",    "(n)", "a={}; a.Add({{1,2},{3,4}}); return a[0][1][0] + n;", "(1)"),
            new Construct("chain_idx",     "(int n)",    "(n)", "a={}; a.Add({7,8}); return a[0][n];", "(1)"),
            // Bitwise | and ^ were missing from the arithmetic operators, so an expression
            // containing one looked "unknown" and its argument names stopped resolving.
            new Construct("bit_or_xor",    "(int n)",    "(n)", "return (n | 4) ^ 1;", "(3)"),
            // An operand starting with "(" has no name before the paren, so the token used
            // to be copied through with its argument name unresolved.
            new Construct("paren_arg",     "(int a, string b, double c)", "(a, b, c)", "return b + (a + c);", "(1, \"x\", 2.5)"),
            // A string-typed condition is not a "known expression", so it takes the path
            // where "s.Length" arrived as a single unresolvable token.
            new Construct("cond_member",   "(string s)", "(s)", "if (s.Length > 2) { return 1; } return 0;", "(\"abcd\")"),
            new Construct("cond_member2",  "(string s)", "(s)", "if (s.StartsWith(\"ab\") && s.EndsWith(\"d\")) { return 1; } return 0;", "(\"abcd\")"),
            // CSCS clamps Substring instead of throwing, and honours an explicit "no_case"
            // where the C# member of the same name has no such argument. Compiling these
            // straight through would answer differently, so they go through
            // CscsStringMembers -- these constructs are what proves it, since a same-case
            // argument would agree either way.
            new Construct("str_case",      "(string s)", "(s)", "if (s.Contains(\"BC\")) { return 1; } return 0;", "(\"abcd\")"),
            new Construct("str_nocase",    "(string s)", "(s)", "if (s.Contains(\"BC\", \"no_case\")) { return 1; } return 0;", "(\"abcd\")"),
            new Construct("str_startcase", "(string s)", "(s)", "if (s.StartsWith(\"AB\", \"no_case\") && s.EndsWith(\"cd\")) { return 1; } return 0;", "(\"abcd\")"),
            // Equals compiles to EqualsCscs rather than the C# overload, which compares
            // ordinally and has nowhere to put the "no_case" argument.
            new Construct("str_equals",    "(string s)", "(s)", "if (s.Equals(\"ABCD\")) { return 1; } return 0;", "(\"abcd\")"),
            new Construct("str_eq_nocase", "(string s)", "(s)", "t = s; if (t.Equals(\"ABCD\", \"no_case\") && t.Length == 4) { return 1; } return 0;", "(\"abcd\")"),
            new Construct("str_sub_clamp", "(string s)", "(s)", "return s.Substring(2, 99);", "(\"abcd\")"),
            new Construct("coll_contains", "(int n)",    "(n)", "a={1,2,3}; if (a.Contains(2)) { return 1; } return n;", "(0)"),
            new Construct("map_keys",      "(int n)",    "(n)", "m={\"a\":1,\"b\":2}; return m.Keys.Size + n;", "(0)"),
            // Assignment through a chain of subscripts. The builder stopped at the first
            // "]", so the statement fell through and came out as a C# declaration.
            new Construct("idx2_assign",   "(int n)",    "(n)", "a={}; a.Add({1,2}); a[0][1] = 9; return a[0][1] + n;", "(0)"),
            new Construct("idx2_compound", "(int n)",    "(n)", "a={}; a.Add({1,2}); a[0][n] += 5; return a[0][1];", "(1)"),
            // "~" is a prefix rather than one of the interpreter's actions, so it used to be
            // glued onto its operand and looked up as a variable called "~n".
            new Construct("bit_not",       "(int n)",    "(n)", "return (~n) & 255;", "(3)"),
            // A do-loop is deliberately left to the interpreter: translating it emitted a
            // callback that handed ProcessDoWhile a fragment with no body to run.
            new Construct("do_while",      "(int n)",    "(n)", "i=0; do { i++; } while (i < n); return i;", "(4)"),

            // Small complete algorithms. Each combines features that are covered one at a time
            // above, and several of them only compiled once the pieces met: an element compared
            // with an argument, a for-initialiser over a member, a string read with At.
            new Construct("alg_linsearch", "(int n)", "(n)", "a={4,9,2,7}; for(i=0;i<a.Size;i++){ if(a[i]==n){ return i; } } return -1;", "(2)"),
            new Construct("alg_binsearch", "(int n)", "(n)", "a={1,3,5,7,9,11}; lo=0; hi=a.Size-1; while(lo<=hi){ mid=int((lo+hi)/2); if(a[mid]==n){ return mid; } if(a[mid]<n){ lo=mid+1; } else { hi=mid-1; } } return -1;", "(9)"),
            new Construct("alg_bubble", "(int n)", "(n)", "a={5,1,4,2,3}; for(i=0;i<a.Size;i++){ for(j=0;j<a.Size-1-i;j++){ if(a[j]>a[j+1]){ t=a[j]; a[j]=a[j+1]; a[j+1]=t; } } } return a[0]*10000+a[1]*1000+a[2]*100+a[3]*10+a[4]+n;", "(0)"),
            new Construct("alg_selsort", "(int n)", "(n)", "a={5,2,8,1}; for(i=0;i<a.Size-1;i++){ m=i; for(j=i+1;j<a.Size;j++){ if(a[j]<a[m]){ m=j; } } t=a[i]; a[i]=a[m]; a[m]=t; } return a[0]*1000+a[1]*100+a[2]*10+a[3]+n;", "(0)"),
            new Construct("alg_gcd", "(int a, int b)", "(a, b)", "while (b != 0) { t = b; b = a % b; a = t; } return a;", "(48, 18)"),
            new Construct("alg_primes", "(int n)", "(n)", "c=0; for(k=2;k<=n;k++){ p=1; for(d=2;d*d<=k;d++){ if(k%d==0){ p=0; break; } } c+=p; } return c;", "(50)"),
            new Construct("alg_fizzbuzz", "(int n)", "(n)", "r=\"\"; for(i=1;i<=n;i++){ if(i%15==0){ r+=\"FB\"; } elif(i%3==0){ r+=\"F\"; } elif(i%5==0){ r+=\"B\"; } else { r+=i; } } return r;", "(15)"),
            new Construct("alg_transpose", "(int n)", "(n)", "g={{1,2},{3,4},{5,6}}; t={}; for(j=0;j<2;j++){ row={}; for(i=0;i<3;i++){ row.Add(g[i][j]); } t.Add(row); } return t[1][2]+n;", "(0)"),
            new Construct("alg_dedup", "(int n)", "(n)", "a={1,2,2,3,1}; r={}; for(x in a){ if(!r.Contains(x)){ r.Add(x); } } return r.Size + n;", "(0)"),

            // Two recursive calls in one invocation. Every call left its level of locals on the
            // interpreter's stack -- the level pushed was a copy with an id of its own, popped by
            // the original's id -- and a compiled caller publishes its arguments into whatever
            // level is on top, so the second call read the first one's leftovers: fib(10) was -80.
            new Construct("rec_fib", "(int n)", "(n)", "if (n < 2) { return n; } return SELF(n-1) + SELF(n-2);", "(10)"),
            new Construct("rec_fib_split", "(int n)", "(n)", "if (n < 2) { return n; } a = SELF(n-1); b = SELF(n-2); return a + b;", "(10)"),
            new Construct("rec_twice", "(int n)", "(n)", "if (n < 2) { return 1; } return SELF(n-1) + SELF(n-1);", "(4)"),

            // At is how CSCS reads a character (s[i] on a string is an error there). It maps
            // onto AtCscs, which returns a one-letter string and an empty one past the end.
            new Construct("at_palin", "(string s)", "(s)", "i=0; j=s.Length-1; while(i<j){ if(s.At(i)!=s.At(j)){ return 0; } i++; j--; } return 1;", "(\"racecar\")"),
            new Construct("at_reverse", "(string s)", "(s)", "r=\"\"; for(i=s.Length-1;i>=0;i--){ r+=s.At(i); } return r;", "(\"hello\")"),
            new Construct("at_vowels", "(string s)", "(s)", "v=\"aeiou\"; c=0; for(i=0;i<s.Length;i++){ if(v.Contains(s.At(i))){ c++; } } return c;", "(\"education\")"),
            new Construct("at_caesar", "(string s)", "(s)", "a=\"abcdefghijklmnopqrstuvwxyz\"; r=\"\"; for(i=0;i<s.Length;i++){ k=a.IndexOf(s.At(i)); r+=a.At((k+3)%26); } return r;", "(\"xyz\")"),
            new Construct("at_past_end", "(string s)", "(s)", "return s.At(99) + \"|\" + s.At(0);", "(\"ab\")"),
            new Construct("at_frac_idx", "(string s)", "(s)", "k = 1.7; return s.At(k);", "(\"abc\")"),
            new Construct("at_tally", "(string s)", "(s)", "m={}; for(i=0;i<s.Length;i++){ c=s.At(i); if(m.Contains(c)){ m[c]+=1; } else { m[c]=1; } } return m[\"l\"];", "(\"hello world\")"),

            // A member after a call that returns a string: the expression path resolves the call
            // and its arguments as separate tokens and used to copy ".Upper" after them verbatim.
            new Construct("chain_at_up", "(string s)", "(s)", "return s.At(0).Upper + s.Substring(1);", "(\"hello\")"),
            new Construct("chain_sub_up", "(string s)", "(s)", "return s.Substring(0, 2).Upper;", "(\"hello\")"),
            new Construct("chain_sub_nc", "(string s)", "(s)", "if (s.Substring(0, 3).Contains(\"BC\", \"no_case\")) { return 1; } return 0;", "(\"abcdef\")"),
            new Construct("chain_trim_up", "(string s)", "(s)", "t = s.Trim().Upper.Substring(0, 2); return t;", "(\"  hello \")"),

            // The loop variable used to be declared with its initialiser repeated, cut short at the
            // first member: "i = s.Length - 1" declared "double i = s".
            new Construct("for_init_mem", "(string s)", "(s)", "t=0; for(i=s.Length-1;i>=0;i--){ t+=i; } return t;", "(\"abcd\")"),

            // The interpreter's literal parser turns only \\, \", \' and \n into characters: "\t"
            // stays a backslash and a letter (Print shows it as a tab). C# made it one character.
            new Construct("esc_tab", "(int n)", "(n)", "s = \"a\\tb\"; return s.Length + n;", "(0)"),
            new Construct("esc_cr", "(int n)", "(n)", "s = \"a\\rb\"; return s.Length + n;", "(0)"),
            new Construct("esc_tab_cmp", "(string s)", "(s)", "if (s == \"a\\tb\") { return 1; } return 0;", "(\"a\\tb\")"),

            // A minus in front of a group: the interpreter looked "-" up as a function and failed.
            new Construct("neg_group", "(int n)", "(n)", "x = -n; y = -(n + 1); return x + y;", "(3)"),
            new Construct("neg_group_dbl", "(double n)", "(n)", "return -(n * 2) + 1;", "(2.5)"),
            new Construct("neg_group_call", "(int n)", "(n)", "return -(helper(n));", "(2)"),
            new Construct("neg_group_elem", "(int n)", "(n)", "a = {4, 5}; return -(a[1]) * n;", "(2)"),

            // "return (n);" is the single token "return(n)" and was counted as a bare return:
            // an empty result, with no compile error, whenever the group had no operator in it.
            new Construct("ret_paren", "(int n)", "(n)", "return (n);", "(2)"),
            new Construct("ret_paren_str", "(string s)", "(s)", "return (s);", "(\"ab\")"),
            new Construct("ret_paren_elem", "(int n)", "(n)", "a = {5, 6}; return (a[1]);", "(0)"),
            new Construct("ret_paren_math", "(int n)", "(n)", "return (Math.Abs(n));", "(-2)"),
            new Construct("ret_paren_call", "(int n)", "(n)", "return (helper(n));", "(2)"),

            // A script call inside a group has no name before its parenthesis, so it looked like
            // punctuation and went down the expression path as a C# method that does not exist.
            // grp_call_str also needed the interpreter fixed: a group holding a lone string left
            // its ")" unread, and "(f(s)) + s" returned only f(s).
            new Construct("grp_call_add", "(int n)", "(n)", "return 1 + (helper(n));", "(2)"),
            new Construct("grp_call_str", "(string s)", "(s)", "return (strHelper(s)) + s;", "(\"a\")"),
            new Construct("grp_call_cond", "(int n)", "(n)", "if ((helper(n)) > 5) { return 1; } return 0;", "(2)"),

            // A grouped call in an assignment, and two in one expression: the assignment used to
            // end the statement after the call, and a second call went out as trailing text.
            new Construct("grp_call_asg", "(int n)", "(n)", "x = (helper(n)) * 2; return x;", "(2)"),
            new Construct("grp_call_two", "(int n)", "(n)", "return (helper(n)) + (helper(1));", "(2)"),

            // An element compared with a name of fixed type: "a[i] == n" was Variable == int.
            new Construct("eq_elem_arg", "(int n)", "(n)", "a={4,9}; c=0; for(i=0;i<a.Size;i++){ if(a[i]!=n){ c++; } } return c;", "(9)"),
            new Construct("eq_elem_sarg", "(string s)", "(s)", "a={5,6}; if (a[0] == s) { return 1; } return 0;", "(\"5\")"),
            new Construct("eq_elem_ctr", "(int n)", "(n)", "a={0,5,2}; c=0; for(i=0;i<a.Size;i++){ if(a[i]==i){ c++; } } return c + n;", "(0)"),
            new Construct("eq_fe_arg", "(int n)", "(n)", "a={4,9,4}; c=0; for (x in a) { if (x == n) { c++; } } return c;", "(4)"),

            // Left interpreted: n is declared int, and the interpreter makes it 13.5. Compiling it
            // as int division would answer 13.
            new Construct("int_arg_halve", "(int n)", "(n)", "n = n / 2; return n;", "(27)"),

            // A script call inside "&&", "||" or "?:" runs only where the operator reaches it. It
            // used to be hoisted ahead of the statement and ran regardless: these read a[0] of an
            // empty array, and the compiled code threw where the interpreter returned. They are
            // inline calls now (CscsCalls.Call), and so are calls in a loop's condition, which a
            // hoist ran once -- "while (i < 10 && f(i) < n)" tested a stale value.
            new Construct("sc_guard_and", "(int n)", "(n)", "a = {}; if (a.Size > 0 && helper(a[0]) > 1) { return 1; } return 0;", "(0)"),
            new Construct("sc_guard_or", "(int n)", "(n)", "a = {}; if (a.Size == 0 || helper(a[0]) > 1) { return 1; } return 0;", "(0)"),
            new Construct("sc_guard_tern", "(int n)", "(n)", "a = {}; return a.Size > 0 ? helper(a[0]) : -1;", "(0)"),
            new Construct("sc_guard_while", "(int n)", "(n)", "a = {3, 4}; i = 0; t = 0; while (i < a.Size && helper(a[i]) > 0) { t += a[i]; i++; } return t;", "(0)"),
            new Construct("call_while_and", "(int n)", "(n)", "i = 0; while (i < 10 && helper(i) < n) { i++; } return i;", "(10)"),
            new Construct("call_for_cond", "(int n)", "(n)", "t = 0; for (i = 0; i < helper(n); i++) { t += 1; } return t;", "(2)"),

            // A call's value keeps its runtime type. Read as a number at translation time, a string
            // result was 0: "f(n) * 2" gave 0 where the interpreter joins "s2".
            new Construct("callres_str_mul", "(int n)", "(n)", "return strHelper(n) * 2;", "(1)"),
            new Construct("callres_str_tern", "(int n)", "(n)", "return n > 0 ? strHelper(n) : \"z\";", "(1)"),
            new Construct("callres_str_eq", "(int n)", "(n)", "if (strHelper(n) == \"s\") { return 1; } return 0;", "(1)"),
            new Construct("callres_str_lt", "(int n)", "(n)", "if (strHelper(n) < \"t\") { return 1; } return 0;", "(1)"),
            new Construct("callres_num_eq", "(int n)", "(n)", "if (helper(n) == 3) { return 1; } return 0;", "(1)"),
            new Construct("callres_accum", "(int n)", "(n)", "r = \"\"; for (i = 0; i < n; i++) { r += strHelper(i); } return r;", "(3)"),

            // A local assigned from a call holds a Variable, and so does one computed from it.
            new Construct("callres_asg_add", "(int n)", "(n)", "x = helper(n) + 1; return x * 2;", "(2)"),
            new Construct("callres_asg_use", "(int n)", "(n)", "x = helper(n); y = x + 1; return y;", "(2)"),
            new Construct("callres_asg_loop", "(int n)", "(n)", "t = 0; for (i = 0; i < n; i++) { x = helper(i) * 2; t += x; } return t;", "(3)"),

            // A conversion takes exactly its own parenthesised argument, which may be a call.
            new Construct("conv_of_call", "(int n)", "(n)", "return int(helper(n)) + 1;", "(2)"),
            new Construct("conv_sqrt", "(int n)", "(n)", "return int(Math.Sqrt(n));", "(50)"),
            new Construct("conv_in_cond", "(string s)", "(s)", "if (int(s) > 10) { return \"big\"; } return \"small\";", "(\"42\")"),
            new Construct("conv_in_while", "(string s)", "(s)", "i = 0; while (i < int(s)) { i++; } return i;", "(\"4\")"),
            new Construct("conv_trailing", "(string s)", "(s)", "x = int(s) * 2 + 1; return x;", "(\"4\")"),

            // The interpreter kept every space after "new", so "new Point(i, i * 2)" split its
            // arguments at the spaces, matched no constructor, and silently kept the default fields.
            new Construct("cls_new_expr", "(int n)", "(n)", "a = {}; for (i = 0; i < n; i++) { a.Add(new Point(i, i * 2)); } t = 0; for (q in a) { t += q.y; } return t;", "(4)"),

            // A brace literal as a map value failed in the interpreter ("Couldn't find operand []")
            // and was emitted verbatim by the translator.
            new Construct("map_nested_lit", "(int n)", "(n)", "g = {\"x\": {1, 2}, \"y\": {3}}; t = 0; for (k in g.Keys) { t += g[k].Size; } return t + n;", "(0)"),
            new Construct("map_nested_map", "(int n)", "(n)", "g = {\"x\": {\"a\": {7, 8}}}; return g[\"x\"][\"a\"][1] + n;", "(0)"),

            // A method on an element of a function's own local: the interpreter looked the array up
            // with a fresh script that could not see the function's locals.
            new Construct("elem_method_loc", "(int n)", "(n)", "a = {\" x \"}; if (a[0].Trim() == \"x\") { return 1; } return 0;", "(0)"),
            new Construct("elem_sub_at", "(int n)", "(n)", "a = {\"hello\"}; return a[0].Substring(1).At(0);", "(0)"),

            // Members on a loop element, after one of Variable's string members, and Trim.
            new Construct("fe_title", "(string s)", "(s)", "words = s.Split(\" \"); r = \"\"; for (w in words) { if (r != \"\") { r += \" \"; } r += w.Substring(0, 1).Upper + w.Substring(1); } return r;", "(\"hello big world\")"),
            new Construct("fe_trim", "(string s)", "(s)", "parts = s.Split(\",\"); r = \"\"; for (p in parts) { r += p.Trim(); } return r;", "(\" a , b ,c \")"),
            new Construct("loc_member_cond", "(string s)", "(s)", "c = 0; for (i = 0; i < s.Length; i++) { ch = s.At(i); if (ch == ch.Upper && ch != ch.Lower) { c++; } } return c;", "(\"HeLLo World\")"),
            new Construct("arg_member_cond", "(string s)", "(s)", "if (s != s.Lower) { return 1; } return 0;", "(\"aB\")"),

            // The string-join rewrite for "*" took "\"\" + n" as the left operand of "* 100".
            new Construct("mul_precedence", "(int n)", "(n)", "s = \"\" + n * 100; return s.Length;", "(123)"),
            new Construct("wf_most", "(string s)", "(s)", "words = s.Split(\" \"); m = {}; for (w in words) { if (m.Contains(w)) { m[w] += 1; } else { m[w] = 1; } } best = \"\"; bc = 0; for (k in m.Keys) { if (m[k] > bc) { bc = m[k]; best = k; } } return best;", "(\"b a b c b a\")"),
            new Construct("arr_merge2", "(int n)", "(n)", "a = {1, 4, 6}; b = {2, 3, 7}; r = {}; i = 0; j = 0; while (i < a.Size && j < b.Size) { if (a[i] <= b[j]) { r.Add(a[i]); i++; } else { r.Add(b[j]); j++; } } while (i < a.Size) { r.Add(a[i]); i++; } while (j < b.Size) { r.Add(b[j]); j++; } return r[2] * 10 + r[5] + n;", "(0)"),
            new Construct("matrix_mul2", "(int n)", "(n)", "a = {{1, 2}, {3, 4}}; b = {{5, 6}, {7, 8}}; c = {}; for (i = 0; i < 2; i++) { row = {}; for (j = 0; j < 2; j++) { s = 0; for (k = 0; k < 2; k++) { s += a[i][k] * b[k][j]; } row.Add(s); } c.Add(row); } return c[1][1] + n;", "(0)"),

            // Left interpreted: a call's value is a Variable, and Math.Max needs a number.
            new Construct("call_math_arg", "(int n)", "(n)", "return Math.Max(helper(n), 5);", "(2)"),

            // A Math argument that is not a C# number: a script call resolves to
            // CscsCalls.Call and a global read to GetVariableValue, both of which yield a
            // Variable, so C# found no Math overload and named the first one it had --
            // "byte", "short", "decimal". NumberMathArgs wraps those in
            // CscsConvert.ToNumber over the finished code, the way CastRoundDigits works;
            // ReplaceMathArgs, the one place that knows it is in a Math call, is never
            // reached for a "return Math..." statement.
            new Construct("math_arg_glob", "(int n)", "(n)", "return Math.Max(gcount, 5);", "(0)"),
            new Construct("math_arg_two_calls", "(int n)", "(n)", "return Math.Max(helper(n), helper(1));", "(2)"),
            new Construct("math_arg_min", "(int n)", "(n)", "return Math.Min(helper(n), 5);", "(2)"),
            new Construct("math_arg_abs", "(int n)", "(n)", "return Math.Abs(helper(n));", "(2)"),
            new Construct("math_arg_round", "(int n)", "(n)", "return Math.Round(helper(n));", "(2)"),
            new Construct("math_arg_pow", "(int n)", "(n)", "return Math.Pow(helper(n), 2);", "(2)"),
            new Construct("math_arg_sqrt", "(int n)", "(n)", "return Math.Sqrt(helper(n));", "(3)"),
            new Construct("math_arg_expr", "(int n)", "(n)", "return Math.Max(helper(n) + 1, 5);", "(2)"),
            new Construct("math_arg_mapglob", "(int n)", "(n)", "return Math.Max(gmap[\"k\"], 5);", "(0)"),
            new Construct("math_arg_assign", "(int n)", "(n)", "v = Math.Max(helper(n), 5); return v;", "(2)"),
            // A truth value reaches the argument as the bare name of a bool local, so the text
            // alone cannot tell -- the declaration "var b=..>1;" is on an earlier line.
            // NeedsNumericArgument asks IsBoolLocal, which reads the type CollectLocalTypes
            // recorded; that is why the pass is an instance method rather than static.
            new Construct("math_arg_bool", "(int n)", "(n)", "b = n > 1; return Math.Max(b, 5);", "(2)"),
            // List and map arguments. Each is the caller's own Variable, fetched at the start and
            // used as a collection local; the typed copy the runtime also passes became an opaque
            // object whenever it met the interpreter -- "return a" gave a C# type name, "a[1].Upper"
            // the whole list -- and most uses did not compile. Changes reach the caller, as they do
            // when the function is interpreted.
            new Construct("carg_sum", "(list<int> a)", "(a)", "t = 0; for (i = 0; i < a.Size; i++) { t += a[i]; } return t;", "({1, 2, 3})"),
            new Construct("carg_fe_max", "(list<int> a)", "(a)", "m = a[0]; for (x in a) { if (x > m) { m = x; } } return m;", "({4, 9, 2})"),
            new Construct("carg_contains", "(list<int> a)", "(a)", "if (a.Contains(9)) { return 1; } return 0;", "({4, 9, 2})"),
            new Construct("carg_set", "(list<int> a)", "(a)", "a[0] = 7; return a[0] + a[1];", "({4, 9, 2})"),
            new Construct("carg_return", "(list<int> a)", "(a)", "return a;", "({4, 9, 2})"),
            new Construct("carg_str_member", "(list<string> a)", "(a)", "return a[1].Upper;", "({\"a\", \"b\", \"c\"})"),
            new Construct("carg_str_join", "(list<string> a)", "(a)", "r = \"\"; for (s in a) { r += s; } return r;", "({\"a\", \"b\", \"c\"})"),
            new Construct("carg_dbl_avg", "(list<double> a)", "(a)", "t = 0; for (x in a) { t += x; } return t / a.Size;", "({1.5, 2.5})"),
            new Construct("carg_map_keys", "(map<string,double> m)", "(m)", "t = 0; for (k in m.Keys) { t += m[k]; } return t;", "({\"a\": 1, \"b\": 2})"),
            new Construct("carg_map_add", "(map<string,double> m)", "(m)", "m[\"new\"] = 5; t = 0; for (k in m.Keys) { t += m[k]; } return t;", "({\"a\": 1})"),
            new Construct("carg_map_str", "(map<string,string> m)", "(m)", "r = \"\"; for (k in m.Keys) { r += k + \"=\" + m[k] + \";\"; } return r;", "({\"x\": \"1\", \"y\": \"2\"})"),
            new Construct("carg_recursive", "(list<int> a)", "(a)", "if (a.Size == 0) { return 0; } b = {}; for (i = 1; i < a.Size; i++) { b.Add(a[i]); } return a[0] + SELF(b);", "({1, 2, 3, 4})"),
            new Construct("varg_index", "(variable v)", "(v)", "return v[1];", "({4, 5})"),

            // An int argument is a C# int; next to arithmetic it is widened, as it already was next
            // to "/". Without that "n * n * n" for 100000 wrapped to -1530494976.
            new Construct("int_ovf_cube", "(int n)", "(n)", "return n * n * n;", "(100000)"),
            new Construct("int_ovf_mul", "(int a, int b)", "(a, b)", "return a * b;", "(100000, 100000)"),
            new Construct("int_ovf_add", "(int n)", "(n)", "return n + 2147483647;", "(5)"),
            new Construct("int_ovf_text", "(int n)", "(n)", "return \"v\" + n * n;", "(100000)"),

            // A widened position still indexes: SubstringCscs truncates a double as GetSafeInt does.
            new Construct("int_arg_sub_expr", "(string s, int n)", "(s, n)", "return s.Substring(n - 1);", "(\"hello\", 2)"),

            // An int argument the body assigns to becomes a double local, since the interpreter's
            // value need not be an int: "n = n / 2" is 13.5 for 27.
            new Construct("int_arg_collatz", "(int n)", "(n)", "c = 0; while (n > 1) { n = n % 2 == 0 ? n / 2 : 3 * n + 1; c++; } return c;", "(27)"),
            new Construct("int_arg_grow", "(int n)", "(n)", "n = n * n; return n;", "(100000)"),
            new Construct("int_arg_step", "(int n)", "(n)", "n++; n += 2; n--; return n * 10;", "(5)"),

            // A local used outside the block it is first assigned in is declared at the top of the
            // function: C# scopes a local to its block, CSCS does not. The type is the one all its
            // assignments agree on.
            new Construct("scope_anagram", "(string a, string b)", "(a, b)", "if (a.Length != b.Length) { return 0; } m = {}; for (i = 0; i < a.Length; i++) { c = a.At(i); if (m.Contains(c)) { m[c] += 1; } else { m[c] = 1; } } for (i = 0; i < b.Length; i++) { c = b.At(i); if (!m.Contains(c)) { return 0; } m[c] -= 1; if (m[c] < 0) { return 0; } } return 1;", "(\"listen\", \"silent\")"),
            new Construct("scope_branches", "(int n)", "(n)", "if (n > 0) { r = n * 2; } else { r = -1; } return r;", "(3)"),
            new Construct("scope_str_later", "(int n)", "(n)", "if (n > 0) { s = \"pos\"; } if (n < 10) { s = s + \"small\"; } return s;", "(3)"),
            new Construct("scope_coll_later", "(int n)", "(n)", "if (n > 0) { a = {1, 2}; } else { a = {3}; } return a.Size;", "(1)"),
            new Construct("scope_bool_later", "(int n)", "(n)", "if (n > 0) { ok = n > 2; } else { ok = false; } if (ok) { return 1; } return 0;", "(3)"),
            new Construct("scope_inst_later", "(int n)", "(n)", "if (n > 0) { p = new Point(1, n); } else { p = new Point(0, 0); } return p.Sum();", "(3)"),

            // Interpreter: a member after the second of two calls was left behind -- "Couldn't find
            // variable [.Length]" at the top level, silently dropped inside a function.
            new Construct("str_chain_prop", "(string s)", "(s)", "return s.Replace(\"a\", \"\").Replace(\"b\", \"\").Length;", "(\"aabbcc\")"),

            // Left interpreted: one branch assigns text and the other a number, so no one C# type fits.
            new Construct("scope_mixed_types", "(int n)", "(n)", "if (n > 0) { v = \"text\"; } else { v = 5; } return v;", "(3)"),

            // A crossing local whose assignments disagree on a type is declared a Variable.
            // Only on a real disagreement: a local with no plain assignment at all
            // ("double result = 0;" in a C#-form body) is left alone, as it always was.
            new Construct("cross_mixed_num_str", "(int n)", "(n)", "if (n > 0) { v = 5; } else { v = \"text\"; } return v;", "(3)"),
            new Construct("cross_mixed_else", "(int n)", "(n)", "if (n > 0) { v = \"text\"; } else { v = 5; } return v;", "(0)"),
            new Construct("cross_mixed_elif", "(int n)", "(n)", "if (n > 2) { v = \"a\"; } elif (n > 0) { v = 5; } else { v = 1; } return v;", "(3)"),
            new Construct("cross_mixed_coll", "(int n)", "(n)", "if (n > 0) { v = {1,2}; } else { v = 5; } return v.Size;", "(3)"),
            new Construct("cross_mixed_concat", "(int n)", "(n)", "if (n > 0) { v = \"text\"; } else { v = 5; } return v + \"!\";", "(3)"),
            new Construct("cross_mixed_cmp", "(int n)", "(n)", "if (n > 0) { v = \"text\"; } else { v = 5; } if (v == \"text\") { return 1; } return 0;", "(3)"),
            // A local whose first assignment is outside any block, later given another type.
            new Construct("cross_predeclared", "(int n)", "(n)", "v = 0; if (n > 0) { v = \"text\"; } return v;", "(3)"),
            new Construct("redecl_str_then_num", "(int n)", "(n)", "v = \"z\"; if (n > 0) { v = 5; } return v;", "(3)"),
            new Construct("redecl_top_num_str", "(int n)", "(n)", "v = 0; v = \"text\"; return v;", "(0)"),
            new Construct("redecl_top_str_num", "(int n)", "(n)", "v = \"z\"; v = 5; return v;", "(0)"),
            new Construct("redecl_loop", "(int n)", "(n)", "v = 0; for (i = 0; i < n; i++) { v = \"s\"; } return v;", "(2)"),
            new Construct("redecl_coll", "(int n)", "(n)", "v = 0; if (n > 0) { v = {1,2}; } return v.Size;", "(3)"),
            // A ternary assignment must not invent a disagreement: this compiled before too.
            new Construct("redecl_ternary_keep", "(int n)", "(n)", "v = 0; v = n > 2 ? 1 : 2; return v;", "(3)"),
            // Left interpreted: the widened int is a double, and Math.Round's digits must be an int.
            new Construct("int_arg_round_expr", "(double x, int n)", "(x, n)", "return Math.Round(x, n + 1);", "(3.14159, 1)"),

            // An argument with a default. The signature parser took the name to be the last
            // space-separated word, so "int n = 5" declared an argument called "5" -- rejected as an
            // illegal name at the first call -- and registered no default, so f() did not even match.
            new Construct("default_arg", "(int n = 5)", "(n = 5)", "return n * 2;", "()"),
            new Construct("default_arg_given", "(int n = 5)", "(n = 5)", "return n * 2;", "(7)"),
            new Construct("default_two", "(int a = 2, int b = 3)", "(a = 2, b = 3)", "return a * 10 + b;", "()"),
            new Construct("default_one_given", "(int a = 2, int b = 3)", "(a = 2, b = 3)", "return a * 10 + b;", "(9)"),
            new Construct("default_str", "(string s = \"hi\")", "(s = \"hi\")", "return s + \"!\";", "()"),
            new Construct("zero_arg", "()", "()", "return 42;", "()"),

            // Comments inside a body, block and line alike (test.cscs has the line form).
            new Construct("comment_block", "(int n)", "(n)", "x = n; /* doubled */ x = x * 2; return x;", "(3)"),
            new Construct("comment_in_expr", "(int n)", "(n)", "return n /* here */ + 2;", "(3)"),

            // An instance of a derived class: its own methods and fields, and the base's.
            new Construct("inherit_base_method", "(int n)", "(n)", "d = new Derived(2, 3); return d.addBase(n);", "(5)"),
            new Construct("inherit_own_method", "(int n)", "(n)", "d = new Derived(2, 3); return d.addAll(n);", "(5)"),
            new Construct("inherit_fields", "(int n)", "(n)", "d = new Derived(4, 5); return d.bx * 10 + d.dz + n;", "(0)"),

            // A function and a variable of a namespace.
            new Construct("ns_call", "(int n)", "(n)", "return nsp.nfunc(n);", "(3)"),
            new Construct("ns_var", "(int n)", "(n)", "return nsp.nlocal + n;", "(1)"),

            // A format passed to string(). Two things were wrong: the second argument never reached
            // ToText, and the format literal was split on its separators by SplitToken, which ignored
            // quotes -- "yyyy/MM/dd" went out as "yyyy/__varTempVar1/dd", and .NET then formatted the
            // date through the temp's name, turning its "m" into the value's minutes.
            new Construct("date_fmt", "(int n)", "(n)", "d = DateTime(\"9/11/2001 19:31:51\", \"M/d/yyyy HH:mm:ss\"); return string(d, \"yyyy/MM/dd\");", "(0)"),
            new Construct("date_fmt_time", "(int n)", "(n)", "d = DateTime(\"9/11/2001 19:31:51\", \"M/d/yyyy HH:mm:ss\"); return string(d, \"HH:mm:ss\");", "(0)"),
            new Construct("date_fmt_add", "(int n)", "(n)", "d = DateTime(\"9/11/2001 19:31:51\", \"M/d/yyyy HH:mm:ss\"); d.Add(\"1y\"); return string(d, \"yyyy/MM/dd\");", "(0)"),
            new Construct("num_fmt", "(double n)", "(n)", "return string(n, \"0.00\");", "(2.5)"),
            new Construct("conv_mapkey", "(int n)", "(n)", "m = {\"a\": \"7\"}; return int(m[\"a\"]) + n;", "(1)"),
            new Construct("conv_slash_concat", "(string s)", "(s)", "return string(\"a/\" + s);", "(\"b\")"),
            new Construct("idx_key_slash", "(int n)", "(n)", "m = {\"a/b\": 5}; return m[\"a/b\"] + n;", "(0)"),

            // The plain-function forms of the built-ins, rather than the member forms.
            new Construct("fn_size", "(int n)", "(n)", "a = {1, 2, 3}; return Size(a) + n;", "(0)"),
            new Construct("fn_substring", "(string s)", "(s)", "return Substring(s, 1, 2);", "(\"hello\")"),
            new Construct("fn_sort_member", "(int n)", "(n)", "a = {3, 1, 2}; a.Sort(); return a[0] * 100 + a[2] + n;", "(0)"),
            new Construct("fn_rev_member", "(int n)", "(n)", "a = {1, 2, 3}; a.Reverse(); return a[0] + n;", "(0)"),
            new Construct("fn_tokenize", "(string s)", "(s)", "t = Tokenize(s, \",\"); return t.Size;", "(\"a,b,c\")"),
            new Construct("fn_strbetween", "(string s)", "(s)", "return StrBetween(s, \"[\", \"]\");", "(\"x[y]z\")"),
            new Construct("fn_type_member", "(int n)", "(n)", "a = {1}; return a.Type;", "(0)"),
            new Construct("fn_typeof", "(string s)", "(s)", "return typeOf(s);", "(\"q\")"),
            new Construct("fn_deepcopy", "(int n)", "(n)", "a = {1, 2}; b = DeepCopy(a); b[0] = 9; return a[0] * 10 + b[0];", "(0)"),
            new Construct("fn_getkeys", "(int n)", "(n)", "m = {\"a\": 1, \"b\": 2}; k = GetKeys(m); return k.Size + n;", "(0)"),
            new Construct("fn_strupper", "(string s)", "(s)", "return StrUpper(s);", "(\"ab\")"),
            new Construct("fn_findindex", "(int n)", "(n)", "a = {5, 6, 7}; return find_index(a, 6) + n;", "(0)"),
            new Construct("fn_addunique", "(int n)", "(n)", "a = {1, 2}; a.AddUnique(2); a.AddUnique(3); return a.Size + n;", "(0)"),
            new Construct("fn_remove_item", "(int n)", "(n)", "a = {1, 2, 3}; a.Remove(2); return a.Size * 10 + a[1] + n;", "(0)"),
            new Construct("fn_math_log", "(double n)", "(n)", "return Math.Round(Math.Log(Math.Exp(n)), 6);", "(7)"),
            new Construct("fn_math_trig", "(double n)", "(n)", "return Math.Round(Math.Sin(n) * Math.Sin(n) + Math.Cos(n) * Math.Cos(n), 6);", "(1)"),
            new Construct("fn_math_atan2", "(double n)", "(n)", "return Math.Round(Math.Atan2(n, n), 4);", "(1)"),
            new Construct("three_d", "(int n)", "(n)", "g = {{{1, 2}, {3, 4}}, {{5, 6}, {7, 8}}}; return g[1][0][1] + n;", "(0)"),
            new Construct("map_elem_inc", "(int n)", "(n)", "m = {\"k\": 1}; m[\"k\"]++; return m[\"k\"] + n;", "(0)"),
            new Construct("do_continue", "(int n)", "(n)", "t = 0; i = 0; do { i++; if (i % 2 == 0) { continue; } t += i; } while (i < n); return t;", "(5)"),
            new Construct("str_cmp_le", "(string s)", "(s)", "if (s <= \"m\") { return 1; } return 0;", "(\"abc\")"),

            // A local collection passed to an interpreted function, which may also change it.
            new Construct("local_arr_to_fn", "(int n)", "(n)", "a = {1, 2, 3}; return sumArr(a) + n;", "(0)"),
            new Construct("local_map_to_fn", "(int n)", "(n)", "m = {\"a\": 2}; return mapVal(m, \"a\") + n;", "(0)"),
            new Construct("local_arr_mutated_by_fn", "(int n)", "(n)", "a = {1, 2}; addToEnd(a); return a.Size + n;", "(0)"),

            // Left interpreted, deliberately: the interpreter implements no shift, so "3 << 2" is 0
            // there, where C#'s shift gives 12. Compiling it would answer differently.
            new Construct("bit_shift", "(int n)", "(n)", "return n << 2;", "(3)"),

            // Left interpreted: iff is a statement in the interpreter and needs the script around it.
            // Called back with only its arguments it threw "Couldn't skip expression".
            new Construct("fn_iff", "(int n)", "(n)", "return iff(n > 2, \"big\", \"small\");", "(5)"),

            // Left interpreted: an enum's members are not names the generated C# knows.
            new Construct("enum_read", "(int n)", "(n)", "return Colors.Green + n;", "(1)"),
            // An enum member, or any Variable-valued expression, compared with a number.
            new Construct("enum_eq_num", "(int n)", "(n)", "if (Colors.Green == 1) { return 1; } return 0;", "(0)"),
            new Construct("enum_ne_num", "(int n)", "(n)", "if (Colors.Green != 0) { return 1; } return 0;", "(0)"),
            new Construct("enum_eq_and", "(int n)", "(n)", "if (Colors.Green == 1 && n > 0) { return 1; } return 0;", "(1)"),
            new Construct("enum_ret_eq", "(int n)", "(n)", "return Colors.Green == 1;", "(0)"),
            new Construct("global_eq_num", "(int n)", "(n)", "if (gcount == 1) { return 1; } return 0;", "(0)"),

            // A global as the RIGHT operand of "==" / "!=". The tokenizer splits on the
            // operator and leaves the condition's ")" glued to the global -- "gcount)" --
            // which ProcessFunction took for a function name and emitted as a call, leaving
            // the condition short a paren (CS1026). Relational operators never took that
            // path, so "gcount > 5" always compiled while "1 == gcount" did not.
            new Construct("glob_eq_lit_left", "(int n)", "(n)", "if (1 == gcount) { return 1; } return 0;", "(0)"),
            new Construct("glob_ne_lit_left", "(int n)", "(n)", "if (99 != gcount) { return 1; } return 0;", "(0)"),
            new Construct("glob_eq_glob", "(int n)", "(n)", "if (gcount == gcount) { return 1; } return 0;", "(0)"),
            new Construct("glob_eq_arg", "(int n)", "(n)", "if (n == gcount) { return 1; } return 0;", "(10)"),
            new Construct("glob_eq_local", "(int n)", "(n)", "v = 1; if (v == gcount) { return 1; } return 0;", "(0)"),
            new Construct("glob_eq_2paren", "(int n)", "(n)", "if ((1 == gcount)) { return 1; } return 0;", "(0)"),
            new Construct("glob_eq_while", "(int n)", "(n)", "t = 0; while (t == gcount) { t++; } return t;", "(0)"),
            new Construct("glob_eq_return", "(int n)", "(n)", "return 1 == gcount;", "(0)"),
            // A string against a global: emitted as Variable.SameValue, never an operator.
            new Construct("glob_eq_str_lit", "(int n)", "(n)", "if (\"gs\" == gstr) { return 1; } return 0;", "(0)"),
            new Construct("glob_str_right", "(int n)", "(n)", "if (gstr == \"gs\") { return 1; } return 0;", "(0)"),
            new Construct("glob_str_ne", "(int n)", "(n)", "if (gstr != \"zz\") { return 1; } return 0;", "(0)"),
            new Construct("glob_str_case", "(int n)", "(n)", "if (gstr == \"GS\") { return 1; } return 0;", "(0)"),
            new Construct("glob_str_arg", "(string s)", "(s)", "if (s == gstr) { return 1; } return 0;", "(\"gs\")"),
            new Construct("glob_str_ret", "(int n)", "(n)", "return gstr == \"gs\";", "(0)"),
            new Construct("glob_str_tern", "(int n)", "(n)", "return gstr == \"gs\" ? \"y\" : \"n\";", "(0)"),
            new Construct("glob_num_text", "(int n)", "(n)", "if (gcount == \"10\") { return 1; } return 0;", "(0)"),
            // A string or bool condition in "elif" / "else if": the token loop dropped the space
            // between "else" and "if" ("elseif"), and the string-comparison rewrite skipped elif.
            new Construct("elif_str_arg", "(string s)", "(s)", "if (s == \"zz\") { return 2; } elif (s == \"ab\") { return 1; } return 0;", "(\"ab\")"),
            new Construct("elif_str_lt", "(string s)", "(s)", "if (s == \"zz\") { return 2; } elif (s < \"b\") { return 1; } return 0;", "(\"ab\")"),
            new Construct("elif_str_else", "(string s)", "(s)", "if (s == \"zz\") { return 2; } elif (s == \"ab\") { return 1; } else { return 9; } return 0;", "(\"xx\")"),
            new Construct("elif_bool", "(int n)", "(n)", "b = n > 1; if (n > 5) { return 2; } elif (b) { return 1; } return 0;", "(3)"),
            new Construct("elif_elem_str", "(int n)", "(n)", "c = {\"a\"}; if (n > 5) { return 2; } elif (c[0] == \"a\") { return 1; } return 0;", "(0)"),
            new Construct("elif_glob_str", "(int n)", "(n)", "if (n > 5) { return 2; } elif (gstr == \"gs\") { return 1; } return 0;", "(0)"),
            new Construct("else_if_str", "(string s)", "(s)", "if (s == \"zz\") { return 2; } else if (s == \"ab\") { return 1; } return 0;", "(\"ab\")"),
            new Construct("elif_nested_str", "(string s)", "(s)", "if (s == \"a\") { return 1; } elif (s == \"b\") { if (s == \"b\") { return 2; } elif (s == \"c\") { return 3; } } return 0;", "(\"b\")"),
            // An assignment inside a condition: the name is declared at the top with the type
            // every assignment to it agrees on. A ternary, bool or disagreeing value falls back.
            new Construct("assign_in_cond", "(int n)", "(n)", "t = 0; while ((x = n - t) > 2) { t++; } return t;", "(6)"),

            new Construct("cond_asg_if", "(int n)", "(n)", "if ((y = n * 2) > 5) { return y; } return 0;", "(3)"),
            new Construct("cond_asg_ne", "(int n)", "(n)", "t = 0; while ((x = n - t) != 2) { t++; } return t;", "(6)"),
            new Construct("cond_asg_after", "(int n)", "(n)", "t = 0; while ((x = n - t) > 2) { t++; } return x;", "(6)"),
            new Construct("cond_asg_pair", "(int n)", "(n)", "if ((x = n + 1) > 2 && (z = x * 2) > 5) { return z; } return 0;", "(3)"),
            new Construct("cond_asg_elif", "(int n)", "(n)", "if (n > 10) { return 1; } elif ((y = n * 3) > 5) { return y; } return 0;", "(3)"),
            new Construct("cond_asg_str", "(string s)", "(s)", "if ((t = s + \"!\") == \"a!\") { return 1; } return 0;", "(\"a\")"),
            // Still falls back: the condition assigns a number, the block a string.
            new Construct("cond_asg_conflict", "(int n)", "(n)", "if ((v = n * 2) > 5) { v = \"big\"; } return v;", "(3)"),
            // A truth value assigned inside a condition. Refused while the interpreter itself threw on
            // "if ((b = n > 2))"; that crash is fixed, so it compiles as a bool local.
            new Construct("cond_bool_if", "(int n)", "(n)", "if ((b = n > 2)) { return 1; } return 0;", "(3)"),
            new Construct("cond_bool_false", "(int n)", "(n)", "if ((b = n > 2)) { return 1; } return 0;", "(1)"),
            new Construct("cond_bool_read", "(int n)", "(n)", "if ((b = n > 2)) { return b + 10; } return 0;", "(3)"),
            new Construct("cond_bool_while", "(int n)", "(n)", "t = 0; while ((b = t < n)) { t++; } return t;", "(3)"),
            new Construct("cond_bool_elif", "(int n)", "(n)", "if (n > 9) { return 2; } elif ((b = n > 2)) { return 1; } return 0;", "(3)"),
            new Construct("cond_bool_and", "(int n)", "(n)", "if ((b = n > 2 && n < 9)) { return 1; } return 0;", "(3)"),
            // A number as a whole condition is "!= 0", which is what the interpreter's truth test is
            // for a double; and an assignment in grouping parentheses outside a condition is declared
            // too -- but never one inside a call's arguments.
            new Construct("numcond_asg", "(int n)", "(n)", "if ((b = n + 2)) { return b; } return 0;", "(3)"),
            new Construct("numcond_asg_zero", "(int n)", "(n)", "if ((b = n - 3)) { return 1; } return 0;", "(3)"),
            new Construct("numcond_while", "(int n)", "(n)", "t = n; while ((t = t - 1)) { } return t;", "(3)"),
            new Construct("numcond_local", "(int n)", "(n)", "b = n * 1.5; if (b) { return 1; } return 0;", "(2)"),
            new Construct("numcond_local_zero", "(int n)", "(n)", "b = n * 0; if (b) { return 1; } return 0;", "(2)"),
            new Construct("grpasg_nested", "(int n)", "(n)", "x = ((b = 7)); return x + b;", "(0)"),
            new Construct("grpasg_expr", "(int n)", "(n)", "x = (b = n * 2) + 1; return x + b;", "(3)"),
            new Construct("grpasg_single", "(int n)", "(n)", "x = (b = 7); return x + b;", "(0)"),
            new Construct("grpasg_in_call_keep", "(int n)", "(n)", "return Math.Max((q = n * 2), 5) + q;", "(2)"),
            new Construct("numcond_alias_keep", "(int n)", "(n)", "b = n > 1; c = b; if (c) { return 1; } return 0;", "(2)"),
            // A number beside "!", "&&" or "||": each numeric clause is compared with zero.
            new Construct("numlogic_not", "(int n)", "(n)", "b = n * 1.0; if (!b) { return 1; } return 0;", "(0)"),
            new Construct("numlogic_not_true", "(int n)", "(n)", "b = n * 1.0; if (!b) { return 1; } return 0;", "(3)"),
            new Construct("numlogic_and", "(int n)", "(n)", "b = n * 1.0; if (b && n < 5) { return 1; } return 0;", "(2)"),
            new Construct("numlogic_or", "(int n)", "(n)", "b = n * 0.0; if (b || n > 1) { return 1; } return 0;", "(2)"),
            new Construct("numlogic_two", "(int n)", "(n)", "b = n * 1.0; c = n - 2.0; if (b && c) { return 1; } return 0;", "(2)"),
            new Construct("numlogic_while", "(int n)", "(n)", "t = n * 1.0; k = 0; while (t && k < 10) { t = t - 1; k++; } return k;", "(3)"),
            new Construct("numlogic_arg_not", "(int n)", "(n)", "if (!n) { return 1; } return 0;", "(0)"),
            new Construct("numlogic_bool_mix", "(int n)", "(n)", "f = n > 1; b = n * 1.0; if (f && b) { return 1; } return 0;", "(2)"),
            new Construct("numlogic_elem_keep", "(int n)", "(n)", "a = {0, 3}; b = n * 1.0; if (b && a[1]) { return 1; } return 0;", "(2)"),
            // A clause whose type is settled when it runs -- an element, a Variable local, a string --
            // beside another clause: read through CscsConvert.IsTrue/IsFalse, the interpreter's pair.
            // Not "!= 0": an element holding "5" is false there, though AsDouble() would say true.
            new Construct("truthy_elem_and", "(int n)", "(n)", "a = {0, 3}; b = n * 1.0; if (a[1] && b) { return 1; } return 0;", "(2)"),
            new Construct("truthy_elem_or", "(int n)", "(n)", "a = {0, 3}; if (a[0] || a[1]) { return 1; } return 0;", "(0)"),
            new Construct("truthy_varlocal_and", "(int n)", "(n)", "v = helper(n); if (v && n > 1) { return 1; } return 0;", "(2)"),
            new Construct("truthy_str_and", "(string s, int n)", "(s, n)", "if (s && n > 1) { return 1; } return 0;", "(\"x\", 2)"),
            new Construct("truthy_elem_not_and", "(int n)", "(n)", "a = {0, 3}; if (!a[0] && n > 1) { return 1; } return 0;", "(2)"),
            // A string is false as a whole condition too, not only beside another clause -- and "!s"
            // is false as well, since "!x" asks for a number that is zero. CscsConvert takes an
            // object, so a C# string argument reaches the same test a Variable does.
            new Construct("strcond_alone", "(string s)", "(s)", "if (s) { return 1; } return 0;", "(\"ab\")"),
            new Construct("strcond_num", "(string s)", "(s)", "if (s) { return 1; } return 0;", "(\"5\")"),
            new Construct("strcond_empty", "(string s)", "(s)", "if (s) { return 1; } return 0;", "(\"\")"),
            new Construct("strcond_local", "(int n)", "(n)", "t = \"x\"; if (t) { return 1; } return 0;", "(0)"),
            new Construct("strcond_asg", "(string s)", "(s)", "if ((t = s + \"x\")) { return 1; } return 0;", "(\"ab\")"),
            new Construct("strcond_not", "(string s)", "(s)", "if (!s) { return 1; } return 0;", "(\"5\")"),
            new Construct("strcond_not_str", "(string s)", "(s)", "if (!s && 1 == 1) { return 1; } return 0;", "(\"5\")"),
            new Construct("strcond_not_join", "(int n)", "(n)", "vals = {\"5\", 0}; r = 0; if (!vals[0] && n == 0) { r += 10; } if (!vals[1] && n == 0) { r += 100; } return r;", "(0)"),
            // Kept on the interpreter: ".Length" is a number there, but a C# int in a condition.
            new Construct("strcond_len_keep", "(string s)", "(s)", "if (s.Length) { return 1; } return 0;", "(\"ab\")"),

            // An assignment inside a return. Whitespace is stripped by the time the declaration scan
            // runs, so "return(b=n*2)+b" looked exactly like a call to a function named "return" and
            // nothing declared b (CS0103). A keyword's own parenthesis is now told apart from a call's.
            new Construct("retasg_plus", "(int n)", "(n)", "return (b = n * 2) + b;", "(3)"),
            new Construct("retasg_alone", "(int n)", "(n)", "return (b = n * 2);", "(3)"),
            new Construct("retasg_mult", "(int n)", "(n)", "return (b = n * 2) * b;", "(3)"),
            new Construct("retasg_str", "(string s)", "(s)", "return (t = s + \"x\") + t;", "(\"a\")"),
            new Construct("retasg_double", "(int n)", "(n)", "return (b = n * 2) + (c = b + 1) + c;", "(3)"),
            new Construct("retasg_nested", "(int n)", "(n)", "return ((b = n * 2)) + b;", "(3)"),
            new Construct("retasg_pre", "(int n)", "(n)", "b = 1; return (b = n * 2) + b;", "(3)"),
            new Construct("retasg_used_later", "(int n)", "(n)", "x = 0; if (n > 0) { x = (b = n * 2) + b; } return x; ", "(3)"),
            // A return that opens with a group, inside a block. The interpreter used to truncate these
            // at the closing parenthesis -- 6 where the arithmetic says 9 -- so they were refused to
            // keep compiled code from answering differently. Fixed in ReturnStatement.Evaluate
            // (Functions.Flow.cs) on 2026-09-13, and compiled since.
            new Construct("retasg_block", "(int n)", "(n)", "if (n > 0) { return (b = n * 2) + b; } return 0;", "(3)"),
            new Construct("retgrp_block", "(int n)", "(n)", "if (n > 0) { return (n * 2) + n; } return 0;", "(3)"),
            new Construct("retgrp_while", "(int n)", "(n)", "while (n > 0) { return (n * 2) + n; } return 0;", "(3)"),
            new Construct("retgrp_str", "(string s)", "(s)", "if (s != \"\") { return (s + \"x\") + \"y\"; } return \"\";", "(\"a\")"),
            // Kept: "(b = n > 2) + b" adds a C# bool to an int (CS0019).
            // A truth-valued assignment group beside arithmetic. It is 1 or 0 in CSCS but a C# bool, and
            // there is no single token to convert, so the group is hoisted into a statement of its own --
            // the order CSCS evaluates in anyway -- leaving a name the bool-to-number rule handles.
            new Construct("boolgrp_ret", "(int n)", "(n)", "return (b = n > 2) + b;", "(3)"),
            new Construct("boolgrp_ret_false", "(int n)", "(n)", "return (b = n > 2) + b;", "(1)"),
            new Construct("boolgrp_mult", "(int n)", "(n)", "return (b = n > 2) * 5;", "(3)"),
            new Construct("boolgrp_asg", "(int n)", "(n)", "t = (b = n > 2) + 10; return t;", "(3)"),
            // Kept: two groups in one expression -- the second is not hoisted, so it stays a C# bool.
            new Construct("boolgrp_twice_keep", "(int n)", "(n)", "return (b = n > 2) + (c = n > 1) + b + c;", "(3)"),
            // Kept: the call builder does not carry an assignment out of an argument list.
            new Construct("retasg_call_keep", "(int n)", "(n)", "return helper((q = n * 2)) + q;", "(4)"),

            // A numeric member as the whole condition. CSCS reads ".Length" and ".Size" as numbers, C#
            // as ints, and the two agree on every type -- Variable.Size is 0 for anything but an array,
            // just as the interpreter reports 0 for a string. A C# condition needs a bool, so these were
            // CS0029 until the numeric rewrite learned to read them as "!= 0".
            new Construct("len_cond", "(string s)", "(s)", "if (s.Length) { return 1; } return 0;", "(\"ab\")"),
            new Construct("len_cond_empty", "(string s)", "(s)", "if (s.Length) { return 1; } return 0;", "(\"\")"),
            new Construct("len_not", "(string s)", "(s)", "if (!s.Length) { return 1; } return 0;", "(\"\")"),
            new Construct("len_and", "(string s, int n)", "(s, n)", "if (s.Length && n > 1) { return 1; } return 0;", "(\"ab\", 2)"),
            new Construct("len_while", "(string s)", "(s)", "t = 0; while (s.Length) { t += 1; s = s.Substring(1, s.Length - 1); } return t;", "(\"abc\")"),
            new Construct("size_cond", "(int n)", "(n)", "a = {1,2}; if (a.Size) { return 1; } return 0;", "(0)"),
            new Construct("size_empty", "(int n)", "(n)", "a = {}; if (a.Size) { return 1; } return 0;", "(0)"),
            new Construct("size_not", "(int n)", "(n)", "a = {}; if (!a.Size) { return 1; } return 0;", "(0)"),
            new Construct("size_map", "(int n)", "(n)", "m = {\"k\" : 1}; if (m.Size) { return 1; } return 0;", "(0)"),
            new Construct("size_call_result", "(int n)", "(n)", "c = build(); if (c.Size) { return 1; } return 0;", "(0)"),
            new Construct("size_of_var", "(int n)", "(n)", "v = helper(n); if (v.Size) { return 1; } return 0;", "(2)"),
            // A member read off a local holding a Variable is a Variable when it runs, so its truth is
            // the interpreter's own test, not a C# conversion. A member holding text is false there.
            new Construct("mem_cond", "(int n)", "(n)", "p = new Point(0, 5); if (p.y) { return 1; } return 0;", "(0)"),
            new Construct("mem_cond_zero", "(int n)", "(n)", "p = new Point(0, 5); if (p.x) { return 1; } return 0;", "(0)"),
            new Construct("mem_not", "(int n)", "(n)", "p = new Point(0, 5); if (!p.x) { return 1; } return 0;", "(0)"),
            new Construct("mem_and", "(int n)", "(n)", "p = new Point(0, 5); if (p.y && n == 0) { return 1; } return 0;", "(0)"),
            new Construct("mem_str_false", "(int n)", "(n)", "q = new Named(1, \"t\"); if (q.tag) { return 1; } return 0;", "(0)"),
            new Construct("mem_num", "(int n)", "(n)", "q = new Named(7, \"t\"); if (q.v) { return 1; } return 0;", "(0)"),
            new Construct("mem_while", "(int n)", "(n)", "p = new Point(0, 3); t = 0; while (p.y) { t += 1; p.y = p.y - 1; } return t;", "(0)"),
            // Kept on the interpreter: a C# string has no Size member at all.
            new Construct("str_size_keep", "(string s)", "(s)", "if (s.Size) { return 1; } return 0;", "(\"ab\")"),
            // A property written as a call. The mapping adds no parentheses of its own -- the script's
            // own are copied through -- and it is safe to compile only since the interpreter stopped
            // leaving a property's "()" behind. "s.Trim" stays interpreted: it does not trim there.
                        new Construct("upper_call", "(string s)", "(s)", "return s.Upper();", "(\"ab\")"),
            new Construct("upper_call_cmp", "(string s)", "(s)", "if (s.Upper() == \"AB\") { return 1; } return 0;", "(\"ab\")"),
            new Construct("upper_call_chain", "(string s)", "(s)", "return s.Upper().Trim();", "(\" ab \")"),
            new Construct("lower_call", "(string s)", "(s)", "return s.Lower();", "(\"AB\")"),
            new Construct("trim_prop_keep", "(string s)", "(s)", "return s.Trim;", "(\" a \")"),
            // A method on a collection element. The expression builder has always built these, but only
            // for a KNOWN expression: "return a[0].Sum() + a[1].Sum();" qualified and the lone
            // "return a[1].Sum();" did not, so the call went out as C# ".Sum()" on a Variable (CS1929).
            // Keyed subscripts too: they were refused while a map literal holding a "new" came out of the
            // interpreter as a tuple, which is fixed in Parser.CheckConsistencyAndSign.
            new Construct("elem_method", "(int n)", "(n)", "a = {new Point(1,2), new Point(3,4)}; return a[1].Sum();", "(0)"),
            new Construct("elem_method_first", "(int n)", "(n)", "a = {new Point(1,2)}; return a[0].Sum();", "(0)"),
            new Construct("elem_method_arg", "(int n)", "(n)", "a = {new Point(1,2)}; return a[0].Scale(n);", "(3)"),
            new Construct("elem_method_argvar", "(int n)", "(n)", "a = {new Point(1,2), new Point(3,4)}; return a[n].Sum();", "(1)"),
            new Construct("elem_method_expr", "(int n)", "(n)", "a = {new Point(1,2), new Point(3,4)}; return a[n - 1].Sum();", "(2)"),
            new Construct("elem_method_loop", "(int n)", "(n)", "a = {new Point(1,2), new Point(3,4)}; t = 0; for (i = 0; i < 2; i++) { t += a[i].Sum(); } return t;", "(0)"),
            // A member read off an element: a field goes through GetProperty, a Variable's own property
            // is read directly, and a property written as a call -- "a[0].Upper()" -- drops the
            // empty parentheses, exactly as the interpreter's own property lookup now does.
            new Construct("elem_field", "(int n)", "(n)", "a = {new Point(1,2)}; return a[0].x;", "(0)"),
            new Construct("elem_field_cond", "(int n)", "(n)", "a = {new Point(1,2)}; if (a[0].y > 1) { return 1; } return 0;", "(0)"),
            new Construct("elem_str_field", "(int n)", "(n)", "q = {new Named(1, \"t\")}; return q[0].tag;", "(0)"),
            new Construct("elem_size", "(int n)", "(n)", "a = {{1,2,3}}; return a[0].Size;", "(0)"),
            new Construct("elem_type", "(int n)", "(n)", "a = {\"abc\"}; return a[0].Type;", "(0)"),
            new Construct("elem_first", "(int n)", "(n)", "a = {{5,6}}; return a[0].First;", "(0)"),
            new Construct("elem_upper_call", "(int n)", "(n)", "a = {\"ab\"}; return a[0].Upper();", "(0)"),
            new Construct("elem_size_call", "(int n)", "(n)", "a = {{1,2,3}}; return a[0].Size();", "(0)"),
            new Construct("elem_first_call", "(int n)", "(n)", "a = {{5,6}}; return a[0].First();", "(0)"),
            new Construct("elem_lower_call", "(int n)", "(n)", "a = {\"AB\"}; return a[0].Lower();", "(0)"),
            new Construct("elem_sort_still", "(int n)", "(n)", "a = {{3,1,2}}; a[0].Sort(); return a[0][0];", "(0)"),
            new Construct("elem_replace_still", "(int n)", "(n)", "a = {\"abc\"}; return a[0].Replace(\"a\", \"z\");", "(0)"),
            // A chained assignment whose targets are elements. The unrolling that already handled
            // "x = y = 7" -- write the innermost, then assign each target the one to its right, which is
            // the right-to-left order CSCS evaluates in -- now takes element targets too. Emitting it as
            // written made C# assign to a Variable's indexer, which is read only (CS0200/CS0131).
            new Construct("chain_elem", "(int n)", "(n)", "a = {1,2}; a[0] = a[1] = 7; return a[0] + a[1];", "(0)"),
            new Construct("chain_elem_value", "(int n)", "(n)", "a = {1,2}; b = a[1] = 7; return b + a[0];", "(0)"),
            new Construct("chain_map", "(int n)", "(n)", "m = {}; m[\"a\"] = m[\"b\"] = 3; return m[\"a\"] + m[\"b\"];", "(0)"),
            new Construct("chain_triple", "(int n)", "(n)", "f = {1,2}; g = f[0] = f[1] = 4; return g + f[0] + f[1];", "(0)"),
            new Construct("chain_idx_param", "(int n)", "(n)", "a = {1,2,3}; a[n] = a[0] = 7; return a[n] + a[0];", "(2)"),
            new Construct("chain_nested", "(int n)", "(n)", "e = {{1,2},{3,4}}; e[0][1] = e[1][0] = 5; return e[0][1] + e[1][0];", "(0)"),
            new Construct("chain_map_varkey", "(int n)", "(n)", "m = {}; k = \"z\"; m[k] = m[\"y\"] = 4; return m[k] + m[\"y\"];", "(0)"),
            new Construct("chain_four", "(int n)", "(n)", "a = {1,2,3}; a[0] = a[1] = a[2] = 6; return a[0] + a[1] + a[2];", "(0)"),
            new Construct("chain_global", "(int n)", "(n)", "garr[0] = garr[1] = 8; return garr[0] + garr[1];", "(0)"),
            // Kept: the index runs twice in the unrolling -- once as a target, once as the value beside
            // it -- so one that can change something is left alone. A member target is left alone too.
            new Construct("chain_inc_index_keep", "(int n)", "(n)", "a = {1,2,3}; i = 0; a[i++] = a[0] = 7; return a[0] + i;", "(0)"),
            new Construct("chain_member", "(int n)", "(n)", "p = new Point(1,2); p.x = p.y = 5; return p.x + p.y;", "(0)"),
            new Construct("chain_member_value", "(int n)", "(n)", "p = new Point(1,2); v = p.y = 6; return v + p.y;", "(0)"),
            new Construct("chain_member_elem", "(int n)", "(n)", "p = new Point(1,2); a = {0,0}; a[0] = p.y = 4; return a[0] + p.y;", "(0)"),
            new Construct("chain_two_objects", "(int n)", "(n)", "p = new Point(1,2); q = new Point(3,4); p.x = q.y = 5; return p.x + q.y;", "(0)"),
            new Construct("chain_three_members", "(int n)", "(n)", "p = new Point(1,2); q = new Point(3,4); p.x = p.y = q.x = 6; return p.x + p.y + q.x;", "(0)"),
            new Construct("chain_str_member", "(int n)", "(n)", "q = new Named(1, \"t\"); v = q.tag = \"z\"; return v + q.tag;", "(0)"),
            new Construct("chain_member_loop", "(int n)", "(n)", "p = new Point(0,0); t = 0; for (i = 1; i < 3; i++) { p.x = p.y = i; t += p.x + p.y; } return t;", "(0)"),
            // Kept: the owner has to be a name the unrolling can mention twice without re-running it,
            // so a deeper member ("q.kid.x") and a member of an element ("a[0].v") stay interpreted.
            new Construct("chain_deep_keep", "(int n)", "(n)", "q = new Named(1, \"t\"); q.kid = new Point(1,2); q.kid.x = q.v = 5; return q.v;", "(0)"),
            new Construct("chain_elem_member_keep", "(int n)", "(n)", "a = {new Named(1, \"t\")}; p = new Point(1,2); a[0].v = p.x = 3; return p.x;", "(0)"),
            // A compound assignment to a member, and the increment forms. C# has neither for a member of
            // a Variable (CS1061/CS1059), so each becomes the read-apply-write the interpreter does,
            // through the same Compound helper an element compound already used.
            new Construct("memcomp_plus", "(int n)", "(n)", "p = new Point(1,2); p.x += 5; return p.x;", "(0)"),
            new Construct("memcomp_minus", "(int n)", "(n)", "p = new Point(1,2); p.y -= 1; return p.y;", "(0)"),
            new Construct("memcomp_mult", "(int n)", "(n)", "p = new Point(1,2); p.y *= 3; return p.y;", "(0)"),
            new Construct("memcomp_div", "(int n)", "(n)", "p = new Point(8,2); p.x /= 4; return p.x;", "(0)"),
            new Construct("memcomp_str", "(int n)", "(n)", "q = new Named(1, \"t\"); q.tag += \"z\"; return q.tag;", "(0)"),
            new Construct("memcomp_str_num", "(int n)", "(n)", "q = new Named(1, \"t\"); q.tag += n; return q.tag;", "(7)"),
            new Construct("memcomp_inc", "(int n)", "(n)", "p = new Point(1,2); p.x++; return p.x;", "(0)"),
            new Construct("memcomp_dec", "(int n)", "(n)", "p = new Point(1,2); p.y--; return p.y;", "(0)"),
            new Construct("memcomp_arg", "(int n)", "(n)", "p = new Point(1,2); p.x += n; return p.x;", "(4)"),
            new Construct("memcomp_call_value", "(int n)", "(n)", "p = new Point(1,2); p.x += helper(n); return p.x;", "(2)"),
            new Construct("memcomp_loop", "(int n)", "(n)", "p = new Point(0,0); for (i = 0; i < 3; i++) { p.x += 2; } return p.x;", "(0)"),
            new Construct("memcomp_while", "(int n)", "(n)", "p = new Point(5,0); while (p.x > 2) { p.x--; } return p.x;", "(0)"),
            // Kept: the prefix form is worth the field's new value, which the statement shape does not
            // produce, and a member of an element is not a named owner.
            new Construct("memcomp_pre_keep", "(int n)", "(n)", "p = new Point(1,2); return ++p.x;", "(0)"),
            new Construct("elemwrite_comp", "(int n)", "(n)", "a = {new Named(1, \"t\")}; a[0].v += 3; return a[0].v;", "(0)"),
            // A member write through a subscript: plain, compound and increment, on an array element,
            // a map element, with an argument or an expression as the index. The value is worked out
            // before the subscript, the order the interpreter uses, and the element is taken through
            // CscsConvert.ElementForWrite, which throws on a missing element exactly as the interpreter
            // does -- the indexer cannot, since a missing element reads back as a brand-new Variable.
            new Construct("elemwrite_set", "(int n)", "(n)", "a = {new Named(1, \"t\"), new Named(2, \"u\")}; a[0].v = 9; return a[0].v + a[1].v;", "(0)"),
            new Construct("elemwrite_set_str", "(int n)", "(n)", "a = {new Named(1, \"t\")}; a[0].tag = \"z\"; return a[0].tag;", "(0)"),
            new Construct("elemwrite_arg_idx", "(int n)", "(n)", "a = {new Named(1, \"t\"), new Named(2, \"u\")}; a[n].v = 7; return a[n].v;", "(1)"),
            new Construct("elemwrite_call_val", "(int n)", "(n)", "a = {new Named(1, \"t\")}; a[0].v = helper(n); return a[0].v;", "(2)"),
            new Construct("elemwrite_map", "(int n)", "(n)", "m = {}; m[\"k\"] = new Named(5, \"t\"); m[\"k\"].v = 8; return m[\"k\"].v;", "(0)"),
            new Construct("elemwrite_order", "(int n)", "(n)", "a = {new Named(1, \"t\"), new Named(2, \"u\")}; i = 0; a[i].v = (i = 1) + 10; return a[0].v + \"|\" + a[1].v;", "(0)"),
            new Construct("elemwrite_comp_str", "(int n)", "(n)", "a = {new Named(1, \"t\")}; a[0].tag += \"z\"; return a[0].tag;", "(0)"),
            new Construct("elemwrite_default_twice", "(int n)", "(n)", "a = {new Named(1, \"t\")}; a[0].tag += \"z\"; b = {new Named(2, \"t\")}; return a[0].tag + \"|\" + b[0].tag;", "(0)"),
            new Construct("elemwrite_inc", "(int n)", "(n)", "a = {new Named(1, \"t\")}; a[0].v++; a[0].v++; return a[0].v;", "(0)"),
            new Construct("elemwrite_dec", "(int n)", "(n)", "a = {new Named(5, \"t\")}; a[0].v--; return a[0].v;", "(0)"),
            new Construct("elemwrite_comp_map", "(int n)", "(n)", "m = {}; m[\"k\"] = new Named(5, \"t\"); m[\"k\"].v *= 2; return m[\"k\"].v;", "(0)"),
            new Construct("elemwrite_loop", "(int n)", "(n)", "a = {new Named(0, \"t\"), new Named(0, \"u\")}; for (i = 0; i < 2; i++) { a[i].v += i + 1; a[i].v++; } return a[0].v + a[1].v;", "(0)"),
            // Kept: the WRITE compiles, but a read in the same function does not yet -- a nested
            // element member ("e[0][0].v") and an element member assigned to a local ("r = a[0].v").
            new Construct("elemwrite_nested_read_keep", "(int n)", "(n)", "e = {{new Named(7, \"t\")}}; e[0][0].v = 3; return e[0][0].v;", "(0)"),
            new Construct("elemwrite_assign_read_keep", "(int n)", "(n)", "a = {new Named(1, \"t\")}; r = a[0].v; a[0].v += 5; return r + \"|\" + a[0].v;", "(0)"),
            // A switch with consecutive case labels. They arrive on one line, so the second label was
            // left inside the first clause's body and went out as a C# "case" in the middle of an if
            // (CS1003). Each label now starts its own clause with an empty body, which is what
            // fall-through already is here: the first sets the match flag, the next clause's body runs.
            new Construct("switch_fall_two", "(int n)", "(n)", "switch (n) { case 1: case 2: return 12; default: return 0; }", "(2)"),
            new Construct("switch_fall_first", "(int n)", "(n)", "switch (n) { case 1: case 2: return 12; default: return 0; }", "(1)"),
            new Construct("switch_fall_miss", "(int n)", "(n)", "switch (n) { case 1: case 2: return 12; default: return 0; }", "(5)"),
            new Construct("switch_fall_three", "(int n)", "(n)", "switch (n) { case 1: case 2: case 3: return 99; default: return 0; }", "(3)"),
            new Construct("switch_fall_str", "(string s)", "(s)", "switch (s) { case \"a\": case \"b\": return \"ab\"; default: return \"z\"; }", "(\"b\")"),
            new Construct("switch_fall_mixed", "(int n)", "(n)", "switch (n) { case 1: return 10; case 2: case 3: return 23; default: return 0; }", "(3)"),
            new Construct("switch_fall_body", "(int n)", "(n)", "t = 0; switch (n) { case 1: case 2: t = 5; break; default: t = 1; } return t;", "(2)"),
            new Construct("switch_fall_default", "(int n)", "(n)", "switch (n) { case 1: default: return 7; }", "(9)"),
            // Real fall-through, where the first clause has a body of its own and no break.
            new Construct("switch_real_fall", "(int n)", "(n)", "t = 0; switch (n) { case 1: t += 1; case 2: t += 10; break; default: t = 99; } return t;", "(1)"),
            new Construct("mapasg_method", "(int n)", "(n)", "m = {}; m[\"p\"] = new Point(1,2); return m[\"p\"].Sum();", "(0)"),
            new Construct("mapnew_direct", "(int n)", "(n)", "m = {\"p\" : new Point(1,2)}; return m[\"p\"].Sum();", "(0)"),
            new Construct("mapnew_varkey", "(int n)", "(n)", "m = {\"p\" : new Point(1,2)}; k = \"p\"; return m[k].Sum();", "(0)"),
            new Construct("mapnew_mixed", "(int n)", "(n)", "m = {\"a\" : 1, \"p\" : new Point(1,2)}; return m[\"a\"] + m[\"p\"].Sum();", "(0)"),
            new Construct("mapnew_first", "(int n)", "(n)", "m = {\"p\" : new Point(1,2), \"a\" : 1}; return m[\"a\"] + m[\"p\"].Sum();", "(0)"),
            // A field on a map element reads the same way.
            new Construct("elem_map_field", "(int n)", "(n)", "m = {\"p\" : new Point(1,2)}; return m[\"p\"].x;", "(0)"),
            // "===" on an int argument or a numeric local: rewritten to "==", both sides being
            // certainly numbers. An element, a mixed pair or a conflicting local still falls back.
            new Construct("strict_int_eq", "(int n)", "(n)", "if (n === 5) { return 1; } return 0;", "(5)"),
            new Construct("strict_int_ne", "(int n)", "(n)", "if (n !== 5) { return 1; } return 0;", "(5)"),
            new Construct("strict_int_ret", "(int n)", "(n)", "return n === 5;", "(5)"),
            new Construct("strict_num_local", "(int n)", "(n)", "v = n + 1; if (v === 6) { return 1; } return 0;", "(5)"),
            new Construct("strict_two_ints", "(int n)", "(n)", "m = 5; if (n === m) { return 1; } return 0;", "(5)"),
            new Construct("strict_elem_keep", "(int n)", "(n)", "a = {5}; if (a[0] === 5) { return 1; } return 0;", "(0)"),
            // Left interpreted: the plain-function Contains has no C# counterpart taking a collection.
            new Construct("fn_contains_arr", "(int n)", "(n)", "a = {1, 2}; if (Contains(a, 2)) { return 1; } return 0;", "(0)"),

            // A finally clause. "finally" is not one of Constants.RESERVED, so it never reached the
            // clause handling "catch" goes through: it was resolved as a name, became a callback to a
            // function of that name, and threw at run time. C# spells it the same way.
            new Construct("try_finally", "(int n)", "(n)", "r = 0; try { r = 1; throw \"x\"; } catch (e) { r += 10; } finally { r += 100; } return r;", "(0)"),
            new Construct("try_finally_noexc", "(int n)", "(n)", "r = 0; try { r = 1; } catch (e) { r += 10; } finally { r += 100; } return r;", "(0)"),

            // Division by zero, and the values it produces. CSCS numbers are doubles, so these are
            // infinities and NaN in both, and they have to print the same way too.
            new Construct("div_zero", "(double n)", "(n)", "return n / 0;", "(5)"),
            new Construct("mod_zero", "(double n)", "(n)", "return n % 0;", "(5)"),
            new Construct("float_add", "(double n)", "(n)", "return n + 0.2;", "(0.1)"),
            new Construct("float_eq", "(double n)", "(n)", "if (n + 0.2 == 0.3) { return 1; } return 0;", "(0.1)"),
            new Construct("neg_zero", "(double n)", "(n)", "return -n * 0;", "(5)"),
            new Construct("big_precision", "(double n)", "(n)", "return Math.Round(n / 3, 10);", "(1)"),
            new Construct("str_div_concat", "(double n)", "(n)", "return \"v=\" + n / 3;", "(1)"),

            // Collections inside collections: writing through two subscripts, appending to an element,
            // and building a nested map from nothing. An element is a Variable, which has no Add of its
            // own, so the runtime adds one -- the element being the same object the collection holds.
            new Construct("map_nested_write", "(int n)", "(n)", "m = {\"a\": {\"b\": 1}}; m[\"a\"][\"b\"] = 5; return m[\"a\"][\"b\"] + n;", "(0)"),
            new Construct("map_arr_append", "(int n)", "(n)", "m = {\"a\": {1, 2}}; m[\"a\"].Add(3); return m[\"a\"].Size + n;", "(0)"),
            new Construct("map_new_nested", "(int n)", "(n)", "m = {}; m[\"a\"] = {}; m[\"a\"][\"b\"] = 7; return m[\"a\"][\"b\"] + n;", "(0)"),
            new Construct("map_arr_of_maps", "(int n)", "(n)", "a = {}; a.Add({\"k\": 1}); a.Add({\"k\": 2}); t = 0; for (e in a) { t += e[\"k\"]; } return t + n;", "(0)"),

            // A constructor's own default arguments, and a class's ToString -- in a concatenation and
            // called by name.
            new Construct("new_defaults", "(int n)", "(n)", "c = new WithDef(); return c.da * 100 + c.db * 10 + c.dc + n;", "(0)"),
            new Construct("new_defaults_partial", "(int n)", "(n)", "c = new WithDef(7); return c.da * 100 + c.db * 10 + c.dc + n;", "(0)"),
            new Construct("tostring_concat", "(int n)", "(n)", "c = new WithDef(1, 2, 3); return \"v=\" + c;", "(0)"),
            new Construct("tostring_call", "(int n)", "(n)", "c = new WithDef(1, 2, 3); return c.ToString();", "(0)"),

            // A for header with a section left out. There is no counter to declare then, and declaring
            // one anyway emitted "double ;".
            new Construct("for_no_init", "(int n)", "(n)", "i = 1; t = 0; for (; i <= n; i++) { t += i; } return t;", "(4)"),
            new Construct("for_no_step", "(int n)", "(n)", "t = 0; for (i = 0; i < n;) { t += i; i++; } return t;", "(4)"),
            new Construct("arr_lit_expr", "(int n)", "(n)", "a = {n + 1, helper(n), n * 2}; return a[0] + a[1] + a[2];", "(2)"),
            new Construct("map_lit_expr", "(int n)", "(n)", "m = {\"a\": n + 1, \"b\": helper(n)}; return m[\"a\"] + m[\"b\"];", "(2)"),

            // Text outside ASCII: the same length, the same upper case, the same character at an index.
            new Construct("unicode_len", "(string s)", "(s)", "return s.Length;", "(\"héllo\")"),
            new Construct("unicode_upper", "(string s)", "(s)", "return s.Upper;", "(\"héllo\")"),
            new Construct("unicode_at", "(string s)", "(s)", "return s.At(1);", "(\"héllo\")"),
            new Construct("cmp_parens", "(int n)", "(n)", "if ((n > 2) == (n < 10)) { return 1; } return 0;", "(5)"),
            new Construct("mutual_cf_interp", "(int n)", "(n)", "return callsBack(n) + 1;", "(3)"),
            new Construct("cf_in_namespace", "(int n)", "(n)", "return nsp.nfunc(n) + nsp.nlocal;", "(1)"),
            new Construct("while_call_and", "(int n)", "(n)", "i = 0; while (helper(i) < n && i < 10) { i++; } return i;", "(10)"),
            new Construct("str_split_two_seps", "(string s)", "(s)", "p = s.Split(\",;\"); return p.Size;", "(\"a,b;c\")"),
            new Construct("tokenize_option", "(string s)", "(s)", "t = Tokenize(s, \",\"); return t[2];", "(\"a,b,c\")"),

            // "x == null": emitted as Variable.SameValue(x, ""), the interpreter's text comparison.
            new Construct("null_cmp", "(int n)", "(n)", "x = null; if (x == null) { return 1; } return 0;", "(0)"),

            // Against the literal null: "==" is a text comparison unless both sides are numbers,
            // and null renders as "" -- so null equals a null local and "", and not 0 or {}.
            // Emitted as Variable.SameValue(v, ""). "null == x" used to be a C# reference test.
            new Construct("nullcmp_left", "(int n)", "(n)", "x = null; if (null == x) { return 1; } return 0;", "(0)"),
            new Construct("nullcmp_ne", "(int n)", "(n)", "x = null; y = 5; if (y != null) { return y + n; } return 0;", "(1)"),
            new Construct("nullcmp_zero", "(int n)", "(n)", "z = 0; if (z == null) { return 1; } return 0;", "(0)"),
            new Construct("nullcmp_empty_str", "(int n)", "(n)", "s = \"\"; if (s == null) { return 1; } return 0;", "(0)"),
            new Construct("nullcmp_coll", "(int n)", "(n)", "a = {}; if (a == null) { return 1; } return 0;", "(0)"),
            new Construct("nullcmp_map", "(int n)", "(n)", "m = {\"a\": null}; if (m[\"a\"] == null) { return 1; } return 0;", "(0)"),
            new Construct("nullcmp_arg_empty", "(string s)", "(s)", "if (s == null) { return 1; } return 0;", "(\"\")"),
            new Construct("nullcmp_ret", "(int n)", "(n)", "x = null; return x == null;", "(0)"),
            new Construct("nullcmp_both_keep", "(int n)", "(n)", "if (null == null) { return 1; } return 0;", "(0)"),
            // Computed arguments to a string member, and assigning such a call. "s.Substring(n - 1,
            // n + 1)" tokenized with "1,n" between the operators, which IsKnownExpression could not
            // place; and a known expression declared "t = s.Substring(n - 1)" a double.
            new Construct("substr_two_expr", "(string s, int n)", "(s, n)", "return s.Substring(n - 1, n + 1);", "(\"hello\", 2)"),
            new Construct("substr_two_mul", "(string s, int n)", "(s, n)", "return s.Substring(n * 0, n * 2);", "(\"hello\", 2)"),
            new Construct("substr_three_ops", "(string s, int n)", "(s, n)", "return s.Substring(n - 2 + 1, n + 1 - 1);", "(\"hello\", 2)"),
            new Construct("strasg_substr_one", "(string s, int n)", "(s, n)", "t = s.Substring(n - 1); return t;", "(\"hello\", 2)"),
            new Construct("strasg_substr_two", "(string s, int n)", "(s, n)", "t = s.Substring(n - 1, n + 1); return t;", "(\"hello\", 2)"),
            new Construct("strasg_at", "(string s, int n)", "(s, n)", "t = s.At(n - 1); return t;", "(\"hello\", 2)"),
            new Construct("strasg_then_concat", "(string s, int n)", "(s, n)", "t = s.Substring(n - 1); t = t + n; return t;", "(\"hello\", 2)"),
            new Construct("strasg_then_type", "(string s, int n)", "(s, n)", "t = s.Substring(n - 1); return t.Type;", "(\"hello\", 2)"),
            // Beside a string literal: the token loop now emits "1,n" as expression text, not a call.
            new Construct("substr_two_expr_cmp", "(string s, int n)", "(s, n)", "if (s.Substring(n - 1, n + 1) == \"ell\") { return 1; } return 0;", "(\"hello\", 2)"),
            new Construct("substrlit_ne", "(string s, int n)", "(s, n)", "if (s.Substring(n - 1, n + 1) != \"x\") { return 1; } return 0;", "(\"hello\", 2)"),
            new Construct("substrlit_concat", "(string s, int n)", "(s, n)", "return s.Substring(n - 1, n + 1) + \"!\";", "(\"hello\", 2)"),
            new Construct("substrlit_asg", "(string s, int n)", "(s, n)", "t = \"<\" + s.Substring(n - 1, n + 1); return t;", "(\"hello\", 2)"),
            new Construct("substrlit_local_args", "(string s, int n)", "(s, n)", "k = 1; if (s.Substring(n - k, n + k) == \"ell\") { return 1; } return 0;", "(\"hello\", 2)"),
            new Construct("substrlit_math_keep", "(int n)", "(n)", "return \"m\" + Math.Max(n - 1, n + 1);", "(2)"),
            new Construct("substrlit_calls_keep", "(int n)", "(n)", "return helper(n - 1) + \",\" + helper(n + 1);", "(2)"),
            // Left interpreted: an instance assigned through two members deep. Only "p.kid = new X()"
            // is built; a longer path has to be set through the interpreter.
            new Construct("deep_member_write", "(int n)", "(n)", "p = new Named(1, \"a\"); p.kid = new Named(2, \"b\"); p.kid.kid = new Named(3, \"c\"); return p.kid.kid.v + n;", "(0)"),

            // A field written through a chain: the owner is the GetProperty chain a read builds, and
            // the write reaches the live nested instance, not a copy.
            new Construct("fwrite_two_deep", "(int n)", "(n)", "p = new Named(1, \"a\"); p.kid = new Named(2, \"b\"); p.kid.v = 9; return p.kid.v + n;", "(0)"),
            new Construct("fwrite_two_deep_str", "(int n)", "(n)", "p = new Named(1, \"a\"); p.kid = new Named(2, \"b\"); p.kid.tag = \"z\"; return p.kid.tag;", "(0)"),
            new Construct("fwrite_via_alias", "(int n)", "(n)", "p = new Named(1, \"a\"); p.kid = new Named(2, \"b\"); p.kid.v = 9; q = p.kid; return q.v + n;", "(0)"),
            new Construct("fwrite_parent_intact", "(int n)", "(n)", "p = new Named(1, \"a\"); p.kid = new Named(2, \"b\"); p.kid.v = 9; return p.v * 100 + p.kid.v;", "(0)"),
            new Construct("fwrite_loop", "(int n)", "(n)", "p = new Named(1, \"a\"); p.kid = new Named(0, \"b\"); for (i = 0; i < n; i++) { p.kid.v = p.kid.v + i; } return p.kid.v;", "(4)"),
            new Construct("fwrite_three_deep", "(int n)", "(n)", "p = new Named(1, \"a\"); p.kid = new Named(2, \"b\"); p.kid.kid = new Named(3, \"c\"); p.kid.kid.tag = \"zz\"; return p.kid.kid.tag;", "(0)"),
            // A method called on what another method returned: each further call wraps the chain
            // so far in Variable.CallMethod, in both the statement and the expression builders.
            new Construct("methchain_read", "(int n)", "(n)", "p = new Named(1, \"a\"); p.kid = new Named(2, \"b\"); p.kid.kid = new Named(3, \"c\"); return p.Kid().Kid().v + n;", "(0)"),
            new Construct("methchain_tag", "(int n)", "(n)", "p = new Named(1, \"a\"); p.kid = new Named(2, \"b\"); p.kid.kid = new Named(3, \"c\"); return p.Kid().Kid().tag;", "(0)"),
            new Construct("methchain_cond", "(int n)", "(n)", "p = new Named(1, \"a\"); p.kid = new Named(2, \"b\"); p.kid.kid = new Named(3, \"c\"); if (p.Kid().Kid().v == 3) { return 1; } return 0;", "(0)"),
            new Construct("methchain_assign", "(int n)", "(n)", "p = new Named(1, \"a\"); p.kid = new Named(2, \"b\"); p.kid.kid = new Named(3, \"c\"); q = p.Kid().Kid(); return q.v;", "(0)"),
            new Construct("methchain_while", "(int n)", "(n)", "p = new Named(1, \"a\"); p.kid = new Named(2, \"b\"); p.kid.kid = new Named(3, \"c\"); t = 0; while (t < p.Kid().Kid().v) { t++; } return t;", "(0)"),
            new Construct("methchain_field_mid", "(int n)", "(n)", "p = new Named(1, \"a\"); p.kid = new Named(2, \"b\"); p.kid.kid = new Named(3, \"c\"); return p.Kid().kid.v + n;", "(0)"),
            // Left interpreted: a header with every section empty.
            new Construct("for_empty_sections", "(int n)", "(n)", "i = 0; for (;;) { i++; if (i >= n) { break; } } return i;", "(4)"),
            // A truth value is the number 1 or 0 in CSCS, so it takes part in arithmetic.
            new Construct("bool_arith_add", "(int n)", "(n)", "b = n > 1; return b + 1;", "(2)"),
            new Construct("bool_arith_mul", "(int n)", "(n)", "b = n > 1; return b * 2;", "(2)"),
            new Construct("bool_arith_neg", "(int n)", "(n)", "b = n > 1; return -b;", "(2)"),
            new Construct("bool_arith_chain", "(int n)", "(n)", "b = n > 1; c = n > 0; return b + c * 2;", "(2)"),
            new Construct("bool_arith_false", "(int n)", "(n)", "b = n < 1; return b + 5;", "(2)"),
            new Construct("bool_arith_index", "(int n)", "(n)", "b = n > 1; a = {10, 20}; return a[b + 0];", "(2)"),
            // ".Type" inside a statement that also holds a string literal: that statement
            // is not a "known expression", so it reaches C# through ProcessToken.
            new Construct("type_concat_after", "(int n)", "(n)", "x = n + 1; return \"t=\" + x.Type;", "(1)"),
            new Construct("type_concat_before", "(int n)", "(n)", "x = n + 1; return x.Type + \"!\";", "(1)"),
            new Construct("type_concat_str", "(string s)", "(s)", "t = s + \"!\"; return \"t=\" + t.Type;", "(\"a\")"),
            new Construct("type_concat_two", "(int n)", "(n)", "x = n + 1; s = \"a\"; return x.Type + \"/\" + s.Type;", "(1)"),
            // A truth value beside a compound operator or compared with a number: CSCS has
            // no boolean type, so both are ordinary arithmetic there.
            new Construct("bool_compound_add", "(int n)", "(n)", "t = 0; b = n > 1; t += b; return t;", "(2)"),
            new Construct("bool_compound_sub", "(int n)", "(n)", "t = 5; b = n > 1; t -= b; return t;", "(2)"),
            new Construct("bool_compound_loop", "(int n)", "(n)", "t = 0; i = 0; while (i < n) { b = i > 0; t += b; i++; } return t;", "(3)"),
            new Construct("bool_eq_one", "(int n)", "(n)", "b = n > 1; if (b == 1) { return 1; } return 0;", "(2)"),
            new Construct("bool_eq_zero", "(int n)", "(n)", "b = n > 1; if (b == 0) { return 1; } return 0;", "(2)"),
            new Construct("bool_ne_zero", "(int n)", "(n)", "b = n > 1; if (b != 0) { return 1; } return 0;", "(2)"),

            // Left interpreted: a ternary inside a literal. The statement tokenizer treats "?" and ":"
            // as separators, so the literal is already in pieces before the builder sees it.
            new Construct("arr_lit_ternary", "(int n)", "(n)", "a = {n > 2 ? 10 : 20, 5}; return a[0] + a[1];", "(3)"),

            // A ternary inside a literal. Its ":" is not a map key separator (IsMapEntry);
            // two of the three literal builders tested a bare split on ":" and built
            // "{n > 2 ? 10 : 20}" as the map entry "n > 2 ? 10" -> 20.
            new Construct("litt_second", "(int n)", "(n)", "a = {5, n > 2 ? 10 : 20}; return a[0] + a[1];", "(3)"),
            new Construct("litt_only", "(int n)", "(n)", "a = {n > 2 ? 10 : 20}; return a[0] + n;", "(3)"),
            new Construct("litt_str", "(int n)", "(n)", "a = {n > 2 ? \"a\" : \"b\", \"c\"}; return a[0] + a[1];", "(3)"),
            new Construct("litt_nested", "(int n)", "(n)", "a = {{n > 2 ? 1 : 2}, {3}}; return a[0][0] + a[1][0];", "(3)"),
            new Construct("litt_reassign", "(int n)", "(n)", "a = {}; a = {n > 2 ? 10 : 20, 5}; return a[0] + a[1];", "(3)"),
            // Left interpreted: NameExists asks the interpreter what it holds.
            new Construct("name_exists_fn", "(int n)", "(n)", "x = 5; if (NameExists(\"x\")) { return 1; } return 0;", "(0)"),

            // Arguments given by name, in any order, and mixed with defaults. The compiled path filled
            // the defaults itself before the binder reordered the named ones, so "f(b = 2)" on
            // "f(int a = 5, int b = 7)" gave a the default of b -- 72 where the interpreter has 52.
            // RegisterArguments, which an interpreted function uses, does the binding now.
            new Construct("named_both", "(int a, int b)", "(a, b)", "return a * 10 + b;", "(a = 1, b = 2)"),
            new Construct("named_reversed", "(int a, int b)", "(a, b)", "return a * 10 + b;", "(b = 2, a = 1)"),
            new Construct("named_with_default", "(int a = 5, int b = 7)", "(a = 5, b = 7)", "return a * 10 + b;", "(b = 2)"),
            new Construct("named_one_of_two", "(int a, int b = 7)", "(a, b = 7)", "return a * 10 + b;", "(a = 3)"),

            // JSON parsed into a collection, and a value marshalled and read back.
            new Construct("json_map", "(int n)", "(n)", "j = \"{\\\"a\\\": 1, \\\"b\\\": 2}\"; v = GetVariableFromJSON(j); return v[\"a\"] + v[\"b\"] + n;", "(0)"),
            new Construct("json_array", "(int n)", "(n)", "j = \"[1, 2, 3]\"; v = GetVariableFromJSON(j); return v[1] + n;", "(0)"),
            new Construct("marshal_num", "(int n)", "(n)", "m = Marshal(n); u = Unmarshal(m); return u + 1;", "(5)"),
            new Construct("marshal_str", "(string s)", "(s)", "m = Marshal(s); u = Unmarshal(m); return u + \"!\";", "(\"ab\")"),

            // A member written in any case. CSCS names are case-insensitive and the member went out
            // exactly as the script spelt it, so "m.keys" and "a.sort()" did not compile while "m.Keys"
            // did. The C# spelling is used for anything Variable provides.
            new Construct("lower_size_arr", "(int n)", "(n)", "a = {1, 2, 3}; return a.size + n;", "(0)"),
            new Construct("lower_size_map", "(int n)", "(n)", "m = {\"a\": 1, \"b\": 2}; return m.size + n;", "(0)"),
            new Construct("lower_length_str", "(string s)", "(s)", "return s.length;", "(\"abcd\")"),
            new Construct("lower_upper_str", "(string s)", "(s)", "return s.upper;", "(\"ab\")"),
            new Construct("lower_contains_str", "(string s)", "(s)", "if (s.contains(\"b\")) { return 1; } return 0;", "(\"abc\")"),
            new Construct("lower_add_arr", "(int n)", "(n)", "a = {1}; a.add(5); return a.Size * 10 + a[1] + n;", "(0)"),
            new Construct("lower_keys_map", "(int n)", "(n)", "m = {\"a\": 1, \"b\": 2}; k = m.keys; return k.Size + n;", "(0)"),
            new Construct("lower_sort_arr", "(int n)", "(n)", "a = {3, 1, 2}; a.sort(); return a[0] + n;", "(0)"),
            new Construct("lower_trim_str", "(string s)", "(s)", "return s.trim() + \"!\";", "(\" ab \")"),
            new Construct("lower_substring", "(string s)", "(s)", "return s.substring(1, 2);", "(\"hello\")"),
            new Construct("lower_indexof", "(string s)", "(s)", "return s.indexOf(\"l\");", "(\"hello\")"),
            new Construct("lower_at_str", "(string s)", "(s)", "return s.at(1);", "(\"abc\")"),
            new Construct("mixed_case_size", "(int n)", "(n)", "a = {1, 2}; return a.SIZE + n;", "(0)"),
            new Construct("mixed_case_upper", "(string s)", "(s)", "return s.UPPER;", "(\"ab\")"),
            new Construct("map_keys_sort", "(int n)", "(n)", "m = {\"b\": 1, \"a\": 2}; k = m.Keys; k.Sort(); return k[0];", "(0)"),
            new Construct("obj_chain_tag", "(int n)", "(n)", "p = new Named(1, \"x\"); p.kid = new Named(2, \"y\"); return p.Kid().tag;", "(0)"),
            new Construct("type_of_map", "(int n)", "(n)", "m = {\"a\": 1}; return m.Type;", "(0)"),
            new Construct("str_trim_fn", "(string s)", "(s)", "return StrTrim(s) + \"!\";", "(\" ab \")"),
            new Construct("math_abs_max", "(int n)", "(n)", "return Math.Abs(n) + Math.Max(n, 0);", "(-5)"),

            // Size is 0 for a string in the interpreter, so it must not be mapped to Length.
            new Construct("size_of_string", "(string s)", "(s)", "return s.Size + s.Length;", "(\"abcd\")"),

            // A member on the Variable a call returned. The local holding it is a Variable local
            // now: the name kept the script's case there, and a string member's mapping was
            // applied to it -- ".ContainsCscs" on a Variable, which has Contains.
            new Construct("regex_size", "(string s)", "(s)", "r = Regex(\"[0-9]+\", s); return r.size;", "(\"a12b34\")"),
            new Construct("regex_contains", "(string s)", "(s)", "r = Regex(\"[0-9]+\", s); if (r.contains(\"12\")) { return 1; } return 0;", "(\"a12b34\")"),
            // Left interpreted: "type" in lower case on the expression path, which emits the
            // member as the script spelt it. Any capitalisation of "Type" compiles, and the
            // other members -- size, keys, sort -- are spelt canonically wherever they appear.
            new Construct("lower_type", "(int n)", "(n)", "a = {1}; return a.type;", "(0)"),

            // Left interpreted: Type of a numeric local, which is a C# double and has none.
            new Construct("type_of_num", "(int n)", "(n)", "x = 5; return x.Type;", "(0)"),

            // Left interpreted: the interpreter answers 0 for one collection plus another.
            new Construct("arr_plus_arr", "(int n)", "(n)", "a = {1, 2}; b2 = {3}; c = a + b2; return c.Size + n;", "(0)"),

            // Left interpreted: StrEqual and StrContains have no C# counterpart taking these arguments.
            new Construct("str_equal_fn", "(string s)", "(s)", "if (StrEqual(s, \"AB\")) { return 1; } return 0;", "(\"ab\")"),
            new Construct("str_contains_nocase", "(string s)", "(s)", "if (StrContains(s, \"B\", \"no_case\")) { return 1; } return 0;", "(\"ab\")"),

            // An enum declared in the function: built as EnumFunction builds it; only declared
            // members compile, so Local.Type (NONE interpreted) stays with the interpreter.
            new Construct("enum_in_cf_local", "(int n)", "(n)", "var Local = Enum {X, Y}; return Local.Y + n;", "(1)"),

            new Construct("lenum_plain", "(int n)", "(n)", "Local = Enum {X, Y}; return Local.Y + n;", "(1)"),
            new Construct("lenum_first_last", "(int n)", "(n)", "var Local = Enum {X, Y, Z}; return Local.X * 10 + Local.Z;", "(0)"),
            new Construct("lenum_cmp", "(int n)", "(n)", "var Local = Enum {X, Y}; if (Local.Y == 1) { return 1; } return 0;", "(0)"),
            new Construct("lenum_loop", "(int n)", "(n)", "var Local = Enum {X, Y, Z}; t = 0; for (i = 0; i < n; i++) { t += Local.Z; } return t;", "(3)"),
            new Construct("lenum_concat", "(int n)", "(n)", "var Local = Enum {X, Y}; return \"v=\" + Local.Y;", "(0)"),
            new Construct("lenum_two", "(int n)", "(n)", "var A = Enum {P, Q}; var B = Enum {R, S, T}; return A.Q * 10 + B.T;", "(0)"),
            new Construct("lenum_neg", "(int n)", "(n)", "var Local = Enum {X, Y}; return -Local.Y;", "(0)"),
            new Construct("lenum_type_keep", "(int n)", "(n)", "var Local = Enum {X, Y}; return Local.Type;", "(0)"),
            // An exception crossing between compiled and interpreted code, each way, and an instance
            // handed to an interpreted function.
            new Construct("catch_interp_throw", "(int n)", "(n)", "try { throwsAlways(n); } catch (e) { return \"caught:\" + e; } return \"none\";", "(3)"),
            new Construct("instance_to_interp", "(int n)", "(n)", "p = new Point(n, n); return sumPoint(p);", "(3)"),

            // A return from inside try, with no finally in the function.
            new Construct("return_in_try", "(int n)", "(n)", "try { return n * 2; } catch (e) { return -1; } return 0;", "(4)"),
            new Construct("return_in_finally", "(int n)", "(n)", "try { r = 1; } catch (e) { r = 2; } finally { r = 3; } return r;", "(0)"),
            new Construct("return_in_catch_finally", "(int n)", "(n)", "r = 0; try { throw \"x\"; } catch (e) { return 7; } finally { r = 99; } return r;", "(0)"),
            new Construct("throw_in_catch", "(int n)", "(n)", "try { try { throw \"inner\"; } catch (e) { throw \"outer:\" + e; } } catch (e2) { return e2; } return \"none\";", "(0)"),

            // An argument declared "variable" is a Variable as much as a local holding one, so a field
            // on it reads through the same property lookup. Only the method form worked before.
            new Construct("var_param_field", "(variable v)", "(v)", "return v.x + v.y;", "(new Point(3, 4))"),
            new Construct("var_param_field_str", "(variable v)", "(v)", "return v.tag + \"!\";", "(new Named(1, \"q\"))"),
            new Construct("var_param_chain", "(variable v)", "(v)", "return v.tag.Upper;", "(new Named(1, \"q\"))"),
            new Construct("var_param_method", "(variable v)", "(v)", "return v.Sum();", "(new Point(3, 4))"),
            new Construct("instance_compare", "(int n)", "(n)", "a = new Point(1, 5); b2 = new Point(2, 3); if (a.Sum() == b2.Sum()) { return 1; } return 0;", "(0)"),
            new Construct("instance_iter_field", "(int n)", "(n)", "a = {}; a.Add(new Point(3, 1)); a.Add(new Point(1, 1)); t = 0; for (q in a) { t = t * 10 + q.x; } return t + n;", "(0)"),
            new Construct("tern_collections", "(int n)", "(n)", "a = n > 2 ? {1, 2} : {3}; return a.Size + n;", "(5)"),

            // Iterating a map itself walks its values, not its keys -- "m.Keys" is what gives
            // those. Both sides agree on it, which is what this pins.
            new Construct("for_over_map", "(int n)", "(n)", "m = {\"a\": 1, \"b\": 2}; t = \"\"; for (k in m) { t += k; } return t;", "(0)"),
            new Construct("deep_nesting", "(int n)", "(n)", "t = 0; for (i = 0; i < n; i++) { if (i % 2 == 0) { for (j = 0; j < n; j++) { if (j > i) { while (t < 100) { t += 7; if (t > 20) { break; } } } } } } return t;", "(4)"),
            new Construct("bit_not_negative", "(int n)", "(n)", "return ~n;", "(-5)"),
            new Construct("bitwise_neg_and", "(int n)", "(n)", "return (n & 3) + (n ^ 1);", "(-5)"),

            // How a number reaches text at the edges of the range.
            new Construct("tiny_number", "(double n)", "(n)", "return \"v=\" + n / 10000000;", "(1)"),
            new Construct("huge_number", "(double n)", "(n)", "return \"v=\" + n * n;", "(1e10)"),
            new Construct("assign_in_tern", "(int n)", "(n)", "x = 0; y = n > 2 ? (x = 5) : (x = 7); return x * 10 + y;", "(5)"),
            new Construct("neg_index_read", "(int n)", "(n)", "a = {1, 2, 3}; return a[a.Size - n];", "(1)"),

            // A local named after a function the script also defines.
            new Construct("local_shadows_fn", "(int n)", "(n)", "helper = 5; return helper + n;", "(2)"),
            new Construct("many_locals", "(int n)", "(n)", "a1 = n; a2 = a1 + 1; a3 = a2 + 1; a4 = a3 + 1; a5 = a4 + 1; a6 = a5 + 1; a7 = a6 + 1; a8 = a7 + 1; return a8;", "(1)"),

            // Left interpreted: CSCS does not leave the function at a return inside try when there is a
            // finally -- it runs the finally and carries on with the statement after it, answering 99
            // where C#'s return leaves with 8. Nothing in C# expresses that, so the function falls back.
            new Construct("return_in_try_finally", "(int n)", "(n)", "r = 0; try { return n * 2; } catch (e) { return -1; } finally { r = 99; } return r;", "(4)"),

            // Left interpreted: C# cannot chain comparisons, the second one being bool against int.
            new Construct("chained_compare", "(int n)", "(n)", "if (1 < n < 10) { return 1; } return 0;", "(5)"),

            // Left interpreted: text times a number inside a compound assignment.
            new Construct("str_mult_loop", "(int n)", "(n)", "s = \"\"; for (i = 0; i < n; i++) { s += \"ab\" * 2; } return s.Length;", "(3)"),

            // Reference semantics. A collection assigned to another name is the same collection, and so
            // is one reached through a subscript, a map key, a foreach variable or an argument -- a
            // change through either name shows through both. Scalars and text copy instead. Compiled
            // code holds the same Variables the interpreter does, so this has to stay true of it.
            new Construct("alias_assign", "(int n)", "(n)", "a = {1, 2}; b2 = a; b2.Add(3); return a.Size * 10 + b2.Size + n;", "(0)"),
            new Construct("alias_map_elem", "(int n)", "(n)", "m = {\"a\": {1, 2}}; v = m[\"a\"]; v.Add(3); return m[\"a\"].Size + n;", "(0)"),
            new Construct("alias_foreach", "(int n)", "(n)", "g = {}; g.Add({1}); g.Add({2}); for (row in g) { row.Add(9); } return g[0].Size * 10 + g[1].Size + n;", "(0)"),
            new Construct("alias_param", "(list<int> a)", "(a)", "b2 = a; b2.Add(3); return a.Size;", "({1, 2})"),
            new Construct("alias_nested_write", "(int n)", "(n)", "g = {}; g.Add({1, 2}); r2 = g[0]; r2[0] = 99; return g[0][0] + n;", "(0)"),
            new Construct("alias_instance", "(int n)", "(n)", "p = new Named(1, \"a\"); q = p; q.v = 9; return p.v + n;", "(0)"),
            new Construct("alias_instance_in_arr", "(int n)", "(n)", "a = {}; p = new Named(1, \"a\"); a.Add(p); p.v = 7; return a[0].v + n;", "(0)"),
            new Construct("copy_scalar", "(int n)", "(n)", "x = n; y = x; y = y + 1; return x * 10 + y;", "(5)"),
            new Construct("copy_string", "(string s)", "(s)", "t = s; t += \"!\"; return s + t;", "(\"a\")"),

            // break, continue and return inside a try or its catch, and a throw from inside a loop
            // caught outside it.
            new Construct("break_in_try", "(int n)", "(n)", "t = 0; for (i = 0; i < n; i++) { try { if (i == 2) { break; } t += i; } catch (e) { t = -1; } } return t;", "(5)"),
            new Construct("continue_in_try", "(int n)", "(n)", "t = 0; for (i = 0; i < n; i++) { try { if (i % 2 == 0) { continue; } t += i; } catch (e) { t = -1; } } return t;", "(5)"),
            new Construct("continue_in_catch", "(int n)", "(n)", "t = 0; for (i = 0; i < n; i++) { try { if (i % 2 == 0) { throw \"x\"; } t += i; } catch (e) { continue; } } return t;", "(5)"),
            new Construct("break_in_catch", "(int n)", "(n)", "t = 0; for (i = 0; i < n; i++) { try { if (i == 2) { throw \"x\"; } t += i; } catch (e) { break; } } return t;", "(5)"),
            new Construct("return_in_foreach_try", "(int n)", "(n)", "a = {1, 5, 3}; for (x in a) { try { if (x > n) { return x; } } catch (e) { return -1; } } return 0;", "(2)"),
            new Construct("throw_in_loop_caught_outside", "(int n)", "(n)", "t = 0; try { for (i = 0; i < n; i++) { if (i == 2) { throw \"stop\"; } t += i; } } catch (e) { t += 100; } return t;", "(5)"),

            // A method reading its own field without naming it.
            new Construct("this_field", "(int n)", "(n)", "p = new Counter(5); return p.Value() + n;", "(1)"),
            new Construct("big_literal", "(int n)", "(n)", "a = {1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20}; t = 0; for (x in a) { t += x; } return t + n;", "(0)"),

            // Three hundred levels deep, each with its own arguments and locals.
            new Construct("deep_recursion", "(int n)", "(n)", "if (n <= 0) { return 0; } return 1 + SELF(n - 1);", "(300)"),
            new Construct("print_in_cf", "(int n)", "(n)", "Print(\"in cf \", n); return n * 2;", "(3)"),
        };

        /// <summary>
        /// Constructs that currently precompile correctly. Anything listed here must keep
        /// working. When a gap below is closed, move its name up into this list.
        /// </summary>
        static readonly HashSet<string> Supported = new HashSet<string>
        {
            "arith", "compound", "if_else", "while", "for", "break", "continue", "nested_loop",
            "modulo", "string_concat", "string_len", "string_upper", "string_sub",
            "string_idx", "string_repl", "math_calls", "multi_return", "string_num",
            "increment", "nested_call", "logical", "bool_var", "array_add", "int_div", "else_if", "ternary", "not", "recursion", "call_cscs_fn", "call_expr_arg", "call_twice", "call_nested", "add_str_str", "add_str_num", "add_num_num", "add_lit_call", "add_members", "coll_ends", "coll_ends_str", "str_gt", "str_gt_false", "str_lit_left", "str_two_args", "str_range", "str_mixed_cmp", "str_while_cmp", "math_ceil", "map_by_key", "map_literal_rhs", "bit_compound", "not_bool_var", "elem_str_add", "map_arith", "map_elem_ops", "eq_str_num", "eq_str_pad", "eq_num_str", "ne_str_num", "nested_try", "catch_rethrow", "try_in_loop", "call_in_cond", "call_cond_and", "map_of_array", "strict_eq", "strict_eq_case", "strict_ne", "global_read", "global_mutate", "for_from_arg", "global_cmp", "global_index", "global_string", "map_in_map", "mixed_add", "truthy_elem", "truthy_key", "tern_literal", "tern_lit_else", "tern_lit_skip", "tern_map_skip", "elem_to_local", "elem_to_str", "elem_in_loop", "elem_then_math", "elem_member", "elem_upper", "elem_str_cmp", "acc_str", "acc_mixed", "acc_num", "acc_call", "cond_strlen", "while_strlen", "neg_elem", "neg_elem_str", "cmp_elem", "cmp_elstr2", "cmp_elle", "cmp_elnum", "cmp_elmath", "cmp_elcall", "cmp_elwhile", "tern_cmp", "tern_cmp_loc", "tern_cmp_nest", "tern_cmp_num", "asg_cmp", "asg_cmp_and", "conv_map_str", "conv_map_int", "conv_map_dbl", "loc_str_cmp", "loc_str_lit", "loc_num_cmp", "elem_sum", "elem_swap", "elem_ternary", "foreach_in", "call_in_while", "do_while", "call_idx", "call_idx_str", "trim_cmp", "trim_ret", "trim_len", "trim_asg", "fe_shadow", "idx_call", "idx_convert", "idx_call_asg", "idx_math", "fe_split", "fe_call", "fe_nested", "fe_elem_str", "fe_elem_num", "keys_add", "keys_remove", "split_rows", "tally_str", "acc_convert", "acc_int", "keys_sort", "keys_reverse", "kw_local", "kw_coll", "kw_foreach", "kw_string", "kw_catch", "kw_filter", "mul_str", "mul_str_num", "mul_str_trim", "mul_str_asg", "mul_num", "mix_mul", "mix_lt", "mix_gt_lit", "mix_zero_str", "bool_text", "truthy_str", "truthy_notstr", "truthy_notnum", "cmp_asg_str", "cmp_elem_str", "cmp_elem_sub", "step_elem_str", "plus_elem_str", "size_scalar", "size_coll", "for_fn", "for_fn_down", "lit_fn", "maplit_fn", "seed_fn", "var_step", "var_step_dn", "var_step_str", "gread_bare", "gread_key", "gread_2d", "gelem_fn", "gelem_fn_cmp", "add_fn", "elem_asg_fn", "elem_cmp_fn", "map_asg_fn", "elem_asg_2d", "elem_asg_str", "not_var", "not_var_par", "not_elem", "var_in_while", "chain_assign", "chain_three", "chain_str", "chain_expr", "eq_and", "eq_and_num", "eq_or", "eq_ternary", "eq_assigned", "eq_two_elems", "ne_two_elems", "eq_elem_lit", "eq_lit_elem", "eq_loop_var", "eq_elem_num", "eq_num_text", "eq_text_zero", "gelem_plus", "gelem_times", "gelem_step", "gelem_mapadd", "gelem_2dadd", "gelem_ixadd", "foreach_chars", "foreach_cat", "foreach_upper", "gelem_arr", "gelem_map", "gelem_newkey", "gelem_2d", "gelem_idx", "gelem_loop", "gelem_shadow", "bit_elem", "bit_elem_or", "bit_elem_xor", "bit_elem_trun", "cmp_member", "cmp_member2", "cmp_sub", "cmp_indexof", "chain_member", "global_accum", "global_step", "foreach_map", "class_newarg", "elem_predec", "fallback_mix", "class_two_add", "class_inarr", "class_nested", "class_fromm", "class_methfld", "class_mfstr", "class_iter", "switch_in_loop", "sw_break_loop", "sw_break_whl", "sw_ret_loop", "sw_no_break", "sw_deflt_only", "switch_after", "switch_deflt", "do_once", "do_break", "do_nested", "while_cont", "while_break", "foreach_str", "nested_assign", "power_two", "power_lits", "power_assign", "power_mixed", "power_tail", "power_call", "power_elem", "power_paren", "power_pboth", "power_assoc", "power_chain", "strict_eq_num", "strict_ne_num", "class_field", "class_two", "class_str", "class_method", "class_marg", "class_mstr", "class_write", "class_fldmem", "class_fldstr", "switch_fld", "switch_fnum", "switch_fdef", "math_nested2", "mix_grid", "string_split", "array_lit", "array_size", "map_lit", "map_set", "map_assign", "array_assign", "neg_index", "idx_expr", "idx_assign_expr", "switch", "switch_fall", "string_eq", "string_ne", "arg_reassign", "local_upper", "array_add_expr", "array_iterate", "elem_compound", "convert_int", "convert_half", "convert_str", "bitwise", "lit_arg", "lit_nested", "chain_idx", "bit_or_xor", "paren_arg", "cond_member", "cond_member2", "str_case", "str_nocase", "str_startcase", "str_equals", "str_eq_nocase", "str_sub_clamp", "coll_contains", "map_keys", "idx2_assign", "idx2_compound", "bit_not", "try_catch", "catch_value",
            "alg_linsearch", "alg_binsearch", "alg_bubble", "alg_selsort", "alg_gcd", "alg_primes", "alg_fizzbuzz", "alg_transpose", "alg_dedup", "rec_fib", "rec_fib_split", "rec_twice", "at_palin", "at_reverse", "at_vowels", "at_caesar", "at_past_end", "at_frac_idx", "at_tally", "chain_at_up", "chain_sub_up", "chain_sub_nc", "chain_trim_up", "for_init_mem", "esc_tab", "esc_cr", "esc_tab_cmp", "neg_group", "neg_group_dbl", "neg_group_call", "neg_group_elem", "ret_paren", "ret_paren_str", "ret_paren_elem", "ret_paren_math", "ret_paren_call", "grp_call_add", "grp_call_str", "grp_call_cond", "eq_elem_arg", "eq_elem_sarg", "eq_elem_ctr", "eq_fe_arg",
            "sc_guard_and", "sc_guard_or", "sc_guard_tern", "sc_guard_while", "call_while_and", "call_for_cond", "callres_str_mul", "callres_str_tern", "callres_str_eq", "callres_str_lt", "callres_num_eq", "callres_accum", "callres_asg_add", "callres_asg_use", "callres_asg_loop", "conv_of_call", "conv_sqrt", "conv_in_cond", "conv_in_while", "conv_trailing", "cls_new_expr", "map_nested_lit", "map_nested_map", "elem_method_loc", "elem_sub_at", "fe_title", "fe_trim", "loc_member_cond", "arg_member_cond", "mul_precedence", "wf_most", "arr_merge2", "matrix_mul2", "grp_call_asg", "grp_call_two",
            "carg_sum", "carg_fe_max", "carg_contains", "carg_set", "carg_return", "carg_str_member", "carg_str_join", "carg_dbl_avg", "carg_map_keys", "carg_map_add", "carg_map_str", "carg_recursive", "varg_index", "int_ovf_cube", "int_ovf_mul", "int_ovf_add", "int_ovf_text", "int_arg_sub_expr", "int_arg_collatz", "int_arg_grow", "int_arg_step", "scope_anagram", "scope_branches", "scope_str_later", "scope_coll_later", "scope_bool_later", "scope_inst_later", "str_chain_prop", "int_arg_halve",
            "default_arg", "default_arg_given", "default_two", "default_one_given", "default_str", "zero_arg", "comment_block", "comment_in_expr", "inherit_base_method", "inherit_own_method", "inherit_fields", "ns_call", "ns_var", "date_fmt", "date_fmt_time", "date_fmt_add", "num_fmt", "conv_mapkey", "conv_slash_concat", "idx_key_slash", "fn_size", "fn_substring", "fn_sort_member", "fn_rev_member", "fn_tokenize", "fn_strbetween", "fn_type_member", "fn_typeof", "fn_deepcopy", "fn_getkeys", "fn_strupper", "fn_findindex", "fn_addunique", "fn_remove_item", "fn_math_log", "fn_math_trig", "fn_math_atan2", "three_d", "map_elem_inc", "do_continue", "str_cmp_le", "local_arr_to_fn", "local_map_to_fn", "local_arr_mutated_by_fn",
            "try_finally", "try_finally_noexc", "div_zero", "mod_zero", "float_add", "float_eq", "neg_zero", "big_precision", "str_div_concat", "map_nested_write", "map_arr_append", "map_new_nested", "map_arr_of_maps", "new_defaults", "new_defaults_partial", "tostring_concat", "tostring_call", "for_no_init", "for_no_step", "arr_lit_expr", "map_lit_expr", "unicode_len", "unicode_upper", "unicode_at", "cmp_parens", "mutual_cf_interp", "cf_in_namespace", "while_call_and", "str_split_two_seps", "tokenize_option",
            "named_both", "named_reversed", "named_with_default", "named_one_of_two", "json_map", "json_array", "marshal_num", "marshal_str", "lower_size_arr", "lower_size_map", "lower_length_str", "lower_upper_str", "lower_contains_str", "lower_add_arr", "lower_keys_map", "lower_sort_arr", "lower_trim_str", "lower_substring", "lower_indexof", "lower_at_str", "mixed_case_size", "mixed_case_upper", "map_keys_sort", "obj_chain_tag", "type_of_map", "str_trim_fn", "math_abs_max", "size_of_string",
            "regex_size", "regex_contains",
            "catch_interp_throw", "instance_to_interp", "return_in_try", "return_in_finally", "return_in_catch_finally", "throw_in_catch", "var_param_field", "var_param_field_str", "var_param_chain", "var_param_method", "instance_compare", "instance_iter_field", "tern_collections", "for_over_map", "deep_nesting", "bit_not_negative", "bitwise_neg_and", "tiny_number", "huge_number", "assign_in_tern", "neg_index_read", "local_shadows_fn", "many_locals",
            "alias_assign", "alias_map_elem", "alias_foreach", "alias_param", "alias_nested_write", "alias_instance", "alias_instance_in_arr", "copy_scalar", "copy_string", "break_in_try", "continue_in_try", "continue_in_catch", "break_in_catch", "return_in_foreach_try", "throw_in_loop_caught_outside", "this_field", "big_literal", "deep_recursion", "print_in_cf",
            "for_empty_sections", "lower_type", "type_of_num",
            "bool_arith_add", "bool_arith_mul", "bool_arith_neg", "bool_arith_chain",
            "bool_arith_false", "bool_arith_index",
            "type_concat_after", "type_concat_before", "type_concat_str", "type_concat_two",
            "int_arg_round_expr",
            "str_equal_fn", "str_contains_nocase", "fn_contains_arr", "name_exists_fn",
            "enum_read", "arr_plus_arr",
            "glob_eq_lit_left", "glob_ne_lit_left", "glob_eq_glob", "glob_eq_arg",
            "glob_eq_local", "glob_eq_2paren", "glob_eq_while", "glob_eq_return",
            "call_math_arg", "math_arg_glob", "math_arg_two_calls", "math_arg_min",
            "math_arg_abs", "math_arg_round", "math_arg_pow", "math_arg_sqrt",
            "math_arg_expr", "math_arg_mapglob", "math_arg_assign", "math_arg_bool",
            "scope_mixed_types", "cross_mixed_num_str", "cross_mixed_else", "cross_mixed_elif",
            "cross_mixed_coll", "cross_mixed_concat", "cross_mixed_cmp",
            "cross_predeclared", "loc_mixed_cmp", "redecl_str_then_num", "redecl_top_num_str",
            "redecl_top_str_num", "redecl_loop", "redecl_coll", "redecl_ternary_keep",
            "arr_lit_ternary", "litt_second", "litt_only", "litt_str", "litt_nested", "litt_reassign",
            "glob_eq_str_lit", "glob_str_right", "glob_str_ne", "glob_str_case", "glob_str_arg",
            "glob_str_ret", "glob_str_tern", "glob_num_text",
            "elif_str_arg", "elif_str_lt", "elif_str_else", "elif_bool", "elif_elem_str",
            "elif_glob_str", "else_if_str", "elif_nested_str",
            "assign_in_cond", "cond_asg_if", "cond_asg_ne", "cond_asg_after", "cond_asg_pair",
            "cond_asg_elif", "cond_asg_str",
            "strict_int_eq", "strict_int_ne", "strict_int_ret", "strict_num_local", "strict_two_ints",
            "deep_member_write", "fwrite_two_deep", "fwrite_two_deep_str", "fwrite_via_alias",
            "fwrite_parent_intact", "fwrite_loop", "fwrite_three_deep",
            "methchain_read", "methchain_tag", "methchain_cond", "methchain_assign", "methchain_while",
            "methchain_field_mid",
            "null_cmp", "nullcmp_left", "nullcmp_ne", "nullcmp_zero", "nullcmp_empty_str", "nullcmp_coll",
            "nullcmp_map", "nullcmp_arg_empty", "nullcmp_ret",
            "substr_two_expr", "substr_two_mul", "substr_three_ops", "strasg_substr_one", "strasg_substr_two",
            "strasg_at", "strasg_then_concat", "strasg_then_type",
            "substr_two_expr_cmp", "substrlit_ne", "substrlit_concat", "substrlit_asg", "substrlit_local_args",
            "substrlit_math_keep", "substrlit_calls_keep",
            "enum_in_cf_local", "lenum_plain", "lenum_first_last", "lenum_cmp", "lenum_loop", "lenum_concat",
            "lenum_two", "lenum_neg",
            "cond_bool_if", "cond_bool_false", "cond_bool_read", "cond_bool_while", "cond_bool_elif", "cond_bool_and",
            "numcond_asg", "numcond_asg_zero", "numcond_while", "numcond_local", "numcond_local_zero",
            "grpasg_nested", "grpasg_expr", "grpasg_single", "numcond_alias_keep",
            "numlogic_not", "numlogic_not_true", "numlogic_and", "numlogic_or", "numlogic_two", "numlogic_while",
            "numlogic_arg_not", "numlogic_bool_mix",
            "numlogic_elem_keep", "truthy_elem_and", "truthy_elem_or", "truthy_varlocal_and",
            "truthy_str_and", "truthy_elem_not_and",
            "strcond_alone", "strcond_num", "strcond_empty", "strcond_local", "strcond_asg",
            "strcond_not", "strcond_not_str", "strcond_not_join",
            "retasg_plus", "retasg_alone", "retasg_mult", "retasg_str", "retasg_double",
            "retasg_nested", "retasg_pre", "retasg_used_later",
            "retasg_block", "retgrp_block", "retgrp_while", "retgrp_str",
            "len_cond", "len_cond_empty", "len_not", "len_and", "len_while",
            "size_cond", "size_empty", "size_not", "size_map", "size_call_result", "size_of_var",
            "mem_cond", "mem_cond_zero", "mem_not", "mem_and", "mem_str_false", "mem_num", "mem_while",
            "upper_call", "upper_call_cmp", "upper_call_chain", "lower_call",
            "elem_method", "elem_method_first", "elem_method_arg", "elem_method_argvar",
            "elem_method_expr", "elem_method_loop",
            "mapasg_method", "mapnew_direct", "mapnew_varkey", "mapnew_mixed", "mapnew_first",
            "elem_field", "elem_field_cond", "elem_str_field", "elem_size", "elem_type", "elem_first",
            "elem_map_field", "elem_upper_call", "elem_size_call", "elem_first_call", "elem_lower_call",
            "elem_sort_still", "elem_replace_still",
            "chain_elem", "chain_elem_value", "chain_map", "chain_triple", "chain_idx_param",
            "chain_nested", "chain_map_varkey", "chain_four", "chain_global",
            "chain_member", "chain_member_value", "chain_member_elem", "chain_two_objects",
            "chain_three_members", "chain_str_member", "chain_member_loop",
            "memcomp_plus", "memcomp_minus", "memcomp_mult", "memcomp_div", "memcomp_str",
            "memcomp_str_num", "memcomp_inc", "memcomp_dec", "memcomp_arg", "memcomp_call_value",
            "memcomp_loop", "memcomp_while",
            "switch_fall_two", "switch_fall_first", "switch_fall_miss", "switch_fall_three",
            "switch_fall_str", "switch_fall_mixed", "switch_fall_body", "switch_fall_default",
            "switch_real_fall",
            "boolgrp_ret", "boolgrp_ret_false", "boolgrp_mult", "boolgrp_asg",
            "elemwrite_comp", "elemwrite_set", "elemwrite_set_str", "elemwrite_arg_idx", "elemwrite_call_val",
            "elemwrite_map", "elemwrite_order", "elemwrite_comp_str", "elemwrite_default_twice", "elemwrite_inc",
            "elemwrite_dec", "elemwrite_comp_map", "elemwrite_loop",
            "enum_eq_num", "enum_ne_num", "enum_eq_and", "enum_ret_eq", "global_eq_num",
            "bool_compound_add", "bool_compound_sub", "bool_compound_loop",
            "bool_eq_one", "bool_eq_zero", "bool_ne_zero",
        };

        class Outcome
        {
            public string Interpreted;
            public string Compiled;
            public string InterpretedError;
            public string CompiledError;
            public bool FellBack;          // ran interpreted because translation failed
            public bool Compiles => CompiledError == null && !FellBack;
            public bool Matches => CompiledError == null && InterpretedError == null &&
                                   Compiled == Interpreted;
        }

        static string Run(string script, out string error)
        {
            error = null;
            try
            {
                var interpreter = new Interpreter();
                interpreter.InitStandalone();
                new CSCSMath.CscsMathModule().CreateInstance(interpreter);
                var preamble = "gcount = 10;\ngstr = \"gs\";\ngarr = {1,2,3};\ngmap = {\"k\":1};\ngrid = {{1,2},{3,4}};\n" +
                               "class Point { x = 0; y = 0; Point(a, b) { x = a; y = b; } function Sum() { return x + y; } " +
                               "function Scale(f) { return x * f; } function Tag(pre) { return pre + x; } }\n" +
                               "class Named { v = 0; tag = \"\"; kid = 0; Named(a, b) { v = a; tag = b; } " +
                               "function Kid() { return kid; } }\n" +
                               "function helper(q) { return q * 3; }\n" +
                               "function strHelper(q) { return \"s\"; }\n" +
                               "function build() { return {7, 8}; }\n" +
                               "function sumArr(q) { t = 0; for (x in q) { t += x; } return t; }\n" +
                               "function mapVal(mp, k) { return mp[k]; }\n" +
                               "function addToEnd(q) { q.Add(99); }\n" +
                               "class Base { bx = 1; Base(a) { bx = a; } function addBase(n) { return n + bx; } }\n" +
                               "class Derived : Base { dz = 2; Derived(a, b) { bx = a; dz = b; } " +
                               "function addAll(n) { return n + bx + dz; } }\n" +
                               "var Colors = Enum {Red, Green, Blue};\n" +
                               "namespace nsp { nlocal = 7; function nfunc(x) { return x + nlocal; } }\n" +
                               "function callsBack(x) { return x * 2; }\n" +
                               "function throwsAlways(x) { throw \"boom\" + x; }\n" +
                               "function sumPoint(p) { return p.x + p.y; }\n" +
                               "class Counter { cv = 0; Counter(v) { cv = v; } " +
                               "function Value() { return cv; } }\n" +
                               "class WithDef { da = 0; db = 0; dc = 0; WithDef(x = 1, y = 2, z = 3) " +
                               "{ da = x; db = y; dc = z; } " +
                               "function ToString() { return \"{\" + da + \",\" + db + \",\" + dc + \"}\"; } }\n";
                var result = interpreter.Process(preamble + script, "coverage", true);
                return result == null ? "<null>" : result.AsString();
            }
            catch (Exception exc)
            {
                error = exc.Message.Split('\n')[0];
                return null;
            }
        }

        static Outcome Evaluate(Construct construct)
        {
            var compiledName = "c_" + construct.Name;
            var plainName = "n_" + construct.Name;

            var compiledScript = "cfunction " + compiledName + construct.TypedSig + " { " +
                construct.Body.Replace("SELF", compiledName) + " }\n" + compiledName + construct.Call + ";";
            var plainScript = "function " + plainName + construct.PlainSig + " { " +
                construct.Body.Replace("SELF", plainName) + " }\n" + plainName + construct.Call + ";";

            var outcome = new Outcome();
            outcome.Interpreted = Run(plainScript, out outcome.InterpretedError);

            SplitAndMerge.Precompiler.ClearFallbacks();
            outcome.Compiled = Run(compiledScript, out outcome.CompiledError);
            outcome.FellBack = SplitAndMerge.Precompiler.DidFallBack(compiledName);
            return outcome;
        }

        [TestMethod]
        public void Every_Construct_Behaves_As_The_Interpreter_Does()
        {
            // The headline guarantee: whatever the translator can or cannot handle, marking
            // a function "cfunction" never changes what the script computes. Constructs the
            // translator cannot compile fall back to the interpreter and still run.
            var wrong = new List<string>();
            foreach (var construct in Constructs)
            {
                var outcome = Evaluate(construct);
                if (outcome.InterpretedError != null)
                {
                    continue;   // no reference result to compare against
                }
                if (!outcome.Matches)
                {
                    wrong.Add($"  {construct.Name}: " +
                        (outcome.CompiledError ?? $"cfunction=[{outcome.Compiled}] interpreted=[{outcome.Interpreted}]") +
                        $"   body: {construct.Body}");
                }
            }

            Assert.AreEqual(0, wrong.Count,
                "declaring these as cfunction changed the result:\n" + string.Join("\n", wrong));
        }

        [TestMethod]
        public void Generated_Code_Never_Diverges_From_Interpreted()
        {
            // Same check with the safety net switched off, so it measures the translator
            // itself: anything it chooses to compile must be right. A compile error here is
            // acceptable; a different answer is not.
            SplitAndMerge.Precompiler.FallbackToInterpreter = false;
            try
            {
                var diverged = new List<string>();
                foreach (var construct in Constructs)
                {
                    var outcome = Evaluate(construct);
                    if (outcome.Compiles && outcome.InterpretedError == null &&
                        outcome.Compiled != outcome.Interpreted)
                    {
                        diverged.Add($"  {construct.Name}: compiled=[{outcome.Compiled}] " +
                                     $"interpreted=[{outcome.Interpreted}]  body: {construct.Body}");
                    }
                }

                Assert.AreEqual(0, diverged.Count,
                    "generated C# silently disagreed with the interpreter:\n" +
                    string.Join("\n", diverged));
            }
            finally
            {
                SplitAndMerge.Precompiler.FallbackToInterpreter = true;
            }
        }

        [TestMethod]
        public void Supported_Constructs_Are_Really_Compiled()
        {
            // Guards against the fallback quietly absorbing a regression: these constructs
            // must still be translated to C#, not merely produce the right answer.
            var regressed = new List<string>();
            foreach (var construct in Constructs.Where(c => Supported.Contains(c.Name)))
            {
                var outcome = Evaluate(construct);
                if (outcome.FellBack)
                {
                    regressed.Add($"  {construct.Name}: fell back to the interpreter");
                }
                else if (!outcome.Matches)
                {
                    regressed.Add($"  {construct.Name}: " +
                        (outcome.CompiledError ?? $"compiled=[{outcome.Compiled}] interpreted=[{outcome.Interpreted}]"));
                }
            }

            Assert.AreEqual(0, regressed.Count,
                "constructs that used to compile to C# no longer do:\n" + string.Join("\n", regressed));
        }

        [TestMethod]
        public void Report_Coverage()
        {
            // Not an assertion -- prints the current state so the gap list stays visible.
            var report = new StringBuilder();
            int compiled = 0, fellBack = 0, broken = 0;
            var newlyCompiled = new List<string>();

            foreach (var construct in Constructs)
            {
                var outcome = Evaluate(construct);
                string status;
                if (outcome.InterpretedError != null) { status = "no interpreted baseline"; }
                else if (!outcome.Matches) { status = "BROKEN"; broken++; }
                else if (outcome.FellBack) { status = "interpreted (fallback)"; fellBack++; }
                else
                {
                    status = "compiled";
                    compiled++;
                    if (!Supported.Contains(construct.Name))
                    {
                        newlyCompiled.Add(construct.Name);
                    }
                }
                report.AppendLine($"  {status,-24} {construct.Name}");
            }

            report.AppendLine();
            report.AppendLine($"  compiled={compiled}  fallback={fellBack}  broken={broken}  total={Constructs.Length}");
            if (newlyCompiled.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("  Newly compiled -- add to Supported: " + string.Join(", ", newlyCompiled));
            }
            Console.WriteLine(report.ToString());
        }
    }
}
