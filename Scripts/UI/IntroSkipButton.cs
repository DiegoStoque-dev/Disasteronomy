#nullable enable
using Godot;

/// <summary>
/// Reusable, restyle-able skip hint for video intros.
///
/// Fades in from transparent after <see cref="DelaySeconds"/> (so it does not distract),
/// stays semi-transparent, and emits the standard Button "pressed" signal when clicked
/// (mouse or touch). Placement / text / theme are editable in the editor scene.
/// </summary>
public partial class IntroSkipButton : Button
{
	/// <summary>Seconds before the hint fades in (the intro plays quietly first).</summary>
	[Export] public float DelaySeconds = 4.0f;

	/// <summary>Duration of the fade-in, once the delay elapses.</summary>
	[Export] public float FadeInTime = 0.8f;

	/// <summary>Final opacity of the hint (semi-transparent by default).</summary>
	[Export] public float TargetOpacity = 0.6f;

	public override void _Ready()
	{
		// Start fully transparent so the intro is not distracted before the delay.
		Modulate = new Color(1f, 1f, 1f, 0f);

		Tween tween = CreateTween();
		tween.TweenInterval(DelaySeconds);
		tween.TweenProperty(this, "modulate:a", TargetOpacity, FadeInTime);
	}
}