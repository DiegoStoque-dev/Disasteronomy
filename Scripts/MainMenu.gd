extends Node2D

# ============================================================================
# CAMINHOS DAS CENAS ALVO
# ============================================================================
const SCENA_MULTIPLAYER: String = "res://cenas/MultiplayerMenu.tscn"
const SCENA_CONFIG: String = "res://cenas/SettingsMenu.tscn"
const SCENA_CREDITOS: String = "res://cenas/CreditsMenu.tscn"

# ============================================================================
# REFERÊNCIAS AOS NÓS (preenchidas em _ready com validação de nulidade)
# ============================================================================
@onready var btn_start: Button = get_node_or_null("VBoxContainer/btn_start")
@onready var btn_config: Button = get_node_or_null("VBoxContainer/btn_config")
@onready var btn_creditos: Button = get_node_or_null("VBoxContainer/btn_creditos")
@onready var btn_sair: Button = get_node_or_null("VBoxContainer/btn_sair")

@onready var painel_save_modal: Control = get_node_or_null("PainelSaveModal")
@onready var btn_novo_jogo: Button = get_node_or_null("PainelSaveModal/VBoxContainer/btn_novo_jogo")
@onready var btn_continuar: Button = get_node_or_null("PainelSaveModal/VBoxContainer/btn_continuar")
@onready var btn_cancelar_modal: Button = get_node_or_null("PainelSaveModal/VBoxContainer/btn_cancelar")

func _ready() -> void:
	_validar_nos()
	_conectar_sinais()
	_inicializar_modal_oculto()
	_atualizar_botao_continuar()

func _validar_nos() -> void:
	if btn_start == null:
		push_error("MainMenu: Nó 'btn_start' não encontrado em 'VBoxContainer'.")
	if btn_config == null:
		push_error("MainMenu: Nó 'btn_config' não encontrado em 'VBoxContainer'.")
	if btn_creditos == null:
		push_error("MainMenu: Nó 'btn_creditos' não encontrado em 'VBoxContainer'.")
	if btn_sair == null:
		push_error("MainMenu: Nó 'btn_sair' não encontrado em 'VBoxContainer'.")
	if painel_save_modal == null:
		push_error("MainMenu: Nó 'PainelSaveModal' não encontrado na raiz da cena.")
	if btn_novo_jogo == null:
		push_error("MainMenu: Nó 'btn_novo_jogo' não encontrado em 'PainelSaveModal/VBoxContainer'.")
	if btn_continuar == null:
		push_error("MainMenu: Nó 'btn_continuar' não encontrado em 'PainelSaveModal/VBoxContainer'.")
	if btn_cancelar_modal == null:
		push_error("MainMenu: Nó 'btn_cancelar' não encontrado em 'PainelSaveModal/VBoxContainer'.")

func _conectar_sinais() -> void:
	if btn_start != null:
		btn_start.pressed.connect(_on_start_pressed)
	if btn_config != null:
		btn_config.pressed.connect(_on_config_pressed)
	if btn_creditos != null:
		btn_creditos.pressed.connect(_on_creditos_pressed)
	if btn_sair != null:
		btn_sair.pressed.connect(_on_sair_pressed)
	if btn_novo_jogo != null:
		btn_novo_jogo.pressed.connect(_on_novo_jogo_pressed)
	if btn_continuar != null:
		btn_continuar.pressed.connect(_on_continuar_pressed)
	if btn_cancelar_modal != null:
		btn_cancelar_modal.pressed.connect(_on_cancelar_modal_pressed)

func _inicializar_modal_oculto() -> void:
	if painel_save_modal != null:
		painel_save_modal.hide()

func _atualizar_botao_continuar() -> void:
	if btn_continuar == null:
		return
	var global: Global = Global
	if global == null:
		push_error("MainMenu: Singleton 'Global' não encontrado em project.godot.")
		btn_continuar.disabled = true
		return
	btn_continuar.disabled = not global.ExisteSave()

# ============================================================================
# AÇÕES DOS BOTÕES PRINCIPAIS
# ============================================================================

func _on_start_pressed() -> void:
	if painel_save_modal != null:
		painel_save_modal.show()
		if btn_novo_jogo != null:
			btn_novo_jogo.grab_focus()
	_atualizar_botao_continuar()

func _on_config_pressed() -> void:
	_transicionar_para(SCENA_CONFIG)

func _on_creditos_pressed() -> void:
	_transicionar_para(SCENA_CREDITOS)

func _on_sair_pressed() -> void:
	get_tree().quit()

# ============================================================================
# AÇÕES DO MODAL DE SAVE
# ============================================================================

func _on_novo_jogo_pressed() -> void:
	if painel_save_modal != null:
		painel_save_modal.hide()
	var global: Global = Global
	if global != null:
		global.Salvar()
	_transicionar_para(SCENA_MULTIPLAYER)

func _on_continuar_pressed() -> void:
	var global: Global = Global
	if global == null:
		push_error("MainMenu: Singleton 'Global' não encontrado.")
		return
	if not global.ExisteSave():
		btn_continuar.disabled = true
		return
	global.Carregar()
	if painel_save_modal != null:
		painel_save_modal.hide()
	_transicionar_para(SCENA_MULTIPLAYER)

func _on_cancelar_modal_pressed() -> void:
	if painel_save_modal != null:
		painel_save_modal.hide()

# ============================================================================
# TRANSIÇÃO DE CENAS
# ============================================================================

func _transicionar_para(caminho_cena: String) -> void:
	if caminho_cena.is_empty():
		push_error("MainMenu: Caminho da cena está vazio. Verifique as constantes SCENA_*.")
		return
	var scene_transitions: Object = SceneTransitions
	if scene_transitions != null and scene_transitions.has_method("change_scene"):
		scene_transitions.change_scene.call(caminho_cena)
	else:
		get_tree().change_scene_to_file(caminho_cena)
