namespace HackerOs.MarketPlace;

/// <summary>Arguments of one command: <c>--name value</c> pairs, then positional values.</summary>
internal sealed class CommandLine
{
    private readonly Dictionary<string, string> options;

    private CommandLine(Dictionary<string, string> options, List<string> positional)
    {
        this.options = options;
        Positional = positional;
    }

    /// <summary>Gets the values that are not options, in order.</summary>
    public IReadOnlyList<string> Positional { get; }

    /// <summary>Parses the arguments that follow the command name.</summary>
    /// <exception cref="ArgumentException">An option has no value or appears twice.</exception>
    public static CommandLine Parse(ReadOnlySpan<string> args)
    {
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        List<string> positional = [];
        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(arg);
                continue;
            }

            if (index + 1 >= args.Length || !options.TryAdd(arg[2..], args[++index]))
            {
                throw new ArgumentException($"The option {arg} needs one value and may appear once.");
            }
        }

        return new CommandLine(options, positional);
    }

    /// <summary>Gets a required option.</summary>
    /// <exception cref="ArgumentException">The option is missing.</exception>
    public string Required(string name) =>
        Optional(name) ?? throw new ArgumentException($"Missing required option --{name}.");

    /// <summary>Gets an option, or <see langword="null"/>.</summary>
    public string? Optional(string name) =>
        options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
