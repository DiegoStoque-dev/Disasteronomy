
using Godot;

public partial class Player : CharacterBody2D
{
	private const float Speed = 200f;

	private AnimatedSprite2D sprite;
	private Label nicknameLabel;
	private Node networkHandler;
	private Callable nicknameChangedCallable;
	private long playerId;
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

		nicknameLabel = GetNode<Label>("NicknameLabel");
		networkHandler = GetNode("/root/HighLevelNetworkHandler");
		nicknameChangedCallable = Callable.From<long, string>(OnPlayerNicknameChanged);
		networkHandler.Connect("player_nickname_changed", nicknameChangedCallable);
		if (long.TryParse(Name.ToString(), out playerId))
			UpdateNickname(networkHandler.Call("get_player_nickname", playerId).AsString());

		sprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
		GetNode<Camera2D>("Camera2D").Enabled = IsMultiplayerAuthority();

		if (IsMultiplayerAuthority())
			GD.Print("Controlando meu personagem: " + Name);

		UpdateAnimation("idle_down");
	}

	public override void _ExitTree()
	{
		if (networkHandler != null &&
			networkHandler.IsConnected("player_nickname_changed", nicknameChangedCallable))
		{
			networkHandler.Disconnect("player_nickname_changed", nicknameChangedCallable);
		}
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
		if (sprite.Animation != animationName)
			sprite.Play(animationName);
	}

	private void OnPlayerNicknameChanged(long changedPlayerId, string nickname)
	{
		if (changedPlayerId == playerId)
			UpdateNickname(nickname);
	}

	private void UpdateNickname(string nickname)
	{
		nicknameLabel.Text = string.IsNullOrWhiteSpace(nickname)
			? $"Jogador {playerId}"
			: nickname;
	}

	public override void _EnterTree()
	{
		if (int.TryParse(Name.ToString(), out int peerId))
			SetMultiplayerAuthority(peerId, true);
	}
}
