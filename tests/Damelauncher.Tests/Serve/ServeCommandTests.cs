using Damelauncher.Serve;

namespace Damelauncher.Tests.Serve;

[TestClass]
public sealed class ServeCommandTests
{
    [TestMethod]
    public void ReadsAQueryWithItsSequence() =>
        Assert.AreEqual(new QueryCommand(12, "fire fox"), ServeCommand.Parse("query\t12\tfire fox"));

    [TestMethod]
    public void ReadsAnEmptyQueryWithOrWithoutItsField()
    {
        Assert.AreEqual(new QueryCommand(3, ""), ServeCommand.Parse("query\t3\t"));
        Assert.AreEqual(new QueryCommand(3, ""), ServeCommand.Parse("query\t3"));
    }

    [TestMethod]
    public void AQueryKeepsTheTabsInItsText() =>
        Assert.AreEqual(new QueryCommand(1, "a\tb"), ServeCommand.Parse("query\t1\ta\tb"));

    [TestMethod]
    public void ReadsARunWithAnIdThatHasColonsAndTabs() =>
        Assert.AreEqual(
            new RunCommand("programs", @"C:\a:b\tc"),
            ServeCommand.Parse("run\tprograms\tC:\\a:b\\tc"));

    [TestMethod]
    public void ReadsQuit() => Assert.IsInstanceOfType<QuitCommand>(ServeCommand.Parse("quit"));

    [TestMethod]
    [DataRow("")]
    [DataRow("hello")]
    [DataRow("query")]
    [DataRow("query\tx\ttext")]
    [DataRow("query\t-1\ttext")]
    [DataRow("run\tprograms")]
    [DataRow("quit\tnow")]
    [DataRow("Query\t1\ttext")]
    public void IgnoresWhatIsntACommand(string line) => Assert.IsNull(ServeCommand.Parse(line));
}
