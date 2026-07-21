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
        var actorPath = Path.Combine(templateDir, "car.actor");
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
            // Truncate car name to 9 chars and pad template name to 22 chars
            if (carName.Length > 9) carName = carName.Substring(0, 9);
            var templateName = "ks_audi_rs_3_sportback"; // 22 chars
            var paddedName = (carName.Length > 22 ? carName.Substring(0, 22) : carName.PadRight(22, '_')).Substring(0, 22);
            var srcBytes = System.Text.Encoding.ASCII.GetBytes(templateName);
            var dstBytes = System.Text.Encoding.ASCII.GetBytes(paddedName);

            kspkgPath = Path.Combine(aceModsFolder, $"{carName}.kspkg");
            Directory.CreateDirectory(tempDir);
            var carDir = Path.Combine(tempDir, "content", "cars", carName);
            var meshesDir = Path.Combine(carDir, "meshes");
            var dataDir = Path.Combine(carDir, "data");
            var colliderDir = Path.Combine(carDir, "collider");
            Directory.CreateDirectory(meshesDir); Directory.CreateDirectory(dataDir); Directory.CreateDirectory(colliderDir);

            // Convert kn5 to meshes
            foreach (var kn5 in Directory.GetFiles(sourceDir, "*.kn5", SearchOption.AllDirectories))
            {
                var parser = new Kn5Parser();
                var result = parser.ParseWithFallback(kn5);
                if (!result.Success || result.Meshes.Count == 0) continue;
                var isCollider = Path.GetFileNameWithoutExtension(kn5).Contains("collider");
                var meshDir = isCollider ? colliderDir : meshesDir;
                var allPos = new List<float>(); var allNrm = new List<float>(); var allUv = new List<float>(); var allIdx = new List<uint>(); int off = 0;
                foreach (var m in result.Meshes) { allPos.AddRange(m.Vertices); allNrm.AddRange(m.Normals); allUv.AddRange(m.UVs); foreach (var idx in m.Indices) allIdx.Add((uint)(idx + off)); off += m.Vertices.Length / 3; }
                if (allPos.Count == 0) continue;
                var mesh = new MeshDataProto { Type = 4, IsVisible = true, IsRenderable = true, LodOut = 1000f, BoundsMin = new Vector3DataProto { X = -5, Y = -5, Z = -5 }, BoundsMax = new Vector3DataProto { X = 5, Y = 5, Z = 5 }, ImportSettings = new ImportSettingsProto { CreateDefaultsForMissingMaterials = true }, Lods = new List<MeshLodDataProto> { new MeshLodDataProto { CastShadows = true, Positions = allPos, Normals = allNrm, Texcoords = allUv, Indices = allIdx, BoundsMin = new Vector3DataProto { X = -5, Y = -5, Z = -5 }, BoundsMax = new Vector3DataProto { X = 5, Y = 5, Z = 5 }, Batches = new List<MeshBatchProto> { new MeshBatchProto { Name = Path.GetFileNameWithoutExtension(kn5), StartIndex = 0, IndexCount = allIdx.Count, Material = $"editor/{Path.GetFileNameWithoutExtension(kn5)}.material" } } } } };
                using var ms = new MemoryStream(); Serializer.Serialize(ms, mesh);
                File.WriteAllBytes(Path.Combine(meshDir, Path.GetFileNameWithoutExtension(kn5) + ".mesh"), ms.ToArray());
            }

            // Convert DDS to texture
            foreach (var dds in Directory.GetFiles(sourceDir, "*.dds", SearchOption.AllDirectories))
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(dds);
                    var data = File.ReadAllBytes(dds);
                    var texDir = Path.Combine(carDir, "texture"); Directory.CreateDirectory(texDir);
                    using var texMs = new MemoryStream();
                    texMs.WriteByte(10); texMs.WriteByte(0); texMs.WriteByte(0); texMs.WriteByte(0);
                    texMs.WriteByte(0); texMs.WriteByte(4); texMs.WriteByte(0); texMs.WriteByte(0);
                    texMs.WriteByte(0); texMs.WriteByte(4); texMs.WriteByte(0); texMs.WriteByte(0);
                    File.WriteAllBytes(Path.Combine(texDir, name + ".texture"), texMs.ToArray());
                    if (data.Length > 128) File.WriteAllBytes(Path.Combine(texDir, name + ".texturemips"), data[128..]);
                } catch { }
            }

            // Create material files
            var matsDir = Path.Combine(carDir, "materials"); Directory.CreateDirectory(matsDir);
            foreach (var mesh in Directory.GetFiles(meshesDir, "*.mesh"))
                File.WriteAllText(Path.Combine(matsDir, Path.GetFileNameWithoutExtension(mesh) + ".material"), "{}");

            // Patch and write actor/cardata templates
            foreach (var (template, filePath) in new[] { (ActorTemplate, Path.Combine(carDir, carName + ".actor")), (CardataTemplate, Path.Combine(dataDir, "cardata.car")) })
            {
                if (template.Length == 0) continue;
                var patched = new byte[template.Length];
                Buffer.BlockCopy(template, 0, patched, 0, template.Length);
                for (int i = 0; i < patched.Length - 22; i++)
                {
                    bool match = true;
                    for (int j = 0; j < 22; j++) { if (patched[i + j] != srcBytes[j]) { match = false; break; } }
                    if (match) for (int j = 0; j < 22; j++) patched[i + j] = dstBytes[j];
                }
                File.WriteAllBytes(filePath, patched);
            }

            // Create minimal scene
            using var sceneMs = new MemoryStream();
            ProtoWriter.WriteFieldProto(sceneMs, 1, WireType.String);
            var nb = System.Text.Encoding.UTF8.GetBytes(carName);
            ProtoWriter.WriteVarint(sceneMs, (uint)nb.Length); sceneMs.Write(nb, 0, nb.Length);
            ProtoWriter.WriteFieldProto(sceneMs, 5, WireType.String);
            var ab = System.Text.Encoding.UTF8.GetBytes($"content\\cars\\{carName}\\{carName}.actor");
            ProtoWriter.WriteVarint(sceneMs, (uint)ab.Length); sceneMs.Write(ab, 0, ab.Length);
            File.WriteAllBytes(Path.Combine(carDir, $"{carName}.scene"), sceneMs.ToArray());

            // Pack to kspkg
            using var builder = new KspkgBuilder(kspkgPath);
            var basePath = Path.Combine(tempDir, "content");
            foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(basePath, file).Replace("/", "\\");
                var gp = "content\\" + rel;
                builder.AddDirectory(Path.GetDirectoryName(gp)!.Replace("/", "\\"));
                builder.AddFile(gp, File.ReadAllBytes(file));
            }
            builder.Build();

            var meshCount = Directory.GetFiles(meshesDir, "*.mesh").Length + Directory.GetFiles(colliderDir, "*.mesh").Length;
            Log.Information("Car conversion complete: {Name} ({MeshCount} meshes)", carName, meshCount);
            return (new ConversionResult { Success = true, ModName = carName, ErrorMessage = $"Car: {carName} ({meshCount} meshes)" }, kspkgPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Car conversion failed: {Name}", carName);
            return (new ConversionResult { Success = false, ModName = carName, ErrorMessage = $"Failed: {ex.Message}" }, null!);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }
}
