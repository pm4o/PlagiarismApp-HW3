using System.Net.Http.Json;

namespace AnalysisService.Infra;

public sealed class QuickChartClient
{
    private readonly HttpClient _http;

    public QuickChartClient(HttpClient http) => _http = http;

    public async Task<byte[]> BuildWordCloudPngAsync(string text, int width, int height, CancellationToken ct)
    {
        var payload = new
        {
            format = "png",
            width,
            height,
            text
        };

        using var resp = await _http.PostAsJsonAsync("/wordcloud", payload, ct);
        if (resp.IsSuccessStatusCode)
            return await resp.Content.ReadAsByteArrayAsync(ct);

        var url = $"/wordcloud?format=png&width={width}&height={height}&text={Uri.EscapeDataString(text)}";
        using var resp2 = await _http.GetAsync(url, ct);
        resp2.EnsureSuccessStatusCode();
        return await resp2.Content.ReadAsByteArrayAsync(ct);
    }
}