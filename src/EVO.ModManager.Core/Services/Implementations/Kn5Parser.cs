using System.Runtime.InteropServices;
using System.Linq;

namespace EVO.ModManager.Core.Services.Implementations;

public class Kn5Parser
{
    public Kn5Result ParseWithFallback(string path)
    {
        var data = File.ReadAllBytes(path);
        var result = Parse(data);
        if (result.Success) return result;

        // Try sc6969 format or general binary scan
        return ParseBinaryScan(data);
    }

    private Kn5Result ParseBinaryScan(byte[] data)
    {
        var result = new Kn5Result();
        var vertices = new List<float>();
        var indices = new List<uint>();

        int searchLimit = Math.Min(data.Length, 5 * 1024 * 1024); // Only scan first 5MB
        int[] strides = { 32, 36, 48, 28, 64 };

        // Scan at multiple strides to find positions
        int bestStride = 32;
        int bestCount = 0;
        int bestStart = 100;

        foreach (var stride in strides)
        {
            int found = 0, firstPos = 0;
            for (int offset = 100; offset < searchLimit - 36; offset += stride)
            {
                float x = BitConverter.ToSingle(data, offset);
                float y = BitConverter.ToSingle(data, offset + 4);
                float z = BitConverter.ToSingle(data, offset + 8);
                if (float.IsNormal(x) && float.IsNormal(y) && float.IsNormal(z) &&
                    Math.Abs(x) < 50 && Math.Abs(y) < 50 && Math.Abs(z) < 50 &&
                    (Math.Abs(x) > 0.001f || Math.Abs(y) > 0.001f || Math.Abs(z) > 0.001f))
                {
                    found++;
                    if (found == 1) firstPos = offset;
                    if (found >= 20) break;
                }
            }
            if (found > bestCount) { bestCount = found; bestStride = stride; bestStart = firstPos; }
        }

        if (bestCount < 3) return result;

        // Extract positions with best stride
        for (int i = 0; i < 3000; i++)
        {
            int off = bestStart + i * bestStride;
            if (off + 12 > data.Length) break;
            float x = BitConverter.ToSingle(data, off);
            float y = BitConverter.ToSingle(data, off + 4);
            float z = BitConverter.ToSingle(data, off + 8);
            if (!float.IsNormal(x) && !float.IsNormal(y) && !float.IsNormal(z) &&
                Math.Abs(x) < 0.001f && Math.Abs(y) < 0.001f && Math.Abs(z) < 0.001f) break;
            vertices.Add(x); vertices.Add(y); vertices.Add(z);
        }

        if (vertices.Count < 9) return result;

        // Find indices
        int idxSearchStart = bestStart + (vertices.Count / 3) * bestStride + 4;
        int maxVert = vertices.Count / 3;
        for (int i = idxSearchStart; i < Math.Min(data.Length, idxSearchStart + 50000) && indices.Count < 600; i += 2)
        {
            ushort i0 = BitConverter.ToUInt16(data, i);
            ushort i1 = BitConverter.ToUInt16(data, i + 2);
            ushort i2 = BitConverter.ToUInt16(data, i + 4);
            if (i0 < maxVert && i1 < maxVert && i2 < maxVert && i0 != i1 && i1 != i2 && i0 != i2)
                { indices.Add(i0); indices.Add(i1); indices.Add(i2); i += 4; }
        }

        if (indices.Count == 0)
            for (int i = 0; i + 2 < vertices.Count / 3; i += 3)
                { indices.Add((uint)i); indices.Add((uint)(i + 1)); indices.Add((uint)(i + 2)); }

        result.Success = true;
        result.Meshes.Add(new Kn5MeshData
        {
            Name = "converted",
            Vertices = vertices.ToArray(),
            Normals = Enumerable.Repeat(0f, vertices.Count).ToArray(),
            UVs = Enumerable.Repeat(0f, (vertices.Count / 3) * 2).ToArray(),
            Indices = indices.Select(i => (int)i).ToArray()
        });
        return result;
    }
    public Kn5Result Parse(string path)
    {
        var data = File.ReadAllBytes(path);
        return Parse(data);
    }

    public Kn5Result Parse(byte[] data)
    {
        var result = new Kn5Result { Success = false };

        if (data.Length < 8) return result;
        var magic = System.Text.Encoding.ASCII.GetString(data, 0, 3);
        if (magic != "kn5") return result;

        result.Version = BitConverter.ToUInt32(data, 4);

        // Read chunks after header (header is 8 bytes or more)
        int pos = 8;
        while (pos + 8 <= data.Length)
        {
            uint chunkId = BitConverter.ToUInt32(data, pos);
            uint chunkSize = BitConverter.ToUInt32(data, pos + 4);
            uint chunkEnd = (uint)pos + 8 + chunkSize;

            if (chunkSize == 0 || chunkEnd > data.Length) break;

            // Look for mesh nodes in the node hierarchy
            // The kn5 structure has nodes that can be meshes, materials, textures, etc.
            if (chunkId == 1) // Node section
            {
                ParseNodes(data, pos + 8, (int)chunkSize, result);
            }

            pos = (int)chunkEnd;
        }

        return result;
    }

    private void ParseNodes(byte[] data, int offset, int size, Kn5Result result)
    {
        // Node format:
        // 4 bytes: node name length (or 0 for end of siblings)
        // N bytes: node name (UTF-8)
        // 4 bytes: node class ID
        //   0 = BaseNode, 1 = MeshNode, 3 = SkinnedMesh, 4 = Camera, etc.
        // Then node-specific data depending on class
        
        int pos = offset;
        int end = offset + size;

        while (pos + 4 <= end)
        {
            int nameLen = BitConverter.ToInt32(data, pos);
            if (nameLen <= 0 || nameLen > 200) break;

            pos += 4;
            string nodeName = System.Text.Encoding.UTF8.GetString(data, pos, nameLen);
            pos += nameLen;

            if (pos + 4 > end) break;
            int nodeClass = BitConverter.ToInt32(data, pos);
            pos += 4;

            // Skip node transform (4x4 matrix = 64 bytes)
            pos += 64;
            if (pos > end) break;

            // Skip children count + active flag
            pos += 8;
            if (pos > end) break;

            if (nodeClass == 1) // Mesh node
            {
                ParseMeshNode(data, pos, end - pos, nodeName, result);
            }

            // Skip material reference
            pos += 4;
            if (pos > end) break;

            // Skip additional node data based on class
            if (nodeClass == 1)
            {
                // Mesh node has LOD data reference
                pos += 4; // mesh LOD index
                pos += 4; // cast shadows flag
                pos += 4; // transparency flag
                pos += 4; // render order
                // Skip non-visible parts
                if (pos > end) break;
                // Collider meshes have additional data
                if (pos + 12 <= end)
                {
                    // Check for sub-children
                    int childCount = BitConverter.ToInt32(data, pos);
                    pos += 4;
                    if (childCount > 0)
                    {
                        ParseNodes(data, pos, end - pos, result);
                    }
                }
            }
        }
    }

    private void ParseMeshNode(byte[] data, int offset, int remaining, string nodeName, Kn5Result result)
    {
        // Mesh data blocks:
        // 4 bytes: vertex count
        // Then vertex data array (each vertex = 32 bytes for standard layout)
        //   float[3] position (12 bytes)
        //   float[3] normal (12 bytes)  
        //   float[2] texcoord (8 bytes)
        //   Total: 32 bytes per vertex
        // Then 4 bytes: index count (divide by 3 for triangle count)
        // Then index data (ushort per index, 2 bytes each)

        int pos = offset;
        if (pos + 4 > data.Length) return;
        int vertexCount = BitConverter.ToInt32(data, pos);
        pos += 4;

        var vertices = new List<float>();
        var normals = new List<float>();
        var uvs = new List<float>();

        const int vertexStride = 32; // pos[12] + normal[12] + uv[8] = 32 bytes

        for (int i = 0; i < vertexCount && pos + vertexStride <= data.Length; i++)
        {
            float x = BitConverter.ToSingle(data, pos);
            float y = BitConverter.ToSingle(data, pos + 4);
            float z = BitConverter.ToSingle(data, pos + 8);
            vertices.Add(x); vertices.Add(y); vertices.Add(z);

            float nx = BitConverter.ToSingle(data, pos + 12);
            float ny = BitConverter.ToSingle(data, pos + 16);
            float nz = BitConverter.ToSingle(data, pos + 20);
            normals.Add(nx); normals.Add(ny); normals.Add(nz);

            float u = BitConverter.ToSingle(data, pos + 24);
            float v = BitConverter.ToSingle(data, pos + 28);
            uvs.Add(u); uvs.Add(v);

            pos += vertexStride;
        }

        if (pos + 4 > data.Length) return;
        int indexCount = BitConverter.ToInt32(data, pos);
        pos += 4;

        var indices = new List<uint>();
        for (int i = 0; i < indexCount && pos + 2 <= data.Length; i++)
        {
            ushort idx = BitConverter.ToUInt16(data, pos);
            indices.Add(idx);
            pos += 2;
        }

        if (vertices.Count > 0 && indices.Count > 0)
        {
            result.Meshes.Add(new Kn5MeshData
            {
                Name = nodeName,
                Vertices = vertices.ToArray(),
                Normals = normals.ToArray(),
                UVs = uvs.ToArray(),
                Indices = indices.Select(i => (int)i).ToArray()
            });
            result.Success = true;
        }
    }
}

public class Kn5Result
{
    public bool Success { get; set; }
    public uint Version { get; set; }
    public List<Kn5MeshData> Meshes { get; set; } = new();
}

public class Kn5MeshData
{
    public string Name { get; set; } = "";
    public float[] Vertices { get; set; } = Array.Empty<float>();
    public float[] Normals { get; set; } = Array.Empty<float>();
    public float[] UVs { get; set; } = Array.Empty<float>();
    public int[] Indices { get; set; } = Array.Empty<int>();
}



