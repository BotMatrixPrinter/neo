using System;
using System.Reflection;
using System.Runtime.Loader;
using Godot;
using Neo.Shell.Host;

namespace Neo.Shell;

/// <summary>
/// Root of the main scene. Finds every module (Neo.csproj names each modules/*/Core
/// assembly), starts it, loads modules/&lt;Name&gt;/Godot/Main.tscn if there is one, and
/// runs each module's tick on Godot's fixed physics step, each in its own guard.
/// </summary>
public partial class Bootstrap : Node
{
    private ModuleHost _host = null!;
    private Node?[] _views = Array.Empty<Node?>();

    public override void _Ready()
    {
        _host = new ModuleHost(new MessageBus(), message => GD.Print(message));
        _host.ModuleDisabled += OnModuleDisabled;

        var context = AssemblyLoadContext.GetLoadContext(typeof(Bootstrap).Assembly) ?? AssemblyLoadContext.Default;
        foreach (var module in typeof(Bootstrap).Assembly.GetCustomAttributes<ModuleAssemblyAttribute>())
        {
            Assembly core;
            try
            {
                core = context.LoadFromAssemblyName(new AssemblyName(module.AssemblyName));
            }
            catch (Exception e)
            {
                _host.Add(module.ModuleName, () => throw new InvalidOperationException("cannot load " + module.AssemblyName, e));
                continue;
            }
            _host.Add(module.ModuleName, core);
        }
        _host.Start();
        foreach (var slot in _host.Modules)
            if (slot.Enabled)
                GD.Print("NEO_MODULE_READY " + slot.Name);

        _views = new Node?[_host.Modules.Count];
        foreach (var slot in _host.Modules)
        {
            string scene = "res://modules/" + slot.Name + "/Godot/Main.tscn";
            if (!slot.Enabled || !ResourceLoader.Exists(scene))
                continue;
            var view = GD.Load<PackedScene>(scene).Instantiate();
            _views[slot.Index] = view;
            if (view is IModuleView bound)
            {
                try
                {
                    bound.Bind(slot.Module!);
                }
                catch (Exception e)
                {
                    GD.Print($"{ModuleHost.DisabledMarker} {slot.Name}: its view failed to bind, view not shown. {e}");
                    view.Free();
                    _views[slot.Index] = null;
                    continue;
                }
            }
            AddChild(view);
        }

        // test.sh looks for this line to know the main scene and its C# loaded.
        GD.Print("NEO_READY modules=" + _host.Modules.Count);
    }

    public override void _PhysicsProcess(double delta) => _host.Step(delta);

    private void OnModuleDisabled(ModuleSlot slot)
    {
        if (slot.Index < _views.Length && _views[slot.Index] is { } view)
        {
            // A dead module's view stops: it would only show stale or broken state.
            view.ProcessMode = ProcessModeEnum.Disabled;
            if (view is CanvasItem item)
                item.Visible = false;
        }
    }
}
