using System.Runtime.InteropServices;
using ACEvo.Package;
using ACEvo.Package.Hashing;

namespace EVO.ModManager.Core.Services.Implementations;

public class KspkgBuilder : IDisposable
{
    private readonly string _outputPath;
    private readonly FileStream _stream;
    private readonly List<PendingFile> _files = new();
    private long _currentOffset;

    private const ulong KEY = 0x9F9721A97D1135C1;
    private const long FILE_TABLE_SIZE = 0x2000000; // 32MB
    private const int ENTRY_SIZE = 0x100;  // 256 bytes per entry

    public KspkgBuilder(string outputPath)
    {
        _outputPath = outputPath;
        _stream = File.Create(outputPath);
    }

    public void AddFile(string gamePath, byte[] data)
    {
        var normalizedPath = gamePath.ToLowerInvariant().Replace("/", "\\");
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(normalizedPath);
        if (nameBytes.Length > 247)
            throw new InvalidOperationException($"Path too long: {normalizedPath}");

        _files.Add(new PendingFile
        {
            GamePath = normalizedPath,
            NameBytes = nameBytes,
            PathHash = FNV1A64.Hash(System.Text.Encoding.Unicode.GetBytes(normalizedPath)),
            Data = data,
            Offset = _currentOffset
        });
        _currentOffset += data.Length;
    }

    public void AddDirectory(string gamePath)
    {
        var normalizedPath = gamePath.ToLowerInvariant().Replace("/", "\\");
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(normalizedPath);
        if (nameBytes.Length > 247)
            return;

        _files.Add(new PendingFile
        {
            GamePath = normalizedPath,
            NameBytes = nameBytes,
            PathHash = FNV1A64.Hash(System.Text.Encoding.Unicode.GetBytes(normalizedPath)),
            Data = Array.Empty<byte>(),
            Offset = _currentOffset,
            IsDirectory = true
        });
    }

    public void Build()
    {
        // Step 1: Write all file data, XOR encrypted
        foreach (var file in _files.OrderBy(f => f.Offset))
        {
            if (file.Data.Length > 0)
            {
                var encrypted = new byte[file.Data.Length];
                file.Data.CopyTo(encrypted, 0);
                Xor(encrypted);
                _stream.Write(encrypted);
            }
        }

        // Step 2: Write file table (last 32MB)
        long tableStart = _stream.Position;
        byte[] table = new byte[FILE_TABLE_SIZE];
        int entryIndex = 0;

        foreach (var file in _files)
        {
            int offset = entryIndex * ENTRY_SIZE;
            var span = table.AsSpan(offset);

            // Path hash (ulong) at offset 0
            BitConverter.TryWriteBytes(span, file.PathHash);

            // Offset (ulong) at offset 8
            BitConverter.TryWriteBytes(span[8..], (ulong)file.Offset);

            // Size (int) at offset 16
            BitConverter.TryWriteBytes(span[16..], file.Data.Length);

            // Flags (int) at offset 20
            int flags = file.IsDirectory ? 0x20 : 0x10; // Encrypted=0x10, Directory=0x20
            BitConverter.TryWriteBytes(span[20..], flags);

            // File name length (int) at offset 24
            BitConverter.TryWriteBytes(span[24..], file.NameBytes.Length);

            // File name (ASCII) at offset 28
            file.NameBytes.CopyTo(span[28..]);

            entryIndex++;
        }

        // XOR encrypt the file table
        Xor(table);

        // Write the file table at the end
        _stream.Write(table);
    }

    private void Xor(byte[] data)
    {
        Span<byte> span = data;
        while (span.Length >= 8)
        {
            Span<ulong> ulongs = MemoryMarshal.Cast<byte, ulong>(span);
            ulongs[0] ^= KEY;
            span = span[8..];
        }
        while (span.Length > 0)
        {
            span[0] ^= unchecked((byte)KEY);
            span = span[1..];
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
    }

    private class PendingFile
    {
        public string GamePath;
        public byte[] NameBytes;
        public ulong PathHash;
        public byte[] Data;
        public long Offset;
        public bool IsDirectory;
    }
}

