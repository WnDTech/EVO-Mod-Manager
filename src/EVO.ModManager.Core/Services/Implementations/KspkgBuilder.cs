using System.Runtime.InteropServices;
using ACEvo.Package.Hashing;

namespace EVO.ModManager.Core.Services.Implementations;

public class KspkgBuilder : IDisposable
{
    private readonly string _outputPath;
    private readonly Stream _stream;
    private readonly List<PendingFile> _files = new();
    private long _currentOffset;

    private const ulong KEY = 0x9F9721A97D1135C1;
    private const long FILE_TABLE_SIZE = 0x4000000; // 64MB (required for ACE EVO v0.7+)
    private const int ENTRY_SIZE = 0x100;  // 256 bytes per entry

    // ACE EVO v0.7+ uses 64MB file table = 262144 max entries
    

    public KspkgBuilder(string outputPath)
    {
        _outputPath = outputPath;
        _stream = File.Create(outputPath);
    }

    public void AddFile(string gamePath, byte[] data)
    {
        var normalized = gamePath.ToLowerInvariant().Replace("/", "\\");
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(normalized);
        if (nameBytes.Length > 0xDF)
            throw new InvalidOperationException($"Path too long: {normalized}");

        _files.Add(new PendingFile
        {
            NameBytes = nameBytes,
            NameLength = (short)nameBytes.Length,
            PathHash = FNV1A64.Hash(System.Text.Encoding.Unicode.GetBytes(normalized)),
            Data = data,
            Offset = _currentOffset,
            Flags = (ushort)0x10 // Encrypted
        });
        _currentOffset += data.Length;
    }

    public void AddDirectory(string gamePath)
    {
        var normalized = gamePath.ToLowerInvariant().Replace("/", "\\");
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(normalized);
        if (nameBytes.Length > 0xDF) return;

        _files.Add(new PendingFile
        {
            NameBytes = nameBytes,
            NameLength = (short)nameBytes.Length,
            PathHash = FNV1A64.Hash(System.Text.Encoding.Unicode.GetBytes(normalized)),
            Data = Array.Empty<byte>(),
            Offset = _currentOffset,
            Flags = (ushort)0x21 // IsDirectory + Encrypted
        });
    }

    public void Build()
    {
        // Write file data XOR-encrypted
        foreach (var file in _files.OrderBy(f => f.Offset))
        {
            if (file.Data.Length > 0)
            {
                var enc = new byte[file.Data.Length];
                file.Data.CopyTo(enc, 0);
                Xor(enc);
                _stream.Write(enc);
            }
        }

        // Write file table - 32MB at end of file
        var table = new byte[FILE_TABLE_SIZE];
        int idx = 0;

        foreach (var file in _files)
        {
            int off = idx * ENTRY_SIZE;
            var span = table.AsSpan(off);

            // PackFileEntry layout (256 bytes):
            // [0x000] FileName: fixed byte[0xE0] (224 bytes) - ASCII path
            file.NameBytes.CopyTo(span);

            // [0x0E0] UnkAlways0: int (4 bytes) - always 0
            // Already 0 from initialization

            // [0x0E4] Flags: ushort (2 bytes)
            BitConverter.TryWriteBytes(span[0xE4..], file.Flags);

            // [0x0E6] FileNameLength: short (2 bytes)
            BitConverter.TryWriteBytes(span[0xE6..], file.NameLength);

            // [0x0E8] PathHash: ulong (8 bytes) - FNV1A64 hash
            BitConverter.TryWriteBytes(span[0xE8..], file.PathHash);

            // [0x0F0] FileSize: long (8 bytes)
            BitConverter.TryWriteBytes(span[0xF0..], (long)file.Data.Length);

            // [0x0F8] FileOffset: long (8 bytes)
            BitConverter.TryWriteBytes(span[0xF8..], file.Offset);

            idx++;
        }

        Xor(table);
        _stream.Write(table);
    }

    private void Xor(byte[] data)
    {
        Span<byte> span = data;
        while (span.Length >= 8)
        {
            var ulongs = MemoryMarshal.Cast<byte, ulong>(span);
            ulongs[0] ^= KEY;
            span = span[8..];
        }
        while (span.Length > 0)
        {
            span[0] ^= unchecked((byte)KEY);
            span = span[1..];
        }
    }

    public void Dispose() => _stream.Dispose();

    private class PendingFile
    {
        public byte[] NameBytes = Array.Empty<byte>();
        public short NameLength;
        public ulong PathHash;
        public byte[] Data = Array.Empty<byte>();
        public long Offset;
        public ushort Flags;
    }
}



