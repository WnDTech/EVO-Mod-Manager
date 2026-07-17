using Serilog;
using SkiaSharp;
using EVO.ModManager.Core.Services.Interfaces;
using System.Runtime.InteropServices;

namespace EVO.ModManager.Core.Services.Implementations;

public class TextureConverterService : ITextureConverterService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<TextureConverterService>();

    private const int TextureFormatBC7 = 10;
    private const int MaxMipCount = 11;
    private const uint DdsMagic = 0x20534444u;
    private const uint FourCC_DX10 = 0x30315844u;

    public async Task<TextureConvertResult> ConvertAsync(
        string sourceImagePath,
        string outputDirectory,
        CancellationToken ct = default)
    {
        var result = new TextureConvertResult();

        if (string.IsNullOrWhiteSpace(sourceImagePath))
        {
            result.Message = "No source image path provided.";
            return result;
        }

        if (!File.Exists(sourceImagePath))
        {
            result.Message = $"Source image not found: {sourceImagePath}";
            return result;
        }

        try
        {
            Directory.CreateDirectory(outputDirectory);

            var ext = Path.GetExtension(sourceImagePath).ToLowerInvariant();
            int width, height;
            byte[] mipData;

            switch (ext)
            {
                case ".dds":
                    (width, height, mipData) = await ProcessDdsAsync(sourceImagePath, ct);
                    break;
                case ".png":
                    (width, height, mipData) = ProcessPng(sourceImagePath);
                    break;
                default:
                    result.Message = $"Unsupported format: {ext}. Use .dds or .png.";
                    return result;
            }

            var baseName = Path.GetFileNameWithoutExtension(sourceImagePath);
            var texPath = Path.Combine(outputDirectory, $"{baseName}.texture");
            var mipsPath = Path.Combine(outputDirectory, $"{baseName}.texturemips");

            await WriteTextureHeaderAsync(texPath, width, height, ct);
            await File.WriteAllBytesAsync(mipsPath, mipData, ct);

            result.Success = true;
            result.TextureFilePath = texPath;
            result.TextureMipsFilePath = mipsPath;
            result.Message = $"Converted {sourceImagePath} -> {texPath}, {mipsPath}";

            Log.Information("Texture converted: {Src} -> {Tex}, {Mips}",
                sourceImagePath, texPath, mipsPath);
        }
        catch (OperationCanceledException)
        {
            result.Message = "Texture conversion cancelled.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Texture conversion failed for {Path}", sourceImagePath);
            result.Message = $"Conversion failed: {ex.Message}";
        }

        return result;
    }

    private static async Task<(int width, int height, byte[] mipData)> ProcessDdsAsync(
        string path, CancellationToken ct)
    {
        await using var fs = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);

        var magic = new byte[4];
        await fs.ReadExactlyAsync(magic, 0, 4, ct);
        if (BitConverter.ToUInt32(magic, 0) != DdsMagic)
            throw new InvalidDataException("Not a valid DDS file.");

        var header = new byte[124];
        await fs.ReadExactlyAsync(header, 0, 124, ct);

        var width = (int)BitConverter.ToUInt32(header, 12);
        var height = (int)BitConverter.ToUInt32(header, 8);

        var fourCC = BitConverter.ToUInt32(header, 80);
        if (fourCC == FourCC_DX10)
        {
            var dx10 = new byte[20];
            await fs.ReadExactlyAsync(dx10, 0, 20, ct);
        }

        var remaining = (int)(fs.Length - fs.Position);
        var mipData = new byte[remaining];
        await fs.ReadExactlyAsync(mipData, 0, remaining, ct);

        return (width, height, mipData);
    }

    private static (int width, int height, byte[] mipData) ProcessPng(string path)
    {
        using var original = SKBitmap.Decode(path);
        if (original == null)
            throw new InvalidDataException($"Failed to decode PNG: {path}");

        var width = NextPowerOfTwo(original.Width);
        var height = NextPowerOfTwo(original.Height);

        var mips = new List<SKBitmap>();
        try
        {
            var sampling = new SKSamplingOptions(SKFilterMode.Linear);
            var baseInfo = new SKImageInfo(width, height);
            var baseMip = original.Resize(baseInfo, sampling);
            mips.Add(baseMip);

            var w = width;
            var h = height;
            while ((w > 1 || h > 1) && mips.Count < MaxMipCount)
            {
                w = Math.Max(1, w / 2);
                h = Math.Max(1, h / 2);
                var mip = mips.Last().Resize(new SKImageInfo(w, h), sampling);
                mips.Add(mip);
            }

            using var ms = new MemoryStream();
            foreach (var mip in mips)
            {
                var pixelPtr = mip.GetPixels();
                var byteCount = (long)mip.RowBytes * mip.Height;
                var buffer = new byte[byteCount];
                Marshal.Copy(pixelPtr, buffer, 0, (int)byteCount);
                ms.Write(buffer, 0, buffer.Length);
            }

            return (width, height, ms.ToArray());
        }
        finally
        {
            foreach (var mip in mips)
                mip.Dispose();
        }
    }

    private static async Task WriteTextureHeaderAsync(
        string path, int width, int height, CancellationToken ct)
    {
        var header = new byte[12];
        BitConverter.TryWriteBytes(header.AsSpan(0, 4), TextureFormatBC7);
        BitConverter.TryWriteBytes(header.AsSpan(4, 4), width);
        BitConverter.TryWriteBytes(header.AsSpan(8, 4), height);
        await File.WriteAllBytesAsync(path, header, ct);
    }

    private static int NextPowerOfTwo(int value)
    {
        int v = value;
        v--;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        return v + 1;
    }
}
