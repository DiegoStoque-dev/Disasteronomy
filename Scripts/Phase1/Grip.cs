using Godot;

/// <summary>
/// A single discrete handhold bolted to the outside of the ship (replaces the old
/// continuous handrails). Every grip is a plain point in the middle of the hull;
/// it needs no collisions. The manager owns the layout (image-pixel positions) and
/// creates these at ImageToWorld-converted world positions.
///
/// Flags identify the grip where the player starts (IsStart), the return / airlock
/// grip (IsAirlock) and any grip sitting next to the CPS (IsCps). By default the
/// same grip carries both IsStart and IsAirlock, but the layout allows them apart.
/// </summary>
public partial class Grip : Node2D
{
	[Export] public bool IsStart { get; set; }
	[Export] public bool IsAirlock { get; set; }
	[Export] public bool IsCps { get; set; }

	[Export] public float Radius = 8f;

	/// <summary>Set by the manager each frame; glows when within reach of the player.</summary>
	public bool Highlighted { get; set; }
	/// <summary>Set by the manager each frame; glows when the cursor is over the grip.</summary>
	public bool UnderCursor { get; set; }

	private bool lastGlow;

	public override void _Ready()
	{
		AddToGroup("grip");
	}

	public override void _Process(double delta)
	{
		bool glow = Highlighted || UnderCursor;
		if (glow != lastGlow)
		{
			lastGlow = glow;
			QueueRedraw();
		}
	}

	public override void _Draw()
	{
		// Glow halo when it can be grabbed or is under the cursor.
		if (lastGlow)
		{
			DrawCircle(Vector2.Zero, Radius + 7f, new Color(1f, 1f, 0.3f, 0.35f));
			DrawArc(Vector2.Zero, Radius + 7f, 0f, Mathf.Tau, 24, Colors.White, 2f);
		}

		// Small anodized-yellow handle.
		DrawCircle(Vector2.Zero, Radius, Colors.Yellow);
		DrawCircle(Vector2.Zero, Radius * 0.4f, new Color(0.9f, 0.45f, 0f));

		// Flag notches: start = green, airlock = cyan, CPS = red (debug aid).
		if (IsStart)
			DrawCircle(new Vector2(0f, -Radius - 3f), 2.5f, Colors.Green);
		if (IsAirlock)
			DrawCircle(new Vector2(-Radius - 3f, 0f), 2.5f, Colors.Cyan);
		if (IsCps)
			DrawCircle(new Vector2(Radius + 3f, 0f), 2.5f, Colors.Red);
	}
}