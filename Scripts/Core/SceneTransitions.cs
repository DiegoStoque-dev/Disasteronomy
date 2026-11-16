using Godot;
using System.Threading.Tasks;

/// <summary>
/// Autoload (singleton) responsible for smooth transitions (fade) between scenes.
/// Registered in project.godot in the [autoload] section.
/// </summary>
public partial class SceneTransitions : CanvasLayer
{
    public static SceneTransitions Instance { get; private set; }

    /// <summary>Path of the main menu scene (used by screens that return to it).</summary>
    public const string MainMenuScenePath = "res://Scenes/MainMenu.tscn";

    private ColorRect _fadeRect;
    private bool _transitioning;

    public override void _Ready()
    {
        Instance = this;
        Layer = 100; // Always on top of everything
        ProcessMode = Node.ProcessModeEnum.Always; // Works even when game paused

        _fadeRect = new ColorRect
        {
            Color = new Color(0, 0, 0, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            Visible = false,
        };
        AddChild(_fadeRect);
    }

    /// <summary>
    /// Change scene with fade to black and back.
    /// </summary>
    public void ChangeScene(string scenePath, float fadeTime = 0.35f)
    {
        if (_transitioning || string.IsNullOrEmpty(scenePath))
        {
            return;
        }
        _transitioning = true;
        _ = RunTransition(scenePath, fadeTime);
    }

    /// <summary>
    /// Returns to the main menu with a fade.
    /// Parameterless on purpose: GDScript can call this reliably with call("GoToMainMenu").
    /// </summary>
    public void GoToMainMenu()
    {
        ChangeScene(MainMenuScenePath);
    }

    private async Task RunTransition(string scenePath, float fadeTime)
    {
        // Block clicks during fade
        _fadeRect.MouseFilter = Control.MouseFilterEnum.Stop;
        _fadeRect.Visible = true;

        Tween fadeIn = CreateTween();
        fadeIn.TweenProperty(_fadeRect, "color:a", 1.0f, fadeTime);
        await ToSignal(fadeIn, Tween.SignalName.Finished);

        GetTree().ChangeSceneToFile(scenePath);

        Tween fadeOut = CreateTween();
        fadeOut.TweenProperty(_fadeRect, "color:a", 0.0f, fadeTime);
        await ToSignal(fadeOut, Tween.SignalName.Finished);

        _fadeRect.Visible = false;
        _fadeRect.MouseFilter = Control.MouseFilterEnum.Ignore;
        _transitioning = false;
    }
}