extends CharacterBody2D
class_name NodeTunnelPlayer

const RESPAWN_TIME = 15
const MAX_HEALTH = 100

var health: int = MAX_HEALTH:
	set(value):
		if health < value and health <= 0:
			$Sprite.texture = load("res://addons/nodetunnel/new_demo/assets/soldier1_gun.png")
		if health >= 0 and health <= MAX_HEALTH:
			health = value
		if health <= 0:
			$Sprite.texture = load("res://addons/nodetunnel/new_demo/assets/soldier1_ded.png")

var score: int = 0:
	set(value):
		if value >= 0 and value < 999999:
			score = value


var aim_direction: Vector2 = Vector2.ZERO
var input_dir: float = 0.0
var speed = 200


func _enter_tree() -> void:
	set_multiplayer_authority(name.to_int())


func _ready() -> void:
	set_process_input(multiplayer.get_unique_id() == get_multiplayer_authority())
	if get_multiplayer_authority() == multiplayer.get_unique_id():
		# TODO init cameras, gameplay attributes, etc
		pass


func to_dict() -> Dictionary:
	return {
		"id": name.to_int(),
		"health": health,
		"score": score
	}

static func from_dict(instance: NodeTunnelPlayer, data: Dictionary):
	instance.health = data.get("health", 0)
	instance.score = data.get("score", 0)


func _unhandled_input(event: InputEvent) -> void:
	if not is_multiplayer_authority():
		return
	if health <= 0: return


func _input(event: InputEvent) -> void:
	if not is_multiplayer_authority():
		return
	if health <= 0: return

	input_dir = Input.get_axis("backward", "forward")

	if Input.is_action_just_pressed("shoot"):
		# "Muzzle" is a Marker2D placed at the barrel of the gun.
		_spawn_bullet.rpc_id(1, $Muzzle.global_position, rotation)

	if event is InputEventMouseMotion:
		aim_direction = get_global_mouse_position() - global_position


func _physics_process(delta):
	if not is_multiplayer_authority(): return
	if health <= 0: return
	
	velocity = transform.x * input_dir * speed

	## Don't move if too close to the mouse pointer.
	if aim_direction.length() > 5:
		rotation = aim_direction.angle()
		move_and_slide()


func _flash_red():
	var flash_tween: Tween = create_tween()
	modulate = Color.RED
	flash_tween.tween_property(self, "modulate", Color.WHITE, 0.15)


func take_damage(killedby: int, amount: int):
	if not multiplayer.is_server(): return
	if health > 0:
		health -= amount
		_flash_red()
		_set_health_rpc.rpc(health, true)
		if health <= 0:
			var parent := get_parent() as NodeTunnelGameMode
			if parent:
				parent.add_score(killedby)
				# show the respawn timer only to the player is currently dead
				_start_respawn_rpc.rpc_id(name.to_int())
				# and server respawns them at the opportune time
				await get_tree().create_timer(RESPAWN_TIME).timeout
				parent.respawn_player(name.to_int())


func _process(delta: float) -> void:
	%Label_Respawn.global_position = global_position + Vector2(-25, -75)
	%Label_Respawn.rotation = 0.0


@rpc("any_peer", "call_local", "reliable")
func _start_respawn_rpc():
	if not is_multiplayer_authority(): return
	%Label_Respawn.visible = true
	for i in range(RESPAWN_TIME):
		_flash_red()
		await get_tree().create_timer(1).timeout
		%Label_Respawn.text = str(i+1)
	await get_tree().create_timer(0.25).timeout
	%Label_Respawn.visible = false


@rpc("any_peer", "call_local", "reliable")
func _set_health_rpc(_health: int, damaged: bool):
	if multiplayer.is_server(): return
	health = _health
	if damaged: _flash_red()


@rpc("any_peer", "call_local", "reliable")
func _spawn_bullet(pos: Vector2, rot: float):
	if not multiplayer.is_server(): return
	var parent := get_parent() as NodeTunnelGameMode
	if parent: parent.add_bullet(pos, rot)
