#nullable enable
using Godot;

/// <summary>
/// Objectives overlay (reusable).
/// Shows a small hint at the bottom of the screen and, while the player presses
/// the "objectives" action (TAB), the goal of the current stage.
/// The text comes from Global.CurrentObjective, so it changes from stage to stage.
/// Drop this scene into any gameplay scene.
/// </summary>
public partial class ObjectiveHud : CanvasLayer
{
    private const string HintText = "TAB  -  OBJECTIVES";

    private Control? _root;
    private PanelContainer? _panel;
    private Label? _objectiveLabel;

    private bool _active;
    private bool _expanded;

    public override void _Ready()
    {
        Layer = 5;
        ProcessMode = Node.ProcessModeEnum.Always;

        BuildUi();

        Global? global = GetNodeOrNull<Global>("/root/Global");
        global?.Connect("objective_changed", Callable.From(Refresh));

        _active = false;
        ApplyVisibility(false);
        Refresh();
    }

    /// <summary>Enables the overlay (called once the player learned about it).</summary>
    public void Activate()
    {
        _active = true;
        ApplyVisibility(true);
        Refresh();
    }

    public void Deactivate()
    {
        _active = false;
        _expanded = false;
        ApplyVisibility(false);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_active || !@event.IsActionPressed("objectives"))
            return;

        // TAB toggles the objectives panel. Not marked as handled on purpose, so
        // the TAB tutorial can also watch the same press.
        SetExpanded(!_expanded);
    }

    private void SetExpanded(bool expanded)
    {
        _expanded = expanded;
        if (_panel != null)
            _panel.Visible = expanded;

        if (expanded)
            Refresh();
    }

    private void Refresh()
    {
        if (_objectiveLabel == null)
            return;

        Global? global = GetNodeOrNull<Global>("/root/Global");
        string objective = global?.CurrentObjective ?? string.Empty;

        _objectiveLabel.Text = string.IsNullOrEmpty(objective)
            ? "No objective right now."
            : objective;
    }

    private void ApplyVisibility(bool value)
    {
        if (_root != null)
            _root.Visible = value;
        if (_panel != null)
            _panel.Visible = value && _expanded;
    }

    private void BuildUi()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        VBoxContainer column = new()
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.End,
        };
        column.AnchorLeft = 0.5f;
        column.AnchorRight = 0.5f;
        column.AnchorTop = 1f;
        column.AnchorBottom = 1f;
        column.OffsetLeft = -330f;
        column.OffsetRight = 330f;
        column.OffsetTop = -180f;
        column.OffsetBottom = -22f;
        column.AddThemeConstantOverride("separation", 8);
        _root.AddChild(column);

        // Objectives panel, hidden until TAB is pressed.
        _panel = new PanelContainer
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        column.AddChild(_panel);

        VBoxContainer panelBox = new();
        panelBox.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(panelBox);

        Label title = new()
        {
            Text = "CURRENT OBJECTIVE",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 18);
        title.AddThemeColorOverride("font_color", new Color(0.85f, 0.62f, 0.98f));
        panelBox.AddChild(title);

        _objectiveLabel = new Label
        {
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _objectiveLabel.AddThemeFontSizeOverride("font_size", 22);
        panelBox.AddChild(_objectiveLabel);

        // Permanent hint so the player remembers the key.
        Label hint = new()
        {
            Text = HintText,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        hint.AddThemeFontSizeOverride("font_size", 16);
        hint.AddThemeColorOverride("font_color", new Color(0.70f, 0.66f, 0.80f));
        column.AddChild(hint);
    }
}
