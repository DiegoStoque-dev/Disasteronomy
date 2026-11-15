using Godot;
using System;

public partial class Handrail : Area2D
{
    [Export] public Vector2 StartOffset { get; set; } = new Vector2(-50, 0);
    [Export] public Vector2 EndOffset { get; set; } = new Vector2(50, 0);

    /// <summary>Mouse distance (px) that lights up the rail's anchor nodes.</summary>
    [Export] public float HoverRadius = 22f;

    public Vector2 GlobalStart => GlobalPosition + StartOffset;
    public Vector2 GlobalEnd => GlobalPosition + EndOffset;

    private bool _hovered = false;

    public override void _Ready()
    {
        AddToGroup("handrail");
        SetPhysicsProcess(false);
    }

    public override void _Process(double delta)
    {
        // Light interactive feedback when the cursor is close enough to clip/re-grab.
        bool isHovered = GetGlobalMousePosition().DistanceTo(GetClosestGrabPoint(GetGlobalMousePosition())) <= HoverRadius;
        if (isHovered != _hovered)
        {
            _hovered = isHovered;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        // Structural mounting brackets/standoffs that ground the rail to the hull plating.
        Color mount = new Color(0.55f, 0.57f, 0.62f);
        DrawRect(new Rect2(StartOffset - new Vector2(4, 9), new Vector2(8, 18)), mount);
        DrawRect(new Rect2(EndOffset - new Vector2(4, 9), new Vector2(8, 18)), mount);

        // High-visibility anodized yellow tubular safety rail.
        DrawLine(StartOffset, EndOffset, Colors.Yellow, 6.0f);

        // Interactive anchor nodes (clip points) at both ends.
        Color node = _hovered ? Colors.White : Colors.Yellow;
        DrawCircle(StartOffset, 5.0f, node);
        DrawCircle(EndOffset, 5.0f, node);
        if (_hovered)
        {
            DrawArc(StartOffset, 9f, 0f, Mathf.Tau, 24, Colors.White, 1f);
            DrawArc(EndOffset, 9f, 0f, Mathf.Tau, 24, Colors.White, 1f);
        }
    }

    /// <summary>
    /// Projects a world point onto the rail segment and returns the closest point on the segment.
    /// </summary>
    public Vector2 GetClosestGrabPoint(Vector2 worldPoint)
    {
        Vector2 start = GlobalStart;
        Vector2 end = GlobalEnd;
        Vector2 segment = end - start;
        float segmentLengthSq = segment.LengthSquared();
        if (segmentLengthSq == 0f)
            return start;
        float t = worldPoint.DistanceTo(start) / segment.Length();
        t = Mathf.Clamp(Dot((worldPoint - start), segment) / segmentLengthSq, 0f, 1f);
        return start + segment * t;
    }

    /// <summary>
    /// Returns the rail's primary axis: "horizontal" or "vertical".
    /// </summary>
    public string GetAxis()
    {
        return MathF.Abs(EndOffset.X - StartOffset.X) > MathF.Abs(EndOffset.Y - StartOffset.Y)
            ? "horizontal"
            : "vertical";
    }

    private static float Dot(Vector2 a, Vector2 b) => a.X * b.X + a.Y * b.Y;
}