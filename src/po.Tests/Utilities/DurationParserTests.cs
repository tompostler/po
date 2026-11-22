using po.Utilities;

namespace po.Tests.Utilities
{
    [TestClass]
    public sealed class DurationParserTests
    {
        [TestMethod]
        [DataRow("90m", 90 * 60)]
        [DataRow("2H", 2 * 60 * 60)]
        [DataRow("1.5d", 36 * 60 * 60)]
        [DataRow("45s", 45)]
        [DataRow("01:30:00", 90 * 60)]
        public void ParsesSuffixedNumbersAndTimeSpans(string input, int expectedSeconds)
        {
            (string msg, TimeSpan parsed) = DurationParser.Parse(input);

            Assert.IsNull(msg);
            Assert.AreEqual(TimeSpan.FromSeconds(expectedSeconds), parsed);
        }

        [TestMethod]
        [DataRow("xm")]
        [DataRow("xs")]
        [DataRow("soon")]
        public void RejectsUnparseableInputWithAMessage(string input)
        {
            (string msg, _) = DurationParser.Parse(input);

            Assert.IsNotNull(msg);
        }
    }
}
