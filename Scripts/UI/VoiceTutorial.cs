#nullable enable
using Godot;

/// <summary>
/// Voice chat tutorial shown at the end of cutscene 2.
/// The cutscene stays paused until every connected player uses the voice chat
/// (by holding the voice_chat action, V by default).
/// The host can also skip the whole step for everyone.
///
/// The tutorial changes the scene itself (NextScenePath), so the flow always
/// continues even if the parent cutscene is not listening to the signal.
/// </summary>
public partial class VoiceTutorial : Control
{
    [Signal]
    public delegate void TutorialFinishedEventHandler();

    private const string HandlerPath = "/root/HighLevelNetworkHandler";

    /// <summary>How long the key must be held to count as "using" the voice chat.</summary>
    private const float HoldSeconds = 0.5f;

    /// <summary>Scene opened once everybody finished (or the host skipped).</summary>
    [Export] public string NextScenePath = "";

    /// <summary>NetSession exit-barrier name for the cutscene that hosts this tutorial.</summary>
    [Export] public string ExitBarrierName = "";

    private Node? _handler;
    private Label? _localState;
    private Label? _progressLabel;
    private Label? _holdProgress;
    private Button? _skipButton;

    private bool _localDone;
    private float _holdTime;
    private bool _finished;

    public override void _Ready()
    {
        _handler = GetNodeOrNull(HandlerPath);

        _localState = GetNodeOrNull<Label>("Center/Panel/VBox/LocalState");
        _progressLabel = GetNodeOrNull<Label>("Center/Panel/VBox/ProgressLabel");
        _holdProgress = GetNodeOrNull<Label>("Center/Panel/VBox/HoldProgress");
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
        else
        {
            GD.PushError("VoiceTutorial: HighLevelNetworkHandler autoload not found.");
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
        SetHoldProgress("");
        Visible = true;
        RefreshProgress();
        GD.Print("[VoiceTutorial] Started.");
    }

    public override void _Process(double delta)
    {
        if (!Visible || _localDone || _finished)
            return;

        if (Input.IsActionPressed("voice_chat"))
        {
            _holdTime += (float)delta;
            float ratio = Mathf.Clamp(_holdTime / HoldSeconds, 0f, 1f);
            SetHoldProgress($"Holding V... {Mathf.RoundToInt(ratio * 100f)}%");

            if (_holdTime >= HoldSeconds)
                CompleteLocalStep();
        }
        else
        {
            // Decays, so a quick tap does not count as talking.
            _holdTime = Mathf.Max(0f, _holdTime - ((float)delta * 2f));
            SetHoldProgress(_holdTime > 0f ? "Keep holding V..." : string.Empty);
        }
    }

    private void CompleteLocalStep()
    {
        _localDone = true;
        SetLocalState("Voice chat checked. Waiting for the other players...");
        SetHoldProgress(string.Empty);
        _handler?.Call("mark_local_tutorial_ready");
        RefreshProgress();
        GD.Print("[VoiceTutorial] Local step complete.");

        // Solo, or host without other players: make sure the flow continues.
        if (IsHost() && PlayerCount() <= 1)
            _handler?.Call("host_skip_tutorial");
    }

    private void OnSkipPressed()
    {
        // Host only: marks the tutorial as done for everyone.
        GD.Print("[VoiceTutorial] Host skipped the tutorial.");
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
        GD.Print("[VoiceTutorial] Tutorial finished, continuing.");

        EmitSignal(SignalName.TutorialFinished);
        GoToNextScene();
    }

    /// <summary>Continues the flow (cutscene 1 by default).</summary>
    private void GoToNextScene()
    {
        if (string.IsNullOrEmpty(NextScenePath))
        {
            GD.PushWarning("VoiceTutorial: NextScenePath is empty, the flow cannot continue.");
            return;
        }

        string exitBarrier = ExitBarrierName;
        if (string.IsNullOrEmpty(exitBarrier))
        {
            string? sceneName = GetTree().CurrentScene?.Name;
            exitBarrier = ((string.IsNullOrEmpty(sceneName) ? "Cutscene" : sceneName) + "_Exit");
        }

        if (SceneTransitions.Instance != null)
            SceneTransitions.Instance.ChangeSceneSynchronized(NextScenePath, exitBarrier);
        else
            GetTree().ChangeSceneToFile(NextScenePath);
    }

    private void SetLocalState(string text)
    {
        if (_localState != null)
            _localState.Text = text;
    }

    private void SetHoldProgress(string text)
    {
        if (_holdProgress != null)
            _holdProgress.Text = text;
    }

    private void RefreshProgress()
    {
        if (_handler == null)
            return;

        OnProgressChanged(PlayerCount(), TotalPlayerCount());
    }

    private int PlayerCount() => _handler?.Call("get_tutorial_ready_count").AsInt32() ?? 0;

    private int TotalPlayerCount() => _handler?.Call("get_tutorial_total_count").AsInt32() ?? 1;

    private bool IsHost() => _handler?.Call("is_host").AsBool() ?? true;
}
