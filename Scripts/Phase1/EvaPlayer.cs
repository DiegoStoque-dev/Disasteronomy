using Godot;
using System;

/// <summary>
/// Astronaut controller for Level 1 (the exterior of the ship, zero gravity).
/// Two movement states:
///  - ATTACHED: held on a discrete grip. Hold the left mouse button to reach for
///    another grip (the arm extends up to ArmReach); when the arm tip touches a grip
///    you are pulled in a straight line to it at GrabPullSpeed and become attached.
///    Releasing the button mid-pull cancels the pull and returns you to the grip you
///    came from.
///  - FLOATING: inertial drift (no thrusters). Only the SAFER jet changes your speed.
///    While floating you can still grab any grip within reach; the right mouse button
///    reels you back to the last grip you held (the rope anchor) so you can never
///    get soft-locked.
///
/// The rope (tether) is anchored to the LAST grip the player held, so the anchor is
/// always a grip. At full length only the outward radial velocity is removed and the
/// position is clamped (taut-rope model: no bouncing, no energy-gain).
/// </summary>
public partial class EvaPlayer : CharacterBody2D
{
	public enum PlayerState { Attached, Floating }
	public enum RopeState { Rope, BrokenArm }
	public enum TetherStress { Green, Taut, Red }

	public RopeState ropeState = RopeState.Rope;
	public bool IsTetherBroken => ropeState == RopeState.BrokenArm;
	public bool IsRuptured => isRuptured;

	// --- Movement / burst exports ------------------------------------------------------------------
	[Export] public bool EnableThrusters = false; // Level 1: no WASD RCS (walking on air). Code kept, left disabled.
	[Export] public bool EnableSafer = true;      // SAFER jet stays available during the level.
	[Export] public float MaxDriftSpeed = 65f;
	[Export] public float SuitAcceleration = 150f;
	[Export] public float DriftDrag = 0.6f;

	// --- Grip / arm exports -------------------------------------------------------------------------
	[Export] public float ArmReach = 140f;        // how far the arm extends toward the cursor / a grip.
	[Export] public float GrabPullSpeed = 180f;   // straight-line grip-to-grip pull speed.
	[Export] public float GrabPullBrakeDist = 60f; // reserved for tuning.
	[Export] public float GrabRadius = 30f;       // arm tip vs grip distance that counts as "touched".

	// --- Tether (rope) exports -----------------------------------------------------------------------
	[Export] public int MaxFatigueStrikes = 3;
	[Export] public float CriticalSnapLoadSpeed = 95f;
	[Export] public float RedTensionDuration = 0.35f;
	[Export] public float TetherMaxLength = 300f;
	[Export] public float TetherDisableThrustMargin = 20f;
	[Export] public float ReelSpeed = 120f;       // right-mouse reel back toward the last grip (px/s).
	[Export] public float ReelArriveDist = 8f;    // a few px from the anchor -> re-attach.

	// --- SAFER ---------------------------------------------------------------------------------------
	[Export] public int SaferChargesDefault = 3;
	[Export] public float SaferImpulse = 140f;
	[Export] public float SaferCooldown = 1.0f;

	// --- World / misc --------------------------------------------------------------------------------
	[Export] public Rect2 LevelBounds = new Rect2(-1000, -600, 2000, 1400);
	[Export] public float OutOfBoundsTimeLimit = 3f;
	[Export] public bool DebugKeys = true; // F9 = teleport near the CPS, F2 = debug grip overlay.
	[Export] public float SpinDamping = 1.2f;
	[Export] public float ReleaseRandomSpin = 90f;
	[Export] public float SaferRandomSpin = 120f;
	[Export] public float GrabSpinDamping = 8f;
	[Export] public float ThrusterPuffInterval = 0.08f;
	[Export] public string SpriteFramesPath = "";

	private Line2D tetherLine;
	private AnimatedSprite2D sprite;
	private bool facingRight = true;
	private Line2D armLine;
	private MultiplayerSynchronizer sync;
	private Label nicknameLabel;
	public Camera2D playerCamera;
	private Phase1Manager manager;

	public PlayerState state = PlayerState.Floating;

	// Grip state.
	private Grip currentGrip;      // grip currently held (only in ATTACHED).
	private Grip ropeAnchorGrip;   // LAST grip held -> the rope anchor (always a grip).

	// Arm.
	private Vector2 armTip = Vector2.Zero;
	private Vector2 armDirection = Vector2.Down;

	// Pull (grip-to-grip grab-and-pull, with cancel back to the origin).
	private bool pulling = false;
	private Grip pullOriginGrip;   // the grip we came from (null when the pull started while floating).
	private Grip pullGoalGrip;     // the grip we attach to when the pull finishes.
	private Vector2 pullTargetPos = Vector2.Zero;
	private bool pullCancel = false;
	private bool returnAfterCancel = false; // after a cancelled pull we dock without needing the button held.

	// Tether / rope.
	private float tetherDistance = 0f;
	private int stressCount = 0;
	private bool wasBeyondMax = false;
	private bool hasTriggeredThisCycle = false;
	private TetherStress tetherStress = TetherStress.Green;
	private bool isRuptured = false;
	private float redTimer = 0f;

	// SAFER.
	private int saferCharges;
	private float saferCooldownTimer = 0f;

	private float outOfBoundsTimer = 0f;
	private float puffTimer = 0f;
	private Node2D visual;
	private float spinDeg = 0f;
	private float spinVelocity = 0f;
	private RandomNumberGenerator rng = new();

	// Edge detection.
	private bool eWasDown = false;
	private bool f9WasDown = false;
	private bool f2WasDown = false;
	private bool ignoreLeftClick = false; // shift+click in debug mode must not grab.

	/// <summary>True once the pistol tool has been collected by this player.</summary>
	public bool HasTool { get; private set; } = false;
	public Grip CurrentGrip => currentGrip;
	public bool IsHoldingCpsGrip => state == PlayerState.Attached && currentGrip != null && currentGrip.IsCps;
	public bool IsHoldingAirlockGrip => state == PlayerState.Attached && currentGrip != null && currentGrip.IsAirlock;

	private Phase1Manager Manager
	{
		get
		{
			if (manager == null || !IsInstanceValid(manager))
			{
				Node n = GetParent();
				while (n != null)
				{
					if (n is Phase1Manager m) { manager = m; break; }
					n = n.GetParent();
				}
			}
			return manager;
		}
	}

	public override void _EnterTree()
	{
		if (int.TryParse(Name, out int peerId))
			SetMultiplayerAuthority(peerId, true);
	}
public override void _Ready()
	{
		AddToGroup("network_players");

		foreach (Node node in GetTree().GetNodesInGroup("network_players"))
		{
			if (node == this || !(node is CharacterBody2D other)) continue;
			AddCollisionExceptionWith(other);
			other.AddCollisionExceptionWith(this);
		}

		var collision = new CollisionShape2D();
		collision.Shape = new CircleShape2D { Radius = 16f };
		AddChild(collision);

		rng.Randomize();
		visual = new Node2D { Name = "Visual" }; // tumbles; camera/tether/arm stay un-rotated.
		AddChild(visual);

		SetupSprite();

		tetherLine = new Line2D { Name = "TetherLine", Width = 2f };
		AddChild(tetherLine);

		armLine = new Line2D { Name = "ArmLine", Width = 4f, DefaultColor = Colors.Cyan, Visible = false };
		AddChild(armLine);

		nicknameLabel = new Label
		{
			Name = "NicknameLabel",
			Position = new Vector2(-50f, -48f),
			Size = new Vector2(100f, 20f),
			HorizontalAlignment = HorizontalAlignment.Center
		};
		AddChild(nicknameLabel);

		playerCamera = new Camera2D { Name = "PlayerCamera" };
		playerCamera.PositionSmoothingEnabled = true;
		playerCamera.PositionSmoothingSpeed = 8f;
		AddChild(playerCamera);
		if (IsMultiplayerAuthority())
		{
			playerCamera.Enabled = true;
			playerCamera.MakeCurrent();
		}

		sync = new MultiplayerSynchronizer { Name = "Sync" };
		AddChild(sync);
		var replicationConfig = new SceneReplicationConfig();
		replicationConfig.AddProperty(new NodePath(".:position"));
		replicationConfig.PropertySetReplicationMode(new NodePath(".:position"), SceneReplicationConfig.ReplicationMode.Always);
		replicationConfig.AddProperty(new NodePath(".:velocity"));
		replicationConfig.PropertySetReplicationMode(new NodePath(".:velocity"), SceneReplicationConfig.ReplicationMode.Always);
		sync.ReplicationConfig = replicationConfig;

		try
		{
			var networkHandler = GetNode("/root/HighLevelNetworkHandler");
			long pid = 0;
			if (long.TryParse(Name, out pid))
			{
				string nick = networkHandler.Call("get_player_nickname", pid).AsString();
				nicknameLabel.Text = string.IsNullOrWhiteSpace(nick) ? $"Jogador {pid}" : nick;
			}
		}
		catch { nicknameLabel.Text = $"Jogador {Name}"; }

		saferCharges = SaferChargesDefault;
	}
public override void _Input(InputEvent @event)
	{
		// Debug helper: in debug overlay mode, Shift + left-click prints the clicked
		// image-pixel coordinate in layout-array format and must NOT trigger a grab.
		if (@event is InputEventMouseButton mb
			&& mb.ButtonIndex == MouseButton.Left
			&& mb.Pressed
			&& Input.IsKeyPressed(Key.Shift))
		{
			var mgr = Manager;
			if (mgr != null && mgr.DebugActive)
			{
				Vector2 img = mgr.WorldToImage(mb.GlobalPosition);
				GD.Print($"new Vector2({img.X:F1}, {img.Y:F1}),");
				GetViewport().SetInputAsHandled();
				ignoreLeftClick = true;
			}
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Multiplayer.HasMultiplayerPeer())
		{
			// Only the spawner-created suits (their names ARE the peer ids) simulate in
			// multiplayer; the editor-instanced placeholder ("EvaPlayer") is a frozen dummy.
			if (!int.TryParse(Name, out _)) return;
			if (!IsMultiplayerAuthority()) return;
		}

		float dt = (float)delta;
		var mgr = Manager;

		// Edge detection runs every frame, even while input is locked.
		bool eDown = Input.IsPhysicalKeyPressed(Key.E);
		bool ePressed = eDown && !eWasDown;
		eWasDown = eDown;

		bool f9Down = Input.IsPhysicalKeyPressed(Key.F9);
		bool f9Pressed = f9Down && !f9WasDown;
		f9WasDown = f9Down;

		bool f2Down = Input.IsPhysicalKeyPressed(Key.F2);
		bool f2Pressed = f2Down && !f2WasDown;
		f2WasDown = f2Down;

		bool grabHeld = GrabHeld() && !ignoreLeftClick;
		ignoreLeftClick = false;

		saferCooldownTimer = Mathf.Max(0f, saferCooldownTimer - dt);

		bool locked = mgr != null && mgr.InputLocked;

		if (f2Pressed && mgr != null)
			mgr.ToggleDebugOverlay();

		if (!locked)
		{
			if (DebugKeys && OS.IsDebugBuild() && f9Pressed && mgr != null)
				DebugTeleportNearCps(mgr);

			HandleSaferInput();

			if (state == PlayerState.Floating)
				UpdateFloating(dt, grabHeld);
			else
				UpdateAttached(dt, grabHeld);

			if (ePressed && mgr != null)
			{
				if (!HasTool && mgr.IsNearTool(GlobalPosition))
					PickupTool(mgr);
				else if (IsHoldingCpsGrip && HasTool)
					mgr.RequestMinigame();
				// Holding the CPS grip without the tool: the HUD prompt already says it.
			}
		}
		else
		{
			// Minigame open or level over: no grabbing / reeling.
			armLine.Visible = false;
			pulling = false;
			pullCancel = false;
			returnAfterCancel = false;
		}

		UpdateTether(dt);
		CheckBounds(dt);

		MoveAndSlide();
		UpdateSprite();

		// Cosmetic tumbling: only the "Visual" child rotates around the body.
		spinDeg += spinVelocity * dt;
		if (visual != null)
			visual.RotationDegrees = spinDeg;
	}

	/// <summary>SAFER burst. Works while floating or attached; detaches on top of a grip.</summary>
	private void HandleSaferInput()
	{
		if (!EnableSafer) return;
		if (!Input.IsActionJustPressed("pular")) return;
		if (saferCharges <= 0 || saferCooldownTimer > 0f) return;

		// Detaching fires too: the jet pushes you off the grip.
		if (state == PlayerState.Attached)
			ReleaseGrip();

		Vector2 toCursor = GetGlobalMousePosition() - GlobalPosition;
		if (toCursor.Length() > 0.01f) toCursor = toCursor.Normalized();
		Velocity -= toCursor * SaferImpulse; // jet fires toward the cursor, you go the opposite way.
		saferCharges--;
		saferCooldownTimer = SaferCooldown;
		spinVelocity += (float)(SaferRandomSpin * (rng.Randf() * 2.0 - 1.0));
		Sfx.Play(this, "safer_burst");
	}

	// -------------------------------------------------------------------------
	// FLOATING
	// -------------------------------------------------------------------------

	private void UpdateFloating(float dt, bool grabHeld)
	{
		// Inertial drift with NO thrusters for Level 1. WASD/RCS code is kept behind
		// EnableThrusters so it can be turned back on elsewhere without rewriting.
		if (EnableThrusters)
		{
			Vector2 inputDir = ReadMoveInput();
			if (inputDir != Vector2.Zero)
			{
				float accelStep = SuitAcceleration * dt;
				float vx = Mathf.MoveToward(Velocity.X, inputDir.X * MaxDriftSpeed, accelStep);
				float vy = Mathf.MoveToward(Velocity.Y, inputDir.Y * MaxDriftSpeed, accelStep);
				Velocity = new Vector2(vx, vy);
				SpawnThrusterPuff(inputDir, dt);
			}
			else
			{
				Velocity *= Mathf.Max(0f, 1f - DriftDrag * dt);
			}
		}
		else
		{
			// No thrusters: only drag bleeds off inherited/SAFER speed.
			Velocity *= Mathf.Max(0f, 1f - DriftDrag * dt);
		}

		// Right mouse: reel back toward the last grip held, overriding the drift.
		bool reeling = !isRuptured
			&& Input.IsMouseButtonPressed(MouseButton.Right)
			&& ropeAnchorGrip != null
			&& IsInstanceValid(ropeAnchorGrip);

		if (reeling)
		{
			Vector2 toAnchor = ropeAnchorGrip.GlobalPosition - GlobalPosition;
			float d = toAnchor.Length();
			if (d > 0.01f)
			{
				Velocity = toAnchor / d * ReelSpeed;
			}
			// On arrival (a few px) the player re-attaches to the anchor grip.
			if (d <= ReelArriveDist)
			{
				GlobalPosition = ropeAnchorGrip.GlobalPosition;
				currentGrip = ropeAnchorGrip;
				state = PlayerState.Attached;
				Velocity = Vector2.Zero;
				pulling = false;
				pullCancel = false;
				returnAfterCancel = false;
				spinVelocity = Mathf.MoveToward(spinVelocity, 0f, GrabSpinDamping * dt);
				Sfx.Play(this, "rail_grab");
			}
		}

		spinVelocity *= Mathf.Exp(-SpinDamping * dt);

		// Left mouse: the arm can still grab any grip within reach.
		if (grabHeld && !isRuptured)
		{
			UpdateArm();
			if (pulling)
				UpdatePull(dt);
			else if (!reeling)
			{
				Grip target = FindGripUnderArmTip(null);
				if (target != null)
					StartPull(null, target);
			}
			armLine.Points = pulling
				? new Vector2[] { Vector2.Zero, pullTargetPos - GlobalPosition }
				: new Vector2[] { Vector2.Zero, armTip - GlobalPosition };
			armLine.Visible = true;
		}
		else if (pulling)
		{
			// Released mid-pull while floating: no origin grip to return to, just stop.
			pulling = false;
			pullCancel = false;
			armLine.Visible = false;
		}
		else
		{
			armLine.Visible = false;
		}
	}
// -------------------------------------------------------------------------
	// ATTACHED
	// -------------------------------------------------------------------------

	private void UpdateAttached(float dt, bool grabHeld)
	{
		if (isRuptured)
		{
			ReleaseGrip();
			return;
		}

		if (currentGrip == null || !IsInstanceValid(currentGrip))
		{
			ReleaseGrip();
			return;
		}

		if (!grabHeld)
		{
			if (pulling)
			{
				// Released mid-pull: cancel back to the grip we came from, pulling all the way.
				if (!pullCancel)
					CancelPullToOrigin();
				UpdatePull(dt);
			}
			else if (returnAfterCancel)
			{
				// Just docked from a cancelled pull with the button up: stay attached
				// until the player moves again (press+release), instead of instantly floating.
				returnAfterCancel = false;
			}
			else
			{
				ReleaseGrip(); // let go and drift (you can always reel back to this grip).
			}
			return;
		}

		// Glue the suit to the grip; no slide.
		Velocity = Vector2.Zero;
		spinVelocity = Mathf.MoveToward(spinVelocity, 0f, GrabSpinDamping * dt);
		returnAfterCancel = false;

		if (pulling)
		{
			UpdatePull(dt);
			return;
		}

		// Reach for another grip while holding this one.
		UpdateArm();
		Grip target = FindGripUnderArmTip(currentGrip);
		if (target != null)
		{
			StartPull(currentGrip, target);
			armLine.Points = new Vector2[] { Vector2.Zero, pullTargetPos - GlobalPosition };
			armLine.Visible = true;
		}
		else
		{
			armLine.Visible = false;
		}
	}

	/// <summary>Releasing the left button mid-pull returns the player to the grip they came from.</summary>
	private void CancelPullToOrigin()
	{
		pullCancel = true;
		if (pullOriginGrip != null && IsInstanceValid(pullOriginGrip))
		{
			pullTargetPos = pullOriginGrip.GlobalPosition;
			pullGoalGrip = pullOriginGrip;
		}
		else
		{
			// Shouldn't happen while attached, but be safe: just stop pulling.
			pulling = false;
			pullCancel = false;
			armLine.Visible = false;
		}
	}

	/// <summary>Advances a straight-line grip pull at GrabPullSpeed; attaches on arrival.</summary>
	private void UpdatePull(float dt)
	{
		float dist = GlobalPosition.DistanceTo(pullTargetPos);
		float step = GrabPullSpeed * dt;

		if (dist <= step)
		{
			GlobalPosition = pullTargetPos;
			Velocity = Vector2.Zero;
			if (pullGoalGrip != null && IsInstanceValid(pullGoalGrip))
			{
				currentGrip = pullGoalGrip;
				ropeAnchorGrip = currentGrip;
				state = PlayerState.Attached;
				if (pullCancel)
					returnAfterCancel = true; // dock after a cancelled pull even though the button is up.
				spinVelocity = Mathf.MoveToward(spinVelocity, 0f, GrabSpinDamping * dt);
				Sfx.Play(this, "rail_grab");
			}
			pulling = false;
			pullCancel = false;
			armLine.Visible = false;
		}
		else
		{
			// Straight-line, no inertia.
			Velocity = Vector2.Zero;
			GlobalPosition += (pullTargetPos - GlobalPosition).Normalized() * step;
			armLine.Points = new Vector2[] { Vector2.Zero, pullTargetPos - GlobalPosition };
			armLine.Visible = true;
		}
	}

	private void StartPull(Grip from, Grip target)
	{
		pullOriginGrip = from;
		pullGoalGrip = target;
		pullTargetPos = target.GlobalPosition;
		pulling = true;
		pullCancel = false;
		Velocity = Vector2.Zero; // stop drifting the instant you grab.
	}

	/// <summary>Drops out of ATTACHED into FLOATING; the rope stays anchored to the grip just let go.</summary>
	private void ReleaseGrip()
	{
		ropeAnchorGrip = currentGrip ?? ropeAnchorGrip;
		if (state == PlayerState.Attached)
			spinVelocity += (float)(ReleaseRandomSpin * (rng.Randf() * 2.0 - 1.0));
		state = PlayerState.Floating;
		currentGrip = null;
		pulling = false;
		pullCancel = false;
		returnAfterCancel = false;
		armLine.Visible = false;
	}

	/// <summary>Attach this player to a grip (start position and re-association after spawn).</summary>
	public void StartOnGrip(Grip g)
	{
		if (g == null) return;
		currentGrip = g;
		ropeAnchorGrip = g;
		state = PlayerState.Attached;
		GlobalPosition = g.GlobalPosition;
		Velocity = Vector2.Zero;
		pulling = false;
		pullCancel = false;
		returnAfterCancel = false;
		spinVelocity = 0f;
		armLine.Visible = false;
	}

	private void UpdateArm()
	{
		Vector2 cursorPos = GetGlobalMousePosition();
		Vector2 toCursor = cursorPos - GlobalPosition;
		float dist = toCursor.Length();

		if (dist > 0.01f)
			armDirection = toCursor / dist;

		float armLen = Mathf.Min(dist, ArmReach);
		armTip = GlobalPosition + armDirection * armLen;
	}

	private Grip FindGripUnderArmTip(Grip from)
	{
		Phase1Manager mgr = Manager;
		foreach (Node node in GetTree().GetNodesInGroup("grip"))
		{
			if (node is not Grip g || g == currentGrip) continue;
			// While attached to a grip, only a NEIGHBOR grip can be grabbed (one grip per
			// grab, no skipping). While floating the player is free to grab anything in reach.
			if (from != null && (mgr == null || !mgr.AreNeighbors(from, g))) continue;
			if (armTip.DistanceTo(g.GlobalPosition) <= GrabRadius)
				return g;
		}
		return null;
	}

	private void PickupTool(Phase1Manager mgr)
	{
		HasTool = true;
		mgr.OnToolPickedUp();
		Sfx.Play(this, "tool_pickup");
		GD.Print("[Level1] Pistol tool picked up.");
	}
// -------------------------------------------------------------------------
	// TETHER / ROPE (anchored to the last grip held)
	// -------------------------------------------------------------------------

	private void UpdateTether(float dt)
	{
		if (isRuptured)
		{
			if (tetherLine != null) tetherLine.Visible = false;
			tetherDistance = 0f;
			tetherStress = TetherStress.Green;
			return;
		}

		Vector2 anchorPos;
		if (ropeAnchorGrip != null && IsInstanceValid(ropeAnchorGrip))
			anchorPos = ropeAnchorGrip.GlobalPosition;
		else
		{
			if (tetherLine != null) tetherLine.Visible = false;
			return;
		}

		Vector2 toAnchor = anchorPos - GlobalPosition;
		float dist = toAnchor.Length();
		tetherDistance = dist;

		// Keep drawing the rope from the player to the anchor, warning when almost taut.
		if (tetherLine != null)
		{
			tetherLine.Points = new Vector2[] { Vector2.Zero, ToLocal(anchorPos) };
			tetherLine.Visible = dist > 1f;

			redTimer = Mathf.Max(0f, redTimer - dt);
			if (redTimer > 0f)
				tetherLine.DefaultColor = Colors.Red;
			else
				tetherLine.DefaultColor = dist / TetherMaxLength >= 0.97f ? Colors.Yellow : new Color(0.4f, 0.9f, 0.5f);
		}

		if (dist > TetherMaxLength)
		{
			Vector2 dir = toAnchor / dist;

			// One critical-stress hit per slack->taut cycle, based on the outward radial speed.
			if (!wasBeyondMax)
			{
				wasBeyondMax = true;
				hasTriggeredThisCycle = false;
			}

			float radialOut = Velocity.Dot(dir);
			if (!hasTriggeredThisCycle && radialOut >= CriticalSnapLoadSpeed)
			{
				hasTriggeredThisCycle = true;
				ApplyCriticalStress();
				if (isRuptured) return;
			}

			// Taut-rope: remove only the outward radial component and clamp the position.
			if (radialOut > 0f)
				Velocity -= dir * radialOut;
			GlobalPosition = anchorPos - dir * TetherMaxLength;
			tetherDistance = TetherMaxLength;

			if (redTimer <= 0f)
				tetherStress = TetherStress.Taut;
		}
		else
		{
			wasBeyondMax = false;
			if (redTimer <= 0f)
				tetherStress = TetherStress.Green;
		}
	}

	public int GetSaferCharges() => saferCharges;
	public int GetFatigueStrikes() => stressCount;
	public RopeState GetRopeState() => ropeState;
	public TetherStress GetTetherStress() => tetherStress;
	public float GetTetherTension() => tetherDistance > 0f && TetherMaxLength > 0f ? tetherDistance / TetherMaxLength : 0f;
	public bool IsTetherCritical => redTimer > 0f;
	public bool IsTetherTaut => !IsRuptured && TetherMaxLength > 0f && tetherDistance >= TetherMaxLength - TetherDisableThrustMargin;

	private void ApplyCriticalStress()
	{
		if (isRuptured) return;
		stressCount++;
		tetherStress = TetherStress.Red;
		redTimer = RedTensionDuration;
		Sfx.Play(this, "rope_stress");
		GD.Print($"[Level1] Tether critical stress {stressCount}/{MaxFatigueStrikes}");
		if (stressCount >= MaxFatigueStrikes)
			SnapRope();
	}

	/// <summary>Permanent rupture: severs the rope and leaves only SAFER drift.</summary>
	private void SnapRope()
	{
		if (isRuptured) return;
		isRuptured = true;
		ropeState = RopeState.BrokenArm;
		if (state == PlayerState.Attached)
			ReleaseGrip();
		if (tetherLine != null)
			tetherLine.Visible = false;
		tetherStress = TetherStress.Green;
		hasTriggeredThisCycle = false;
		Sfx.Play(this, "rope_break");
		GD.Print("[Level1] Tether ruptured - SAFER-only drift from now on.");
	}

	private void CheckBounds(float dt)
	{
		if (!LevelBounds.HasPoint(GlobalPosition))
		{
			outOfBoundsTimer += dt;
			if (outOfBoundsTimer >= OutOfBoundsTimeLimit)
				Manager?.RestartPhase();
		}
		else
		{
			outOfBoundsTimer = 0f;
		}
	}

	private void DebugTeleportNearCps(Phase1Manager mgr)
	{
		ReleaseGrip();
		if (mgr.StartGrip != null)
			ropeAnchorGrip = mgr.StartGrip;
		GlobalPosition = mgr.CpsWorldPosition + new Vector2(-120f, 0f);
		Velocity = Vector2.Zero;
		spinVelocity = 0f;
		GD.Print("[DEBUG] F9: teleported near the CPS");
	}

	private static bool GrabHeld() => Input.IsMouseButtonPressed(MouseButton.Left);

	private static Vector2 ReadMoveInput()
	{
		float x = 0f;
		float y = 0f;
		if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left)) x -= 1f;
		if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right)) x += 1f;
		if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up)) y -= 1f;
		if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down)) y += 1f;
		Vector2 v = new Vector2(x, y);
		return v.LengthSquared() > 1f ? v.Normalized() : v;
	}
// -------------------------------------------------------------------------
	// Sprite / visuals (unchanged from the original)
	// -------------------------------------------------------------------------

	private void SetupSprite()
	{
		if (!string.IsNullOrEmpty(SpriteFramesPath) && ResourceLoader.Exists(SpriteFramesPath))
		{
			var frames = GD.Load<SpriteFrames>(SpriteFramesPath);
			if (frames != null)
			{
				sprite = MakeSprite(frames);
				return;
			}
		}

		int peerId = int.TryParse(Name.ToString(), out int namedPeerId)
			? namedPeerId
			: Multiplayer.HasMultiplayerPeer() ? (int)Multiplayer.GetUniqueId() : 1;
		int characterIndex = 0;
		Node networkHandler = GetNodeOrNull("/root/HighLevelNetworkHandler");
		if (networkHandler != null)
		{
			int selectedCharacter = networkHandler.Call("get_player_character", peerId).AsInt32();
			if (Characters.IsValid(selectedCharacter))
				characterIndex = selectedCharacter;
		}

		var built = new SpriteFrames();
		var idle = Characters.BuildIdleFrames(characterIndex, Characters.Suit.Standard, "float");
		var walk = Characters.BuildWalkFrames(characterIndex, Characters.Suit.Standard, "grab");
		if (idle != null)
			MergeAnimation(built, idle, "float");
		if (walk != null)
			MergeAnimation(built, walk, "grab");
		if (!built.HasAnimation("float"))
			return; // no suit art found -> keep the placeholder drawing.

		sprite = MakeSprite(built);
	}

	private static void MergeAnimation(SpriteFrames target, SpriteFrames source, string animationName)
	{
		if (!source.HasAnimation(animationName))
			return;

		target.AddAnimation(animationName);
		target.SetAnimationSpeed(animationName, source.GetAnimationSpeed(animationName));
		target.SetAnimationLoopMode(animationName, source.GetAnimationLoopMode(animationName));

		for (int i = 0; i < source.GetFrameCount(animationName); i++)
			target.AddFrame(animationName, source.GetFrameTexture(animationName, i));
	}

	private AnimatedSprite2D MakeSprite(SpriteFrames frames)
	{
		var s = new AnimatedSprite2D { Name = "Sprite", SpriteFrames = frames };
		visual.AddChild(s);
		if (frames.HasAnimation("float"))
			s.Play("float");
		return s;
	}

	private void UpdateSprite()
	{
		if (sprite == null) return;
		if (Mathf.Abs(Velocity.X) > 5f) facingRight = Velocity.X > 0f;
		sprite.FlipH = !facingRight;
		string anim = state == PlayerState.Attached ? "grab" : "float";
		if (!sprite.SpriteFrames.HasAnimation(anim)) anim = "float";
		if (sprite.SpriteFrames.HasAnimation(anim) && (string)sprite.Animation != anim)
			sprite.Play(anim);
	}

	public override void _Draw()
	{
		if (sprite != null) return;
		DrawSetTransform(Vector2.Zero, Mathf.DegToRad(spinDeg), Vector2.One);
		DrawCircle(Vector2.Zero, 14f, Colors.White);
		DrawRect(new Rect2(-10, 6, 20, 22), Colors.Gray);
		DrawLine(new Vector2(-10, 12), new Vector2(-20, 12), Colors.White, 2f);
		DrawLine(new Vector2(10, 12), new Vector2(20, 12), Colors.White, 2f);
		DrawLine(new Vector2(-8, 24), new Vector2(-8, 32), Colors.White, 2f);
		DrawLine(new Vector2(8, 24), new Vector2(8, 32), Colors.White, 2f);
		DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
	}

	private void SpawnThrusterPuff(Vector2 thrustDir, float dt)
	{
		puffTimer += dt;
		if (puffTimer < ThrusterPuffInterval) return;
		puffTimer = 0f;
		if (thrustDir.LengthSquared() < 0.01f) return;

		Vector2 thrust = thrustDir.Normalized();
		Vector2 local = -thrust * 16f;
		var puff = new ColorRect
		{
			Color = new Color(0.9f, 0.95f, 1f, 0.75f),
			Size = new Vector2(5f, 5f),
			Position = local - new Vector2(2.5f, 2.5f)
		};
		AddChild(puff);

		Vector2 endPos = (Vector2)puff.Position - thrust * 22f;
		Tween t = CreateTween();
		t.TweenProperty(puff, "position", endPos, 0.28f);
		t.Parallel().TweenProperty(puff, "modulate:a", 0f, 0.28f);
		t.Finished += puff.QueueFree;
	}
}