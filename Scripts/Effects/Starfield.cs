#nullable enable
using Godot;

/// <summary>
/// Randomized pixel-art starfield used as a space background.
/// Draws a black frame with tiny hard-edged squares (1-2 px) at integer positions,
/// so it stays true pixel art. Set Scroll = true to make the stars drift
/// from left to right across the screen.
///
/// Put this Control inside a CanvasLayer (see Scenes/Effects/StarfieldStatic.tscn
/// and StarfieldScroll.tscn) so it ignores scene cameras and parent transforms.
/// </summary>
public partial class Starfield : Control
{
    [Export] public Color BackgroundColor = Colors.Black;

    /// <summary>How many stars to randomize.</summary>
    [Export] public int StarCount = 280;

    /// <summary>Star size in pixels (keep it small: 1-2 for pixel art).</summary>
    [Export] public int MinStarSize = 1;
    [Export] public int MaxStarSize = 2;

    /// <summary>0 = new random pattern every run. Any other value gives a repeatable pattern.</summary>
    [Export] public int Seed = 0;

    /// <summary>When true the stars drift from left to right.</summary>
    [Export] public bool Scroll = false;

    /// <summary>Base drift speed in pixels per second.</summary>
    [Export] public float ScrollSpeed = 14f;

    /// <summary>How much the star speeds differ (1.2 = +/- 60%), giving a depth illusion.</summary>
    [Export] public float SpeedVariation = 1.2f;

    private struct Star
    {
        public float X;
        public float Y;
        public int Size;
        public Color Tint;
        public float SpeedMultiplier;
    }

    private Star[] _stars = System.Array.Empty<Star>();
    private Vector2 _area;
    private float _wrapWidth;
    private float _scrollOffset;

    public override void _Ready()
    {
        MouseFilter = Control.MouseFilterEnum.Ignore;
        SetProcess(Scroll);
        Regenerate();
        Resized += Regenerate;
    }

    public override void _Process(double delta)
    {
        _scrollOffset += ScrollSpeed * (float)delta;

        // Keep the offset bounded so float precision stays stable on long sessions.
        if (_wrapWidth > 0f && _scrollOffset > _wrapWidth * 1000f)
            _scrollOffset = Mathf.PosMod(_scrollOffset, _wrapWidth);

        QueueRedraw();
    }

    /// <summary>(Re)creates the randomized star pattern for the current size.</summary>
    private void Regenerate()
    {
        _area = Size;
        if (_area.X < 2f || _area.Y < 2f)
            _area = GetViewportRect().Size; // fallback before the layout settles

        if (_area.X < 2f || _area.Y < 2f)
            return;

        int maxSize = Mathf.Max(MinStarSize, MaxStarSize);
        _wrapWidth = _area.X + maxSize;

        var rng = new RandomNumberGenerator();
        if (Seed == 0)
            rng.Randomize();
        else
            rng.Seed = (ulong)Seed;

        _stars = new Star[Mathf.Max(0, StarCount)];
        for (int i = 0; i < _stars.Length; i++)
        {
            int size = rng.RandiRange(MinStarSize, maxSize);
            _stars[i] = new Star
            {
                X = rng.RandfRange(0f, _wrapWidth),
                Y = rng.RandfRange(0f, _area.Y),
                Size = size,
                Tint = MakeStarTint(rng, size, maxSize),
                SpeedMultiplier = rng.RandfRange(
                    1f - SpeedVariation * 0.5f,
                    1f + SpeedVariation * 0.5f),
            };
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), BackgroundColor);

        foreach (Star star in _stars)
        {
            float x = star.X;
            if (Scroll)
                x = Mathf.PosMod(star.X + _scrollOffset * star.SpeedMultiplier, _wrapWidth);

            // Integer positions + integer sizes = crisp pixel-art squares.
            DrawRect(
                new Rect2(Mathf.Floor(x), Mathf.Floor(star.Y), star.Size, star.Size),
                star.Tint,
                filled: true,
                width: -1f,
                antialiased: false);
        }
    }

    /// <summary>Bright stars for the bigger ones, dim ones for the small specks.</summary>
    private static Color MakeStarTint(RandomNumberGenerator rng, int size, int maxSize)
    {
        float brightness = size >= maxSize
            ? rng.RandfRange(0.78f, 1f)
            : rng.RandfRange(0.32f, 0.82f);

        float roll = rng.Randf();
        Color hue = roll < 0.15f ? new Color(0.72f, 0.84f, 1f)   // icy blue
                  : roll > 0.88f ? new Color(1f, 0.94f, 0.78f)   // warm yellow
                  : new Color(1f, 1f, 1f);

        return new Color(hue.R * brightness, hue.G * brightness, hue.B * brightness, 1f);
    }
}
