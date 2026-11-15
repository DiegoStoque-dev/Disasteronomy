using Godot;
using System;

public partial class EvaPlayer : CharacterBody2D
{
	public enum PlayerState { Floating, Grabbed }
	public enum RopeState { Rope, BrokenArm }
	public enum TetherStress { Green, Taut, Red }
	public RopeState ropeState = RopeState.Rope;
	public bool IsTetherBroken => ropeState == RopeState.BrokenArm;
	public bool IsRuptured => isRuptured;

	[Export] public float MaxDriftSpeed = 65f;       // velocidade cruzeiro do RCS (média, sem deriva extrema)
	[Export] public float SuitAcceleration = 150f; // resposta imediata: atinge o teto em ~0.4s
	[Export] public float DriftDrag = 0.6f;      // light linear drag per second when no RCS input
	[Export] public int MaxFatigueStrikes = 3;
	[Export] public float CriticalSnapLoadSpeed = 95f; // outward radial speed (px/s) at the taut transition that counts as a critical stress hit
	[Export] public float RedTensionDuration = 0.35f;
	[Export] public float ArmReach = 90f; // renamed from ArmMaxLength: Phase1.tscn's 160 override is ignored
	[Export] public float GrabPullSpeed = 180f;
	[Export] public float GrabPullBrakeDist = 60f; // rail-to-rail pull eases out over this distance
	[Export] public float RailSlideSpeed = 90f;
	[Export] public int SaferChargesDefault = 3;
	[Export] public float SaferImpulse = 140f;
	[Export] public float SaferCooldown = 1.0f;
	[Export] public float TetherMaxLength = 300f;
	[Export] public float TetherDisableThrustMargin = 20f;
	[Export] public float TetherReelSpeed = 25f;
	[Export] public float ReelClimbSpeed = 180f; // hand-over-hand climb speed (right mouse, SAFER empty)
	[Export] public float RopeShortenRate = 60f;    // effective rope shortens this fast while hauling
	[Export] public float RopeExtendRate = 25f;     // …and relaxes back to max when you stop hauling
	[Export] public float MinRopeReelRatio = 0.4f;  // never shorter than 40% of TetherMaxLength
	[Export] public float ArrivalBrakeRadius = 40f; // start easing the climb pull this close to the anchor
	[Export] public float ArrivalDamping = 4f;      // exponential velocity fade near the anchor (stops slingshots)
	[Export] public Rect2 LevelBounds = new Rect2(-600, -450, 2200, 1300);
	[Export] public float OutOfBoundsTimeLimit = 3f;
	[Export] public float GrabRadius = 30f;
	[Export] public bool DebugKeys = true; // F9 = teleport near the CPS
	[Export] public float SpinDamping = 1.2f;
	[Export] public float ReleaseRandomSpin = 90f;
	[Export] public float SaferRandomSpin = 120f;
	[Export] public float GrabSpinDamping = 8f;
	[Export] public float ThrusterPuffInterval = 0.08f;
	/// <summary>If this SpriteFrames resource exists it replaces the placeholder drawing. Animations: float, grab (sprites face RIGHT).</summary>
	[Export] public string SpriteFramesPath = ""; // empty = build the space-suit animations in code

	/// <summary>Built-in space-suit sheets used to build the player's animations when no SpriteFrames resource is set.</summary>
	private static readonly string SuitIdlePath = "res://Sprites/Traje_Protecao/Idle/traje_protecao_idle_right.png";
	private static readonly string SuitGrabMovePath = "res://Sprites/Traje_Protecao/Walk/traje_protecao_walk_left.png";
	private const int SuitFrameWidth = 32;

	private Line2D tetherLine;
	private AnimatedSprite2D sprite;
	private bool facingRight = true;
	private Line2D armLine;
	private MultiplayerSynchronizer sync;
	private Label nicknameLabel;
	public Camera2D playerCamera;
	private Phase1Manager manager;

	public PlayerState state = PlayerState.Floating;
	private Handrail currentRail;
	private Vector2 armTip = Vector2.Zero;
	private Vector2 armDirection = Vector2.Down;

	// Grab / pull
	private bool pulling = false;
	private Vector2 pullTarget = Vector2.Zero;
	private Vector2 lastSlideVelocity = Vector2.Zero;

	// Tether: anchored at the airlock at first, then at the last handrail point the player held.
	private bool tetherInitialized = false;
	private Vector2 tetherAnchorPos = Vector2.Zero;
	private float tetherDistance = 0f;
	private float currentRopeLength = 0f; // effective working rope length; shrinks while hauling in
	private bool isHauling = false;
	private int stressCount = 0;      // permanent material fatigue: does NOT reset when the rope returns to slack
	private bool wasBeyondMax = false;
	private bool hasTriggeredThisCycle = false;
	private TetherStress tetherStress = TetherStress.Green;
	private bool isRuptured = false;
	private float redTimer = 0f;

	private int saferCharges;
	private float saferCooldownTimer = 0f;
	private float outOfBoundsTimer = 0f;
	private float puffTimer = 0f;
	private Node2D visual;
	private float spinDeg = 0f;
	private float spinVelocity = 0f; // degrees per second
	private RandomNumberGenerator rng = new();

	private bool eWasDown = false;
	private bool f9WasDown = false;

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
		visual = new Node2D { Name = "Visual" }; // tumbles; camera/tether/arm stay un-rotated
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
		currentRopeLength = TetherMaxLength;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Multiplayer.HasMultiplayerPeer() && !IsMultiplayerAuthority()) return;

		float dt = (float)delta;
		var mgr = Manager;

		// Edge detection for E and F9 must run every frame, even while input is locked.
		bool eDown = Input.IsPhysicalKeyPressed(Key.E);
		bool ePressed = eDown && !eWasDown;
		eWasDown = eDown;

		bool f9Down = Input.IsPhysicalKeyPressed(Key.F9);
		bool f9Pressed = f9Down && !f9WasDown;
		f9WasDown = f9Down;

		saferCooldownTimer = Mathf.Max(0f, saferCooldownTimer - dt);

		bool locked = mgr != null && mgr.InputLocked;

		if (!locked)
		{
			if (DebugKeys && f9Pressed && mgr != null)
				DebugTeleportNearCps(mgr);

			if (state == PlayerState.Floating)
				UpdateFloating(dt);
			else
				UpdateGrabbed(dt);

			if (ePressed && mgr != null && mgr.IsPlayerNearCps(GlobalPosition) && Velocity.Length() < mgr.CpsInteractSpeedMax)
				mgr.RequestMinigame();
		}
		else
		{
			armLine.Visible = false;
			isHauling = false; // no climbing while the minigame is open / phase is over
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

	private void SetupSprite()
	{
		// Prefer an explicit SpriteFrames resource when a team member provides one.
		if (!string.IsNullOrEmpty(SpriteFramesPath) && ResourceLoader.Exists(SpriteFramesPath))
		{
			var frames = GD.Load<SpriteFrames>(SpriteFramesPath);
			if (frames != null)
			{
				sprite = MakeSprite(frames);
				return;
			}
		}

		// Otherwise build a SpriteFrames from the built-in space-suit sheets so Phase 1
		// shows the astronaut instead of the placeholder drawing.
		var built = new SpriteFrames();
		AddSlicedAnimation(built, "float", SuitIdlePath);
		AddSlicedAnimation(built, "grab", SuitGrabMovePath);
		if (!built.HasAnimation("float"))
			return; // no suit art found -> keep the placeholder drawing

		sprite = MakeSprite(built);
	}

	private AnimatedSprite2D MakeSprite(SpriteFrames frames)
	{
		var s = new AnimatedSprite2D { Name = "Sprite", SpriteFrames = frames };
		visual.AddChild(s);
		if (frames.HasAnimation("float"))
			s.Play("float");
		return s;
	}

	private void AddSlicedAnimation(SpriteFrames frames, string anim, string texturePath)
	{
		if (string.IsNullOrEmpty(texturePath) || !ResourceLoader.Exists(texturePath)) return;
		var tex = GD.Load<Texture2D>(texturePath);
		if (tex == null) return;

		int cols = tex.GetWidth() / SuitFrameWidth; // suit sheets are 32px frames
		int frameHeight = tex.GetHeight();
		if (cols <= 0) return;

		frames.AddAnimation(anim);
		frames.SetAnimationLoopMode(anim, SpriteFrames.LoopMode.Linear);
		frames.SetAnimationSpeed(anim, 6f);
		for (int i = 0; i < cols; i++)
		{
			var atlas = new AtlasTexture
			{
				Atlas = tex,
				Region = new Rect2(i * SuitFrameWidth, 0, SuitFrameWidth, frameHeight)
			};
			frames.AddFrame(anim, atlas);
		}
	}

	private void UpdateSprite()
	{
		if (sprite == null) return;
		if (Mathf.Abs(Velocity.X) > 5f) facingRight = Velocity.X > 0f;
		sprite.FlipH = !facingRight;
		string anim = state == PlayerState.Grabbed ? "grab" : "float";
		if (!sprite.SpriteFrames.HasAnimation(anim)) anim = "float";
		if (sprite.SpriteFrames.HasAnimation(anim) && (string)sprite.Animation != anim)
			sprite.Play(anim);
	}

	public override void _Draw()
	{
		if (sprite != null) return; // a real sprite is in use
		DrawSetTransform(Vector2.Zero, Mathf.DegToRad(spinDeg), Vector2.One); // tumbling placeholder
		DrawCircle(Vector2.Zero, 14f, Colors.White);
		DrawRect(new Rect2(-10, 6, 20, 22), Colors.Gray);
		DrawLine(new Vector2(-10, 12), new Vector2(-20, 12), Colors.White, 2f);
		DrawLine(new Vector2(10, 12), new Vector2(20, 12), Colors.White, 2f);
		DrawLine(new Vector2(-8, 24), new Vector2(-8, 32), Colors.White, 2f);
		DrawLine(new Vector2(8, 24), new Vector2(8, 32), Colors.White, 2f);
		DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
	}

	// Reads WASD + arrow keys by PHYSICAL key, so it does not depend on the InputMap
	// (ui_left / ui_right had no A / D bound, which is why A and D did nothing).
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

	private static bool GrabHeld() => Input.IsMouseButtonPressed(MouseButton.Left);

	private void UpdateFloating(float dt)
	{
		bool tetherTaut = IsTetherTaut;

		// Post-rupture lockdown: the suit RCS (WASD) is cut off too; only SAFER jets and
		// inherited momentum remain, so the severed astronaut can never "walk on air".
		Vector2 inputDir = isRuptured ? Vector2.Zero : ReadMoveInput();
		if (tetherTaut)
			inputDir = Vector2.Zero; // rope is taut: thrusters offline (arm/grab and SAFER still work)

		if (inputDir != Vector2.Zero)
		{
			// Crisp RCS: drive each axis directly toward the input-commanded speed so the
			// response is immediate (no exponential ramp) and it strictly caps at MaxDriftSpeed.
			// Holding a direction moves the axis, never building past the cap.
			// Note: Velocity components are read-only (property returns a copy), so assign a whole Vector2.
			float accelStep = SuitAcceleration * dt;
			float vx = Mathf.MoveToward(Velocity.X, inputDir.X * MaxDriftSpeed, accelStep);
			float vy = Mathf.MoveToward(Velocity.Y, inputDir.Y * MaxDriftSpeed, accelStep);
			Velocity = new Vector2(vx, vy);
			SpawnThrusterPuff(inputDir, dt);
		}
		else
		{
			// Light linear drag when coasting: bleeds off speed naturally instead of
			// drifting forever; SAFER overspeed still decays via the same drag.
			Velocity *= Mathf.Max(0f, 1f - DriftDrag * dt);
		}

		// One anchor vector shared by the winch, the climb and the arrival braking (no redeclaration).
		Vector2 toAnchor = tetherAnchorPos - GlobalPosition;

		// Slow winch: while the rope is taut the reel pulls the player back toward the anchor.
		if (tetherTaut)
		{
			float d = toAnchor.Length();
			if (d > 0.1f)
				GlobalPosition += toAnchor / d * TetherReelSpeed * dt;
		}

		// Manual lifeline climbing: with the SAFER depleted (rope still intact) you can haul
		// yourself hand-over-hand toward the active anchor by holding the right mouse button.
		bool haul = !isRuptured && saferCharges <= 0 && tetherInitialized && Input.IsMouseButtonPressed(MouseButton.Right);
		isHauling = haul;
		float anchorDist = toAnchor.Length();
		if (haul)
		{
			if (anchorDist > 1f)
			{
				Vector2 reelDir = toAnchor / anchorDist;
				// Cancel any outward radial drift, then drive inward at the climb speed.
				float radialOut = Mathf.Max(0f, Velocity.Dot(reelDir));
				Velocity -= reelDir * radialOut;
				// Arrival braking: ease the pull down as the anchor gets close so the player
				// settles instead of overshooting, and releases almost stopped.
				float ease = Mathf.Clamp(anchorDist / ArrivalBrakeRadius, 0f, 1f);
				float inward = Mathf.Min(ReelClimbSpeed * ease, anchorDist / Mathf.Max(dt, 0.0001f));
				Velocity += reelDir * inward;
			}
			// Dynamically shorten the effective rope: the taut clamp then drags the player
			// closer to the anchor while they climb (never below MinRopeReelRatio of max).
			currentRopeLength = Mathf.MoveToward(currentRopeLength, TetherMaxLength * MinRopeReelRatio, RopeShortenRate * dt);
		}

		// Arrival/release braking: very close to the anchor we fade leftover linear AND
		// rotational speed so letting go near the handrail never slingshots the player away.
		if (!isRuptured && anchorDist < ArrivalBrakeRadius)
		{
			Velocity *= Mathf.Exp(-ArrivalDamping * dt);
			spinVelocity = Mathf.MoveToward(spinVelocity, 0f, ArrivalDamping * dt);
		}

		if (Input.IsActionJustPressed("pular") && saferCharges > 0 && saferCooldownTimer <= 0f)
		{
			Vector2 toCursor = GetGlobalMousePosition() - GlobalPosition;
			if (toCursor.Length() > 0.01f) toCursor = toCursor.Normalized();
			Velocity -= toCursor * SaferImpulse; // jet toward the cursor, push the opposite way
			saferCharges--;
			saferCooldownTimer = SaferCooldown;
			spinVelocity += (float)(SaferRandomSpin * (rng.Randf() * 2.0 - 1.0)); // random tumble from the jet
			Sfx.Play(this, "safer_burst");
		}

		// Light damping of the cosmetic tumble while drifting.
		spinVelocity *= Mathf.Exp(-SpinDamping * dt);

		if (GrabHeld() && !isRuptured)
		{
			UpdateArm();
			CheckGrab(null);
			armLine.Points = new Vector2[] { Vector2.Zero, armTip - GlobalPosition };
			armLine.Visible = state == PlayerState.Floating;
		}
		else
		{
			armTip = GlobalPosition;
			armLine.Visible = false;
		}
	}

	private void UpdateGrabbed(float dt)
	{
		// Absolute lockdown: no rail sliding / arm interaction after a permanent rupture.
		if (isRuptured)
		{
			ReleaseRail();
			return;
		}
		isHauling = false; // hand-over-hand climbing only applies while floating
		if (currentRail == null || !IsInstanceValid(currentRail))
		{
			ReleaseRail();
			return;
		}

		if (!GrabHeld())
		{
			ReleaseRail();
			return;
		}

		Velocity = Vector2.Zero;
		spinVelocity = Mathf.MoveToward(spinVelocity, 0f, GrabSpinDamping * dt); // the grip damps the tumble

		if (pulling)
		{
			Vector2 to = pullTarget - GlobalPosition;
			float distTo = to.Length();
			// Ease-out approach: pull fast when far from the new rail, decelerate as the
			// player nears it so corner transfers feel weighty and controlled, never a yank.
			float ease = Mathf.Clamp(distTo / GrabPullBrakeDist, 0f, 1f);
			float step = GrabPullSpeed * dt * Mathf.Lerp(0.3f, 1f, ease);
			if (distTo <= step)
			{
				GlobalPosition = pullTarget;
				pulling = false;
			}
			else
			{
				GlobalPosition += to / distTo * step;
			}
			armLine.Points = new Vector2[] { Vector2.Zero, pullTarget - GlobalPosition };
			armLine.Visible = true;
			lastSlideVelocity = Vector2.Zero;
			return;
		}

		armLine.Visible = false;

		string axis = currentRail.GetAxis();
		Vector2 inputDir = ReadMoveInput();
		Vector2 slideDir = axis == "horizontal"
			? new Vector2(inputDir.X, 0f)
			: new Vector2(0f, inputDir.Y);

		lastSlideVelocity = Vector2.Zero;
		if (slideDir != Vector2.Zero)
		{
			slideDir = slideDir.Normalized();
			lastSlideVelocity = slideDir * RailSlideSpeed;
			GlobalPosition += lastSlideVelocity * dt;
		}

		// Stay glued to the rail (also stops sliding past the ends).
		GlobalPosition = currentRail.GetClosestGrabPoint(GlobalPosition);

		// The tether clips to the rail you are holding (never after rupture).
		if (!isRuptured)
			tetherAnchorPos = GlobalPosition;

		// While holding the button, you may reach for ANOTHER rail (e.g. at a corner).
		UpdateArm();
		CheckGrab(currentRail);
	}

	private void ReleaseRail()
	{
		if (state == PlayerState.Grabbed)
		{
			Velocity = lastSlideVelocity; // keep a little inertia when letting go
			spinVelocity += (float)(ReleaseRandomSpin * (rng.Randf() * 2.0 - 1.0)); // small random tumble when you let go
		}
		state = PlayerState.Floating;
		currentRail = null;
		pulling = false;
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

	private void CheckGrab(Handrail ignore)
	{
		if (isRuptured) return; // permanently severed: no re-anchoring, no tether pulling
		foreach (Node node in GetTree().GetNodesInGroup("handrail"))
		{
			if (!(node is Handrail rail) || rail == ignore) continue;
			Vector2 closest = rail.GetClosestGrabPoint(armTip);
			if (armTip.DistanceTo(closest) <= GrabRadius)
			{
				state = PlayerState.Grabbed;
				currentRail = rail;
				pullTarget = rail.GetClosestGrabPoint(GlobalPosition);
				pulling = true;
				Velocity = Vector2.Zero;
				Sfx.Play(this, "rail_grab");
				tetherAnchorPos = pullTarget; // dual-lanyard clip: the cable moves to the new anchor

				// Re-anchoring beyond the effective rope range is an instant taut shock (no travel velocity needed).
				if (tetherAnchorPos.DistanceTo(GlobalPosition) > currentRopeLength)
					ApplyCriticalStress();
				return;
			}
		}
	}

	private void UpdateTether(float dt)
	{
		// Ruptured: permanently severed. No constraint, no reel, no tension checks.
		if (ropeState == RopeState.BrokenArm)
		{
			if (tetherLine != null)
				tetherLine.Visible = false;
			tetherDistance = 0f;
			tetherStress = TetherStress.Green;
			return;
		}

		if (!tetherInitialized)
		{
			var anchors = GetTree().GetNodesInGroup("tether_anchor");
			if (anchors.Count > 0 && anchors[0] is Node2D a)
			{
				tetherAnchorPos = a.GlobalPosition;
				tetherInitialized = true;
			}
			else
			{
				return;
			}
		}

		Vector2 toAnchor = tetherAnchorPos - GlobalPosition;
		float dist = toAnchor.Length();
		tetherDistance = dist;

		// The effective rope length relaxes back toward max when the player is not hauling in.
		if (!isHauling)
			currentRopeLength = Mathf.MoveToward(currentRopeLength, TetherMaxLength, RopeExtendRate * dt);

		// The rope bends around hull corners instead of clipping straight through solid structure.
		tetherLine.Points = ToLocalPath(ComputeTetherPath(GlobalPosition, tetherAnchorPos));

		// Stress colour: Green (safe) / Yellow (taut) / Red (critical).
		redTimer = Mathf.Max(0f, redTimer - dt);
		if (redTimer > 0f)
		{
			tetherLine.DefaultColor = Colors.Red;
		}
		else
		{
			float ratio = currentRopeLength > 0f ? dist / currentRopeLength : 0f;
			tetherLine.DefaultColor = ratio >= 0.97f ? Colors.Yellow : new Color(0.4f, 0.9f, 0.5f);
		}

		if (dist > currentRopeLength)
		{
			Vector2 toAnchorDir = toAnchor / dist;

			// Dynamic snap-load: measure the outward radial velocity on the exact slack->taut
			// transition frame. A single taut event registers only once (hasTriggeredThisCycle)
			// until the rope becomes slack again.
			if (!wasBeyondMax)
			{
				wasBeyondMax = true;
				hasTriggeredThisCycle = false;
			}

			float radialOut = Velocity.Dot(toAnchorDir);
			if (!hasTriggeredThisCycle && radialOut >= CriticalSnapLoadSpeed)
			{
				hasTriggeredThisCycle = true;
				ApplyCriticalStress();
				if (isRuptured) return;
			}

			// Rigid distance constraint: clamp to the exact effective rope radius and keep only the
			// tangential component of the velocity (momentum-conserving swing around the anchor).
			if (radialOut > 0f)
				Velocity -= toAnchorDir * radialOut;
			GlobalPosition = tetherAnchorPos - toAnchorDir * currentRopeLength;
			tetherDistance = currentRopeLength;

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

	/// <summary>
	/// Registers one critical (yield-strength) stress event. The fatigue counter is PERMANENT:
	/// it never resets when the rope returns to slack. On the Nth strike the rope ruptures for good.
	/// </summary>
	private void ApplyCriticalStress()
	{
		if (isRuptured) return;
		stressCount++;
		tetherStress = TetherStress.Red;
		redTimer = RedTensionDuration;
		Sfx.Play(this, "rope_stress");
		GD.Print($"[Phase1] Tether critical stress {stressCount}/{MaxFatigueStrikes}");
		if (stressCount >= MaxFatigueStrikes)
			SnapRope();
	}

	/// <summary>
	/// Permanent rupture: severs the rope and disables all tether mechanics.
	/// The astronaut is now free-floating and relies solely on the SAFER jetpack.
	/// </summary>
	private void SnapRope()
	{
		if (isRuptured) return;
		isRuptured = true;
		ropeState = RopeState.BrokenArm;
		if (state == PlayerState.Grabbed)
			ReleaseRail();
		if (tetherLine != null)
			tetherLine.Visible = false;
		tetherStress = TetherStress.Green;
		hasTriggeredThisCycle = false;
		Sfx.Play(this, "rope_break");
		GD.Print("[Phase1] Tether ruptured - SAFER-only drift from now on.");
		// TODO: add Music/sfx/rope_break.ogg for a rupture cue.
	}

	// --- Simple hull corner wrapping for the tether line ---

	private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

	private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
	{
		float d1 = Cross(c - a, b - a);
		float d2 = Cross(d - a, b - a);
		float d3 = Cross(a - c, d - c);
		float d4 = Cross(b - c, d - c);
		return (d1 > 0f && d2 < 0f || d1 < 0f && d2 > 0f) && (d3 > 0f && d4 < 0f || d3 < 0f && d4 > 0f);
	}

	private bool SegmentClear(Vector2 a, Vector2 b)
	{
		foreach (var seg in Phase1Manager.TetherHullSegments)
			if (SegmentsIntersect(a, b, seg.A, seg.B)) return false;
		return true;
	}

	/// <summary>
	/// Returns the tether path in world coordinates. When the straight line between the player
	/// and the anchor would cross a hull segment, the rope bends around the nearest clear corner
	/// (single-vertex wrap), so the tether never clips straight through solid structure.
	/// </summary>
	private Vector2[] ComputeTetherPath(Vector2 from, Vector2 to)
	{
		var hulls = Phase1Manager.TetherHullSegments;
		if (hulls == null || hulls.Count == 0)
			return new Vector2[] { from, to };

		Vector2 corner = Vector2.Zero;
		float bestLen = float.MaxValue;
		bool wrapped = false;

		foreach (var seg in hulls)
		{
			if (!SegmentsIntersect(from, to, seg.A, seg.B)) continue;
			foreach (Vector2 c in new Vector2[] { seg.A, seg.B })
			{
				if (c.DistanceTo(from) < 24f || c.DistanceTo(to) < 24f) continue;
				float len = from.DistanceTo(c) + c.DistanceTo(to);
				if (len >= bestLen) continue;
				if (!SegmentClear(from, c) || !SegmentClear(c, to)) continue;
				corner = c;
				bestLen = len;
				wrapped = true;
			}
		}

		if (!wrapped)
			return new Vector2[] { from, to };
		return new Vector2[] { from, corner, to };
	}

	private Vector2[] ToLocalPath(Vector2[] worldPath)
	{
		var local = new Vector2[worldPath.Length];
		for (int i = 0; i < worldPath.Length; i++)
			local[i] = ToLocal(worldPath[i]);
		return local;
	}

	private void CheckBounds(float dt)
	{
		if (!LevelBounds.HasPoint(GlobalPosition))
		{
			outOfBoundsTimer += dt;
			// Leaving the map bounds restarts the phase (fresh rope, stress and fatigue reset with the scene).
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
		ReleaseRail();
		GlobalPosition = mgr.CpsPosition + new Vector2(-220f, 0f);
		Velocity = Vector2.Zero;
		tetherAnchorPos = GlobalPosition;
		tetherInitialized = true;
		spinVelocity = 0f;
		GD.Print("[DEBUG] F9: teleported near the CPS");
	}

	public int GetSaferCharges() => saferCharges;

	public int GetFatigueStrikes() => stressCount;

	public RopeState GetRopeState() => ropeState;

	public TetherStress GetTetherStress() => tetherStress;

	public float GetTetherTension() => currentRopeLength > 0f ? tetherDistance / currentRopeLength : 0f;

	public bool IsTetherCritical => redTimer > 0f;

	public bool IsTetherTaut => !IsTetherBroken && currentRopeLength > 0f && tetherDistance >= currentRopeLength - TetherDisableThrustMargin;

	private void SpawnThrusterPuff(Vector2 thrustDir, float dt)
	{
		puffTimer += dt;
		if (puffTimer < ThrusterPuffInterval) return;
		puffTimer = 0f;
		if (thrustDir.LengthSquared() < 0.01f) return;

		Vector2 thrust = thrustDir.Normalized();
		Vector2 local = -thrust * 16f; // behind the suit, opposite to the thrust
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