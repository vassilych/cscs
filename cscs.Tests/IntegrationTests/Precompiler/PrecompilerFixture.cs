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
            // "**" is the stable example: C# has no exponentiation operator, and rewriting it
            // to Math.Pow needs precedence-aware operand extraction, so it stays interpreted.
            // try/catch and switch each played this role until they started compiling.
            SplitAndMerge.Precompiler.ClearFallbacks();
            var result = Process(@"
                cfunction double tricky(double a) {
                  return 2**3**a;
                }
                tricky(2);");

            AssertNoCscsException();
            Assert.AreEqual(512.0, result.AsDouble(), 1e-9,
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

        void AssertNoCscsException()
        {
            var output = OutputBuffer.ToString();
            Console.WriteLine(output);
            Assert.IsFalse(output.Contains("CSCS Parsing Exception"),
                "script raised a CSCS exception:\n" + output);
        }
    }
}
