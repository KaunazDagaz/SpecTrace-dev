using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SpecTrace.Pipeline;

public sealed class UploadRejectedException : Exception
{
    public UploadRejectedException(string message)
        : base(message)
    {
    }
}

public sealed record PreparedDocument(
    string Path,
    string DocumentId,
    DocumentSource Source,
    string Sha256,
    string Raw,
    string CacheDirectory);

public static class DocumentIntake
{
    public const int MaxBytes = 64 * 1024;

    public const int MaxNameLength = 40;

    public const string UploadSuffix = "-upload";

    private const string FallbackName = "document";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly HashSet<string> DeviceNames = new(StringComparer.Ordinal)
    {
        "con", "prn", "aux", "nul",
        "com0", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt0", "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    public static string? SizeProblem(long length) => length switch
    {
        0 => "The file is empty.",
        > MaxBytes =>
            $"The file holds {length.ToString("N0", CultureInfo.InvariantCulture)} bytes; the limit is "
            + $"{MaxBytes.ToString("N0", CultureInfo.InvariantCulture)} bytes (64 KiB), which keeps one live run well inside the daily request quota.",
        _ => null,
    };

    public static void Validate(ReadOnlySpan<byte> bytes)
    {
        if (SizeProblem(bytes.Length) is { } problem)
        {
            throw new UploadRejectedException(problem);
        }

        string text;

        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new UploadRejectedException(
                "The file is not UTF-8 plain text. Only UTF-8 text is accepted; PDF, Word, HTML exports and other binary formats are not.");
        }

        if (text.Contains('\0', StringComparison.Ordinal))
        {
            throw new UploadRejectedException("The file contains a NUL character, so it is not plain text.");
        }
    }

    public static string Slug(string? fileName)
    {
        var name = (fileName ?? string.Empty).Split('/', '\\')[^1];
        var extension = name.LastIndexOf('.');

        if (extension >= 0)
        {
            name = name[..extension];
        }

        var slug = new StringBuilder(name.Length);

        foreach (var character in name.ToLowerInvariant())
        {
            var keep = character is >= 'a' and <= 'z' or >= '0' and <= '9';

            if (keep)
            {
                slug.Append(character);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var result = slug.ToString().Trim('-');

        if (result.Length > MaxNameLength)
        {
            result = result[..MaxNameLength].TrimEnd('-');
        }

        if (result.Length == 0)
        {
            return FallbackName;
        }

        return DeviceNames.Contains(result) ? $"{result}-doc" : result;
    }

    public static async Task<PreparedDocument> PrepareAsync(
        Workspace workspace,
        string? fileName,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(bytes);

        Validate(bytes);

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var corpusFiles = Directory.Exists(workspace.Corpus)
            ? Directory.EnumerateFiles(workspace.Corpus, "*.txt").Order(StringComparer.Ordinal).ToList()
            : [];

        foreach (var corpusFile in corpusFiles)
        {
            if (new FileInfo(corpusFile).Length == bytes.Length
                && (await File.ReadAllBytesAsync(corpusFile, cancellationToken).ConfigureAwait(false)).AsSpan().SequenceEqual(bytes))
            {
                return new PreparedDocument(
                    corpusFile,
                    ExtractionRun.DocumentIdFor(corpusFile),
                    DocumentSource.Corpus,
                    sha256,
                    await File.ReadAllTextAsync(corpusFile, cancellationToken).ConfigureAwait(false),
                    workspace.Cache);
            }
        }

        var documentId = Slug(fileName);

        if (corpusFiles.Any(corpusFile => ExtractionRun.DocumentIdFor(corpusFile) == documentId))
        {
            documentId += UploadSuffix;
        }

        var path = workspace.UploadedDocument(sha256, documentId);

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

            var temporary = $"{path}.{Environment.ProcessId}.tmp";

            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        else if (!(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)).AsSpan().SequenceEqual(bytes))
        {
            throw new UploadRejectedException($"'{path}' already holds different bytes under the same SHA-256 prefix; nothing was overwritten.");
        }

        return new PreparedDocument(
            path,
            documentId,
            DocumentSource.Upload,
            sha256,
            await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false),
            workspace.UploadCache);
    }
}
