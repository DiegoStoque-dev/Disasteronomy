#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// Manager for the Lobby scene — the ship interior module (the area reached
/// after the third cutscene and before the EVA Phase 1).
///
/// Uses the structure that already exists in Scenes/Lobby/lobby.tscn instead of
/// rebuilding the room:
///   - StaticBody2D          the map (LobbyMap.png) with its wall/floor polygons,
///   - StaticBody2D/Spawn    Area2D defining the spawn zone of the crew,
///   - StaticBody2D/Terminal Area2D defining the interaction zone of the Terminal.
///
/// Responsibilities:
///   - Spawn Area: every player gets its own randomly-placed position inside the
///     central 60% of the Spawn zone, no overlapping, solo and multiplayer.
///   - Black background behind the map (no stars/exterior visible).
///   - Terminal: only the host can activate it (E while inside the Terminal
///     Area2D); a 5s countdown starts on every peer, then a smooth fade to black
///     and the whole group moves to Phase 1.
///   - Collisions: the lobby polygons keep working; players are on layer 1 with
///     mask 2 (world only) plus collision exceptions, so nobody can push anyone
///     else while the floor/walls and the interaction areas still work.
///   - HUD: bottom-left chat/action panel (system + dialogue + player chat),
///     the interaction prompt and the countdown/fade overlay (see ShipHud).
///
/// Multiplayer follows the project conventions: a MultiplayerSpawner with spawn
/// path "Players", host-spawned nodes named by peer id.
/// </summary>
public partial class ShipManager : Node2D
{
    /// <summary>Seconds between the activation and the fade/transition.</summary>
    [Export] public float TerminalCountdown = 5f;
    /// <summary>Scene opened after the fade (Phase 1 / EVA).</summary>
    [Export] public string PhaseScenePath = "res://Scenes/Phase1/Phase1.tscn";
    /// <summary>Player scene spawned inside the lobby.</summary>
    [Export] public string PlayerScenePath = "res://Scenes/Player.tscn";

    private const float FadeDuration = 1.2f;
    private const float MinSpawnSeparation = 44f;

    private StaticBody2D? world;      // map root: polygons + the two Area2D zones
    private Area2D? spawnZone;        // "StaticBody2D/Spawn"
    private Area2D? terminalZone;     // "StaticBody2D/Terminal"
    private Rect2 worldBounds;
    private Rect2 spawnBounds;

    private Node2D? playersRoot;
    private MultiplayerSpawner? spawner;
    private ShipHud? hud;
    private Player? localPlayer;

    private bool terminalBusy;
    private bool transitioning;
    private float countdownRemaining;
    private string localNickname = "";

    private readonly List<Vector2> takenSpawnPositions = new();

    private bool IsHost => Multiplayer.MultiplayerPeer == null || Multiplayer.IsServer();
    private long LocalPeerId => Multiplayer.MultiplayerPeer == null ? 1 : Multiplayer.GetUniqueId();

    public override void _Ready()
    {
        ProcessMode = Node.ProcessModeEnum.Always;
        RegisterInputs();
        ReadLevelLayout();
        BuildBlackBackdrop();
        SetupHud();
        SetupObjectives();
        ConfigureSpawner();
        SpawnPlayers();

        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;

        LogSystem("Você está a bordo da MPLM. Siga até o Terminal quando a equipe estiver pronta.");
        PlayIntroDialogue();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        if (localPlayer == null)
        {
            FindLocalPlayer();
            if (localPlayer != null)
                localPlayer.SetCameraLimits(worldBounds);
        }

        // Sync the chat focus with the player input gate: while the LineEdit has
        // focus, A/D/Space only type text and the character never moves.
        if (localPlayer != null)
            localPlayer.InputEnabled = hud == null || !hud.IsChatFocused;

        // While the chat is focused, movement/interaction keys belong to typing.
        if (hud != null && hud.IsChatFocused)
            return;

        if (transitioning)
            return;

        if (terminalBusy)
        {
            countdownRemaining -= dt;
            hud?.SetCountdown(Mathf.Max(0f, countdownRemaining));

            if (countdownRemaining <= 0f && localPlayer != null)
                StartFadeToPhase();
            return;
        }

        if (!IsHost || localPlayer == null)
            return;

        // Host-only interaction using the Terminal Area2D of the lobby.
        bool insideTerminal = terminalZone != null
            ? terminalZone.OverlapsBody(localPlayer)
            : worldBounds.GetCenter().DistanceTo(localPlayer.GlobalPosition) < 90f;

        hud?.ShowInteractPrompt(insideTerminal);

        if (insideTerminal && Input.IsActionJustPressed("interact"))
            ActivateTerminal();
    }

    // ------------------------------------------------------------------
    // Input setup (actions registered in code, like Phase1 does)
    // ------------------------------------------------------------------

    private void RegisterInputs()
    {
        // "interact" = E (used by the terminal). No project.godot edit required.
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
    // Reading the lobby layout (the scene already provides the structure)
    // ------------------------------------------------------------------

    private void ReadLevelLayout()
    {
        world = GetNodeOrNull<StaticBody2D>("StaticBody2D");
        if (world == null)
        {
            GD.PushError("ShipManager: lobby.tscn must contain a 'StaticBody2D' node.");
            worldBounds = new Rect2(-400, -100, 900, 500);
            return;
        }

        // Spawn zone (Area2D + CollisionShape2D) -> world-space rectangle.
        spawnZone = world.GetNodeOrNull<Area2D>("Spawn");
        CollisionShape2D? spawnShape = spawnZone?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
        if (spawnShape != null && spawnShape.Shape is RectangleShape2D spawnRect)
        {
            Vector2 center = spawnShape.GlobalPosition;
            spawnBounds = new Rect2(center - spawnRect.Size / 2f, spawnRect.Size);
        }
        else
        {
            spawnBounds = new Rect2(world.GlobalPosition + new Vector2(580, 90), new Vector2(160, 40));
        }

        // Terminal zone (Area2D + CollisionShape2D) for the activation check.
        terminalZone = world.GetNodeOrNull<Area2D>("Terminal");

        worldBounds = ComputeWorldBounds(world);
    }

    /// <summary>
    /// World-space rectangle that contains the lobby map, computed from the
    /// collision polygon vertices of the StaticBody2D so camera limits and the
    /// background always match the real map footprint.
    /// </summary>
    private Rect2 ComputeWorldBounds(StaticBody2D body)
    {
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);

        foreach (Node child in body.GetChildren())
        {
            if (child is not CollisionPolygon2D polygon)
                continue;

            foreach (Vector2 point in polygon.Polygon)
            {
                Vector2 global = polygon.GlobalPosition + point;
                min = new Vector2(Mathf.Min(min.X, global.X), Mathf.Min(min.Y, global.Y));
                max = new Vector2(Mathf.Max(max.X, global.X), Mathf.Max(max.Y, global.Y));
            }
        }

        if (!float.IsFinite(min.X))
            return new Rect2(body.GlobalPosition, new Vector2(816, 224));

        return new Rect2(min, max - min);
    }

    /// <summary>
    /// Uniform black backdrop behind the map: the interior reads without any
    /// external scenery (stars/planets) visible around the ship.
    /// </summary>
    private void BuildBlackBackdrop()
    {
        var backgroundLayer = new CanvasLayer { Name = "Background", Layer = -100 };
        AddChild(backgroundLayer);

        var backdrop = new ColorRect { Color = Colors.Black };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        backgroundLayer.AddChild(backdrop);
    }

    // ------------------------------------------------------------------
    // Spawner / HUD
    // ------------------------------------------------------------------

    /// <summary>
    /// MultiplayerSpawner following the Phase1 pattern: spawn path "Players",
    /// Player.tscn registered as spawnable. The host adds a node per peer under
    /// the path and the spawner replicates it to every client.
    /// </summary>
    private void ConfigureSpawner()
    {
        playersRoot = new Node2D { Name = "Players", ZIndex = 1 };
        AddChild(playersRoot);

        spawner = new MultiplayerSpawner { Name = "MultiplayerSpawner" };
        AddChild(spawner);
        spawner.SpawnPath = new NodePath("Players");
        spawner.AddSpawnableScene(PlayerScenePath);
        spawner.Spawned += OnPlayerSpawned;
    }

    private void SetupHud()
    {
        hud = new ShipHud { Name = "ShipHud" };
        hud.SendChatRequested += SendChat;
        AddChild(hud);
    }

    /// <summary>
    /// Makes the TAB (objectives) overlay work in the Lobby: it shows the current
    /// goal (Global.CurrentObjective) and keeps working until the objective is
    /// completed. The goal is cleared when the host activates the Terminal.
    /// </summary>
    private void SetupObjectives()
    {
        var objectiveHud = new ObjectiveHud { Name = "LobbyObjectiveHud" };
        AddChild(objectiveHud);
        objectiveHud.Activate();

        GetNodeOrNull<Global>("/root/Global")?.SetObjective(
            "Dirija-se ao Terminal e ative-o quando a equipe estiver pronta (apenas o host ativa).");
    }

    // ------------------------------------------------------------------
    // Spawn logic (solo + multiplayer, random central, no overlap)
    // ------------------------------------------------------------------

    private void SpawnPlayers()
    {
        takenSpawnPositions.Clear();

        if (Multiplayer.HasMultiplayerPeer())
        {
            if (IsHost)
            {
                SpawnPlayerForPeer(LocalPeerId);
                foreach (int peerId in Multiplayer.GetPeers())
                    if (peerId != Multiplayer.GetUniqueId())
                        SpawnPlayerForPeer(peerId);
            }
            return;
        }

        // Solo: one player, randomly placed in the central spawn zone.
        SpawnPlayerForPeer(1);
    }

    private void SpawnPlayerForPeer(long peerId)
    {
        PackedScene? scene = GD.Load<PackedScene>(PlayerScenePath);
        if (scene == null)
        {
            GD.PushError("ShipManager: player scene not found: " + PlayerScenePath);
            return;
        }

        var player = scene.Instantiate<Player>();
        player.Name = peerId.ToString();
        player.GlobalPosition = PickSpawnPosition();
        playersRoot?.AddChild(player);

        if (peerId == LocalPeerId)
            localPlayer = player;

        GD.Print($"[Lobby] Spawned player {peerId} at {player.GlobalPosition}");
    }

    /// <summary>
    /// Random position in the central 60% of the Spawn zone, retried so spawned
    /// characters never overlap and never land outside the map.
    /// </summary>
    private Vector2 PickSpawnPosition()
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();

        Vector2 fallback = spawnBounds.GetCenter();

        for (int attempt = 0; attempt < 10; attempt++)
        {
            float x = spawnBounds.Position.X + spawnBounds.Size.X * rng.RandfRange(0.3f, 0.7f);
            float y = spawnBounds.Position.Y + spawnBounds.Size.Y * rng.RandfRange(0.3f, 0.7f);
            Vector2 candidate = new(x, y);

            if (attempt == 0)
                fallback = candidate;

            bool clear = true;
            foreach (Vector2 taken in takenSpawnPositions)
            {
                if (candidate.DistanceTo(taken) < MinSpawnSeparation)
                {
                    clear = false;
                    break;
                }
            }

            if (clear && worldBounds.HasPoint(candidate))
            {
                takenSpawnPositions.Add(candidate);
                return candidate;
            }
        }

        takenSpawnPositions.Add(fallback);
        return fallback;
    }

    private void OnPlayerSpawned(Node node)
    {
        // Replicated spawn: keep everyone inside the map.
        if (node is not Player player)
            return;

        if (!worldBounds.HasPoint(player.GlobalPosition))
            player.GlobalPosition = spawnBounds.GetCenter();
    }

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

        string nickname = GetNickname(peerId);
        RpcSystem($"{nickname} entrou na partida. Jogadores totais: {Multiplayer.GetPeers().Length + 1}.");
        RpcChat(GetNickname(peerId), "Olá, equipe! Cheguei a bordo.");
    }

    private void OnPeerDisconnected(long peerId)
    {
        if (!IsHost)
            return;

        string nickname = GetNickname(peerId);
        if (!string.IsNullOrWhiteSpace(nickname))
            RpcSystem($"{nickname} saiu da partida.");

        Node? node = playersRoot?.GetNodeOrNull<Player>(peerId.ToString());
        if (node != null)
        {
            playersRoot?.RemoveChild(node);
            node.QueueFree();
        }
    }

    // ------------------------------------------------------------------
    // Terminal activation (host only) -> synchronized countdown -> fade
    // ------------------------------------------------------------------

    private void ActivateTerminal()
    {
        if (!IsHost || terminalBusy || transitioning)
            return;

        // Guards against multiple players activating it at the same time.
        terminalBusy = true;
        countdownRemaining = TerminalCountdown;

        string hostName = GetNickname(LocalPeerId);
        RpcSystem($"{hostName} ativou o Terminal.");
        RpcDialogue("Comando", "Transição autorizada. Todos para o módulo de utilidades em 5 segundos.", -1);

        // The objective is completed once the Terminal is activated, so TAB stops
        // showing a pending goal from here on.
        GetNodeOrNull<Global>("/root/Global")?.ClearObjective();

        // Single RPC starts the countdown on every peer at the same moment.
        Rpc(nameof(RpcNotifyTerminalActivated));
        hud?.ShowInteractPrompt(false);
        hud?.SetCountdown(TerminalCountdown);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
    private void RpcNotifyTerminalActivated()
    {
        if (transitioning)
            return;

        terminalBusy = true;
        countdownRemaining = TerminalCountdown;
        hud?.ShowInteractPrompt(false);
    }

    private void StartFadeToPhase()
    {
        if (transitioning)
            return;

        transitioning = true;
        terminalBusy = false;
        hud?.ShowCountdown(false);
        hud?.ShowInteractPrompt(false);
        hud?.FadeToBlack(FadeDuration, FadeToPhaseTransition);
    }

    private void FadeToPhaseTransition()
    {
        if (SceneTransitions.Instance != null)
            SceneTransitions.Instance.ChangeScene(PhaseScenePath);
        else
            GetTree().ChangeSceneToFile(PhaseScenePath);
    }

    // ------------------------------------------------------------------
    // HUD log helpers (system / dialogues / player chat)
    // ------------------------------------------------------------------

    private void LogSystem(string message) => hud?.LogSystem(message);

    private void RpcSystem(string message) =>
        Rpc(nameof(RpcNotifySystem), message);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
    private void RpcNotifySystem(string message) => hud?.LogSystem(message);

    private void RpcChat(string sender, string message) =>
        Rpc(nameof(RpcNotifyChat), sender, message);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
    private void RpcNotifyChat(string sender, string message) =>
        hud?.LogChat(sender, message);

    private void RpcDialogue(string speaker, string text, int colorIndex) =>
        Rpc(nameof(RpcNotifyDialogue), speaker, text, colorIndex);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
    private void RpcNotifyDialogue(string speaker, string text, int colorIndex)
    {
        Color color = colorIndex < 0
            ? new Color(0.66f, 0.68f, 0.78f) // mission-control voice
            : Characters.GetColor(colorIndex);
        hud?.LogDialogue(speaker, text, color);
    }

    /// <summary>Player submitted a chat line (Enter or the send button).</summary>
    private void SendChat(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (string.IsNullOrEmpty(localNickname))
            localNickname = GetNickname(LocalPeerId);

        // Reliable broadcast, sender included via CallLocal.
        RpcChat(localNickname, text.Trim());
    }

    /// <summary>
    /// Reads the nickname from the network handler. Solo falls back to the
    /// character name so every action/dialogue is still identifiable.
    /// </summary>
    private string GetNickname(long peerId)
    {
        Node? handler = GetNodeOrNull("/root/HighLevelNetworkHandler");
        if (handler != null)
        {
            Variant result = handler.Call("get_player_nickname", (int)peerId);
            string nickname = result.AsString();
            if (!string.IsNullOrWhiteSpace(nickname))
                return nickname;
        }

        if (Multiplayer.MultiplayerPeer == null)
            return Characters.GetName(GetLocalCharacterIndex());

        return $"Player {peerId}";
    }

    private int GetLocalCharacterIndex()
    {
        Node? handler = GetNodeOrNull("/root/HighLevelNetworkHandler");
        if (handler != null)
        {
            Variant result = handler.Call("get_player_character", (int)LocalPeerId);
            int index = result.AsInt32();
            if (Characters.IsValid(index))
                return index;
        }
        return 0;
    }

    /// <summary>
    /// A couple of scripted crew dialogues, shown in the chat panel with the
    /// speaker color so they are visually distinct from player/system lines.
    /// </summary>
    private void PlayIntroDialogue()
    {
        if (!IsHost)
            return;

        int localChar = GetLocalCharacterIndex();
        string speaker = Characters.GetName(localChar);

        RpcDialogue("Comando", "Todos a postos. O Terminal libera o acesso ao módulo de utilidades.", -1);
        RpcDialogue(speaker, "Entendido. Equipe, vou até o Terminal quando todos estiverem prontos.", localChar);
    }
}
