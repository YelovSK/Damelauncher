using Dame.Win32.Input;
using Damelauncher.Settings;

namespace Damelauncher.Tests.Settings;

[TestClass]
public sealed class HotKeyTests
{
    [TestMethod]
    public void ReadsTheWayItsPressed()
    {
        Assert.AreEqual("Alt+Space", new HotKey(HotKeyModifiers.Alt, WindowKey.Space).Text);
        Assert.AreEqual(
            "Ctrl+Alt+Shift+Win+F1",
            new HotKey(HotKeyModifiers.Windows | HotKeyModifiers.Shift | HotKeyModifiers.Alt | HotKeyModifiers.Control, WindowKey.F1).Text);
        Assert.AreEqual("Ctrl+.", new HotKey(HotKeyModifiers.Control, WindowKey.Period).Text);
    }

    [TestMethod]
    public void ReadsBackWhatItWrites()
    {
        HotKey[] hotKeys =
        [
            HotKey.Default,
            new(HotKeyModifiers.Alt | HotKeyModifiers.Shift, WindowKey.Space),
            new(HotKeyModifiers.Control, WindowKey.Period),
            new(HotKeyModifiers.Windows, WindowKey.Number1),
            new(HotKeyModifiers.Control | HotKeyModifiers.Alt, WindowKey.Backslash),
        ];

        foreach (HotKey hotKey in hotKeys)
        {
            Assert.AreEqual(hotKey, HotKey.Parse(hotKey.Text), hotKey.Text);
        }
    }

    // The settings file is meant to be edited by hand too.
    [TestMethod]
    public void ReadsHandWrittenSpellings() =>
        Assert.AreEqual(new HotKey(HotKeyModifiers.Control | HotKeyModifiers.Alt, WindowKey.Space), HotKey.Parse("alt + ctrl + space"));

    [TestMethod]
    public void RejectsWhatIsntAKeyCombination()
    {
        Assert.IsNull(HotKey.Parse(string.Empty));
        Assert.IsNull(HotKey.Parse("Alt+Banana"));
        Assert.IsNull(HotKey.Parse("Hyper+Space"));
        Assert.IsNull(HotKey.Parse("Space+Alt"));
    }
}
