namespace AnalysisService.Application;

public sealed record SubmissionStartRequest(
    string IdempotencyKey,
    string RequestHash,
    string StudentId,
    string StudentName,
    string AssignmentId
);

public sealed record SubmissionStartResponse(
    string Kind,
    Guid WorkId,
    string WorkStatus,
    string? ExistingFileId,
    string? ResponseJson
);

public sealed record AttachFileRequest(
    string IdempotencyKey,
    string FileId
);

public sealed record AnalyzeRequest(
    string IdempotencyKey
);

public sealed record SubmissionResult(
    Guid WorkId,
    string Status,
    bool Plagiarism,
    Guid? PlagiarismSourceWorkId,
    IReadOnlyList<ReportDto> Reports
);

public sealed record ReportDto(
    Guid ReportId,
    string Type,
    string Status,
    string ResultJson,
    string? ArtifactFileId,
    DateTimeOffset CreatedAtUtc
);