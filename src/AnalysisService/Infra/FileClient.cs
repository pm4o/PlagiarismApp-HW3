using System.Net.Http.Json;

namespace AnalysisService.Infra;

public sealed class FileClient
{
    private readonly HttpClient _http;

    public FileClient(HttpClient http) => _http = http;

    public async Task<Stream> DownloadAsync(string fileId, CancellationToken ct)
    {
        var resp = await _http.GetAsync($"/files/{fileId}", HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStreamAsync(ct);
    }

    public async Task<string> UploadRawAsync(byte[] bytes, string contentType, string originalName, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"/files/raw?contentType={Uri.EscapeDataString(contentType)}&originalName={Uri.EscapeDataString(originalName)}"
        );

        req.Content = new ByteArrayContent(bytes);
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<UploadRawResponse>(cancellationToken: ct);
        if (dto is null || string.IsNullOrWhiteSpace(dto.FileId))
            throw new InvalidOperationException("FileService returned invalid response");

        return dto.FileId!;
    }

    private sealed class UploadRawResponse
    {
        public string? FileId { get; set; }
        public string? OriginalName { get; set; }
        public string? ContentType { get; set; }
        public long Size { get; set; }
    }
}