using po.Commands;

namespace po.Tests.Commands
{
    [TestClass]
    public sealed class CommandListTests
    {
        /// <summary>
        /// A command that needs enabling but isn't offered by <c>/commandlist</c> can never run, and nothing else would notice.
        /// </summary>
        [TestMethod]
        [DataRow("add")]
        [DataRow("remove")]
        public void OffersEveryCommandThatNeedsEnabling(string subcommand)
        {
            List<ICommand> commands = typeof(ICommand).Assembly.GetTypes()
                .Where(x => typeof(ICommand).IsAssignableFrom(x) && !x.IsAbstract)
                .Select(CreateForDefinition)
                .ToList();
            IEnumerable<string> needEnabling = commands.Where(x => x.Definition.RequiresChannelEnablement).Select(x => x.Definition.Name);

            CommandDefinition commandList = commands.OfType<CommandList>().Single().Definition;
            IReadOnlyList<string> offered = commandList.Subcommands.Single(x => x.Name == subcommand).Options.Single().Choices;

            Assert.AreSequenceEqual(needEnabling.ToList(), offered.ToList(), SequenceOrder.InAnyOrder);
        }

        /// <summary>
        /// Every command only stores its dependencies in its constructor, so nulls are enough to read its definition.
        /// </summary>
        private static ICommand CreateForDefinition(Type type)
        {
            System.Reflection.ConstructorInfo constructor = type.GetConstructors().Single();
            return (ICommand)constructor.Invoke(new object[constructor.GetParameters().Length]);
        }
    }
}
