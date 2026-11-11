using System.Globalization;

namespace Damelauncher.Serve;

/// <summary>A line read from the program that serve mode searches for, one tab-separated command per line.</summary>
internal abstract record ServeCommand
{
    private protected ServeCommand()
    {
    }

    /// <returns>The command, or <see langword="null"/> for a line that isn't one, which is ignored.</returns>
    /// <remarks>The last field is split off whole, so a query or id keeps any tab it has.</remarks>
    internal static ServeCommand? Parse(string line) =>
        line.Split('\t', 3) switch
        {
            ["query", string sequence] => Query(sequence, string.Empty),
            ["query", string sequence, string text] => Query(sequence, text),
            ["run", string provider, string id] => new RunCommand(provider, id),
            ["quit"] => new QuitCommand(),
            _ => null,
        };

    private static QueryCommand? Query(string sequence, string text) =>
        long.TryParse(sequence, NumberStyles.None, CultureInfo.InvariantCulture, out long number)
            ? new QueryCommand(number, text)
            : null;
}

/// <param name="Sequence">Increases with each query, and tags the results sent for it.</param>
internal sealed record QueryCommand(long Sequence, string Text) : ServeCommand;

/// <summary>Runs one of the results last sent, by the provider and result ids it was sent with.</summary>
internal sealed record RunCommand(string Provider, string Id) : ServeCommand;

internal sealed record QuitCommand : ServeCommand;
