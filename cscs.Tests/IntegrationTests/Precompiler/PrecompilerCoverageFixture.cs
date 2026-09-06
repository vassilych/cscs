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
            // Exponentiation is right-associative: 2**3**2 is 2**(3**2) = 512, not 64.
            // Falls back rather than compiling: C# has no ** operator, so this guards
            // that compiled and interpreted still agree on the value.
            new Construct("power_assoc", "(double n)", "(n)", "return 2**3**n;", "(2)"),
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
            "increment", "nested_call", "logical", "bool_var", "array_add", "int_div", "else_if", "ternary", "not", "recursion", "call_cscs_fn", "call_expr_arg", "call_twice", "call_nested", "string_split", "array_lit", "array_size", "map_lit", "map_set", "map_assign", "array_assign", "neg_index", "idx_expr", "idx_assign_expr", "switch", "switch_fall", "string_eq", "string_ne", "arg_reassign", "local_upper", "array_add_expr", "try_catch", "catch_value",
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
                var preamble = "function helper(q) { return q * 3; }\n";
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
