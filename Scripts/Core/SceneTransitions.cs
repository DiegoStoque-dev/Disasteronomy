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
    private string _pendingSyncPath = "";
    private string _pendingSyncBarrier = "";
    private Callable _syncConnectCallable;
    private bool _syncConnected;

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
    /// Synchronized scene change used by the multiplayer flow. Every peer calls it
    /// once to confirm it finished the current scene (an exit barrier handled by the
    /// NetSession autoload). When every participant confirms, the all_ready signal
    /// is emitted on every device and each peer fades to the next scene. In solo the
    /// barrier resolves immediately and the scene changes right away.
    /// </summary>
    public void ChangeSceneSynchronized(string scenePath, string exitBarrier)
    {
        if (_transitioning || string.IsNullOrEmpty(scenePath))
        {
            return;
        }

        _pendingSyncPath = scenePath;
        _pendingSyncBarrier = exitBarrier;

        Node handler = GetNodeOrNull("/root/HighLevelNetworkHandler");
        if (handler == null)
        {
            // No session autoload: fall back to a plain transition.
            ChangeScene(scenePath);
            return;
        }

        // Connect BEFORE marking ready and check the barrier name (no blind OneShot).
        _syncConnectCallable = Callable.From<string>(OnSyncAllReady);
        _syncConnected = true;
        handler.Connect("all_ready", _syncConnectCallable);
        handler.Call("mark_ready", exitBarrier);
    }

    private void OnSyncAllReady(string barrierName)
    {
        if (barrierName != _pendingSyncBarrier)
        {
            return;
        }

        // Single unlock per transition: stop listening, then change the scene.
        _pendingSyncBarrier = string.Empty;
        Node handler = GetNodeOrNull("/root/HighLevelNetworkHandler");
        if (_syncConnected && handler != null && handler.IsConnected("all_ready", _syncConnectCallable))
        {
            handler.Disconnect("all_ready", _syncConnectCallable);
        }
        _syncConnected = false;
        _syncConnectCallable = default;

        ChangeScene(_pendingSyncPath);
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