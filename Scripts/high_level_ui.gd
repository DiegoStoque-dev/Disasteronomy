extends Control

@onready var activity_log: RichTextLabel = get_node_or_null("CanvasLayer/ActivityPanel/ActivityLog")
@onready var room_code_input: LineEdit = $CanvasLayer/VBoxContainer/RoomCode

func _ready() -> void:
	HighLevelNetworkHandler.peer_activity.connect(_on_peer_activity)
	HighLevelNetworkHandler.room_created.connect(_on_room_created)

func _on_server_pressed() -> void:
	HighLevelNetworkHandler.start_server(room_code_input.text)


func _on_client_pressed() -> void:
	HighLevelNetworkHandler.start_client_by_room_code(room_code_input.text)

func _on_peer_activity(peer_id: int, joined: bool) -> void:
	var action: String = "entrou na sala" if joined else "saiu da sala"
	if activity_log == null:
		print("Jogador %d %s." % [peer_id, action])
		return

	activity_log.append_text("Jogador %d %s.\n" % [peer_id, action])

func _on_room_created(room_code: String) -> void:
	room_code_input.text = room_code
