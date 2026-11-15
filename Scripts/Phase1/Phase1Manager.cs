using Godot;
using System.Collections.Generic;

/// <summary>
/// Level 1 manager - the EXTERIOR of the spaceship, zero gravity, grip-to-grip
/// movement with the SAFER + rope, a loose pistol tool to fetch, and a mandatory
/// return to the ship. Replaces the old interior Phase 1 rail level.
///
/// The ship is drawn by a Sprite2D (ShipTexturePath, fallback rectangle if the file
/// is missing). Every layout position below (grips, tool, CPS) is written in the
/// IMAGE's own pixel space (origin = top-left of the image) and converted to world
/// space by <see cref="ImageToWorld"/> so reading coordinates from an image editor
/// works and changing ShipScale never breaks the layout.
/// </summary>
public partial class Phase1Manager : Node2D
{
	public enum PhaseState { Start, Transit, CpsArrival, Minigame, Return, Completed, Failed }

	// ------------------------------------------------------------------
	// SHIP / CAMERA
	// ------------------------------------------------------------------
	[Export] public string ShipTexturePath = "res://Sprites/ship_exterior.png";
	[Export] public float ShipScale = 1f;
	[Export] public float CameraZoom = 1f;
	[Export] public float BoundsMargin = 40f;      // extra space around the image for bounds / camera limits.
	[Export] public float FadeInDuration = 1.5f;   // slow fade from black at level start.

	// ------------------------------------------------------------------
	// PISTOL TOOL
	// ------------------------------------------------------------------
	[Export] public string ToolTexturePath = "res://Sprites/pistol_tool.png";
	[Export] public Vector2 ToolImagePos = new Vector2(760, 760); // image pixels.
	[Export] public float PickupRadius = 40f;

	// ------------------------------------------------------------------
	// CPS
	// ------------------------------------------------------------------
	[Export] public Vector2 CpsImagePos = new Vector2(624, 344); // image pixels.

	[Export] public float MissionTime = 0f;
	[Export] public string NextScenePath = "res://Scenes/Phase2/Phase2.tscn";

	// ==================================================================
	// LEVEL 1 LAYOUT (all positions in IMAGE pixels; origin = top-left).
	// Read these from an image editor and paste with the F2 Shift+click tool.
	// Each GripDef is one discrete handhold; flags mark its role.
	// ==================================================================
	private class GripDef
	{
		public Vector2 Pos;
		public bool IsStart;
		public bool IsAirlock;
		public bool IsCps;
	}

	private readonly List<GripDef> GripDefs = new()
	{
		new GripDef { Pos = new Vector2(300, 300), IsStart = true, IsAirlock = true }, // start + return (side of ship)
		new GripDef { Pos = new Vector2(410, 300) },
		new GripDef { Pos = new Vector2(520, 270) },
		new GripDef { Pos = new Vector2(630, 300), IsCps = true },                     // next to the CPS
		new GripDef { Pos = new Vector2(742, 332) },
		new GripDef { Pos = new Vector2(852, 360) },
	};

	private const float TetherValidationMargin = 40f;

	// ------------------------------------------------------------------
	// Runtime state
	// ------------------------------------------------------------------
	private PhaseState state = PhaseState.Start;
	private readonly Dictionary<int, bool> playerCompleted = new();
	private bool localCompleted = false;
	private string failureReason = "";

	private CpsMinigame cpsMinigame;
	private MultiplayerSpawner spawner;

	private Label hudCharges;
	private Label hudTether;
	private Label hudObjective;
	private Label hudTimer;
	private Label hudDebug;
	private Label hudPrompt;
	private Label hudTool;

	private ColorRect screenOverlay;
	private Label screenTitle;
	private Label screenReason;
	private Label screenDetails;
	private Button screenBtnRetry;
	private Button screenBtnMenu;
	private Button screenBtnNext;

	private Node2D worldLayer;
	private CanvasLayer hudLayer;
	private EvaPlayer localPlayer;
	private MissionSummary missionSummary;

	// Ship / layout runtime.
	private Node2D shipNode;                  // holds the Sprite2D or fallback rect (centered at origin).
	private Vector2 shipTextureSize = new Vector2(1600, 900); // unscaled image size.
	private Rect2 levelBounds;
	private readonly List<Grip> grips = new();
	private Grip startGrip;
	private Grip airlockGrip;
	private Node toolNode;                    // the loose pistol tool.
	private Vector2 toolWorldPos;

	// Debug overlay (F2).
	private bool debugActive = false;
	private readonly List<Label> debugGripLabels = new();
	private Label debugToolLabel;
	private Label debugMouseLabel;

	// Public accessors used by the player (EvaPlayer).
	public bool DebugActive => debugActive;
	public Grip StartGrip => startGrip;
	public Vector2 CpsWorldPosition => ImageToWorld(CpsImagePos);

	public bool IsNearTool(Vector2 pos) =>
		toolNode != null && IsInstanceValid(toolNode) && toolWorldPos.DistanceTo(pos) <= PickupRadius;

	/// <summary>True while the player must not control the astronaut (minigame open or level over).</summary>
	public bool InputLocked =>
		state == PhaseState.Completed ||
		state == PhaseState.Failed ||
		(cpsMinigame != null && cpsMinigame.IsOpen);
// ------------------------------------------------------------------
	// USER-FACING TEXTS
	// ------------------------------------------------------------------
	private const string Level1Controls =
		"Hold LEFT mouse: reach for a grip and pull yourself to it (release mid-pull to return)\n" +
		"Hold RIGHT mouse: reel back to your last grip (while floating)\n" +
		"SPACE: SAFER jet (point at the cursor, you move the opposite way)\n" +
		"E: pick up the pistol tool / start the CPS calibration\n" +
		"P: pause";

	private const string Level1Briefing =
		"Out in zero gravity with no floor, the only way to move is handhold to handhold and your SAFER jet.\n\n" +
		"Hold LEFT mouse to extend your arm toward a yellow grip and pull yourself across. The tether is " +
		"anchored to the last grip you held - hold RIGHT mouse to reel back so you never drift away.\n\n" +
		"A NASA pistol tool floats near the base of the ship, out of arm's reach: use the SAFER (SPACE) to " +
		"go fetch it, then climb grip to grip to the CPS in the middle of the hull. Press E while clipped to " +
		"a CPS grip to calibrate. When it's done, return to the airlock grip to complete the level.";

	// ------------------------------------------------------------------
	// Coordinate conversion (image pixels <-> world)
	// ------------------------------------------------------------------

	/// <summary>Image-pixel coordinate (origin = top-left of the image) to world space.</summary>
	private Vector2 ImageToWorld(Vector2 imagePos)
	{
		Vector2 origin = (shipNode != null ? shipNode.GlobalPosition : Vector2.Zero)
			- shipTextureSize * ShipScale * 0.5f;
		return origin + imagePos * ShipScale;
	}

	/// <summary>World to image-pixel; used only by the debug overlay (F2).</summary>
	public Vector2 WorldToImage(Vector2 worldPos)
	{
		Vector2 origin = (shipNode != null ? shipNode.GlobalPosition : Vector2.Zero)
			- shipTextureSize * ShipScale * 0.5f;
		return (worldPos - origin) / ShipScale;
	}

	public override void _Ready()
	{
		RegisterGrabInput();
		BuildLevel();
		ConfigureSpawner();
		SetupHud();
		SetupScreens();
		SetupMissionSummary();
		SetupFadeIn();
		SetupDecor();

		FindLocalPlayer();
		if (localPlayer == null && !Multiplayer.HasMultiplayerPeer())
			SpawnSoloPlayer();

		// Solo: start attached on the IsStart grip with the camera framed on the ship.
		if (localPlayer != null && !Multiplayer.HasMultiplayerPeer())
			SetupPlayerStart(localPlayer);

		// Multiplayer: hide the editor-instanced placeholder suit and disable its camera.
		if (Multiplayer.HasMultiplayerPeer())
		{
			foreach (Node child in GetChildren())
			{
				if (child is EvaPlayer p && !int.TryParse(p.Name, out _))
				{
					p.Visible = false;
					if (p.playerCamera != null)
						p.playerCamera.Enabled = false;
				}
			}
		}

		TryEnsureHostPlayer();

		UpdateObjective();
		ValidateLayout();
		PauseMenu.Attach(this, Level1Controls);
		BriefingScreen.Open(this, "LEVEL 1 - EXTERIOR", Level1Briefing, Level1Controls);
		GD.Print("Level1: level built");
	}

	public override void _Process(double delta)
	{
		if (state == PhaseState.Completed || state == PhaseState.Failed) return;

		MissionTime += (float)delta;
		hudTimer.Text = $"Time: {MissionTime:F1}s";

		if (localPlayer == null || !IsInstanceValid(localPlayer))
			FindLocalPlayer();

		UpdateGripHighlights();
		UpdateProgressState();
		CheckReturnCompletion();
		UpdateHud();
		if (debugActive)
			UpdateDebugOverlay();
	}

	private void UpdateProgressState()
	{
		if (localPlayer == null) return;
		if (state == PhaseState.Minigame || state == PhaseState.Completed || state == PhaseState.Failed) return;

		PhaseState next;
		if (localCompleted)
			next = PhaseState.Return;                       // minigame done: go back to the airlock.
		else if (localPlayer.HasTool && localPlayer.IsHoldingCpsGrip)
			next = PhaseState.CpsArrival;
		else
			next = PhaseState.Transit;

		if (next != state)
		{
			state = next;
			UpdateObjective();
		}
	}

	/// <summary>Completes the level when the team returns to the airlock grip after the minigame.</summary>
	private void CheckReturnCompletion()
	{
		if (localCompleted && state == PhaseState.Return
			&& localPlayer != null && localPlayer.IsHoldingAirlockGrip)
		{
			CompleteLevel();
		}
	}

	private void CompleteLevel()
	{
		if (state == PhaseState.Completed || state == PhaseState.Failed) return;
		int peerId = Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetUniqueId() : 1;
		if (Multiplayer.HasMultiplayerPeer())
			Rpc(nameof(RpcReportComplete), peerId);
		else
			RpcReportComplete(peerId);
	}

	private void RegisterGrabInput()
	{
		if (!InputMap.HasAction("grab"))
		{
			InputMap.AddAction("grab");
			InputMap.ActionAddEvent("grab", new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
		}
		if (!InputMap.HasAction("pular"))
		{
			InputMap.AddAction("pular");
			InputMap.ActionAddEvent("pular", new InputEventKey { PhysicalKeycode = Key.Space });
		}
	}
// ------------------------------------------------------------------
	// LEVEL CONSTRUCTION
	// ------------------------------------------------------------------

	private void BuildLevel()
	{
		// Screen-space background only (does not move with the camera).
		var backgroundLayer = new CanvasLayer { Name = "Background", Layer = -100 };
		AddChild(backgroundLayer);
		var bg = new ColorRect { Color = new Color(0.02f, 0.02f, 0.06f, 1f) };
		bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		backgroundLayer.AddChild(bg);

		// Everything else lives in WORLD space so it follows the camera.
		worldLayer = new Node2D { Name = "World", ZIndex = -10 };
		AddChild(worldLayer);

		// Static stars.
		var rng = new RandomNumberGenerator { Seed = 12345 };
		for (int i = 0; i < 260; i++)
		{
			var star = new ColorRect
			{
				Color = new Color(1f, 1f, 1f, rng.RandfRange(0.3f, 0.9f)),
				Size = new Vector2(2f, 2f),
				Position = new Vector2(rng.RandfRange(-1000f, 1000f), rng.RandfRange(-700f, 700f))
			};
			worldLayer.AddChild(star);
		}

		BuildShip();
		BuildCpsMarker();
		BuildGrips();
		BuildTool();
		ComputeBounds();
	}

	/// <summary>Ship sprite (or a plain rectangle fallback) centered at the world origin.</summary>
	private void BuildShip()
	{
		shipNode = new Node2D { Name = "Ship" };
		worldLayer.AddChild(shipNode);

		if (ResourceLoader.Exists(ShipTexturePath))
		{
			var tex = GD.Load<Texture2D>(ShipTexturePath);
			if (tex != null)
			{
				shipTextureSize = new Vector2(tex.GetWidth(), tex.GetHeight());
				var spr = new Sprite2D { Texture = tex, Scale = Vector2.One * ShipScale };
				shipNode.AddChild(spr);
				return;
			}
		}

		// Fallback: a plain rectangle so the level still runs when the art is missing.
		shipTextureSize = new Vector2(1600, 900);
		var fallback = new ColorRect
		{
			Color = new Color(0.28f, 0.3f, 0.36f),
			Size = shipTextureSize * ShipScale,
			Position = -shipTextureSize * ShipScale * 0.5f
		};
		shipNode.AddChild(fallback);
	}

	private void BuildCpsMarker()
	{
		Vector2 pos = CpsWorldPosition;
		var marker = new ColorRect
		{
			Color = new Color(0.95f, 0.6f, 0.2f, 0.8f),
			Size = new Vector2(20f, 20f),
			Position = pos - new Vector2(10f, 10f)
		};
		worldLayer.AddChild(marker);
	}
private void BuildGrips()
	{
		grips.Clear();
		foreach (GripDef gd in GripDefs)
		{
			var g = new Grip
			{
				Name = $"Grip_{grips.Count}",
				IsStart = gd.IsStart,
				IsAirlock = gd.IsAirlock,
				IsCps = gd.IsCps,
				Position = ImageToWorld(gd.Pos)
			};
			grips.Add(g);
			worldLayer.AddChild(g);

			if (gd.IsStart && startGrip == null) startGrip = g;
			if (gd.IsAirlock && airlockGrip == null) airlockGrip = g;
		}

		if (airlockGrip == null) airlockGrip = startGrip;
		if (startGrip == null && grips.Count > 0) startGrip = grips[0];
	}

	private void BuildTool()
	{
		toolWorldPos = ImageToWorld(ToolImagePos);

		if (ResourceLoader.Exists(ToolTexturePath))
		{
			var tex = GD.Load<Texture2D>(ToolTexturePath);
			if (tex != null)
			{
				var spr = new Sprite2D { Texture = tex, Position = toolWorldPos };
				toolNode = spr;
				worldLayer.AddChild(spr);
				return;
			}
		}

		// Fallback icon drawn in code (small yellow box).
		var rect = new ColorRect
		{
			Color = Colors.Yellow,
			Size = new Vector2(14f, 14f),
			Position = toolWorldPos - new Vector2(7f, 7f)
		};
		toolNode = rect;
		worldLayer.AddChild(rect);
	}

	/// <summary>Bounds and camera limits come from the image size plus a margin.</summary>
	private void ComputeBounds()
	{
		Vector2 half = shipTextureSize * ShipScale * 0.5f;
		levelBounds = new Rect2(
			-half.X - BoundsMargin,
			-half.Y - BoundsMargin,
			shipTextureSize.X * ShipScale + BoundsMargin * 2f,
			shipTextureSize.Y * ShipScale + BoundsMargin * 2f);
	}

	private void ConfigureSpawner()
	{
		spawner = GetNodeOrNull<MultiplayerSpawner>("MultiplayerSpawner");
		if (spawner == null)
		{
			spawner = new MultiplayerSpawner { Name = "MultiplayerSpawner" };
			AddChild(spawner);
		}
		spawner.SpawnPath = new NodePath(".");
		spawner.AddSpawnableScene("res://Scenes/Phase1/EvaPlayer.tscn");
		spawner.Spawned += OnPlayerSpawned;
	}

	private EvaPlayer FindLocalPlayer()
	{
		if (Multiplayer.HasMultiplayerPeer())
		{
			foreach (Node node in GetTree().GetNodesInGroup("network_players"))
			{
				if (node is EvaPlayer p && int.TryParse(p.Name, out _) && p.IsMultiplayerAuthority())
				{
					localPlayer = p;
					return p;
				}
			}
		}
		else
		{
			foreach (Node child in GetChildren())
			{
				if (child is EvaPlayer p)
				{
					localPlayer = p;
					return p;
				}
			}
		}
		return null;
	}

	private void SpawnSoloPlayer()
	{
		foreach (Node child in GetChildren())
			if (child is EvaPlayer p && p.Visible) return;
		var player = new EvaPlayer { Name = "1" };
		AddChild(player);
		localPlayer = player;
		player.SetMultiplayerAuthority(1, true);
		SetupPlayerStart(player);   // also covers the host-fallback spawn in MP.
		GD.Print($"Level1: player spawned at {localPlayer.GlobalPosition}");
	}

	private void SetupPlayerStart(EvaPlayer p)
	{
		p.StartOnGrip(startGrip);
		SetupCamera(p);
	}

	private void SetupCamera(EvaPlayer p)
	{
		if (p == null || p.playerCamera == null) return;
		p.LevelBounds = levelBounds;               // out-of-bounds check uses the image-derived bounds.
		p.playerCamera.Zoom = new Vector2(CameraZoom, CameraZoom);
		p.playerCamera.LimitLeft = (int)levelBounds.Position.X;
		p.playerCamera.LimitTop = (int)levelBounds.Position.Y;
		p.playerCamera.LimitRight = (int)levelBounds.End.X;
		p.playerCamera.LimitBottom = (int)levelBounds.End.Y;
	}

	private void OnPlayerSpawned(Node node)
	{
		if (Multiplayer.HasMultiplayerPeer())
			FindLocalPlayer();

		if (node is EvaPlayer p && Multiplayer.HasMultiplayerPeer())
		{
			p.StartOnGrip(startGrip);
			SetupCamera(p);
			GD.Print($"Level1: spawned player {p.Name} at {p.GlobalPosition}");
		}
	}

	private async void TryEnsureHostPlayer()
	{
		await ToSignal(GetTree().CreateTimer(0.5f), SceneTreeTimer.SignalName.Timeout);
		if (!IsInstanceValid(this) || !Multiplayer.HasMultiplayerPeer() || !Multiplayer.IsServer()) return;
		if (FindLocalPlayer() == null)
			SpawnSoloPlayer();
	}
private void SetupHud()
	{
		hudLayer = new CanvasLayer { Name = "HUD", Layer = 100 };
		AddChild(hudLayer);

		hudCharges = new Label { Position = new Vector2(20, 20), Text = "SAFER: 3/3" };
		hudLayer.AddChild(hudCharges);

		hudTether = new Label { Position = new Vector2(20, 44), Text = "Tether: OK" };
		hudLayer.AddChild(hudTether);

		hudObjective = new Label { Position = new Vector2(20, 68), Text = "Objective: ..." };
		hudLayer.AddChild(hudObjective);

		hudTimer = new Label { Position = new Vector2(20, 92), Text = "Time: 0.0s" };
		hudLayer.AddChild(hudTimer);

		hudTool = new Label { Position = new Vector2(20, 116), Text = "Pistol tool: missing" };
		hudLayer.AddChild(hudTool);

		hudDebug = new Label { Position = new Vector2(20, 140), Text = "Debug: waiting..." };
		hudLayer.AddChild(hudDebug);

		hudPrompt = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center };
		hudLayer.AddChild(hudPrompt);
		hudPrompt.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
		hudPrompt.OffsetTop = -90f;
		hudPrompt.OffsetBottom = -50f;
	}

	private void UpdateHud()
	{
		if (localPlayer == null)
		{
			hudDebug.Text = "Debug: waiting for player...";
			return;
		}

		var lp = localPlayer;
		hudCharges.Text = $"SAFER: {lp.GetSaferCharges()}/{lp.SaferChargesDefault}  (Space, jet points at cursor)";

		float tension = lp.GetTetherTension();
		if (lp.IsRuptured)
		{
			hudTether.Text = "TETHER RUPTURED - SAFER ONLY";
			hudTether.Modulate = Colors.Red;
		}
		else if (lp.IsTetherCritical)
		{
			hudTether.Text = $"TETHER CRITICAL - fatigue {lp.GetFatigueStrikes()}/{lp.MaxFatigueStrikes}";
			hudTether.Modulate = Colors.Red;
		}
		else if (lp.IsTetherTaut)
		{
			hudTether.Text = "ROPE TAUT - REELING IN (hold right mouse)";
			hudTether.Modulate = Colors.Yellow;
		}
		else
		{
			hudTether.Text = $"Tether: {tension * 100f:F0}%";
			hudTether.Modulate = tension > 0.8f ? Colors.Yellow : Colors.White;
		}

		hudTool.Text = lp.HasTool ? "Pistol tool: carried" : "Pistol tool: missing";
		hudDebug.Text = $"Pos: {lp.GlobalPosition:F0} | State: {lp.state} | F9 = teleport to CPS | F2 = grip overlay";

		if (!lp.HasTool && IsNearTool(lp.GlobalPosition))
			hudPrompt.Text = "Press E to pick up the pistol tool";
		else if (lp.IsHoldingCpsGrip)
			hudPrompt.Text = lp.HasTool ? "Press E to start CPS calibration" : "You need the pistol tool";
		else
			hudPrompt.Text = "";
	}

	private void UpdateGripHighlights()
	{
		if (localPlayer == null)
		{
			foreach (Grip g in grips) { g.Highlighted = false; g.UnderCursor = false; }
			return;
		}
		Vector2 mouse = localPlayer.GetGlobalMousePosition();
		float reach = localPlayer.ArmReach;
		foreach (Grip g in grips)
		{
			g.UnderCursor = g.GlobalPosition.DistanceTo(mouse) <= 20f;
			g.Highlighted = localPlayer.GlobalPosition.DistanceTo(g.GlobalPosition) <= reach + localPlayer.GrabRadius;
		}
	}

	private void UpdateDebugOverlay()
	{
		if (localPlayer == null) return;
		for (int i = 0; i < grips.Count && i < debugGripLabels.Count; i++)
		{
			Grip g = grips[i];
			string flags = (g.IsStart ? "S" : "") + (g.IsAirlock ? "A" : "") + (g.IsCps ? "C" : "");
			debugGripLabels[i].Text = $"[{i}]{flags}";
			debugGripLabels[i].Position = g.GlobalPosition + new Vector2(12, -16);
		}
		if (debugToolLabel != null)
		{
			debugToolLabel.Text = "Tool";
			debugToolLabel.Position = toolWorldPos + new Vector2(12, 10);
		}
		if (debugMouseLabel != null)
		{
			Vector2 img = WorldToImage(localPlayer.GetGlobalMousePosition());
			debugMouseLabel.Text = $"img ({img.X:F0},{img.Y:F0})";
			debugMouseLabel.Position = localPlayer.GetGlobalMousePosition() + new Vector2(14, -16);
		}
	}
private void SetupDecor()
	{
		// One debug label per grip, plus the tool and mouse labels (all in world space).
		foreach (Grip g in grips)
		{
			debugGripLabels.Add(new Label { Visible = false, Modulate = new Color(1f, 1f, 0.5f) });
			worldLayer.AddChild(debugGripLabels[debugGripLabels.Count - 1]);
		}
		debugToolLabel = new Label { Visible = false, Modulate = new Color(1f, 0.6f, 0.6f) };
		worldLayer.AddChild(debugToolLabel);
		debugMouseLabel = new Label { Visible = false, Modulate = new Color(0.6f, 1f, 1f) };
		worldLayer.AddChild(debugMouseLabel);
	}

	/// <summary>Toggles the F2 debug grip overlay (shows indices, flags, tool and mouse in image px).</summary>
	public void ToggleDebugOverlay()
	{
		debugActive = !debugActive;
		foreach (Label l in debugGripLabels) l.Visible = debugActive;
		if (debugToolLabel != null) debugToolLabel.Visible = debugActive;
		if (debugMouseLabel != null) debugMouseLabel.Visible = debugActive;
		GD.Print("[Level1] Debug grip overlay " + (debugActive ? "ON - Shift+LMB prints image coords" : "OFF"));
	}

	/// <summary>Called by the player when the pistol tool is collected: the loose object disappears.</summary>
	public void OnToolPickedUp()
	{
		if (toolNode != null)
		{
			toolNode.QueueFree();
			toolNode = null;
		}
	}

	/// <summary>The level starts fully black and fades in slowly (about FadeInDuration seconds).</summary>
	private void SetupFadeIn()
	{
		var layer = new CanvasLayer
		{
			Name = "FadeIn",
			Layer = 200,
			ProcessMode = ProcessModeEnum.Always   // keep fading even while the briefing pauses the tree.
		};
		AddChild(layer);

		var rect = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
		rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(rect);

		Tween tween = layer.CreateTween();
		tween.TweenProperty(rect, "modulate:a", 0f, FadeInDuration);
		tween.TweenCallback(Callable.From(() => layer.QueueFree()));
	}

	private void UpdateObjective()
	{
		bool noTool = localPlayer != null && !localPlayer.HasTool;
		string objective;
		switch (state)
		{
			case PhaseState.Start:
			case PhaseState.Transit:
				objective = noTool
					? "Objective: Use the SAFER to reach the pistol tool near the base, then grab a grip"
					: "Objective: Climb grip to grip to the CPS (hold left mouse on a grip)";
				break;
			case PhaseState.CpsArrival:
				objective = noTool
					? "Objective: Go back near the base and fetch the pistol tool"
					: "Objective: Press E at the CPS grip to start calibration";
				break;
			case PhaseState.Minigame:
				objective = "Objective: Spin the mouse around the dial (Esc to cancel)";
				break;
			case PhaseState.Return:
				objective = "Objective: Return to the airlock grip (right mouse to reel back)";
				break;
			case PhaseState.Completed:
				objective = "Objective: Level complete";
				break;
			default:
				objective = "Objective: Mission failed";
				break;
		}
		hudObjective.Text = objective;
	}

	/// <summary>Startup layout checker (runs once in _Ready).</summary>
	private void ValidateLayout()
	{
		float reach = localPlayer != null ? localPlayer.ArmReach : 140f;
		if (startGrip == null)
		{
			GD.PushWarning("[Level1] VALIDATION: no IsStart grip found. Add startGrip= true to a grip.");
			return;
		}

		var reachable = new HashSet<Grip>();
		var queue = new Queue<Grip>();
		reachable.Add(startGrip);
		queue.Enqueue(startGrip);
		while (queue.Count > 0)
		{
			Grip g = queue.Dequeue();
			foreach (Grip n in grips)
			{
				if (!reachable.Contains(n) && g.GlobalPosition.DistanceTo(n.GlobalPosition) <= reach)
				{
					reachable.Add(n);
					queue.Enqueue(n);
				}
			}
		}

		bool cpsOk = false, airOk = false;
		for (int i = 0; i < grips.Count; i++)
		{
			Grip g = grips[i];
			if (g.IsCps && reachable.Contains(g)) cpsOk = true;
			if (g.IsAirlock && reachable.Contains(g)) airOk = true;
			if (!reachable.Contains(g))
				GD.PushWarning($"[Level1] VALIDATION: grip #{i} at img {WorldToImage(g.GlobalPosition)} is NOT reachable from the start (reach {reach}).");
		}
		GD.Print($"[Level1] VALIDATION: start reach {reach}; IsCps reachable={cpsOk}; IsAirlock reachable={airOk}");

		// Tool must be out of arm reach of every grip (it must need the SAFER), yet
		// reachable by the rope from the start so reeling in works.
		foreach (Grip g in grips)
		{
			if (g.GlobalPosition.DistanceTo(toolWorldPos) <= reach)
			{
				GD.PushWarning("[Level1] VALIDATION: pistol tool is within arm reach of a grip - it must need the SAFER.");
				break;
			}
		}
		float maxRope = localPlayer != null ? localPlayer.TetherMaxLength : 300f;
		float distToolStart = startGrip.GlobalPosition.DistanceTo(toolWorldPos);
		if (distToolStart > maxRope - TetherValidationMargin)
			GD.PushWarning($"[Level1] VALIDATION: tool is {distToolStart:F0}px from start (rope max {maxRope}) - beyond rope range, SAFER required (intended).");
		else
			GD.Print($"[Level1] VALIDATION: tool is {distToolStart:F0}px from start (< rope max {maxRope}) - reachable by reeling.");
	}
private void SetupScreens()
	{
		var screenLayer = new CanvasLayer { Name = "Screens", Layer = 110 };
		AddChild(screenLayer);

		screenOverlay = new ColorRect
		{
			Color = new Color(0, 0, 0, 0.8f),
			Visible = false
		};
		screenLayer.AddChild(screenOverlay);
		screenOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var center = new CenterContainer();
		screenOverlay.AddChild(center);
		center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var screenContainer = new VBoxContainer
		{
			CustomMinimumSize = new Vector2(520, 0),
			Alignment = BoxContainer.AlignmentMode.Center
		};
		center.AddChild(screenContainer);

		screenTitle = new Label { Text = "MISSION FAILED", HorizontalAlignment = HorizontalAlignment.Center };
		screenContainer.AddChild(screenTitle);

		screenReason = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center };
		screenContainer.AddChild(screenReason);

		screenDetails = new Label
		{
			Text = "",
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart
		};
		screenContainer.AddChild(screenDetails);

		screenBtnRetry = new Button { Text = "Retry" };
		screenBtnRetry.Pressed += Retry;
		screenContainer.AddChild(screenBtnRetry);

		screenBtnNext = new Button { Text = "Next: Phase 2", Visible = false };
		screenBtnNext.Pressed += GoToNextPhase;
		screenContainer.AddChild(screenBtnNext);

		screenBtnMenu = new Button { Text = "Back to Menu" };
		screenBtnMenu.Pressed += BackToMenu;
		screenContainer.AddChild(screenBtnMenu);
	}

	private void SetupMissionSummary()
	{
		missionSummary = new MissionSummary { Name = "MissionSummary" };
		missionSummary.ContinuePressed += () => missionSummary.HideScreen();
		AddChild(missionSummary);
	}

	public void RequestMinigame()
	{
		if (state == PhaseState.Completed || state == PhaseState.Failed) return;
		if (localCompleted) return;
		if (cpsMinigame != null && cpsMinigame.IsOpen) return;

		if (cpsMinigame == null)
		{
			cpsMinigame = new CpsMinigame { Name = "CpsMinigame", Layer = 50 };
			cpsMinigame.MinigameCompleted += OnLocalMinigameCompleted;
			cpsMinigame.MinigameCancelled += OnLocalMinigameCancelled;
			AddChild(cpsMinigame);
		}
		cpsMinigame.Open();
		state = PhaseState.Minigame;
		UpdateObjective();
	}

	private void OnLocalMinigameCancelled()
	{
		if (state == PhaseState.Minigame)
			state = PhaseState.CpsArrival;
		UpdateObjective();
	}

	private void OnLocalMinigameCompleted()
	{
		localCompleted = true;
		state = PhaseState.Return;   // minigame done: now the level needs the return to the airlock.
		UpdateObjective();
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	private void RpcReportComplete(int peerId)
	{
		playerCompleted[peerId] = true;
		CheckCompletion();
	}

	private void CheckCompletion()
	{
		int required = Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetPeers().Length + 1 : 1;
		int completed = 0;
		foreach (var kv in playerCompleted)
			if (kv.Value) completed++;

		if (completed >= required && state != PhaseState.Completed && state != PhaseState.Failed)
		{
			state = PhaseState.Completed;
			ShowSuccessScreen();
		}
	}

	public void OnMissionFailed(string reason)
	{
		if (state == PhaseState.Completed || state == PhaseState.Failed) return;
		if (Multiplayer.HasMultiplayerPeer())
			Rpc(nameof(RpcReportFailed), reason);
		else
			RpcReportFailed(reason);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	private void RpcReportFailed(string reason)
	{
		if (state == PhaseState.Completed || state == PhaseState.Failed) return;
		failureReason = reason;
		state = PhaseState.Failed;
		if (cpsMinigame != null && cpsMinigame.IsOpen) cpsMinigame.Close();
		ShowFailureScreen();
	}
private void ShowSuccessScreen()
	{
		screenOverlay.Visible = true;
		screenTitle.Text = "LEVEL 1 COMPLETE";
		screenReason.Text = "Thermal actuators unfrozen. Pistol tool recovered and returned to the airlock.";
		screenDetails.Text = $"Time: {MissionTime:F1}s | Energy: 160 kWh | Comms: stable (1.3-2s latency) | Mass: ~115,000 kg";
		screenBtnRetry.Visible = false;
		screenBtnNext.Visible = ResourceLoader.Exists(NextScenePath);
		screenBtnMenu.Text = "Back to Menu";
		screenBtnMenu.Visible = true;

		// End-of-mission ranking (F-S). CONTINUE hides this overlay and the standard
		// success buttons (Next phase / Back to Menu) appear below.
		// TODO(flow): when a GameFlow/"return to DHS" progression exists, this should
		// load the DHS (Level 2) scene instead of stopping on the result screen.
		FindLocalPlayer();
		if (localPlayer != null)
			missionSummary.Show(
				MissionTime,
				localPlayer.GetFatigueStrikes(),
				localPlayer.IsRuptured,
				localPlayer.GetSaferCharges(),
				"LEVEL 1 COMPLETE");
		else
			missionSummary.Show(MissionTime, 0, false, 0, "LEVEL 1 COMPLETE");

		Sfx.Play(this, "mission_complete");
		UpdateObjective();
	}

	private void ShowFailureScreen()
	{
		screenOverlay.Visible = true;
		screenTitle.Text = "MISSION FAILED";
		screenReason.Text = failureReason;
		screenDetails.Text = "Return to the airlock and try again.";
		screenBtnRetry.Visible = true;
		screenBtnNext.Visible = false;
		screenBtnMenu.Text = "Back to Menu";
		screenBtnMenu.Visible = true;
		Sfx.Play(this, "mission_fail");
		UpdateObjective();
	}

	private void Retry() => RestartPhase();

	/// <summary>Single restart entry point for Level 1 (out-of-bounds, retry button, etc.).</summary>
	public void RestartPhase()
	{
		GetTree().ChangeSceneToFile("res://Scenes/Phase1/Phase1.tscn");
	}

	private void GoToNextPhase()
	{
		if (ResourceLoader.Exists(NextScenePath))
			GetTree().ChangeSceneToFile(NextScenePath);
	}

	private void BackToMenu()
	{
		GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
	}
}