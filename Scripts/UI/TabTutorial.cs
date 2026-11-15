#nullable enable
using Godot;

/// <summary>
/// TRAINER: teaches the objectives overlay after a story cutscene.
/// Sets the stage goal, asks the player to open the objectives with TAB and only
/// continues once that happened. The goal text is exported, so it changes from
/// stage to stage (this stage: "Boot up the system").
/// </summary>
public partial class TabTutorial : Control
{
    [Signal]
    public delegate void TutorialFinishedEventHandler();

    /// <summary>Goal of this stage, shown by the objectives overlay.</summary>
    [Export] public string ObjectiveText = "Boot up the system";

    /// <summary>Scene opened when the player is done with the tutorial.</summary>
    [Export] public string NextScenePath = "";

    /// <summary>Objectives overlay that this tutorial unlocks.</summary>
    [Export] public NodePath HudPath = new("");

    private ObjectiveHud? _hud;
    private Label? _state;
    private Button? _continueButton;
    private bool _opened;
    private bool _finished;

    public override void _Ready()
    {
        _hud = string.IsNullOrEmpty(HudPath.ToString()) ? null : GetNodeOrNull<ObjectiveHud>(HudPath);
        if (_hud == null)
            GD.PushWarning("TabTutorial: objectives overlay not found, TAB will not be unlocked.");

        BuildUi();
        Visible = false;
    }

    /// <summary>Starts the tutorial step.</summary>
    public void Begin()
    {
        _opened = false;
        _finished = false;

        Global? global = GetNodeOrNull<Global>("/root/Global");
        global?.SetObjective(ObjectiveText);

        // Unlock the overlay so TAB works during and after the tutorial.
        _hud?.Activate();

        SetState("Press TAB to open your objectives.");
        if (_continueButton != null)
            _continueButton.Disabled = true;

        Visible = true;
        GD.Print($"[TabTutorial] Started. Objective: {ObjectiveText}");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible || _finished || !@event.IsActionPressed("objectives"))
            return;

        if (_opened)
            return;

        _opened = true;
        SetState($"Objective: {ObjectiveText}");
        if (_continueButton != null)
            _continueButton.Disabled = false;

        GD.Print("[TabTutorial] Objectives opened.");
    }

    private void OnContinuePressed()
    {
        if (!_opened || _finished)
            return;

        _finished = true;
        GD.Print("[TabTutorial] Finished, continuing.");

        EmitSignal(SignalName.TutorialFinished);

        if (string.IsNullOrEmpty(NextScenePath))
        {
            GD.PushWarning("TabTutorial: NextScenePath is empty, the flow cannot continue.");
            return;
        }

        if (SceneTransitions.Instance != null)
            SceneTransitions.Instance.ChangeScene(NextScenePath);
        else
            GetTree().ChangeSceneToFile(NextScenePath);
    }

    private void SetState(string text)
    {
        if (_state != null)
            _state.Text = text;
    }

    private void BuildUi()
    {
        Control root = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        CenterContainer center = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(center);

        PanelContainer panel = new() { CustomMinimumSize = new Vector2(660, 0) };
        center.AddChild(panel);

        VBoxContainer box = new();
        box.AddThemeConstantOverride("separation", 12);
        panel.AddChild(box);

        Label title = new()
        {
            Text = "OBJECTIVES",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 30);
        title.AddThemeColorOverride("font_color", new Color(0.85f, 0.62f, 0.98f));
        box.AddChild(title);

        Label message = new()
        {
            Text = "You can check your current objective at any time.",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        message.AddThemeFontSizeOverride("font_size", 22);
        box.AddChild(message);

        Label keyHint = new()
        {
            Text = "Press TAB",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        keyHint.AddThemeFontSizeOverride("font_size", 26);
        box.AddChild(keyHint);

        _state = new Label
        {
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _state.AddThemeFontSizeOverride("font_size", 20);
        _state.AddThemeColorOverride("font_color", new Color(0.55f, 0.95f, 0.60f));
        box.AddChild(_state);

        _continueButton = new Button
        {
            Text = "CONTINUE",
            CustomMinimumSize = new Vector2(0, 46),
            Disabled = true,
        };
        _continueButton.Pressed += OnContinuePressed;
        box.AddChild(_continueButton);
    }
}
