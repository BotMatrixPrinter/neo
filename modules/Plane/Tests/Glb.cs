using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Neo.Plane.Tests;

/// <summary>Reads the glTF JSON of the module's .glb models (Models/).</summary>
internal static class Glb
{
    public static JsonDocument ReadJson(PlaneModel model)
    {
        byte[] data = File.ReadAllBytes(Path.Combine(ModuleDir(), "Models", PlaneLook.FileName(model)));
        Assert.Equal("glTF", Encoding.ASCII.GetString(data, 0, 4));
        int length = BitConverter.ToInt32(data, 12);
        Assert.Equal("JSON", Encoding.ASCII.GetString(data, 16, 4));
        return JsonDocument.Parse(new ReadOnlyMemory<byte>(data, 20, length));
    }

    /// <summary>The node named <paramref name="name"/>, if the model has one.</summary>
    public static JsonElement? Node(JsonDocument gltf, string name)
    {
        foreach (var node in gltf.RootElement.GetProperty("nodes").EnumerateArray())
            if (node.TryGetProperty("name", out var n) && n.GetString() == name)
                return node;
        return null;
    }

    // modules/Plane/, found upward from the test binaries.
    private static string ModuleDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Models", "biplane.glb")))
                return dir.FullName;
        throw new FileNotFoundException("modules/Plane/Models/biplane.glb not found above " + AppContext.BaseDirectory);
    }
}
