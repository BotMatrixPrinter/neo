using System.Numerics;

namespace Neo.Targets;

/// <summary>
/// What the Godot view shows for each balloon, followed from <see cref="TargetsModule"/> state
/// (no Godot here, so it can be tested): where the balloon is, where its cable runs (from under
/// the body down to its anchor on the terrain), whether its shape (body, basket
/// and cable) is shown, and the age of its pop effect. A pop hides the shape and starts the
/// effect; when the balloon comes back the shape is shown again at its spot. Allocation-free
/// per <see cref="Update"/>.
/// </summary>
public sealed class BalloonViews
{
    /// <summary>How long the pop effect (smoke puffs and shreds) plays, in seconds.</summary>
    public const float PopSeconds = 1.4f;

    /// <summary>Where the cable leaves the balloon, from its center (m): under the body, behind the basket.</summary>
    public static readonly Vector3 CableAttach = new(0f, -4.5f, -2f);

    private readonly TargetsModule _targets;
    private readonly Vector3[] _positions = new Vector3[TargetsModule.Count];
    private readonly bool[] _shapeVisible = new bool[TargetsModule.Count];
    private readonly float[] _popAge = new float[TargetsModule.Count];

    public BalloonViews(TargetsModule targets)
    {
        _targets = targets;
        for (int i = 0; i < TargetsModule.Count; i++)
        {
            var state = targets.GetState(i);
            _positions[i] = state.Position;
            _shapeVisible[i] = state.Alive;
            _popAge[i] = -1;
        }
    }

    /// <summary>Where balloon <paramref name="i"/> is drawn, in meters.</summary>
    public Vector3 Position(int i) => _positions[i];

    /// <summary>Top end of balloon <paramref name="i"/>'s cable, under its body; it sways with the balloon.</summary>
    public Vector3 CableTop(int i) => _positions[i] + CableAttach;

    /// <summary>
    /// Bottom end of balloon <paramref name="i"/>'s cable (where its winch would stand): fixed
    /// on the ground under its tether point, <see cref="TargetsModule.Anchor"/>.
    /// </summary>
    public Vector3 CableBottom(int i) => TargetsModule.Anchor(i);

    /// <summary>Whether balloon <paramref name="i"/>'s body, basket and cable are shown.</summary>
    public bool ShapeVisible(int i) => _shapeVisible[i];

    /// <summary>Whether balloon <paramref name="i"/>'s pop effect is playing.</summary>
    public bool PopPlaying(int i) => _popAge[i] >= 0;

    /// <summary>Seconds since balloon <paramref name="i"/>'s pop effect started; -1 when not playing.</summary>
    public float PopAge(int i) => _popAge[i];

    /// <summary>Follows the Core state after <paramref name="delta"/> seconds of frame time.</summary>
    public void Update(float delta)
    {
        for (int i = 0; i < TargetsModule.Count; i++)
        {
            var state = _targets.GetState(i);
            _positions[i] = state.Position;
            if (state.Alive != _shapeVisible[i])
            {
                _shapeVisible[i] = state.Alive;
                _popAge[i] = state.Alive ? -1 : 0;
            }
            if (_popAge[i] >= 0)
            {
                _popAge[i] += delta;
                if (_popAge[i] >= PopSeconds)
                    _popAge[i] = -1;
            }
        }
    }
}
