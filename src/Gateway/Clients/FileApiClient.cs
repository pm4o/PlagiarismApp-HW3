using System.Net.Http.Json;

namespace Gateway.Clients;

public sealed class FileApiClient
{
    private readonly HttpClient _http;

    public FileApiClient(HttpClient http) => _http = http;

    public async Task<HttpResponseMessage> UploadAsync(string tempPath, string originalFileName, CancellationToken ct)
    {
        await using var fs = File.OpenRead(tempPath);

        using var content = new MultipartFormDataContent();
        var sc = new StreamContent(fs);
        content.Add(sc, "file", originalFileName);

        return await _http.PostAsync("/files", content, ct);
    }

    public async Task<HttpResponseMessage> DownloadAsync(string fileId, CancellationToken ct)
        => await _http.GetAsync($"/files/{fileId}", HttpCompletionOption.ResponseHeadersRead, ct);

    public async Task<FileUploadResponse?> ReadUploadResponseAsync(HttpResponseMessage resp, CancellationToken ct)
        => await resp.Content.ReadFromJsonAsync<FileUploadResponse>(cancellationToken: ct);

    public sealed class FileUploadResponse
    {
        public string? FileId { get; set; }
        public string? OriginalName { get; set; }
        public string? ContentType { get; set; }
        public long Size { get; set; }
    }
}