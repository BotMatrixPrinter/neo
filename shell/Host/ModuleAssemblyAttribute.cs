using System;

namespace Neo.Shell.Host;

/// <summary>
/// Names one module's Core assembly and its folder under <c>modules/</c>. Neo.csproj adds
/// one per <c>modules/*/Core/*.csproj</c> at build time, so no file lists modules by hand.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ModuleAssemblyAttribute : Attribute
{
    public ModuleAssemblyAttribute(string assemblyName, string moduleName)
    {
        AssemblyName = assemblyName;
        ModuleName = moduleName;
    }

    public string AssemblyName { get; }

    /// <summary>The module's folder name, <c>modules/&lt;ModuleName&gt;/</c>.</summary>
    public string ModuleName { get; }
}
