#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// Translucent "ghost" figure shown at the screen edges when sanity is low.
/// Purely visual and local (screen-space FX layer): it never collides or blocks.
/// Fades in (~1 s), drifts toward the screen center, holds, then fades out (~1 s).
/// Each ghost randomly picks one frame from any loaded creature texture
/// (Sprites/Monsters + Sprites/Ansiedade); a soft rect is the fallback.
/// </summary>
public partial class Ghost : Node2D
{
    /// <summary>Texture actually used by this ghost (for the F7 debug print).</summary>
    public string UsedTexture { get; private set; } = "";

    public bool Finished { get; private set; }

    private static readonly List<(string Name, Texture2D Tex, int Frames)> Cache = new();

    private Vector2 velocity;
    private float life;
    private float time;
    private float peakAlpha;
    private Node2D visual = null!;

    public static void EnsureCacheLoaded()
    {
        if (Cache.Count > 0)
            return;
        AddIfUsable("res://Sprites/Monsters/skuba.png");
        AddIfUsable("res://Sprites/Ansiedade/Ansiedade_stage2.png");
        AddIfUsable("res://Sprites/Ansiedade/Ansiedade_stage3.png");
    }

    private static void AddIfUsable(string path)
    {
        Texture2D? tex = GD.Load<Texture2D>(path);
        if (tex == null || tex.GetWidth() <= 0 || tex.GetHeight() <= 0)
            return;
        // Square frames laid out horizontally (frames are as tall as the sheet).
        int frames = Mathf.Max(1, tex.GetWidth() / tex.GetHeight());
        Cache.Add((path.GetFile(), tex, frames));
    }

    public void Setup(Vector2 start, Vector2 drift, float size, float alpha)
    {
        Position = start;
        velocity = drift;
        peakAlpha = alpha;
        time = 0f;
        life = 3.5f + new RandomNumberGenerator().RandfRange(0f, 1.5f); // fade-in 1s + hold 1.5-3s + fade-out 1s
        Finished = false;

        visual = new Node2D();
        EnsureCacheLoaded();
        var rng = new RandomNumberGenerator();
        rng.Randomize();

        if (Cache.Count > 0)
        {
            (string name, Texture2D tex, int frames) = Cache[rng.RandiRange(0, Cache.Count - 1)];
            UsedTexture = name;

            // Square frames laid out horizontally: use ONE random frame.
            int frame = frames > 1 ? rng.RandiRange(0, frames - 1) : 0;
            float fh = tex.GetHeight();          // frame height = sheet height
            var sprite = new Sprite2D
            {
                Texture = new AtlasTexture { Atlas = tex, Region = new Rect2(frame * fh, 0, fh, fh) },
                Scale = Vector2.One * (size / fh),
            };
            visual.AddChild(sprite);
        }
        else
        {
            UsedTexture = "";
            var rect = new ColorRect
            {
                Color = new Color(0.85f, 0.88f, 1f, 0.8f),
                Size = new Vector2(size * 0.6f, size),
            };
            rect.Position = -rect.Size * 0.5f;
            visual.AddChild(rect);
        }
        AddChild(visual);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        time += dt;
        Position += velocity * dt;

        // Fade in 1 s, hold, fade out 1 s (life >= 3.5 s total by construction).
        float a;
        if (time < 1f)
            a = time;
        else if (time > life - 1f)
            a = Mathf.Max(0f, (life - time));
        else
            a = 1f;

        visual.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(a, 0f, 1f) * peakAlpha);

        if (time >= life)
        {
            Finished = true;
            QueueFree();
        }
    }
}