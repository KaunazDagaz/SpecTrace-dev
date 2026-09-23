namespace SpecTrace.Pipeline;

public static class GenerationSchema
{
    public const string Json =
        """
        {"type":"OBJECT","properties":{"cases":{"type":"ARRAY","items":{"type":"OBJECT","properties":{"title":{"type":"STRING"},"type":{"type":"STRING","enum":["positive","negative","boundary"]},"precondition":{"type":"STRING"},"input":{"type":"STRING"},"expected_result":{"type":"STRING"}},"required":["title","type","precondition","input","expected_result"],"propertyOrdering":["title","type","precondition","input","expected_result"]}},"blocked_reason":{"type":"STRING","nullable":true}},"required":["cases"],"propertyOrdering":["cases","blocked_reason"]}
        """;
}
