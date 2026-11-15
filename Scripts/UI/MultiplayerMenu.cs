#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// Multiplayer screen: the rooms available on the LAN are listed on the left,
/// the join-with-code options are on the right, so the horizontal space is used.
/// Creating a room belongs to Start -> New Game (see MatchSetup), so there is no
/// intermediate choice step here. A successful join goes to the lobby screen.
/// </summary>
public partial class MultiplayerMenu : Control
{
    private const string MainMenuScenePath = "res://Scenes/MainMenu.tscn";
    private const string MatchSetupScenePath = "res://Scenes/MatchSetup.tscn";
    private const string HandlerPath = "/root/HighLevelNetworkHandler";
    private const string LeftPath = "CanvasLayer/Root/LeftPanel/LeftVBox";
    private const string RightPath = "CanvasLayer/Root/RightPanel/RightVBox";

    private LineEdit? _nickname;
    private LineEdit? _roomCode;
    private ItemList? _roomList;
    private Label? _status;

    private readonly List<string> _discoveredCodes = new();
    private readonly List<string> _discoveredIps = new();

    private Node? _handler;
    private bool _joining;

    public override void _Ready()
    {
        _handler = GetNodeOrNull(HandlerPath);

        _nickname = GetNodeOrNull<LineEdit>($"{RightPath}/Nickname");
        _roomCode = GetNodeOrNull<LineEdit>($"{RightPath}/RoomCode");
        _roomList = GetNodeOrNull<ItemList>($"{LeftPath}/RoomList");
        _status = GetNodeOrNull<Label>($"{RightPath}/JoinStatus");

        ConnectButton($"{LeftPath}/FindRooms", OnSearchPressed);
        ConnectButton($"{RightPath}/Client", OnJoinPressed);
        ConnectButton($"{RightPath}/Back", LeaveToMainMenu);

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
        SetStatus("Searching for rooms on the LAN...");
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
            SetStatus($"Connecting to {_discoveredIps[index]}...");
            _handler?.Call("start_client", _discoveredIps[index]);
            return;
        }

        string code = (_roomCode?.Text ?? "").Trim();
        SetStatus($"Looking for room {code}...");
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
            SetStatus("Pick a nickname with 1 to 16 characters.");
        return ok;
    }

    private void SetStatus(string text)
    {
        if (_status != null)
            _status.Text = text;
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
            ? "No rooms found."
            : $"{roomCount} room(s) found.");
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
