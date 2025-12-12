namespace Gateway.Support;

public sealed class SubmitForm
{
    public string StudentId { get; init; } = default!;
    public string StudentName { get; init; } = default!;
    public string AssignmentId { get; init; } = default!;
    public IFormFile File { get; init; } = default!;
}