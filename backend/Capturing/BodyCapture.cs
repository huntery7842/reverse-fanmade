using System.Text;

namespace ReVerse.Capture.Capturing;

public sealed record BodyCapture(string Encoding, string Data, int CapturedBytes, long TotalBytes,
    bool Truncated, string? ReadError)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static BodyCapture Empty => Create([], 0);

    public static BodyCapture Create(byte[] bytes, long total, string? error = null, bool forceBinary = false)
    {
        if (!forceBinary)
        {
            try
            {
                var text = StrictUtf8.GetString(bytes);
                if (!text.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
                    return new("utf8", text, bytes.Length, total, total > bytes.Length, error);
            }
            catch (DecoderFallbackException)
            {
            }
        }
        return new("base64", Convert.ToBase64String(bytes), bytes.Length, total, total > bytes.Length, error);
    }

    public static async Task<BodyCapture> ReadAsync(Stream stream, int limit, bool forceBinary, CancellationToken cancellationToken)
    {
        using var prefix = new MemoryStream();
        var buffer = new byte[32 * 1024];
        long total = 0;
        string? error = null;
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                total += read;
                var keep = Math.Min(read, limit - (int)prefix.Length);
                if (keep > 0) prefix.Write(buffer, 0, keep);
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or BadHttpRequestException)
        {
            error = exception.GetType().Name;
        }
        return Create(prefix.ToArray(), total, error, forceBinary);
    }
}

public static class BodyContentType
{
    public static bool IsBinary(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        var mediaType = contentType.Split(';', 2)[0].Trim();
        return !(mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase));
    }
}