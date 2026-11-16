#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// Level 2 — inside the DSH (deep space habitat), a side-view platformer built
/// from the real room art in res://Sprites/map2/ (same scale and zoom as the
/// lobby). Find the 3 ECLSS valves (hold E), then hold E at the central module.
/// Everything (map, HUD, players, completion overlay) is built in _Ready; the
/// scene file is just a Node2D root with this script.
///
/// Rooms are placed left to right with a RoomOverlap art-pixel seam: UtilityTunnel2
/// (spawn) -> ISSLab -> UtilityTunnel1 -> BigUtilityTunnel -> MPLM. Tunnels sit
/// 31 art px lower than rooms so every floor shares the same world y.
/// </summary>
public partial class Phase2Manager : Node2D
{
    // Room art scale = the lobby DSH map (LobbyMap.png drawn with scale (2,2)).
    [Export] public float RoomScale = 2f;
    /// <summary>Art-pixel horizontal overlap between two neighbouring rooms (tune seams).</summary>
    [Export] public float RoomOverlap = 12f;

    /// <summary>Lobby scene, used by the "Return to Lobby" button at the end of Level 2.</summary>
    private const string LobbyScenePath = "res://Scenes/Lobby/lobby.tscn";
    private const string Level2ScenePath = "res://Scenes/Phase2/Phase2.tscn";
    [Export] public string PlayerScenePath = "res://Scenes/Player.tscn";

    // --- Sanity (local player only, no networking) ---
    [Export] public float SanityDrainPerSecond = 0.7f;
    [Export] public float ShockSanityDamage = 12f;
    [Export] public float ValveSanityBonus = 10f;
    private const float ShockInvulnTime = 1.2f;

    // --- Smoke / creatures (visual, local) ---
    [Export] public float SmokeRampSeconds = 90f;  // smoke reaches "half the view" after this many seconds.
    [Export] public float GhostStartSanity = 70f;  // first hallucinations appear below this sanity.

    // --- Valve / central hold interactions ---
    [Export] public float ValveHoldSeconds = 3f;   // how long to hold E on a valve to restore it.
    [Export] public float OxygenHoldSeconds = 6f;  // how long to hold E at the central module.

    private Node2D? worldRoot;
    private Node2D? playersRoot;
    private Player? localPlayer;
    private DebugMapGuide? debugGuide;

    /// <summary>One room + its art-level placement (x is level art px, y 31 for tunnels).</summary>
    private readonly struct RoomInfo
    {
        public readonly string Name;
        public readonly float WidthArt, HeightArt;
        public readonly bool IsTunnel;
        public readonly float XArt, YArt;
        public RoomInfo(string name, float w, float h, bool tunnel) : this(name, w, h, tunnel, 0f, 0f) { }
        public RoomInfo(string name, float w, float h, bool tunnel, float xArt, float yArt)
        {
            Name = name; WidthArt = w; HeightArt = h; IsTunnel = tunnel; XArt = xArt; YArt = yArt;
        }
    }

    private readonly List<RoomInfo> rooms = new();
    private float totalArtWidth;
    private readonly List<Vector2> cratePositions = new();

    /// <summary>Shared world-space floor y (room art 82 and tunnel art 51+31, both scaled by 2).</summary>
    private float RoomFloorWorldY => 164f;

    private Rect2 levelBounds;

    private readonly List<Vector2> spawnPoints = new();
    private readonly List<Interactable> interactables = new();
    private readonly HashSet<int> valvesDone = new();
    private int centralId = -1;
    private bool centralDone;
    private bool completed;
    private bool failed;

    private float sanity = 100f;
    private float shockInvulnRemaining;

    private readonly List<Hazard> hazards = new();

    // --- Smoke + creatures (screen-space FX layers) ---
    private CanvasLayer? fxLayer;          // Layer 3: smoke vignette + tint.
    private CanvasLayer? ghostLayer;       // Layer 4: creatures, above smoke, below HUD (5).
    private ColorRect? smokeTint;
    private TextureRect? smokeGradient;
    private ColorRect? lowSanityTint;
    private float smokeTime;
    private float ghostSpawnTimer;
    private float ghostDebugTimer;
    private readonly List<Ghost> ghosts = new();
    private bool f6WasDown;
    private bool f7WasDown;

    // --- Valve hold (local progress per valve; completion via RpcValveDone) ---
    private readonly Dictionary<int, float> valveHold = new();
    private readonly Dictionary<int, ProgressBar> valveBars = new();

    // --- Central oxygen hold (local progress; completion is the existing RPC) ---
    private float oxygenProgress;
    private bool centralActivationSent;
    private ProgressBar? centralProgressBar;
    private bool oxygenObjectiveSet;

    private Label? hudObjective;
    private Label? hudValves;
    private Label? hudPrompt;
    private ColorRect? hudSanityFill;
    private ColorRect? shockFlash;
    private Tween? shockFlashTween;

    private bool f8WasDown;
    private bool f9WasDown;
    private bool f10WasDown;

    private bool IsHost => Multiplayer.MultiplayerPeer == null || Multiplayer.IsServer();
    private long LocalPeerId => Multiplayer.MultiplayerPeer == null ? 1 : Multiplayer.GetUniqueId();

    public override void _Ready()
    {
        RegisterInputs();
        BuildLevel();
        BuildFx();
        SetupHud();
        SpawnPlayers();

        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;

        GetNodeOrNull<Global>("/root/Global")?.SetObjective("Restore system: find the 3 ECLSS valves.");
    }

    public override void _Process(double delta)
    {
        FindLocalPlayer();
        HandleDebugKeys();

        if (localPlayer == null || completed)
        {
            if (hudPrompt != null) hudPrompt.Text = "";
            UpdateHud();
            return;
        }

        // Sanity: passive drain + shock hazards (local player only).
        UpdateSanity((float)delta);

        if (failed)
        {
            if (hudPrompt != null) hudPrompt.Text = "";
            UpdateHud();
            return;
        }

        // Passive ambience: smoke ramp + ghost figures + low-sanity tint.
        UpdateAmbience((float)delta);

        // --- "Press E" HUD prompt driven by the interactable the local player stands on ---
        Interactable? target = null;
        foreach (Interactable ia in interactables)
        {
            if (ia.OverlapsBody(localPlayer))
            {
                target = ia;
                break;
            }
        }

        // Valve and central module are HOLD interactions (E). The LOCAL player's
        // hold builds progress; it drains back when E is released or they leave,
        // and completion goes through the existing idempotent RPCs.
        if (target != null && target.ItemKind == Interactable.Kind.Valve)
        {
            if (!valvesDone.Contains(target.Id))
                UpdateValveHold(target.Id, (float)delta);
            else
                DrainValveHolds((float)delta);
        }
        else if (target != null && target.ItemKind == Interactable.Kind.Central && target.Id == centralId)
        {
            if (valvesDone.Count >= 3)
                UpdateOxygenHold((float)delta);
            else
            {
                GD.Print("[Level2] Central blocked: restore the valves first.");
                if (oxygenProgress > 0f)
                    oxygenProgress = Mathf.Max(0f, oxygenProgress - (float)delta * 1.5f);
            }
        }
        else
        {
            DrainValveHolds((float)delta);
            if (oxygenProgress > 0f)
                oxygenProgress = Mathf.Max(0f, oxygenProgress - (float)delta * 1.5f); // left the area: drains
        }

        // Sync every hold-progress bar (also while draining after leaving).
        foreach (var pair in valveBars)
        {
            float p = valveHold.TryGetValue(pair.Key, out float val) ? val : 0f;
            pair.Value.Value = p;
            pair.Value.Visible = p > 0.01f && !valvesDone.Contains(pair.Key);
        }
        if (centralProgressBar != null)
        {
            centralProgressBar.Value = oxygenProgress;
            centralProgressBar.Visible = oxygenProgress > 0.01f;
        }

        if (target == null)
        {
            hudPrompt!.Text = "";
        }
        else if (target.ItemKind == Interactable.Kind.Valve)
        {
            if (valvesDone.Contains(target.Id))
                hudPrompt!.Text = "";
            else
            {
                float p = valveHold.TryGetValue(target.Id, out float v) ? v : 0f;
                hudPrompt!.Text = p > 0f ? "Restoring valve..." : "Hold E to restore the valve";
            }
        }
        else
        {
            hudPrompt!.Text = valvesDone.Count >= 3
                ? (oxygenProgress > 0f ? "Regulating oxygen..." : "Hold E to regulate oxygen")
                : "Restore the valves first";
        }

        UpdateHud();
    }

    // ------------------------------------------------------------------
    // Sanity (local player only) — passive drain + shock hazards
    // ------------------------------------------------------------------

    private void UpdateSanity(float dt)
    {
        if (failed || completed)
            return;

        if (shockInvulnRemaining > 0f)
            shockInvulnRemaining = Mathf.Max(0f, shockInvulnRemaining - dt);

        // Passive drain.
        sanity = Mathf.Clamp(sanity - SanityDrainPerSecond * dt, 0f, 100f);

        // Shock hazard overlap while the hazard is "on" and not invulnerable.
        if (shockInvulnRemaining <= 0f && localPlayer != null)
        {
            foreach (Hazard h in hazards)
            {
                if (h.IsOn && h.OverlapsBody(localPlayer))
                {
                    TakeShock(h);
                    break;
                }
            }
        }

        if (sanity <= 0f && !failed)
        {
            failed = true;
            if (hudPrompt != null) hudPrompt.Text = "";
            ShowFailScreen();
        }
    }

    private void TakeShock(Hazard hazard)
    {
        sanity = Mathf.Clamp(sanity - ShockSanityDamage, 0f, 100f);
        shockInvulnRemaining = ShockInvulnTime;

        if (localPlayer != null)
        {
            // Small upward/backward knockback away from the hazard (Player exposes Velocity).
            Vector2 away = (localPlayer.GlobalPosition - hazard.GlobalPosition).Normalized();
            if (away.LengthSquared() < 0.0001f) away = Vector2.Right;
            localPlayer.Velocity = new Vector2(away.X * 180f, -160f);
        }

        FlashShock();

        if (sanity <= 0f && !failed)
        {
            failed = true;
            if (hudPrompt != null) hudPrompt.Text = "";
            ShowFailScreen();
        }
    }

    private void FlashShock()
    {
        if (shockFlash == null) return;
        if (shockFlashTween != null && shockFlashTween.IsValid())
            shockFlashTween.Kill();
        shockFlash.Modulate = new Color(1f, 1f, 1f, 0.38f);
        shockFlashTween = CreateTween();
        shockFlashTween.TweenProperty(shockFlash, "modulate:a", 0f, 0.35f);
    }

    // ------------------------------------------------------------------
    // Smoke + creatures (screen-space FX layer, local only, no collisions)
    // ------------------------------------------------------------------

    private void BuildFx()
    {
        // Layer above the world (3) but below the HUD (5).
        fxLayer = new CanvasLayer { Name = "Fx", Layer = 3 };
        AddChild(fxLayer);

        // Radial smoke gradient: transparent center -> gray edges.
        var grad = new Gradient
        {
            Offsets = new float[] { 0f, 1f },
            Colors = new Color[] { Colors.Transparent, new Color(0.62f, 0.63f, 0.7f, 1f) },
        };
        var gradTex = new GradientTexture2D
        {
            Gradient = grad,
            Width = 900,
            Height = 500,
            Fill = GradientTexture2D.FillEnum.Radial,
        };
        smokeGradient = new TextureRect
        {
            Texture = gradTex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        smokeGradient.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        fxLayer.AddChild(smokeGradient);

        // Uniform gray haze (its alpha follows the smoke density).
        smokeTint = new ColorRect
        {
            Color = new Color(0.55f, 0.56f, 0.62f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        smokeTint.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        fxLayer.AddChild(smokeTint);

        // Faint dark-red overlay when sanity drops below 20.
        lowSanityTint = new ColorRect
        {
            Color = new Color(0.5f, 0.05f, 0.05f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        lowSanityTint.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        fxLayer.AddChild(lowSanityTint);

        // Creatures draw above the smoke but below the HUD.
        ghostLayer = new CanvasLayer { Name = "GhostFx", Layer = 4 };
        AddChild(ghostLayer);
    }

    private void UpdateAmbience(float dt)
    {
        if (fxLayer == null) return;

        // Smoke density rises 0.15 -> 0.55 over SmokeRampSeconds, then stays.
        smokeTime += dt;
        float density = Mathf.Lerp(0.15f, 0.55f, Mathf.Clamp(smokeTime / Mathf.Max(1f, SmokeRampSeconds), 0f, 1f));
        if (smokeTint != null) smokeTint.Color = new Color(0.55f, 0.56f, 0.62f, density * 0.35f);
        if (smokeGradient != null) smokeGradient.Modulate = new Color(1f, 1f, 1f, density);

        UpdateCreatures(dt);
    }

    private void UpdateCreatures(float dt)
    {
        if (localPlayer == null) return;

        for (int i = ghosts.Count - 1; i >= 0; i--)
        {
            if (!IsInstanceValid(ghosts[i]) || ghosts[i].Finished)
                ghosts.RemoveAt(i);
        }

        // Below 20 sanity: faint dark-red screen tint.
        if (lowSanityTint != null)
        {
            float red = sanity < 20f ? 0.16f * Mathf.Clamp(1f - sanity / 20f, 0f, 1f) : 0f;
            lowSanityTint.Color = new Color(0.5f, 0.05f, 0.05f, red);
        }

        if (sanity >= GhostStartSanity || completed || failed)
        {
            ghostDebugTimer = 0f;
            return;
        }

        // Debug (debug builds only): report eligibility once per second.
        ghostDebugTimer += dt;
        if (OS.IsDebugBuild() && ghostDebugTimer >= 1f)
        {
            ghostDebugTimer = 0f;
            GD.Print($"[Ghost] eligible sanity={sanity:F0} alive={ghosts.Count} nextIn={ghostSpawnTimer:F1}s");
        }

        // One ghost at a time at first (<70), up to 2 below 40, up to 4 below 20.
        int maxGhosts = sanity < 20f ? 4 : sanity < 40f ? 2 : 1;
        if (ghosts.Count >= maxGhosts)
            return;

        ghostSpawnTimer -= dt;
        if (ghostSpawnTimer > 0f)
            return;

        // Spawn interval shrinks as sanity drops (rare at 70 -> intense at 0).
        float factor = Mathf.Clamp(1f - sanity / GhostStartSanity, 0f, 1f);
        ghostSpawnTimer = Mathf.Lerp(10f, 1.5f, factor);
        SpawnGhost(factor, debug: false);
    }

    private void SpawnGhost(float intensity, bool debug)
    {
        if (ghostLayer == null) return;

        Vector2 view = GetViewport().GetVisibleRect().Size;
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        int side = rng.RandiRange(0, 3);
        Vector2 start = side switch
        {
            0 => new Vector2(-60f, rng.RandfRange(0f, view.Y)),                 // left
            1 => new Vector2(view.X + 60f, rng.RandfRange(0f, view.Y)),          // right
            2 => new Vector2(rng.RandfRange(0f, view.X), -60f),                  // top
            _ => new Vector2(rng.RandfRange(0f, view.X), view.Y + 60f),          // bottom
        };

        Vector2 target = new(view.X * 0.5f + rng.RandfRange(-120f, 120f), view.Y * 0.5f + rng.RandfRange(-80f, 80f));
        Vector2 dir = (target - start).Normalized();
        float speed = Mathf.Lerp(6f, 20f, intensity);

        float alpha = 0.35f + intensity * 0.2f;   // peak ~0.35 (rare) .. ~0.55 (critical)
        var ghost = new Ghost { Name = $"Ghost{ghosts.Count}" };
        ghost.Setup(start, dir * speed, 96f + rng.RandfRange(0f, 64f), alpha);
        ghostLayer.AddChild(ghost);
        ghosts.Add(ghost);

        if (debug)
            GD.Print($"[Ghost] spawned tex={ghost.UsedTexture} pos={ghost.Position} alpha={alpha:F2}");
    }

    private void UpdateValveHold(int id, float dt)
    {
        valveHold.TryGetValue(id, out float p);
        if (Input.IsActionPressed("interact"))
        {
            p = Mathf.Min(1f, p + dt / Mathf.Max(1f, ValveHoldSeconds));
            valveHold[id] = p;
            if (p >= 1f)
                MarkValveDone(id);
        }
        else if (p > 0f)
        {
            valveHold[id] = Mathf.Max(0f, p - dt * 1.5f); // released: drains back
        }
    }

    private void DrainValveHolds(float dt)
    {
        foreach (int id in new System.Collections.Generic.List<int>(valveHold.Keys))
        {
            if (valveHold[id] > 0f)
                valveHold[id] = Mathf.Max(0f, valveHold[id] - dt * 1.5f);
        }
    }

    private void UpdateOxygenHold(float dt)
    {
        if (centralActivationSent) return;

        if (Input.IsActionPressed("interact"))
        {
            oxygenProgress = Mathf.Min(1f, oxygenProgress + dt / Mathf.Max(1f, OxygenHoldSeconds));
            if (oxygenProgress >= 1f)
            {
                centralActivationSent = true;
                ActivateCentral();
            }
        }
        else if (oxygenProgress > 0f)
        {
            oxygenProgress = Mathf.Max(0f, oxygenProgress - dt * 1.5f); // released: drains back
        }
    }

    private void RegisterInputs()
    {
        if (!InputMap.HasAction("interact"))
        {
            InputMap.AddAction("interact");
            InputMap.ActionAddEvent("interact", new InputEventKey
            {
                PhysicalKeycode = Key.E,
                Pressed = true,
            });
        }
    }
// ------------------------------------------------------------------
    // Map building
    // ------------------------------------------------------------------

    private void BuildLevel()
    {
        // Background: very dark screen-space layer behind everything.
        var backgroundLayer = new CanvasLayer { Name = "Background", Layer = -100 };
        AddChild(backgroundLayer);
        var bg = new ColorRect { Color = new Color(0.015f, 0.015f, 0.02f, 1f) };
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        backgroundLayer.AddChild(bg);

        // Same convention as the lobby: a "StaticBody2D" root so Player.EnsureStandaloneRoom()
        // sees an existing world and does not build a stray test room in solo.
        worldRoot = new Node2D { Name = "StaticBody2D", ZIndex = -10 };
        AddChild(worldRoot);

        float totalWorldW = BuildRooms();
        BuildCollision(totalWorldW);
        PlaceLevelObjects();

        levelBounds = new Rect2(-30f, -60f, totalWorldW + 60f, 340f);
        GD.Print($"[Level2] Rooms built; level width {totalWorldW:F0}px, floorWorldY={RoomFloorWorldY}");

        // Debug guide lines (floor/ceiling), toggled with F6.
        debugGuide = new DebugMapGuide { FloorWorldY = RoomFloorWorldY, LevelWidth = totalWorldW };
        worldRoot.AddChild(debugGuide);
    }

    /// <summary>Draws each room sprite left->right with RoomOverlap seams; returns total world width.</summary>
    private float BuildRooms()
    {
        rooms.Clear();
        RoomInfo[] defs =
        {
            new("UtilityTunnel2", 112f, 62f, true),
            new("ISSLab", 144f, 112f, false),
            new("UtilityTunnel1", 112f, 62f, true),
            new("BigUtilityTunnel", 144f, 114f, false),
            new("MPLM", 144f, 112f, false),
        };

        float curX = 0f;
        foreach (RoomInfo d in defs)
        {
            RoomInfo r = new(d.Name, d.WidthArt, d.HeightArt, d.IsTunnel, curX, d.IsTunnel ? 31f : 0f);
            rooms.Add(r);

            var sprite = new Sprite2D
            {
                Texture = GD.Load<Texture2D>("res://Sprites/map2/" + r.Name + ".png"),
                Centered = false,           // top-left anchored
                Scale = Vector2.One * RoomScale,
                Position = new Vector2(r.XArt * RoomScale, r.YArt * RoomScale),
            };
            worldRoot!.AddChild(sprite);

            curX += r.WidthArt - RoomOverlap;
        }

        totalArtWidth = curX + RoomOverlap; // back out the last overlap.
        return totalArtWidth * RoomScale;
    }

    /// <summary>One StaticBody2D, rectangle shapes only: floor, per-room ceilings, far walls.</summary>
    private void BuildCollision(float totalWorldW)
    {
        var collision = new StaticBody2D { Name = "LevelCollision", CollisionLayer = 2, CollisionMask = 0 };
        worldRoot!.AddChild(collision);

        // Floor along the whole level at the shared floor world y (thickness 16 below it).
        AddRect(collision, new Vector2(totalWorldW * 0.5f, RoomFloorWorldY + 8f), new Vector2(totalWorldW, 16f));

        // Ceilings per room: rooms y=62, tunnels y=78 (16 tall).
        foreach (RoomInfo r in rooms)
        {
            float ceilWorldY = RoomCeilingWorldY(r);
            var size = new Vector2(r.WidthArt * RoomScale, 16f);
            var center = new Vector2(r.XArt * RoomScale + size.X * 0.5f, ceilWorldY - 8f);
            AddRect(collision, center, size);
        }

        // Far side walls (left / right ends).
        AddRect(collision, new Vector2(4f, 90f), new Vector2(8f, 180f));
        AddRect(collision, new Vector2(totalWorldW - 4f, 90f), new Vector2(8f, 180f));
    }

    private void AddRect(StaticBody2D body, Vector2 center, Vector2 size)
    {
        body.AddChild(new CollisionShape2D
        {
            Position = center,
            Shape = new RectangleShape2D { Size = size },
        });
    }

    /// <summary>Spawns, valves, central module, crates and hazards at their fixed art positions.</summary>
    private void PlaceLevelObjects()
    {
        // Spawns: UtilityTunnel2 x=20/40/60/80 (art), standing on the floor.
        for (int i = 0; i < 4; i++)
            spawnPoints.Add(new Vector2((20f + i * 20f) * RoomScale, RoomFloorWorldY - 30f));

        // Valves: ISSLab x=26 and x=100, BigUtilityTunnel x=72.
        PlaceValve(1, 26f, 0, "ISSLab");
        PlaceValve(1, 100f, 1, "ISSLab");
        PlaceValve(3, 72f, 2, "BigUtilityTunnel");

        // Central module: MPLM x=110 (the big machine on its right side).
        float centralX = (rooms[4].XArt + 110f) * RoomScale;
        var central = new Interactable { Name = "Central" };
        central.Setup(Interactable.Kind.Central, 0, 26f, 34f, new Vector2(centralX, RoomFloorWorldY - 17f));
        worldRoot!.AddChild(central);
        interactables.Add(central);
        centralId = 0;
        centralProgressBar = MakeHoldBar();
        central.AddChild(centralProgressBar);

        // Crates sliced from barriers.png. Tallest is 24 art px = 48 world px (< 60% of the ~221px jump).
        Texture2D? crateSheet = GD.Load<Texture2D>("res://Sprites/map2/barriers.png");
        PlaceCrate(1, 60f, new Rect2(0f, 19f, 17f, 13f), crateSheet);
        PlaceCrate(3, 30f, new Rect2(64f, 19f, 23f, 13f), crateSheet);
        PlaceCrate(3, 110f, new Rect2(32f, 8f, 23f, 24f), crateSheet);
        PlaceCrate(4, 40f, new Rect2(0f, 48f, 48f, 16f), crateSheet);

        // Shock hazards (ElectricBarrier frames 1-4 are ON, frame 0 is the OFF spark).
        PlaceHazard(1, 70f);   // ISSLab
        PlaceHazard(2, 56f);   // UtilityTunnel1
        PlaceHazard(4, 70f);   // MPLM
    }

    private void PlaceValve(int roomIndex, float localX, int id, string roomName)
    {
        float worldX = (rooms[roomIndex].XArt + localX) * RoomScale;
        var v = new Interactable { Name = $"Valve{id}" };
        v.Setup(Interactable.Kind.Valve, id, 14f, 20f, new Vector2(worldX, RoomFloorWorldY - 10f));
        worldRoot!.AddChild(v);
        interactables.Add(v);
        var bar = MakeHoldBar();
        v.AddChild(bar);
        valveBars[id] = bar;
    }

    private void PlaceCrate(int roomIndex, float localX, Rect2 artRegion, Texture2D? sheet)
    {
        if (sheet == null) return;
        float w = artRegion.Size.X * RoomScale;
        float h = artRegion.Size.Y * RoomScale;
        float worldX = (rooms[roomIndex].XArt + localX) * RoomScale;
        var body = new StaticBody2D
        {
            Name = "Crate",
            CollisionLayer = 2,
            CollisionMask = 0,
            Position = new Vector2(worldX, RoomFloorWorldY - h * 0.5f),
        };
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(w, h) } });
        body.AddChild(new Sprite2D
        {
            Texture = new AtlasTexture { Atlas = sheet, Region = artRegion },
            Scale = Vector2.One * RoomScale,
        });
        worldRoot!.AddChild(body);
        cratePositions.Add(body.Position);
    }

    private void PlaceHazard(int roomIndex, float localX)
    {
        float worldX = (rooms[roomIndex].XArt + localX) * RoomScale;
        var hazard = new Hazard { Name = $"Hazard{hazards.Count}" };
        hazard.Setup(new Vector2(worldX, RoomFloorWorldY - 14f));
        worldRoot!.AddChild(hazard);
        hazards.Add(hazard);
    }

    private ProgressBar MakeHoldBar()
    {
        return new ProgressBar
        {
            MinValue = 0f,
            MaxValue = 1f,
            Value = 0f,
            ShowPercentage = false,
            Visible = false,
            Size = new Vector2(96f, 8f),
            Position = new Vector2(-48f, -26f),
        };
    }

    private float RoomCeilingWorldY(RoomInfo r) => (r.YArt + (r.IsTunnel ? 8f : 31f)) * RoomScale;
// ------------------------------------------------------------------
    // HUD
    // ------------------------------------------------------------------

    private void SetupHud()
    {
        var hudLayer = new CanvasLayer { Name = "Hud", Layer = 5 };
        AddChild(hudLayer);

        hudObjective = new Label
        {
            Text = "LEVEL 2 - DSH: find the ECLSS valves, then the central module",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        hudObjective.AddThemeFontSizeOverride("font_size", 20);
        hudLayer.AddChild(hudObjective);
        hudObjective.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        hudObjective.OffsetTop = 10;

        hudValves = new Label { Text = "Valves: 0/3", Position = new Vector2(20, 44) };
        hudValves.AddThemeFontSizeOverride("font_size", 18);
        hudLayer.AddChild(hudValves);

        hudPrompt = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center };
        hudPrompt.AddThemeFontSizeOverride("font_size", 20);
        hudLayer.AddChild(hudPrompt);
        hudPrompt.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        hudPrompt.OffsetTop = -80;

        // SANITY bar (top-left, under the objective / valves labels).
        var sanityLabel = new Label { Text = "SANITY", Position = new Vector2(20, 70) };
        sanityLabel.AddThemeFontSizeOverride("font_size", 16);
        hudLayer.AddChild(sanityLabel);

        var sanityBg = new ColorRect
        {
            Color = new Color(0.1f, 0.1f, 0.12f, 0.9f),
            Size = new Vector2(160, 14),
            Position = new Vector2(88, 72),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hudLayer.AddChild(sanityBg);

        hudSanityFill = new ColorRect
        {
            Color = Colors.Green,
            Size = new Vector2(160, 14),
            Position = new Vector2(88, 72),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hudLayer.AddChild(hudSanityFill);

        // Full-screen red flash on shock (blocks nothing).
        var flashLayer = new CanvasLayer { Name = "FlashLayer", Layer = 115, ProcessMode = ProcessModeEnum.Always };
        AddChild(flashLayer);
        shockFlash = new ColorRect
        {
            Color = new Color(1f, 0.12f, 0.12f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        shockFlash.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        flashLayer.AddChild(shockFlash);
    }

    private void UpdateHud()
    {
        if (hudValves != null)
            hudValves.Text = $"Restore system: {valvesDone.Count}/3";

        if (hudSanityFill != null)
        {
            Color color = sanity > 60f ? Colors.Green : sanity > 30f ? Colors.Yellow : new Color(0.9f, 0.15f, 0.12f);
            hudSanityFill.Color = color;
            hudSanityFill.Size = new Vector2(160f * Mathf.Clamp(sanity / 100f, 0f, 1f), 14f);
        }
    }

    // ------------------------------------------------------------------
    // Players (same spawn pattern as ShipManager: host decides + RPC)
    // ------------------------------------------------------------------

    private void SpawnPlayers()
    {
        if (!IsHost)
            return;

        SpawnPlayerForPeer(LocalPeerId);
        foreach (int peerId in Multiplayer.GetPeers())
            if (peerId != Multiplayer.GetUniqueId())
                SpawnPlayerForPeer(peerId);
    }

    private void SpawnPlayerForPeer(long peerId)
    {
        Vector2 pos = spawnPoints.Count > 0
            ? spawnPoints[(int)(peerId % spawnPoints.Count)]
            : new Vector2(200f, 130f);
        Rpc(nameof(RpcSpawnPlayer), (int)peerId, pos.X, pos.Y);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
    private void RpcSpawnPlayer(int peerId, float x, float y)
    {
        if (playersRoot == null)
        {
            playersRoot = new Node2D { Name = "Players", ZIndex = 1 };
            AddChild(playersRoot);
        }

        if (playersRoot.GetNodeOrNull(peerId.ToString()) != null)
            return;

        PackedScene? scene = GD.Load<PackedScene>(PlayerScenePath);
        if (scene == null)
        {
            GD.PushError("Phase2Manager: player scene not found: " + PlayerScenePath);
            return;
        }

        var player = scene.Instantiate<Player>();
        player.Name = peerId.ToString();
        player.GlobalPosition = ClampToWorld(new Vector2(x, y));
        playersRoot.AddChild(player);

        // Level 2 = protection suit. Derives from the scene (runs on every peer for
        // local and remote players); leaving to the lobby spawns fresh Standard-suit
        // players again. No synced property needed.
        player.SetSuit(Characters.Suit.Protection);

        if (peerId == LocalPeerId)
        {
            localPlayer = player;
            player.SetCameraLimits(levelBounds);
        }

        GD.Print($"[Level2] Spawned player {peerId} at {player.GlobalPosition}");
    }

    private Vector2 ClampToWorld(Vector2 position) =>
        levelBounds.HasPoint(position) ? position : levelBounds.GetCenter();

    private void FindLocalPlayer()
    {
        if (Multiplayer.HasMultiplayerPeer())
        {
            foreach (Node node in GetTree().GetNodesInGroup("network_players"))
            {
                if (node is Player p && p.IsMultiplayerAuthority())
                {
                    localPlayer = p;
                    return;
                }
            }
            return;
        }

        foreach (Node child in playersRoot?.GetChildren() ?? new Godot.Collections.Array<Node>())
        {
            if (child is Player p)
            {
                localPlayer = p;
                return;
            }
        }
    }

    private void OnPeerConnected(long peerId)
    {
        if (!IsHost)
            return;
        SpawnPlayerForPeer(peerId);
    }

    private void OnPeerDisconnected(long peerId)
    {
        if (!IsHost)
            return;
        Rpc(nameof(RpcRemovePlayer), (int)peerId);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
    private void RpcRemovePlayer(int peerId)
    {
        Node? node = playersRoot?.GetNodeOrNull(peerId.ToString());
        if (node != null)
        {
            playersRoot?.RemoveChild(node);
            node.QueueFree();
        }
    }
// ------------------------------------------------------------------
    // Task state (valves -> central), like Phase1's RpcReportComplete pattern
    // ------------------------------------------------------------------

    private void MarkValveDone(int id)
    {
        if (Multiplayer.HasMultiplayerPeer())
            Rpc(nameof(RpcValveDone), id);
        else
            RpcValveDone(id);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
    private void RpcValveDone(int id)
    {
        if (valvesDone.Add(id))
        {
            // Restoring a valve steadies the nerves (sanity is local to each player).
            sanity = Mathf.Clamp(sanity + ValveSanityBonus, 0f, 100f);
            GD.Print($"[Level2] Valve {id} fixed ({valvesDone.Count}/3)");

            // Once all valves are restored the objective switches to oxygen.
            if (valvesDone.Count >= 3 && !oxygenObjectiveSet)
            {
                oxygenObjectiveSet = true;
                GetNodeOrNull<Global>("/root/Global")?.SetObjective("Regulate oxygen: hold E at the central module.");
            }
        }
    }

    private void ActivateCentral()
    {
        if (Multiplayer.HasMultiplayerPeer())
            Rpc(nameof(RpcCentralDone));
        else
            RpcCentralDone();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
    private void RpcCentralDone()
    {
        if (centralDone || valvesDone.Count < 3 || centralId < 0)
            return;

        centralDone = true;
        completed = true;
        Global? global = Global.Instance ?? GetNodeOrNull<Global>("/root/Global");
        if (global != null)
            global.Level2Completed = true;
        ShowCompleteScreen();
    }

    // ------------------------------------------------------------------
    // Completion overlay (simple, no ranking yet)
    // ------------------------------------------------------------------

    private void ShowCompleteScreen()
    {
        var layer = new CanvasLayer { Name = "CompleteLayer", Layer = 120, ProcessMode = ProcessModeEnum.Always };
        AddChild(layer);

        var overlay = new ColorRect { Color = new Color(0, 0, 0, 0.82f) };
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(overlay);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        overlay.AddChild(center);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(480, 0) };
        center.AddChild(box);

        var title = new Label { Text = "LEVEL 2 COMPLETE", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 34);
        box.AddChild(title);

        var subtitle = new Label
        {
            Text = "ECLSS restored. Oxygen levels are stabilizing across the DSH.",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        subtitle.AddThemeFontSizeOverride("font_size", 20);
        box.AddChild(subtitle);

        var back = new Button { Text = "Return to Lobby", CustomMinimumSize = new Vector2(0, 46) };
        back.Pressed += () =>
        {
            if (SceneTransitions.Instance != null)
                SceneTransitions.Instance.ChangeScene(LobbyScenePath);
            else
                GetTree().ChangeSceneToFile(LobbyScenePath);
        };
        box.AddChild(back);
    }

    // ------------------------------------------------------------------
    // Fail overlay (sanity lost) — Retry reloads Phase 2, or return to the Lobby.
    // ------------------------------------------------------------------

    private void ShowFailScreen()
    {
        var layer = new CanvasLayer { Name = "FailLayer", Layer = 121, ProcessMode = ProcessModeEnum.Always };
        AddChild(layer);

        var overlay = new ColorRect { Color = new Color(0, 0, 0, 0.85f) };
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(overlay);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        overlay.AddChild(center);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(480, 0) };
        center.AddChild(box);

        var title = new Label { Text = "MISSION FAILED - sanity lost", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 34);
        box.AddChild(title);

        var retry = new Button { Text = "Retry", CustomMinimumSize = new Vector2(0, 46) };
        retry.Pressed += () =>
        {
            if (SceneTransitions.Instance != null)
                SceneTransitions.Instance.ChangeScene(Level2ScenePath);
            else
                GetTree().ChangeSceneToFile(Level2ScenePath);
        };
        box.AddChild(retry);

        var back = new Button { Text = "Return to Lobby", CustomMinimumSize = new Vector2(0, 46) };
        back.Pressed += () =>
        {
            if (SceneTransitions.Instance != null)
                SceneTransitions.Instance.ChangeScene(LobbyScenePath);
            else
                GetTree().ChangeSceneToFile(LobbyScenePath);
        };
        box.AddChild(back);
    }

    // ------------------------------------------------------------------
    // Dev shortcuts (F8: sanity 20; F9: teleport near central module; F10: fix all valves)
    // ------------------------------------------------------------------

    private void HandleDebugKeys()
    {
        if (failed)
            return;

        // Cheat/dev shortcuts must never ship in exported builds.
        if (!OS.IsDebugBuild())
            return;

        bool f6Down = Input.IsPhysicalKeyPressed(Key.F6);
        bool f7Down = Input.IsPhysicalKeyPressed(Key.F7);
        bool f8Down = Input.IsPhysicalKeyPressed(Key.F8);
        bool f9Down = Input.IsPhysicalKeyPressed(Key.F9);
        bool f10Down = Input.IsPhysicalKeyPressed(Key.F10);
        bool f6Pressed = f6Down && !f6WasDown;
        bool f7Pressed = f7Down && !f7WasDown;
        bool f8Pressed = f8Down && !f8WasDown;
        bool f9Pressed = f9Down && !f9WasDown;
        bool f10Pressed = f10Down && !f10WasDown;
        f6WasDown = f6Down;
        f7WasDown = f7Down;
        f8WasDown = f8Down;
        f9WasDown = f9Down;
        f10WasDown = f10Down;

        if (f6Pressed)
        {
            if (debugGuide != null)
                debugGuide.Visible = !debugGuide.Visible;
            PrintMapInfo();
        }

        if (f7Pressed)
        {
            SpawnGhost(0.8f, debug: true); // immediate ghost at a random screen edge
        }

        if (f8Pressed)
        {
            sanity = Mathf.Min(20f, sanity);
            GD.Print("[Level2] F8: sanity set to 20.");
        }

        if (f10Pressed)
        {
            for (int i = 0; i < 3; i++)
            {
                if (!valvesDone.Contains(i))
                    MarkValveDone(i);
            }
            GD.Print("[Level2] F10: all valves fixed.");
        }

        if (f9Pressed && localPlayer != null && centralId >= 0)
        {
            foreach (Interactable ia in interactables)
            {
                if (ia.ItemKind == Interactable.Kind.Central)
                {
                    localPlayer.GlobalPosition = ClampToWorld(ia.GlobalPosition + new Vector2(-60f, -20f));
                    localPlayer.Velocity = Vector2.Zero;
                    GD.Print("[Level2] F9: teleported near the central module.");
                    break;
                }
            }
        }
    }

    /// <summary>Debug (F6): prints room offsets and every object's world position.</summary>
    private void PrintMapInfo()
    {
        foreach (RoomInfo r in rooms)
            GD.Print($"[Map] room {r.Name} offset={new Vector2(r.XArt * RoomScale, r.YArt * RoomScale)} floorWorldY={RoomFloorWorldY}");

        for (int i = 0; i < interactables.Count; i++)
        {
            Interactable ia = interactables[i];
            GD.Print($"[Map] {(ia.ItemKind == Interactable.Kind.Valve ? "valve" : "central")} {ia.Id} world={ia.GlobalPosition}");
        }

        for (int i = 0; i < cratePositions.Count; i++)
            GD.Print($"[Map] crate {i} world={cratePositions[i]}");

        for (int i = 0; i < hazards.Count; i++)
        {
            if (IsInstanceValid(hazards[i]))
                GD.Print($"[Map] hazard {i} world={hazards[i].GlobalPosition}");
        }
    }
}

/// <summary>F6 debug overlay: thin world-space lines for the shared floor and room ceilings.</summary>
public partial class DebugMapGuide : Node2D
{
    public float FloorWorldY;
    public float LevelWidth;

    public override void _Draw()
    {
        // Shared floor world y (green).
        DrawLine(new Vector2(-12f, FloorWorldY), new Vector2(LevelWidth + 12f, FloorWorldY), new Color(0f, 1f, 0f, 0.9f), 2f);
        // Room ceiling y=62 (red) and tunnel ceiling y=78 (orange).
        DrawLine(new Vector2(-12f, 62f), new Vector2(LevelWidth + 12f, 62f), new Color(1f, 0.25f, 0.2f, 0.8f), 2f);
        DrawLine(new Vector2(-12f, 78f), new Vector2(LevelWidth + 12f, 78f), new Color(1f, 0.6f, 0.3f, 0.7f), 1f);
    }
}
