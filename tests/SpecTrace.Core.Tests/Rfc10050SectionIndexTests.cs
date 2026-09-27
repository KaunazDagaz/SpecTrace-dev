namespace SpecTrace.Core.Tests;

public sealed class Rfc10050SectionIndexTests
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "corpus", "rfc10050.txt");

    private static readonly Lazy<string> LazyRaw = new(() => File.ReadAllText(FilePath));

    private static string Raw => LazyRaw.Value;

    [Fact]
    public void TheCommittedFileKeepsTheByteOrderMarkTheRfcEditorPublishedAndReadingItDropsIt()
    {
        var bytes = File.ReadAllBytes(FilePath);

        Assert.Equal(31_890, bytes.Length);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.NotEqual('﻿', Raw[0]);
        Assert.Equal(31_881, Raw.Length);
    }

    [Fact]
    public void TheIndexHoldsExactlyTheSectionsOfTheTableOfContentsInOrder()
    {
        var index = SectionIndex.Build(Raw);

        Assert.Equal(
            ["1", "2", "3", "3.1", "3.2", "3.3", "3.4", "4", "5", "6", "6.1", "6.2", "A", "A.1", "A.2", "A.3", "A.4"],
            index.Headers.Select(header => header.Number));
        Assert.Equal("Example Profile", index.Headers.Single(header => header.Number == "A").Title);
        Assert.Equal("Supported Properties Example", index.Headers[^1].Title);
    }

    [Theory]
    [InlineData("This document defines the \"JSContact Profiles\" registry, an IANA registry", SectionIndex.FrontMatter)]
    [InlineData("Section 1.7.4 of [RFC9553] outlines how JSContact implementations may ignore unknown JSContact elements", "1")]
    [InlineData("\"OPTIONAL\" in this document are to be interpreted as described in BCP 14", "2")]
    [InlineData("A profile MAY define additional restrictions for these elements as outlined in Section 3.3, but a profile MUST NOT loosen restrictions.", "3")]
    [InlineData("It MUST start with an alphabetic character", "3.1")]
    [InlineData("The version MUST be a positive integer", "3.2")]
    [InlineData("All profiles MUST support \"@type\" and \"version\"; therefore, profiles MUST NOT include entries for these properties.", "3.3")]
    [InlineData("A Card object complies with the profile if all its properties are part of the supported properties", "3.4")]
    [InlineData("The name MUST be unique among all registered profiles and MUST comply with the definitions in Section 3.1.", "4")]
    [InlineData("This document does not provide any new security considerations.", "5")]
    [InlineData("Guidelines for Writing an IANA Considerations Section in RFCs", "6.2")]
    [InlineData("{ \"kind\": \"surname\", \"value\": \"宮崎\" }", "A.2")]
    [InlineData("Name: jscontact-simple-example", "A.3")]
    public void ASampledSpanReportsTheSectionThatContainsIt(string quote, string expectedSection)
    {
        var resolution = NormalizedDocument.Create(Raw).Resolve(quote);

        Assert.Equal(Verification.Exact, resolution.Verification);
        Assert.Equal(expectedSection, SectionIndex.Build(Raw).SectionFor(resolution.Span!.Value));
    }
}
