using System.Runtime.InteropServices;
using ACEvo.Package.Hashing;

namespace EVO.ModManager.Core.Services.Implementations;

public class KspkgReader : IDisposable
{
    private readonly string _path;
    private readonly byte[] _key = { 0xC1, 0x35, 0x11, 0x7D, 0xA9, 0x21, 0x97, 0x9F };

    public KspkgReader(string path) => _path = path;

    public List<KspkgEntry> ListFiles()
    {
        var data = File.ReadAllBytes(_path);
        long len = data.Length;

        foreach (var tableSize in new[] { 0x4000000L, 0x2000000L })
        {
            if (len < tableSize + 0x100) continue;
            int tableOff = (int)(len - tableSize);
            int tableSI = (int)tableSize;

            var entryBuf = new byte[0x100];
            Buffer.BlockCopy(data, tableOff, entryBuf, 0, 0x100);
            Decrypt(entryBuf);

            int nameLen = BitConverter.ToInt16(entryBuf, 0xE6);
            ulong pathHash = BitConverter.ToUInt64(entryBuf, 0xE8);
            if (pathHash == 0 || nameLen <= 0 || nameLen > 260) continue;

            var table = new byte[tableSI];
            Buffer.BlockCopy(data, tableOff, table, 0, tableSI);
            Decrypt(table);

            var entries = new List<KspkgEntry>();
            for (int idx = 0; idx * 0x100 + 0xFF < tableSI; idx++)
            {
                int off = idx * 0x100;
                ulong ph = BitConverter.ToUInt64(table, off + 0xE8);
                if (ph == 0) break;

                int nl = BitConverter.ToInt16(table, off + 0xE6);
                if (nl <= 0 || nl > 260) break;

                string name = System.Text.Encoding.ASCII.GetString(table, off, nl);
                long fileOff = BitConverter.ToInt64(table, off + 0xF8);
                long fileSz = BitConverter.ToInt64(table, off + 0xF0);
                ushort flags = BitConverter.ToUInt16(table, off + 0xE4);

                entries.Add(new KspkgEntry
                {
                    Name = name,
                    Offset = fileOff,
                    Size = fileSz,
                    IsDirectory = (flags & 1) == 1
                });
            }
            if (entries.Count > 0) return entries;
        }
        return new List<KspkgEntry>();
    }

    public byte[] ExtractFile(string gamePath)
    {
        var entries = ListFiles();
        var entry = entries.FirstOrDefault(e => e.Name.Equals(gamePath, StringComparison.OrdinalIgnoreCase) && !e.IsDirectory);
        if (entry == null) throw new FileNotFoundException($"Not found: {gamePath}");

        var data = File.ReadAllBytes(_path);
        var result = new byte[(int)entry.Size];
        Buffer.BlockCopy(data, (int)entry.Offset, result, 0, (int)entry.Size);
        Decrypt(result);
        return result;
    }

    private void Decrypt(byte[] data)
    {
        for (int i = 0; i < data.Length; i += 8)
            for (int j = 0; j < 8 && i + j < data.Length; j++)
                data[i + j] ^= _key[j];
    }

    public void Dispose() { }
}

public class KspkgEntry
{
    public string Name { get; set; } = "";
    public long Offset { get; set; }
    public long Size { get; set; }
    public bool IsDirectory { get; set; }
}
