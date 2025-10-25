extends Node
class_name NodeTunnelGameMode

const MAX_PLAYERS = 4

const PLAYER_SCENE = preload("res://addons/nodetunnel/new_demo/player/node_tunnel_player.tscn")

var peer: NodeTunnelPeer = NodeTunnelPeer.new()
var players: Array[NodeTunnelPlayer] = []
var bullets: Array[NodeTunnelBullet] = []
var relay_ready: bool = false

var world_state_data: Dictionary = {}


func _quit_game():
	if peer and (peer.connection_state == NodeTunnelPeer.ConnectionState.JOINED
			or peer.connection_state == NodeTunnelPeer.ConnectionState.HOSTING):
		peer.leave_room()
	get_tree().quit()


func _notification(what):
	# Windows / Linux
	if what == NOTIFICATION_WM_CLOSE_REQUEST:
		_quit_game()
	# Android
	elif what == NOTIFICATION_WM_GO_BACK_REQUEST:
		_quit_game()


func _perform_spawn_player(data: Dictionary) -> Node:
	var id: int = data["id"]
	var player: NodeTunnelPlayer = PLAYER_SCENE.instantiate()
	player.set_multiplayer_authority(id)
	player.name = str(id)
	player.position = data["position"]
	players.append(player)
	return player


func _perform_spawn_bullet(data: Dictionary) -> Node:
	var bullet: Node = preload("res://addons/nodetunnel/new_demo/objects/node_tunnel_bullet.tscn").instantiate()
	bullet.start(data["ownerid"], data["position"], data["rotation"])
	bullets.append(bullet)
	return bullet


func _ready() -> void:
	if multiplayer.is_server():
		$PlayerSpawner.set_spawn_function(Callable(self, "_perform_spawn_player"))
		$BulletSpawner.set_spawn_function(Callable(self, "_perform_spawn_bullet"))

	#peer.debug_enabled = true
	multiplayer.multiplayer_peer = peer
	peer.set_encryption_enabled(true)

	var relay: String = OS.get_environment("NODETUNNEL_RELAY")
	if relay:
		peer.connect_to_relay(relay, 9998)
	else:
		peer.connect_to_relay("secure.nodetunnel.io", 9998)

	await peer.relay_connected
	peer.peer_connected.connect(_add_player)
	peer.peer_disconnected.connect(_remove_player)
	peer.room_left.connect(_cleanup_room)
	
	%IDLabel.text = "Online ID: " + peer.online_id
	relay_ready = true


func _on_host_pressed() -> void:
	if not relay_ready:
		print("Waiting for relay connection...")
		await peer.relay_connected
		relay_ready = true

	var lobby_name = %LobbyNameInput.text
	if lobby_name.is_empty():
		lobby_name = "My Lobby"

	var metadata = LobbyMetadata.create(
		lobby_name,
		"Deathmatch",
		"Arena",
		MAX_PLAYERS,
		false,
		{
			"Version": "1.0.0",
			"Region": "US-West",
			"Difficulty": "Normal"
		}
	)

	var registry: String = OS.get_environment("NODETUNNEL_REGISTRY")
	if not registry or registry.is_empty():
		registry = "http://secure.nodetunnel.io:8099"
	else:
		if not registry.begins_with("http://") and not registry.begins_with("https://"):
			registry = "http://" + registry
		if not registry.contains(":8099"):
			registry = registry + ":8099"

	print("Using registry: ", registry)
	peer.enable_lobby_registration(self, registry, metadata)

	print("Online ID: ", peer.online_id)
	peer.host()
	
	DisplayServer.clipboard_set(peer.online_id)
	
	await peer.hosting
	
	_add_player()
	%ConnectionControls.hide()
	%LeaveRoom.show()

## These two funcs are the world state rpc are where you handle late joins.

@rpc("any_peer", "call_local", "reliable")
func receive_world_state(_world_state_data: Dictionary):
	if multiplayer.is_server(): return
	print("World state received from server.")
	world_state_data = _world_state_data
	var found_us: bool = false
	for i in range(players.size()):
		var id: int = players[i].name.to_int()
		NodeTunnelPlayer.from_dict(players[i], world_state_data[id])
		if id == multiplayer.get_unique_id():
			found_us = true
		if id == 1: %Label_Score1.text = str(players[i].score)
		elif id == 2: %Label_Score2.text = str(players[i].score)
		elif id == 3: %Label_Score3.text = str(players[i].score)
		elif id == 4: %Label_Score4.text = str(players[i].score)
	if not found_us:
		# only 4 players, and we're not one of them
		# note: you could remove this to have spectators
		peer.leave_room()



@rpc("any_peer", "call_local", "reliable")
func fetch_world_state():
	if not multiplayer.is_server(): return
	var player_states:Dictionary = {}
	for i in range(players.size()):
		var id: int = players[i].name.to_int()
		player_states[id] = players[i].to_dict()
	rpc_id(multiplayer.get_remote_sender_id(), "receive_world_state", player_states)


func _on_join_pressed() -> void:
	peer.join(%HostID.text)
	await peer.joined
	fetch_world_state.rpc_id(1)
	%ConnectionControls.hide()
	%LeaveRoom.show()


func _on_browse_lobbies_pressed() -> void:
	%LobbyListContainer.show()
	_load_lobby_list()


func _on_close_lobby_list_pressed() -> void:
	%LobbyListContainer.hide()


func _load_lobby_list() -> void:
	var placeholder = %LobbyListPlaceholder
	if placeholder.get_child_count() == 0:
		var lobby_list_scene = preload("res://addons/nodetunnel/new_demo/lobby/lobby_list.tscn")
		var lobby_list = lobby_list_scene.instantiate()
		placeholder.add_child(lobby_list)
		lobby_list.lobby_selected.connect(_on_lobby_selected)
		lobby_list.anchors_preset = Control.PRESET_FULL_RECT
		lobby_list.anchor_right = 1.0
		lobby_list.anchor_bottom = 1.0
	else:
		var lobby_list = placeholder.get_child(0)
		lobby_list.refresh_lobbies()


func _on_lobby_selected(lobby_id: String) -> void:
	%LobbyListContainer.hide()
	%HostID.text = lobby_id
	_on_join_pressed()


func _add_player(peer_id: int = 1) -> Node:
	if !multiplayer.is_server(): return
	if players.size()+1 > MAX_PLAYERS:
		print("Cannot accept more than ",MAX_PLAYERS," players.")
		return
	print("Player Joined: ", peer_id)
	var pos: Vector2 = $Level.get_children()[players.size() % MAX_PLAYERS].global_position
	return $PlayerSpawner.spawn({"id": peer_id, "position": pos})


func _get_random_spawnpoint():
	return $Level.get_children().pick_random().global_position


func _remove_player(peer_id: int) -> void:
	if !multiplayer.is_server(): return
	var player = get_node(str(peer_id))
	if player:
		players.erase(player)
		player.queue_free()


func _on_leave_room_pressed() -> void:
	peer.leave_room()


func _cleanup_room() -> void:
	%LeaveRoom.hide()
	%ConnectionControls.show()
	players = []
	bullets = []
	world_state_data = {}
	reset_scores()


func _on_close_button_pressed() -> void:
	%LobbyListContainer.hide()


func add_bullet(pos: Vector2, rot: float) -> void:
	$BulletSpawner.spawn({"ownerid": multiplayer.get_remote_sender_id(), "position": pos, "rotation": rot})


func add_score(playerid: int):
	for i in range(players.size()):
		var id: int = players[i].name.to_int()
		if playerid == id:
			players[i].score = players[i].score + 1
			_update_score_rpc.rpc(playerid, players[i].score)
			if id == 1: %Label_Score1.text = str(players[i].score)
			elif id == 2: %Label_Score2.text = str(players[i].score)
			elif id == 3: %Label_Score3.text = str(players[i].score)
			elif id == 4: %Label_Score4.text = str(players[i].score)
			break


func respawn_player(playerid: int):
	for i in range(players.size()):
		var id: int = players[i].name.to_int()
		if playerid == id:
			players[i].health = players[i].MAX_HEALTH
			_update_health_rpc.rpc(playerid, players[i].health)
			break


func reset_scores():
	%Label_Score1.text = "0"
	%Label_Score2.text = "0"
	%Label_Score3.text = "0"
	%Label_Score4.text = "0"



@rpc("any_peer", "call_local", "reliable")
func _update_score_rpc(playerid: int, score: int):
	if multiplayer.is_server(): return
	for i in range(players.size()):
		var id: int = players[i].name.to_int()
		if playerid == id:
			players[i].score = score
			if id == 1: %Label_Score1.text = str(score)
			elif id == 2: %Label_Score2.text = str(score)
			elif id == 3: %Label_Score3.text = str(score)
			elif id == 4: %Label_Score4.text = str(score)
			break


@rpc("any_peer", "call_local", "reliable")
func _update_health_rpc(playerid: int, health: int):
	if multiplayer.is_server(): return
	for i in range(players.size()):
		var id: int = players[i].name.to_int()
		if playerid == id:
			players[i].health = health
			break
