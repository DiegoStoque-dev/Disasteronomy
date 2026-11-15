#nullable enable
using Godot;

/// <summary>
/// Autoload that keeps the menu soundtrack playing across every screen
/// (main menu, settings, multiplayer, lobby, intro...) without restarting.
/// The volume is controlled from the settings screen and stored in user://settings.cfg.
/// </summary>
public partial class MusicManager : Node
{
    public static MusicManager? Instance { get; private set; }

    /// <summary>Looped menu track.</summary>
    [Export] public string MenuTrackPath =
        "res://Music/Loops - Lonely Nightmare/Loops Lonely Nightmare/Loops OGG/A Place I've Seen Before Loop1.ogg";

    /// <summary>Alternative non-looped source, kept for reference.</summary>
    public const string MenuTrackMp3Path =
        "res://Music/Lonely Nightmare/Lonely Nightmare/MP3/A Place I've Seen Before.mp3";

    private const float FadeInTime = 1.5f;
    private const float MinVolumeDb = -80f;

    private AudioStreamPlayer? _player;
    private Tween? _fadeTween;

    public override void _Ready()
    {
        Instance = this;
        // Keeps playing even if a scene pauses the tree.
        ProcessMode = Node.ProcessModeEnum.Always;

        _player = new AudioStreamPlayer { Name = "MusicPlayer" };
        AddChild(_player);

        PlayMenuTrack();
        ApplyVolume();
    }

    /// <summary>Starts (or restarts) the looped menu soundtrack.</summary>
    public void PlayMenuTrack(string? trackPath = null)
    {
        if (_player == null)
            return;

        string path = string.IsNullOrEmpty(trackPath) ? MenuTrackPath : trackPath;
        if (_player.Playing && _player.Stream?.ResourcePath == path)
            return;

        AudioStream? stream = GD.Load<AudioStream>(path);
        if (stream == null)
        {
            GD.PushError($"MusicManager: could not load '{path}'.");
            return;
        }

        // Loop forever (works for both OGG Vorbis and MP3 streams).
        switch (stream)
        {
            case AudioStreamOggVorbis ogg:
                ogg.Loop = true;
                break;
            case AudioStreamMP3 mp3:
                mp3.Loop = true;
                break;
            case AudioStreamWav wav:
                wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                break;
        }

        _player.Stream = stream;
        _player.Play();

        // Gentle fade in so entering the game is not abrupt.
        _player.VolumeDb = MinVolumeDb;
        ApplyVolume(FadeInTime);
    }

    public void Stop()
    {
        _player?.Stop();
    }

    /// <summary>Re-reads the volume from the settings and applies it to the player.</summary>
    public void ApplyVolume(float fadeTime = 0f)
    {
        if (_player == null)
            return;

        float linear = Global.Instance?.MusicVolume ?? 0.7f;
        float targetDb = linear <= 0.001f
            ? MinVolumeDb
            : Mathf.Max(MinVolumeDb, Mathf.LinearToDb(linear));

        if (_fadeTween != null && _fadeTween.IsValid())
            _fadeTween.Kill();

        if (fadeTime <= 0f)
        {
            _player.VolumeDb = targetDb;
            return;
        }

        _fadeTween = CreateTween();
        _fadeTween.TweenProperty(_player, "volume_db", targetDb, fadeTime);
    }

    public bool IsPlaying => _player?.Playing ?? false;
}
