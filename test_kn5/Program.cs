using System;
using System.IO;
using ProtoBuf;
using EVO.ModManager.Core.Services.Implementations;

// Test deserializing the game's reference cardata.car
try {
    var cd = File.ReadAllBytes(@"C:\Users\paul_\OneDrive\Documents\APP\EVO Mod Manager\src\EVO.ModManager.Core\Templates\cardata.car");
    using var ms = new MemoryStream(cd);
    var cardata = Serializer.Deserialize<CarDataProto>(ms);
    Console.Write($"Cardata: ScreenName='{cardata.General.ScreenName}', TotalMass={cardata.General.TotalMass}\n");
    Console.Write($"Suspensions: WheelBase={cardata.Suspensions.WheelBase}\n");
}
catch (Exception ex) {
    Console.Write($"Cardata error: {ex.Message}\n");
}

// Test deserializing the game's reference actor
try {
    var actor = File.ReadAllBytes(@"C:\Users\paul_\OneDrive\Documents\APP\EVO Mod Manager\src\EVO.ModManager.Core\Templates\car.actor");
    using var ms = new MemoryStream(actor);
    var actorData = Serializer.Deserialize<CarActorDataProto>(ms);
    Console.Write($"\nActor: BodyMesh='{actorData.BaseMeshes.BodyMesh}'");
    Console.Write($", LodOut={actorData.LodOutDistance}\n");
    Console.Write($"LodIn: {string.Join(", ", actorData.LodInDistances)}\n");
}
catch (Exception ex) {
    Console.Write($"Actor error: {ex.Message}\n");
}
