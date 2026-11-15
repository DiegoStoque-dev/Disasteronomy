
using Godot;
using System;

public partial class MainMenu : Control
{
	[Export] public string GameScenePath = "res://Scenes/Game.tscn";

	[Export] public NodePath MenuContainerPath = "MenuContainer";
	[Export] public NodePath MenuStartPath = "MenuStart";
	[Export] public NodePath MenuMultiplayerPath = "MenuMultiplayer";
	[Export] public NodePath MenuOptionsPath = "MenuOptions";

	public Control? MenuContainer { get; private set; }
	public Control? MenuStart { get; private set; }
	public Control? MenuMultiplayer { get; private set; }
	public Control? MenuOptions { get; private set; }

	private Control? _menu_atual;
	private Tween? _tween;
	private bool _mudando_menu = false;

	private const float FADE_DURATION = 0.25f;

	public override void _Ready()
	{
		MenuContainer = GetNodeOrNull<Control>(MenuContainerPath);
		MenuStart = GetNodeOrNull<Control>(MenuStartPath);
		MenuMultiplayer = GetNodeOrNull<Control>(MenuMultiplayerPath);
		MenuOptions = GetNodeOrNull<Control>(MenuOptionsPath);

		if (MenuContainer == null || MenuStart == null ||
			MenuMultiplayer == null || MenuOptions == null)
		{
			GD.PrintErr("MainMenu: Containers não encontrados.");
			return;
		}

		// Menu inicial
		_menu_atual = MenuContainer;

		OcultarTodosMenus();

		MenuContainer.Visible = true;
		MenuContainer.Modulate = new Color(1, 1, 1, 1);

		ConnectButtonsAutomatic();

		AddToGroup("ui_cancel");
	}

	// ==============================
	// CONEXÃO DOS BOTÕES
	// ==============================

	private void ConnectButtonsAutomatic()
	{
		BindButton(MenuContainer, "BtnStart", _OnStartButtonPressed);
		BindButton(MenuContainer, "BtnMultiplayer", _OnMultiplayerButtonPressed);
		BindButton(MenuContainer, "BtnOptions", _OnOptionsButtonPressed);
		BindButton(MenuContainer, "BtnCredits", _OnCreditsButtonPressed);
		BindButton(MenuContainer, "BtnLeave", _OnLeaveButtonPressed);

		BindButton(MenuStart, "BtnLoad", _OnLoadButtonPressed);
		BindButton(MenuStart, "BtnNewgame", _OnNewGameButtonPressed);

		BindButton(MenuMultiplayer, "BtnCreate", _OnCreateButtonPressed);
		BindButton(MenuMultiplayer, "BtnJoin", _OnJoinButtonPressed);

		BindButton(MenuOptions, "BtnOptionsBack", _OnOptionsBackButtonPressed);
	}

	private void BindButton(Control? parent, string nodeName, Action action)
	{
		if (parent == null) return;

		Node? node = FindChildRecursive(parent, nodeName);

		if (node is BaseButton btn)
		{
			btn.Pressed += action;
			GD.Print($"Botão conectado: {nodeName}");
		}
		else
		{
			GD.PrintErr($"Botão não encontrado: {nodeName}");
		}
	}

	private Node? FindChildRecursive(Node root, string name)
	{
		if (root.Name == name)
			return root;

		foreach (Node child in root.GetChildren())
		{
			Node? found = FindChildRecursive(child, name);

			if (found != null)
				return found;
		}

		return null;
	}

	// ==============================
	// CONTROLE DOS MENUS
	// ==============================

	private void OcultarTodosMenus()
	{
		if (MenuContainer != null) MenuContainer.Visible = false;
		if (MenuStart != null) MenuStart.Visible = false;
		if (MenuMultiplayer != null) MenuMultiplayer.Visible = false;
		if (MenuOptions != null) MenuOptions.Visible = false;
	}

	private async void MudarMenu(Control? menuAlvo)
	{
		if (menuAlvo == null || menuAlvo == _menu_atual || _mudando_menu)
			return;

		_mudando_menu = true;

		// Cancela animação anterior, se existir
		if (_tween != null && _tween.IsRunning())
			_tween.Kill();

		// FADE OUT DO MENU ATUAL
		if (_menu_atual != null)
		{
			_tween = CreateTween();

			_tween.TweenProperty(
				_menu_atual,
				"modulate:a",
				0.0f,
				FADE_DURATION
			);

			await ToSignal(_tween, Tween.SignalName.Finished);

			_menu_atual.Visible = false;
		}

		// OCULTA TODOS OS CONTAINERS
		OcultarTodosMenus();

		// PREPARA O NOVO MENU
		menuAlvo.Modulate = new Color(1, 1, 1, 0);
		menuAlvo.Visible = true;

		// FADE IN DO NOVO MENU
		_tween = CreateTween();

		_tween.TweenProperty(
			menuAlvo,
			"modulate:a",
			1.0f,
			FADE_DURATION
		);

		await ToSignal(_tween, Tween.SignalName.Finished);

		// ATUALIZA MENU ATUAL
		_menu_atual = menuAlvo;

		_mudando_menu = false;
	}

	// ==============================
	// BOTÕES
	// ==============================

	public void _OnStartButtonPressed()
	{
		MudarMenu(MenuStart);
	}

	public void _OnMultiplayerButtonPressed()
	{
		MudarMenu(MenuMultiplayer);
	}

	public void _OnOptionsButtonPressed()
	{
		MudarMenu(MenuOptions);
	}

	public void _OnCreditsButtonPressed()
	{
		GD.Print("Abrir créditos");
	}

	public void _OnLeaveButtonPressed()
	{
		GetTree().Quit();
	}

	public void _OnLoadButtonPressed()
	{
		GD.Print("Carregar jogo");
		MudarMenu(MenuContainer);
	}

	public void _OnNewGameButtonPressed()
	{
		GD.Print("Novo jogo");
		MudarMenu(MenuContainer);
	}

	public void _OnCreateButtonPressed()
	{
		GD.Print("Criar sala");
		MudarMenu(MenuContainer);
	}

	public void _OnJoinButtonPressed()
	{
		GD.Print("Entrar em sala");
		MudarMenu(MenuContainer);
	}

	public void _OnOptionsBackButtonPressed()
	{
		MudarMenu(MenuContainer);
	}

	public void _OnBackToMainMenu()
	{
		if (_menu_atual != MenuContainer)
			MudarMenu(MenuContainer);
	}

	// ==============================
	// TECLA ESC
	// ==============================

	public override void _UnhandledInput(InputEvent eventInput)
	{
		if (eventInput is InputEventKey key &&
			key.Keycode == Key.Escape &&
			key.Pressed &&
			!key.Echo)
		{
			if (_menu_atual != MenuContainer)
				_OnBackToMainMenu();
		}
	}
}
