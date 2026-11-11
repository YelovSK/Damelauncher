using Damelauncher.Serve;

namespace Damelauncher.Tests.Serve;

[TestClass]
public sealed class ServeIconsTests
{
    private string _directory = null!;

    [TestInitialize]
    public void CreateDirectory() =>
        _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"damelauncher-icons-{Guid.NewGuid():N}")).FullName;

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    public void FilesOfOneTypeShareAKey()
    {
        Assert.AreEqual("ext:.txt", ServeIcons.GetKey(@"C:\notes\a.txt", NoFolders));
        Assert.AreEqual("ext:.txt", ServeIcons.GetKey(@"D:\b.TXT", NoFolders));
        Assert.AreEqual("ext:", ServeIcons.GetKey(@"C:\notes\README", NoFolders));
    }

    [TestMethod]
    [DataRow(@"C:\Apps\tool.exe")]
    [DataRow(@"C:\Start Menu\Tool.LNK")]
    [DataRow(@"C:\icons\app.ico")]
    [DataRow(@"C:\Users\me\site.url")]
    public void FilesWithTheirOwnIconKeepTheirPath(string location) =>
        Assert.AreEqual(location, ServeIcons.GetKey(location, NoFolders));

    [TestMethod]
    public void FoldersShareAKeyWhateverTheirName() =>
        Assert.AreEqual(
            ServeIcons.GetKey(@"C:\Packages\Microsoft.Notepad_8wekyb3d8bbwe", _ => true),
            ServeIcons.GetKey(@"C:\Users", _ => true));

    [TestMethod]
    [DataRow(@"shell:AppsFolder\Microsoft.WindowsNotepad_8wekyb3d8bbwe!App")]
    [DataRow(@"C:\")]
    public void ShellLocationsAndDrivesKeepTheirLocation(string location) =>
        Assert.AreEqual(location, ServeIcons.GetKey(location, _ => true));

    [TestMethod]
    public void PruningDeletesOnlyWhatWasLastWrittenBeforeTheCutoff()
    {
        DateTime cutoff = DateTime.UtcNow - TimeSpan.FromDays(30);
        string stale = WriteFile("stale.png", cutoff - TimeSpan.FromDays(1));
        string leftover = WriteFile("leftover.png.4.tmp", cutoff - TimeSpan.FromDays(1));
        string recent = WriteFile("recent.png", cutoff + TimeSpan.FromDays(1));

        ServeIcons.Prune(_directory, cutoff);

        Assert.IsFalse(File.Exists(stale));
        Assert.IsFalse(File.Exists(leftover));
        Assert.IsTrue(File.Exists(recent));
    }

    [TestMethod]
    public void PruningAMissingFolderDoesNothing() =>
        ServeIcons.Prune(Path.Combine(_directory, "missing"), DateTime.UtcNow);

    private static bool NoFolders(string location) => false;

    private string WriteFile(string name, DateTime lastWriteUtc)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, []);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }
}
