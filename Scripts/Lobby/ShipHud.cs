#nullable enable
using Godot;
using System;

/// <summary>
/// HUD of the ship interior: a semi-transparent chat/action panel in the bottom
/// left corner, the Terminal interaction prompt (host only), the 5s transition
/// countdown and the fade-to-black overlay used before switching to Phase 1.
///
/// The panel mixes three kinds of messages in one place:
///   - system/action log   (gray, [SISTEMA] prefix),
///   - character dialogues (colored by speaker, <<Name>> prefix),
///   - player chat          (white, "Nickname: text").
///
/// When the chat input is focused the player movement is paused (the manager
/// polls <see cref="IsChatFocused"/>).
/// </summary>
public partial class ShipHud : CanvasLayer
{
    /// <summary>Raised when the player submits a chat message (Enter or the send button).</summary>
    public event Action<string>? SendChatRequested;

    private const string SystemColor = "#9a9aa8";
    private const int MaxLogLines = 60;

    private Control? _root;
    private PanelContainer? _panel;
    private RichTextLabel? _log;
    private LineEdit? _chatInput;
    private Label? _interactPrompt;
    private Label? _countdownLabel;
    private ColorRect? _fadeRect;

    public bool IsChatFocused => _chatInput != null && _chatInput.HasFocus();

    public override void _Ready()
    {
        Layer = 60;
        ProcessMode = Node.ProcessModeEnum.Always;
        BuildUi();
        ShowInteractPrompt(false);
        ShowCountdown(false);

        // Keep the gameplay keys available everywhere else.
        SetProcessUnhandledInput(true);
    }

    /// <summary>
    /// Escape releases the chat focus so the player can move again without having
    /// to send the message. (Enter submits and also releases the focus.)
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Keycode != Key.Escape)
            return;

        if (_chatInput != null && _chatInput.HasFocus())
        {
            GetViewport().SetInputAsHandled();
            _chatInput.ReleaseFocus();
            _chatInput.Clear();
        }
    }

    // ------------------------------------------------------------------
    // Public API used by ShipManager
    // ------------------------------------------------------------------

    public void LogSystem(string message)
    {
        AppendLine($"[color={SystemColor}][SISTEMA][/color] {EscapeBbc(message)}");
    }

    public void LogChat(string sender, string message)
    {
        AppendLine($"[b]{EscapeBbc(sender)}:[/b] {EscapeBbc(message)}");
    }

    public void LogDialogue(string speaker, string message, Color color)
    {
        string hex = color.ToHtml();
        AppendLine($"[color={hex}]<<{EscapeBbc(speaker)}>>[/color] {EscapeBbc(message)}");
    }

    public void ShowInteractPrompt(bool visible)
    {
        if (_interactPrompt != null)
            _interactPrompt.Visible = visible;
    }

    public void ShowCountdown(bool visible)
    {
        if (_countdownLabel != null)
            _countdownLabel.Visible = visible;
    }

    public void SetCountdown(float remaining)
    {
        if (_countdownLabel == null)
            return;

        _countdownLabel.Text = $"ATIVANDO O TERMINAL...  {remaining:F0}s";
        _countdownLabel.Visible = true;
    }

    public void SetPromptText(string text)
    {
        if (_interactPrompt != null)
            _interactPrompt.Text = text;
    }

    /// <summary>Smooth fade to a completely black screen, then fires onComplete.</summary>
    public void FadeToBlack(float duration, Action onComplete)
    {
        if (_fadeRect == null)
        {
            onComplete?.Invoke();
            return;
        }

        _fadeRect.Visible = true;
        _fadeRect.Modulate = new Color(1f, 1f, 1f, 0f);

        Tween tween = CreateTween();
        tween.TweenProperty(_fadeRect, "modulate:a", 1f, duration);
        tween.TweenCallback(Callable.From(() => onComplete?.Invoke()));
    }

    // ------------------------------------------------------------------
    // Chat submission
    // ------------------------------------------------------------------

    private void SubmitChat(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        SendChatRequested?.Invoke(text.Trim());
        if (_chatInput != null)
        {
            _chatInput.Clear();
            _chatInput.ReleaseFocus(); // leave the chat: movement resumes immediately.
        }
    }

    // ------------------------------------------------------------------
    // UI construction
    // ------------------------------------------------------------------

    private void AppendLine(string bbcodeLine)
    {
        if (_log == null)
            return;

        _log.AppendText(bbcodeLine + "\n");

        // Keep the log readable: drop the oldest lines when it grows too long.
        if (_log.GetLineCount() > MaxLogLines)
            _log.Clear();
    }

    /// <summary>Escapes BBCode so player text can't inject tags.</summary>
    private static string EscapeBbc(string value) =>
        value.Replace("[", "[lb]").Replace("]", "[rb]");

    private void BuildUi()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        // Fade overlay (hidden until the terminal transition starts).
        _fadeRect = new ColorRect
        {
            Color = Colors.Black,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _fadeRect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_fadeRect);

        // --- Bottom-left chat/action panel ---
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.62f),
            BorderColor = new Color(0.55f, 0.5f, 0.9f, 0.85f),
        };
        style.SetCornerRadiusAll(8);
        style.SetBorderWidthAll(2);

        _panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", style);
        _root.AddChild(_panel);

        // Bottom-left, 16px standard margins. Rect: (16, 394)-(400, 632), 384x238.
        _panel.AnchorLeft = 0f;
        _panel.AnchorRight = 0f;
        _panel.AnchorTop = 1f;
        _panel.AnchorBottom = 1f;
        _panel.OffsetLeft = 16f;
        _panel.OffsetRight = 400f;
        _panel.OffsetTop = -254f;
        _panel.OffsetBottom = -16f;

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(box);

        _log = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollFollowing = true,
            ScrollActive = true,
            FitContent = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _log.AddThemeFontSizeOverride("normal_font_size", 15);
        _log.CustomMinimumSize = new Vector2(0, 180f);
        box.AddChild(_log);

        var inputRow = new HBoxContainer();
        inputRow.AddThemeConstantOverride("separation", 8);
        box.AddChild(inputRow);

        _chatInput = new LineEdit
        {
            PlaceholderText = "Fale com a equipe... (Enter)",
            MaxLength = 120,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _chatInput.TextSubmitted += SubmitChat;
        inputRow.AddChild(_chatInput);

        var sendButton = new Button { Text = "Enviar" };
        sendButton.Pressed += () => SubmitChat(_chatInput?.Text ?? "");
        inputRow.AddChild(sendButton);

        // --- Terminal interaction prompt (center-bottom) ---
        _interactPrompt = new Label
        {
            Text = "E  -  ATIVAR TERMINAL",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _interactPrompt.AddThemeFontSizeOverride("font_size", 24);
        _interactPrompt.AddThemeColorOverride("font_color", new Color(0f, 0.95f, 0.75f));
        _root.AddChild(_interactPrompt);

        _interactPrompt.AnchorLeft = 0.5f;
        _interactPrompt.AnchorRight = 0.5f;
        _interactPrompt.AnchorTop = 1f;
        _interactPrompt.AnchorBottom = 1f;
        // 320 wide (offsets 160 each side), bottom margin 16. Rect: (416, 596)-(736, 632).
        _interactPrompt.OffsetLeft = -160f;
        _interactPrompt.OffsetRight = 160f;
        _interactPrompt.OffsetTop = -52f;
        _interactPrompt.OffsetBottom = -16f;

        // --- Countdown label (center, during the terminal activation) ---
        _countdownLabel = new Label
        {
            Text = "ATIVANDO O TERMINAL...  5s",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _countdownLabel.AddThemeFontSizeOverride("font_size", 40);
        _countdownLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.8f, 0.35f));
        _root.AddChild(_countdownLabel);

        // Centered 900x90 (Rect: (126, 279)-(1026, 369)); not reduced.
        _countdownLabel.AnchorLeft = 0.5f;
        _countdownLabel.AnchorRight = 0.5f;
        _countdownLabel.AnchorTop = 0.5f;
        _countdownLabel.AnchorBottom = 0.5f;
        _countdownLabel.OffsetLeft = -450f;
        _countdownLabel.OffsetRight = 450f;
        _countdownLabel.OffsetTop = -45f;
        _countdownLabel.OffsetBottom = 45f;
        _countdownLabel.Visible = false;
        _countdownLabel.Visible = false;
    }
}
