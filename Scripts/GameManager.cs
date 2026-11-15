#nullable enable
using Godot;
using System;
using System.Collections.Generic;

public partial class GameManager : Node
{
	private const string SavePath = "user://savegame.json";
	
	public Player? Player { get; private set; }
	
	public override void _Ready()
	{
		// Find player in children
		Player = GetNodeOrNull<Player>("Player");
		
		// Load saved position if exists AND game was loaded from saved state
		// Use a flag set by MainMenu when loading an existing save.
		var isNewGame = GetTree().CurrentScene?.GetMeta("NewGame", false).AsBool();
		if (!isNewGame.HasValue || !isNewGame.Value)
		{
			LoadGame();
		}
	}
	
	public void SaveGame()
	{
		if (Player == null) return;
		
		var data = new Godot.Collections.Dictionary<string, Variant>()
		{
			{ "player_position_x", Player.GlobalPosition.X },
			{ "player_position_y", Player.GlobalPosition.Y }
		};
		
		string json = Json.Stringify(data);
		
		using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
		if (file != null)
		{
			file.StoreString(json);
			GD.Print("Game saved to ", SavePath);
		}
		else
		{
			GD.PrintErr("Failed to save game: ", FileAccess.GetOpenError());
		}
	}
	
	public void LoadGame()
	{
		if (!FileAccess.FileExists(SavePath))
		{
			GD.Print("No save file found. Starting fresh.");
			return;
		}
		
		using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			GD.PrintErr("Failed to load save game: ", FileAccess.GetOpenError());
			return;
		}
		
		string json = file.GetAsText();
		var result = Json.ParseString(json);
		
		if (result.VariantType == Variant.Type.Dictionary)
		{
			var data = result.AsGodotDictionary<string, Variant>();
			if (Player != null)
			{
				if (data.TryGetValue("player_position_x", out Variant x) && 
					data.TryGetValue("player_position_y", out Variant y))
				{
					Player.GlobalPosition = new Vector2(x.AsSingle(), y.AsSingle());
					GD.Print("Loaded player position: ", Player.GlobalPosition);
				}
			}
		}
		else
		{
			GD.PrintErr("Save file corrupted.");
		}
	}
	
	public void OnMainMenuPressed()
	{
		GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
	}
}
