using System.Text;

namespace AnalysisService.Infra;

public static class TextTools
{
    public static async Task<string> TryExtractUtf8TextAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await CopyUpToAsync(stream, ms, maxBytes, ct);

        var bytes = ms.ToArray();
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static Dictionary<string, int> BuildWordFreq(string text, int maxWords)
    {
        var freq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                Flush();
            }
        }
        Flush();

        void Flush()
        {
            if (sb.Length <= 1) { sb.Clear(); return; }
            var token = sb.ToString();
            sb.Clear();

            if (IsStopWord(token)) return;

            if (freq.TryGetValue(token, out var c)) freq[token] = c + 1;
            else freq[token] = 1;
        }

        static bool IsStopWord(string w)
        {
            return w is "the" or "and" or "or" or "a" or "an" or "to" or "of" or "in" or "on" or "for" or "with"
                or "и" or "а" or "но" or "или" or "что" or "это" or "как" or "на" or "в" or "к" or "по" or "за"
                or "от" or "до" or "из" or "у" or "мы" or "вы" or "они" or "он" or "она" or "оно";
        }

        return freq
            .OrderByDescending(kv => kv.Value)
            .Take(maxWords)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    public static string ExpandForWordCloud(Dictionary<string, int> freq, int maxTotalTokens)
    {
        var tokens = new List<string>(Math.Min(maxTotalTokens, 2000));

        foreach (var kv in freq.OrderByDescending(x => x.Value))
        {
            var repeats = Math.Clamp(kv.Value, 1, 30);
            for (int i = 0; i < repeats && tokens.Count < maxTotalTokens; i++)
                tokens.Add(kv.Key);

            if (tokens.Count >= maxTotalTokens) break;
        }

        return string.Join(' ', tokens);
    }

    private static async Task CopyUpToAsync(Stream src, Stream dst, int maxBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        var remaining = maxBytes;

        while (remaining > 0)
        {
            var read = await src.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), ct);
            if (read <= 0) break;

            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            remaining -= read;
        }
    }
}
