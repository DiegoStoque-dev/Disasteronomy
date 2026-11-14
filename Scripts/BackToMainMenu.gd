extends Control

@onready var btn_voltar: Button = get_node_or_null("VBoxContainer/BtnVoltar")

func _ready() -> void:
    if btn_voltar == null:
        push_error("BackToMainMenu: Nó 'BtnVoltar' não encontrado em 'VBoxContainer'.")
        return
    btn_voltar.pressed.connect(_on_voltar_pressed)

func _on_voltar_pressed() -> void:
    get_tree().change_scene_to_file("res://Scenes/MainMenu.tscn")