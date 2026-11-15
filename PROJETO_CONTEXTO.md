# Disasteronomy — Contexto do Projeto (para outras IAs)

Documento de referência: o que existe em cada arquivo e para que serve, para análise/edição do código por agentes de IA. Evite ler `.import`, `.uid`, `.bak` e `.old`.

## 1. Visão geral

- **Jogo:** Disasteronomy — co-op 2D pixel-art de "exploração espacial em cenário de desastre" (NASA Space Apps hackathon). O astronauta faz EVA, segue os corrimãos dourados da nave até a CPS e calibra atuadores congelados antes da corda (tether) arrebentar.
- **Engine:** Godot **4.7.2 Mono/C#** (net8.0), renderer Forward Plus, 2D, stretch `canvas_items`.
- **Código:** C# (.cs) + GDScript (.gd) apenas no autoload de rede.
- **Plataforma:** Windows Desktop (`export_presets.cfg`).

### Fluxo de cenas

```
Intro.tscn (vídeo logo) ─▶ MainMenu.tscn ─▶ MatchSetup.tscn (lobby/room)
  ▶ Cutscene1 (tutorial voz) ▶ Cutscene2 (tutorial TAB) ▶ Cutscene3
    ▶ Lobby/lobby.tscn (interior da nave; host ativa Terminal)
      ▶ Phase1/Phase1.tscn (EVA / Fase 1) ─▶ Fase 2/3 (planejadas)
```

### Autoloads (project.godot)

| Autoload | Arquivo | Função |
|---|---|---|
| `HighLevelNetworkHandler` | `Scripts/high_level_network_handler.gd` | ENet multiplayer + descoberta de salas LAN (UDP) + room codes |
| `SceneTransitions` | `Scripts/Core/SceneTransitions.cs` | Fade entre cenas (`ChangeScene`) |
| `Global` | `Scripts/Core/Global.cs` | Config de vídeo/áudio, remap de teclas, save/load, objetivo atual |
| `Music` | `Scripts/Core/MusicManager.cs` | Música do menu em loop + volume |

### Build / execução

- `dotnet build disasteronomy.csproj` na raiz (ou `Alt+B` no editor Godot).
- Cena principal: `res://Scenes/Intro.tscn`. Não há suite de testes automatizada.

---

## 2. Arquivos na raiz

| Arquivo | Conteúdo / função |
|---|---|
| `project.godot` | Config do motor: main scene, autoloads, Input Map (move_left/right, jump, run, dash, inventory=E, voice_chat=V, objectives=TAB), tema global, microfone, filtragem nearest/HDR. |
| `disasteronomy.csproj` / `.sln` | Projeto .NET (regenerado pelo Godot). |
| `export_presets.cfg` | Preset de exportação Windows Desktop (`../exe/disasteronomy demo.exe`). |
| `default_bus_layout.tres` | Áudio: buses Master + **Microphone** (com `AudioEffectCapture` para chat de voz). |
| `README.md` | Instruções, controles, estrutura, roteiro de fases. |
| `teste.tscn` | Cena de teste que apenas instancia `multiplayer.tscn` (fora do fluxo). |
| `Themes/DisasteronomyTheme.tres` | Tema global escuro/roxo, fonte `Fontes/Boiled Pasta.ttf`, estilos de botões/painéis. |
| `.editorconfig` / `.gitattributes` / `.gitignore` / `LICENSE` | Ferramentas/legal. |

---

## 3. Scripts — C# core (`Scripts/Core/`)

| Arquivo | Função |
|---|---|
| `Global.cs` | Singleton de configuração: resolução/fullscreen, volume, remap de input (salvo em `user://settings.cfg`), save da posição do jogador (`user://disasteronomy_save.dat`), sinal/estado do objetivo atual e **progresso da Fase 1 em memória** (`Level1Completed`/`Level1Last*`, resetado por `ResetProgress()` no início de uma nova partida). |
| `Characters.cs` | Catálogo dos 4 personagens (Ana=vermelho, Lucy=roxo, Morgana=azul, Roger=verde) + caminhos das folhas de sprite e geração de `SpriteFrames` (idle/walk/jump). |
| `MusicManager.cs` | Autoload: música do menu (`Loops - Lonely Nightmare/...loop1.ogg`) tocando em loop com fade. |
| `Sfx.cs` | Helper estático `Sfx.Play(node, "nome")` — toca `res://Music/sfx/<nome>.{ogg,wav,mp3}` se existir; silencioso se faltar. |
| `SpriteSheetUtil.cs` | Fatia folhas de sprite horizontais em `SpriteFrames` (Atlases). |
| `BriefingScreen.cs` | Modal de briefing no início da fase (pausa o jogo, botão "Start mission"). |
| `MissionRank.cs` | Cálculo de nota F–S por tempo/estresse/ruptura/SAFER restante. |
| `PauseMenu.cs` | Menu de pausa na tecla P (Resume / Restart phase / Back to Menu). |
| `SceneTransitions.cs` | Autoload de fade preto entre cenas; `GoToMainMenu()` chamável por GDScript. |

## 4. Scripts — Fase 1 / EVA (`Scripts/Phase1/`)

| Arquivo | Função |
|---|---|
| `Phase1Manager.cs` | **Manager da Fase 1.** Monta a nave (`ShipTexturePath = res://Sprites/map1.png`, 448x491, `ShipScale=1.5`), cria grips a partir de `GripDefs` (coordenadas em **pixels da imagem**, convertidas por `ImageToWorld`), ferramenta (pistola), marcador da CPS, minigame, HUD, telas de vitória/derrota, validação de layout no `_Ready`, completamento em solo/multiplayer. Também constrói o **grafo de vizinhos** (escalada de um corrimão por vez, `NeighborMaxDistance`), guarda o **relatório da Fase 1 no Global** e, na tela de sucesso, oferece **"Return to Lobby"** em vez de Next/Menu. |
| `EvaPlayer.cs` | Controlador do astronauta: estados Attached/Floating, braço até `ArmReach=140`, corda (tether) com 3 extremidades até arrebentar, jato SAFER, câmera, limites, sprite. |
| `Grip.cs` | Handgrip discreto (Node2D): flags `IsStart`/`IsAirlock`/`IsCps`. **Invisível por padrão** (`ShowVisual=false` não desenha nada; o overlay F2 liga a visualização) e entra no **grafo de vizinhos** do manager (um corrimão por vez, sem pular). |
| `Handrail.cs` | Trilho contínuo antigo (Area2D) — **substituído por Grip; sem uso ativo.** |
| `CpsMinigame.cs` | Minigame de calibração "gire o mouse" (3 pinos, zonas lento/bom/rápido, superaquecimento/congelamento). |

## 5. Scripts — UI (`Scripts/UI/`)

| Arquivo | Função |
|---|---|
| `IntroCutscene.cs` | Vídeo de introdução/cutscene com skip, auto-advance e gancho p/ tutoriais (voz/TAB). |
| `IntroSkipButton.cs` | Botão "Skip [Enter]" que fade-in após delay. |
| `MainMenu.cs` | Menu principal (Start → New Game / Multiplayer / Options / Credits / Leave) + overlay de configurações + toasts. |
| `MatchSetup.cs` | Tela de lobby/setup: nave atravessa a tela, código da sala (6 dígitos), lista de jogadores (apelido/cor/pronto/remover), preview do personagem, Start → Cutscene1. |
| `MultiplayerMenu.cs` | Tela de join: lista de salas da LAN + entrar por código; vai ao lobby. |
| `MissionSummary.cs` | Resumo final da missão (nota F–S + estatísticas + CONTINUE). |
| `ObjectiveHud.cs` | Overlay de objetivos (TAB) lendo `Global.CurrentObjective`. |
| `SettingsMenu.cs` | Configurações (fullscreen, resolução, remap de teclas) com fade. |
| `SpaceshipMotion.cs` | Deriviva/movimento da nave do menu (sines + impulso de partida; usado no flyby). |
| `TabTutorial.cs` | Tutorial "aperte TAB p/ ver objetivos" (libera a ObjectiveHud). |
| `VoiceTutorial.cs` | Tutorial de voz (segurar V), espera todos os jogadores; host pode pular. |

## 6. Scripts — demais

| Arquivo | Função |
|---|---|
| `Scripts/GameManager.cs` | Scene antiga de save/load (posição do `Player`) para `Scenes/Game.tscn`. |
| `Scripts/Player.cs` | Jogador do lobby: platformer com microgravidade, sincronização multiplayer, nomes/cores. |
| `Scripts/VoiceChat.cs` | Push-to-talk: captura bus Microphone → pacotes PCM 16-bit mono via RPC → reproduz com `AudioStreamGenerator`. |
| `Scripts/BackToMainMenu.gd` | Botão "voltar" dos créditos usando `SceneTransitions.GoToMainMenu()`. |
| `Scripts/high_level_network_handler.gd` | **Rede principal:** host/cliente ENet (porta 7777), descoberta UDP (7778), room code, nicknames, personagens, ready, kick, tutorial, start_match. |
| `Scripts/high_level_multiplayer-spawner.gd` | Spawn/despawn de jogadores do lobby por peer id. |
| `Scripts/high_level_ui.gd` | UI LAN antiga (lista de salas, apelido) — base do fluxo atual. |

### Scripts — Lobby (`Scripts/Lobby/`)

| Arquivo | Função |
|---|---|
| `ShipHud.cs` | HUD do lobby: painel de chat/log (sistema, diálogos, jogadores), prompt de interação do Terminal, contagem regressiva de 5 s e fade preto antes da Fase 1. |
| `ShipManager.cs` | Manager do lobby: lê o mapa/zones (Spawn e Terminal), spawna os jogadores (o host decide e transmite via `RpcSpawnPlayer`). O Terminal é host-only: 5 s de contagem → fade → Fase 1. Depois da Fase 1, mostra o relatório (`MissionSummary`, título "LEVEL 1 REPORT") e o Continue vai para a Fase 2, ou loga "LEVEL 2 - NOT AVAILABLE YET" se a cena não existir. |

## 7. Cenas (`Scenes/`)

| Cena | Função |
|---|---|
| `Intro.tscn` | Intro (logo) em vídeo → MainMenu. |
| `MainMenu.tscn` | Menu principal (nave animada + Starfield). |
| `MatchSetup.tscn` | Lobby/setup (nave + painel de jogadores). |
| `multiplayer.tscn` | Join LAN (MultiplayerMenu + VoiceChat + spawner + Starfield). |
| `Game.tscn` | Cena solo legada (GameManager + Player). |
| `Player.tscn` | Jogador do lobby (CharacterBody2D + sincronização de posição/anim). |
| `CreditsMenu.tscn` | Créditos (atribuição "NASA Space Apps"). |
| `Cutscenes/Cutscene1.tscn` | Cutscene 1 (IntroCutscene + **voice tutorial**). |
| `Cutscenes/Cutscene2.tscn` | Cutscene 2 (IntroCutscene + **TAB tutorial**). |
| `Cutscenes/Cutscene3.tscn` | Cutscene 3 (para antes do lobby). |
| `Cutscenes/TabTutorial.tscn`, `VoiceTutorial.tscn` | Overlays reutilizáveis dos tutoriais. |
| `Lobby/lobby.tscn` | Interior da nave (mapa `LobbyMap.png`, polígonos de parede/chão, zones Spawn e Terminal, MultiplayerSpawner). Manager: `ShipManager`. |
| `Phase1/Phase1.tscn` | **Fase 1 ativa:** instancia `Scripts/Phase1/EvaPlayer.tscn`; manager `Phase1Manager`. (Override local: `ArmMaxLength = 160.0` no EvaPlayer.) |
| `Phase1/Phase1_cline.tscn` | Backup da Fase 1 (fora do fluxo). |
| `Phase1/Phase1_old.tscn` | Versão antiga da Fase 1 (mesmo manager; fora do fluxo). |
| `Phase1/EvaPlayer.tscn` | Variante legada do jogador (node custom `EvaPlayer`). |
| `UI/IntroSkipButton.tscn`, `UI/ObjectiveHud.tscn` | Componentes de UI reutilizáveis. |

### Fora do mapa (`Scripts/Phase1/*.tscn`)

| Arquivo | Conteúdo |
|---|---|
| `Scripts/Phase1/EvaPlayer.tscn` | Cena do astronauta usada pela Fase 1 (CharacterBody2D + script `EvaPlayer.cs`). |
| `Scripts/Phase1/EvaPlayer_cline.tscn` | Cópia de backup. |

---

## 8. Assets relevantes (`Sprites/`, `Music/`, `Fontes/`)

| Pasta/Arquivo | Conteúdo |
|---|---|
| `Sprites/map1.png` | **Arte da nave da Fase 1** (448x491, fundo preto, corrimãos dourados). Referenciada por `Phase1Manager.ShipTexturePath`. |
| `Sprites/map2/LobbyMap.png` | Mapa do interior da nave (lobby). |
| `Sprites/Traje_Protecao/` | Trajes espaciais por personagem (Ana/Lucy/Morgana/Roger: Idle/Walk/Jump, frames esquerda/direita, arquivos .aseprite de origem). Usado por `Characters`/`EvaPlayer`. |
| `Sprites/Traje_Padrao/` | Traje antigo (Roger) — legado. |
| `Sprites/UI/MainMenu/` | Arte do menu (botões, nave, estrelas). |
| `Sprites/cutscenes/`, `Monsters/`, `Status/`, `Ansiedade/` | Arte de cutscenes e miscelânea. |
| `Music/` | Pacotes de trilha licenciados (menu usa `Loops - Lonely Nightmare`). **Não existe `Music/sfx/` ainda** — `Sfx.Play` só toca quando o arquivo for colocado lá. |
| `Fontes/Boiled Pasta.ttf` | Fonte padrão do tema. |

---

## 9. Convenções / observações úteis para agentes de IA

- **Coordenadas da Fase 1:** grips, tool e CPS são definidos em **pixels de imagem** em `Phase1Manager` (`GripDefs`, `ToolImagePos`, `CpsImagePos`) e convertidos por `ImageToWorld` (centro da imagem = origem do mundo). `WorldToImage` existe p/ o overlay de debug (F2).
- **Não há `Music/sfx/`**: adicionar sons lá ativa os SFX sem mudar código.
- **Cenas `_old`/`_cline` e `teste.tscn` são backups/testes** — não fazem parte do fluxo.
- **Rede:** autoridade no host (RPCs `AnyPeer`/`Authority`); solo funciona sem peer.
- **`README.md` menciona `SCOPO.md`**, que ainda não existe no repositório.
- **Build C#:** `dotnet build` na raiz é a forma mais rápida de validar sem abrir o editor.

---

## 10. Fase 2 (planejada)

A Fase 2 começa logo após a Fase 1, dentro do DSH (Deep Space Habitat). A comunicação com o
CAPCOM cai quase que instantaneamente e uma fumaça leve se espalha pela nave; ao vestir os
trajes de proteção, a fumaça já cobre metade da visão. A nave vira um labirinto: o grupo se
divide para encontrar as válvulas do ECLSS e o módulo central, restaurando o sistema e
regulando o oxigênio. Cuidado com caixas e choques — tocar em um choque drena a sanidade;
com sanidade baixa, criaturas/vultos começam a surgir.



