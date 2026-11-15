#nullable enable
using Godot;

/// <summary>
/// Voice chat tutorial shown at the end of cutscene 2.
/// The cutscene stays paused until every connected player uses the voice chat
/// (by holding the voice_chat action, V by default).
/// The host can also skip the whole step for everyone.
/// </summary>
public partial class VoiceTutorial : Control
{
    [Signal]
    public delegate void TutorialFinishedEventHandler();

    private const string HandlerPath = "/root/HighLevelNetworkHandler";

    /// <summary>How long the key must be held to count as "using" the voice chat.</summary>
    private const float HoldSeconds = 0.8f;

    private Node? _handler;
    private Label? _localState;
    private Label? _progressLabel;
    private Button? _skipButton;

    private bool _localDone;
    private float _holdTime;
    private bool _finished;

    public override void _Ready()
    {
        _handler = GetNodeOrNull(HandlerPath);

        _localState = GetNodeOrNull<Label>("Center/Panel/VBox/LocalState");
        _progressLabel = GetNodeOrNull<Label>("Center/Panel/VBox/ProgressLabel");
        _skipButton = GetNodeOrNull<Button>("Center/Panel/VBox/SkipButton");

        if (_skipButton != null)
        {
            _skipButton.Visible = IsHost();
            _skipButton.Pressed += OnSkipPressed;
        }

        if (_handler != null)
        {
            _handler.Connect("tutorial_progress_changed", Callable.From<int, int>(OnProgressChanged));
            _handler.Connect("tutorial_finished", Callable.From(OnTutorialFinished));
        }

        Visible = false;
    }

    /// <summary>Shows the tutorial and resets the shared progress.</summary>
    public void Begin()
    {
        _localDone = false;
        _holdTime = 0f;
        _finished = false;

        _handler?.Call("reset_tutorial");
        SetLocalState("Press and hold V to use the voice chat.");
        Visible = true;
        RefreshProgress();
    }

    public override void _Process(double delta)
    {
        if (!Visible || _localDone || _finished)
            return;

        if (Input.IsActionPressed("voice_chat"))
        {
            _holdTime += (float)delta;
            if (_holdTime >= HoldSeconds)
                CompleteLocalStep();
        }
        else
        {
            // Decays, so a quick tap does not count as talking.
            _holdTime = Mathf.Max(0f, _holdTime - ((float)delta * 2f));
        }
    }

    private void CompleteLocalStep()
    {
        _localDone = true;
        _handler?.Call("mark_local_tutorial_ready");
        SetLocalState("Voice chat checked. Waiting for the other players...");
        RefreshProgress();
    }

    private void OnSkipPressed()
    {
        // Host only: marks the tutorial as done for everyone.
        _handler?.Call("host_skip_tutorial");
    }

    private void OnProgressChanged(int readyCount, int totalCount)
    {
        if (_progressLabel != null)
            _progressLabel.Text = $"{readyCount} of {totalCount} players ready";
    }

    private void OnTutorialFinished()
    {
        if (_finished)
            return;

        _finished = true;
        Visible = false;
        EmitSignal(SignalName.TutorialFinished);
    }

    private void SetLocalState(string text)
    {
        if (_localState != null)
            _localState.Text = text;
    }

    private void RefreshProgress()
    {
        if (_handler == null)
            return;

        OnProgressChanged(
            _handler.Call("get_tutorial_ready_count").AsInt32(),
            _handler.Call("get_tutorial_total_count").AsInt32());
    }

    private bool IsHost() => _handler?.Call("is_host").AsBool() ?? true;
}
