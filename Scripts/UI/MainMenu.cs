#nullable enable
using Godot;
using System;

public partial class MainMenu : Control
{
	[Export] public string GameScenePath = "res://Scenes/Game.tscn";
	[Export] public string MultiplayerScenePath = "res://Scenes/multiplayer.tscn";

	[Export] public NodePath MenuContainerPath = "MenuContainer";
	[Export] public NodePath MenuStartPath = "MenuStart";
	[Export] public NodePath MenuMultiplayerPath = "MenuMultiplayer";
	[Export] public NodePath MenuOptionsPath = "MenuOptions";

	public Control? MenuContainer { get; private set; }
	public Control? MenuStart { get; private set; }
	public Control? MenuMultiplayer { get; private set; }
	public Control? MenuOptions { get; private set; }

	private Control? _overlayAtual;
	private Tween? _tween;
	private bool _mudandoMenu = false;

	private const float FADE_DURATION = 0.3f;

	private OptionButton? _resolutionDropdown;
	private HSlider? _volumeSlider;
	private Label? _keysLabel;

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

		OcultarSubMenus();
		MenuContainer.Visible = true;
		MenuContainer.Modulate = new Color(1, 1, 1, 1);

		ConnectButtonsAutomatic();
		SetupOptions();
	}

	private void ConnectButtonsAutomatic()
	{
		BindButton(MenuContainer, "BtnStart", _OnStartButtonPressed);
		BindButton(MenuContainer, "BtnMultiplayer", _OnMultiplayerButtonPressed);
		BindButton(MenuContainer, "BtnOptions", _OnOptionsButtonPressed);
		BindButton(MenuContainer, "BtnCredits", _OnCreditsButtonPressed);
		BindButton(MenuContainer, "BtnLeave", _OnLeaveButtonPressed);

		BindButton(MenuStart, "BtnLoad", _OnLoadButtonPressed);
		BindButton(MenuStart, "BtnNewgame", _OnNewGameButtonPressed);
		BindButton(MenuStart, "BtnStartBack", _OnStartBackPressed);

		BindButton(MenuMultiplayer, "BtnCreate", _OnCreateButtonPressed);
		BindButton(MenuMultiplayer, "BtnJoin", _OnJoinButtonPressed);
		BindButton(MenuMultiplayer, "BtnMultiplayerBack", _OnMultiplayerBackPressed);

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
	private void OcultarSubMenus()
	{
		if (MenuStart != null) MenuStart.Visible = false;
		if (MenuMultiplayer != null) MenuMultiplayer.Visible = false;
		if (MenuOptions != null) MenuOptions.Visible = false;
	}

	private async void AbrirOverlay(Control? overlay)
	{
		if (overlay == null || overlay == _overlayAtual || _mudandoMenu)
			return;

		_mudandoMenu = true;

		if (_tween != null && _tween.IsRunning())
			_tween.Kill();

		// Fecha overlay anterior, se houver
		if (_overlayAtual != null)
		{
			_tween = CreateTween();
			_tween.TweenProperty(_overlayAtual, "modulate:a", 0.0f, FADE_DURATION);
			await ToSignal(_tween, Tween.SignalName.Finished);
			_overlayAtual.Visible = false;
			_overlayAtual = null;
		}

		// Abre o novo overlay com fade in
		overlay.Modulate = new Color(1, 1, 1, 0);
		overlay.Visible = true;

		_tween = CreateTween();
		_tween.TweenProperty(overlay, "modulate:a", 1.0f, FADE_DURATION);

		await ToSignal(_tween, Tween.SignalName.Finished);

		_overlayAtual = overlay;
		_mudandoMenu = false;

		GD.Print($"Overlay aberto: {overlay.Name}");
	}

	private async void FecharOverlay()
	{
		if (_overlayAtual == null || _mudandoMenu)
			return;

		_mudandoMenu = true;

		if (_tween != null && _tween.IsRunning())
			_tween.Kill();

		_tween = CreateTween();
		_tween.TweenProperty(_overlayAtual, "modulate:a", 0.0f, FADE_DURATION);
		await ToSignal(_tween, Tween.SignalName.Finished);

		_overlayAtual.Visible = false;
		_overlayAtual = null;
		_mudandoMenu = false;

		GD.Print("Overlay fechado");
	}

	public void _OnStartButtonPressed()
	{
		AbrirOverlay(MenuStart);
	}

	public void _OnMultiplayerButtonPressed()
	{
		AbrirOverlay(MenuMultiplayer);
	}

	public void _OnOptionsButtonPressed()
	{
		AbrirOverlay(MenuOptions);
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
		FecharOverlay();
	}

	public void _OnNewGameButtonPressed()
	{
		GD.Print("Novo jogo");
		FecharOverlay();
	}

	public void _OnCreateButtonPressed()
	{
		GD.Print("Criar sala (Host)");
		GetTree().ChangeSceneToFile(MultiplayerScenePath);
	}

	public void _OnJoinButtonPressed()
	{
		GD.Print("Entrar na sala (Join)");
		GetTree().ChangeSceneToFile(MultiplayerScenePath);
	}

	public void _OnOptionsBackButtonPressed()
	{
		FecharOverlay();
	}

	public void _OnBackToMainMenu()
	{
		FecharOverlay();
	}

	public void _OnStartBackPressed()
	{
		FecharOverlay();
	}

	public void _OnMultiplayerBackPressed()
	{
		FecharOverlay();
	}

	public override void _UnhandledInput(InputEvent eventInput)
	{
		if (eventInput is InputEventKey key &&
			key.Keycode == Key.Escape &&
			key.Pressed &&
			!key.Echo)
		{
			if (_overlayAtual != null)
			{
				FecharOverlay();
			}
		}
	}
	private void SetupOptions()
	{
		if (MenuOptions == null) return;

		_resolutionDropdown = MenuOptions.GetNodeOrNull<OptionButton>("ResolutionDropdown");
		_volumeSlider = MenuOptions.GetNodeOrNull<HSlider>("VolumeSlider");
		_keysLabel = MenuOptions.GetNodeOrNull<Label>("LabelKeys");

		if (_resolutionDropdown != null)
		{
			_resolutionDropdown.Clear();
			_resolutionDropdown.AddItem("1280 x 720  (HD)", 0);
			_resolutionDropdown.AddItem("1920 x 1080 (Full HD)", 1);
			_resolutionDropdown.AddItem("2560 x 1440 (QHD)", 2);
			_resolutionDropdown.AddItem("3840 x 2160 (4K UHD)", 3);

			Vector2I tamanhoAtual = DisplayServer.WindowGetSize();
			int indiceSelecionado = 1;

			if (tamanhoAtual == new Vector2I(1280, 720)) indiceSelecionado = 0;
			else if (tamanhoAtual == new Vector2I(1920, 1080)) indiceSelecionado = 1;
			else if (tamanhoAtual == new Vector2I(2560, 1440)) indiceSelecionado = 2;
			else if (tamanhoAtual == new Vector2I(3840, 2160)) indiceSelecionado = 3;

			_resolutionDropdown.Selected = indiceSelecionado;
			_resolutionDropdown.ItemSelected += _OnResolutionSelected;
		}

		if (_volumeSlider != null)
		{
			int masterBus = AudioServer.GetBusIndex("Master");
			if (masterBus >= 0)
			{
				double volumeDb = AudioServer.GetBusVolumeDb(masterBus);
				float volumeLinear = Mathf.DbToLinear((float)volumeDb);
				_volumeSlider.Value = Mathf.Clamp(volumeLinear, 0.0f, 1.0f);
			}
			_volumeSlider.ValueChanged += _OnVolumeChanged;
		}

		if (_keysLabel != null)
		{
			_keysLabel.Text = ObterDescricaoTeclas();
		}
	}

	private string ObterDescricaoTeclas()
	{
		var moverEsquerda = InputMap.ActionGetEvents("mover_esquerda");
		var moverDireita = InputMap.ActionGetEvents("mover_direita");
		var pular = InputMap.ActionGetEvents("pular");
		var voz = InputMap.ActionGetEvents("voice_chat");

		string teclaEsquerda = ObterNomeTecla(moverEsquerda);
		string teclaDireita = ObterNomeTecla(moverDireita);
		string teclaPular = ObterNomeTecla(pular);
		string teclaVoz = ObterNomeTecla(voz);

		return $"Teclas: {teclaEsquerda}/{teclaDireita} = Mover | {teclaPular} = Pular | {teclaVoz} = Voz";
	}

	private string ObterNomeTecla(Godot.Collections.Array<InputEvent> eventos)
	{
		foreach (var ev in eventos)
		{
			if (ev is InputEventKey key)
				return key.Keycode.ToString();
		}
		return "Não atribuído";
	}

	private void _OnResolutionSelected(long indice)
	{
		Vector2I tamanho = indice switch
		{
			0 => new Vector2I(1280, 720),
			1 => new Vector2I(1920, 1080),
			2 => new Vector2I(2560, 1440),
			3 => new Vector2I(3840, 2160),
			_ => new Vector2I(1920, 1080)
		};

		DisplayServer.WindowSetSize(tamanho);
		GD.Print($"Resolução alterada para: {tamanho.X} x {tamanho.Y}");
	}

	private void _OnVolumeChanged(double valor)
	{
		int masterBus = AudioServer.GetBusIndex("Master");
		if (masterBus >= 0)
		{
			double volumeDb = Mathf.LinearToDb((float)valor);
			AudioServer.SetBusVolumeDb(masterBus, (float)volumeDb);
			GD.Print($"Volume do Master alterado para: {valor:P0} ({volumeDb:F1} dB)");
		}
	}

	public void RemapearTecla(string acao, Key novaTecla)
	{
		var eventosExistentes = InputMap.ActionGetEvents(acao);
		foreach (var ev in eventosExistentes)
		{
			if (ev is InputEventKey)
				InputMap.ActionEraseEvent(acao, ev);
		}

		var novoEvento = new InputEventKey
		{
			Keycode = novaTecla,
			Pressed = true
		};
		InputMap.ActionAddEvent(acao, novoEvento);

		GD.Print($"Input Map remapeado: {acao} → {novaTecla}");

		if (_keysLabel != null)
			_keysLabel.Text = ObterDescricaoTeclas();
	}
}
