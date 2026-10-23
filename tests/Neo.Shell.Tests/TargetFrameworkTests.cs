using System;
using System.Reflection;
using System.Runtime.Versioning;
using Neo.Shell.Host;
using Xunit;

namespace Neo.Shell.Tests;

// The repo targets .NET 10 LTS; these fail if a project falls back to an older framework.
public class TargetFrameworkTests
{
    [Fact]
    public void ShellTargetsNet10()
    {
        foreach (var assembly in new[] { typeof(IModule).Assembly, typeof(ModuleHost).Assembly })
            Assert.Equal(".NETCoreApp,Version=v10.0", assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName);
    }

    [Fact]
    public void TestsTargetNet10()
    {
        var attr = typeof(TargetFrameworkTests).Assembly.GetCustomAttribute<TargetFrameworkAttribute>();
        Assert.Equal(".NETCoreApp,Version=v10.0", attr?.FrameworkName);
    }

    [Fact]
    public void TestsRunOnNet10Runtime()
    {
        Assert.Equal(10, Environment.Version.Major);
    }

    [Fact]
    public void ShellDoesNotReferenceGodot()
    {
        foreach (var assembly in new[] { typeof(IModule).Assembly, typeof(ModuleHost).Assembly })
            foreach (var name in assembly.GetReferencedAssemblies())
                Assert.DoesNotContain("Godot", name.Name);
    }
}
