#nullable enable
using Godot;

/// <summary>
/// Simple drifting motion for the main menu spaceship.
/// X and Y follow sines with different frequencies, so the ship accelerates
/// towards one side, slows down at the turn and keeps going, giving the
/// impression of floating/drifting through space.
/// Values are in the node's local space (the parent "background" has scale 0.1,
/// so 700 local units are about 70 screen pixels).
/// </summary>
public partial class SpaceshipMotion : AnimatedSprite2D
{
    [Export] public Vector2 Amplitude = new Vector2(700f, 320f);

    /// <summary>Frequency in cycles per second, one value per axis.</summary>
    [Export] public Vector2 Speed = new Vector2(0.18f, 0.11f);

    /// <summary>Maximum tilt in radians when the ship is at the sides.</summary>
    [Export] public float TiltAmount = 0.05f;

    private Vector2 _origin;
    private float _time;

    public override void _Ready()
    {
        _origin = Position;
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;

        float offsetX = Mathf.Sin(_time * Speed.X * Mathf.Tau) * Amplitude.X;
        float offsetY = Mathf.Sin(_time * Speed.Y * Mathf.Tau) * Amplitude.Y;

        Position = _origin + new Vector2(offsetX, offsetY);

        if (Amplitude.X > 0f)
            Rotation = (offsetX / Amplitude.X) * TiltAmount;
    }
}
