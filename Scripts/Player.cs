#nullable enable
using Godot;

/// <summary>
/// Lobby player — a 2D platformer inside the ship, built on the same logic as
/// Sazsazan's character (Erika): horizontal A/D (mover_esquerda / mover_direita),
/// Space jump (pular), MoveToward acceleration/friction, MoveAndSlide with
/// IsOnFloor/IsOnCeiling, coyote time and an extra (double) jump.
///
/// The only difference from Earth platforming is the weaker adapted microgravity:
/// the character still accelerates on the ground and jumps, but the downward pull
/// is reduced so it floats up and sinks back gently, landing on the floor or
/// stopping at the ceiling without ever passing through the geometry.
///
/// The node keeps the project's multiplayer conventions (nothing duplicated):
///   - Name = peer id sets the multiplayer authority (see _EnterTree).
///   - Added to the "network_players" group + collision exceptions between
///     players (nobody can push another player, while the lobby floor/walls/
///     ceiling still block — it collides on world layer 2 only).
///   - The camera is enabled only on the locally-controlled player and follows
///     with position smoothing, clamped to the lobby bounds via SetCameraLimits.
///   - A MultiplayerSynchronizer replicates position/velocity + animation so the
///     movement of other players is smooth on every machine.
///   - While the chat input has focus, InputEnabled is false (set by the lobby
///     manager from the LineEdit focus): typing A/D/Space only enters text and
///     the character never moves until the focus is released.
///
/// Sprites come from the Characters catalog. The Standard suit is used by default;
/// Phase2Manager explicitly selects the Protection suit for Level 2.
/// </summary>
public partial class Player : CharacterBody2D
{
    // --- Sazsazan platformer movement (adapted microgravity) ---
    [Export] public float MoveSpeed = 150f;
    /// <summary>Ground horizontal acceleration (responsive start, reduced).</summary>
    [Export] public float GroundAcceleration = 800f;
    /// <summary>Air horizontal acceleration (reduced mid-air control, more inertia).</summary>
    [Export] public float AirAcceleration = 300f;
    /// <summary>Ground friction when there is no input (lower = more inertia/drift).</summary>
    [Export] public float GroundFriction = 500f;
    /// <summary>Adapted microgravity: a weak downward pull, below Earth gravity.</summary>
    [Export] public float MicroGravity = 130f;
    /// <summary>Maximum fall speed (keeps physics stable, slow falls).</summary>
    [Export] public float TerminalFallSpeed = 280f;
    /// <summary>
    /// Upward impulse applied by the jump command (pular). Apex height = v^2 / 2g
    /// = 240^2/(2*130) ~= 221px (close to the ~216px target); slow jump.
    /// </summary>
    [Export] public float JumpVelocity = -240f;
    /// <summary>
    /// Hover: while airborne with |Velocity.Y| below this threshold, gravity is
    /// multiplied by LevitationMultiplier, giving a light float at the jump apex.
    /// </summary>
    [Export] public float LevitationLimit = 30f;
    /// <summary>Gravity multiplier applied while hovering (see LevitationLimit).</summary>
    [Export] public float LevitationMultiplier = 0.5f;
    /// <summary>Seconds the player can still jump after walking off a ledge.</summary>
    [Export] public float CoyoteTime = 0.09f;
    /// <summary>Extra jumps allowed while airborne (1 = double jump).</summary>
    [Export] public int ExtraJumps = 1;
    /// <summary>Vertical impulse multiplier for the extra (double) jump.</summary>
    [Export] public float DoubleJumpMultiplier = 1.2f;
    /// <summary>Horizontal input deadzone so tiny axis noise is ignored.</summary>
    [Export] public float InputDeadZone = 0.15f;
    /// <summary>
    /// Set to false while the chat input is focused (typing must not move the
    /// character). Toggled by the lobby manager from the LineEdit focus.
    /// </summary>
    public bool InputEnabled = true;

    private AnimatedSprite2D? sprite;
    private Label? nicknameLabel;
    private Node? networkHandler;
    private long playerId;
    private int characterIndex;
    private string facing = "right";
    private Characters.Suit suit = Characters.Suit.Standard;
    private float _coyoteTimer;
    private int _extraJumpsLeft;

    public override void _EnterTree()
    {
        // The project spawns players with their peer id as the node name, so
        // the authority of a replicated player is the peer it belongs to.
        if (long.TryParse(Name.ToString(), out long peerId))
            SetMultiplayerAuthority((int)peerId, true);
    }

    public override void _Ready()
    {
        // Register the Sazsazan actions (mover_esquerda/mover_direita/pular) since
        // Disasteronomy's input map does not define them. Done before any input is
        // read so Input.GetAxis no longer logs "action doesn't exist" every frame
        // and the character can actually run.
        RegisterInputs();

        AddToGroup("network_players");

        // No player can push another player, while floor/walls/ceiling still collide.
        foreach (Node node in GetTree().GetNodesInGroup("network_players"))
        {
            if (node == this || node is not CharacterBody2D otherPlayer)
                continue;

            AddCollisionExceptionWith(otherPlayer);
            otherPlayer.AddCollisionExceptionWith(this);
        }

        networkHandler = GetNodeOrNull("/root/HighLevelNetworkHandler");
        ResolveIdentity();

        nicknameLabel = GetNodeOrNull<Label>("NicknameLabel");
        ApplyNickname();

        BuildCharacterSprite();
        ApplyCharacterColor();

        var camera = GetNodeOrNull<Camera2D>("Camera2D");
        if (camera != null)
        {
            camera.Enabled = IsMultiplayerAuthority();
            // Fluid camera: smooth follow instead of rigid placement.
            camera.PositionSmoothingEnabled = true;
            camera.PositionSmoothingSpeed = 7f;
        }

        // Standalone (Player.tscn opened directly) still gets a small room so the
        // platformer movement is testable without the Lobby scene.
        EnsureStandaloneRoom();
    }

    /// <summary>Clamps the camera to the room and keeps the character framed.</summary>
    public void SetCameraLimits(Rect2 room)
    {
        var camera = GetNodeOrNull<Camera2D>("Camera2D");
        if (camera == null)
            return;

        camera.LimitLeft = (int)room.Position.X;
        camera.LimitTop = (int)room.Position.Y;
        camera.LimitRight = (int)room.End.X;
        camera.LimitBottom = (int)room.End.Y;
    }

    private void ResolveIdentity()
    {
        if (long.TryParse(Name.ToString(), out playerId))
        {
            if (networkHandler != null)
                characterIndex = networkHandler.Call("get_player_character", (int)playerId).AsInt32();
        }
        else
        {
            playerId = 0;
            if (networkHandler != null)
                characterIndex = networkHandler.Call("get_player_character", (int)Multiplayer.GetUniqueId()).AsInt32();
        }

        if (!Characters.IsValid(characterIndex))
            characterIndex = 0;
    }

    private void ApplyNickname()
    {
        if (nicknameLabel == null)
            return;

        string nickname = "";
        if (networkHandler != null)
        {
            if (playerId != 0)
                nickname = networkHandler.Call("get_player_nickname", (int)playerId).AsString();
            else
                nickname = networkHandler.Call("get_player_nickname", (int)Multiplayer.GetUniqueId()).AsString();
        }

        if (string.IsNullOrWhiteSpace(nickname))
            nickname = playerId == 0 ? Characters.GetName(characterIndex) : $"Player {playerId}";

        nicknameLabel.Text = nickname;
    }

    /// <summary>Colors the name tag with the character identification color.</summary>
    private void ApplyCharacterColor()
    {
        if (nicknameLabel == null)
            return;

        nicknameLabel.AddThemeColorOverride("font_color", Characters.GetColor(characterIndex));
    }

    /// <summary>Rebuilds the animation frames from the character's suit sheets.</summary>
    private void BuildCharacterSprite()
    {
        sprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (sprite == null)
            return;

        SpriteFrames combined = new();
        combined.RemoveAnimation("default");

        SpriteFrames? idle = Characters.BuildIdleFrames(characterIndex, suit);
        SpriteFrames? walk = Characters.BuildWalkFrames(characterIndex, suit);
        SpriteFrames? jump = Characters.BuildJumpFrames(characterIndex, suit);

        MergeAnimation(combined, idle, "idle");
        MergeAnimation(combined, walk, "walk");
        MergeAnimation(combined, jump, "jump");

        if (!combined.HasAnimation("idle"))
            combined = new SpriteFrames(); // no sheets found: keep a visible (empty) placeholder

        sprite.SpriteFrames = combined;
        sprite.Play("idle");
        sprite.FlipH = facing == "left";
    }

    /// <summary>
    /// Switches the suit shown by the character (Standard by default everywhere;
    /// Level 2 switches to Protection). Rebuilds the sprite frames in place.
    /// </summary>
    public void SetSuit(Characters.Suit newSuit)
    {
        if (suit == newSuit || !IsInsideTree())
            return;
        suit = newSuit;
        BuildCharacterSprite();
    }

    private static void MergeAnimation(SpriteFrames target, SpriteFrames? source, string animationName)
    {
        if (source == null || !source.HasAnimation(animationName))
            return;

        target.AddAnimation(animationName);
        target.SetAnimationSpeed(animationName, source.GetAnimationSpeed(animationName));
        target.SetAnimationLoopMode(animationName, source.GetAnimationLoopMode(animationName));

        int frameCount = source.GetFrameCount(animationName);
        for (int i = 0; i < frameCount; i++)
            target.AddFrame(animationName, source.GetFrameTexture(animationName, i));
    }


    /// <summary>
    /// Platformer physics mirroring Sazsazan's scheme, under adapted microgravity:
    ///   - mover_esquerda / mover_direita (A/D): MoveToward with ground/air
    ///     acceleration and ground friction (platformer, responsive stop).
    ///   - pular (Space): jump impulse with coyote time and a double jump; only
    ///     reads input while InputEnabled (chat typing must not move the player).
    ///   - MicroGravity: a weak downward pull, capped at TerminalFallSpeed, so the
    ///     character floats up and sinks back gently (microgravity feel).
    ///   - MoveAndSlide + floor/ceiling: the floor swallows the downward speed and
    ///     the ceiling stops the upward speed — never passes through geometry.
    ///   - While the chat has focus (InputEnabled == false) no gameplay input is
    ///     read, gravity still brings the character down, and movement resumes the
    ///     moment the focus is released.
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        // Only the controlling peer moves its own character; remote players are
        // interpolated by the MultiplayerSynchronizer (pushing here would jitter).
        if (Multiplayer.HasMultiplayerPeer() && !IsMultiplayerAuthority())
            return;

        float dt = (float)delta;

        // --- Horizontal input (mover_esquerda / mover_direita), blocked by chat ---
        float horizontal = 0f;
        if (InputEnabled)
            horizontal = Input.GetAxis("mover_esquerda", "mover_direita");

        if (Mathf.Abs(horizontal) < InputDeadZone)
            horizontal = 0f;

        if (Mathf.Abs(horizontal) > 0.01f)
            facing = horizontal < 0f ? "left" : "right";

        Vector2 velocity = Velocity;

        // --- Horizontal acceleration / friction (Sazsazan scheme) ---
        if (Mathf.Abs(horizontal) > 0.01f)
        {
            float acceleration = IsOnFloor() ? GroundAcceleration : AirAcceleration;
            velocity.X = Mathf.MoveToward(velocity.X, horizontal * MoveSpeed, acceleration * dt);
        }
        else if (IsOnFloor())
        {
            velocity.X = Mathf.MoveToward(velocity.X, 0f, GroundFriction * dt);
        }
        // airborne with no input: keeps the current drift (reduced control, no extra friction)

        // --- Adapted microgravity: weak pull with a float at the jump apex ---
        if (!IsOnFloor())
        {
            float gravity = (Mathf.Abs(velocity.Y) < LevitationLimit)
                ? MicroGravity * LevitationMultiplier   // gentle hover near the top
                : MicroGravity;

            velocity.Y += gravity * dt;
            if (velocity.Y > TerminalFallSpeed)
                velocity.Y = TerminalFallSpeed;
        }

        // --- Coyote timer / jump counters ---
        if (IsOnFloor())
        {
            _coyoteTimer = CoyoteTime;
            _extraJumpsLeft = ExtraJumps;
        }
        else
        {
            _coyoteTimer -= dt;
        }

        // --- Jump (pular): normal (ground/coyote) or extra (double) jump ---
        if (InputEnabled && Input.IsActionJustPressed("pular"))
        {
            if (IsOnFloor() || _coyoteTimer > 0f)
            {
                velocity.Y = JumpVelocity;
                _coyoteTimer = 0f;
            }
            else if (_extraJumpsLeft > 0)
            {
                velocity.Y = JumpVelocity * DoubleJumpMultiplier;
                _extraJumpsLeft--;
            }
        }

        Velocity = velocity;
        MoveAndSlide();

        // --- Floor / ceiling response (never pass through geometry) ---
        if (IsOnFloor() && Velocity.Y > 0f)
            Velocity = new Vector2(Velocity.X, 0f);
        else if (IsOnCeiling() && Velocity.Y < 0f)
            Velocity = new Vector2(Velocity.X, 0f);

        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (sprite == null)
            return;

        if (Mathf.Abs(Velocity.X) > 6f)
            facing = Velocity.X < 0f ? "left" : "right";

        sprite.FlipH = facing == "left";

        string animation =
            Mathf.Abs(Velocity.Y) > 45f ? "jump" :
            Mathf.Abs(Velocity.X) > 15f ? "walk" :
            "idle";

        if (sprite.Animation != animation)
            sprite.Play(animation);
    }

    /// <summary>
    /// When Player.tscn runs standalone (MainMenu legacy path / solo test) build a
    /// small room so the platformer movement has floor, ceiling and walls. Skipped
    /// when a world already exists (the Lobby StaticBody2D, ShipBounds or Phase 1).
    /// </summary>
    private void EnsureStandaloneRoom()
    {
        if (Multiplayer.HasMultiplayerPeer())
            return;

        if (GetTree().CurrentScene != null)
        {
            if (GetTree().CurrentScene.FindChild("ShipBounds", recursive: true, owned: false) != null)
                return;
            if (GetTree().CurrentScene.FindChild("StaticBody2D", recursive: true, owned: false) != null)
                return;
        }

        Node parent = GetParent() ?? this;

        var room = new StaticBody2D { Name = "ShipBounds", CollisionLayer = 2, CollisionMask = 0 };
        parent.AddChild(room);

        AddWall(room, new Vector2(0f, 200f), new Vector2(900f, 24f));      // floor
        AddWall(room, new Vector2(0f, -200f), new Vector2(900f, 24f));     // ceiling
        AddWall(room, new Vector2(-450f, 0f), new Vector2(24f, 424f));     // left wall
        AddWall(room, new Vector2(450f, 0f), new Vector2(24f, 424f));      // right wall
    }

    private static void AddWall(StaticBody2D room, Vector2 center, Vector2 size)
    {
        var body = new StaticBody2D { Position = center, CollisionLayer = 2, CollisionMask = 0 };
        room.AddChild(body);

        var shape = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = size },
        };
        body.AddChild(shape);
    }

    /// <summary>Registers the Sazsazan input actions if they do not exist yet.</summary>
    private void RegisterInputs()
    {
        EnsureAction("mover_esquerda", KeyEvents(Key.A, Key.Left));
        EnsureAction("mover_direita", KeyEvents(Key.D, Key.Right));
        EnsureAction("pular", KeyEvents(Key.Space));
    }

    private static void EnsureAction(string action, Godot.Collections.Array<InputEvent> events)
    {
        if (InputMap.HasAction(action))
            return;

        InputMap.AddAction(action);
        foreach (InputEvent ev in events)
            InputMap.ActionAddEvent(action, ev);
    }

    private static Godot.Collections.Array<InputEvent> KeyEvents(params Key[] keys)
    {
        Godot.Collections.Array<InputEvent> events = new();
        foreach (Key key in keys)
            events.Add(new InputEventKey { PhysicalKeycode = key, Pressed = true });
        return events;
    }
}
