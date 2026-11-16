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

    /// <summary>Suit variant used to build the sprite frames.</summary>
    public enum Suit { Standard, Protection }

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

    /// <summary>Standard (Traje_Padrao) idle sheets, one per character (_right facing).</summary>
    private static readonly string[] IdleStandardPaths =
    {
        "res://Sprites/Traje_Padrao/Ana/Ana_idle_right.png",
        "res://Sprites/Traje_Padrao/Lucy/Lucy_idle_right.png",
        "res://Sprites/Traje_Padrao/Morgana/Morgana_idle_right.png",
        "res://Sprites/Traje_Padrao/Roger/Roger_idle_right.png",
    };

    /// <summary>"walk" sheets (filename casing varies per character, e.g. Roger_Walk).</summary>
    private static readonly string[] WalkSheetPaths =
    {
        "res://Sprites/Traje_Protecao/Traje_Protecao_Ana/Walk/Ana_walk.png",
        "res://Sprites/Traje_Protecao/Traje_Protecao_Lucy/Walk/Lucy_walk.png",
        "res://Sprites/Traje_Protecao/Traje_Protecao_Morgana/Walk/Morgana_walk.png",
        "res://Sprites/Traje_Protecao/Traje_Protecao_Roger/Walk/Roger_Walk.png",
    };

    /// <summary>Standard (Traje_Padrao) walk sheets (_right facing).</summary>
    private static readonly string[] WalkStandardPaths =
    {
        "res://Sprites/Traje_Padrao/Ana/Ana_walk_right.png",
        "res://Sprites/Traje_Padrao/Lucy/Lucy_walk_right.png",
        "res://Sprites/Traje_Padrao/Morgana/Morgana_walk_right.png",
        "res://Sprites/Traje_Padrao/Roger/Roger_walk_right.png",
    };

    /// <summary>"jump" sheets (casing varies, e.g. Morgana_Jump and Roger_Jump).</summary>
    private static readonly string[] JumpSheetPaths =
    {
        "res://Sprites/Traje_Protecao/Traje_Protecao_Ana/Jump/Ana_jump.png",
        "res://Sprites/Traje_Protecao/Traje_Protecao_Lucy/Jump/Lucy_jump.png",
        "res://Sprites/Traje_Protecao/Traje_Protecao_Morgana/Jump/Morgana_Jump.png",
        "res://Sprites/Traje_Protecao/Traje_Protecao_Roger/Jump/Roger_Jump.png",
    };

    /// <summary>Standard (Traje_Padrao) jump sheets (_right facing).</summary>
    private static readonly string[] JumpStandardPaths =
    {
        "res://Sprites/Traje_Padrao/Ana/Ana_jump_right.png",
        "res://Sprites/Traje_Padrao/Lucy/Lucy_jump_right.png",
        "res://Sprites/Traje_Padrao/Morgana/Morgana_jump_right.png",
        "res://Sprites/Traje_Padrao/Roger/Roger_jump_right.png",
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

    /// <summary>True when the character has all three Standard (Traje_Padrao) sheets.</summary>
    public static bool HasStandardSuit(int index)
    {
        if (!IsValid(index))
            return false;
        return ResourceLoader.Exists(IdleStandardPaths[index])
            && ResourceLoader.Exists(WalkStandardPaths[index])
            && ResourceLoader.Exists(JumpStandardPaths[index]);
    }

    /// <summary>
    /// Builds the "idle" SpriteFrames for a character. Defaults to the Standard suit;
    /// characters without a complete Standard set fall back to Protection everywhere.
    /// </summary>
    public static SpriteFrames? BuildIdleFrames(int characterIndex, Suit suit = Suit.Standard)
        => BuildAnimationFrames(characterIndex, suit, IdleFps, "idle", IdleStandardPaths, IdleSheetPaths);

    /// <summary>"walk" frames for a character (8 frames per row, 32 wide each).</summary>
    public static SpriteFrames? BuildWalkFrames(int characterIndex, Suit suit = Suit.Standard)
        => BuildAnimationFrames(characterIndex, suit, 8f, "walk", WalkStandardPaths, WalkSheetPaths);

    /// <summary>"jump" frames for a character (used for the upward/vertical impulse).</summary>
    public static SpriteFrames? BuildJumpFrames(int characterIndex, Suit suit = Suit.Standard)
        => BuildAnimationFrames(characterIndex, suit, 8f, "jump", JumpStandardPaths, JumpSheetPaths);

    private static SpriteFrames? BuildAnimationFrames(
        int characterIndex, Suit suit, float fps, string animation,
        string[] standardPaths, string[] protectionPaths)
    {
        if (!IsValid(characterIndex))
            return null;

        // Standard is the default; fall back to Protection when the character's
        // Standard set is incomplete so nobody shows a mixed/half-empty suit.
        bool useStandard = suit == Suit.Standard && HasStandardSuit(characterIndex);
        string path = useStandard ? standardPaths[characterIndex] : protectionPaths[characterIndex];

        Texture2D? tex = GD.Load<Texture2D>(path);
        if (tex == null)
        {
            GD.PushError($"Characters: {animation} sheet not found for {GetName(characterIndex)}.");
            return null;
        }

        // Frame height comes from the sheet itself (Standard sheets are 32 tall,
        // Protection sheets are 34 tall).
        return SpriteSheetUtil.BuildFrames(path, FrameWidth, tex.GetHeight(), fps, animation, loop: true);
    }
}
