using System.Text.Json;

namespace FileService.Storage;

public sealed class LocalFileStore
{
    private readonly string _root;

    public LocalFileStore(string rootPath)
    {
        _root = rootPath;
        Directory.CreateDirectory(_root);
    }

    public string GetBinPath(string fileId) => Path.Combine(_root, $"{fileId}.bin");
    public string GetMetaPath(string fileId) => Path.Combine(_root, $"{fileId}.json");

    public async Task<FileMeta> SaveMultipartAsync(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
            throw new ArgumentException("file is required");

        var fileId = Guid.NewGuid().ToString("N");
        var originalName = SafeFileName(file.FileName);
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;

        var binPath = GetBinPath(fileId);
        var metaPath = GetMetaPath(fileId);

        await using (var fs = File.Create(binPath))
        {
            await file.CopyToAsync(fs, ct);
        }

        var meta = new FileMeta
        {
            FileId = fileId,
            OriginalName = originalName,
            ContentType = contentType,
            Size = file.Length,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta, JsonOptions), ct);
        return meta;
    }

    public async Task<FileMeta> SaveRawAsync(Stream body, string contentType, string originalName, CancellationToken ct)
    {
        if (body is null)
            throw new ArgumentException("body is required");

        var fileId = Guid.NewGuid().ToString("N");
        var safeName = SafeFileName(originalName);
        var ctFinal = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;

        var binPath = GetBinPath(fileId);
        var metaPath = GetMetaPath(fileId);

        await using (var fs = File.Create(binPath))
        {
            await body.CopyToAsync(fs, ct);
        }

        var size = new FileInfo(binPath).Length;

        var meta = new FileMeta
        {
            FileId = fileId,
            OriginalName = safeName,
            ContentType = ctFinal,
            Size = size,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta, JsonOptions), ct);
        return meta;
    }

    public bool Exists(string fileId) => File.Exists(GetBinPath(fileId));

    public FileMeta? TryReadMeta(string fileId)
    {
        var metaPath = GetMetaPath(fileId);
        if (!File.Exists(metaPath)) return null;

        try
        {
            var json = File.ReadAllText(metaPath);
            return JsonSerializer.Deserialize<FileMeta>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public (string binPath, FileMeta? meta) GetForDownload(string fileId)
    {
        var binPath = GetBinPath(fileId);
        if (!File.Exists(binPath))
            throw new FileNotFoundException("file not found", binPath);

        var meta = TryReadMeta(fileId);
        return (binPath, meta);
    }

    private static string SafeFileName(string name) => Path.GetFileName(string.IsNullOrWhiteSpace(name) ? "file.bin" : name);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}
