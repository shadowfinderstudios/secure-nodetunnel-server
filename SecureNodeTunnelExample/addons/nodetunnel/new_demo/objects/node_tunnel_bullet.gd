extends CharacterBody2D
class_name NodeTunnelBullet

const DAMAGE = 10

var speed: int = 750
var ownerid: int = 0

func _enter_tree() -> void:
	set_multiplayer_authority(1)


func start(_ownerid: int, _position: Vector2, _direction: float):
	name = "bullet"
	ownerid = _ownerid
	rotation = _direction
	position = _position
	velocity = Vector2(speed, 0).rotated(rotation)


func _physics_process(delta):
	if not multiplayer.is_server(): return
	var collision = move_and_collide(velocity * delta)
	if collision:
		velocity = velocity.bounce(collision.get_normal())
		var collider: Node = collision.get_collider()
		if collider.has_method("hit"):
			collision.get_collider().hit()
		elif collider.has_method("take_damage"):
			collision.get_collider().take_damage(ownerid, DAMAGE)
			queue_free()


func _on_VisibilityNotifier2D_screen_exited():
	if not multiplayer.is_server(): return
	# Deletes the bullet when it exits the screen.
	queue_free()
