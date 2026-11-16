using Godot;

/// <summary>
/// Level 2 interactable: an ECLSS valve or the central module, standing on the
/// floor as a small Area2D zone + colored rect. The manager polls OverlapsBody.
/// </summary>
public partial class Interactable : Area2D
{
    public enum Kind { Valve, Central }

    [Export] public Kind ItemKind;
    [Export] public int Id;

    /// <summary>Builds the interaction zone: a rect on top of the floor.</summary>
    public void Setup(Kind kind, int id, float width, float height, Vector2 pos)
    {
        ItemKind = kind;
        Id = id;
        Position = pos;

        Color color = kind == Kind.Valve
            ? new Color(0f, 1f, 1f, 0.9f)      // cyan: ECLSS valve
            : new Color(1f, 0.6f, 0.1f, 0.9f); // orange: central module

        var rect = new ColorRect
        {
            Color = color,
            Size = new Vector2(width, height),
        };
        rect.Position = -rect.Size * 0.5f;
        AddChild(rect);

        var shape = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(width * 2f, height * 1.5f) },
        };
        AddChild(shape);
    }
}