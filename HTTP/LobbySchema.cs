using System.Text.Json;

namespace NodeTunnel.HTTP;

public static class LobbySchema {
    // Allowed optional fields, add or edit the custom fields here to whitelist them
    private static readonly HashSet<string> AllowedOptionalFields = new() {
        "Version",          // Game version
        "Region",           // Geographic region
        "Difficulty",       // Game difficulty
        "Ranked",           // Whether it's a ranked match
        "CustomRules",      // Custom game rules description
        "Tags",             // Comma-separated tags
        "MinLevel",         // Minimum player level
        "MaxLevel",         // Maximum player level
        "ServerMods",       // Active server modifications
        "VoiceChat"         // Voice chat enabled/disabled
    };

    // Required fields that must be present in every lobby registration (Don't modify)
    private static readonly HashSet<string> RequiredFields = new() {
        "LobbyId",
        "Name",
        "Mode",
        "Map",
        "CurrentPlayers",
        "MaxPlayers",
        "IsPassworded"
    };

    private const int MaxStringLength = 200;
    private const int MaxLobbyIdLength = 64;

    public static ValidationResult Validate(Dictionary<string, object> metadata) {
        foreach (var field in RequiredFields) {
            if (!metadata.ContainsKey(field)) {
                return ValidationResult.Failure($"Missing required field: {field}");
            }
        }

        if (!ValidateField(metadata, "LobbyId", typeof(string), MaxLobbyIdLength)) {
            return ValidationResult.Failure("Invalid LobbyId");
        }

        if (!ValidateField(metadata, "Name", typeof(string), MaxStringLength)) {
            return ValidationResult.Failure("Invalid Name");
        }

        if (!ValidateField(metadata, "Mode", typeof(string), MaxStringLength)) {
            return ValidationResult.Failure("Invalid Mode");
        }

        if (!ValidateField(metadata, "Map", typeof(string), MaxStringLength)) {
            return ValidationResult.Failure("Invalid Map");
        }

        if (!ValidateIntegerField(metadata, "CurrentPlayers", 0, 1000)) {
            return ValidationResult.Failure("Invalid CurrentPlayers");
        }

        if (!ValidateIntegerField(metadata, "MaxPlayers", 1, 1000)) {
            return ValidationResult.Failure("Invalid MaxPlayers");
        }

        if (!ValidateField(metadata, "IsPassworded", typeof(bool))) {
            return ValidationResult.Failure("Invalid IsPassworded");
        }

        foreach (var key in metadata.Keys) {
            if (!RequiredFields.Contains(key) && !AllowedOptionalFields.Contains(key)) {
                return ValidationResult.Failure($"Disallowed field: {key}");
            }
        }

        foreach (var field in AllowedOptionalFields) {
            if (metadata.ContainsKey(field)) {
                if (metadata[field] is string strValue && strValue.Length > MaxStringLength) {
                    return ValidationResult.Failure($"Field {field} exceeds maximum length");
                }
            }
        }

        return ValidationResult.Success();
    }

    private static bool ValidateField(Dictionary<string, object> metadata, string key, Type expectedType, int? maxLength = null) {
        if (!metadata.TryGetValue(key, out var value)) {
            return false;
        }

        if (value is JsonElement element) {
            value = ConvertJsonElement(element, expectedType);
            if (value == null) return false;
        }

        if (value.GetType() != expectedType) {
            return false;
        }

        if (maxLength.HasValue && value is string strValue && strValue.Length > maxLength.Value) {
            return false;
        }

        return true;
    }

    private static bool ValidateIntegerField(Dictionary<string, object> metadata, string key, int min, int max) {
        if (!metadata.TryGetValue(key, out var value)) {
            return false;
        }

        if (value is JsonElement element) {
            if (element.ValueKind != JsonValueKind.Number) return false;
            if (!element.TryGetInt32(out int intValue)) return false;
            value = intValue;
        }

        if (value is not int intVal) {
            return false;
        }

        return intVal >= min && intVal <= max;
    }

    private static object? ConvertJsonElement(JsonElement element, Type targetType) {
        try {
            if (targetType == typeof(string)) {
                return element.GetString();
            }
            else if (targetType == typeof(int)) {
                return element.GetInt32();
            }
            else if (targetType == typeof(bool)) {
                return element.GetBoolean();
            }
            return null;
        }
        catch {
            return null;
        }
    }

    public class ValidationResult {
        public bool IsValid { get; private set; }
        public string? ErrorMessage { get; private set; }

        private ValidationResult(bool isValid, string? errorMessage = null) {
            IsValid = isValid;
            ErrorMessage = errorMessage;
        }

        public static ValidationResult Success() => new(true);
        public static ValidationResult Failure(string message) => new(false, message);
    }
}
