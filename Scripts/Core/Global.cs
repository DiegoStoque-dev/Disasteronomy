using Godot;
using System;
using System.IO;

public partial class Global : Node
{
    private const string SAVE_FILE = "user://disasteronomy_save.dat";

    // --- Singleton pattern ---
    public static Global Instance { get; private set; }

    // --- Video settings ---
    public static readonly Vector2I[] AvailableResolutions =
    {
        new Vector2I(1152, 648),
        new Vector2I(1280, 720),
        new Vector2I(1600, 900),
        new Vector2I(1920, 1080),
        new Vector2I(2560, 1440)
    };

    public bool IsFullscreen { get; private set; } = false;
    public Vector2I WindowResolution { get; private set; } = new Vector2I(1152, 648);

    /// <summary>Menu music volume, 0..1.</summary>
    public float MusicVolume { get; private set; } = 0.7f;

    // Remappable input actions (must match the Input Map in project.godot).
    public static readonly string[] RemappableActions =
    {
        "move_left", "move_right", "jump", "dash", "run", "voice_chat"
    };

    private const string SettingsPath = "user://settings.cfg";

    public override void _Ready()
    {
        Instance = this;
        LoadVideoSettings();
        LoadAudioSettings();
        LoadKeyBindings();
    }

    /// <summary>
    /// Checks if a save file exists.
    /// </summary>
    public bool HasSave()
    {
        return Godot.FileAccess.FileExists(SAVE_FILE);
    }

    /// <summary>
    /// Saves game data (reset for new game or current progress).
    /// </summary>
    /// <summary>
    /// When true the next loaded game scene ignores the stored player position
    /// and starts a fresh run (used by the "New Game" button).
    /// </summary>
    public bool NewRunRequested { get; set; } = false;

    /// <summary>
    /// Last player position persisted to disk. Only valid when <see cref="HasPlayerSave"/> is true.
    /// </summary>
    public Vector2 SavedPlayerPosition { get; private set; } = Vector2.Zero;
    public bool HasPlayerSave { get; private set; } = false;

    // --- Level 1 progress (in memory only; never persisted, reset for a new match) ---
    public bool Level1Completed { get; set; } = false;
    public float Level1LastTime { get; set; } = 0f;
    public int Level1LastStrikes { get; set; } = 0;
    public bool Level1LastRuptured { get; set; } = false;
    public int Level1LastSafer { get; set; } = 0;

    // --- Level 2 progress (in memory only) ---
    public bool Level2Completed { get; set; } = false;

    /// <summary>Clears the in-memory Level 1 report. Called when a new match starts.</summary>
    public void ResetProgress()
    {
        Level1Completed = false;
        Level1LastTime = 0f;
        Level1LastStrikes = 0;
        Level1LastRuptured = false;
        Level1LastSafer = 0;
        Level2Completed = false;
    }

    private const string SaveHeader = "DISASTERONOMY_SAVE_V1";
    private const int SaveVersion = 1;

    /// <summary>Saves the current game progress (player position).</summary>
    public void Save()
    {
        Save(LastKnownPlayerPosition);
    }

    /// <summary>Player position kept in memory so Save() without arguments still works.</summary>
    public Vector2 LastKnownPlayerPosition { get; set; } = Vector2.Zero;

    /// <summary>Saves game progress with an explicit player position.</summary>
    public void Save(Vector2 playerPosition)
    {
        try
        {
            LastKnownPlayerPosition = playerPosition;
            SavedPlayerPosition = playerPosition;
            HasPlayerSave = true;

            using var file = Godot.FileAccess.Open(SAVE_FILE, Godot.FileAccess.ModeFlags.Write);
            if (file == null)
            {
                GD.PrintErr("[Global] Could not open save file for writing: " + Godot.FileAccess.GetOpenError());
                return;
            }

            file.StoreString(SaveHeader);
            file.Store64((ulong)Time.GetUnixTimeFromSystem());
            file.Store32(SaveVersion);
            file.StoreFloat(playerPosition.X);
            file.StoreFloat(playerPosition.Y);
            GD.Print("[Global] Save saved at: " + SAVE_FILE);
        }
        catch (Exception e)
        {
            GD.PrintErr("[Global] Error saving: " + e.Message);
        }
    }

    /// <summary>
    /// Loads existing save data.
    /// </summary>
    public void Load()
    {
        if (!HasSave())
        {
            GD.PrintErr("[Global] No save found to load.");
            return;
        }

        try
        {
            using var file = Godot.FileAccess.Open(SAVE_FILE, Godot.FileAccess.ModeFlags.Read);
            if (file == null)
            {
                GD.PrintErr("[Global] Could not open save file for reading: " + Godot.FileAccess.GetOpenError());
                return;
            }

            file.Get64(); // timestamp
            var version = file.Get32();
            if (version >= 1)
            {
                float x = file.GetFloat();
                float y = file.GetFloat();
                SavedPlayerPosition = new Vector2(x, y);
                LastKnownPlayerPosition = SavedPlayerPosition;
                HasPlayerSave = true;
            }
            GD.Print($"[Global] Save loaded: version {version}, player at {SavedPlayerPosition}");
        }
        catch (Exception e)
        {
            GD.PrintErr("[Global] Error loading save: " + e.Message);
        }
    }

    // --- Video: fullscreen / resolution ---

    /// <summary>Toggle fullscreen and save.</summary>
    public void SetFullscreen(bool fullscreen)
    {
        IsFullscreen = fullscreen;
        DisplayServer.WindowSetMode(fullscreen
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);

        // When returning to window, reapply chosen resolution and center
        if (!fullscreen)
        {
            ApplyResolution();
        }
        SaveSettings();
    }

    /// <summary>Set window resolution. In fullscreen, only store for later.</summary>
    public void SetResolution(Vector2I resolution)
    {
        WindowResolution = resolution;
        SaveSettings();
        if (!IsFullscreen)
        {
            ApplyResolution();
        }
    }

    private void ApplyResolution()
    {
        DisplayServer.WindowSetSize(WindowResolution);
        // Center window on current screen
        var screenSize = DisplayServer.ScreenGetSize();
        var centeredPos = (screenSize - (Vector2I)WindowResolution) / 2;
        DisplayServer.WindowSetPosition(centeredPos);
    }

    // Load video settings from settings.cfg and apply on boot.
    private void LoadVideoSettings()
    {
        ConfigFile cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) != Error.Ok) { return; }

        IsFullscreen = (bool)cfg.GetValue("video", "fullscreen", false);

        string resolutionText = (string)cfg.GetValue("video", "resolution", "1152x648");
        foreach (Vector2I r in AvailableResolutions)
        {
            if ($"{r.X}x{r.Y}" == resolutionText)
            {
                WindowResolution = r;
                break;
            }
        }

        // With a small delay, to ensure main window exists
        Callable.From(ApplyVideoOnBoot).CallDeferred();
    }

    private void ApplyVideoOnBoot()
    {
        if (IsFullscreen)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        }
        else
        {
            ApplyResolution();
        }
    }

    private void SaveSettings()
    {
        ConfigFile cfg = new ConfigFile();
        cfg.Load(SettingsPath); // fail silently if not exists yet
        cfg.SetValue("video", "fullscreen", IsFullscreen);
        cfg.SetValue("video", "resolution", $"{WindowResolution.X}x{WindowResolution.Y}");
        cfg.SetValue("audio", "music_volume", MusicVolume);
        cfg.Save(SettingsPath);
    }

    // --- Audio ---

    /// <summary>Sets the menu music volume (0..1), saves it and applies it right away.</summary>
    public void SetMusicVolume(float volume)
    {
        MusicVolume = Mathf.Clamp(volume, 0f, 1f);
        SaveSettings();
        MusicManager.Instance?.ApplyVolume();
    }

    /// <summary>Reads the saved music volume from settings.cfg.</summary>
    private void LoadAudioSettings()
    {
        ConfigFile cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) != Error.Ok) { return; }

        MusicVolume = Mathf.Clamp((float)cfg.GetValue("audio", "music_volume", 0.7f), 0f, 1f);
    }

    // --- Objectives (the stage goal shown by the TAB overlay) ---

    [Signal]
    public delegate void ObjectiveChangedEventHandler();

    /// <summary>Goal of the current stage. Shown by the objectives overlay (TAB).</summary>
    public string CurrentObjective { get; private set; } = "";

    /// <summary>Sets the stage goal and notifies every listening HUD.</summary>
    public void SetObjective(string objective)
    {
        CurrentObjective = objective ?? "";
        EmitSignal(SignalName.ObjectiveChanged);
    }

    /// <summary>Removes the current goal (between stages).</summary>
    public void ClearObjective()
    {
        SetObjective("");
    }

    // --- Input key remapping (from Sazsazan) ---

    /// <summary>
    /// Returns the friendly name of the current key for an action.
    /// Prefers keyboard events; if action has only mouse/gamepad, describe the button.
    /// </summary>
    public string GetActionKeyText(string action)
    {
        InputEventMouseButton mouse = null;

        foreach (InputEvent ev in InputMap.ActionGetEvents(action))
        {
            if (ev is InputEventKey key)
            {
                return key.PhysicalKeycode != Key.None
                    ? key.AsTextPhysicalKeycode()
                    : key.AsText();
            }
            if (ev is InputEventMouseButton mb)
            {
                mouse = mb;
            }
        }

        if (mouse != null)
        {
            return mouse.ButtonIndex == MouseButton.Left
                ? "Left Mouse Button"
                : $"Mouse Button {(int)mouse.ButtonIndex}";
        }

        return action;
    }

    /// <summary>
    /// Changes the key for an action (keeps any mouse/gamepad events present),
    /// applies to InputMap and saves to user://settings.cfg.
    /// </summary>
    public void SetActionKey(string action, Key key)
    {
        if (!InputMap.HasAction(action)) { return; }

        // Remove only KEYBOARD events from the action
        foreach (InputEvent ev in InputMap.ActionGetEvents(action))
        {
            if (ev is InputEventKey)
            {
                InputMap.ActionEraseEvent(action, ev);
            }
        }

        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });

        ConfigFile cfg = new ConfigFile();
        cfg.Load(SettingsPath); // fail silently if not exists yet
        cfg.SetValue("input", action, (long)key);
        cfg.Save(SettingsPath);
    }

    /// <summary>Apply saved keys from settings.cfg (called on boot).</summary>
    private void LoadKeyBindings()
    {
        ConfigFile cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) != Error.Ok) { return; }

        foreach (string action in RemappableActions)
        {
            if (!cfg.HasSectionKey("input", action)) { continue; }

            Key key = (Key)(long)cfg.GetValue("input", action, 0L);
            if (key == Key.None) { continue; }

            foreach (InputEvent ev in InputMap.ActionGetEvents(action))
            {
                if (ev is InputEventKey)
                {
                    InputMap.ActionEraseEvent(action, ev);
                }
            }
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }
    }
}