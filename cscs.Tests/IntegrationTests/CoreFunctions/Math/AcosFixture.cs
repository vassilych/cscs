using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitAndMerge;

namespace cscs.Tests.IntegrationTests.CoreFunctions.Math
{
    [TestClass]
    public class AcosFixture : BaseCscsFixture
    {
        [TestInitialize]
        public void IntializeTest()
        {
            OutputBuffer.Clear();
        }

        [TestMethod]
        [DataRow(short.MinValue)]
        [DataRow(short.MaxValue)]
        [DataRow(int.MinValue)]
        [DataRow(int.MaxValue)]
        [DataRow(long.MinValue)]
        [DataRow(long.MaxValue)]
        [DataRow(1)]
        [DataRow(1.1)]
        [DataRow(-1)]
        [DataRow(-1.1)]
        [DataRow(double.Epsilon)]
        [DataRow(-double.Epsilon)]
        [DataRow(double.MinValue)]
        [DataRow(double.MaxValue)]
        [DataRow(double.NaN)]
        public void Should_Return_Acosine(double input)
        {
            var expected = System.Math.Acos(input);
            var script = $"acos({input}); // Should return: {expected}";
            var actual = Process(script);
            Console.WriteLine(script);
            Console.WriteLine(OutputBuffer);
            Assert.AreEqual(expected.ToString("G"), actual.AsString());
        }


        [TestMethod]
        [DataRow(double.NegativeInfinity)]
        [DataRow(double.PositiveInfinity)]
        public void Should_Throw_Cscs_Exception(double input)
        {
            var expected = System.Math.Acos(input);
            var script = $"acos({input}); // Should return: {expected}";
            var actual = Process(script);
            Console.WriteLine(script);
            Console.WriteLine(OutputBuffer);
            StringAssert.Contains(OutputBuffer.ToString(), "CSCS Parsing Exception");

        }
    }
}
