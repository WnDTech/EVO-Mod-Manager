using ProtoBuf;
using Serilog;
using EVO.ModManager.Core.Models;
using System.IO;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class LegacyCarConverter
{
    private static readonly ILogger Log = Serilog.Log.ForContext<LegacyCarConverter>();
    private static readonly byte[] ActorTemplate;
    private static readonly byte[] CardataTemplate;

    static LegacyCarConverter()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var templateDir = Path.Combine(baseDir, "Templates");
        if (!Directory.Exists(templateDir))
            templateDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "EVO.ModManager.Core", "Templates");
        if (!Directory.Exists(templateDir))
            templateDir = @"C:\Users\paul_\OneDrive\Documents\APP\EVO Mod Manager\src\EVO.ModManager.Core\Templates";

        var actorPath = Path.Combine(templateDir, "sd_banana.actor");
        var cardataPath = Path.Combine(templateDir, "cardata.car");
        ActorTemplate = File.Exists(actorPath) ? File.ReadAllBytes(actorPath) : Array.Empty<byte>();
        CardataTemplate = File.Exists(cardataPath) ? File.ReadAllBytes(cardataPath) : Array.Empty<byte>();
    }

    public (ConversionResult result, string kspkgPath) Convert(string carName, string sourceDir, string aceModsFolder)
    {
        string kspkgPath = null!;
        var tempDir = Path.Combine(Path.GetTempPath(), "EVOMM", "ace_build", Guid.NewGuid().ToString("N")[..8]);

        try
        {
            if (carName.Length > 9) carName = carName.Substring(0, 9);
            kspkgPath = Path.Combine(aceModsFolder, $"{carName}.kspkg");
            Directory.CreateDirectory(tempDir);
            var carDir = Path.Combine(tempDir, "content", "cars", carName);
            var meshesDir = Path.Combine(carDir, "meshes");
            var dataDir = Path.Combine(carDir, "data");
            var colliderDir = Path.Combine(carDir, "collider");
            Directory.CreateDirectory(meshesDir);
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(colliderDir);

            // 1. Convert .kn5 to .mesh using Kn5Parser
            var kn5Files = Directory.GetFiles(sourceDir, "*.kn5", SearchOption.AllDirectories);
            Log.Information("  Found {Count} kn5 files in {Dir}", kn5Files.Length, sourceDir);
            // 2. Convert DDS to .texture/.texturemips
            var ddsFiles = System.IO.Directory.GetFiles(sourceDir, "*.dds", System.IO.SearchOption.AllDirectories);
            foreach (var dds in ddsFiles) {
                try {
                    var name = System.IO.Path.GetFileNameWithoutExtension(dds);
                    var data = System.IO.File.ReadAllBytes(dds);
                    var texDir = System.IO.Path.Combine(carDir, "texture");
                    System.IO.Directory.CreateDirectory(texDir);
                    // .texture descriptor (12 bytes: format, width, height, mips)
                    using var ms = new System.IO.MemoryStream();
                    ms.WriteByte(10); ms.WriteByte(0); ms.WriteByte(0); ms.WriteByte(0);
                    ms.WriteByte(0); ms.WriteByte(4); ms.WriteByte(0); ms.WriteByte(0);
                    ms.WriteByte(0); ms.WriteByte(4); ms.WriteByte(0); ms.WriteByte(0);
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(texDir, name + ".texture"), ms.ToArray());
                    // texturemips: skip DDS header
                    if (data.Length > 128)
                        System.IO.File.WriteAllBytes(System.IO.Path.Combine(texDir, name + ".texturemips"), data[128..]);
                } catch { }
            }

            // 3. Create basic material files
            var matsDir = System.IO.Path.Combine(carDir, "materials");
            System.IO.Directory.CreateDirectory(matsDir);
            foreach (var mesh in System.IO.Directory.GetFiles(meshesDir, "*.mesh")) {
                var matName = System.IO.Path.GetFileNameWithoutExtension(mesh) + ".material";
                System.IO.File.WriteAllText(System.IO.Path.Combine(matsDir, matName), "{}");
            }

            
            foreach (var kn5 in kn5Files)
            {
                var relName = Path.GetFileNameWithoutExtension(kn5);
                var isCollider = relName.ToLower().Contains("collider");
                var meshPath = isCollider 
                    ? Path.Combine(colliderDir, $"{relName}.mesh")
                    : Path.Combine(meshesDir, $"{relName}.mesh");
                
                if (ConvertKn5ToMesh(kn5, meshPath, relName))
                    Log.Information("  Converted mesh: {Name}", relName);
            }

            // 2. Patch and copy actor template (sd_banana → carName truncated to 9 chars)
            var actorTemplate = ActorTemplate;
            var cardataTemplate = CardataTemplate;
            var shortName = carName.Length > 9 ? carName.Substring(0, 9) : carName.PadRight(9);
            var srcBytes = System.Text.Encoding.ASCII.GetBytes("sd_banana");
            var dstBytes = System.Text.Encoding.ASCII.GetBytes(shortName.PadRight(9, '_'));

            if (actorTemplate.Length > 0)
            {
                var patched = new byte[actorTemplate.Length];
                Buffer.BlockCopy(actorTemplate, 0, patched, 0, actorTemplate.Length);
                PatchBytes(patched, srcBytes, dstBytes);
                File.WriteAllBytes(Path.Combine(carDir, $"{carName}.actor"), patched);
            }

            if (cardataTemplate.Length > 0)
            {
                var patched = new byte[cardataTemplate.Length];
                Buffer.BlockCopy(cardataTemplate, 0, patched, 0, cardataTemplate.Length);
                PatchBytes(patched, srcBytes, dstBytes);
                File.WriteAllBytes(Path.Combine(dataDir, "cardata.car"), patched);
            }

            // 4. Create minimal scene file
            CreateMinimalScene(Path.Combine(carDir, $"{carName}.scene"), carName);

            // 5. Pack into .kspkg
            PackToKspkg(tempDir, kspkgPath);

            var meshCount = Directory.GetFiles(meshesDir, "*.mesh").Length + Directory.GetFiles(colliderDir, "*.mesh").Length;
            Log.Information("Car conversion complete: {Name} ({MeshCount} meshes) -> {Kspkg}", carName, meshCount, kspkgPath);

            return (new ConversionResult
            {
                Success = true,
                ModName = carName,
                ErrorMessage = $"Car: {carName}\n{meshCount} meshes → mods/{carName}.kspkg"
            }, kspkgPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Car conversion failed: {Name}", carName);
            return (new ConversionResult { Success = false, ModName = carName, ErrorMessage = $"Failed: {ex.Message}" }, null!);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    private bool ConvertKn5ToMesh(string kn5Path, string meshPath, string meshName)
    {
        try
        {
            var parser = new Kn5Parser();
            var result = parser.ParseWithFallback(kn5Path);
            if (!result.Success || result.Meshes.Count == 0) return false;

            var allPos = new List<float>();
            var allNrm = new List<float>();
            var allUv = new List<float>();
            var allIdx = new List<uint>();
            int off = 0;

            foreach (var m in result.Meshes)
            {
                allPos.AddRange(m.Vertices);
                allNrm.AddRange(m.Normals);
                allUv.AddRange(m.UVs);
                foreach (var idx in m.Indices) allIdx.Add((uint)(idx + off));
                off += m.Vertices.Length / 3;
            }
            if (allPos.Count == 0) return false;

            var mesh = new MeshDataProto
            {
                Type = 4, IsVisible = true, IsRenderable = true, LodOut = 1000f,
                BoundsMin = new Vector3DataProto { X = -5, Y = -5, Z = -5 },
                BoundsMax = new Vector3DataProto { X = 5, Y = 5, Z = 5 },
                ImportSettings = new ImportSettingsProto { CreateDefaultsForMissingMaterials = true },
                Lods = new List<MeshLodDataProto>
                {
                    new MeshLodDataProto
                    {
                        CastShadows = true, Positions = allPos, Normals = allNrm, Texcoords = allUv, Indices = allIdx,
                        BoundsMin = new Vector3DataProto { X = -5, Y = -5, Z = -5 },
                        BoundsMax = new Vector3DataProto { X = 5, Y = 5, Z = 5 },
                        Batches = new List<MeshBatchProto>
                        {
                            new MeshBatchProto { Name = meshName, StartIndex = 0, IndexCount = allIdx.Count, Material = $"editor/{meshName}.material" }
                        }
                    }
                }
            };

            using var ms = new MemoryStream();
            Serializer.Serialize(ms, mesh);
            File.WriteAllBytes(meshPath, ms.ToArray());
            return true;
        }
        catch { return false; }
    }

    private void CreateMinimalScene(string path, string carName)
    {
        using var ms = new MemoryStream();
        ProtoWriter.WriteFieldProto(ms, 1, WireType.String);
        var nb = System.Text.Encoding.UTF8.GetBytes(carName);
        ProtoWriter.WriteVarint(ms, (uint)nb.Length); ms.Write(nb, 0, nb.Length);
        ProtoWriter.WriteFieldProto(ms, 5, WireType.String);
        var ab = System.Text.Encoding.UTF8.GetBytes($"content\\cars\\{carName}\\{carName}.actor");
        ProtoWriter.WriteVarint(ms, (uint)ab.Length); ms.Write(ab, 0, ab.Length);
        File.WriteAllBytes(path, ms.ToArray());
    }

    private static void PatchBytes(byte[] data, byte[] src, byte[] dst)
    {
        for (int i = 0; i < data.Length - src.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < src.Length; j++) { if (data[i + j] != src[j]) { match = false; break; } }
            if (match)
                for (int j = 0; j < dst.Length; j++) data[i + j] = dst[j];
        }
    }

    private void PackToKspkg(string tempDir, string kspkgPath)
    {
        using var builder = new KspkgBuilder(kspkgPath);
        var basePath = Path.Combine(tempDir, "content");
        foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(basePath, file).Replace("/", "\\");
            var gp = "content\\" + rel;
            var dir = Path.GetDirectoryName(gp)!.Replace("/", "\\");
            builder.AddDirectory(dir);
            builder.AddFile(gp, File.ReadAllBytes(file));
        }
        builder.Build();
    }
}







