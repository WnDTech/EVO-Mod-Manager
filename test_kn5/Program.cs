using System;
using System.IO;
using ProtoBuf;
using EVO.ModManager.Core.Services.Implementations;

// Dynamic approach: use protobuf-net's MetaType to build a model that can read ANY proto
// This allows us to read the template, modify strings, and re-serialize

// Step 1: Read the template actor file as raw bytes
var templatePath = @"C:\Users\paul_\OneDrive\Documents\APP\EVO Mod Manager\src\EVO.ModManager.Core\Templates\sd_banana.actor";
var templateData = File.ReadAllBytes(templatePath);

// Step 2: Replace all "sd_banana" with "carName" in the binary data
// We'll use a 9-char name that matches "sd_banana" length
var srcBytes = System.Text.Encoding.ASCII.GetBytes("sd_banana");
var dstBytes = System.Text.Encoding.ASCII.GetBytes("CUSTOMCAR");  // 9 chars

var result = new byte[templateData.Length];
Buffer.BlockCopy(templateData, 0, result, 0, templateData.Length);

int replaceCount = 0;
for (int i = 0; i < result.Length - 9; i++)
{
    bool match = true;
    for (int j = 0; j < 9; j++) { if (result[i + j] != srcBytes[j]) { match = false; break; } }
    if (match) { for (int j = 0; j < 9; j++) { result[i + j] = dstBytes[j]; } replaceCount++; }
}

Console.Write($"Replaced {replaceCount} occurrences of 'sd_banana' -> 'CUSTOMCAR'\n");
Console.Write($"Output size: {result.Length} bytes\n");
File.WriteAllBytes(@"C:\Users\paul_\OneDrive\Documents\APP\EVO Mod Manager\src\EVO.ModManager.Core\Templates\actor_patched.bin", result);

// Step 3: Also patch cardata.car
var cdPath = @"C:\Users\paul_\OneDrive\Documents\APP\EVO Mod Manager\src\EVO.ModManager.Core\Templates\cardata.car";
var cdData = File.ReadAllBytes(cdPath);
var cdResult = new byte[cdData.Length];
Buffer.BlockCopy(cdData, 0, cdResult, 0, cdData.Length);

int cdCount = 0;
for (int i = 0; i < cdResult.Length - 9; i++)
{
    bool match = true;
    for (int j = 0; j < 9; j++) { if (cdResult[i + j] != srcBytes[j]) { match = false; break; } }
    if (match) { for (int j = 0; j < 9; j++) { cdResult[i + j] = dstBytes[j]; } cdCount++; }
}

Console.Write($"Cardata: replaced {cdCount} occurrences\n");
File.WriteAllBytes(@"C:\Users\paul_\OneDrive\Documents\APP\EVO Mod Manager\src\EVO.ModManager.Core\Templates\cardata_patched.bin", cdResult);
