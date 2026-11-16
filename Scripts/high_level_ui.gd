extends Control

@onready var activity_log: RichTextLabel = get_node_or_null("CanvasLayer/ActivityPanel/ActivityLog")
@onready var nickname_input: LineEdit = $CanvasLayer/VBoxContainer/Nickname
@onready var room_code_input: LineEdit = $CanvasLayer/VBoxContainer/RoomCode
@onready var room_list: ItemList = $CanvasLayer/VBoxContainer/RoomList
@onready var room_status: Label = $CanvasLayer/VBoxContainer/RoomStatus

var _discovered_room_codes: Array[String] = []
var _discovered_room_ips: Array[String] = []

func _ready() -> void:
	nickname_input.text = HighLevelNetworkHandler.local_nickname
	HighLevelNetworkHandler.peer_activity.connect(_on_peer_activity)
	HighLevelNetworkHandler.room_created.connect(_on_room_created)
	HighLevelNetworkHandler.room_found.connect(_on_room_found)
	HighLevelNetworkHandler.room_search_completed.connect(_on_room_search_completed)
	room_list.item_selected.connect(_on_room_selected)

func _on_server_pressed() -> void:
	if not _save_local_nickname():
		return
	HighLevelNetworkHandler.start_server(room_code_input.text)


func _on_client_pressed() -> void:
	if not _save_local_nickname():
		return

	var selected_items := room_list.get_selected_items()
	if selected_items.size() > 0:
		var selected_index: int = selected_items[0]
		if room_code_input.text.strip_edges() == _discovered_room_codes[selected_index]:
			HighLevelNetworkHandler.start_client(_discovered_room_ips[selected_index])
			return

	HighLevelNetworkHandler.start_client_by_room_code(room_code_input.text)

func _on_search_rooms_pressed() -> void:
	room_list.clear()
	_discovered_room_codes.clear()
	_discovered_room_ips.clear()
	room_status.text = "Searching for rooms on the network..."
	HighLevelNetworkHandler.discover_rooms()

func _on_room_found(room_code: String, host_ip: String) -> void:
	room_list.add_item("%s - %s" % [room_code, host_ip])
	_discovered_room_codes.append(room_code)
	_discovered_room_ips.append(host_ip)

func _on_room_search_completed(room_count: int) -> void:
	if room_count == 0:
		room_status.text = "No rooms found."
	else:
		room_status.text = "%d room(s) found." % room_count

func _on_room_selected(index: int) -> void:
	room_code_input.text = _discovered_room_codes[index]

func _on_peer_activity(peer_id: int, joined: bool) -> void:
	var nickname := HighLevelNetworkHandler.get_player_nickname(peer_id)
	if nickname.is_empty():
		nickname = "Player %d" % peer_id
	var action: String = "joined the room" if joined else "left the room"
	if activity_log == null:
		print("%s %s." % [nickname, action])
		return

	activity_log.append_text("%s %s.\n" % [nickname, action])

func _on_room_created(room_code: String) -> void:
	room_code_input.text = room_code

func _save_local_nickname() -> bool:
	return HighLevelNetworkHandler.set_local_nickname(nickname_input.text)
