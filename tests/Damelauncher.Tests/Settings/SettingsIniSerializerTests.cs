using Dame.Win32.Input;
using Damelauncher.Settings;

namespace Damelauncher.Tests.Settings;

[TestClass]
public sealed class SettingsIniSerializerTests
{
    [TestMethod]
    public void AnEmptyFileGivesTheDefaults()
    {
        (LauncherSettings settings, IReadOnlyList<string> ignored) = SettingsIniSerializer.Read(string.Empty);

        Assert.AreEqual(HotKey.Default, settings.HotKey);
        Assert.AreEqual(ThemeId.Dark, settings.Theme);
        Assert.IsFalse(settings.OpenWithWindowsKey);
        Assert.IsEmpty(settings.Plugins);
        Assert.IsEmpty(ignored);
    }

    [TestMethod]
    public void ReadsBackWhatItWrites()
    {
        var written = new LauncherSettings
        {
            Theme = ThemeId.CatppuccinMocha,
            HotKey = new HotKey(HotKeyModifiers.Control | HotKeyModifiers.Shift, WindowKey.K),
            OpenWithWindowsKey = true,
            MaxResults = 25,
            Plugins = new Dictionary<string, PluginSettings>
            {
                ["everything"] = new() { Enabled = true, Keyword = "find", InNormalSearches = false, MaxResults = 7 },
                ["settings"] = new() { Enabled = false, Keyword = string.Empty },
                ["programs"] = new() { Keyword = null },
            },
        };

        (LauncherSettings read, IReadOnlyList<string> ignored) =
            SettingsIniSerializer.Read(SettingsIniSerializer.Write(written, string.Empty));

        Assert.AreEqual(written.Theme, read.Theme);
        Assert.AreEqual(written.HotKey, read.HotKey);
        Assert.AreEqual(written.OpenWithWindowsKey, read.OpenWithWindowsKey);
        Assert.AreEqual(written.MaxResults, read.MaxResults);
        CollectionAssert.AreEquivalent(written.Plugins.Keys.ToArray(), read.Plugins.Keys.ToArray());
        foreach ((string id, PluginSettings plugin) in written.Plugins)
        {
            Assert.AreEqual(plugin, read.Plugins[id], id);
        }
        Assert.IsEmpty(ignored);
    }

    // Comments and settings from a newer version survive an older one saving the file.
    [TestMethod]
    public void SavingKeepsCommentsAndUnknownKeys()
    {
        string existing = "; picked this one on purpose\nhotkey=Alt+Space\nfromTheFuture=1\n";

        string written = SettingsIniSerializer.Write(new LauncherSettings(), existing);

        StringAssert.Contains(written, "; picked this one on purpose");
        StringAssert.Contains(written, "fromTheFuture=1");
    }

    [TestMethod]
    public void AnUnreadableValueKeepsItsDefaultAndIsReported()
    {
        (LauncherSettings settings, IReadOnlyList<string> ignored) =
            SettingsIniSerializer.Read("hotkey=Alt+Banana\ntheme=chartreuse\nmaxResults=0\n[plugins.everything]\nmaxResults=many\n");

        Assert.AreEqual(HotKey.Default, settings.HotKey);
        Assert.AreEqual(ThemeId.Dark, settings.Theme);
        Assert.AreEqual(new LauncherSettings().MaxResults, settings.MaxResults);
        Assert.IsNull(settings.GetPlugin("everything").MaxResults);
        Assert.HasCount(4, ignored);
    }
}
