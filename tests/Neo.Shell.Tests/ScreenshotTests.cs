using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace Neo.Shell.Tests;

// screenshot.sh renders under Xvfb and is not run by test.sh; these check its tool is in place,
// which renderer it asks for (with a fake godot), where it saves (-n: path only), and that no
// screenshot is committed.
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
    public void ScreenshotScriptRendersUnderXvfbAndIsExecutable()
    {
        var root = RepoRoot();
        var script = File.ReadAllText(Path.Combine(root, "screenshot.sh"));
        Assert.Contains("xvfb-run", script);
        Assert.Contains("--resolution 1920x1080", script);
        Assert.Contains("NEO_RENDERER", File.ReadAllText(Path.Combine(root, "shell", "Godot", "Screenshot.gd")));
        if (!OperatingSystem.IsWindows())
            Assert.True((File.GetUnixFileMode(Path.Combine(root, "screenshot.sh")) & UnixFileMode.UserExecute) != 0,
                "screenshot.sh is not executable");
    }

    // Runs screenshot.sh with fake dotnet, xvfb-run and godot first on PATH. The fake godot logs its
    // --rendering-driver and, per mode, renders with Vulkan ("vulkan"), switches to opengl3 itself
    // as real Godot does when Vulkan fails to start ("self-fallback"), or crashes before rendering
    // with Vulkan ("vulkan-crash"). Returns screenshot.sh's exit code, output, the drivers asked for.
    private static (int Code, string Output, string[] Drivers) RunWithFakeGodot(string mode, string screenshotDir)
    {
        var bin = Directory.CreateTempSubdirectory("neo-fake-godot-").FullName;
        var driversLog = Path.Combine(bin, "drivers.log");
        void Script(string name, string body)
        {
            var path = Path.Combine(bin, name);
            File.WriteAllText(path, "#!/usr/bin/env bash\n" + body);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        Script("dotnet", "exit 0\n");
        Script("xvfb-run", "while [ \"$1\" != godot ]; do shift; done\nexec \"$@\"\n");
        Script("godot", $$"""
            driver=""; user=()
            while [ $# -gt 0 ]; do
              case "$1" in
                --rendering-driver) driver="$2"; shift ;;
                --) shift; user=("$@"); break ;;
              esac
              shift
            done
            echo "$driver" >>"{{driversLog}}"
            case "{{mode}}:$driver" in
              vulkan-crash:vulkan) echo "ERROR: Vulkan device lost"; exit 134 ;;
              self-fallback:vulkan) driver=opengl3; method=gl_compatibility ;;
              *:vulkan) method=forward_plus ;;
              *) method=gl_compatibility ;;
            esac
            echo "NEO_RENDERER $driver $method"
            echo png >"${user[1]}"
            echo "NEO_SCREENSHOT ${user[1]} 1920x1080"
            """);
        try
        {
            var info = new ProcessStartInfo("bash", "screenshot.sh")
            {
                WorkingDirectory = RepoRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            info.Environment["PATH"] = bin + ":" + info.Environment["PATH"];
            info.Environment["SCREENSHOT_DIR"] = screenshotDir;
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            var drivers = File.Exists(driversLog)
                ? File.ReadAllLines(driversLog)
                : Array.Empty<string>();
            return (process.ExitCode, output, drivers);
        }
        finally
        {
            Directory.Delete(bin, true);
        }
    }

    [Theory]
    [InlineData("vulkan", new[] { "vulkan" }, false)]
    [InlineData("self-fallback", new[] { "vulkan" }, true)]
    [InlineData("vulkan-crash", new[] { "vulkan", "opengl3" }, true)]
    public void ScreenshotRendersWithVulkanFallingBackToOpenGlOnlyIfVulkanFailsToStart(
        string mode, string[] drivers, bool fellBack)
    {
        if (OperatingSystem.IsWindows())
            return;
        var dir = Directory.CreateTempSubdirectory("neo-screenshots-").FullName;
        try
        {
            var (code, output, asked) = RunWithFakeGodot(mode, dir);
            Assert.True(code == 0, $"screenshot.sh exited with {code}: {output}");
            Assert.Equal(drivers, asked);
            Assert.True(File.Exists(Path.Combine(dir, "Main.png")), output);
            Assert.Contains($"== saved {dir}/Main.png", output);
            Assert.Equal(fellBack, output.Contains("Vulkan failed to start"));
            if (!fellBack)
                Assert.Contains("rendered with Vulkan (vulkan forward_plus)", output);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
