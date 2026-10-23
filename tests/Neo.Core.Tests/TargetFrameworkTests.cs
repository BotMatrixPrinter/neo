using System;
using System.Reflection;
using System.Runtime.Versioning;
using Neo.Core.Clock;
using Xunit;

namespace Neo.Core.Tests;

// The repo targets .NET 10 LTS; these fail if a project falls back to an older framework.
public class TargetFrameworkTests
{
    [Fact]
    public void CoreTargetsNet10()
    {
        var attr = typeof(FixedClock).Assembly.GetCustomAttribute<TargetFrameworkAttribute>();
        Assert.Equal(".NETCoreApp,Version=v10.0", attr?.FrameworkName);
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
}
