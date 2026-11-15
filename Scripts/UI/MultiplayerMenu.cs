#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// Multiplayer screen: lists the available rooms and lets the player join one
/// by picking it from the list or typing its code.
/// Creating a room now belongs to Start -> New Game (see MatchSetup).
/// A successful join goes straight to the match setup / lobby screen.
/// </summary>
public partial class MultiplayerMenu : Control
{
    private const string MainMenuScenePath = "res://Scenes/MainMenu.tscn";
    private const string MatchSetupScenePath = "res://Scenes/MatchSetup.tscn";
    private const string HandlerPath = "/root/HighLevelNetworkHandler";
    private const string RootPath = "CanvasLayer/PanelHolder/Panel/Root";

    private LineEdit? _nickname;
    private LineEdit? _roomCode;
    private ItemList? _roomList;
    private Label? _joinStatus;

    private readonly List<string> _discoveredCodes = new();
    private readonly List<string> _discoveredIps = new();

    private Node? _handler;
    private bool _joining;

    public override void _Ready()
    {
        _handler = GetNodeOrNull(HandlerPath);

        _nickname = GetNodeOrNull<LineEdit>($"{RootPath}/Nickname");
        _roomCode = GetNodeOrNull<LineEdit>($"{RootPath}/RoomCode");
        _roomList = GetNodeOrNull<ItemList>($"{RootPath}/RoomList");
        _joinStatus = GetNodeOrNull<Label>($"{RootPath}/JoinStatus");

        ConnectButton($"{RootPath}/FindRooms", OnSearchPressed);
        ConnectButton($"{RootPath}/Client", OnJoinPressed);
        ConnectButton($"{RootPath}/Back", LeaveToMainMenu);

        if (_roomList != null)
            _roomList.ItemSelected += OnRoomSelected;

        ConnectNetworkSignals();

        if (_nickname != null && _handler != null)
            _nickname.Text = _handler.Get("local_nickname").AsString();

        Multiplayer.ConnectedToServer += OnConnectedToServer;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
            LeaveToMainMenu();
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

        _handler.Connect("room_found", Callable.From<string, string>(OnRoomFound));
        _handler.Connect("room_search_completed", Callable.From<int>(OnRoomSearchCompleted));
    }

    // ------------------------------------------------------------------
    // Actions
    // ------------------------------------------------------------------
    private void OnSearchPressed()
    {
        _roomList?.Clear();
        _discoveredCodes.Clear();
        _discoveredIps.Clear();
        SetStatus("Procurando salas na rede...");
        _handler?.Call("discover_rooms");
    }

    private void OnJoinPressed()
    {
        if (!SaveNickname())
            return;

        int index = _roomList != null && _roomList.GetSelectedItems().Length > 0
            ? (int)_roomList.GetSelectedItems()[0]
            : -1;

        _joining = true;

        if (index >= 0 && index < _discoveredIps.Count)
        {
            SetStatus($"Conectando a {_discoveredIps[index]}...");
            _handler?.Call("start_client", _discoveredIps[index]);
            return;
        }

        string code = (_roomCode?.Text ?? "").Trim();
        SetStatus($"Procurando a sala {code}...");
        _handler?.Call("start_client_by_room_code", code);
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
            SetStatus("Escolha um apelido com 1 a 16 caracteres.");
        return ok;
    }

    private void SetStatus(string text)
    {
        if (_joinStatus != null)
            _joinStatus.Text = text;
        GD.Print("[MultiplayerMenu] " + text);
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
        SetStatus(roomCount == 0
            ? "Nenhuma sala encontrada."
            : $"{roomCount} sala(s) encontrada(s).");
    }

    private void OnConnectedToServer()
    {
        if (!_joining)
            return;

        // Joined: go straight to the lobby where the match is configured.
        if (SceneTransitions.Instance != null)
            SceneTransitions.Instance.ChangeScene(MatchSetupScenePath);
        else
            GetTree().ChangeSceneToFile(MatchSetupScenePath);
    }
}
