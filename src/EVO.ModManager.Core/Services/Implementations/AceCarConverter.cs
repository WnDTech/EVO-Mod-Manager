using ProtoBuf;
using ProtoBuf.Meta;
using System.Text;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

/// <summary>
/// Converts AC car mods to ACE EVO format including .mesh, .material, .scene, .actor, cardata.car
/// </summary>
public class AceCarConverter
{
    private const ulong KEY = 0x9F9721A97D1135C1;

    /// <summary>
    /// Convert an extracted AC car mod to ACE EVO format in the mods folder
    /// </summary>
    public ConversionResult ConvertCar(string carName, string sourceDir, string aceModsFolder)
    {
        var aceCarDir = Path.Combine(aceModsFolder, carName);
        Directory.CreateDirectory(aceCarDir);

        // 1. Create .scene file
        CreateSceneFile(aceCarDir, carName);

        // 2. Convert mesh files (.kn5 → .mesh)
        var kn5Files = Directory.GetFiles(sourceDir, "*.kn5", SearchOption.AllDirectories);
        foreach (var kn5 in kn5Files)
        {
            ConvertKn5ToMesh(kn5, aceCarDir, carName);
        }

        // 3. Create basic material files
        var meshesDir = Path.Combine(aceCarDir, "meshes");
        if (Directory.Exists(meshesDir))
        {
            var meshFiles = Directory.GetFiles(meshesDir, "*.mesh", SearchOption.AllDirectories);
            foreach (var mesh in meshFiles)
            {
                var matName = Path.GetFileNameWithoutExtension(mesh) + ".material";
                var matPath = Path.Combine(aceCarDir, "materials", matName);
                Directory.CreateDirectory(Path.GetDirectoryName(matPath)!);
                CreateMaterialFile(matPath, mesh, aceCarDir);
            }
        }

        // 4. Create cardata.car
        CreateCarDataFile(aceCarDir, carName);

        return new ConversionResult
        {
            Success = true,
            ModName = carName,
            ErrorMessage = $"Car converted: {carName}\nFiles in: {aceCarDir}"
        };
    }

    private void CreateSceneFile(string aceCarDir, string carName)
    {
        var scenePath = Path.Combine(aceCarDir, $"{carName}.scene");
        using var ms = new MemoryStream();

        // Protobuf message matching the extracted scene structure
        // Field 1: Type (string) = "Car"
        WriteString(ms, 1, "Car");
        // Field 2: MeshType (string) = "SMesh"  
        WriteString(ms, 2, "SMesh");
        // Field 3: Data (string) - car name, actor reference, position
        var actorPath = $"content\\cars\\{carName}\\{carName}.actor";
        var data = $"{carName}\x1a\x03Car\"\x15\x00\x12\x00\x1a\x0f\x00\x00\x80?\x15\x00\x00\x80?\x1d\x00\x00\x80?a\x1f\x1f\x11\x11'j\x05\x1d\x00\x00zC\x03@{actorPath}\x1a\x00";
        WriteString(ms, 3, data);

        // Write protobuf
        var protoFile = new ProtoFile
        {
            Fields = new List<ProtoField>
            {
                new() { Number = 1, WireType = WireType.String, StringValue = carName },
                new() { Number = 2, WireType = WireType.String, StringValue = "SMesh" },
                new ProtoField { Number = 3, WireType = WireType.String, StringValue = BuildSceneData(carName) }
            }
        };
        
        File.WriteAllBytes(scenePath, SerializeProto(protoFile));
    }

    private string BuildSceneData(string carName)
    {
        // Simplified scene data: name, actor path, position
        var ms = new MemoryStream();
        // Field 1: car name
        WriteString(ms, 1, carName);
        // Field 3: type "Car"
        WriteString(ms, 3, "Car");
        // Position fields
        var posBytes = new byte[] { 0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x80, 0x3F };
        WriteBytes(ms, 7, posBytes);
        // Actor reference
        WriteString(ms, 10, $"content\\cars\\{carName}\\{carName}.actor");
        return Convert.ToBase64String(ms.ToArray());
    }

    private void CreateMaterialFile(string matPath, string meshPath, string aceCarDir)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(matPath)!);
        
        // Create a basic proto material with essential properties
        var fields = new List<ProtoField>
        {
            new() { Number = 1, WireType = WireType.String, StringValue = "UberVehicleInteriorMaterial" },
            new() { Number = 2, WireType = WireType.String, StringValue = "HasBaseColorMap" },
            new ProtoField { Number = 3, WireType = WireType.String, StringValue = "HasNormalMap" },
            new() { Number = 4, WireType = WireType.String, StringValue = "HasRoughnessMap" },
            new() { Number = 5, WireType = WireType.String, StringValue = "ksBaseColor" },
            new() { Number = 6, WireType = WireType.Fixed32, FloatValue = 1.0f },
            new() { Number = 7, WireType = WireType.String, StringValue = "txDiffuse" },
            new() { Number = 8, WireType = WireType.String, StringValue = $"content\\cars\\{Path.GetFileName(aceCarDir)}\\textures\\body_d.texture" },
        };

        File.WriteAllBytes(matPath, SerializeProto(fields));
    }

    private void CreateCarDataFile(string aceCarDir, string carName)
    {
        var dataPath = Path.Combine(aceCarDir, "data");
        Directory.CreateDirectory(dataPath);
        
        // Create minimal cardata.car (protobuf)
        var fields = new List<ProtoField>
        {
            new() { Number = 1, WireType = WireType.String, StringValue = carName },
            new() { Number = 2, WireType = WireType.String, StringValue = "Car" },
            new() { Number = 3, WireType = WireType.Fixed32, FloatValue = 1.0f }, // version
        };
        File.WriteAllBytes(Path.Combine(dataPath, "cardata.car"), SerializeProto(fields));
    }

    // Protobuf serialization helpers
    private static byte[] SerializeProto(List<ProtoField> fields)
    {
        using var ms = new MemoryStream();
        foreach (var field in fields)
        {
            WriteFieldHeader(ms, field.Number, field.WireType);
            switch (field.WireType)
            {
                case WireType.Varint:
                    WriteVarint(ms, field.IntValue);
                    break;
                case WireType.Fixed32:
                    var bytes = BitConverter.GetBytes(field.FloatValue);
                    ms.Write(bytes, 0, 4);
                    break;
                case WireType.String:
                    var strBytes = Encoding.UTF8.GetBytes(field.StringValue ?? "");
                    WriteVarint(ms, (uint)strBytes.Length);
                    ms.Write(strBytes, 0, strBytes.Length);
                    break;
            }
        }
        return ms.ToArray();
    }

    private static void WriteFieldHeader(Stream stream, int fieldNumber, WireType wireType)
    {
        WriteVarint(stream, (uint)((fieldNumber << 3) | (int)wireType));
    }

    private static void WriteVarint(Stream stream, uint value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        stream.WriteByte((byte)value);
    }

    private static void WriteString(MemoryStream ms, int field, string value)
    {
        WriteFieldHeader(ms, field, WireType.String);
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarint(ms, (uint)bytes.Length);
        ms.Write(bytes, 0, bytes.Length);
    }

    private static void WriteBytes(MemoryStream ms, int field, byte[] data)
    {
        WriteFieldHeader(ms, field, WireType.String);
        WriteVarint(ms, (uint)data.Length);
        ms.Write(data, 0, data.Length);
    }

    private void ConvertKn5ToMesh(string kn5Path, string aceCarDir, string carName)
    {
        try
        {
            var data = File.ReadAllBytes(kn5Path);
            if (data.Length < 8) return;

            // Parse kn5 header
            var magic = Encoding.ASCII.GetString(data, 0, 3);
            if (magic != "kn5") return;

            // Extract relative path for the mesh
            var relPath = Path.GetRelativePath(
                Path.Combine(Path.GetDirectoryName(kn5Path)!.Split(Path.DirectorySeparatorChar).TakeWhile(s => s != "content").LastOrDefault() ?? "", "content"),
                kn5Path);
            
            // Create corresponding .mesh file path
            var meshName = Path.GetFileNameWithoutExtension(kn5Path);
            var meshRelPath = relPath.Replace(".kn5", ".mesh");
            // Remove 'content\' prefix if present
            if (meshRelPath.StartsWith("content\\")) meshRelPath = meshRelPath.Substring(8);
            
            var meshPath = Path.Combine(aceCarDir, meshRelPath);
            Directory.CreateDirectory(Path.GetDirectoryName(meshPath)!);

            // Extract vertex data from kn5
            // kn5 format: header + nodes + meshes + materials + textures
            var vertices = new List<float>();
            var indices = new List<int>();
            var (verts, idxs) = ExtractKn5MeshData(data);
            vertices = verts;
            indices = idxs;

            if (vertices.Count == 0)
            {
                // Create empty mesh placeholder
                File.WriteAllBytes(meshPath, CreateEmptyMesh(meshName));
                return;
            }

            // Create .mesh protobuf
            var meshBytes = CreateMeshProtobuf(meshName, vertices.ToArray(), indices.ToArray());
            File.WriteAllBytes(meshPath, meshBytes);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to convert {Kn5}", kn5Path);
        }
    }

    private (List<float> vertices, List<int> indices) ExtractKn5MeshData(byte[] data)
    {
        var verts = new List<float>();
        var idxs = new List<int>();

        // Scan for vertex data (look for patterns of 3 consecutive floats)
        for (int offset = 16; offset < data.Length - 24; offset++)
        {
            // Look for a valid position (reasonable car coordinates)
            float x = BitConverter.ToSingle(data, offset);
            float y = BitConverter.ToSingle(data, offset + 4);
            float z = BitConverter.ToSingle(data, offset + 8);

            if (float.IsNormal(x) && float.IsNormal(y) && float.IsNormal(z) &&
                Math.Abs(x) < 100 && Math.Abs(y) < 100 && Math.Abs(z) < 100)
            {
                verts.Add(x); verts.Add(y); verts.Add(z);
                if (verts.Count >= 900) break; // First 300 verts max
            }
        }

        // Look for triangle indices (consecutive uint16 values matching typical vertex counts)
        for (int offset = 50; offset < data.Length - 6; offset += 2)
        {
            if (verts.Count > 0)
            {
                int maxVert = verts.Count / 3;
                ushort i0 = BitConverter.ToUInt16(data, offset);
                ushort i1 = BitConverter.ToUInt16(data, offset + 2);
                ushort i2 = BitConverter.ToUInt16(data, offset + 4);
                
                if (i0 < maxVert && i1 < maxVert && i2 < maxVert &&
                    i0 != i1 && i1 != i2 && i0 != i2)
                {
                    idxs.Add(i0); idxs.Add(i1); idxs.Add(i2);
                    if (idxs.Count >= 300) break;
                }
            }
        }

        return (verts, idxs);
    }

    private byte[] CreateEmptyMesh(string name)
    {
        var fields = new List<ProtoField>
        {
            new ProtoField { Number = 1, WireType = WireType.Varint, IntValue = 1 },
            new ProtoField { Number = 2, WireType = WireType.String, StringValue = name },
            new ProtoField { Number = 3, WireType = WireType.String, StringValue = $"editor/{name}.material" },
        };
        return SerializeProto(fields);
    }

    private byte[] CreateMeshProtobuf(string name, float[] vertices, int[] indices)
    {
        var fields = new List<ProtoField>
        {
            new ProtoField { Number = 1, WireType = WireType.Varint, IntValue = 1 },
            new ProtoField { Number = 2, WireType = WireType.String, StringValue = name },
            new ProtoField { Number = 3, WireType = WireType.String, StringValue = $"editor/{name}.material" },
        };

        // Pack vertices as field 4 (repeated float)
        var vertBytes = new byte[vertices.Length * 4];
        Buffer.BlockCopy(vertices, 0, vertBytes, 0, vertBytes.Length);
        fields.Add(new() { Number = 4, WireType = WireType.String, BytesValue = vertBytes });

        // Pack indices as field 7 (repeated int, varint packed)
        var idxMs = new MemoryStream();
        foreach (var idx in indices) WriteVarint(idxMs, (uint)idx);
        fields.Add(new() { Number = 7, WireType = WireType.String, BytesValue = idxMs.ToArray() });

        return SerializeProto(fields);
    }
}

internal class ProtoField
{
    public int Number;
    public WireType WireType;
    public int IntValue;
    public float FloatValue;
    public string? StringValue;
    public byte[]? BytesValue;
}


