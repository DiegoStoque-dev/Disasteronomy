#nullable enable
using Godot;

/// <summary>
/// Subtle mouse driven parallax for decorative elements only
/// (the title, the spaceship and the starfield background).
///
/// Never attach this to buttons, panels, windows or any interactive UI:
/// the effect is meant for scenery, not for usability.
///
/// The offset is interpolated every frame, so elements follow the cursor smoothly.
/// </summary>
public partial class MouseParallax : Node
{
	/// <summary>Node that receives the offset. When empty, the parent is used.</summary>
	[Export] public NodePath TargetPath = new("");

	/// <summary>Maximum offset in the target's local units, one value per axis.</summary>
	[Export] public Vector2 Strength = new Vector2(18f, 10f);

	/// <summary>How quickly the element catches up with the cursor (higher = snappier).</summary>
	[Export] public float Smoothing = 4f;

	/// <summary>Inverts the vertical axis, handy for backgrounds.</summary>
	[Export] public bool InvertY = false;

	private Node? _target;
	private Vector2 _basePosition;
	private Vector2 _currentOffset;

	public override void _Ready()
	{
		_target = string.IsNullOrEmpty(TargetPath.ToString())
			? GetParent()
			: GetNodeOrNull(TargetPath);

		if (_target == null)
		{
			// Paths are relative to this node, so a missing "../" lands here.
			GD.PushWarning($"MouseParallax: target '{TargetPath}' not found. Effect disabled.");
			SetProcess(false);
			return;
		}

		_basePosition = _target switch
		{
			CanvasLayer layer => layer.Offset,
			Node2D node2D => node2D.Position,
			Control control => control.Position,
			_ => Vector2.Zero,
		};
	}

	public override void _Process(double delta)
	{
		if (_target == null)
			return;

		// Mouse position normalized to the -1..1 range.
		Vector2 viewport = GetViewport().GetVisibleRect().Size;
		Vector2 mouse = GetViewport().GetMousePosition();
		Vector2 normalized = new(
			viewport.X > 0f ? (mouse.X / viewport.X) * 2f - 1f : 0f,
			viewport.Y > 0f ? (mouse.Y / viewport.Y) * 2f - 1f : 0f);

		Vector2 goal = normalized * Strength;
		if (InvertY)
			goal.Y = -goal.Y;

		// Exponential smoothing keeps the movement fluid instead of snapping.
		_currentOffset = _currentOffset.Lerp(goal, 1f - Mathf.Exp(-Smoothing * (float)delta));
		Apply(_currentOffset);
	}

	private void Apply(Vector2 offset)
	{
		switch (_target)
		{
			// The ship owns its Position, so feed it as a pure offset instead.
			case SpaceshipMotion ship:
				ship.ParallaxOffset = offset;
				break;
			case CanvasLayer layer:
				layer.Offset = _basePosition + offset;
				break;
			case Node2D node2D:
				node2D.Position = _basePosition + offset;
				break;
			case Control control:
				control.Position = _basePosition + offset;
				break;
		}
	}
}
