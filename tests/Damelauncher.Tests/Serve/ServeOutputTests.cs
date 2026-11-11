using Damelauncher.Plugins;
using Damelauncher.Serve;
using Damelauncher.Tests.Search;

namespace Damelauncher.Tests.Serve;

[TestClass]
public sealed class ServeOutputTests : IDisposable
{
    private readonly StringWriter _writer = new();
    private readonly FakeProvider _provider = new("programs");
    private int _disconnects;

    [TestMethod]
    public void FramesEveryResultOfAQueryBetweenItsBeginAndEnd()
    {
        ServeOutput output = Create();

        output.Results(
            7,
            [Result("a", "Alpha", "First", new ShellIcon("x")), Result("b", "Beta", "", null)],
            icon => icon is ShellIcon ? @"C:\icons\a.png" : "");

        Assert.AreEqual(
            "begin\t7\n"
            + "result\tprograms\ta\tAlpha\tFirst\tC:\\icons\\a.png\n"
            + "result\tprograms\tb\tBeta\t\t\n"
            + "end\t7\n",
            _writer.ToString());
    }

    [TestMethod]
    public void NoResultsAreAnEmptyFrame()
    {
        Create().Results(2, [], _ => "");

        Assert.AreEqual("begin\t2\nend\t2\n", _writer.ToString());
    }

    [TestMethod]
    public void TabsAndLineBreaksInFieldsBecomeSpaces()
    {
        Create().Results(1, [Result("a\tb", "Two\r\nlines", "tab\there", null)], _ => "");

        Assert.AreEqual(
            "begin\t1\nresult\tprograms\ta b\tTwo  lines\ttab here\t\nend\t1\n",
            _writer.ToString());
    }

    [TestMethod]
    public void SendsALateIconForItsResult()
    {
        Create().Icon(Result("a", "Alpha", "", null), @"C:\icons\a.png");

        Assert.AreEqual("icon\tprograms\ta\tC:\\icons\\a.png\n", _writer.ToString());
    }

    [TestMethod]
    public void SendsReadyAndToggle()
    {
        ServeOutput output = Create();

        output.Ready();
        output.Toggle();

        Assert.AreEqual("ready\ntoggle\n", _writer.ToString());
    }

    [TestMethod]
    public void SaysWhenTheReaderIsGone()
    {
        var output = new ServeOutput(new BrokenWriter(), () => _disconnects++);

        output.Ready();

        Assert.AreEqual(1, _disconnects);
    }

    public void Dispose() => _writer.Dispose();

    private ServeOutput Create() => new(_writer, () => _disconnects++);

    private LaunchResult Result(string id, string title, string subtitle, ResultIcon? icon) =>
        new(_provider, id, title, subtitle, icon, static () => { });

    private sealed class BrokenWriter : StringWriter
    {
        public override void Flush() => throw new IOException("The pipe has been ended.");
    }
}
