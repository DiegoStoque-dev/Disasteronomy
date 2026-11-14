using Godot;
using System;
using System.IO;

public partial class Global : Node
{
    private const string SAVE_FILE = "user://disasteronomy_save.dat";

    public override void _Ready()
    {
        // Variáveis globais do jogo podem ser inicializadas aqui
    }

    /// <summary>
    /// Verifica se existe um arquivo de save salvo.
    /// </summary>
    public bool ExisteSave()
    {
        return Godot.FileAccess.FileExists(SAVE_FILE);
    }

    /// <summary>
    /// Salva os dados do jogo (reset para novo jogo ou progresso atual).
    /// </summary>
    public void Salvar()
    {
        try
        {
            Godot.DirAccess.MakeDirAbsolute(Path.GetDirectoryName(SAVE_FILE));
            using var file = Godot.FileAccess.Open(SAVE_FILE, Godot.FileAccess.ModeFlags.Write);
            file.StoreString("DISASTERONOMY_SAVE_V1");
            file.Store64((ulong)Time.GetUnixTimeFromSystem());
            file.Store32(1); // versão do save
            GD.Print("[Global] Save salvo em: " + SAVE_FILE);
        }
        catch (Exception e)
        {
            GD.PrintErr("[Global] Erro ao salvar: " + e.Message);
        }
    }

    /// <summary>
    /// Carrega os dados do save existente.
    /// </summary>
    public void Carregar()
    {
        if (!ExisteSave())
        {
            GD.PrintErr("[Global] Nenhum save encontrado para carregar.");
            return;
        }

        try
        {
            using var file = Godot.FileAccess.Open(SAVE_FILE, Godot.FileAccess.ModeFlags.Read);
            file.Get64(); // timestamp
            var version = file.Get32();
            GD.Print($"[Global] Save carregado: versão {version}");
        }
        catch (Exception e)
        {
            GD.PrintErr("[Global] Erro ao carregar save: " + e.Message);
        }
    }
}