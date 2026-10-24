namespace Neo.Damage;

/// <summary>
/// In-flight repair of one plane. Published by the Damage module every fixed step for every
/// plane it knows, in plane id order, right after that plane's <see cref="PlaneDamageState"/>.
/// While <see cref="Repairing"/> is true the pilot is holding Repair: Flight should ignore
/// the pilot's pitch/roll/yaw (throttle still works).
/// </summary>
public readonly struct RepairState
{
    public RepairState(int planeId, bool repairing, PlanePart part, float progress, int partsLeft)
    {
        PlaneId = planeId;
        Repairing = repairing;
        Part = part;
        Progress = progress;
        PartsLeft = partsLeft;
    }

    public int PlaneId { get; }

    /// <summary>A repair cycle is running this step (Repair held, plane alive, something damaged).</summary>
    public bool Repairing { get; }

    /// <summary>The part the current cycle fixes; meaningful only while <see cref="Repairing"/>.</summary>
    public PlanePart Part { get; }

    /// <summary>The current cycle's progress, 0..1 (0 when not repairing).</summary>
    public float Progress { get; }

    /// <summary>Parts below full health, including the one being repaired.</summary>
    public int PartsLeft { get; }
}
