using System.Text;

namespace SpecTrace.Pipeline.Tests;

public sealed class DocumentIntakeTests
{
    [Theory]
    [InlineData("rfc9999.txt", "rfc9999")]
    [InlineData("My Spec (draft 2).TXT", "my-spec-draft-2")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("..\\..\\cache\\evil.txt", "evil")]
    [InlineData("C:\\Users\\someone\\spec.txt", "spec")]
    [InlineData("CON.txt", "con-doc")]
    [InlineData("lpt1", "lpt1-doc")]
    [InlineData("спецификация.txt", "document")]
    [InlineData("", "document")]
    [InlineData(null, "document")]
    [InlineData(".txt", "document")]
    [InlineData("--a--b--.txt", "a-b")]
    [InlineData("a-very-long-name-that-goes-on-and-on-beyond-forty-characters.txt", "a-very-long-name-that-goes-on-and-on-bey")]
    public void AFileNameIsReducedToASafeSlugBeforeItNamesAnything(string? fileName, string expected)
    {
        var slug = DocumentIntake.Slug(fileName);

        Assert.Equal(expected, slug);
        Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", slug);
        Assert.True(slug.Length <= DocumentIntake.MaxNameLength);
    }

    [Fact]
    public void Utf8WithAByteOrderMarkIsAcceptedLikeEveryCurrentRfc()
    {
        DocumentIntake.Validate([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("Text MUST be accepted.\n\f\n")]);
    }

    [Fact]
    public async Task AnUploadIdenticalToACorpusFileIsThatCorpusDocumentWhateverItIsCalled()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch) with { Runs = Path.Combine(scratch.Path, "runs") };
        var bytes = await File.ReadAllBytesAsync(Repository.PathTo("corpus", "rfc10050.txt"));

        var document = await DocumentIntake.PrepareAsync(workspace, "whatever-it-is-called.txt", bytes, CancellationToken.None);

        Assert.Equal(DocumentSource.Corpus, document.Source);
        Assert.Equal("rfc10050", document.DocumentId);
        Assert.Equal(Repository.PathTo("corpus", "rfc10050.txt"), document.Path);
        Assert.Equal(workspace.Cache, document.CacheDirectory);
        Assert.Equal(await File.ReadAllTextAsync(Repository.PathTo("corpus", "rfc10050.txt")), document.Raw);
        Assert.False(Directory.Exists(workspace.Runs));
    }

    [Fact]
    public async Task AnyOtherUploadIsCopiedByteForByteUnderTheIgnoredRunsFolderAndACorpusNameIsNotReused()
    {
        using var scratch = new ScratchDirectory();
        var workspace = ReviewWorkspace.In(scratch);
        byte[] bytes = [.. await File.ReadAllBytesAsync(Repository.PathTo("corpus", "rfc6902.txt")), .. "\r\nAppended.\r\n"u8.ToArray()];

        var document = await DocumentIntake.PrepareAsync(workspace, "rfc6902.txt", bytes, CancellationToken.None);
        var again = await DocumentIntake.PrepareAsync(workspace, "rfc6902.txt", bytes, CancellationToken.None);

        Assert.Equal(DocumentSource.Upload, document.Source);
        Assert.Equal("rfc6902-upload", document.DocumentId);
        Assert.Equal(workspace.UploadCache, document.CacheDirectory);
        Assert.StartsWith(workspace.UploadsDirectory, document.Path, StringComparison.Ordinal);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(document.Path));
        Assert.Equal(document, again);
    }
}
