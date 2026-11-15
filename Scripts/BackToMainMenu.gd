extends Control

@onready var btn_back: Button = get_node_or_null("VBoxContainer/BtnBack")

func _ready() -> void:
	if btn_back == null:
		push_error("BackToMainMenu: Node 'BtnBack' not found in 'VBoxContainer'.")
		return
	btn_back.pressed.connect(_on_back_pressed)

func _on_back_pressed() -> void:
	get_tree().change_scene_to_file("res://scenes/MainMenu.tscn")
