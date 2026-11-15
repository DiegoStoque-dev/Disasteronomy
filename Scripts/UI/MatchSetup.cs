#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// Match setup / lobby screen (reached from Start -> New Game).
/// A transition plays first: the ship crosses the screen from right to left and
/// leaves. Only then the player panel appears on the left, with the room code,
/// the connected players (color, nickname, ready state, remove) and the
/// character idle preview on the right.
/// </summary>
public partial class MatchSetup : Control
{
    private const string HandlerPath = "/root/HighLevelNetworkHandler";
    private const string MainMenuScenePath = "res://Scenes/MainMenu.tscn";
    private const string CutsceneScenePath = "res://Scenes/Cutscenes/Cutscene1.tscn";
    private const string SpaceshipSheetPath = "res://Sprites/UI/MainMenu/main menu/spaceship sheet.png";

    private const int ShipFrameWidth = 384;
    private const int ShipFrameHeight = 216;
    private const float ShipFps = 12f;
    private const float ShipScale = 3f;
    private const float ShipHoldTime = 0.55f;
    private const float FlybyDuration = 1.35f;
    /// <summary>Duration of the reversed departure (ship flying back into place).</summary>
    private const float ReturnDuration = 0.9f;
    private const float ShipStartX = 787f;
    private const float ShipStartY = 399f;
    private const float ShipEndX = -760f;
    private const float ShipEndY = 440f;
    private const float PanelFadeTime = 0.3f;

    private const float PreviewScale = 8f;

    private Node? _handler;
    private SpaceshipMotion? _ship;
    private AnimatedSprite2D? _preview;
    private Label? _previewName;
    private Control? _panel;
    private Label? _codeLabel;
    private Label? _statusLabel;
    private VBoxContainer? _playerList;
    private Button? _startButton;
    private Button? _backButton;

    private readonly List<long> _ids = new();
    private long _selectedId = -1;
    private long _localId = 1;
    private int _previewCharacter = -1;

    private bool _isHost = true;
    private bool _panelShown;
    private bool _matchStarted;
    private bool _leaving;
    private Tween? _shipTween;

    public override void _Ready()
    {
        _handler = GetNodeOrNull(HandlerPath);

        _ship = GetNodeOrNull<SpaceshipMotion>("Spaceship");
        _preview = GetNodeOrNull<AnimatedSprite2D>("CharacterPreview");
        _previewName = GetNodeOrNull<Label>("PreviewName");
        _panel = GetNodeOrNull<Control>("PanelHolder");
        _codeLabel = GetNodeOrNull<Label>("PanelHolder/Panel/VBox/CodeLabel");
        _statusLabel = GetNodeOrNull<Label>("PanelHolder/Panel/VBox/StatusLabel");
        _playerList = GetNodeOrNull<VBoxContainer>("PanelHolder/Panel/VBox/PlayerScroll/PlayerList");
        _startButton = GetNodeOrNull<Button>("PanelHolder/Panel/VBox/StartButton");
        _backButton = GetNodeOrNull<Button>("PanelHolder/Panel/VBox/BackButton");

        if (_startButton != null)
            _startButton.Pressed += OnStartPressed;
        if (_backButton != null)
            _backButton.Pressed += OnBackPressed;

        ConnectNetworkSignals();

        StartFlybyThenPanel();

        // The session is set up after the first frame so the UI already exists.
        Callable.From(StartOrJoinSession).CallDeferred();

        // Safety net: if the host drops the connection, leave the lobby.
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
            OnBackPressed();
    }

    /// <summary>
    /// Solo/host: opens a room so other players can join with the code.
    /// Client: keeps the session created by the multiplayer screen.
    /// </summary>
    private void StartOrJoinSession()
    {
        if (Multiplayer.MultiplayerPeer == null)
            _handler?.Call("start_server", "");

        _isHost = Multiplayer.MultiplayerPeer == null || Multiplayer.IsServer();
        _localId = Multiplayer.GetUniqueId();

        RefreshAll();
    }

    // ------------------------------------------------------------------
    // Transition: the ship crosses the screen, then the panel appears.
    // ------------------------------------------------------------------
    private void StartFlybyThenPanel()
    {
        if (_panel != null)
        {
            _panel.Visible = false;
            _panel.Modulate = new Color(1, 1, 1, 0);
        }

        // The character preview only appears together with the panel.
        if (_preview != null)
            _preview.Visible = false;
        if (_previewName != null)
            _previewName.Visible = false;

        SpriteFrames? shipFrames = SpriteSheetUtil.BuildFrames(
            SpaceshipSheetPath, ShipFrameWidth, ShipFrameHeight, ShipFps, "fly");

        if (_ship == null || shipFrames == null)
        {
            RevealPanel();
            return;
        }

        _ship.SpriteFrames = shipFrames;
        _ship.Animation = "fly";
        _ship.Play();

        // The ship starts exactly where it rests on the main menu, holds for a beat
        // and then accelerates to the left until it leaves the screen.
        // The drifting motion keeps running during the whole departure and extra
        // tugs are layered on X (see SpaceshipMotion.StartDeparture).
        _ship.Position = new Vector2(ShipStartX, ShipStartY);
        _ship.BasePosition = _ship.Position;
        _ship.Scale = new Vector2(ShipScale, ShipScale);
        _ship.Rotation = 0f;

        _shipTween = CreateTween();
        _shipTween.TweenInterval(ShipHoldTime);
        _shipTween.TweenCallback(Callable.From(_ship.StartDeparture));
        _shipTween.TweenProperty(_ship, "BasePosition", new Vector2(ShipEndX, ShipEndY), FlybyDuration)
             .SetTrans(Tween.TransitionType.Quad)
             .SetEase(Tween.EaseType.In); // accelerating, not constant speed
        _shipTween.Finished += RevealPanel;
    }

    private void RevealPanel()
    {
        if (_panelShown || _panel == null)
            return;

        _panelShown = true;

        if (_ship != null)
            _ship.Visible = false;

        if (_preview != null)
            _preview.Visible = true;
        if (_previewName != null)
            _previewName.Visible = true;

        _panel.Visible = true;
        _panel.Modulate = new Color(1, 1, 1, 0);

        Tween tween = CreateTween();
        tween.TweenProperty(_panel, "modulate:a", 1.0f, PanelFadeTime);
    }

    // ------------------------------------------------------------------
    // Network signals
    // ------------------------------------------------------------------
    private void ConnectNetworkSignals()
    {
        if (_handler == null)
        {
            GD.PrintErr("MatchSetup: HighLevelNetworkHandler autoload not found.");
            return;
        }

        _handler.Connect("player_nickname_changed", Callable.From<long, string>(OnNicknameChanged));
        _handler.Connect("player_character_changed", Callable.From<long, int>(OnCharacterChanged));
        _handler.Connect("player_ready_changed", Callable.From<long, bool>(OnReadyChanged));
        _handler.Connect("peer_activity", Callable.From<long, bool>(OnPeerActivity));
        _handler.Connect("player_kicked", Callable.From<long>(OnPlayerKicked));
        _handler.Connect("match_started", Callable.From(OnMatchStarted));
    }

    // ------------------------------------------------------------------
    // Refresh helpers
    // ------------------------------------------------------------------
    private void RefreshAll()
    {
        BuildPlayerIds();
        RefreshCode();
        RebuildRows();
        RefreshStatus();
        RefreshStartButton();
        RefreshPreview();
    }

    /// <summary>
    /// Schedules a refresh for the next idle frame. Signal callbacks can fire from
    /// inside a control that the rebuild is about to free, so never rebuild directly.
    /// </summary>
    private void RequestRefresh() => Callable.From(RefreshAll).CallDeferred();

    private void BuildPlayerIds()
    {
        _ids.Clear();

        if (_handler != null)
        {
            foreach (Variant id in _handler.Call("get_player_ids").AsGodotArray())
                _ids.Add(id.AsInt64());
        }

        // Always list the local player, even without a network session.
        if (!_ids.Contains(_localId))
            _ids.Add(_localId);

        _ids.Sort();
    }

    private void RefreshCode()
    {
        if (_codeLabel == null)
            return;

        string code = _handler?.Call("get_room_code").AsString() ?? string.Empty;
        _codeLabel.Text = string.IsNullOrEmpty(code)
            ? "Room code: (offline)"
            : $"Room code: {code}";
    }

    private void RebuildRows()
    {
        if (_playerList == null)
            return;

        foreach (Node child in _playerList.GetChildren())
            child.QueueFree();

        // Characters already claimed, so a player without one can be shown with a
        // colour nobody else is using yet.
        var taken = new List<int>();
        foreach (long id in _ids)
        {
            int claimed = GetCharacter(id);
            if (claimed >= 0)
                taken.Add(claimed);
        }

        foreach (long id in _ids)
            _playerList.AddChild(BuildRow(id, taken));
    }

    private static StyleBoxFlat FlatBox(Color color, int radius)
    {
        return new StyleBoxFlat
        {
            BgColor = color,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
        };
    }

    /// <summary>
    /// One player row of the lobby table, with the same column widths as the
    /// header in the scene: [colour + hero] [nickname] [ready] [remove].
    /// </summary>
    private Control BuildRow(long id, List<int> taken)
    {
        const int RowHeight = 36;
        const int ActionWidth = 44;

        bool isMe = id == _localId;

        int character = GetCharacter(id);
        if (character < 0)
        {
            character = Characters.FirstFreeCharacter(taken);
            if (character < 0)
                character = 0;
            taken.Add(character);
        }

        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 6);

        // Column 1: big colour square plus the name of the character it stands for.
        HBoxContainer heroCell = new() { CustomMinimumSize = new Vector2(138, 0) };
        heroCell.AddThemeConstantOverride("separation", 8);

        Button colorBox = new()
        {
            CustomMinimumSize = new Vector2(RowHeight, RowHeight),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            TooltipText = isMe
                ? "Click to switch character"
                : $"{GetNickname(id)}'s character",
        };
        StyleBoxFlat colorStyle = FlatBox(Characters.GetColor(character), 4);
        colorBox.AddThemeStyleboxOverride("normal", colorStyle);
        colorBox.AddThemeStyleboxOverride("hover", colorStyle);
        colorBox.AddThemeStyleboxOverride("pressed", colorStyle);
        colorBox.AddThemeStyleboxOverride("focus", colorStyle);
        colorBox.Pressed += () => OnColorBoxPressed(id);
        heroCell.AddChild(colorBox);

        Label heroName = new()
        {
            Text = Characters.GetName(character),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        heroName.AddThemeColorOverride("font_color", Characters.GetColor(character).Lightened(0.25f));
        heroName.AddThemeFontSizeOverride("font_size", 18);
        heroCell.AddChild(heroName);

        row.AddChild(heroCell);

        // Column 2: nickname (editable for yourself; the host may also rename others).
        LineEdit nickname = new()
        {
            Text = GetNickname(id),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, RowHeight),
            MaxLength = 16,
            Editable = isMe || _isHost,
            TooltipText = "Edit nickname",
        };
        nickname.FocusEntered += () => SelectPlayer(id);
        nickname.TextSubmitted += text => OnNicknameSubmitted(id, text);
        nickname.FocusExited += () => OnNicknameSubmitted(id, nickname.Text);
        row.AddChild(nickname);

        // Column 3: ready square - red = not ready, green = ready.
        bool ready = GetReady(id);
        CenterContainer readyCell = new() { CustomMinimumSize = new Vector2(62, 0) };
        Button readyButton = new()
        {
            CustomMinimumSize = new Vector2(RowHeight, RowHeight),
            TooltipText = ready ? "Ready" : "Not ready",
            Disabled = !isMe,
        };
        StyleBoxFlat readyStyle = FlatBox(
            ready ? new Color(0.20f, 0.85f, 0.35f) : new Color(0.85f, 0.18f, 0.18f), 4);
        readyButton.AddThemeStyleboxOverride("normal", readyStyle);
        readyButton.AddThemeStyleboxOverride("hover", readyStyle);
        readyButton.AddThemeStyleboxOverride("pressed", readyStyle);
        readyButton.AddThemeStyleboxOverride("disabled", readyStyle);
        readyButton.Pressed += () => OnReadyToggled(id);
        readyCell.AddChild(readyButton);
        row.AddChild(readyCell);

        // Column 4: red X, only the host removes other players.
        CenterContainer actionCell = new() { CustomMinimumSize = new Vector2(ActionWidth, 0) };
        if (_isHost && !isMe)
        {
            Button removeButton = new()
            {
                Text = "X",
                CustomMinimumSize = new Vector2(RowHeight, RowHeight),
                TooltipText = "Remove player",
            };
            removeButton.AddThemeColorOverride("font_color", new Color(0.95f, 0.25f, 0.25f));
            removeButton.Pressed += () => OnRemovePressed(id);
            actionCell.AddChild(removeButton);
        }
        row.AddChild(actionCell);

        // Wrap so the selected row can be highlighted.
        bool selected = id == _selectedId;
        PanelContainer wrap = new();
        wrap.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = selected ? new Color(1, 1, 1, 0.10f) : new Color(0, 0, 0, 0),
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = 4,
            ContentMarginTop = 2,
            ContentMarginRight = 4,
            ContentMarginBottom = 2,
        });
        wrap.AddChild(row);
        return wrap;
    }

    private void RefreshStatus()
    {
        if (_statusLabel == null || _matchStarted)
            return;

        int readyCount = 0;
        foreach (long id in _ids)
        {
            if (GetReady(id))
                readyCount++;
        }

        if (_isHost && _ids.Count <= 1)
        {
            _statusLabel.Text = "Solo match: mark yourself as ready to start.";
            return;
        }

        _statusLabel.Text = readyCount == _ids.Count
            ? "Everyone is ready! The host can start."
            : $"{readyCount} of {_ids.Count} ready.";
    }

    private void RefreshStartButton()
    {
        if (_startButton == null)
            return;

        _startButton.Visible = _isHost;
        _startButton.Disabled = !AllReady();
    }

    private bool AllReady()
    {
        if (_ids.Count == 0)
            return false;

        foreach (long id in _ids)
        {
            if (!GetReady(id))
                return false;
        }
        return true;
    }

    private void RefreshPreview()
    {
        if (_preview == null)
            return;

        // Default to the local player when nothing was picked yet.
        if (_selectedId < 0 || !_ids.Contains(_selectedId))
            _selectedId = _ids.Contains(_localId) ? _localId : (_ids.Count > 0 ? _ids[0] : -1);

        int character = GetCharacter(_selectedId);
        if (character < 0)
            character = 0;

        // Name above the character's head, tinted with its identification colour.
        if (_previewName != null)
        {
            _previewName.Text = Characters.GetName(character).ToUpperInvariant();
            _previewName.AddThemeColorOverride("font_color", Characters.GetColor(character).Lightened(0.25f));
        }

        if (character == _previewCharacter && _preview.SpriteFrames != null)
            return;

        bool firstTime = _previewCharacter < 0;
        _previewCharacter = character;

        SpriteFrames? frames = Characters.BuildIdleFrames(character);
        if (frames == null)
            return;

        _preview.SpriteFrames = frames;
        _preview.Animation = "idle";
        _preview.Play();

        PlayPreviewPop(firstTime);
    }

    /// <summary>Pop + fade animation played every time the shown character changes.</summary>
    private void PlayPreviewPop(bool firstTime)
    {
        if (_preview == null)
            return;

        float from = PreviewScale * (firstTime ? 0.45f : 0.55f);
        _preview.Scale = new Vector2(from, from);
        _preview.Modulate = new Color(1, 1, 1, firstTime ? 0f : 0.25f);

        Tween tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(_preview, "scale", new Vector2(PreviewScale, PreviewScale), 0.4f)
             .SetTrans(Tween.TransitionType.Back)
             .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_preview, "modulate:a", 1.0f, 0.3f);
    }

    // ------------------------------------------------------------------
    // Data accessors (safe even when the handler has no value yet)
    // ------------------------------------------------------------------
    private int GetCharacter(long id) =>
        _handler?.Call("get_player_character", id).AsInt32() ?? -1;

    private bool GetReady(long id) =>
        _handler?.Call("get_player_ready", id).AsBool() ?? false;

    private string GetNickname(long id)
    {
        string nick = _handler?.Call("get_player_nickname", id).AsString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(nick))
            return nick;

        if (id == _localId && _handler != null)
        {
            string local = _handler.Get("local_nickname").AsString();
            if (!string.IsNullOrWhiteSpace(local))
                return local;
        }

        return $"Player {id}";
    }

    // ------------------------------------------------------------------
    // Player interactions
    // ------------------------------------------------------------------
    private void SelectPlayer(long id)
    {
        if (_selectedId == id)
            return;

        _selectedId = id;
        RequestRefresh();
    }

    /// <summary>
    /// Clicking the colour box selects the player for the preview.
    /// On your own row it also cycles to the next available character.
    /// </summary>
    private void OnColorBoxPressed(long id)
    {
        SelectPlayer(id);

        if (id != _localId)
            return;

        var taken = new List<int>();
        foreach (long other in _ids)
        {
            if (other == _localId)
                continue;

            int claimed = GetCharacter(other);
            if (claimed >= 0)
                taken.Add(claimed);
        }

        int next = NextFreeCharacter(GetCharacter(_localId), taken);
        _handler?.Call("set_local_character", next);
        RequestRefresh();
    }

    private static int NextFreeCharacter(int current, List<int> taken)
    {
        for (int step = 1; step <= Characters.Count; step++)
        {
            int candidate = ((current < 0 ? 0 : current) + step) % Characters.Count;
            if (!taken.Contains(candidate))
                return candidate;
        }
        return 0;
    }

    private void OnNicknameSubmitted(long id, string text)
    {
        if (_handler == null)
            return;

        string clean = text.Trim();
        if (clean.Length == 0 || clean == GetNickname(id))
            return;

        if (id == _localId)
            _handler.Call("set_local_nickname", clean);
        else if (_isHost)
            _handler.Call("set_peer_nickname", id, clean);
    }

    private void OnReadyToggled(long id)
    {
        if (id != _localId)
            return;

        _handler?.Call("set_local_ready", !GetReady(_localId));
    }

    private void OnRemovePressed(long id)
    {
        if (!_isHost || id == _localId)
            return;

        _handler?.Call("kick_player", id);
    }

    // ------------------------------------------------------------------
    // Start / leave
    // ------------------------------------------------------------------
    private void OnStartPressed()
    {
        if (!_isHost || !AllReady())
            return;

        _handler?.Call("start_match");
    }

    private void OnMatchStarted()
    {
        if (_matchStarted)
            return;

        _matchStarted = true;

        if (SceneTransitions.Instance != null)
            SceneTransitions.Instance.ChangeScene(CutsceneScenePath);
        else
            GetTree().ChangeSceneToFile(CutsceneScenePath);
    }

    private void OnBackPressed()
    {
        // Guard against re-entry: stopping the session can emit server_disconnected.
        if (_leaving)
            return;

        _leaving = true;

        // Reverse the departure: the ship flies back to its menu resting place
        // and only then the scene changes.
        if (_panelShown && _ship != null)
            PlayDepartureBack(LeaveToMainMenu);
        else
            LeaveToMainMenu();
    }

    private void LeaveToMainMenu()
    {
        _handler?.Call("stop_multiplayer");

        if (SceneTransitions.Instance != null)
            SceneTransitions.Instance.ChangeScene(MainMenuScenePath);
        else
            GetTree().ChangeSceneToFile(MainMenuScenePath);
    }

    /// <summary>Reversed departure: the ship decelerates back into its resting spot.</summary>
    private void PlayDepartureBack(System.Action onFinished)
    {
        if (_ship == null)
        {
            onFinished();
            return;
        }

        if (_shipTween != null && _shipTween.IsValid())
            _shipTween.Kill();

        // Hide the panel and the preview while the ship comes back.
        if (_panel != null)
            _panel.Visible = false;
        if (_preview != null)
            _preview.Visible = false;
        if (_previewName != null)
            _previewName.Visible = false;

        _ship.Visible = true;
        _ship.SpriteFrames ??= SpriteSheetUtil.BuildFrames(
            SpaceshipSheetPath, ShipFrameWidth, ShipFrameHeight, ShipFps, "fly");

        if (_ship.SpriteFrames != null)
        {
            _ship.Animation = "fly";
            _ship.Play();
        }

        _ship.BasePosition = new Vector2(ShipEndX, ShipEndY);

        _shipTween = CreateTween();
        _shipTween.TweenProperty(_ship, "BasePosition", new Vector2(ShipStartX, ShipStartY), ReturnDuration)
             .SetTrans(Tween.TransitionType.Quad)
             .SetEase(Tween.EaseType.Out); // decelerating into place
        _shipTween.Finished += onFinished;
    }

    private void OnServerDisconnected()
    {
        if (_leaving)
            return;

        GD.Print("[MatchSetup] Connection to the host was lost.");
        OnBackPressed();
    }

    // ------------------------------------------------------------------
    // Network signal handlers
    // ------------------------------------------------------------------
    private void OnNicknameChanged(long id, string nickname) => RequestRefresh();

    private void OnCharacterChanged(long id, int characterIndex) => RequestRefresh();

    private void OnReadyChanged(long id, bool ready) => RequestRefresh();

    private void OnPeerActivity(long id, bool joined)
    {
        GD.Print($"[MatchSetup] Player {id} {(joined ? "joined" : "left")}.");
        RequestRefresh();
    }

    private void OnPlayerKicked(long id)
    {
        // Being removed sends you back to the main menu.
        if (id == _localId)
        {
            OnBackPressed();
            return;
        }

        RequestRefresh();
    }
}
