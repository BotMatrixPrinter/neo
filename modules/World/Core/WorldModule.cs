using Neo.Shell;

namespace Neo.World;

/// <summary>
/// The World module: the countryside you fly over. It has no rules yet, so it does
/// nothing per tick; it exists so the shell loads its scene (Godot/Main.tscn).
/// </summary>
public sealed class WorldModule : IModule
{
    /// <summary>Whether <see cref="Start"/> has run.</summary>
    public bool Started { get; private set; }

    /// <summary>Steps seen since start.</summary>
    public long Steps { get; private set; }

    public void Start(IMessageBus bus) => Started = true;

    public void Tick(in TickContext tick) => Steps++;
}
