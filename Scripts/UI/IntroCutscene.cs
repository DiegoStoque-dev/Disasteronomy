#nullable enable
using Godot;

/// <summary>
/// Plays a video and then continues the flow.
///
/// Used by:
///   Intro.tscn      - logo intro, auto advances to the main menu.
///   Cutscene1.tscn  - story cutscene, stops at the end (flow continues elsewhere).
///   Cutscene2.tscn  - story cutscene that pauses for the voice chat tutorial and
///                     then continues to Cutscene 1.
/// </summary>
public partial class IntroCutscene : Control
{
    /// <summary>Scene opened after the video (or after the tutorial, when there is one).</summary>
    [Export] public string NextScenePath = "res://Scenes/MainMenu.tscn";

    /// <summary>When true, any key / mouse click skips the video.</summary>
    [Export] public bool AllowSkip = true;

    /// <summary>When true, changing to NextScenePath happens as soon as the video ends.</summary>
    [Export] public bool AutoAdvance = true;

    /// <summary>
    /// Voice tutorial overlay (optional). When set, the video pauses for the
    /// tutorial before the flow continues.
    /// </summary>
    [Export] public NodePath TutorialPath = new("");

    /// <summary>
    /// Second at which the video pauses to show the tutorial.
    /// 0 (default) waits for the end of the video instead.
    /// </summary>
    [Export] public float TutorialAtSeconds = 0f;

    private VideoStreamPlayer? _video;
    private VoiceTutorial? _tutorial;
    private bool _finished;
    private bool _tutorialShown;

    public override void _Ready()
    {
        _video = GetNodeOrNull<VideoStreamPlayer>("Video");

        if (!string.IsNullOrEmpty(TutorialPath.ToString()))
        {
            _tutorial = GetNodeOrNull<VoiceTutorial>(TutorialPath);
            if (_tutorial != null)
            {
                _tutorial.Visible = false;
                _tutorial.TutorialFinished += GoToNextScene;
            }
            else
            {
                GD.PushError("IntroCutscene: tutorial node not found.");
            }
        }

        if (_video == null)
        {
            GD.PrintErr("IntroCutscene: VideoStreamPlayer 'Video' not found.");
            GoToNextScene();
            return;
        }

        _video.Finished += OnVideoFinished;
        _video.Play();
    }

    public override void _Process(double delta)
    {
        if (_tutorial == null || _tutorialShown || _finished || _video == null)
            return;
        if (TutorialAtSeconds <= 0f || !_video.IsPlaying())
            return;

        if (_video.StreamPosition >= TutorialAtSeconds)
            ShowTutorial();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!AllowSkip || _finished)
            return;

        bool pressed = @event switch
        {
            InputEventKey key => key.Pressed,
            InputEventMouseButton mouse => mouse.Pressed,
            InputEventJoypadButton pad => pad.Pressed,
            _ => false,
        };

        if (!pressed)
            return;

        GetViewport().SetInputAsHandled();

        // Skipping the video still leads to the tutorial when there is one,
        // so the tutorial step is never silently bypassed.
        if (_tutorial != null && !_tutorialShown)
        {
            ShowTutorial();
            return;
        }

        GoToNextScene();
    }

    private void OnVideoFinished()
    {
        // A tutorial takes priority over the normal auto advance.
        if (_tutorial != null && !_tutorialShown)
        {
            ShowTutorial();
            return;
        }

        if (!AutoAdvance)
        {
            GD.Print("IntroCutscene: video finished (auto advance disabled).");
            return;
        }

        GoToNextScene();
    }

    /// <summary>Pauses the video and hands over to the voice chat tutorial.</summary>
    private void ShowTutorial()
    {
        if (_tutorial == null || _tutorialShown)
            return;

        _tutorialShown = true;

        if (_video != null)
            _video.Paused = true;

        _tutorial.Begin();
    }

    /// <summary>Changes to the next scene using the shared fade transition.</summary>
    private void GoToNextScene()
    {
        if (_finished)
            return;

        _finished = true;

        if (_video != null)
            _video.Paused = true;

        if (SceneTransitions.Instance != null)
            SceneTransitions.Instance.ChangeScene(NextScenePath);
        else
            GetTree().ChangeSceneToFile(NextScenePath);
    }
}
