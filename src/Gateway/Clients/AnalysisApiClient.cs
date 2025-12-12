using System.Net.Http.Json;

namespace Gateway.Clients;

public sealed class AnalysisApiClient
{
    private readonly HttpClient _http;

    public AnalysisApiClient(HttpClient http) => _http = http;

    public async Task<HttpResponseMessage> StartAsync(object payload, CancellationToken ct)
        => await _http.PostAsJsonAsync("/internal/submissions/start", payload, ct);

    public async Task<HttpResponseMessage> AttachFileAsync(Guid workId, object payload, CancellationToken ct)
        => await _http.PostAsJsonAsync($"/internal/works/{workId}/attach-file", payload, ct);

    public async Task<HttpResponseMessage> UploadFailedAsync(Guid workId, CancellationToken ct)
        => await _http.PostAsync($"/internal/works/{workId}/upload-failed", content: null, ct);

    public async Task<HttpResponseMessage> AnalyzeAsync(Guid workId, object payload, CancellationToken ct)
        => await _http.PostAsJsonAsync($"/internal/works/{workId}/analyze", payload, ct);

    public async Task<HttpResponseMessage> GetWorkAsync(Guid workId, CancellationToken ct)
        => await _http.GetAsync($"/works/{workId}", ct);

    public async Task<HttpResponseMessage> GetReportsAsync(Guid workId, CancellationToken ct)
        => await _http.GetAsync($"/works/{workId}/reports", ct);
}