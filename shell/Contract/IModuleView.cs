namespace Neo.Shell;

/// <summary>
/// Optional: the root node of a module's <c>Godot/Main.tscn</c> implements this to get
/// its own module's instance (cast it to the Core class). Views only draw, take input
/// and play sound.
/// </summary>
public interface IModuleView
{
    void Bind(IModule module);
}
