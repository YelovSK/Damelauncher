using Dame.Win32;

namespace Damelauncher.Tests.Win32;

[TestClass]
public sealed class WindowsKeyTapTests
{
    [TestMethod]
    public void CanStopAndStartWatchingAgain()
    {
        for (int round = 0; round < 3; round++)
        {
            using var tap = new WindowsKeyTap(static () => { });
        }
    }

    [TestMethod]
    public void OnlyOneWatchesAtATime()
    {
        using var tap = new WindowsKeyTap(static () => { });

        Assert.ThrowsExactly<InvalidOperationException>(() => new WindowsKeyTap(static () => { }));
    }
}
