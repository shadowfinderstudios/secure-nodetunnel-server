extends Node2D

const PLAYER_SCENE = preload("res://addons/nodetunnel/demo/player/node_tunnel_demo_player.tscn")

var peer: NodeTunnelPeer = NodeTunnelPeer.new()
var players: Array[Player] = []

func _ready() -> void:
	#peer.debug_enabled = true
	multiplayer.multiplayer_peer = peer
	peer.set_encryption_enabled(true)
	#peer.connect_to_relay("127.0.0.1", 9998)
	peer.connect_to_relay("secure.nodetunnel.io", 9998)
	await peer.relay_connected
	peer.peer_connected.connect(_add_player)
	peer.peer_disconnected.connect(_remove_player)
	peer.room_left.connect(_cleanup_room)
	%IDLabel.text = "Online ID: " + peer.online_id

func _on_host_pressed() -> void:
	print("Online ID: ", peer.online_id)
	peer.host()
	DisplayServer.clipboard_set(peer.online_id)
	await peer.hosting
	_add_player()
	%ConnectionControls.hide()
	%LeaveRoom.show()

func _on_join_pressed() -> void:
	peer.join(%HostID.text)
	await peer.joined
	%ConnectionControls.hide()
	%LeaveRoom.show()

func _add_player(peer_id: int = 1) -> Node:
	if !multiplayer.is_server(): return
	print("Player Joined: ", peer_id)
	var player = PLAYER_SCENE.instantiate()
	player.name = str(peer_id)
	player.global_position = $Level.get_child(players.size()).global_position
	add_child(player)
	players.append(player)
	return player

func get_random_spawnpoint():
	return $Level.get_children().pick_random().global_position

func _remove_player(peer_id: int) -> void:
	if !multiplayer.is_server(): return
	var player = get_node(str(peer_id))
	player.queue_free()

func _on_leave_room_pressed() -> void:
	peer.leave_room()

func _cleanup_room() -> void:
	%LeaveRoom.hide()
	%ConnectionControls.show()
