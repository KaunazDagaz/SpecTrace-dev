namespace SpecTrace.Pipeline;

public static class ExtractionSchema
{
    public const string Json =
        """
        {"type":"ARRAY","items":{"type":"OBJECT","properties":{"modality":{"type":"STRING","enum":["MUST","MUST_NOT","SHOULD","SHOULD_NOT","MAY"]},"quote":{"type":"STRING"},"testability":{"type":"STRING","enum":["testable","needs_human_decision","not_testable"]},"testability_note":{"type":"STRING","nullable":true}},"required":["modality","quote","testability"],"propertyOrdering":["modality","quote","testability","testability_note"]}}
        """;
}
