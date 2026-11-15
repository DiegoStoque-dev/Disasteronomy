#nullable enable
using Godot;

/// <summary>
/// End-of-mission summary overlay. Shows the player ranking F-S as the visual
/// highlight together with the mission statistics (time, tether fatigue,
/// rupture status and remaining SAFER fuel), plus a CONTINUE button that
/// emits <see cref="ContinuePressed"/>.
///
/// Layer 120 keeps the summary above Phase 1's existing "Screens" layer (110),
/// and the CONTINUE button simply hides this overlay, revealing the standard
/// success screen (Retry / Next phase / Back to menu) underneath.
/// </summary>
public partial class MissionSummary : CanvasLayer
{
    [Signal]
    public delegate void ContinuePressedEventHandler();

    private const int SummaryLayer = 120;

    private Control? _root;
    private Label? _titleLabel;
    private Label? _gradeLabel;
    private VBoxContainer? _statsBox;
    private Button? _continueButton;

    public override void _Ready()
    {
        Layer = SummaryLayer;
        ProcessMode = Node.ProcessModeEnum.Always;
        BuildUi();
        if (_root != null)
            _root.Visible = false;
    }

    /// <summary>Shows the summary for the current player's run.</summary>
    public void Show(float timeSeconds, int stressStrikes, bool ruptured, int remainingSafer, string title = "MISSION COMPLETE")
    {
        MissionRank.Result result = MissionRank.Compute(timeSeconds, stressStrikes, ruptured, remainingSafer);

        if (_titleLabel != null)
            _titleLabel.Text = title;

        if (_gradeLabel != null)
        {
            _gradeLabel.Text = $"RANK {result.Grade}";
            _gradeLabel.AddThemeColorOverride("font_color", GradeColor(result.Grade));
        }

        if (_statsBox != null)
        {
            ClearStatsBox();
            AddRow("Mission time", $"{timeSeconds:F1}s");
            AddRow("Tether fatigue strikes", stressStrikes.ToString());
            AddRow("Tether rupture", ruptured ? "YES" : "NO");
            AddRow("SAFER remaining", remainingSafer.ToString());
            AddRow("Score", $"{result.Score:F0}");
        }

        if (_root != null)
            _root.Visible = true;
    }

    /// <summary>Hides the overlay, revealing the standard success screen below.</summary>
    public void HideScreen()
    {
        if (_root != null)
            _root.Visible = false;
    }

    private void AddRow(string name, string value)
    {
        if (_statsBox == null)
            return;

        var label = new Label
        {
            Text = $"{name}:  {value}",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", 20);
        _statsBox.AddChild(label);
    }

    private void ClearStatsBox()
    {
        if (_statsBox == null)
            return;

        while (_statsBox.GetChildCount() > 0)
        {
            Node child = _statsBox.GetChild(0);
            _statsBox.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void BuildUi()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var backdrop = new ColorRect { Color = new Color(0, 0, 0, 0.78f) };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(680, 0) };
        center.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        panel.AddChild(box);

        _titleLabel = new Label { Text = "MISSION COMPLETE", HorizontalAlignment = HorizontalAlignment.Center };
        _titleLabel.AddThemeFontSizeOverride("font_size", 34);
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.62f, 0.98f));
        box.AddChild(_titleLabel);

        _gradeLabel = new Label { Text = "RANK S", HorizontalAlignment = HorizontalAlignment.Center };
        _gradeLabel.AddThemeFontSizeOverride("font_size", 84);
        box.AddChild(_gradeLabel);

        _statsBox = new VBoxContainer();
        _statsBox.AddThemeConstantOverride("separation", 8);
        box.AddChild(_statsBox);

        _continueButton = new Button { Text = "CONTINUE", CustomMinimumSize = new Vector2(0, 46) };
        _continueButton.Pressed += () => EmitSignal(SignalName.ContinuePressed);
        box.AddChild(_continueButton);
    }

    private static Color GradeColor(char grade) => grade switch
    {
        'S' => new Color(1f, 0.84f, 0.2f),
        'A' => new Color(0.35f, 0.95f, 0.55f),
        'B' => new Color(0.4f, 0.8f, 1f),
        'C' => new Color(0.9f, 0.9f, 0.4f),
        'D' => new Color(1f, 0.6f, 0.3f),
        _   => new Color(1f, 0.35f, 0.35f),
    };
}
