using EVO.ModManager.Core.Services.Implementations;

var dir = @"C:\Users\paul_\AppData\Local\Temp\EVOMM\convert\cars_ACE\ford_transit";

foreach (var f in System.IO.Directory.GetFiles(dir, "*.kn5"))
{
    var fi = new System.IO.FileInfo(f);
    var parser = new Kn5Parser();
    var result = parser.Parse(System.IO.File.ReadAllBytes(f));
    System.Console.Write($"{fi.Name}: chunkParse={result.Success}, ver={result.Version}, meshes={result.Meshes.Count}\n");
    
    if (!result.Success)
    {
        // Try with fallback
        var result2 = parser.ParseWithFallback(f);
        System.Console.Write($"  fallback: {result2.Success}, meshes={result2.Meshes.Count}\n");
    }
}
