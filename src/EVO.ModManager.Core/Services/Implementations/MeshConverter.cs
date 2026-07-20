using ProtoBuf;
using ProtoBuf.Meta;

namespace EVO.ModManager.Core.Services.Implementations;

/// <summary>
/// Creates ACE EVO .mesh (protobuf) files from vertex/index data.
/// </summary>
public class AceMeshWriter
{
    private const int CurrentVersion = 1;

    public byte[] WriteMesh(string materialName, float[] vertices, int[] indices, float[] normals = null, float[] uvs = null)
    {
        using var ms = new MemoryStream();

        // Field 1: version (varint)
        ProtoWriter.WriteFieldProto(ms, 1, WireType.Varint);
        ProtoWriter.WriteInt32(ms, CurrentVersion);

        // Field 2: mesh name
        ProtoWriter.WriteFieldProto(ms, 2, WireType.String);
        ProtoWriter.WriteString(ms, materialName ?? "default");

        // Field 3: material path
        ProtoWriter.WriteFieldProto(ms, 3, WireType.String);
        ProtoWriter.WriteString(ms, $"editor/{materialName ?? "default"}.material");

        // Pack vertices as field 4 (repeated float)
        WriteFloatArray(ms, 4, vertices);

        // Pack normals as field 5 (repeated float, optional)
        if (normals != null && normals.Length > 0)
            WriteFloatArray(ms, 5, normals);

        // Pack UVs as field 6 (repeated float, optional)
        if (uvs != null && uvs.Length > 0)
            WriteFloatArray(ms, 6, uvs);

        // Pack indices as field 7 (repeated int)
        WritePackedInt32(ms, 7, indices);

        return ms.ToArray();
    }

    private static void WriteFloatArray(MemoryStream ms, int fieldNumber, float[] values)
    {
        // Packed repeated field
        ProtoWriter.WriteFieldProto(ms, fieldNumber, WireType.String);
        var data = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, data, 0, data.Length);
        ProtoWriter.WriteBytes(ms, data);
    }

    private static void WritePackedInt32(MemoryStream ms, int fieldNumber, int[] values)
    {
        // Packed repeated int32 field
        ProtoWriter.WriteFieldProto(ms, fieldNumber, WireType.String);
        using var subStream = new MemoryStream();
        foreach (var v in values)
            ProtoWriter.WriteInt32(subStream, v);
        ProtoWriter.WriteBytes(ms, subStream.ToArray());
    }

    private static void WriteFloatArrayField(MemoryStream ms, int fieldNumber, float[] values)
    {
        var data = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, data, 0, data.Length);
        ProtoWriter.WriteFieldProto(ms, fieldNumber, WireType.String);
        ProtoWriter.WriteBytes(ms, data);
    }

    private static void WritePackedInt32Field(MemoryStream ms, int fieldNumber, int[] values)
    {
        ProtoWriter.WriteFieldProto(ms, fieldNumber, WireType.String);
        using var subMs = new MemoryStream();
        foreach (var v in values)
            ProtoWriter.WriteInt32(subMs, v);
        ProtoWriter.WriteBytes(ms, subMs.ToArray());
    }
}

/// <summary>
/// Reads basic data from an Assetto Corsa .kn5 model file.
/// (Simplified - reads known offsets for vertex data)
/// </summary>
public class Kn5Reader
{
    public Kn5Model ReadModel(string kn5Path)
    {
        var data = File.ReadAllBytes(kn5Path);
        var model = new Kn5Model();

        // kn5 header: "kn5" magic at offset 0, version at offset 4
        if (data.Length < 8) return model;
        if (System.Text.Encoding.ASCII.GetString(data, 0, 3) != "kn5") return model;

        model.Version = BitConverter.ToUInt32(data, 4);

        // Scan through the file for mesh data sections
        // kn5 format uses chunks with headers
        int pos = 8;
        while (pos < data.Length - 12)
        {
            uint chunkId = BitConverter.ToUInt32(data, pos);
            uint chunkSize = BitConverter.ToUInt32(data, pos + 4);

            if (chunkId == 0x4D455348) // "MESH" in ASCII
            {
                if (pos + 8 + chunkSize <= data.Length)
                    ParseMeshChunk(data, pos + 8, (int)chunkSize, model);
            }

            if (chunkSize == 0) break;
            pos += 8 + (int)chunkSize;
        }

        return model;
    }

    private static void ParseMeshChunk(byte[] data, int offset, int size, Kn5Model model)
    {
        // Very simplified: scan for float arrays (vertex positions)
        int vertexCount = 0;
        int indexCount = 0;

        // Look for triangle index patterns (consecutive uint16 values < vertexCount)
        for (int i = offset; i < offset + size - 24; i += 2)
        {
            ushort v0 = BitConverter.ToUInt16(data, i);
            ushort v1 = BitConverter.ToUInt16(data, i + 2);
            ushort v2 = BitConverter.ToUInt16(data, i + 4);

            // Check if these look like valid triangle indices
            if (v0 < 10000 && v1 < 10000 && v2 < 10000 && v0 != v1 && v1 != v2 && v0 != v2)
            {
                indexCount += 3;
            }
        }

        if (indexCount > 0)
        {
            // Found indices - extract them
            var indices = new List<int>();
            for (int i = offset; i < offset + size - 6 && indices.Count < indexCount; i += 2)
            {
                ushort v = BitConverter.ToUInt16(data, i);
                if (v < 10000)
                    indices.Add(v);
                else
                    break;
            }

            // Try to find vertex positions (look for float triples)
            var positions = new List<float>();
            for (int i = offset + indices.Count * 2 + 4; i < offset + size - 12; i += 12)
            {
                float x = BitConverter.ToSingle(data, i);
                float y = BitConverter.ToSingle(data, i + 4);
                float z = BitConverter.ToSingle(data, i + 8);

                if (float.IsNormal(x) || float.IsNormal(y) || float.IsNormal(z))
                {
                    positions.Add(x);
                    positions.Add(y);
                    positions.Add(z);
                }
                else break;
            }

            model.Vertices = positions.ToArray();
            model.Indices = indices.ToArray();
        }
    }
}

public class Kn5Model
{
    public uint Version { get; set; }
    public float[] Vertices { get; set; } = Array.Empty<float>();
    public int[] Indices { get; set; } = Array.Empty<int>();
    public float[] Normals { get; set; } = Array.Empty<float>();
    public float[] UVs { get; set; } = Array.Empty<float>();
}

/// <summary>
/// Quick helper for writing protobuf primitives without the full serializer
/// </summary>
public static class ProtoWriter
{
    public static void WriteFieldProto(Stream stream, int fieldNumber, WireType wireType)
    {
        WriteVarint(stream, (uint)((fieldNumber << 3) | (int)wireType));
    }

    public static void WriteInt32(Stream stream, int value)
    {
        WriteVarint(stream, (uint)value);
    }

    public static void WriteUInt32(Stream stream, uint value)
    {
        WriteVarint(stream, value);
    }

    public static void WriteInt64(Stream stream, long value)
    {
        WriteVarint64(stream, (ulong)value);
    }

    public static void WriteString(Stream stream, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        WriteBytes(stream, bytes);
    }

    public static void WriteBytes(Stream stream, byte[] data)
    {
        WriteVarint(stream, (uint)data.Length);
        stream.Write(data, 0, data.Length);
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

    private static void WriteVarint64(Stream stream, ulong value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        stream.WriteByte((byte)value);
    }
}

public enum WireType
{
    Varint = 0,
    Fixed64 = 1,
    String = 2,
    Fixed32 = 5
}
