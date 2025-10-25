class_name LobbyMetadata

const REQUIRED_FIELDS = ["Name", "Mode", "Map", "MaxPlayers", "IsPassworded"]

## IMPORTANT: Optional fields, these MUST match on the server as well.
const ALLOWED_OPTIONAL_FIELDS = [
	"Version",
	"Region",
	"Difficulty",
	"Ranked",
	"CustomRules",
	"Tags",
	"MinLevel",
	"MaxLevel",
	"ServerMods",
	"VoiceChat"
]

const MAX_STRING_LENGTH = 200

static func create(name: String, mode: String, map: String, max_players: int, is_passworded: bool, custom_fields: Dictionary = {}) -> Dictionary:
	var metadata = {
		"Name": name,
		"Mode": mode,
		"Map": map,
		"MaxPlayers": max_players,
		"IsPassworded": is_passworded
	}
	
	for key in custom_fields:
		if key in ALLOWED_OPTIONAL_FIELDS:
			metadata[key] = custom_fields[key]
		else:
			push_warning("LobbyMetadata: Ignoring disallowed field: " + str(key))
	
	return metadata

static func validate(metadata: Dictionary) -> ValidationResult:
	for field in REQUIRED_FIELDS:
		if not metadata.has(field):
			return ValidationResult.new(false, "Missing required field: " + field)
	
	if not metadata["Name"] is String:
		return ValidationResult.new(false, "Name must be a string")
	
	if not metadata["Mode"] is String:
		return ValidationResult.new(false, "Mode must be a string")
	
	if not metadata["Map"] is String:
		return ValidationResult.new(false, "Map must be a string")
	
	if not metadata["MaxPlayers"] is int:
		return ValidationResult.new(false, "MaxPlayers must be an integer")
	
	if not metadata["IsPassworded"] is bool:
		return ValidationResult.new(false, "IsPassworded must be a boolean")
	
	if metadata["Name"].length() > MAX_STRING_LENGTH:
		return ValidationResult.new(false, "Name exceeds maximum length")
	
	if metadata["Mode"].length() > MAX_STRING_LENGTH:
		return ValidationResult.new(false, "Mode exceeds maximum length")
	
	if metadata["Map"].length() > MAX_STRING_LENGTH:
		return ValidationResult.new(false, "Map exceeds maximum length")
	
	if metadata["MaxPlayers"] < 1 or metadata["MaxPlayers"] > 1000:
		return ValidationResult.new(false, "MaxPlayers must be between 1 and 1000")
	
	for key in metadata:
		if key not in REQUIRED_FIELDS and key not in ALLOWED_OPTIONAL_FIELDS:
			if key != "LobbyId" and key != "CurrentPlayers":
				return ValidationResult.new(false, "Disallowed field: " + str(key))
	
	return ValidationResult.new(true)

class ValidationResult:
	var is_valid: bool
	var error_message: String
	
	func _init(valid: bool, message: String = ""):
		is_valid = valid
		error_message = message
