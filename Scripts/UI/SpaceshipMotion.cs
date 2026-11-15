#nullable enable
using Godot;

/// <summary>
/// Ambient motion for the menu spaceship.
/// X and Y follow sines with different frequencies, so the ship accelerates
/// towards one side, slows down at the turn and keeps going: a natural float.
///
/// This node owns the ship's Position so several effects can be combined smoothly:
///   BasePosition  - the anchor. Scene code can tween it to drive a path
///                   (the departure out of the screen uses it).
///   drift         - the idle floating.
///   impulses      - extra pulls on X used while leaving.
///   ParallaxOffset- subtle offset driven by the mouse (see MouseParallax).
/// </summary>
public partial class SpaceshipMotion : AnimatedSprite2D
{
    [Export] public Vector2 Amplitude = new Vector2(700f, 320f);
    [Export] public Vector2 Speed = new Vector2(0.18f, 0.11f);
    [Export] public float TiltAmount = 0.05f;

    /// <summary>Anchor of the ship. Tween this to move it along a path.</summary>
    [Export] public Vector2 BasePosition { get; set; }

    /// <summary>Amplitude of the small tugs applied on X while departing.</summary>
    [Export] public float ImpulseStrength = 46f;
    [Export] public float ImpulseFrequency = 5.2f;

    /// <summary>Second, faster tug layered on top of the first one.</summary>
    [Export] public float TugStrength = 16f;
    [Export] public float TugFrequency = 11.3f;

    /// <summary>Time used to ramp the tugs in, so nothing snaps at the start.</summary>
    [Export] public float ImpulseRampTime = 0.5f;

    /// <summary>Offset applied by the mouse parallax effect.</summary>
    public Vector2 ParallaxOffset { get; set; } = Vector2.Zero;

    private float _time;
    private bool _departing;
    private float _departureTime;

    public override void _Ready()
    {
        BasePosition = Position;
    }

    /// <summary>Enables the extra pulls used while the ship leaves the screen.</summary>
    public void StartDeparture()
    {
        _departing = true;
        _departureTime = 0f;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;

        float driftX = Mathf.Sin(_time * Speed.X * Mathf.Tau) * Amplitude.X;
        float driftY = Mathf.Sin(_time * Speed.Y * Mathf.Tau) * Amplitude.Y;

        // Small pulls on X while leaving. Both sines are interpolated and the
        // result is ramped in, so the movement never jumps or breaks the drift.
        float impulseX = 0f;
        if (_departing)
        {
            _departureTime += dt;
            float ramp = Mathf.Min(1f, _departureTime / Mathf.Max(ImpulseRampTime, 0.01f));
            impulseX = (Mathf.Sin(_departureTime * ImpulseFrequency) * ImpulseStrength
                      + Mathf.Sin(_departureTime * TugFrequency + 1.7f) * TugStrength) * ramp;
        }

        Position = BasePosition
                 + new Vector2(driftX + impulseX, driftY)
                 + ParallaxOffset;

        if (Amplitude.X > 0f)
            Rotation = (driftX / Amplitude.X) * TiltAmount;
    }
}
