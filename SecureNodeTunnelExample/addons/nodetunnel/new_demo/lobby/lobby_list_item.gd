extends PanelContainer

signal join_pressed()

@onready var name_label = %NameLabel
@onready var mode_label = %ModeLabel
@onready var map_label = %MapLabel
@onready var players_label = %PlayersLabel
@onready var password_label = %PasswordLabel
@onready var join_button = %JoinButton

func _ready() -> void:
	join_button.pressed.connect(_on_join_pressed)

func set_lobby_data(lobby: Dictionary) -> void:
	name_label.text = lobby.get("Name", "Unknown")
	mode_label.text = "Mode: " + lobby.get("Mode", "N/A")
	map_label.text = "Map: " + lobby.get("Map", "N/A")

	var current = lobby.get("CurrentPlayers", 0)
	var max_players = lobby.get("MaxPlayers", 0)
	players_label.text = "Players: %d/%d" % [current, max_players]

	var is_passworded = lobby.get("IsPassworded", false)
	password_label.visible = is_passworded

	if current >= max_players:
		join_button.disabled = true
		join_button.text = "Full"

func _on_join_pressed() -> void:
	join_pressed.emit()
