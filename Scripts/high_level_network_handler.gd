extends Node

signal server_started
signal peer_activity(peer_id: int, joined: bool)
signal room_created(room_code: String)
signal room_found(room_code: String, host_ip: String)
signal room_search_completed(room_count: int)
signal player_nickname_changed(peer_id: int, nickname: String)
signal player_character_changed(peer_id: int, character_index: int)
signal player_ready_changed(peer_id: int, ready: bool)
signal player_kicked(peer_id: int)
signal match_started
signal tutorial_progress_changed(ready_count: int, total_count: int)
signal tutorial_finished
signal all_ready(barrier_name: String)
signal peer_joined_scene(peer_id: int)
signal peer_left(peer_id: int)
signal session_ended
signal rejected_after_start

const PORT: int = 7777
const DISCOVERY_PORT: int = 7778
const DISCOVERY_ADDRESS: String = "255.255.255.255"
const ROOM_CODE_LENGTH: int = 6
const ROOM_SEARCH_TIMEOUT: float = 3.0
const MAX_NICKNAME_LENGTH: int = 16
const PROFILE_PATH: String = "user://player_profile.cfg"
const DISCOVERY_REQUEST_PREFIX: String = "DISASTERONOMY_DISCOVER|"
const DISCOVERY_RESPONSE_PREFIX: String = "DISASTERONOMY_ROOM|"

var peer: ENetMultiplayerPeer
var _discovery_peer: PacketPeerUDP
var _room_code: String = ""
var local_nickname: String = "Jogador"
var _player_nicknames: Dictionary = {}
var _player_characters: Dictionary = {}
var _player_ready: Dictionary = {}
var _tutorial_ready: Dictionary = {}
var _rng := RandomNumberGenerator.new()
var participants: Array = []
var _barriers: Dictionary = {}
var _session_active := false
var _match_started := false

func _ready() -> void:
	_rng.randomize()
	_load_local_nickname()
	multiplayer.peer_connected.connect(_on_peer_connected)
	multiplayer.peer_disconnected.connect(_on_peer_disconnected)
	multiplayer.connected_to_server.connect(_on_connected_to_server)

func _process(_delta: float) -> void:
	if _discovery_peer == null:
		return

	while _discovery_peer.get_available_packet_count() > 0:
		var request: String = _discovery_peer.get_packet().get_string_from_utf8()
		var sender_ip: String = _discovery_peer.get_packet_ip()
		var sender_port: int = _discovery_peer.get_packet_port()
		if (
			request != DISCOVERY_REQUEST_PREFIX + _room_code
			and request != DISCOVERY_REQUEST_PREFIX + "*"
		):
			continue

		_discovery_peer.set_dest_address(sender_ip, sender_port)
		var response := DISCOVERY_RESPONSE_PREFIX + _room_code
		var error := _discovery_peer.put_packet(response.to_utf8_buffer())
		if error != OK:
			push_error("Could not answer the room search: %s" % error_string(error))

func start_server(room_code: String = "") -> void:
	if peer != null:
		push_warning("A multiplayer connection is already active.")
		return

	var selected_code := room_code.strip_edges()
	if selected_code.is_empty():
		selected_code = str(_rng.randi_range(100000, 999999))
	elif not _is_valid_room_code(selected_code):
		push_error("The room code must have exactly 6 digits.")
		return

	var discovery := PacketPeerUDP.new()
	var error := discovery.bind(DISCOVERY_PORT, "0.0.0.0")
	if error != OK:
		push_error("Could not open the discovery port %d: %s" % [DISCOVERY_PORT, error_string(error)])
		return

	var new_peer := ENetMultiplayerPeer.new()
	error = new_peer.create_server(PORT, 4)
	if error != OK:
		discovery.close()
		push_error("Could not start the server on port %d: %s" % [PORT, error_string(error)])
		return

	_room_code = selected_code
	_discovery_peer = discovery
	peer = new_peer
	multiplayer.multiplayer_peer = peer
	_player_nicknames.clear()
	_player_characters.clear()
	_player_ready.clear()
	_tutorial_ready.clear()
	participants.clear()
	_barriers.clear()
	_match_started = false
	_session_active = true
	_sync_player_nickname.rpc(multiplayer.get_unique_id(), local_nickname)
	add_participant(multiplayer.get_unique_id())
	_auto_assign_character(multiplayer.get_unique_id())
	server_started.emit()
	room_created.emit(_room_code)

func _on_peer_connected(peer_id: int) -> void:
	if not multiplayer.is_server():
		return

	if _match_started:
		# The match already started: reject late joiners with a clear message.
		_notify_rejected.rpc_id(peer_id)
		peer.disconnect_peer(peer_id)
		return

	add_participant(peer_id)

	for existing_peer_id in _player_nicknames:
		_sync_player_nickname.rpc_id(
			peer_id,
			existing_peer_id,
			_player_nicknames[existing_peer_id]
		)

	# Send the already claimed characters and ready states to the newcomer.
	for existing_peer_id in _player_characters:
		_sync_player_character.rpc_id(
			peer_id,
			existing_peer_id,
			int(_player_characters[existing_peer_id])
		)
	for existing_peer_id in _player_ready:
		_sync_player_ready.rpc_id(
			peer_id,
			existing_peer_id,
			bool(_player_ready[existing_peer_id])
		)

	# Give the newcomer a free character slot right away.
	_auto_assign_character(peer_id)

	_broadcast_peer_activity.rpc(peer_id, true)

func _on_peer_disconnected(peer_id: int) -> void:
	if not multiplayer.is_server():
		return

	remove_participant(peer_id)
	_remove_peer_from_barriers(peer_id)
	_sync_player_nickname.rpc(peer_id, "")
	# A left peer must not keep the tutorial (or any barrier) blocked: drop its
	# state and re-evaluate so the remaining crew can continue.
	_tutorial_ready.erase(peer_id)
	_player_nicknames.erase(peer_id)
	_player_characters.erase(peer_id)
	_player_ready.erase(peer_id)
	if multiplayer.is_server():
		_emit_tutorial_progress()
		_check_tutorial_complete()
	_sync_player_character.rpc(peer_id, -1)
	_player_ready.erase(peer_id)
	_broadcast_peer_activity.rpc(peer_id, false)

func _on_connected_to_server() -> void:
	_submit_player_nickname.rpc_id(1, local_nickname)

@rpc("authority", "call_local", "reliable")
func _broadcast_peer_activity(peer_id: int, joined: bool) -> void:
	peer_activity.emit(peer_id, joined)

@rpc("any_peer", "call_remote", "reliable")
func _submit_player_nickname(nickname: String) -> void:
	if not multiplayer.is_server():
		return

	var clean_nickname := _sanitize_nickname(nickname)
	if clean_nickname.is_empty():
		push_warning("A client sent an empty or invalid nickname.")
		return

	_sync_player_nickname.rpc(multiplayer.get_remote_sender_id(), clean_nickname)

@rpc("authority", "call_local", "reliable")
func _sync_player_nickname(peer_id: int, nickname: String) -> void:
	if nickname.is_empty():
		_player_nicknames.erase(peer_id)
	else:
		_player_nicknames[peer_id] = nickname

	player_nickname_changed.emit(peer_id, nickname)

func get_player_nickname(peer_id: int) -> String:
	return str(_player_nicknames.get(peer_id, ""))

func set_local_nickname(nickname: String) -> bool:
	var clean_nickname := _sanitize_nickname(nickname)
	if clean_nickname.is_empty():
		push_error("The nickname must be between 1 and %d characters." % MAX_NICKNAME_LENGTH)
		return false

	var config := ConfigFile.new()
	var load_error := config.load(PROFILE_PATH)
	if load_error != OK and load_error != ERR_FILE_NOT_FOUND:
		push_error("Could not load the local profile: %s" % error_string(load_error))
		return false

	config.set_value("profile", "nickname", clean_nickname)
	var save_error := config.save(PROFILE_PATH)
	if save_error != OK:
		push_error("Could not save the local nickname: %s" % error_string(save_error))
		return false

	local_nickname = clean_nickname
	if peer != null and multiplayer.is_server():
		_sync_player_nickname.rpc(multiplayer.get_unique_id(), local_nickname)
	return true

func _load_local_nickname() -> void:
	var config := ConfigFile.new()
	var error := config.load(PROFILE_PATH)
	if error == ERR_FILE_NOT_FOUND:
		return
	if error != OK:
		push_error("Could not load the local profile: %s" % error_string(error))
		return

	var saved_nickname: Variant = config.get_value("profile", "nickname", local_nickname)
	if saved_nickname is String:
		var clean_nickname := _sanitize_nickname(saved_nickname)
		if not clean_nickname.is_empty():
			local_nickname = clean_nickname

func _sanitize_nickname(nickname: String) -> String:
	var clean_nickname := nickname.strip_edges()
	if clean_nickname.length() > MAX_NICKNAME_LENGTH:
		clean_nickname = clean_nickname.substr(0, MAX_NICKNAME_LENGTH)
	return clean_nickname

func start_client(ip_address: String) -> void:
	if peer != null:
		push_warning("A multiplayer connection is already active.")
		return

	var new_peer := ENetMultiplayerPeer.new()
	var error := new_peer.create_client(ip_address, PORT)
	if error != OK:
		push_error("Could not connect to the server %s:%d: %s" % [ip_address, PORT, error_string(error)])
		return

	peer = new_peer
	multiplayer.multiplayer_peer = peer

func start_client_by_room_code(room_code: String) -> void:
	if peer != null:
		push_warning("A multiplayer connection is already active.")
		return

	var requested_code := room_code.strip_edges()
	if not _is_valid_room_code(requested_code):
		push_error("Type a valid room code with 6 digits.")
		return

	var rooms: Array[Dictionary] = await _query_rooms(requested_code)
	if rooms.is_empty():
		push_error("Room %s was not found on the local network." % requested_code)
		return

	start_client(rooms[0]["host_ip"])

func discover_rooms() -> void:
	if peer != null:
		push_warning("Disconnect before searching for rooms.")
		return

	var rooms: Array[Dictionary] = await _query_rooms("")
	for room in rooms:
		room_found.emit(room["room_code"], room["host_ip"])
	room_search_completed.emit(rooms.size())

func _query_rooms(room_code_filter: String) -> Array[Dictionary]:
	var search_peer := PacketPeerUDP.new()
	var error := search_peer.bind(0, "0.0.0.0")
	if error != OK:
		push_error("Could not start the room search: %s" % error_string(error))
		return []

	search_peer.set_broadcast_enabled(true)
	error = search_peer.set_dest_address(DISCOVERY_ADDRESS, DISCOVERY_PORT)
	if error != OK:
		search_peer.close()
		push_error("Could not send the local network search: %s" % error_string(error))
		return []

	var request_code := room_code_filter if not room_code_filter.is_empty() else "*"
	var request := DISCOVERY_REQUEST_PREFIX + request_code
	error = search_peer.put_packet(request.to_utf8_buffer())
	if error != OK:
		search_peer.close()
		push_error("Could not send the room search: %s" % error_string(error))
		return []

	var rooms: Array[Dictionary] = []
	var seen_rooms: Dictionary = {}
	var time_left := ROOM_SEARCH_TIMEOUT
	while time_left > 0.0:
		while search_peer.get_available_packet_count() > 0:
			var response: String = search_peer.get_packet().get_string_from_utf8()
			var host_ip: String = search_peer.get_packet_ip()
			if not response.begins_with(DISCOVERY_RESPONSE_PREFIX):
				continue

			var found_code := response.trim_prefix(DISCOVERY_RESPONSE_PREFIX)
			if not _is_valid_room_code(found_code):
				continue
			if not room_code_filter.is_empty() and found_code != room_code_filter:
				continue

			var room_key := "%s|%s" % [found_code, host_ip]
			if not seen_rooms.has(room_key):
				seen_rooms[room_key] = true
				rooms.append({
					"room_code": found_code,
					"host_ip": host_ip
				})

		await get_tree().create_timer(0.1).timeout
		time_left -= 0.1

	search_peer.close()
	return rooms

func _is_valid_room_code(room_code: String) -> bool:
	return (
		room_code.length() == ROOM_CODE_LENGTH
		and room_code.is_valid_int()
		and int(room_code) >= 100000
		and int(room_code) <= 999999
	)

func stop_multiplayer() -> void:
	# Closes any active session (host or client) and frees the discovery socket.
	# Used when leaving the multiplayer screen back to the main menu.
	if _discovery_peer != null:
		_discovery_peer.close()
		_discovery_peer = null

	if peer != null:
		peer.close()
		peer = null

	multiplayer.multiplayer_peer = null
	_room_code = ""
	_player_nicknames.clear()
	_player_characters.clear()
	_player_ready.clear()
	_tutorial_ready.clear()
	participants.clear()
	_barriers.clear()
	_match_started = false
	_session_active = false
	session_ended.emit()

# ---------------------------------------------------------------------------
# Voice chat tutorial - synchronized with every player in the room.
# Match setup (characters, ready state, kick, start) - used by the lobby screen.
# ---------------------------------------------------------------------------

const CHARACTER_COUNT: int = 4

func get_room_code() -> String:
	return _room_code

func get_player_ids() -> Array:
	# _player_nicknames is kept in sync on every peer, so it lists the whole room.
	return _player_nicknames.keys()

func get_player_character(peer_id: int) -> int:
	return int(_player_characters.get(peer_id, -1))

func get_player_ready(peer_id: int) -> bool:
	return bool(_player_ready.get(peer_id, false))

func set_local_character(character_index: int) -> void:
	if peer == null:
		_player_characters[1] = character_index
		player_character_changed.emit(1, character_index)
		return

	if multiplayer.is_server():
		_assign_character(multiplayer.get_unique_id(), character_index)
	else:
		_submit_character.rpc_id(1, character_index)

func _assign_character(peer_id: int, character_index: int) -> void:
	if not multiplayer.is_server():
		return
	if character_index < 0 or character_index >= CHARACTER_COUNT:
		return

	# Free the chosen character from whoever had it before.
	for other_id in _player_characters.keys():
		if int(_player_characters[other_id]) == character_index and int(other_id) != peer_id:
			_player_characters.erase(other_id)
			_sync_player_character.rpc(int(other_id), -1)

	_player_characters[peer_id] = character_index
	_sync_player_character.rpc(peer_id, character_index)

func _auto_assign_character(peer_id: int) -> void:
	if not multiplayer.is_server():
		return
	if _player_characters.has(peer_id):
		return

	var used: Array = _player_characters.values()
	for index in CHARACTER_COUNT:
		if not used.has(index):
			_assign_character(peer_id, index)
			return
	_assign_character(peer_id, 0)

@rpc("any_peer", "call_remote", "reliable")
func _submit_character(character_index: int) -> void:
	if not multiplayer.is_server():
		return
	_assign_character(multiplayer.get_remote_sender_id(), character_index)

@rpc("authority", "call_local", "reliable")
func _sync_player_character(peer_id: int, character_index: int) -> void:
	if character_index < 0:
		_player_characters.erase(peer_id)
	else:
		_player_characters[peer_id] = character_index
	player_character_changed.emit(peer_id, character_index)

func set_local_ready(value: bool) -> void:
	if peer == null:
		_player_ready[1] = value
		player_ready_changed.emit(1, value)
		return

	if multiplayer.is_server():
		_set_ready_for(multiplayer.get_unique_id(), value)
	else:
		_submit_ready.rpc_id(1, value)

func _set_ready_for(peer_id: int, value: bool) -> void:
	if not multiplayer.is_server():
		return
	_player_ready[peer_id] = value
	_sync_player_ready.rpc(peer_id, value)

@rpc("any_peer", "call_remote", "reliable")
func _submit_ready(value: bool) -> void:
	if not multiplayer.is_server():
		return
	_set_ready_for(multiplayer.get_remote_sender_id(), value)

@rpc("authority", "call_local", "reliable")
func _sync_player_ready(peer_id: int, value: bool) -> void:
	_player_ready[peer_id] = value
	player_ready_changed.emit(peer_id, value)

func set_peer_nickname(peer_id: int, nickname: String) -> void:
	# The host may rename anyone; everyone may rename themselves.
	if peer == null or multiplayer.is_server():
		var clean_nickname := _sanitize_nickname(nickname)
		if not clean_nickname.is_empty():
			_sync_player_nickname.rpc(peer_id, clean_nickname)
		return
	_submit_peer_nickname.rpc_id(1, peer_id, nickname)

@rpc("any_peer", "call_remote", "reliable")
func _submit_peer_nickname(peer_id: int, nickname: String) -> void:
	if not multiplayer.is_server():
		return
	var clean_nickname := _sanitize_nickname(nickname)
	if clean_nickname.is_empty():
		return
	_sync_player_nickname.rpc(peer_id, clean_nickname)

func kick_player(peer_id: int) -> void:
	# Only the host removes players, and never itself.
	if peer == null or not multiplayer.is_server():
		return
	if peer_id == multiplayer.get_unique_id():
		return
	_notify_kicked.rpc(peer_id)
	peer.disconnect_peer(peer_id)

@rpc("authority", "call_local", "reliable")
func _notify_kicked(peer_id: int) -> void:
	player_kicked.emit(peer_id)

func is_solo() -> bool:
	# Solo = no connected peer. Never use Multiplayer.MultiplayerPeer == null:
	# in Godot 4 there is always an offline peer, so rely on the peer list and on
	# the connection status (also avoids the "not active" error while tearing down).
	if not multiplayer.has_multiplayer_peer():
		return true
	var net_peer := multiplayer.multiplayer_peer
	if net_peer.get_connection_status() != MultiplayerPeer.CONNECTION_CONNECTED:
		return true
	return multiplayer.get_peers().is_empty()

func _safe_server_state() -> bool:
	# IsHost: true in solo; in multiplayer check the connection status before
	# calling IsServer() to avoid errors during shutdown.
	if not multiplayer.has_multiplayer_peer():
		return true
	if multiplayer.multiplayer_peer.get_connection_status() != MultiplayerPeer.CONNECTION_CONNECTED:
		return true
	return multiplayer.is_server()

func is_host() -> bool:
	return _safe_server_state()

func has_session() -> bool:
	return _session_active

func start_match() -> void:
	if is_solo():
		# Solo: nothing to synchronize, just fix the participants list and start.
		participants = _player_nicknames.keys().duplicate()
		_match_started = true
		match_started.emit()
		return
	if not multiplayer.is_server():
		return
	participants = _player_nicknames.keys().duplicate()
	_match_started = true
	_sync_participants.rpc(_player_nicknames.keys())
	_start_match.rpc()

@rpc("authority", "call_local", "reliable")
func _start_match() -> void:
	match_started.emit()

func reset_tutorial() -> void:
	_tutorial_ready.clear()
	_emit_tutorial_progress()

func is_tutorial_ready(peer_id: int) -> bool:
	return _tutorial_ready.has(peer_id)

func get_tutorial_ready_count() -> int:
	return _tutorial_ready.size()

func get_tutorial_total_count() -> int:
	var total := _player_nicknames.size()
	return total if total > 0 else 1

func mark_local_tutorial_ready() -> void:
	if peer == null:
		# Solo: nothing to synchronize, this player is the whole room.
		_tutorial_ready[1] = true
		_emit_tutorial_progress()
		tutorial_finished.emit()
		return

	if multiplayer.is_server():
		_set_tutorial_ready(multiplayer.get_unique_id())
	else:
		_submit_tutorial_ready.rpc_id(1)

func _set_tutorial_ready(peer_id: int) -> void:
	if not multiplayer.is_server():
		return
	_sync_tutorial_ready.rpc(peer_id)
	_check_tutorial_complete()

@rpc("any_peer", "call_remote", "reliable")
func _submit_tutorial_ready() -> void:
	if not multiplayer.is_server():
		return
	_set_tutorial_ready(multiplayer.get_remote_sender_id())

@rpc("authority", "call_local", "reliable")
func _sync_tutorial_ready(peer_id: int) -> void:
	_tutorial_ready[peer_id] = true
	_emit_tutorial_progress()

func _emit_tutorial_progress() -> void:
	tutorial_progress_changed.emit(get_tutorial_ready_count(), get_tutorial_total_count())

func _check_tutorial_complete() -> void:
	# Only the host decides when everybody is done.
	if not multiplayer.is_server():
		return
	if _player_nicknames.is_empty():
		return
	for peer_id in _player_nicknames.keys():
		if not _tutorial_ready.has(peer_id):
			return
	_finish_tutorial.rpc()

func host_skip_tutorial() -> void:
	# The host may skip the tutorial for everyone.
	if peer == null:
		tutorial_finished.emit()
		return
	if not multiplayer.is_server():
		return
	_finish_tutorial.rpc()

@rpc("authority", "call_local", "reliable")
func _finish_tutorial() -> void:
	tutorial_finished.emit()

# ---------------------------------------------------------------------------
# NetSession - explicit participant list, scene barriers and session lifecycle.
# The host is the only authority: every RPC of decision here is Authority and
# never trusts a peer id passed as an argument (uses GetRemoteSenderId()).
# ---------------------------------------------------------------------------

func setup_participants(value: Array) -> void:
	participants = value.duplicate()

func get_participants() -> Array:
	return participants.duplicate()

func get_player_ids() -> Array:
	return _player_nicknames.keys()

func add_participant(peer_id: int) -> void:
	if not participants.has(peer_id):
		participants.append(peer_id)

func remove_participant(peer_id: int) -> void:
	participants.erase(peer_id)

func mark_ready(barrier_name: String) -> void:
	# Barrier confirmation entry point (single place). In solo it resolves
	# immediately; in multiplayer it goes to the host which re-broadcasts.
	if is_solo():
		if not _barriers.has(barrier_name):
			_barriers[barrier_name] = {}
		_barriers[barrier_name][1] = true
		all_ready.emit(barrier_name)
		return
	if multiplayer.is_server():
		_register_barrier_ready(barrier_name, multiplayer.get_unique_id())
	else:
		_submit_barrier_ready.rpc_id(1, barrier_name)

func _register_barrier_ready(barrier_name: String, peer_id: int) -> void:
	if not multiplayer.is_server():
		return
	_sync_barrier_ready.rpc(barrier_name, peer_id)
	_recheck_barrier(barrier_name)

@rpc("any_peer", "call_remote", "reliable")
func _submit_barrier_ready(barrier_name: String) -> void:
	if not multiplayer.is_server():
		return
	_register_barrier_ready(barrier_name, multiplayer.get_remote_sender_id())

@rpc("authority", "call_local", "reliable")
func _sync_barrier_ready(barrier_name: String, peer_id: int) -> void:
	if not _barriers.has(barrier_name):
		_barriers[barrier_name] = {}
	_barriers[barrier_name][peer_id] = true
	peer_joined_scene.emit(peer_id)
	if multiplayer.is_server():
		_recheck_barrier(barrier_name)

func _recheck_barrier(barrier_name: String) -> void:
	# Host only: when every participant has confirmed, unlock the barrier for all.
	if not multiplayer.is_server():
		return
	if not _barriers.has(barrier_name):
		return
	var ready_peers: Array = _barriers[barrier_name].keys()
	var required: Array = participants.duplicate()
	if required.is_empty():
		required = _player_nicknames.keys()
	for pid in required:
		if not ready_peers.has(pid):
			return
	_all_ready.rpc(barrier_name)

@rpc("authority", "call_local", "reliable")
func _all_ready(barrier_name: String) -> void:
	all_ready.emit(barrier_name)

func is_ready(barrier_name: String, peer_id: int) -> bool:
	return _barriers.has(barrier_name) and _barriers[barrier_name].has(peer_id)

func clear_barriers() -> void:
	_barriers.clear()

func _mark_peer_left(peer_id: int) -> void:
	peer_left.emit(peer_id)

func _remove_peer_from_barriers(peer_id: int) -> void:
	_mark_peer_left(peer_id)
	for barrier_name in _barriers.keys().duplicate():
		if _barriers[barrier_name].has(peer_id):
			_barriers[barrier_name].erase(peer_id)
			if multiplayer.is_server():
				_recheck_barrier(barrier_name)
	for barrier_name in _barriers.keys().duplicate():
		if _barriers[barrier_name].is_empty():
			_barriers.erase(barrier_name)

@rpc("authority", "call_local", "reliable")
func _sync_participants(value: Array) -> void:
	participants = value.duplicate()

@rpc("authority", "call_remote", "reliable")
func _notify_rejected() -> void:
	rejected_after_start.emit()

func end_session() -> void:
	# Single session reset entry point (back to menu, ServerDisconnected, kick).
	stop_multiplayer()
