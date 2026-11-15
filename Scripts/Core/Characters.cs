#nullable enable
using Godot;

/// <summary>Playable characters. The order also defines the player color.</summary>
public enum CharacterId
{
    Ana = 0,
    Lucy = 1,
    Morgana = 2,
    Roger = 3,
}

/// <summary>
/// Static catalog of the playable characters: display name, identification color
/// (Red = Ana, Purple = Lucy, Blue = Morgana, Green = Roger) and the idle sprite sheet.
/// </summary>
public static class Characters
{
    public const int Count = 4;

    /// <summary>Frame size of every character sheet (8 frames per row).</summary>
    public const int FrameWidth = 32;
    public const int FrameHeight = 34;
    public const int FrameCount = 8;
    public const float IdleFps = 6f;

    private static readonly string[] Names = { "Ana", "Lucy", "Morgana", "Roger" };

    private static readonly Color[] IdColors =
    {
        new Color(0.90f, 0.20f, 0.20f), // Ana    - vermelho
        new Color(0.60f, 0.30f, 0.90f), // Lucy   - roxo
        new Color(0.25f, 0.50f, 0.95f), // Morgana - azul
        new Color(0.25f, 0.80f, 0.35f), // Roger  - verde
    };

    /// <summary>Idle sheets live under Sprites/Traje_Protecao/, one folder per character.</summary>
    private static readonly string[] IdleSheetPaths =
    {
        "res://Sprites/Traje_Protecao/Traje_Protecao_Ana/Idle/Ana_Idle.png",
        "res://Sprites/Traje_Protecao/Traje_Protecao_Lucy/Idle/Lucy_Idle.png",
        "res://Sprites/Traje_Protecao/Traje_Protecao_Morgana/Idle/Morgana_Idle.png",
        "res://Sprites/Traje_Protecao/Traje_Protecao_Roger/Idle/Roger_Idle.png",
    };

    /// <summary>"walk" sheets (filename casing varies per character, e.g. Roger_Walk).</summary>
    private static readonly string[] WalkSheetPaths =
    {
        "res://Sprites/Traje_Protecao/Traje_Protecao_Ana/Walk/Ana_walk.png",
        "res://Sprites/Traje_Protecao/Traje_Protecao_Lucy/Walk/Lucy_walk.png",
        "res://Sprites/Traje_Protecao/Traje_Protecao_Morgana/Walk/Morgana_walk.png",
        "res://Sprites/Traje_Protecao/Traje_Protecao_Roger/Walk/Roger_Walk.png",
    };

    /// <summary>"jump" sheets (casing varies, e.g. Morgana_Jump and Roger_Jump).</summary>
    private static readonly string[] JumpSheetPaths =
    {
        "res://Sprites/Traje_Protecao/Traje_Protecao_Ana/Jump/Ana_jump.png",
        "res://Sprites/Traje_Protecao/Traje_Protecao_Lucy/Jump/Lucy_jump.png",
        "res://Sprites/Traje_Protecao/Traje_Protecao_Morgana/Jump/Morgana_Jump.png",
        "res://Sprites/Traje_Protecao/Traje_Protecao_Roger/Jump/Roger_Jump.png",
    };

    public static bool IsValid(int index) => index >= 0 && index < Count;

    public static string GetName(int index) =>
        IsValid(index) ? Names[index] : "?";

    public static Color GetColor(int index) =>
        IsValid(index) ? IdColors[index] : new Color(0.5f, 0.5f, 0.5f);

    /// <summary>First character that no one in <paramref name="taken"/> is using, or -1.</summary>
    public static int FirstFreeCharacter(System.Collections.Generic.ICollection<int> taken)
    {
        for (int i = 0; i < Count; i++)
        {
            if (!taken.Contains(i))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Builds the "idle" SpriteFrames for a character by slicing its sheet
    /// (256x34 = 8 frames of 32x34), the same way Player.cs builds jump frames.
    /// </summary>
    public static SpriteFrames? BuildIdleFrames(int characterIndex)
    {
        if (!IsValid(characterIndex))
            return null;

        SpriteFrames? frames = SpriteSheetUtil.BuildFrames(
            IdleSheetPaths[characterIndex],
            FrameWidth,
            FrameHeight,
            IdleFps,
            "idle",
            loop: true);

        if (frames == null)
            GD.PushError($"Characters: idle sheet not found for {GetName(characterIndex)}.");

        return frames;
    }

    /// <summary>"walk" frames for a character (8 frames per row, 32x34 each).</summary>
    public static SpriteFrames? BuildWalkFrames(int characterIndex)
    {
        if (!IsValid(characterIndex))
            return null;

        SpriteFrames? frames = SpriteSheetUtil.BuildFrames(
            WalkSheetPaths[characterIndex],
            FrameWidth,
            FrameHeight,
            8f,
            "walk",
            loop: true);

        if (frames == null)
            GD.PushError($"Characters: walk sheet not found for {GetName(characterIndex)}.");

        return frames;
    }

    /// <summary>"jump" frames for a character (used for the upward/vertical impulse).</summary>
    public static SpriteFrames? BuildJumpFrames(int characterIndex)
    {
        if (!IsValid(characterIndex))
            return null;

        SpriteFrames? frames = SpriteSheetUtil.BuildFrames(
            JumpSheetPaths[characterIndex],
            FrameWidth,
            FrameHeight,
            8f,
            "jump",
            loop: true);

        if (frames == null)
            GD.PushError($"Characters: jump sheet not found for {GetName(characterIndex)}.");

        return frames;
    }
}
