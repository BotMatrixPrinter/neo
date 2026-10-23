using System;
using System.Collections.Generic;
using System.Reflection;

namespace Neo.Shell.Host;

/// <summary>
/// Finds, starts and ticks modules, each inside its own guard: a module that throws
/// (while being created, in Start, in Tick or in one of its message handlers) is disabled
/// and logged, and every other module goes on. No Godot here, so it is unit-tested.
/// </summary>
public sealed class ModuleHost
{
    /// <summary>Start of the log line written when a module is disabled (test.sh looks for it).</summary>
    public const string DisabledMarker = "NEO_MODULE_DISABLED";

    private readonly List<ModuleSlot> _slots = new();
    private readonly Action<string> _log;
    private long _tick;

    public ModuleHost(MessageBus bus, Action<string> log)
    {
        Bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        Bus.HandlerFaulted += (owner, e) => Disable(_slots[owner], "a message handler", e);
    }

    public MessageBus Bus { get; }

    public IReadOnlyList<ModuleSlot> Modules => _slots;

    /// <summary>Steps ticked so far.</summary>
    public long Tick => _tick;

    /// <summary>Raised once when a module is disabled.</summary>
    public event Action<ModuleSlot>? ModuleDisabled;

    /// <summary>Adds the module whose entry class is in <paramref name="coreAssembly"/>.</summary>
    public ModuleSlot Add(string name, Assembly coreAssembly)
    {
        Type[] types;
        try
        {
            types = coreAssembly.GetExportedTypes();
        }
        catch (Exception e)
        {
            return AddFailed(name, "loading", e);
        }
        return Add(name, types);
    }

    /// <summary>Adds the module whose entry class is the one <see cref="IModule"/> class among <paramref name="types"/>.</summary>
    public ModuleSlot Add(string name, IEnumerable<Type> types)
    {
        Type? entry = null;
        foreach (var type in types)
        {
            if (!type.IsClass || type.IsAbstract || !typeof(IModule).IsAssignableFrom(type))
                continue;
            if (entry != null)
                return AddFailed(name, "discovery", new InvalidOperationException(
                    $"more than one IModule class ({entry.FullName}, {type.FullName}); a module has exactly one"));
            entry = type;
        }
        if (entry == null)
            return AddFailed(name, "discovery", new InvalidOperationException("no public IModule class in its Core project"));
        return Add(name, () => (IModule)Activator.CreateInstance(entry)!);
    }

    /// <summary>Adds a module made by <paramref name="create"/>; a throw disables it.</summary>
    public ModuleSlot Add(string name, Func<IModule> create)
    {
        var slot = new ModuleSlot(_slots.Count, name) { Enabled = true };
        _slots.Add(slot);
        try
        {
            slot.Module = create();
        }
        catch (Exception e)
        {
            Disable(slot, "its constructor", e is TargetInvocationException { InnerException: { } inner } ? inner : e);
        }
        return slot;
    }

    /// <summary>Calls every enabled module's Start with its own view of the bus.</summary>
    public void Start()
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            if (!slot.Enabled)
                continue;
            try
            {
                slot.Module!.Start(Bus.For(slot.Index));
            }
            catch (Exception e)
            {
                Disable(slot, "Start", e);
            }
        }
    }

    /// <summary>One fixed step for every enabled module. Does not allocate unless a module fails.</summary>
    public void Step(double stepSeconds)
    {
        var context = new TickContext(++_tick, stepSeconds);
        for (int i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            if (!slot.Enabled)
                continue;
            try
            {
                slot.Module!.Tick(in context);
            }
            catch (Exception e)
            {
                Disable(slot, "Tick", e);
            }
        }
    }

    private ModuleSlot AddFailed(string name, string phase, Exception e)
    {
        var slot = new ModuleSlot(_slots.Count, name) { Enabled = true };
        _slots.Add(slot);
        Disable(slot, phase, e);
        return slot;
    }

    private void Disable(ModuleSlot slot, string phase, Exception e)
    {
        if (!slot.Enabled)
            return;
        slot.Enabled = false;
        slot.Fault = e;
        Bus.Mute(slot.Index);
        _log($"{DisabledMarker} {slot.Name}: failed in {phase}, disabled; the game goes on. {e}");
        ModuleDisabled?.Invoke(slot);
    }
}
