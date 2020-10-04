using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitAndMerge;

namespace cscs.Tests.UnitTests.CoreFunctions.Math
{
    [TestClass]
    public class AbsFixture : BaseCscsFixture
    {
        [TestInitialize]
        public void IntializeTest()
        {
            outputBuffer.Clear();
        }

        [TestMethod]
        [DataRow(short.MinValue, short.MaxValue + 1)]
        [DataRow(short.MaxValue, short.MaxValue)]
        [DataRow(int.MinValue, int.MaxValue + 1L)]
        [DataRow(int.MaxValue, int.MaxValue)]
        [DataRow(long.MinValue + 1, long.MaxValue)]
        [DataRow(long.MaxValue, long.MaxValue)]
        [DataRow(1,1)]
        [DataRow(1.1, 1.1)]
        [DataRow(-1, 1)]
        [DataRow(-1.1, 1.1)]
        [DataRow(double.Epsilon, double.Epsilon)]
        [DataRow(-double.Epsilon, double.Epsilon)]
        [DataRow(double.MinValue, double.MaxValue)]
        [DataRow(double.MaxValue, double.MaxValue)]
        [DataRow(double.NaN, double.NaN)]
        //[DataRow(double.NegativeInfinity, 1.1)]
        //[DataRow(double.PositiveInfinity, 1.1)]
        public void Should_Return_AbsoluteValue(double input, double expected)
        {
            var script = $"abs({input});";
            var actual = Process(script);
            Console.WriteLine(script);
            Console.WriteLine(outputBuffer);

            Assert.AreEqual(expected.ToString("G"), actual.AsString());
        }
    }
}
