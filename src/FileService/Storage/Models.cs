namespace FileService.Storage;

public sealed class FileMeta
{
    public string FileId { get; set; } = default!;
    public string OriginalName { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long Size { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}