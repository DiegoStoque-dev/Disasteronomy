
using Godot;

public partial class Player : CharacterBody2D
{
	private const float Speed = 200f;

	private AnimatedSprite2D? sprite;
	private string lastDirection = "down";

	public override void _Ready()
	{
		AddToGroup("network_players");
		foreach (Node node in GetTree().GetNodesInGroup("network_players"))
		{
			if (node == this || node is not CharacterBody2D otherPlayer)
				continue;

			AddCollisionExceptionWith(otherPlayer);
			otherPlayer.AddCollisionExceptionWith(this);
		}

		sprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
		var camera = GetNodeOrNull<Camera2D>("Camera2D");
		if (camera != null)
			camera.Enabled = IsMultiplayerAuthority();

		if (IsMultiplayerAuthority())
			GD.Print("Controlando meu personagem: " + Name);

		UpdateAnimation("idle_down");
	}

	public override void _PhysicsProcess(double delta)
	{
		// Verificar se o multiplayer está inicializado
		if (Multiplayer.MultiplayerPeer == null)
			return;
		if (!IsMultiplayerAuthority())
			return;

		Vector2 direction = Input.GetVector(
			"ui_left",
			"ui_right",
			"ui_up",
            "ui_down"
		);

		Velocity = direction * Speed;
		MoveAndSlide();

		if (direction != Vector2.Zero)
		{
			if (Mathf.Abs(direction.X) > Mathf.Abs(direction.Y))
				lastDirection = direction.X > 0 ? "right" : "left";
			else
				lastDirection = direction.Y > 0 ? "down" : "up";

			UpdateAnimation("walk_" + lastDirection);
		}
		else
		{
			UpdateAnimation("idle_" + lastDirection);
		}
	}

	private void UpdateAnimation(string animationName)
	{
		if (sprite == null)
			return;

		if (sprite.Animation != animationName)
			sprite.Play(animationName);
	}
	public override void _EnterTree()
{
    if (int.TryParse(Name.ToString(), out int peerId))
        SetMultiplayerAuthority(peerId, true);
}
}
