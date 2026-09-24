using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SpecTrace.Pipeline;

public sealed class PromptFile
{
    private PromptFile(string name, string text, string sha256)
    {
        Name = name;
        Text = text;
        Sha256 = sha256;
    }

    public string Name { get; }

    public string Text { get; }

    public string Sha256 { get; }

    public static PromptFile Extraction { get; } = Load("extract.system.md");

    public static PromptFile Generation { get; } = Load("generate.system.md");

    public static PromptFile Load(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var assembly = typeof(PromptFile).Assembly;
        var resource = $"{assembly.GetName().Name}.Prompts.{name}";

        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"Prompt '{name}' is not embedded in {assembly.GetName().Name}. Expected resource "
                + $"'{resource}'. Available: {string.Join(", ", assembly.GetManifestResourceNames())}");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = Normalize(reader.ReadToEnd());

        return new PromptFile(name, text, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
}
