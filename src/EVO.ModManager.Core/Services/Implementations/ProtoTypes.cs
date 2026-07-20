using ProtoBuf;

namespace EVO.ModManager.Core.Services.Implementations;

/// <summary>
/// Auto-generated from Mesh.proto and Scene.proto (via protodump)
/// </summary>

// ====== Math types ======
[ProtoContract]
public class Vector3DataProto
{
    [ProtoMember(1)] public float X { get; set; }
    [ProtoMember(2)] public float Y { get; set; }
    [ProtoMember(3)] public float Z { get; set; }
}

// ====== Mesh types ======
[ProtoContract]
public class MeshLodDataProto
{
    [ProtoMember(1)] public bool CastShadows { get; set; }
    [ProtoMember(4)] public List<MeshBatchProto> Batches { get; set; } = new();
    [ProtoMember(5)] public List<float> Positions { get; set; } = new();
    [ProtoMember(6)] public List<float> Normals { get; set; } = new();
    [ProtoMember(7)] public List<float> Texcoords { get; set; } = new();
    [ProtoMember(11)] public List<uint> Indices { get; set; } = new();
    [ProtoMember(17)] public Vector3DataProto BoundsMin { get; set; } = new();
    [ProtoMember(18)] public Vector3DataProto BoundsMax { get; set; } = new();
}

[ProtoContract]
public class MeshBatchProto
{
    [ProtoMember(1)] public string Name { get; set; } = "";
    [ProtoMember(2)] public int StartIndex { get; set; }
    [ProtoMember(3)] public int IndexCount { get; set; }
    [ProtoMember(4)] public string Material { get; set; } = "";
}

[ProtoContract]
public class MeshDataProto
{
    [ProtoMember(1)] public ImportSettingsProto ImportSettings { get; set; } = new();
    [ProtoMember(2)] public bool IsVisible { get; set; } = true;
    [ProtoMember(3)] public bool IsRenderable { get; set; } = true;
    [ProtoMember(4)] public float LodOut { get; set; } = 1000f;
    [ProtoMember(5)] public List<MeshLodDataProto> Lods { get; set; } = new();
    [ProtoMember(6)] public Vector3DataProto BoundsMin { get; set; } = new();
    [ProtoMember(7)] public Vector3DataProto BoundsMax { get; set; } = new();
    [ProtoMember(14)] public int Type { get; set; } = 4; // MeshType_Car = 4
}

[ProtoContract]
public class ImportSettingsProto
{
    [ProtoMember(1)] public List<string> ImportPaths { get; set; } = new();
    [ProtoMember(5)] public bool CreateDefaultsForMissingMaterials { get; set; } = true;
}

// ====== Scene types ======
[ProtoContract]
public class CarActorDataProto
{
    [ProtoContract]
    public class BaseMeshesProto
    {
        [ProtoContract]
        public class BaseMeshProto
        {
            [ProtoMember(1)] public string MeshPath { get; set; } = "";
            [ProtoMember(4)] public bool ShowroomInvisible { get; set; }
        }

        [ProtoMember(1)] public string BodyMesh { get; set; } = "";
        [ProtoMember(2)] public BaseMeshProto TyreFl { get; set; } = new();
        [ProtoMember(3)] public BaseMeshProto TyreFr { get; set; } = new();
        [ProtoMember(4)] public BaseMeshProto TyreRl { get; set; } = new();
        [ProtoMember(5)] public BaseMeshProto TyreRr { get; set; } = new();
        [ProtoMember(6)] public BaseMeshProto RimFl { get; set; } = new();
        [ProtoMember(7)] public BaseMeshProto RimFr { get; set; } = new();
        [ProtoMember(8)] public BaseMeshProto RimRl { get; set; } = new();
        [ProtoMember(9)] public BaseMeshProto RimRr { get; set; } = new();
        [ProtoMember(10)] public BaseMeshProto Interior { get; set; } = new();
        [ProtoMember(11)] public BaseMeshProto SteeringWheel { get; set; } = new();
    }

    [ProtoMember(12)] public BaseMeshesProto BaseMeshes { get; set; } = new();
    [ProtoMember(5)] public float LodOutDistance { get; set; } = 500f;
    [ProtoMember(6)] public List<float> LodInDistances { get; set; } = new();
}

// ====== CarData types ======
[ProtoContract]
public class GeneralCarDataProto
{
    [ProtoMember(1)] public float TotalMass { get; set; } = 1200f;
    [ProtoMember(2)] public string ScreenName { get; set; } = "";
    [ProtoMember(5)] public float Fuel { get; set; } = 60f;
    [ProtoMember(6)] public float MaxFuel { get; set; } = 60f;
}

[ProtoContract]
public class SuspensionsDataProto
{
    [ProtoMember(2)] public float WheelBase { get; set; } = 2.5f;
    [ProtoMember(52)] public float TrackFront { get; set; } = 1.5f;
    [ProtoMember(53)] public float TrackRear { get; set; } = 1.5f;
}

[ProtoContract]
public class CarDataProto
{
    [ProtoMember(1)] public GeneralCarDataProto General { get; set; } = new();
    [ProtoMember(2)] public SuspensionsDataProto Suspensions { get; set; } = new();
    [ProtoMember(202)] public string ScreenName { get; set; } = "";
}

