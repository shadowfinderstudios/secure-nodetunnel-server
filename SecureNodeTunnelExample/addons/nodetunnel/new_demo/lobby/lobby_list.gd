extends Control

var REGISTRY_URL: String

@onready var lobby_container = %LobbyContainer
@onready var refresh_button = %RefreshButton
@onready var loading_label = %LoadingLabel
@onready var error_label = %ErrorLabel

var lobby_item_scene = preload("res://addons/nodetunnel/new_demo/lobby/lobby_list_item.tscn")

var use_refresh_timer: bool = true

var _refresh_timer: Timer = null
var _refresh_interval: int = 15

signal lobby_selected(lobby_id: String)


func _ready() -> void:
	REGISTRY_URL = OS.get_environment("NODETUNNEL_REGISTRY")
	if not REGISTRY_URL or REGISTRY_URL.is_empty():
		REGISTRY_URL = "http://secure.nodetunnel.io:8099"
	else:
		if not REGISTRY_URL.begins_with("http://") and not REGISTRY_URL.begins_with("https://"):
			REGISTRY_URL = "http://" + REGISTRY_URL
		if not REGISTRY_URL.contains(":8099"):
			REGISTRY_URL = REGISTRY_URL + ":8099"

	print("Lobby list using registry: ", REGISTRY_URL)
	refresh_button.pressed.connect(_on_refresh_pressed)
	refresh_lobbies()
	
	if use_refresh_timer:
		_refresh_timer = Timer.new()
		_refresh_timer.wait_time = _refresh_interval
		_refresh_timer.timeout.connect(refresh_lobbies)
		add_child(_refresh_timer)
		_refresh_timer.start()



func _on_refresh_pressed() -> void:
	refresh_lobbies()


func refresh_lobbies() -> void:
	loading_label.show()
	error_label.hide()
	refresh_button.disabled = true

	for child in lobby_container.get_children():
		child.queue_free()

	var http = HTTPRequest.new()
	add_child(http)
	http.request_completed.connect(_on_lobbies_fetched.bind(http))

	var error = http.request(REGISTRY_URL + "/lobbies")
	if error != OK:
		_show_error("Failed to connect to registry service")
		loading_label.hide()
		refresh_button.disabled = false


func _on_lobbies_fetched(result: int, response_code: int, headers: PackedStringArray, body: PackedByteArray, http: HTTPRequest) -> void:
	loading_label.hide()
	refresh_button.disabled = false
	http.queue_free()

	if result != HTTPRequest.RESULT_SUCCESS:
		_show_error("Network error occurred")
		return

	if response_code != 200:
		_show_error("Server returned error: " + str(response_code))
		return

	var json_text = body.get_string_from_utf8()
	var json = JSON.new()
	var parse_result = json.parse(json_text)

	if parse_result != OK:
		_show_error("Failed to parse response")
		return

	var data = json.get_data()
	if typeof(data) != TYPE_DICTIONARY or not data.has("lobbies"):
		_show_error("Invalid response format")
		return

	var lobbies = data["lobbies"]
	if lobbies.size() == 0:
		_show_error("No lobbies available")
		return

	for lobby in lobbies:
		var item = lobby_item_scene.instantiate()
		lobby_container.add_child(item)
		item.set_lobby_data(lobby)
		item.join_pressed.connect(_on_lobby_join_pressed.bind(lobby["LobbyId"]))


func _on_lobby_join_pressed(lobby_id: String) -> void:
	lobby_selected.emit(lobby_id)


func _show_error(message: String) -> void:
	error_label.text = message
	error_label.show()
