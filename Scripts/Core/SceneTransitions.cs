using Godot;
using System;

public partial class SceneTransitions : Node
{
    public void ChangeScene(string path)
    {
        GetTree().ChangeSceneToFile(path);
    }
}