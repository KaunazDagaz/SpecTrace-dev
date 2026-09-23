namespace SpecTrace.Core;

public sealed record DecisionQueueItem
{
    public const string NoSingleSection = "(multiple locations)";

    public DecisionQueueItem(string id, string quote, string section, string question, string? resolution)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(quote);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        Id = id;
        Quote = quote;
        Section = section;
        Question = question;
        Resolution = resolution;
    }

    public string Id { get; }

    public string Quote { get; }

    public string Section { get; }

    public string Question { get; }

    public string? Resolution { get; }
}
