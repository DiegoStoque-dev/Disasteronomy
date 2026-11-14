using Godot;
using System;

public partial class MainMenu : Control
{
    [Export] public string GameScenePath = "res://Scenes/Game.tscn";

    public void _OnStartButtonPressed()
    {
        GetTree().ChangeSceneToFile(GameScenePath);
    }

    public void _OnQuitButtonPressed()
    {
        GetTree().Quit();
    }
}