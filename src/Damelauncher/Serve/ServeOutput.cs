using System.Globalization;
using Damelauncher.Plugins;

namespace Damelauncher.Serve;

/// <summary>
/// What serve mode tells the program it searches for: one message per line, its fields separated
/// by tabs, each message or batch of them flushed as soon as it's whole.
/// </summary>
/// <param name="disconnected">Called when the program stopped reading, so there's no one left to serve.</param>
internal sealed class ServeOutput(TextWriter writer, Action disconnected)
{
    internal void Ready() => Send(() => writer.Write("ready\n"));

    internal void Toggle() => Send(() => writer.Write("toggle\n"));

    /// <summary>Every result for a query, best first, replacing any sent for it before.</summary>
    /// <param name="getIcon">The icon field of a result: a PNG's path, <c>glyph:</c> and its symbol, or empty.</param>
    internal void Results(long sequence, IReadOnlyList<LaunchResult> results, Func<ResultIcon?, string> getIcon) =>
        Send(() =>
        {
            string tag = sequence.ToString(CultureInfo.InvariantCulture);
            WriteLine("begin", tag);
            foreach (LaunchResult result in results)
            {
                writer.Write("result");
                WriteField(result.Provider.Id);
                WriteField(result.Id);
                WriteField(result.Title);
                WriteField(result.Subtitle);
                WriteField(getIcon(result.Icon));
                writer.Write('\n');
            }

            WriteLine("end", tag);
        });

    /// <summary>An icon that arrived after the result was sent without one.</summary>
    internal void Icon(LaunchResult result, string path) =>
        Send(() =>
        {
            writer.Write("icon");
            WriteField(result.Provider.Id);
            WriteField(result.Id);
            WriteField(path);
            writer.Write('\n');
        });

    private void Send(Action write)
    {
        try
        {
            write();
            writer.Flush();
        }
        catch (IOException)
        {
            disconnected();
        }
    }

    private void WriteLine(string kind, string field)
    {
        writer.Write(kind);
        WriteField(field);
        writer.Write('\n');
    }

    // Tabs and line breaks would split the message, and nothing shown needs them.
    private void WriteField(string text)
    {
        writer.Write('\t');
        if (text.AsSpan().IndexOfAny('\t', '\r', '\n') < 0)
        {
            writer.Write(text);
            return;
        }

        foreach (char character in text)
        {
            writer.Write(character is '\t' or '\r' or '\n' ? ' ' : character);
        }
    }
}
