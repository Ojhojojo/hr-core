using SkiaSharp;

namespace HrCore.Infrastructure;

public sealed record ProcessedFile(byte[] Bytes, string FileName, string ContentType)
{
    public long ByteSize => Bytes.LongLength;
}

public static class PhotoShrink
{
    public const int MaxLongEdge = 1920;
    public const int JpegQuality = 70;
    public const long MaxImageBytesAfterShrink = 5 * 1024 * 1024;
    public const long MaxPdfBytes = 15 * 1024 * 1024;
    public const long MaxOtherBytes = 15 * 1024 * 1024;

    private static readonly SKSamplingOptions HighQualitySampling =
        new(SKCubicResampler.Mitchell);

    public static async Task<ProcessedFile> ProcessAsync(
        Stream content,
        string fileName,
        string? contentType,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Process(buffer.ToArray(), fileName, contentType);
    }

    public static async Task<ProcessedFile> ProcessFormFileAsync(IFormFile upload, CancellationToken cancellationToken)
    {
        await using var stream = upload.OpenReadStream();
        return await ProcessAsync(stream, Path.GetFileName(upload.FileName), upload.ContentType, cancellationToken);
    }

    public static ProcessedFile Process(byte[] original, string fileName, string? contentType)
    {
        ArgumentNullException.ThrowIfNull(original);
        var name = string.IsNullOrWhiteSpace(fileName) ? "file" : Path.GetFileName(fileName);
        var type = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim();

        if (LooksLikePdf(original, type, name))
        {
            if (original.LongLength > MaxPdfBytes)
            {
                throw new InvalidOperationException("That PDF is too large (15 MB max).");
            }

            return new ProcessedFile(original, EnsureExtension(name, ".pdf"), "application/pdf");
        }

        if (LooksLikeImage(original, type, name))
        {
            return ShrinkImage(original, name, type);
        }

        if (original.LongLength > MaxOtherBytes)
        {
            throw new InvalidOperationException("That file is too large (15 MB max).");
        }

        return new ProcessedFile(original, name, type);
    }

    private static ProcessedFile ShrinkImage(byte[] original, string fileName, string contentType)
    {
        try
        {
            using var oriented = DecodeOrientated(original);
            if (oriented is null)
            {
                return FallbackUnreadableImage(original, fileName, contentType);
            }

            using var resized = ResizeToMaxLongEdge(oriented);
            using var flattened = FlattenOntoWhite(resized);
            using var encoded = flattened.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
            if (encoded is null)
            {
                return FallbackUnreadableImage(original, fileName, contentType);
            }

            var bytes = encoded.ToArray();
            if (bytes.LongLength > MaxImageBytesAfterShrink)
            {
                throw new InvalidOperationException("That photo is still too large after shrinking (5 MB max).");
            }

            var baseName = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "photo";
            }

            return new ProcessedFile(bytes, baseName + ".jpg", "image/jpeg");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception)
        {
            return FallbackUnreadableImage(original, fileName, contentType);
        }
    }

    /// <summary>Decode pixels and apply EXIF / encoded origin so phone photos come out upright.</summary>
    private static SKBitmap? DecodeOrientated(byte[] original)
    {
        using var data = SKData.CreateCopy(original);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            return null;
        }

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0)
        {
            return null;
        }

        var decoded = new SKBitmap(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var result = codec.GetPixels(decoded.Info, decoded.GetPixels());
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
        {
            decoded.Dispose();
            return null;
        }

        return ApplyEncodedOrigin(decoded, codec.EncodedOrigin);
    }

    internal static SKBitmap ApplyEncodedOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
        {
            return source;
        }

        var swap = origin is SKEncodedOrigin.LeftTop
            or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom
            or SKEncodedOrigin.LeftBottom;

        var width = swap ? source.Height : source.Width;
        var height = swap ? source.Width : source.Height;
        var result = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(result))
        {
            canvas.Clear(SKColors.Transparent);
            switch (origin)
            {
                case SKEncodedOrigin.TopRight: // 2 mirror horizontal
                    canvas.Translate(width, 0);
                    canvas.Scale(-1, 1);
                    break;
                case SKEncodedOrigin.BottomRight: // 3 rotate 180
                    canvas.Translate(width, height);
                    canvas.RotateDegrees(180);
                    break;
                case SKEncodedOrigin.BottomLeft: // 4 mirror vertical
                    canvas.Translate(0, height);
                    canvas.Scale(1, -1);
                    break;
                case SKEncodedOrigin.LeftTop: // 5 mirror horizontal then rotate 90 CW
                    canvas.Translate(width, 0);
                    canvas.RotateDegrees(90);
                    canvas.Scale(1, -1);
                    canvas.Translate(-source.Width, 0);
                    break;
                case SKEncodedOrigin.RightTop: // 6 rotate 90 CW
                    canvas.Translate(width, 0);
                    canvas.RotateDegrees(90);
                    break;
                case SKEncodedOrigin.RightBottom: // 7 mirror horizontal then rotate 90 CCW
                    canvas.Translate(0, height);
                    canvas.RotateDegrees(-90);
                    canvas.Scale(1, -1);
                    canvas.Translate(-source.Width, 0);
                    break;
                case SKEncodedOrigin.LeftBottom: // 8 rotate 90 CCW
                    canvas.Translate(0, height);
                    canvas.RotateDegrees(-90);
                    break;
            }

            canvas.DrawBitmap(source, 0, 0, HighQualitySampling);
        }

        source.Dispose();
        return result;
    }

    private static SKBitmap ResizeToMaxLongEdge(SKBitmap source)
    {
        var longEdge = Math.Max(source.Width, source.Height);
        if (longEdge <= MaxLongEdge)
        {
            return source.Copy() ?? throw new InvalidOperationException("Could not copy bitmap.");
        }

        var scale = MaxLongEdge / (float)longEdge;
        var newWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        var newHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        var dest = new SKBitmap(newWidth, newHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (source.ScalePixels(dest, HighQualitySampling))
        {
            return dest;
        }

        dest.Dispose();
        var fallback = new SKBitmap(newWidth, newHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(fallback);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, new SKRect(0, 0, newWidth, newHeight), HighQualitySampling);
        return fallback;
    }

    private static SKBitmap FlattenOntoWhite(SKBitmap source)
    {
        var flat = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(flat);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(source, 0, 0, HighQualitySampling);
        return flat;
    }

    private static ProcessedFile FallbackUnreadableImage(byte[] original, string fileName, string contentType)
    {
        if (original.LongLength > MaxImageBytesAfterShrink)
        {
            throw new InvalidOperationException("That photo is too large (5 MB max).");
        }

        return new ProcessedFile(original, fileName, contentType);
    }

    internal static bool LooksLikePdf(ReadOnlySpan<byte> bytes, string contentType, string fileName)
    {
        if (HasMagic(bytes, "%PDF"u8))
        {
            return true;
        }

        if (HasMagic(bytes, [0xFF, 0xD8, 0xFF]) || HasPngMagic(bytes) || HasGifMagic(bytes) || HasWebpMagic(bytes))
        {
            return false;
        }

        return contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool LooksLikeImage(ReadOnlySpan<byte> bytes, string contentType, string fileName)
    {
        if (HasMagic(bytes, [0xFF, 0xD8, 0xFF])
            || HasPngMagic(bytes)
            || HasGifMagic(bytes)
            || HasWebpMagic(bytes)
            || HasMagic(bytes, "BM"u8))
        {
            return true;
        }

        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp" or ".heic" or ".heif";
    }

    private static bool HasPngMagic(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G';

    private static bool HasGifMagic(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 6 && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F';

    private static bool HasWebpMagic(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 12
        && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
        && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P';

    private static bool HasMagic(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> magic)
    {
        if (bytes.Length < magic.Length)
        {
            return false;
        }

        return bytes[..magic.Length].SequenceEqual(magic);
    }

    private static string EnsureExtension(string fileName, string extension)
    {
        if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            return fileName;
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrWhiteSpace(baseName) ? "file" + extension : baseName + extension;
    }
}
