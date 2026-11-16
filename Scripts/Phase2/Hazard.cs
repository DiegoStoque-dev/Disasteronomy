#nullable enable
using Godot;

/// <summary>
/// Level 2 shock hazard: an electric barrier animated with
/// Sprites/map2/ElectricBarrier_sheet.png (160x16 = 5 frames of 32x16, 100 ms
/// per frame). Only DANGEROUS while showing an active frame: frames 1-4 are ON
/// and frame 0 is the OFF spark. Collision box is 32x14 (art px, scaled 2x).
/// </summary>
public partial class Hazard : Area2D
{
    private const string BarrierTexturePath = "res://Sprites/map2/ElectricBarrier_sheet.png";
    private const float FrameTime = 0.1f;   // 100 ms per frame.
    private const int FrameCount = 5;
    private const float OnStartFrame = 1f;  // frames 1..4 are ON; frame 0 is OFF.

    private Sprite2D? sprite;
    private float time;

    public void Setup(Vector2 pos)
    {
        Position = pos;
    }

    public override void _Ready()
    {
        Texture2D? tex = GD.Load<Texture2D>(BarrierTexturePath);
        if (tex != null)
        {
            // Hframes on a Sprite2D shows a single 32x16 frame selected by Frame.
            sprite = new Sprite2D
            {
                Texture = tex,
                Hframes = FrameCount,
                Scale = Vector2.One * 2f, // same art scale as the lobby
            };
            AddChild(sprite);
        }

        var shape = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(64f, 28f) }, // 32x14 art px * 2
        };
        AddChild(shape);
    }

    public override void _Process(double delta)
    {
        if (sprite == null) return;
        time += (float)delta;
        sprite.Frame = ((int)(time / FrameTime)) % FrameCount;
    }

    /// <summary>True while an active frame is shown (1-4); the frame 0 spark is harmless.</summary>
    public bool IsOn => sprite != null && sprite.Frame >= OnStartFrame;
}