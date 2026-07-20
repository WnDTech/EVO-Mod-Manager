using ProtoBuf;
using Serilog;
using EVO.ModManager.Core.Models;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class AceCarConverter
{
    private static readonly ILogger Log = Serilog.Log.ForContext<AceCarConverter>();

    /// <summary>
    /// Convert extracted AC car mod to proper ACE EVO .kspkg file
    /// </summary>
    public (ConversionResult result, string kspkgPath) Convert(string carName, string sourceDir, string aceModsFolder)
    {
        var kspkgPath = Path.Combine(aceModsFolder, $"{carName}.kspkg");
        var tempDir = Path.Combine(Path.GetTempPath(), "EVOMM", "ace_build", Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(tempDir);

            // 1. Create car directory structure inside temp
            var carDir = Path.Combine(tempDir, "content", "cars", carName);
            var meshesDir = Path.Combine(carDir, "meshes");
            var materialsDir = Path.Combine(carDir, "materials");
            var texturesDir = Path.Combine(carDir, "textures");
            var dataDir = Path.Combine(carDir, "data");
            Directory.CreateDirectory(meshesDir);
            Directory.CreateDirectory(materialsDir);
            Directory.CreateDirectory(texturesDir);
            Directory.CreateDirectory(dataDir);

            // 2. Convert .kn5 files to .mesh
            var kn5Files = Directory.GetFiles(sourceDir, "*.kn5", SearchOption.AllDirectories);
            Log.Information("  Found {Count} kn5 files in {Dir}", kn5Files.Length, sourceDir);
            var totalMeshCount = 0;
            foreach (var kn5 in kn5Files)
            {
                var relName = Path.GetFileNameWithoutExtension(kn5);
                var meshPath = Path.Combine(meshesDir, relName + ".mesh");
                if (ConvertKn5ToMesh(kn5, meshPath, relName, carName, texturesDir))
                    totalMeshCount++;
            }

            // 3. Create .actor file
            var actorPath = Path.Combine(carDir, $"{carName}.actor");
            CreateActorFile(actorPath, carName, meshesDir);

            // 4. Create .scene file
            var scenePath = Path.Combine(carDir, $"{carName}.scene");
            CreateSceneFile(scenePath, carName);

            // 5. Create cardata.car
            var carDataPath = Path.Combine(dataDir, "cardata.car");
            CreateCarDataFile(carDataPath, carName);

            // 6. Copy any DDS textures to .texture format
            CopyTextures(sourceDir, texturesDir);

            // 7. Create default material files
            CreateDefaultMaterials(materialsDir, texturesDir);

            // 8. Pack into .kspkg
            PackToKspkg(tempDir, kspkgPath);

            Log.Information("Car conversion complete: {Name} ({MeshCount} meshes) -> {Kspkg}", carName, totalMeshCount, kspkgPath);

            return (new ConversionResult
            {
                Success = true,
                ModName = carName,
                ErrorMessage = $"Car converted: {carName}\n{totalMeshCount} meshes → mods/{carName}.kspkg"
            }, kspkgPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Car conversion failed: {Name}", carName);
            return (new ConversionResult
            {
                Success = false,
                ModName = carName,
                ErrorMessage = $"Conversion failed: {ex.Message}"
            }, null!);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private bool ConvertKn5ToMesh(string kn5Path, string meshPath, string meshName, string carName, string texturesDir)
    {
        try
        {
            var parser = new Kn5Parser();
            var result = parser.ParseWithFallback(kn5Path);
            if (!result.Success || result.Meshes.Count == 0)
                return false;

            var allPositions = new List<float>();
            var allNormals = new List<float>();
            var allUVs = new List<float>();
            var allIndices = new List<uint>();
            int indexOffset = 0;

            foreach (var mesh in result.Meshes)
            {
                allPositions.AddRange(mesh.Vertices);
                allNormals.AddRange(mesh.Normals);
                allUVs.AddRange(mesh.UVs);
                foreach (var idx in mesh.Indices)
                    allIndices.Add((uint)(idx + indexOffset));
                indexOffset += mesh.Vertices.Length / 3;
            }

            if (allPositions.Count == 0) return false;

            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < allPositions.Count; i += 3)
            {
                if (allPositions[i] < minX) minX = allPositions[i];
                if (allPositions[i + 1] < minY) minY = allPositions[i + 1];
                if (allPositions[i + 2] < minZ) minZ = allPositions[i + 2];
                if (allPositions[i] > maxX) maxX = allPositions[i];
                if (allPositions[i + 1] > maxY) maxY = allPositions[i + 1];
                if (allPositions[i + 2] > maxZ) maxZ = allPositions[i + 2];
            }

            var meshProto = new MeshDataProto
            {
                Type = 4,
                IsVisible = true,
                IsRenderable = true,
                LodOut = 1000f,
                BoundsMin = new Vector3DataProto { X = minX, Y = minY, Z = minZ },
                BoundsMax = new Vector3DataProto { X = maxX, Y = maxY, Z = maxZ },
                ImportSettings = new ImportSettingsProto { CreateDefaultsForMissingMaterials = true },
                Lods = new List<MeshLodDataProto>
                {
                    new MeshLodDataProto
                    {
                        CastShadows = true,
                        Positions = allPositions,
                        Normals = allNormals,
                        Texcoords = allUVs,
                        Indices = allIndices,
                        BoundsMin = new Vector3DataProto { X = minX, Y = minY, Z = minZ },
                        BoundsMax = new Vector3DataProto { X = maxX, Y = maxY, Z = maxZ },
                        Batches = new List<MeshBatchProto>
                        {
                            new MeshBatchProto
                            {
                                Name = meshName,
                                StartIndex = 0,
                                IndexCount = allIndices.Count,
                                Material = $"editor/{meshName}.material"
                            }
                        }
                    }
                }
            };

            using var ms = new MemoryStream();
            Serializer.Serialize(ms, meshProto);
            File.WriteAllBytes(meshPath, ms.ToArray());
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "  Failed to convert {Kn5}", kn5Path);
            return false;
        }
    }

    private void CreateActorFile(string actorPath, string carName, string meshesDir)
    {
        var actor = new CarActorDataProto
        {
            BaseMeshes = new CarActorDataProto.BaseMeshesProto
            {
                BodyMesh = $"content\\cars\\{carName}\\meshes\\body.mesh",
                Interior = new CarActorDataProto.BaseMeshesProto.BaseMeshProto
                {
                    MeshPath = $"content\\cars\\{carName}\\meshes\\interior.mesh",
                    ShowroomInvisible = true
                },
                SteeringWheel = new CarActorDataProto.BaseMeshesProto.BaseMeshProto
                {
                    MeshPath = $"content\\cars\\{carName}\\meshes\\steering_wheel.mesh",
                    ShowroomInvisible = true
                }
            },
            LodOutDistance = 500f,
            LodInDistances = new List<float> { 15f, 30f, 60f, 120f }
        };

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, actor);
        File.WriteAllBytes(actorPath, ms.ToArray());
    }

    private void CreateSceneFile(string scenePath, string carName)
    {
        // Minimal scene file - just enough for the game to discover the car
        // Field 1: string carName, Field 2: string type, Field 3: scene data
        using var ms = new MemoryStream();

        // Write manual protobuf for now - Scene schema is complex
        // Field 1: screen name (string)
        ProtoWriter.WriteFieldProto(ms, 1, WireType.String);
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(carName);
        ProtoWriter.WriteVarint(ms, (uint)nameBytes.Length);
        ms.Write(nameBytes, 0, nameBytes.Length);

        // Field 3: Car type reference
        ProtoWriter.WriteFieldProto(ms, 3, WireType.Varint);
        ProtoWriter.WriteVarint(ms, 1); // Car type enum value

        // Field 5: actor path
        var actorPath = $"content\\cars\\{carName}\\{carName}.actor";
        ProtoWriter.WriteFieldProto(ms, 5, WireType.String);
        var actorBytes = System.Text.Encoding.UTF8.GetBytes(actorPath);
        ProtoWriter.WriteVarint(ms, (uint)actorBytes.Length);
        ms.Write(actorBytes, 0, actorBytes.Length);

        File.WriteAllBytes(scenePath, ms.ToArray());
    }

    private void CreateCarDataFile(string carDataPath, string carName)
    {
        var carData = new CarDataProto
        {
            General = new GeneralCarDataProto
            {
                ScreenName = carName,
                TotalMass = 1200f,
                Fuel = 60f,
                MaxFuel = 60f
            },
            Suspensions = new SuspensionsDataProto
            {
                WheelBase = 2.5f,
                TrackFront = 1.5f,
                TrackRear = 1.5f
            }
        };

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, carData);
        File.WriteAllBytes(carDataPath, ms.ToArray());
    }

    private void CopyTextures(string sourceDir, string texturesDir)
    {
        foreach (var ddsFile in Directory.GetFiles(sourceDir, "*.dds", SearchOption.AllDirectories))
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(ddsFile);
                var data = File.ReadAllBytes(ddsFile);

                // Create basic .texture descriptor
                using var texMs = new MemoryStream();
                var bw = new BinaryWriter(texMs);
                bw.Write(10); // format = BC7
                bw.Write(1024); // width
                bw.Write(1024); // height
                bw.Write(11); // mipCount
                File.WriteAllBytes(Path.Combine(texturesDir, $"{name}.texture"), texMs.ToArray());

                // For .texturemips, just copy the DDS pixel data (skip DDS header)
                if (data.Length > 128)
                {
                    var pixelData = data[128..]; // skip DDS header
                    File.WriteAllBytes(Path.Combine(texturesDir, $"{name}.texturemips"), pixelData);
                }
            }
            catch { }
        }
    }

    private void CreateDefaultMaterials(string materialsDir, string texturesDir)
    {
        // Create a simple default material
        var matPath = Path.Combine(materialsDir, "body.material");
        // Minimal material with base color and normal map references
        var fields = new List<ProtoField2>
        {
            new() { Number = 1, WireType = WireType.String, StringValue = "UberVehiclePaint" },
            new() { Number = 2, WireType = WireType.String, StringValue = "txDiffuse" },
            new() { Number = 3, WireType = WireType.String, StringValue = "txNormal" },
            new() { Number = 4, WireType = WireType.String, StringValue = "HasNormalMap" },
            new() { Number = 5, WireType = WireType.String, StringValue = "HasSpecularMap" },
        };
        using var ms = new MemoryStream();
        foreach (var f in fields)
        {
            ProtoWriter.WriteFieldProto(ms, f.Number, f.WireType);
            if (f.WireType == WireType.String)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(f.StringValue!);
                ProtoWriter.WriteVarint(ms, (uint)bytes.Length);
                ms.Write(bytes, 0, bytes.Length);
            }
        }
        File.WriteAllBytes(matPath, ms.ToArray());

        // Copy the same material for common names
        foreach (var name in new[] { "default", "paint", "glass", "wheel", "tyre", "interior", "light" })
        {
            var copyPath = Path.Combine(materialsDir, $"{name}.material");
            File.Copy(matPath, copyPath, true);
        }
    }

    private void PackToKspkg(string tempDir, string kspkgPath)
    {
        using var builder = new KspkgBuilder(kspkgPath);
        var basePath = Path.Combine(tempDir, "content");
        foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
        {
            var relPath = Path.GetRelativePath(basePath, file).Replace("/", "\\");
            var gamePath = "content\\" + relPath;

            // Add parent directories
            var dir = Path.GetDirectoryName(gamePath)!.Replace("/", "\\");
            builder.AddDirectory(dir);

            // Add the file
            builder.AddFile(gamePath, File.ReadAllBytes(file));
        }
        builder.Build();
    }
}

// Simple proto writer helpers
public static class ProtoWriter
{
    public static void WriteFieldProto(Stream stream, int fieldNumber, WireType wireType)
    {
        WriteVarint(stream, (uint)((fieldNumber << 3) | (int)wireType));
    }

    public static void WriteVarint(Stream stream, uint value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        stream.WriteByte((byte)value);
    }
}

public struct ProtoField2
{
    public int Number;
    public WireType WireType;
    public string? StringValue;
    public int IntValue;
}





