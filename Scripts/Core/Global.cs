using Godot;
using System;
using System.IO;

public partial class Global : Node
{
    private const string SAVE_FILE = "user://disasteronomy_save.dat";

    public override void _Ready()
    {
        // Global game variables can be initialized here
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
    public void Save()
    {
        try
        {
            Godot.DirAccess.MakeDirAbsolute(Path.GetDirectoryName(SAVE_FILE));
            using var file = Godot.FileAccess.Open(SAVE_FILE, Godot.FileAccess.ModeFlags.Write);
            file.StoreString("DISASTERONOMY_SAVE_V1");
            file.Store64((ulong)Time.GetUnixTimeFromSystem());
            file.Store32(1); // save version
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
            file.Get64(); // timestamp
            var version = file.Get32();
            GD.Print($"[Global] Save loaded: version {version}");
        }
        catch (Exception e)
        {
            GD.PrintErr("[Global] Error loading save: " + e.Message);
        }
    }
}