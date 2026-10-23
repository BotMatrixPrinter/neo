using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace Neo.Shell.Tests;

// screenshot.sh renders under Xvfb and is not run by test.sh; these check its tool is in place,
// where it saves (-n: path only), and that no screenshot is committed.
public class ScreenshotTests
{
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "project.godot")))
                return dir.FullName;
        throw new InvalidOperationException("repo root (project.godot) not found");
    }

    private static string Run(string file, string args, string? screenshotDir)
    {
        var info = new ProcessStartInfo(file, args)
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.Environment.Remove("SCREENSHOT_DIR");
        if (screenshotDir != null)
            info.Environment["SCREENSHOT_DIR"] = screenshotDir;
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{file} {args} exited with {process.ExitCode}: {output}{error}");
        return output;
    }

    [Fact]
    public void NoScreenshotIsTracked()
    {
        var tracked = Run("git", "ls-files", null).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(tracked);
        Assert.DoesNotContain(tracked, f => f.StartsWith("docs/screenshots/") || f.StartsWith("screenshots/")
            || (f.StartsWith("modules/") && Path.GetFileName(f).StartsWith("screenshot") && f.EndsWith(".png")));
    }

    [Theory]
    [InlineData("docs/screenshots/Main.png")]
    [InlineData("screenshots/Main.png")]
    [InlineData("modules/HUD/screenshot.png")]
    [InlineData("modules/HUD/screenshot-overheated.png")]
    public void ScreenshotsAreGitIgnored(string path)
    {
        Assert.Equal(path, Run("git", $"check-ignore --no-index {path}", null).Trim());
    }

    [Theory]
    [InlineData("", "Main")]
    [InlineData("shell/Godot/Main.tscn", "Main")]
    [InlineData("modules/HUD/Godot/Main.tscn", "HUD")]
    [InlineData("res://modules/World/Godot/Main.tscn", "World")]
    [InlineData("modules/HUD/Godot/Main.tscn HUD-overheated", "HUD-overheated")]
    [InlineData("modules/HUD/Godot/Main.tscn HUD-overheated.png", "HUD-overheated")]
    public void ScreenshotGoesToScreenshotDirNamedAfterModuleOrScene(string args, string name)
    {
        if (OperatingSystem.IsWindows())
            return;
        Assert.Equal($"/out/screenshots/{name}.png", Run("bash", $"screenshot.sh -n {args}", "/out/screenshots").Trim());
        Assert.Equal($"screenshots/{name}.png", Run("bash", $"screenshot.sh -n {args}", null).Trim());
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
