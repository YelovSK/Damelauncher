using Damelauncher.Plugins;
using Damelauncher.Search;

namespace Damelauncher.Tests.Search;

[TestClass]
public sealed class UsageHistoryTests
{
    private string _path = null!;

    [TestInitialize]
    public void CreatePath() =>
        _path = Path.Combine(Path.GetTempPath(), $"damelauncher-history-{Guid.NewGuid():N}.txt");

    [TestCleanup]
    public void DeleteFile() => File.Delete(_path);

    [TestMethod]
    public void AResultThatWasNeverLaunchedHasNoScore()
    {
        var history = UsageHistory.Load(_path);

        Assert.AreEqual(0.0, history.GetScore(new FakeProvider("programs").Result("a"), DateTime.UtcNow));
    }

    [TestMethod]
    public void LaunchesAreKeptAcrossRestarts()
    {
        LaunchResult result = new FakeProvider("programs").Result("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App");
        UsageHistory.Load(_path).RecordLaunch(result);
        UsageHistory.Load(_path).RecordLaunch(result);

        Assert.AreEqual(2.0, UsageHistory.Load(_path).GetScore(result, DateTime.UtcNow), 0.01);
    }

    // Everything's results are known by their paths, which have colons of their own.
    [TestMethod]
    public void IdsWithColonsAreKeptAcrossRestarts()
    {
        LaunchResult result = new FakeProvider("everything").Result(@"C:\Users\me\notes:draft.txt");
        UsageHistory.Load(_path).RecordLaunch(result);

        Assert.AreEqual(1.0, UsageHistory.Load(_path).GetScore(result, DateTime.UtcNow), 0.01);
    }

    [TestMethod]
    public void TheSameIdFromAnotherProviderIsAnotherResult()
    {
        var history = UsageHistory.Load(_path);
        history.RecordLaunch(new FakeProvider("programs").Result("a"));

        Assert.AreEqual(0.0, history.GetScore(new FakeProvider("settings").Result("a"), DateTime.UtcNow));
    }

    [TestMethod]
    public void AScoreHalvesEveryTwoWeeksWithoutALaunch()
    {
        var history = UsageHistory.Load(_path);
        LaunchResult result = new FakeProvider("programs").Result("a");
        history.RecordLaunch(result);
        DateTime now = DateTime.UtcNow;

        Assert.AreEqual(0.5, history.GetScore(result, now.AddDays(14)), 0.01);
        Assert.AreEqual(0.25, history.GetScore(result, now.AddDays(28)), 0.01);
    }

    [TestMethod]
    public void ForgettingIsKeptAcrossRestarts()
    {
        LaunchResult result = new FakeProvider("programs").Result("a");
        var history = UsageHistory.Load(_path);
        history.RecordLaunch(result);
        history.Forget(result);

        Assert.AreEqual(0.0, UsageHistory.Load(_path).GetScore(result, DateTime.UtcNow));
    }

    // A hand-edited or half-written file shouldn't cost the rest of the history.
    [TestMethod]
    public void UnreadableLinesAreSkipped()
    {
        File.WriteAllLines(_path,
        [
            "not a line of history",
            $"3\t{DateTime.UtcNow:O}\tprograms:a",
            "x\ty\tz",
            $"4\t{DateTime.UtcNow:O}\twithout-a-provider",
        ]);

        var history = UsageHistory.Load(_path);

        Assert.AreEqual(3.0, history.GetScore(new FakeProvider("programs").Result("a"), DateTime.UtcNow), 0.01);
    }
}
