extends Node

signal server_started
signal peer_activity(peer_id: int, joined: bool)
signal room_created(room_code: String)
signal room_found(room_code: String, host_ip: String)
signal room_search_completed(room_count: int)
signal player_nickname_changed(peer_id: int, nickname: String)

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
var _rng := RandomNumberGenerator.new()

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
			push_error("Não foi possível responder à busca da sala: %s" % error_string(error))

func start_server(room_code: String = "") -> void:
	if peer != null:
		push_warning("Uma conexão multiplayer já foi iniciada.")
		return

	var selected_code := room_code.strip_edges()
	if selected_code.is_empty():
		selected_code = str(_rng.randi_range(100000, 999999))
	elif not _is_valid_room_code(selected_code):
		push_error("O código da sala deve ter exatamente 6 dígitos.")
		return

	var discovery := PacketPeerUDP.new()
	var error := discovery.bind(DISCOVERY_PORT, "0.0.0.0")
	if error != OK:
		push_error("Não foi possível abrir a porta de descoberta %d: %s" % [DISCOVERY_PORT, error_string(error)])
		return

	var new_peer := ENetMultiplayerPeer.new()
	error = new_peer.create_server(PORT, 4)
	if error != OK:
		discovery.close()
		push_error("Não foi possível iniciar o servidor na porta %d: %s" % [PORT, error_string(error)])
		return

	_room_code = selected_code
	_discovery_peer = discovery
	peer = new_peer
	multiplayer.multiplayer_peer = peer
	_player_nicknames.clear()
	_sync_player_nickname.rpc(multiplayer.get_unique_id(), local_nickname)
	server_started.emit()
	room_created.emit(_room_code)

func _on_peer_connected(peer_id: int) -> void:
	if not multiplayer.is_server():
		return

	for existing_peer_id in _player_nicknames:
		_sync_player_nickname.rpc_id(
			peer_id,
			existing_peer_id,
			_player_nicknames[existing_peer_id]
		)

	_broadcast_peer_activity.rpc(peer_id, true)

func _on_peer_disconnected(peer_id: int) -> void:
	if not multiplayer.is_server():
		return

	_sync_player_nickname.rpc(peer_id, "")
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
		push_warning("Um cliente enviou um apelido vazio ou inválido.")
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
		push_error("O apelido deve ter entre 1 e %d caracteres." % MAX_NICKNAME_LENGTH)
		return false

	var config := ConfigFile.new()
	var load_error := config.load(PROFILE_PATH)
	if load_error != OK and load_error != ERR_FILE_NOT_FOUND:
		push_error("Não foi possível carregar o perfil local: %s" % error_string(load_error))
		return false

	config.set_value("profile", "nickname", clean_nickname)
	var save_error := config.save(PROFILE_PATH)
	if save_error != OK:
		push_error("Não foi possível salvar o apelido local: %s" % error_string(save_error))
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
		push_error("Não foi possível carregar o perfil local: %s" % error_string(error))
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
		push_warning("Uma conexão multiplayer já foi iniciada.")
		return

	var new_peer := ENetMultiplayerPeer.new()
	var error := new_peer.create_client(ip_address, PORT)
	if error != OK:
		push_error("Não foi possível conectar ao servidor %s:%d: %s" % [ip_address, PORT, error_string(error)])
		return

	peer = new_peer
	multiplayer.multiplayer_peer = peer

func start_client_by_room_code(room_code: String) -> void:
	if peer != null:
		push_warning("Uma conexão multiplayer já foi iniciada.")
		return

	var requested_code := room_code.strip_edges()
	if not _is_valid_room_code(requested_code):
		push_error("Digite um código de sala válido com 6 dígitos.")
		return

	var rooms: Array[Dictionary] = await _query_rooms(requested_code)
	if rooms.is_empty():
		push_error("A sala %s não foi encontrada na rede local." % requested_code)
		return

	start_client(rooms[0]["host_ip"])

func discover_rooms() -> void:
	if peer != null:
		push_warning("Desconecte-se antes de procurar salas.")
		return

	var rooms: Array[Dictionary] = await _query_rooms("")
	for room in rooms:
		room_found.emit(room["room_code"], room["host_ip"])
	room_search_completed.emit(rooms.size())

func _query_rooms(room_code_filter: String) -> Array[Dictionary]:
	var search_peer := PacketPeerUDP.new()
	var error := search_peer.bind(0, "0.0.0.0")
	if error != OK:
		push_error("Não foi possível iniciar a busca por salas: %s" % error_string(error))
		return []

	search_peer.set_broadcast_enabled(true)
	error = search_peer.set_dest_address(DISCOVERY_ADDRESS, DISCOVERY_PORT)
	if error != OK:
		search_peer.close()
		push_error("Não foi possível enviar a busca pela rede local: %s" % error_string(error))
		return []

	var request_code := room_code_filter if not room_code_filter.is_empty() else "*"
	var request := DISCOVERY_REQUEST_PREFIX + request_code
	error = search_peer.put_packet(request.to_utf8_buffer())
	if error != OK:
		search_peer.close()
		push_error("Não foi possível enviar a busca pela sala: %s" % error_string(error))
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
