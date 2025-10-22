# ============================================================================
# NodeTunnel Packet Encryption
# ============================================================================
# Provides AES-256 encryption for NodeTunnel packets

class_name PacketEncryption

## The master encryption key from your server
## IMPORTANT: Replace this with the key from your server's master_key.txt
var MASTER_KEY: String = "REPLACE_WITH_YOUR_MASTER_KEY_FROM_SERVER"

var _key: PackedByteArray
var _enabled: bool = true

func _init(enabled: bool = true):
	_enabled = enabled
	if _enabled:
		_initialize_key()

func _initialize_key() -> void:
	var newkey: String = OS.get_environment("NODETUNNEL_MASTER_KEY")
	if newkey: MASTER_KEY = newkey
	
	if MASTER_KEY == "REPLACE_WITH_YOUR_MASTER_KEY_FROM_SERVER":
		push_error("PacketEncryption: Master key not set! Please update MASTER_KEY constant.")
		_enabled = false
		return
	
	_key = Marshalls.base64_to_raw(MASTER_KEY)
	
	if _key.size() != 32:
		push_error("PacketEncryption: Invalid key size. Expected 32 bytes, got %d" % _key.size())
		_enabled = false
		return
	
	print("[Encryption] Initialized with AES-256")

## Encrypts data using AES-256-CBC
## Returns [IV + Encrypted Data] or original data if encryption disabled
func encrypt(data: PackedByteArray) -> PackedByteArray:
	if not _enabled or _key.is_empty():
		return data
	
	if data.is_empty():
		return data
	
	# Add PKCS7 padding (AES requires data to be multiple of 16 bytes)
	var padded_data = _add_padding(data)
	
	var aes = AESContext.new()
	
	# Generate random IV (16 bytes)
	var iv = PackedByteArray()
	iv.resize(16)
	for i in range(16):
		iv[i] = randi() % 256
	
	# Encrypt with AES-256-CBC
	var error = aes.start(AESContext.MODE_CBC_ENCRYPT, _key, iv)
	if error != OK:
		push_error("PacketEncryption: Failed to start encryption: %d" % error)
		return data
	
	var encrypted = aes.update(padded_data)
	aes.finish()
	
	# Return [IV + Encrypted Data]
	var result = iv + encrypted
	return result

## Decrypts data using AES-256-CBC
## Expects [IV + Encrypted Data] or returns original if encryption disabled
func decrypt(data: PackedByteArray) -> PackedByteArray:
	if not _enabled or _key.is_empty():
		return data
	
	if data.is_empty():
		return PackedByteArray()
	
	# Need at least IV (16 bytes) + one encrypted block (16 bytes)
	if data.size() < 32:
		push_warning("PacketEncryption: Data too small to decrypt (size: %d)" % data.size())
		return PackedByteArray()
	
	var aes = AESContext.new()
	
	# Extract IV from first 16 bytes
	var iv = data.slice(0, 16)
	var encrypted_data = data.slice(16)
	
	# Decrypt with AES-256-CBC
	var error = aes.start(AESContext.MODE_CBC_DECRYPT, _key, iv)
	if error != OK:
		push_error("PacketEncryption: Failed to start decryption: %d" % error)
		return PackedByteArray()
	
	var decrypted = aes.update(encrypted_data)
	aes.finish()
	
	# Remove PKCS7 padding
	var unpadded = _remove_padding(decrypted)
	return unpadded

## Enable or disable encryption at runtime
func set_enabled(enabled: bool) -> void:
	_enabled = enabled
	if _enabled and _key.is_empty():
		_initialize_key()

## Check if encryption is enabled
func is_enabled() -> bool:
	return _enabled and not _key.is_empty()

## Add PKCS7 padding to data
func _add_padding(data: PackedByteArray) -> PackedByteArray:
	var block_size = 16
	var padding_needed = block_size - (data.size() % block_size)
	
	# PKCS7: pad with the number of padding bytes
	var padded = data.duplicate()
	for i in range(padding_needed):
		padded.append(padding_needed)
	
	return padded

## Remove PKCS7 padding from data
func _remove_padding(data: PackedByteArray) -> PackedByteArray:
	if data.is_empty():
		return data
	
	# Last byte tells us how many padding bytes there are
	var padding_length = data[data.size() - 1]
	
	# Validate padding
	if padding_length > 16 or padding_length > data.size():
		push_error("PacketEncryption: Invalid padding length: %d" % padding_length)
		return data
	
	# Verify all padding bytes are correct
	for i in range(padding_length):
		var idx = data.size() - 1 - i
		if data[idx] != padding_length:
			push_error("PacketEncryption: Invalid padding at position %d" % idx)
			return data
	
	# Remove padding
	return data.slice(0, data.size() - padding_length)
