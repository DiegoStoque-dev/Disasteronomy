extends Node

signal server_started
signal peer_activity(peer_id: int, joined: bool)
signal room_created(room_code: String)
signal room_found(room_code: String, host_ip: String)
signal room_search_completed(room_count: int)

const PORT: int = 7777
const DISCOVERY_PORT: int = 7778
const DISCOVERY_ADDRESS: String = "255.255.255.255"
const ROOM_CODE_LENGTH: int = 6
const ROOM_SEARCH_TIMEOUT: float = 3.0
const DISCOVERY_REQUEST_PREFIX: String = "DISASTERONOMY_DISCOVER|"
const DISCOVERY_RESPONSE_PREFIX: String = "DISASTERONOMY_ROOM|"

var peer: ENetMultiplayerPeer
var _discovery_peer: PacketPeerUDP
var _room_code: String = ""
var _rng := RandomNumberGenerator.new()

func _ready() -> void:
	_rng.randomize()
	multiplayer.peer_connected.connect(_on_peer_connected)
	multiplayer.peer_disconnected.connect(_on_peer_disconnected)

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
	server_started.emit()
	room_created.emit(_room_code)

func _on_peer_connected(peer_id: int) -> void:
	if multiplayer.is_server():
		_broadcast_peer_activity.rpc(peer_id, true)

func _on_peer_disconnected(peer_id: int) -> void:
	if multiplayer.is_server():
		_broadcast_peer_activity.rpc(peer_id, false)

@rpc("authority", "call_local", "reliable")
func _broadcast_peer_activity(peer_id: int, joined: bool) -> void:
	peer_activity.emit(peer_id, joined)

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
