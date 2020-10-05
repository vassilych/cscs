using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitAndMerge;

namespace cscs.Tests.IntegrationTests.CoreFunctions.Math
{
    [TestClass]
    public class CosFixture : BaseCscsFixture
    {
        [TestInitialize]
        public void IntializeTest()
        {
            outputBuffer.Clear();
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
        public void Should_Return_Cosine(double input)
        {
            var expected = System.Math.Cos(input);
            var script = $"cos({input}); // Should return: {expected}";
            var actual = Process(script);
            Console.WriteLine(script);
            Console.WriteLine(outputBuffer);
            Assert.AreEqual(expected.ToString("G"), actual.AsString());
        }


        [TestMethod]
        [DataRow(double.NegativeInfinity)]
        [DataRow(double.PositiveInfinity)]
        public void Should_Throw_Cscs_Exception(double input)
        {
            var expected = System.Math.Cos(input);
            var script = $"cos({input}); // Should return: {expected}";
            var actual = Process(script);
            Console.WriteLine(script);
            Console.WriteLine(outputBuffer);
            StringAssert.Contains(outputBuffer.ToString(), "CSCS Parsing Exception");

        }
    }
}
