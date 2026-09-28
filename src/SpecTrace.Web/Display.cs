using System.Globalization;
using SpecTrace.Core;
using SpecTrace.Pipeline;

namespace SpecTrace.Web;

public static class Display
{
    public static string Utc(DateTimeOffset? at) =>
        at?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) ?? string.Empty;

    public static string Percent(double? share) =>
        share?.ToString("P1", CultureInfo.InvariantCulture) ?? "n/a";

    public static string Status(CoverageStatus status) => MatrixHtml.StatusLabel(status);

    public static string Review(ReviewStatus status) => status switch
    {
        ReviewStatus.Proposed => "proposed, not reviewed",
        ReviewStatus.Accepted => "accepted",
        ReviewStatus.Edited => "edited",
        ReviewStatus.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static string Queue(QueueDecision decision) => decision switch
    {
        QueueDecision.Testable => "testable",
        QueueDecision.NotTestable => "not testable",
        QueueDecision.Defer => "deferred",
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, null),
    };

    public static string Spell(CaseType type) => RunArtifacts.Spell(type);

    public static string Spell(Modality modality) => RunArtifacts.Spell(modality);
}
