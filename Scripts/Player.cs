#nullable enable
using Godot;

public partial class Player : CharacterBody2D
{
	private const float MoveSpeed = 260f;
	private const float GroundAcceleration = 1800f;
	private const float AirAcceleration = 1000f;
	private const float GroundFriction = 2200f;
	private const float Gravity = 980f;
	private const float JumpVelocity = -600f;
	private const float CoyoteTime = 0.12f;
	private const float JumpBufferTime = 0.12f;
	private const int SpriteFrameWidth = 48;
	private const int SpriteFrameHeight = 64;

	private AnimatedSprite2D? sprite;
	private Label? nicknameLabel;
	private Node? networkHandler;
	private Callable nicknameChangedCallable;
	private long playerId;
	private string facing = "right";
	private float coyoteTimeRemaining;
	private float jumpBufferTimeRemaining;
	private Global? global;

	public override void _Ready()
	{
		AddToGroup("network_players");
		foreach (Node node in GetTree().GetNodesInGroup("network_players"))
		{
			if (node == this || node is not CharacterBody2D otherPlayer)
				continue;

			AddCollisionExceptionWith(otherPlayer);
			otherPlayer.AddCollisionExceptionWith(this);
		}

		nicknameLabel = GetNode<Label>("NicknameLabel");
		networkHandler = GetNodeOrNull("/root/HighLevelNetworkHandler");
		if (networkHandler != null)
		{
			nicknameChangedCallable = Callable.From<long, string>(OnPlayerNicknameChanged);
			if (!networkHandler.IsConnected("player_nickname_changed", nicknameChangedCallable))
				networkHandler.Connect("player_nickname_changed", nicknameChangedCallable);
		}

		if (long.TryParse(Name.ToString(), out playerId))
		{
			if (networkHandler != null)
				UpdateNickname(networkHandler.Call("get_player_nickname", playerId).AsString());
		}
		else
		{
			// Single-player: the node is not a networked player id, just show a default name.
			playerId = 0;
			UpdateNickname("Jogador");
		}

		sprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
		AddJumpAnimation(
			"jump_left",
			"res://Sprites/The Female Adventurer - Free/The Female Adventurer - Free/Jump - NEW/Normal/Jump_Left_Down.png"
		);
		AddJumpAnimation(
			"jump_right",
			"res://Sprites/The Female Adventurer - Free/The Female Adventurer - Free/Jump - NEW/Normal/Jump_Right_Down.png"
		);

		var camera = GetNodeOrNull<Camera2D>("Camera2D");
		if (camera != null)
			camera.Enabled = IsMultiplayerAuthority();

		if (IsMultiplayerAuthority())
			GD.Print("Controlando meu personagem: " + Name);

		ApplySavedState();
		EnsureSinglePlayerGround();

		UpdateAnimation("idle_right");
	}

	/// <summary>
	/// Adds a wide test platform when the scene runs standalone (single-player).
	/// In multiplayer the level geometry is provided by the world scene instead.
	/// </summary>
	private void EnsureSinglePlayerGround()
	{
		if (Multiplayer.MultiplayerPeer != null)
			return;

		Node parent = GetParent() ?? this;
		if (parent.HasNode("SinglePlayerGround"))
			return;

		StaticBody2D floor = new() { Name = "SinglePlayerGround" };
		CollisionShape2D shape = new()
		{
			Shape = new RectangleShape2D { Size = new Vector2(2000, 40) }
		};
		floor.AddChild(shape);

		// Simple visible platform so the standalone test is readable on screen.
		Polygon2D visual = new()
		{
			Polygon = new[]
			{
				new Vector2(-1000, -20), new Vector2(1000, -20),
				new Vector2(1000, 20), new Vector2(-1000, 20),
			},
			Color = new Color(0.25f, 0.30f, 0.40f),
		};
		floor.AddChild(visual);

		floor.Position = new Vector2(GlobalPosition.X, GlobalPosition.Y + 140);
		parent.AddChild(floor);

		GD.Print("[Player] Single-player test ground created.");
	}

	/// <summary>
	/// Restores the player's saved position (single-player) or starts a fresh run
	/// when the main menu requested a new game.
	/// </summary>
	private void ApplySavedState()
	{
		global = GetNodeOrNull<Global>("/root/Global");
		if (global == null)
			return;

		if (global.NewRunRequested)
		{
			// Fresh run: keep the scene's default position and overwrite the old save.
			global.NewRunRequested = false;
			global.Save(GlobalPosition);
			GD.Print("[Player] New run started at " + GlobalPosition);
			return;
		}

		if (!global.HasPlayerSave && global.HasSave())
		{
			global.Load();
		}

		if (global.HasPlayerSave)
		{
			GlobalPosition = global.SavedPlayerPosition;
			GD.Print("[Player] Resumed from save at " + GlobalPosition);
		}
		else
		{
			global.Save(GlobalPosition);
			GD.Print("[Player] No save found, created a new one at " + GlobalPosition);
		}
	}

	/// <summary>Persists the player's current position through the Global autoload.</summary>
	public void SaveState()
	{
		// Multiplayer positions are transient: only the single-player save is persisted.
		if (Multiplayer.MultiplayerPeer != null)
			return;

		global ??= GetNodeOrNull<Global>("/root/Global");
		global?.Save(GlobalPosition);
	}

	public override void _ExitTree()
	{
		// Persist progress before leaving the scene (single-player).
		SaveState();

		if (networkHandler != null &&
			networkHandler.IsConnected("player_nickname_changed", nicknameChangedCallable))
		{
			networkHandler.Disconnect("player_nickname_changed", nicknameChangedCallable);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		// Only restrict control when a multiplayer session is actually running.
		// In single-player (no peer) the local player always has control.
		if (Multiplayer.MultiplayerPeer != null && !IsMultiplayerAuthority())
			return;

		float horizontal = Input.GetAxis("ui_left", "ui_right");
		if (Mathf.Abs(horizontal) < 0.01f)
			horizontal = Input.GetAxis("mover_esquerda", "mover_direita");

		if (Mathf.Abs(horizontal) > 0.01f)
			facing = horizontal < 0 ? "left" : "right";

		float frameDelta = (float)delta;
		bool onFloor = IsOnFloor();
		coyoteTimeRemaining = onFloor
			? CoyoteTime
			: Mathf.Max(0, coyoteTimeRemaining - frameDelta);

		if (Input.IsActionJustPressed("pular"))
			jumpBufferTimeRemaining = JumpBufferTime;
		else
			jumpBufferTimeRemaining = Mathf.Max(0, jumpBufferTimeRemaining - frameDelta);

		float targetSpeed = horizontal * MoveSpeed;
		float acceleration = onFloor ? GroundAcceleration : AirAcceleration;
		float horizontalChange = Mathf.Abs(horizontal) < 0.01f && onFloor
			? GroundFriction
			: acceleration;
		float velocityY = Velocity.Y;

		if (onFloor && velocityY > 0)
			velocityY = 0;
		else if (!onFloor)
			velocityY += Gravity * frameDelta;

		if (jumpBufferTimeRemaining > 0 && coyoteTimeRemaining > 0)
		{
			velocityY = JumpVelocity;
			jumpBufferTimeRemaining = 0;
			coyoteTimeRemaining = 0;
		}

		Velocity = new Vector2(
			Mathf.MoveToward(Velocity.X, targetSpeed, horizontalChange * frameDelta),
			velocityY
		);
		MoveAndSlide();

		if (!IsOnFloor())
			UpdateAnimation("jump_" + facing);
		else if (Mathf.Abs(Velocity.X) > 1f)
			UpdateAnimation("walk_" + facing);
		else
			UpdateAnimation("idle_" + facing);
	}

	private void AddJumpAnimation(string animationName, string texturePath)
	{
		if (sprite == null)
			return;

		SpriteFrames frames = sprite.SpriteFrames;
		if (frames.HasAnimation(animationName))
			return;

		Texture2D texture = GD.Load<Texture2D>(texturePath);
		if (texture == null || texture.GetWidth() % SpriteFrameWidth != 0 ||
			texture.GetHeight() < SpriteFrameHeight)
		{
			GD.PushError($"A textura da animação {animationName} está ausente ou tem dimensões inválidas: {texturePath}");
			return;
		}

		frames.AddAnimation(animationName);
		frames.SetAnimationSpeed(animationName, 8f);
		frames.SetAnimationLoopMode(animationName, SpriteFrames.LoopMode.Linear);
		int frameCount = texture.GetWidth() / SpriteFrameWidth;
		for (int index = 0; index < frameCount; index++)
		{
			var atlas = new AtlasTexture
			{
				Atlas = texture,
				Region = new Rect2(
					index * SpriteFrameWidth,
					0,
					SpriteFrameWidth,
					SpriteFrameHeight
				)
			};
			frames.AddFrame(animationName, atlas);
		}
	}

	private void UpdateAnimation(string animationName)
	{
		if (sprite == null)
			return;

		if (sprite.Animation != animationName)
			sprite.Play(animationName);
	}

	private void OnPlayerNicknameChanged(long changedPlayerId, string nickname)
	{
		if (changedPlayerId == playerId)
			UpdateNickname(nickname);
	}

	private void UpdateNickname(string nickname)
	{
		if (nicknameLabel == null)
			return;

		nicknameLabel.Text = string.IsNullOrWhiteSpace(nickname)
			? $"Jogador {playerId}"
			: nickname;
	}

	public override void _EnterTree()
	{
		if (int.TryParse(Name.ToString(), out int peerId))
			SetMultiplayerAuthority(peerId, true);
	}
}
