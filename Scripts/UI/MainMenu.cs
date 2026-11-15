#nullable enable
using Godot;

/// <summary>
/// Main menu controller.
/// Phase 1: settings overlay (Sazsazan style) opened from the Options button.
/// Phase 2: reusable fade transition between the menu boxes (Start / Multiplayer).
/// Phase 3: Start box starts the game scene using the save system.
/// </summary>
public partial class MainMenu : Control
{
    [Export] public string GameScenePath = "res://Scenes/Player.tscn";
    [Export] public string CreditsScenePath = "res://Scenes/CreditsMenu.tscn";
    [Export] public string MultiplayerScenePath = "res://Scenes/multiplayer.tscn";
    /// <summary>Match setup / lobby used by Start -> New Game (solo or host).</summary>
    [Export] public string MatchSetupScenePath = "res://Scenes/MatchSetup.tscn";

    [Export] public NodePath MenuContainerPath = "MenuContainer";
    [Export] public NodePath MenuStartPath = "MenuStart";
    [Export] public NodePath MenuMultiplayerPath = "MenuMultiplayer";

    private const float FadeDuration = 0.25f;

    private Control? _menuContainer;
    private Control? _menuStart;
    private Control? _menuMultiplayer;
    private SettingsMenu? _settingsMenu;
    private PanelContainer? _toastPanel;
    private Label? _toastLabel;
    private Tween? _toastTween;
    private bool _switching;

    public override void _Ready()
    {
        _menuContainer = GetNodeOrNull<Control>(MenuContainerPath);
        _menuStart = GetNodeOrNull<Control>(MenuStartPath);
        _menuMultiplayer = GetNodeOrNull<Control>(MenuMultiplayerPath);

        if (_menuContainer == null || _menuStart == null || _menuMultiplayer == null)
        {
            GD.PrintErr("MainMenu: menu containers not found.");
            return;
        }

        // Only the main box starts visible.
        _menuContainer.Visible = true;
        _menuContainer.Modulate = Colors.White;
        _menuStart.Visible = false;
        _menuMultiplayer.Visible = false;

        // Phase 1: settings overlay built in code, added on top of the menu.
        _settingsMenu = new SettingsMenu();
        AddChild(_settingsMenu);
        _settingsMenu.BackPressed += OnSettingsClosed;

        ConnectButtons(_menuContainer, new (string, System.Action)[]
        {
            ("BtnStart", OnStartPressed),
            ("BtnMultiplayer", OnMultiplayerPressed),
            ("BtnOptions", OnOptionsPressed),
            ("BtnCredits", OnCreditsPressed),
            ("BtnLeave", OnLeavePressed),
        });

        ConnectButtons(_menuStart, new (string, System.Action)[]
        {
            ("BtnLoad", OnLoadPressed),
            ("BtnNewgame", OnNewGamePressed),
            ("BtnStartBack", OnBackPressed),
        });

        ConnectButtons(_menuMultiplayer, new (string, System.Action)[]
        {
            ("BtnCreate", OnCreatePressed),
            ("BtnJoin", OnJoinPressed),
            ("BtnMultiplayerBack", OnBackPressed),
        });
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel"))
            return;

        if (_settingsMenu != null && _settingsMenu.Visible)
        {
            _settingsMenu.HideWithFade();
            return;
        }

        if (_menuStart != null && _menuStart.Visible)
        {
            OnBackPressed();
            return;
        }

        if (_menuMultiplayer != null && _menuMultiplayer.Visible)
            OnBackPressed();
    }

    // ------------------------------------------------------------------
    // Phase 2: one reusable fade transition used by every box switch.
    // ------------------------------------------------------------------
    private async void SwitchBox(Control from, Control to)
    {
        if (_switching)
            return;

        _switching = true;

        to.Visible = true;
        to.Modulate = new Color(1, 1, 1, 0);

        Tween fadeOut = CreateTween();
        fadeOut.TweenProperty(from, "modulate:a", 0.0f, FadeDuration);
        await ToSignal(fadeOut, Tween.SignalName.Finished);

        from.Visible = false;

        Tween fadeIn = CreateTween();
        fadeIn.TweenProperty(to, "modulate:a", 1.0f, FadeDuration);
        await ToSignal(fadeIn, Tween.SignalName.Finished);

        _switching = false;
    }

    private void OpenSettings()
    {
        if (_settingsMenu == null || _menuContainer == null)
            return;

        if (_menuContainer.Visible)
        {
            // Fade the main box out, then show the settings overlay.
            Tween fadeOut = CreateTween();
            fadeOut.TweenProperty(_menuContainer, "modulate:a", 0.0f, FadeDuration);
            fadeOut.Finished += () =>
            {
                _menuContainer.Visible = false;
                _settingsMenu.ShowWithFade();
            };
        }
        else
        {
            _settingsMenu.ShowWithFade();
        }
    }

    private void OnSettingsClosed()
    {
        if (_menuContainer == null)
            return;

        _menuContainer.Visible = true;
        _menuContainer.Modulate = new Color(1, 1, 1, 0);
        Tween fadeIn = CreateTween();
        fadeIn.TweenProperty(_menuContainer, "modulate:a", 1.0f, FadeDuration);
    }

    // ------------------------------------------------------------------
    // Button handlers
    // ------------------------------------------------------------------
    private void OnStartPressed() => SwitchBox(_menuContainer!, _menuStart!);

    private void OnMultiplayerPressed() => SwitchBox(_menuContainer!, _menuMultiplayer!);

    private void OnOptionsPressed() => OpenSettings();

    private void OnCreditsPressed()
    {
        if (SceneTransitions.Instance != null)
            SceneTransitions.Instance.ChangeScene(CreditsScenePath);
        else
            GetTree().ChangeSceneToFile(CreditsScenePath);
    }

    private void OnLeavePressed() => GetTree().Quit();

    private void OnBackPressed()
    {
        if (_menuContainer == null)
            return;

        if (_menuStart != null && _menuStart.Visible)
            SwitchBox(_menuStart, _menuContainer);
        else if (_menuMultiplayer != null && _menuMultiplayer.Visible)
            SwitchBox(_menuMultiplayer, _menuContainer);
    }

    // Phase 3: Start box.
    private void StartGame(bool newRun)
    {
        Global? global = GetNodeOrNull<Global>("/root/Global");
        if (global != null)
        {
            global.NewRunRequested = newRun;
            if (newRun)
            {
                // Fresh run: overwrite the previous progress right away.
                global.Save(Vector2.Zero);
            }
            else if (global.HasSave())
            {
                global.Load();
            }
            else
            {
                GD.Print("MainMenu: no save found, starting a new run instead.");
            }
        }

        if (SceneTransitions.Instance != null)
            SceneTransitions.Instance.ChangeScene(GameScenePath);
        else
            GetTree().ChangeSceneToFile(GameScenePath);
    }

    /// <summary>
    /// Load Game: continues the stored run. When there is no save file yet,
    /// a message tells the player to start a New Game instead.
    /// </summary>
    private void OnLoadPressed()
    {
        Global? global = GetNodeOrNull<Global>("/root/Global");
        if (global == null || !global.HasSave())
        {
            ShowToast("No saved game found. Start a New Game first.");
            return;
        }

        StartGame(newRun: false);
    }

    /// <summary>Shows a short message at the bottom of the menu.</summary>
    private void ShowToast(string text)
    {
        EnsureToast();
        if (_toastPanel == null || _toastLabel == null)
            return;

        _toastLabel.Text = text;
        _toastPanel.Visible = true;

        if (_toastTween != null && _toastTween.IsValid())
            _toastTween.Kill();

        _toastTween = CreateTween();
        _toastTween.TweenProperty(_toastPanel, "modulate:a", 1.0f, 0.2f);
        _toastTween.TweenInterval(2.6f);
        _toastTween.TweenProperty(_toastPanel, "modulate:a", 0.0f, 0.4f);
        _toastTween.Finished += () =>
        {
            if (_toastPanel != null)
                _toastPanel.Visible = false;
        };
    }

    private void EnsureToast()
    {
        if (_toastPanel != null)
            return;

        // Absolute offsets in the base resolution (1152x648), so the toast is
        // independent from the tiny rect of the menu root Control.
        _toastPanel = new PanelContainer
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
            OffsetLeft = 246f,
            OffsetTop = 540f,
            OffsetRight = 906f,
            OffsetBottom = 604f,
        };
        AddChild(_toastPanel);

        _toastLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _toastLabel.AddThemeFontSizeOverride("font_size", 18);
        _toastPanel.AddChild(_toastLabel);
    }

    /// <summary>
    /// New Game always opens a fresh match setup screen, which also creates the
    /// room (so the code can be shared) and works with a single player.
    /// </summary>
    private void OnNewGamePressed()
    {
        Global? global = GetNodeOrNull<Global>("/root/Global");
        if (global != null)
        {
            global.NewRunRequested = true;
            global.Save(Vector2.Zero);
        }

        // Drop any previous session before hosting a new room.
        GetNodeOrNull("/root/HighLevelNetworkHandler")?.Call("stop_multiplayer");

        GoToScene(MatchSetupScenePath);
    }

    private void GoToScene(string scenePath)
    {
        if (SceneTransitions.Instance != null)
            SceneTransitions.Instance.ChangeScene(scenePath);
        else
            GetTree().ChangeSceneToFile(scenePath);
    }

    private void OnCreatePressed() => GoToMultiplayer();

    private void OnJoinPressed() => GoToMultiplayer();

    private void GoToMultiplayer()
    {
        if (SceneTransitions.Instance != null)
            SceneTransitions.Instance.ChangeScene(MultiplayerScenePath);
        else
            GetTree().ChangeSceneToFile(MultiplayerScenePath);
    }

    private void ConnectButtons(Control parent, (string Name, System.Action Handler)[] bindings)
    {
        foreach ((string name, System.Action handler) in bindings)
        {
            TextureButton? button = parent.GetNodeOrNull<TextureButton>(name);
            if (button == null)
            {
                GD.PrintErr($"MainMenu: button '{name}' not found in '{parent.Name}'.");
                continue;
            }
            button.Pressed += handler;
        }
    }
}
