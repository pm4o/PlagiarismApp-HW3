namespace AnalysisService.Domain;

public enum IdempotencyStatus
{
    InProgress = 0,
    Completed = 1
}

public sealed class SubmissionIdempotency
{
    public string IdempotencyKey { get; set; } = default!;
    public string RequestHash { get; set; } = default!;
    public IdempotencyStatus Status { get; set; }

    public Guid WorkId { get; set; }

    public string? ResponseJson { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}