using System;

namespace Neo.Shell.Host;

/// <summary>One module as the host runs it.</summary>
public sealed class ModuleSlot
{
    internal ModuleSlot(int index, string name)
    {
        Index = index;
        Name = name;
    }

    /// <summary>Position in <see cref="ModuleHost.Modules"/>; also its owner id on the bus.</summary>
    public int Index { get; }

    /// <summary>The module's folder name.</summary>
    public string Name { get; }

    /// <summary>The module's entry object; null if it could not be found or created.</summary>
    public IModule? Module { get; internal set; }

    /// <summary>False once the module failed; it is never called again.</summary>
    public bool Enabled { get; internal set; }

    /// <summary>Why it was disabled, if it was.</summary>
    public Exception? Fault { get; internal set; }
}
