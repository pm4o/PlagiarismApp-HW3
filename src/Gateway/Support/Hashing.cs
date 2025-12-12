using System.Security.Cryptography;
using System.Text;

namespace Gateway.Support;

public static class Hashing
{
    public static string Sha256Hex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    public static async Task<(string fileSha256Hex, string tempPath)> SaveToTempAndHashAsync(Stream input, CancellationToken ct)
    {
        var tmpPath = Path.Combine(Path.GetTempPath(), $"upload_{Guid.NewGuid():N}.bin");

        using var sha = SHA256.Create();

        await using var output = File.Create(tmpPath);

        var buffer = new byte[81920];
        int read;

        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            sha.TransformBlock(buffer, 0, read, null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var fileSha = Convert.ToHexString(sha.Hash!);

        return (fileSha, tmpPath);
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignored
        }
    }
}