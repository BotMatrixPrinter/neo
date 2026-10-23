namespace Neo.Shell;

/// <summary>
/// Marks a module's entry class. Each module's Core project has exactly one public,
/// non-abstract class implementing this, with a public parameterless constructor.
/// The shell finds it at startup; nothing lists modules by name.
/// </summary>
public interface IModule
{
    /// <summary>Called once at startup. Keep <paramref name="bus"/> to publish and subscribe.</summary>
    void Start(IMessageBus bus);

    /// <summary>One fixed simulation step. Runs every physics frame; must not allocate.</summary>
    void Tick(in TickContext tick);
}
