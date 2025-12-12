namespace AnalysisService.Domain;

public enum WorkStatus
{
    Created = 0,
    FileUploadFailed = 1,
    Analyzing = 2,
    Done = 3,
    Failed = 4
}

public sealed class Work
{
    public Guid Id { get; set; }

    public string StudentId { get; set; } = default!;
    public string StudentName { get; set; } = default!;
    public string AssignmentId { get; set; } = default!;

    public DateTimeOffset SubmittedAtUtc { get; set; }

    public string? FileId { get; set; }
    public string? FileHashSha256 { get; set; }

    public WorkStatus Status { get; set; }

    public bool PlagiarismFlag { get; set; }
    public Guid? PlagiarismSourceWorkId { get; set; }
}