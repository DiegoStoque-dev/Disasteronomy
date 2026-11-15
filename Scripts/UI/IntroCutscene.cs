#nullable enable
using Godot;

/// <summary>
/// Plays the game logo intro video and then fades into the main menu.
/// This scene is set as the project main scene (project.godot -> run/main_scene),
/// so the game always boots on the intro.
/// </summary>
public partial class IntroCutscene : Control
{
    /// <summary>Scene opened after the video ends.</summary>
    [Export] public string NextScenePath = "res://Scenes/MainMenu.tscn";

    /// <summary>When true, any key / mouse click skips the intro.</summary>
    [Export] public bool AllowSkip = true;

    /// <summary>When true, changing to NextScenePath happens as soon as the video ends.</summary>
    [Export] public bool AutoAdvance = true;

    private VideoStreamPlayer? _video;
    private bool _finished;

    public override void _Ready()
    {
        _video = GetNodeOrNull<VideoStreamPlayer>("Video");
        if (_video == null)
        {
            GD.PrintErr("IntroCutscene: VideoStreamPlayer 'Video' not found, skipping to menu.");
            GoToNextScene();
            return;
        }

        _video.Finished += OnVideoFinished;
        _video.Play();
    }

    private void OnVideoFinished()
    {
        if (!AutoAdvance)
        {
            // Used by story cutscenes: the flow continues elsewhere.
            GD.Print("IntroCutscene: video finished (auto advance disabled).");
            return;
        }

        GoToNextScene();
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
        GoToNextScene();
    }

    /// <summary>Changes to the main menu using the shared fade transition.</summary>
    private void GoToNextScene()
    {
        if (_finished)
            return;

        _finished = true;

        if (SceneTransitions.Instance != null)
            SceneTransitions.Instance.ChangeScene(NextScenePath);
        else
            GetTree().ChangeSceneToFile(NextScenePath);
    }
}
