using po.Commands;

namespace po.Tests.Commands
{
    [TestClass]
    public sealed class TextArgumentParserTests
    {
        private static readonly CommandDefinition Po = new()
        {
            Name = "po",
            Description = "Displays images.",
            Subcommands =
            [
                new SubcommandDefinition
                {
                    Name = "random",
                    Description = "Schedules images.",
                    Options =
                    [
                        new CommandOption { Name = "count", Description = "How many.", Type = CommandOptionType.Integer, Required = true },
                        new CommandOption { Name = "duration", Description = "How long.", Type = CommandOptionType.String, Required = true },
                        new CommandOption { Name = "category", Description = "Which.", Type = CommandOptionType.String },
                    ],
                },
                new SubcommandDefinition { Name = "status", Description = "Reports." },
            ],
        };

        private static readonly CommandDefinition ScheduleMessage = new()
        {
            Name = "schedule-message",
            Description = "Schedules a message.",
            Options =
            [
                new CommandOption { Name = "delay", Description = "How long.", Type = CommandOptionType.String, Required = true },
                new CommandOption { Name = "message", Description = "What.", Type = CommandOptionType.String, Required = true },
            ],
        };

        [TestMethod]
        public void WordsFillOptionsInOrderAsTheirTypes()
        {
            Assert.IsTrue(TextArgumentParser.TryParse(Po, "RANDOM 5 2h cats", out ParsedArguments parsed, out _));

            Assert.AreEqual("random", parsed.Subcommand);
            Assert.AreEqual(5L, parsed.Values["count"]);
            Assert.AreEqual("2h", parsed.Values["duration"]);
            Assert.AreEqual("cats", parsed.Values["category"]);
        }

        [TestMethod]
        public void LastStringOptionTakesTheRestVerbatim()
        {
            Assert.IsTrue(TextArgumentParser.TryParse(ScheduleMessage, "5m  hello   \"there\" world ", out ParsedArguments parsed, out _));

            Assert.AreEqual("5m", parsed.Values["delay"]);
            Assert.AreEqual("hello   \"there\" world", parsed.Values["message"]);
        }

        [TestMethod]
        public void OmittedOptionalOptionIsAbsent()
        {
            Assert.IsTrue(TextArgumentParser.TryParse(Po, "random 5 2h", out ParsedArguments parsed, out _));

            Assert.IsFalse(parsed.Values.ContainsKey("category"));
        }

        [TestMethod]
        [DataRow("", DisplayName = "missing subcommand")]
        [DataRow("bogus", DisplayName = "unknown subcommand")]
        [DataRow("random 5", DisplayName = "missing required option")]
        [DataRow("random five 2h", DisplayName = "integer that isn't a number")]
        [DataRow("status extra", DisplayName = "too many arguments")]
        public void InvalidInputIsRejectedWithAMessage(string text)
        {
            Assert.IsFalse(TextArgumentParser.TryParse(Po, text, out _, out string error));

            Assert.IsFalse(string.IsNullOrWhiteSpace(error));
        }
    }
}
