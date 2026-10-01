namespace MiniPrinter.Cli;

internal sealed class CliException(string message) : Exception(message);

/// <summary>Minimal "--name value", "--flag" and positional argument parser.</summary>
internal sealed class CliArgs
{
    private static readonly HashSet<string> Flags = ["--simulate", "--log", "--text"];
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _positional = [];

    public CliArgs(IEnumerable<string> args)
    {
        using var e = args.GetEnumerator();
        while (e.MoveNext())
        {
            var arg = e.Current;
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                _positional.Add(arg);
                continue;
            }
            if (Flags.Contains(arg))
            {
                _options[arg] = null;
                continue;
            }
            if (!e.MoveNext())
                throw new CliException($"Option {arg} needs a value.");
            _options[arg] = e.Current;
        }
    }

    public bool Has(string name) => _options.ContainsKey(name);

    public string? Value(string name) => _options.GetValueOrDefault(name);

    public string? Positional(int index) => index < _positional.Count ? _positional[index] : null;
}
