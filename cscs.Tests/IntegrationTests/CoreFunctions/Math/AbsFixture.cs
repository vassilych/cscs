using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace cscs.Tests.IntegrationTests.CoreFunctions.Math
{
    [TestClass]
    public class AbsFixture : BaseCscsFixture
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
        public void Should_Return_AbsoluteValue(double input)
        {
            var expected = System.Math.Abs(input);
            var script = $"abs({input}); // Should return: {expected}";
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
            var expected = System.Math.Abs(input);
            var script = $"abs({input}); // Should return: {expected}";
            var actual = Process(script);
            Console.WriteLine(script);
            Console.WriteLine(OutputBuffer);
            StringAssert.Contains(OutputBuffer.ToString(), "CSCS Parsing Exception");

        }
    }
}
