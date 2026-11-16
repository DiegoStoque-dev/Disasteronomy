#nullable enable
using Godot;
using System;

/// <summary>Playable characters. The order also defines the player color.</summary>
public enum CharacterId
{
    Ana = 0,
    Lucy = 1,
    Morgana = 2,
    Roger = 3,
}

public enum CharacterSuit
{
    Standard,
    Protection,
}

/// <summary>
/// Static catalog of the playable characters: display name, identification color
/// (Red = Ana, Purple = Lucy, Blue = Morgana, Green = Roger) and the idle sprite sheet.
/// </summary>
public static class Characters
{
    public const int Count = 4;

    /// <summary>Common sprite frame width; walk sheets are 34px tall.</summary>
    public const int FrameWidth = 32;
    public const int FrameHeight = 34;
    /// <summary>Idle sheets contain eight frames.</summary>
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

    private static readonly string[] StandardIdleSheetPaths =
    {
        "res://Sprites/Traje_Padrao/Ana/Ana_idle_right.png",
        "res://Sprites/Traje_Padrao/Lucy/Lucy_idle_rightt.png",
        "res://Sprites/Traje_Padrao/Morgana/Morgana_idle_right.png",
        "res://Sprites/Traje_Padrao/Roger/Roger_idle_right.png",
    };

    private static readonly string[] StandardWalkSheetPaths =
    {
        "res://Sprites/Traje_Padrao/Lucy/Ana_walk_right.png",
        "res://Sprites/Traje_Padrao/Lucy/Lucy_walk_right.png",
        "res://Sprites/Traje_Padrao/Morgana/Morgana_walk_right.png",
        "res://Sprites/Traje_Padrao/Roger/Roger_walk_right.png",
    };

    private static readonly string[] StandardJumpSheetPaths =
    {
        "res://Sprites/Traje_Padrao/Ana/Ana_jump_right.png",
        "res://Sprites/Traje_Padrao/Lucy/Lucy_jump_right.png",
        "res://Sprites/Traje_Padrao/Morgana/Morgana_jump_right.png",
        "res://Sprites/Traje_Padrao/Roger/Roger_jump_right.png",
    };

    private static readonly string[] ProtectionIdleSheetPaths =
    {
        "res://Sprites/Traje_Protecao/Traje_Protecao_Ana/Idle/Ana_Idle.png",
        "res://Sprites/Traje_Protecao/Traje_Protecao_Lucy/Idle/Lucy_Idle.png",
        "res://Sprites/Traje_Protecao/Traje_Protecao_Morgana/Idle/Morgana_Idle.png",
        "res://Sprites/Traje_Protecao/Traje_Protecao_Roger/Idle/Roger_Idle.png",
    };

    /// <summary>"walk" sheets (filename casing varies per character, e.g. Roger_Walk).</summary>
    private static readonly string[] ProtectionWalkSheetPaths =
    {
        "res://Sprites/Traje_Protecao/Traje_Protecao_Ana/Walk/Ana_walk.png",
        "res://Sprites/Traje_Protecao/Traje_Protecao_Lucy/Walk/Lucy_walk.png",
        "res://Sprites/Traje_Protecao/Traje_Protecao_Morgana/Walk/Morgana_walk.png",
        "res://Sprites/Traje_Protecao/Traje_Protecao_Roger/Walk/Roger_Walk.png",
    };

    /// <summary>"jump" sheets (casing varies, e.g. Morgana_Jump and Roger_Jump).</summary>
    private static readonly string[] ProtectionJumpSheetPaths =
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

    public static CharacterSuit GetSuitForCurrentScene(SceneTree tree)
    {
        string scenePath = tree.CurrentScene?.SceneFilePath ?? string.Empty;
        return scenePath.Contains("/Phase2/", StringComparison.OrdinalIgnoreCase)
            ? CharacterSuit.Protection
            : CharacterSuit.Standard;
    }

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
    /// Builds the "idle" SpriteFrames for a character by slicing its sheet.
    /// </summary>
    public static SpriteFrames? BuildIdleFrames(
        int characterIndex,
        CharacterSuit suit = CharacterSuit.Standard,
        string animationName = "idle")
    {
        if (!IsValid(characterIndex))
            return null;

        bool protectionSuit = suit == CharacterSuit.Protection;
        SpriteFrames? frames = SpriteSheetUtil.BuildFrames(
            protectionSuit
                ? ProtectionIdleSheetPaths[characterIndex]
                : StandardIdleSheetPaths[characterIndex],
            FrameWidth,
            protectionSuit ? 34 : 32,
            IdleFps,
            animationName,
            loop: true);

        if (frames == null)
            GD.PushError($"Characters: idle sheet not found for {GetName(characterIndex)}.");

        return frames;
    }

    /// <summary>"walk" frames for a character (32x34 each).</summary>
    public static SpriteFrames? BuildWalkFrames(
        int characterIndex,
        CharacterSuit suit = CharacterSuit.Standard,
        string animationName = "walk")
    {
        if (!IsValid(characterIndex))
            return null;

        bool protectionSuit = suit == CharacterSuit.Protection;
        SpriteFrames? frames = SpriteSheetUtil.BuildFrames(
            protectionSuit
                ? ProtectionWalkSheetPaths[characterIndex]
                : StandardWalkSheetPaths[characterIndex],
            FrameWidth,
            FrameHeight,
            8f,
            animationName,
            loop: true);

        if (frames == null)
            GD.PushError($"Characters: walk sheet not found for {GetName(characterIndex)}.");

        return frames;
    }

    /// <summary>"jump" frames for a character (used for the upward/vertical impulse).</summary>
    public static SpriteFrames? BuildJumpFrames(
        int characterIndex,
        CharacterSuit suit = CharacterSuit.Standard,
        string animationName = "jump")
    {
        if (!IsValid(characterIndex))
            return null;

        bool protectionSuit = suit == CharacterSuit.Protection;
        SpriteFrames? frames = SpriteSheetUtil.BuildFrames(
            protectionSuit
                ? ProtectionJumpSheetPaths[characterIndex]
                : StandardJumpSheetPaths[characterIndex],
            FrameWidth,
            protectionSuit ? 34 : 32,
            8f,
            animationName,
            loop: true);

        if (frames == null)
            GD.PushError($"Characters: jump sheet not found for {GetName(characterIndex)}.");

        return frames;
    }
}
