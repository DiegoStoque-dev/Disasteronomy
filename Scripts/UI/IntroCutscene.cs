#nullable enable
using Godot;

/// <summary>
/// Plays a video and then continues the flow.
///
/// Used by:
///   Intro.tscn      - logo intro, auto advances to the main menu.
///   Cutscene1.tscn  - first story cutscene + voice chat tutorial.
///   Cutscene2.tscn  - second story cutscene + objectives (TAB) tutorial.
///   Cutscene3.tscn  - third story cutscene, stops at the end.
///
/// A configured tutorial takes priority over the normal auto advance and owns
/// the transition to the next scene.
/// </summary>
public partial class IntroCutscene : Control
{
    /// <summary>Scene opened after the video when there is no tutorial.</summary>
    [Export] public string NextScenePath = "res://Scenes/MainMenu.tscn";

    /// <summary>When true, any key / mouse click skips the video.</summary>
    [Export] public bool AllowSkip = true;

    /// <summary>When true, changing to NextScenePath happens as soon as the video ends.</summary>
    [Export] public bool AutoAdvance = true;

    /// <summary>Reusable skip-button scene shown over the video (optional).</summary>
    [Export] public PackedScene SkipButtonScene = GD.Load<PackedScene>("res://Scenes/UI/IntroSkipButton.tscn");
    /// <summary>Whether to add the on-screen skip button. Set false on cutscenes that should not show it.</summary>
    [Export] public bool ShowSkipButton = true;

    /// <summary>Optional voice chat tutorial (shown when the video ends).</summary>
    [Export] public NodePath VoiceTutorialPath = new("");

    /// <summary>Optional objectives (TAB) tutorial (shown when the video ends).</summary>
    [Export] public NodePath TabTutorialPath = new("");

    /// <summary>NetSession exit-barrier name for this scene. Empty = derived from the scene node name.</summary>
    [Export] public string ExitBarrierName = "";

    private VideoStreamPlayer? _video;
    private VoiceTutorial? _voiceTutorial;
    private TabTutorial? _tabTutorial;
    private IntroSkipButton? _skipButton;
    private bool _finished;
    private bool _tutorialShown;

    private bool HasPendingTutorial =>
        !_tutorialShown && (_voiceTutorial != null || _tabTutorial != null);

    private bool TutorialVisible =>
        (_voiceTutorial != null && _voiceTutorial.Visible) ||
        (_tabTutorial != null && _tabTutorial.Visible);

    public override void _Ready()
    {
        _video = GetNodeOrNull<VideoStreamPlayer>("Video");

        if (!string.IsNullOrEmpty(VoiceTutorialPath.ToString()))
        {
            _voiceTutorial = GetNodeOrNull<VoiceTutorial>(VoiceTutorialPath);
            if (_voiceTutorial == null)
                GD.PushError("IntroCutscene: voice tutorial node not found.");
            else
                _voiceTutorial.Visible = false;
        }

        if (!string.IsNullOrEmpty(TabTutorialPath.ToString()))
        {
            _tabTutorial = GetNodeOrNull<TabTutorial>(TabTutorialPath);
            if (_tabTutorial == null)
                GD.PushError("IntroCutscene: tab tutorial node not found.");
            else
                _tabTutorial.Visible = false;
        }

        if (_video == null)
        {
            GD.PrintErr("IntroCutscene: VideoStreamPlayer 'Video' not found.");
            GoToNextScene();
            return;
        }

        _video.Finished += OnVideoFinished;
        _video.Play();

        // Show a visible skip hint (bottom-right, fades in after a delay).
        // Only added when skipping is allowed, so tutorial cutscenes can opt out via ShowSkipButton.
        if (ShowSkipButton && AllowSkip && SkipButtonScene != null)
        {
            _skipButton = SkipButtonScene.Instantiate<IntroSkipButton>();
            AddChild(_skipButton);
            _skipButton.Pressed += SkipIntro;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!AllowSkip || _finished)
            return;

        // While a tutorial is on screen the keyboard belongs to it (V and TAB are
        // part of the tutorials), so the cutscene must not skip ahead.
        if (TutorialVisible)
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
        SkipIntro();
    }

    /// <summary>
    /// Shared skip entry point used by both the Enter/key/mouse handler and the on-screen Skip button,
    /// so there is a single place with the skip logic (no duplication).
    /// </summary>
    private void SkipIntro()
    {
        if (!AllowSkip || _finished)
            return;

        // Skipping the video still leads to the tutorial when there is one,
        // so the tutorial step is never silently bypassed.
        if (HasPendingTutorial)
        {
            ShowTutorial();
            return;
        }

        GoToNextScene();
    }

    private void OnVideoFinished()
    {
        GD.Print("[IntroCutscene] Video finished.");

        if (HasPendingTutorial)
        {
            ShowTutorial();
            return;
        }

        if (!AutoAdvance)
        {
            GD.Print("[IntroCutscene] Auto advance disabled, waiting here.");
            return;
        }

        GoToNextScene();
    }

    /// <summary>Pauses the video and hands over to the configured tutorial.</summary>
    private void ShowTutorial()
    {
        if (_tutorialShown)
            return;

        _tutorialShown = true;

        if (_video != null)
            _video.Paused = true;

        if (_voiceTutorial != null)
        {
            GD.Print("[IntroCutscene] Handing over to the voice chat tutorial.");
            _voiceTutorial.Begin();
            return;
        }

        if (_tabTutorial != null)
        {
            GD.Print("[IntroCutscene] Handing over to the objectives tutorial.");
            _tabTutorial.Begin();
        }
    }

    /// <summary>Changes to the next scene using the shared fade transition.</summary>
    private void GoToNextScene()
    {
        if (_finished)
            return;

        _finished = true;

        if (_video != null)
            _video.Paused = true;

        GD.Print($"[IntroCutscene] Going to '{NextScenePath}'.");

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
}
