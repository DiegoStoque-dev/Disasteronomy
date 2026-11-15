#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// Settings overlay for the main menu (Phase 1).
/// Recreates the Sazsazan settings screen: video options (fullscreen + resolution)
/// and input remapping, built entirely in code so it does not depend on scene layout.
/// Opening and closing use a fade in / fade out animation.
/// </summary>
public partial class SettingsMenu : CanvasLayer
{
    [Signal]
    public delegate void VoltarPressedEventHandler();

    // Friendly names shown next to each remappable action.
    private static readonly Dictionary<string, string> ActionNames = new()
    {
        { "mover_esquerda", "Andar para a esquerda" },
        { "mover_direita", "Andar para a direita" },
        { "pular", "Pular" },
        { "dash", "Dash (impulso)" },
        { "correr", "Correr" },
        { "voice_chat", "Chat de Voz" },
    };

    private const float FadeInTime = 0.25f;
    private const float FadeOutTime = 0.2f;

    private readonly Dictionary<string, Button> _actionButtons = new();
    private Control? _root;
    private Tween? _tween;
    private string? _actionAwaitingKey;

    public override void _Ready()
    {
        Layer = 20;
        ProcessMode = Node.ProcessModeEnum.Always;
        Visible = false;
        BuildPanel();
    }

    /// <summary>Fades the settings panel in.</summary>
    public void ShowWithFade()
    {
        if (_root == null)
            return;

        _actionAwaitingKey = null;
        UpdateKeyTexts();

        Visible = true;
        KillTween();
        _root.Modulate = new Color(1, 1, 1, 0);
        _tween = CreateTween();
        _tween.TweenProperty(_root, "modulate:a", 1.0f, FadeInTime);
    }

    /// <summary>Fades the settings panel out, then emits VoltarPressed.</summary>
    public void HideWithFade()
    {
        if (_root == null || !Visible)
            return;

        _actionAwaitingKey = null;
        KillTween();
        _tween = CreateTween();
        _tween.TweenProperty(_root, "modulate:a", 0.0f, FadeOutTime);
        _tween.Finished += () =>
        {
            Visible = false;
            EmitSignal(SignalName.VoltarPressed);
        };
    }

    private void KillTween()
    {
        if (_tween != null && _tween.IsValid())
            _tween.Kill();
    }

    private void BuildPanel()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        // Dark backdrop that also blocks clicks behind the panel.
        ColorRect backdrop = new()
        {
            Color = new Color(0, 0, 0, 0.6f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(backdrop);

        CenterContainer center = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(center);

        PanelContainer panel = new();
        panel.CustomMinimumSize = new Vector2(500, 0);
        center.AddChild(panel);

        VBoxContainer vbox = new();
        vbox.AddThemeConstantOverride("separation", 10);
        panel.AddChild(vbox);

        vbox.AddChild(MakeTitle("CONFIGURAÇÕES", 24));
        vbox.AddChild(MakeTitle("CONTROLES", 18));
        BuildInputRows(vbox);
        vbox.AddChild(MakeTitle("VÍDEO", 18));
        BuildVideoRows(vbox);
        vbox.AddChild(MakeTitle("ÁUDIO", 18));
        BuildAudioRows(vbox);

        Label help = new()
        {
            Text = "Clique em uma tecla e depois aperte a nova tecla. ESC cancela.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        help.AddThemeFontSizeOverride("font_size", 12);
        help.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f));
        vbox.AddChild(help);

        Button back = new() { Text = "VOLTAR", CustomMinimumSize = new Vector2(0, 40) };
        back.Pressed += HideWithFade;
        vbox.AddChild(back);
    }

    private static Label MakeTitle(string text, int size)
    {
        Label label = new() { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Colors.White);
        return label;
    }

    private void BuildInputRows(VBoxContainer vbox)
    {
        Global? global = GetNodeOrNull<Global>("/root/Global");
        if (global == null)
            return;

        foreach (string action in Global.RemappableActions)
        {
            string displayName = ActionNames.TryGetValue(action, out string? friendly) ? friendly : action;

            HBoxContainer row = new();
            row.AddThemeConstantOverride("separation", 10);

            Label label = new() { Text = displayName, CustomMinimumSize = new Vector2(200, 0) };
            label.AddThemeFontSizeOverride("font_size", 16);
            label.AddThemeColorOverride("font_color", Colors.White);
            row.AddChild(label);

            Button button = new()
            {
                Text = global.GetActionKeyText(action),
                CustomMinimumSize = new Vector2(160, 34),
            };

            string capturedAction = action;
            Button capturedButton = button;
            button.Pressed += () => StartAwaitingKey(capturedAction, capturedButton);

            row.AddChild(button);
            _actionButtons[action] = button;
            vbox.AddChild(row);
        }
    }

    /// <summary>Music volume slider (0-100%), applied instantly and saved.</summary>
    private void BuildAudioRows(VBoxContainer vbox)
    {
        Global? global = GetNodeOrNull<Global>("/root/Global");

        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 10);

        Label label = new()
        {
            Text = "Volume da música",
            CustomMinimumSize = new Vector2(200, 0),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        row.AddChild(label);

        HSlider slider = new()
        {
            MinValue = 0,
            MaxValue = 100,
            Step = 1,
            Value = (global?.MusicVolume ?? 0.7f) * 100f,
            CustomMinimumSize = new Vector2(160, 26),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        slider.ValueChanged += value => global?.SetMusicVolume((float)value / 100f);
        row.AddChild(slider);

        vbox.AddChild(row);
    }

    private void BuildVideoRows(VBoxContainer vbox)
    {
        Global? global = GetNodeOrNull<Global>("/root/Global");
        if (global == null)
            return;

        // Fullscreen toggle.
        HBoxContainer fullscreenRow = new();
        fullscreenRow.AddThemeConstantOverride("separation", 10);
        Label fullscreenLabel = new() { Text = "Modo de exibição", CustomMinimumSize = new Vector2(200, 0) };
        fullscreenLabel.AddThemeFontSizeOverride("font_size", 16);
        fullscreenLabel.AddThemeColorOverride("font_color", Colors.White);
        fullscreenRow.AddChild(fullscreenLabel);

        CheckButton fullscreenToggle = new()
        {
            Text = "Tela cheia",
            ButtonPressed = global.IsFullscreen,
        };
        fullscreenToggle.AddThemeColorOverride("font_color", Colors.White);
        fullscreenToggle.Toggled += enabled => global.SetFullscreen(enabled);
        fullscreenRow.AddChild(fullscreenToggle);
        vbox.AddChild(fullscreenRow);

        // Resolution selector.
        HBoxContainer resolutionRow = new();
        resolutionRow.AddThemeConstantOverride("separation", 10);
        Label resolutionLabel = new() { Text = "Resolução", CustomMinimumSize = new Vector2(200, 0) };
        resolutionLabel.AddThemeFontSizeOverride("font_size", 16);
        resolutionLabel.AddThemeColorOverride("font_color", Colors.White);
        resolutionRow.AddChild(resolutionLabel);

        OptionButton resolutionSelect = new() { CustomMinimumSize = new Vector2(160, 34) };
        int currentIndex = 0;
        for (int i = 0; i < Global.AvailableResolutions.Length; i++)
        {
            Vector2I resolution = Global.AvailableResolutions[i];
            resolutionSelect.AddItem($"{resolution.X} x {resolution.Y}");
            if (resolution == global.WindowResolution)
                currentIndex = i;
        }
        resolutionSelect.Select(currentIndex);
        resolutionSelect.ItemSelected += index => global.SetResolution(Global.AvailableResolutions[(int)index]);
        resolutionRow.AddChild(resolutionSelect);
        vbox.AddChild(resolutionRow);
    }

    private void StartAwaitingKey(string action, Button button)
    {
        _actionAwaitingKey = action;
        button.Text = "Aperte uma tecla...";
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_actionAwaitingKey == null || !Visible)
            return;

        if (@event is InputEventKey keyEvent && keyEvent.Pressed)
        {
            GetViewport().SetInputAsHandled();

            Key newKey = keyEvent.PhysicalKeycode != Key.None ? keyEvent.PhysicalKeycode : keyEvent.Keycode;

            // ESC cancels remapping.
            if (newKey == Key.Escape)
            {
                _actionAwaitingKey = null;
                UpdateKeyTexts();
                return;
            }

            Global? global = GetNodeOrNull<Global>("/root/Global");
            global?.SetActionKey(_actionAwaitingKey, newKey);
            _actionAwaitingKey = null;
            UpdateKeyTexts();
        }
    }

    private void UpdateKeyTexts()
    {
        Global? global = GetNodeOrNull<Global>("/root/Global");
        if (global == null)
            return;

        foreach (KeyValuePair<string, Button> pair in _actionButtons)
            pair.Value.Text = global.GetActionKeyText(pair.Key);
    }
}
