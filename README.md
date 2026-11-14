# disasteronomy

Projeto base configurado para desenvolvimento em **Godot 4.7** com suporte a **C# (.NET)**.

## 🛠️ Tecnologias

* **Engine:** Godot 4.7
* **Linguagem:** C#
* **Plataforma alvo:** Desktop (Windows)
* **Renderização:** Forward Plus
* **SDK:** Godot.NET.Sdk 4.7.2

## 📁 Estrutura

* `project.godot` — Configuração do projeto Godot
* `.csproj` / `.sln` — Projeto e solução do Visual Studio
* `export_presets.cfg` — Presets de exportação (Windows Desktop)
* `.gitignore` — Arquivos e pastas ignorados pelo Git
* `.editorconfig` — Padronização de código
* `.gitattributes` — Normalização de fim de linha

## 🚀 Como começar

1. Abra o Godot 4.7.
2. Clique em **Importar** e selecione a pasta `disasteronomy`.
3. O Godot irá detectar o arquivo `project.godot` e carregar o projeto.
4. Para compilar os scripts C#, abra o arquivo `disasteronomy.sln` no Visual Studio ou use `dotnet build`.

## ⚙️ Configurações incluídas

* Input maps já mapeados (setas/WASD, espaço, shift, mouse, E, etc.)
* Autoloads: `SceneTransitions` e `Global`
* Stretch mode: `canvas_items` com aspect `keep` e shrink `1`
* HDR 2D ativado
* Preset de exportação para Windows Desktop
* **Pixel art otimizado:** filtro nearest (sem interpolação), mipmaps desabilitados, filtro anisotrópico desabilitado e repeat de textura desabilitado