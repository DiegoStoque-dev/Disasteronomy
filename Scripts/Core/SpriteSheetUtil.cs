#nullable enable
using Godot;

/// <summary>
/// Helper that slices a horizontal sprite sheet (frames laid out left to right)
/// into a ready-to-play <see cref="SpriteFrames"/> resource.
/// </summary>
public static class SpriteSheetUtil
{
    public static SpriteFrames? BuildFrames(
        string texturePath,
        int frameWidth,
        int frameHeight,
        float fps,
        string animationName = "default",
        bool loop = true)
    {
        Texture2D? texture = GD.Load<Texture2D>(texturePath);
        if (texture == null || frameWidth <= 0 || frameHeight <= 0)
        {
            GD.PushError($"SpriteSheetUtil: invalid sheet '{texturePath}'.");
            return null;
        }

        var frames = new SpriteFrames();
        frames.RemoveAnimation("default");

        frames.AddAnimation(animationName);
        frames.SetAnimationSpeed(animationName, fps);
        frames.SetAnimationLoopMode(animationName,
            loop ? SpriteFrames.LoopMode.Linear : SpriteFrames.LoopMode.None);

        int frameCount = Mathf.Max(1, texture.GetWidth() / frameWidth);
        for (int i = 0; i < frameCount; i++)
        {
            frames.AddFrame(animationName, new AtlasTexture
            {
                Atlas = texture,
                Region = new Rect2(i * frameWidth, 0, frameWidth, frameHeight),
            });
        }

        return frames;
    }
}
