using System;
using System.Buffers.Binary;
using System.IO;
using Xunit;

namespace Neo.Shell.Tests;

// screenshot.sh renders under Xvfb and is not run by test.sh; these check its tool is in place
// and that the committed main-scene screenshot is a real 1920x1080 PNG.
public class ScreenshotTests
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "project.godot")))
                return dir.FullName;
        throw new InvalidOperationException("repo root (project.godot) not found");
    }

    [Fact]
    public void MainSceneScreenshotIsCommitted1920x1080Png()
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot(), "docs", "screenshots", "Main.png"));
        Assert.True(bytes.Length > 1000, "screenshot is (nearly) empty");
        Assert.Equal(PngSignature, bytes[..8]);
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(bytes, 12, 4));
        Assert.Equal(1920u, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)));
        Assert.Equal(1080u, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)));
    }

    [Fact]
    public void ScreenshotScriptRendersWithOpenGlUnderXvfb()
    {
        var root = RepoRoot();
        var script = File.ReadAllText(Path.Combine(root, "screenshot.sh"));
        Assert.Contains("xvfb-run", script);
        Assert.Contains("--rendering-driver opengl3", script);
        Assert.Contains("--resolution 1920x1080", script);
        Assert.True(File.Exists(Path.Combine(root, "shell", "Godot", "Screenshot.gd")));
        if (!OperatingSystem.IsWindows())
            Assert.True((File.GetUnixFileMode(Path.Combine(root, "screenshot.sh")) & UnixFileMode.UserExecute) != 0,
                "screenshot.sh is not executable");
    }
}
