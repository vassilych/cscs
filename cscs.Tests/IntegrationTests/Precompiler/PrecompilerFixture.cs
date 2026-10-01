using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitAndMerge;

namespace cscs.Tests.IntegrationTests.Precompiler
{
    /// <summary>
    /// Guards the cfunction/dllfunction precompilation path.
    ///
    /// This whole feature silently stopped working when the project moved to .NET Core:
    /// CodeDom's CSharpCodeProvider.CompileAssemblyFromSource throws
    /// PlatformNotSupportedException on .NET Core / .NET 5+. Nothing failed loudly,
    /// because the exception surfaced only as a runtime parsing error inside scripts.
    /// These tests exist so that can never happen quietly again.
    /// </summary>
    [TestClass]
    public class PrecompilerFixture : BaseCscsFixture
    {
        [TestInitialize]
        public void IntializeTest()
        {
            OutputBuffer.Clear();
        }

        [TestMethod]
        public void Compiled_Function_Actually_Compiles()
        {
            var result = Process(@"
                cfunction double addC(double a, double b) {
                  return a + b;
                }
                addC(2.5, 4.25);");

            AssertNoCscsException();
            Assert.AreEqual(6.75, result.AsDouble(), 1e-9);
        }

        [TestMethod]
        public void Compiled_For_Loop_Matches_Interpreted()
        {
            var result = Process(@"
                cfunction double loopC(int n) {
                  r = 0.0;
                  for (i = 0; i < n; i++) {
                    r += (i*3.0 + 7.0) / (i + 1.0) - (i % 5);
                  }
                  return r;
                }
                function loopN(n) {
                  r = 0.0;
                  for (i = 0; i < n; i++) {
                    r += (i*3.0 + 7.0) / (i + 1.0) - (i % 5);
                  }
                  return r;
                }
                loopC(1000) - loopN(1000);");

            AssertNoCscsException();
            Assert.AreEqual(0.0, result.AsDouble(), 1e-9, "compiled and interpreted results diverged");
        }

        [TestMethod]
        public void Loop_Counter_Is_An_Int_Only_Where_That_Cannot_Change_An_Answer()
        {
            // Explain mode hands back the generated C# without loading it.
            SplitAndMerge.PrecompileExplainer.Enabled = true;
            try
            {
                Process(@"
                    cfunction double ctrPlain(int n) { t = 0; for (i = 0; i < n; i++) { t += i; } return t; }
                    cfunction double ctrDown(int n) { t = 0; for (k = 10; k >= 0; k -= 2) { t += k; } return t; }
                    cfunction double ctrIndex(int n) { a = {1, 2, 3}; t = 0; for (i = 0; i < a.Size; i++) { t += a[i] * 2; } return t; }
                    cfunction double ctrDivide(int n) { t = 0; for (i = 0; i < n; i++) { t += (i + 1) / 2; } return t; }
                    cfunction double ctrSquare(int n) { t = 0; for (i = 0; i < n; i++) { t += i * i; } return t; }
                    cfunction double ctrCopied(int n) { t = 0; for (i = 0; i < n; i++) { x = i + 1; t += x; } return t; }
                    cfunction double ctrAssigned(int n) { t = 0; for (i = 0; i < n; i++) { t += i; i += 1; } return t; }
                    cfunction double ctrDoubleBound(double n) { t = 0; for (i = 0; i < n; i++) { t += i; } return t; }
                    1;");
            }
            finally
            {
                SplitAndMerge.PrecompileExplainer.Enabled = false;
            }
            AssertNoCscsException();

            string Code(string name) =>
                SplitAndMerge.PrecompileExplainer.Reports.Last(r => r.FunctionName == name).CSharpCode ?? "";

            StringAssert.Contains(Code("ctrPlain"), "int i;");
            StringAssert.Contains(Code("ctrDown"), "int k;");
            StringAssert.Contains(Code("ctrIndex"), "int i;");
            // "/" truncates between ints, "*" overflows, a copy would declare x from an int,
            // an assignment could leave the counter fractional, and a double bound can be
            // fractional or huge: each keeps the CSCS double.
            StringAssert.Contains(Code("ctrDivide"), "double i;");
            StringAssert.Contains(Code("ctrSquare"), "double i;");
            StringAssert.Contains(Code("ctrCopied"), "double i;");
            StringAssert.Contains(Code("ctrAssigned"), "double i;");
            StringAssert.Contains(Code("ctrDoubleBound"), "double i;");
        }

        [TestMethod]
        public void Int_And_Double_Loop_Counters_Answer_As_The_Interpreter_Does()
        {
            // Each body runs compiled and interpreted; the two must agree, and must compile.
            var cases = new[]
            {
                ("ctrSum",    "(int n)", "t = 0; for (i = 0; i < n; i++) { t += i; } return t;", "(100)"),
                ("ctrText",   "(int n)", "r = \"\"; for (i = 0; i < n; i++) { r += \"v\" + i; } return r;", "(4)"),
                ("ctrRound",  "(int n)", "t = 0; for (i = 0; i < n; i++) { t += Math.Round(2.34567, i); } return t;", "(4)"),
                ("ctrStep",   "(int n)", "t = 0; for (k = 10; k >= 0; k -= 3) { t += k; } return t;", "(0)"),
                ("ctrHalf",   "(int n)", "t = 0; for (i = 0; i < n; i++) { t += (i + 1) / 2; } return t;", "(7)"),
                ("ctrBig",    "(int n)", "t = 0; for (i = 0; i < n; i++) { t += i * i; } return t;", "(50000)"),
            };
            foreach (var (name, sig, body, call) in cases)
            {
                SplitAndMerge.Precompiler.ClearFallbacks();
                var result = Process(
                    "cfunction " + name + "C" + sig + " { " + body + " }\n" +
                    "function " + name + "P(n) { " + body + " }\n" +
                    name + "C" + call + " == " + name + "P" + call + ";");
                AssertNoCscsException();
                Assert.AreEqual(1, result.AsInt(), name + ": compiled and interpreted answers differ");
                Assert.IsFalse(SplitAndMerge.Precompiler.DidFallBack(name + "C"), name + " fell back to the interpreter");
            }
        }

        [TestMethod]
        public void Compiled_While_Loop_Runs()
        {
            var result = Process(@"
                cfunction int whileC(int n) {
                  i = 0;
                  total = 0;
                  while (i < n) {
                    total += i;
                    i++;
                  }
                  return total;
                }
                whileC(10);");

            AssertNoCscsException();
            Assert.AreEqual(45, result.AsInt());
        }

        [TestMethod]
        public void Compiled_Function_Can_Call_Another_Compiled_Function()
        {
            // Mirrors the ct/cget pair in Scripts/Samples/test.cscs.
            var result = Process(@"
                cfunction cget(int n, double x, double y) {
                  z = x + y + pow(2,n);
                  return z;
                }
                cfunction ct(int n) {
                  return cget(n, 0.8, 0.9*2);
                }
                ct(10);");

            AssertNoCscsException();
            Assert.AreEqual(1026.6, result.AsDouble(), 1e-9);
        }

        [TestMethod]
        public void Compiled_Function_Handles_Strings()
        {
            var result = Process(@"
                cfunction string greetC(string name) {
                  msg = ""Hello, "" + name + ""!"";
                  return msg;
                }
                greetC(""CSCS"");");

            AssertNoCscsException();
            Assert.AreEqual("Hello, CSCS!", result.AsString());
        }

        [TestMethod]
        public void Compiled_Function_Uses_System_Math()
        {
            var result = Process(@"
                cfunction double mathC(double n) {
                  return Math.Round(Math.Sqrt(n) + Math.Abs(-2.5), 4);
                }
                mathC(16);");

            AssertNoCscsException();
            Assert.AreEqual(6.5, result.AsDouble(), 1e-9);
        }

        [TestMethod]
        public void Compile_Errors_Are_Reported_With_Detail()
        {
            // With the safety net off, a body that cannot be translated must fail loudly,
            // and the message must be actionable -- Roslyn diagnostics against the generated
            // source, not a bare "Operation is not supported on this platform".
            SplitAndMerge.Precompiler.FallbackToInterpreter = false;
            try
            {
                Process(@"
                    cfunction double bad(double a) {
                      return a +* ;
                    }");

                var output = OutputBuffer.ToString();
                StringAssert.Contains(output, "Compile error",
                    "a malformed cfunction should surface a compile error");
                Assert.IsFalse(output.Contains("not supported on this platform"),
                    "the runtime compiler backend is unavailable on this platform");
            }
            finally
            {
                SplitAndMerge.Precompiler.FallbackToInterpreter = true;
            }
        }

        [TestMethod]
        public void Untranslatable_Body_Falls_Back_Instead_Of_Throwing()
        {
            // The translator covers a subset of CSCS. A construct outside that subset must
            // not kill the script: the function is registered as an ordinary interpreted
            // one, and the reason is recorded rather than swallowed.
            // A caught exception's Stack is the example, and a fallback by design rather than a
            // gap: it is the interpreter's chain of calls at the throw, which compiled code keeps
            // no record of. try/catch, switch, "**", "===" on numbers, class fields, "===" between
            // a string and a number, and then a shift each played this role until they started
            // compiling.
            SplitAndMerge.Precompiler.ClearFallbacks();
            var result = Process(@"
                cfunction string tricky(int a) {
                  try { throw ""bad "" + a; } catch (e) { return e.Stack; }
                }
                tricky(3);");

            AssertNoCscsException();
            StringAssert.Contains(result.AsString(), "tricky()",
                "the fallback should still produce the interpreted result");
            Assert.IsTrue(SplitAndMerge.Precompiler.DidFallBack("tricky"),
                "an untranslatable cfunction should be recorded as a fallback");
            SplitAndMerge.Precompiler.ClearFallbacks();
        }

        [TestMethod]
        public void Math_Module_Is_Registered_Under_Its_Current_Name()
        {
            // The CoreFunctions/Math fixtures still call abs()/sin()/... but the module
            // registers Math.Abs/Math.Sin/... (see Constants.MATH_ABS). Documenting the
            // live name here so the rename is visible rather than showing up as 75
            // mystery failures.
            var result = Process("Math.Abs(-1);");
            AssertNoCscsException();
            Assert.AreEqual(1.0, result.AsDouble(), 1e-9);
        }

        [TestMethod]
        public void Ahead_Of_Time_Implementation_Replaces_Code_Generation()
        {
            // The AOT path is what makes cfunction usable on targets that forbid runtime
            // code generation (iOS). A registered implementation must be used verbatim,
            // and nothing may be compiled.
            PrecompiledRegistry.Register("aotSquare", (interp, str, num, ints, arrStr, arrNum,
                arrInt, mapStr, mapNum, vars) => new Variable(num[0] * num[0]));
            try
            {
                var before = RoslynCompiler.CacheMisses;
                var result = Process(@"
                    cfunction double aotSquare(double a) {
                      return 0;  // deliberately wrong: the registered version must win
                    }
                    aotSquare(7);");

                AssertNoCscsException();
                Assert.AreEqual(49.0, result.AsDouble(), 1e-9,
                    "the registered implementation was not used");
                Assert.AreEqual(before, RoslynCompiler.CacheMisses,
                    "code was generated even though an implementation was registered");
            }
            finally
            {
                PrecompiledRegistry.Clear();
            }
        }

        [TestMethod]
        public void Generated_Aot_Source_Contains_Every_Collected_Function()
        {
            AotGenerator.Clear();
            AotGenerator.Collecting = true;
            try
            {
                Process(@"
                    cfunction double genA(double a) { return a * 2; }
                    cfunction double genB(double b) { return b + 1; }");

                AssertNoCscsException();
                var source = AotGenerator.GetSource("TestPrecompiled", "SplitAndMerge");
                StringAssert.Contains(source, "Variable genA");
                StringAssert.Contains(source, "Variable genB");
                StringAssert.Contains(source, "PrecompiledRegistry.Register(\"genA\", genA);");
                StringAssert.Contains(source, "PrecompiledRegistry.Register(\"genB\", genB);");
                StringAssert.Contains(source, "public static partial class TestPrecompiled");
            }
            finally
            {
                AotGenerator.Collecting = false;
                AotGenerator.Clear();
            }
        }

        [TestMethod]
        public void Direct_Self_Calls_Only_Where_Nothing_Else_Needs_The_Interpreter()
        {
            SplitAndMerge.PrecompileExplainer.Enabled = true;
            try
            {
                Process(@"
                    function dscHelper(q) { return q * 3; }
                    cfunction double dscPure(int n) { if (n < 2) { return n; } return dscPure(n - 1) + dscPure(n - 2); }
                    cfunction double dscCallback(int n) { if (n == 0) { return dscHelper(1); } return dscCallback(n - 1) + 1; }
                    cfunction double dscTextRoute(int n) { if (n == 0) { return dscHelper(""a""); } return dscTextRoute(n - 1); }
                    cfunction double dscDefault(int n, int step = 1) { if (n <= 0) { return 0; } return 1 + dscDefault(n - step); }
                    cfunction double dscGlobal(int n) { if (n == 0) { return dscGlobalValue; } return dscGlobal(n - 1); }
                    1;");
            }
            finally
            {
                SplitAndMerge.PrecompileExplainer.Enabled = false;
            }
            AssertNoCscsException();

            string Code(string name) =>
                SplitAndMerge.PrecompileExplainer.Reports.Last(r => r.FunctionName == name).CSharpCode ?? "";

            // Every value dscPure returns is a number, so its calls to itself are typed both ways.
            StringAssert.Contains(Code("dscPure"), "dscPure__numdirect(__interpreter, CscsDirect.Int(");
            StringAssert.Contains(Code("dscPure"), "public static double dscPure__numbody(");
            Assert.IsFalse(Code("dscPure").Contains("CscsCalls.Call"));
            // A call with values to a script function needs no frame of the caller's -- a CSCS
            // function cannot see its caller's locals -- so it stays an ordinary call and the
            // recursion around it is still direct.
            StringAssert.Contains(Code("dscCallback"), "dscCallback__direct(__interpreter, CscsDirect.Int(");
            StringAssert.Contains(Code("dscCallback"), "CscsCalls.Call(__interpreter, \"dscHelper\"");
            // The text route names the caller's values, a default is bound by the interpreter, a
            // global is read from it: the frames matter.
            Assert.IsFalse(Code("dscTextRoute").Contains("__direct"));
            Assert.IsFalse(Code("dscDefault").Contains("__direct"));
            Assert.IsFalse(Code("dscGlobal").Contains("__direct"));
        }

        [TestMethod]
        public void Direct_Recursion_Stops_Where_The_Interpreter_Stops()
        {
            var saved = SplitAndMerge.InterpreterSecurity.MaxCallDepth;
            SplitAndMerge.InterpreterSecurity.MaxCallDepth = 100;
            try
            {
                Process(@"
                    cfunction double depthC(int n) { if (n == 0) { return 0; } return 1 + depthC(n - 1); }
                    function depthI(n) { if (n == 0) { return 0; } return 1 + depthI(n - 1); }
                    1;");
                for (int n = 90; n <= 110; n++)
                {
                    var compiled = Process("try { r = depthC(" + n + "); } catch (e) { r = \"err:\" + e; } r;").AsString();
                    var interpreted = Process("try { r = depthI(" + n + "); } catch (e) { r = \"err:\" + e; } r;").AsString();
                    Assert.AreEqual(interpreted, compiled, "depth " + n);
                }
                var deep = Process("try { r = depthC(5000); } catch (e) { r = \"err:\" + e; } r;").AsString();
                StringAssert.Contains(deep, "Recursion too deep");
            }
            finally
            {
                SplitAndMerge.InterpreterSecurity.MaxCallDepth = saved;
            }
            AssertNoCscsException();
        }

        /// <summary>
        /// Every operator, in a value and in a condition, and every truth test, compiled and
        /// interpreted over each kind of value an untyped argument can hold. This matrix is what
        /// found "x == y" comparing references, "&&"/"||" giving 1 or 0 where the interpreter gives
        /// the left value, lists being false in a condition, and "!text" giving 0 -- none of which
        /// the construct fixture, written one shape at a time, had covered.
        /// </summary>
        [TestMethod]
        public void Operators_And_Truth_Agree_With_The_Interpreter_For_Every_Kind_Of_Value()
        {
            var values = new[] { "3", "\"abc\"", "\"5\"", "{1, 2}", "0", "2.5" };
            var binary = new[] { "+", "-", "*", "/", "%", "<", ">", "<=", ">=", "==", "!=", "&&", "||", "===", "!==", "**" };
            var conditional = new[] { "<", ">", "==", "!=", "&&", "||", "===", "!==" };
            var unary = new[] {
                "if (x) { return \"T\"; } return \"F\";",
                "if (!x) { return \"T\"; } return \"F\";",
                "r = !x; return r;",
                "return x ? \"T\" : \"F\";",
            };
            var script = new System.Text.StringBuilder("opDiffs = \"\";\n");
            int k = 0;
            void Case(string signatureArgs, string callArgs, string body, string label)
            {
                k++;
                script.Append("cfunction opc" + k + "(" + signatureArgs + ") { " + body + " }\n");
                script.Append("function opi" + k + "(" + signatureArgs.Replace("variable ", "") + ") { " + body + " }\n");
                script.Append("try { c = \"\" + opc" + k + "(" + callArgs + "); } catch (e) { c = \"ERR\"; }\n");
                script.Append("try { i = \"\" + opi" + k + "(" + callArgs + "); } catch (e) { i = \"ERR\"; }\n");
                script.Append("if (c != i) { opDiffs += \"" + label.Replace("\"", "'") + " -> \" + c + \" vs \" + i + \"; \"; }\n");
            }
            foreach (var op in binary)
                foreach (var a in values)
                    foreach (var bv in values)
                        Case("variable x, variable y", a + ", " + bv, "r = x " + op + " y; return r;", a + " " + op + " " + bv);
            foreach (var op in conditional)
                foreach (var a in values)
                    foreach (var bv in values)
                        Case("variable x, variable y", a + ", " + bv,
                             "if (x " + op + " y) { return \"T\"; } return \"F\";", "if " + a + " " + op + " " + bv);
            foreach (var body in unary)
                foreach (var a in values)
                    Case("variable x", a, body, body + " on " + a);
            script.Append("opDiffs;");

            var result = Process(script.ToString());
            AssertNoCscsException();
            Assert.AreEqual("", result.AsString(), "compiled and interpreted disagree: " + result.AsString());
        }

        void AssertNoCscsException()
        {
            var output = OutputBuffer.ToString();
            Console.WriteLine(output);
            Assert.IsFalse(output.Contains("CSCS Parsing Exception"),
                "script raised a CSCS exception:\n" + output);
        }
    }
}
