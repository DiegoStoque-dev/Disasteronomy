#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// Multiplayer screen with three separate views (Phase 4):
/// - MenuView: nickname + "Criar sala" / "Entrar em uma sala".
/// - CreateView: shows the generated room code and the connected players.
/// - JoinView: shows the room list and the room code field.
/// </summary>
public partial class MultiplayerMenu : Control
{
    private const string MainMenuScenePath = "res://Scenes/MainMenu.tscn";
    private const string HandlerPath = "/root/HighLevelNetworkHandler";
    private const string RootPath = "CanvasLayer/Center/Panel/Root";
    private const float FadeTime = 0.18f;

    private LineEdit? _nickname;
    private LineEdit? _roomCode;
    private ItemList? _roomList;
    private ItemList? _playerList;
    private Label? _roomCodeLabel;
    private Label? _menuStatus;
    private Label? _createStatus;
    private Label? _joinStatus;
    private Control? _menuView;
    private Control? _createView;
    private Control? _joinView;
    private Control? _center;
    private Control? _backdrop;

    private readonly List<string> _discoveredCodes = new();
    private readonly List<string> _discoveredIps = new();
    private readonly Dictionary<long, string> _players = new();

    private Node? _handler;
    private bool _switching;
    private bool _inGameView;

    public override void _Ready()
    {
        _handler = GetNodeOrNull(HandlerPath);

        _menuView = GetNodeOrNull<Control>($"{RootPath}/MenuView");
        _createView = GetNodeOrNull<Control>($"{RootPath}/CreateView");
        _joinView = GetNodeOrNull<Control>($"{RootPath}/JoinView");
        _center = GetNodeOrNull<Control>("CanvasLayer/Center");
        _backdrop = GetNodeOrNull<Control>("CanvasLayer/Backdrop");

        _nickname = GetNodeOrNull<LineEdit>($"{RootPath}/MenuView/Nickname");
        _menuStatus = GetNodeOrNull<Label>($"{RootPath}/MenuView/MenuStatus");
        _roomCodeLabel = GetNodeOrNull<Label>($"{RootPath}/CreateView/RoomCodeLabel");
        _playerList = GetNodeOrNull<ItemList>($"{RootPath}/CreateView/PlayerList");
        _createStatus = GetNodeOrNull<Label>($"{RootPath}/CreateView/CreateStatus");
        _roomList = GetNodeOrNull<ItemList>($"{RootPath}/JoinView/RoomList");
        _roomCode = GetNodeOrNull<LineEdit>($"{RootPath}/JoinView/RoomCode");
        _joinStatus = GetNodeOrNull<Label>($"{RootPath}/JoinView/JoinStatus");

        ConnectButton($"{RootPath}/MenuView/CreateRoom", OnCreateRoomPressed);
        ConnectButton($"{RootPath}/MenuView/JoinRoom", OnJoinRoomPressed);
        ConnectButton($"{RootPath}/MenuView/Back", LeaveToMainMenu);
        ConnectButton($"{RootPath}/CreateView/EnterGame", OnEnterGamePressed);
        ConnectButton($"{RootPath}/CreateView/CreateBack", OnBackToMenuPressed);
        ConnectButton($"{RootPath}/JoinView/FindRooms", OnSearchPressed);
        ConnectButton($"{RootPath}/JoinView/Client", OnClientJoinPressed);
        ConnectButton($"{RootPath}/JoinView/JoinBack", OnBackToMenuPressed);

        if (_roomList != null)
            _roomList.ItemSelected += OnRoomSelected;

        ConnectNetworkSignals();

        if (_nickname != null && _handler != null)
            _nickname.Text = _handler.Get("local_nickname").AsString();

        ShowView(_menuView, instant: true);
        Multiplayer.ConnectedToServer += OnConnectedToServer;
    }

    private void ConnectButton(string path, System.Action handler)
    {
        Button? button = GetNodeOrNull<Button>(path);
        if (button == null)
        {
            GD.PrintErr($"MultiplayerMenu: button '{path}' not found.");
            return;
        }
        button.Pressed += handler;
    }

    private void ConnectNetworkSignals()
    {
        if (_handler == null)
        {
            GD.PrintErr("MultiplayerMenu: HighLevelNetworkHandler autoload not found.");
            return;
        }

        _handler.Connect("room_created", Callable.From<string>(OnRoomCreated));
        _handler.Connect("room_found", Callable.From<string, string>(OnRoomFound));
        _handler.Connect("room_search_completed", Callable.From<int>(OnRoomSearchCompleted));
        _handler.Connect("server_started", Callable.From(OnServerStarted));
        _handler.Connect("player_nickname_changed", Callable.From<long, string>(OnPlayerNicknameChanged));
        _handler.Connect("peer_activity", Callable.From<long, bool>(OnPeerActivity));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel"))
            return;

        if (_inGameView)
        {
            ShowLobby();
            return;
        }

        if (_createView?.Visible == true || _joinView?.Visible == true)
        {
            OnBackToMenuPressed();
            return;
        }

        LeaveToMainMenu();
    }

    // ------------------------------------------------------------------
    // View navigation (single reusable fade transition, like MainMenu)
    // ------------------------------------------------------------------
    private async void ShowView(Control? target, bool instant = false)
    {
        if (target == null || _switching)
            return;

        _switching = true;

        foreach (Control? view in new[] { _menuView, _createView, _joinView })
        {
            if (view == null || view == target || !view.Visible)
                continue;

            if (!instant)
            {
                Tween fadeOut = CreateTween();
                fadeOut.TweenProperty(view, "modulate:a", 0.0f, FadeTime);
                await ToSignal(fadeOut, Tween.SignalName.Finished);
            }
            view.Visible = false;
        }

        target.Visible = true;
        if (instant)
        {
            target.Modulate = Colors.White;
        }
        else
        {
            target.Modulate = new Color(1, 1, 1, 0);
            Tween fadeIn = CreateTween();
            fadeIn.TweenProperty(target, "modulate:a", 1.0f, FadeTime);
            await ToSignal(fadeIn, Tween.SignalName.Finished);
        }

        _switching = false;
    }

    private void OnEnterGamePressed()
    {
        _inGameView = true;
        if (_center != null)
            _center.Visible = false;
        if (_backdrop != null)
            _backdrop.Visible = false;
    }

    private void ShowLobby()
    {
        _inGameView = false;
        if (_center != null)
            _center.Visible = true;
        if (_backdrop != null)
            _backdrop.Visible = true;
    }

    // ------------------------------------------------------------------
    // Lobby actions
    // ------------------------------------------------------------------
    private void OnCreateRoomPressed()
    {
        if (!SaveNickname())
            return;

        SetText(_menuStatus, "Criando sala...");
        _handler?.Call("start_server", "");
        ShowView(_createView);
    }

    private void OnJoinRoomPressed()
    {
        if (!SaveNickname())
            return;

        SetText(_joinStatus, "Busque salas ou digite um código.");
        ShowView(_joinView);
    }

    private void OnSearchPressed()
    {
        _roomList?.Clear();
        _discoveredCodes.Clear();
        _discoveredIps.Clear();
        SetText(_joinStatus, "Procurando salas na rede...");
        _handler?.Call("discover_rooms");
    }

    private void OnClientJoinPressed()
    {
        if (!SaveNickname())
            return;

        int index = _roomList != null && _roomList.GetSelectedItems().Length > 0
            ? (int)_roomList.GetSelectedItems()[0]
            : -1;

        if (index >= 0 && index < _discoveredIps.Count)
        {
            SetText(_joinStatus, $"Conectando a {_discoveredIps[index]}...");
            _handler?.Call("start_client", _discoveredIps[index]);
            return;
        }

        string code = (_roomCode?.Text ?? "").Trim();
        SetText(_joinStatus, $"Procurando a sala {code}...");
        _handler?.Call("start_client_by_room_code", code);
    }

    /// <summary>Returns to the first multiplayer view, closing any active session.</summary>
    private void OnBackToMenuPressed()
    {
        _handler?.Call("stop_multiplayer");
        RemoveSpawnedPlayers();
        _players.Clear();
        RefreshPlayerList();
        if (_roomCodeLabel != null)
            _roomCodeLabel.Text = "Código da sala: ------";
        ShowLobby();
        ShowView(_menuView);
    }

    /// <summary>Removes player nodes spawned by the MultiplayerSpawner.</summary>
    private void RemoveSpawnedPlayers()
    {
        foreach (Node child in GetChildren())
        {
            if (child is CharacterBody2D)
                child.QueueFree();
        }
    }

    private void LeaveToMainMenu()
    {
        _handler?.Call("stop_multiplayer");

        if (SceneTransitions.Instance != null)
            SceneTransitions.Instance.ChangeScene(MainMenuScenePath);
        else
            GetTree().ChangeSceneToFile(MainMenuScenePath);
    }

    private bool SaveNickname()
    {
        if (_handler == null)
            return false;

        bool ok = _handler.Call("set_local_nickname", _nickname?.Text ?? "").AsBool();
        if (!ok)
            SetText(_menuStatus, "Escolha um apelido com 1 a 16 caracteres.");
        return ok;
    }

    private static void SetText(Label? label, string text)
    {
        if (label != null)
            label.Text = text;
    }

    // ------------------------------------------------------------------
    // Signal handlers
    // ------------------------------------------------------------------
    private void OnRoomSelected(long index)
    {
        int i = (int)index;
        if (_roomCode != null && i >= 0 && i < _discoveredCodes.Count)
            _roomCode.Text = _discoveredCodes[i];
    }

    private void OnRoomFound(string roomCode, string hostIp)
    {
        _roomList?.AddItem($"{roomCode} - {hostIp}");
        _discoveredCodes.Add(roomCode);
        _discoveredIps.Add(hostIp);
    }

    private void OnRoomSearchCompleted(int roomCount)
    {
        SetText(_joinStatus, roomCount == 0
            ? "Nenhuma sala encontrada."
            : $"{roomCount} sala(s) encontrada(s).");
    }

    private void OnRoomCreated(string roomCode)
    {
        if (_roomCodeLabel != null)
            _roomCodeLabel.Text = $"Código da sala: {roomCode}";
        SetText(_createStatus, "Sala criada. Compartilhe o código para os outros entrarem.");
    }

    private void OnServerStarted()
    {
        SetText(_createStatus, "Sala ativa. Aguardando jogadores...");
        ShowView(_createView);
    }

    private void OnConnectedToServer()
    {
        // Show the room view so the player also sees who is connected.
        SetText(_createStatus, "Conectado à sala!");
        ShowView(_createView);
    }

    private void OnPlayerNicknameChanged(long peerId, string nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
            _players.Remove(peerId);
        else
            _players[peerId] = nickname;

        RefreshPlayerList();
        SetText(_createStatus, $"{_players.Count} jogador(es) conectado(s).");
    }

    private void OnPeerActivity(long peerId, bool joined)
    {
        SetText(_createStatus, $"Jogador {peerId} {(joined ? "entrou na sala" : "saiu da sala")}.");
    }

    /// <summary>Rebuilds the connected players list shown on the create-room view.</summary>
    private void RefreshPlayerList()
    {
        if (_playerList == null)
            return;

        _playerList.Clear();
        int localId = Multiplayer.GetUniqueId();

        foreach (KeyValuePair<long, string> entry in _players)
        {
            string displayName = string.IsNullOrWhiteSpace(entry.Value)
                ? $"Jogador {entry.Key}"
                : entry.Value;
            string suffix = entry.Key == localId ? "  (você)" : "";
            _playerList.AddItem($"{displayName}{suffix}");
        }
    }
}
