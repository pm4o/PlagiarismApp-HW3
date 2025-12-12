namespace AnalysisService.Domain;

public enum ReportType
{
    Plagiarism = 0,
    WordCloud = 1
}

public enum ReportStatus
{
    Pending = 0,
    Done = 1,
    Failed = 2
}

public sealed class Report
{
    public Guid Id { get; set; }

    public Guid WorkId { get; set; }
    public Work Work { get; set; } = default!;

    public ReportType Type { get; set; }
    public ReportStatus Status { get; set; }

    public string ResultJson { get; set; } = "{}";
    public string? ArtifactFileId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}