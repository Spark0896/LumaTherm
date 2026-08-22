using System.Text.Json;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Settings;

namespace LumaTherm.Infrastructure.Settings;

internal static class SettingsMigrator
{
    public static AppSettings Migrate(string json, JsonSerializerOptions serializerOptions)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !TryGetProperty(root, "schemaVersion", out var schemaElement) || !schemaElement.TryGetInt32(out var schemaVersion))
        {
            throw new InvalidDataException("Settings schema version is missing or invalid.");
        }

        return schemaVersion switch
        {
            0 => MigrateVersionZero(root),
            1 => MigrateVersionOne(root),
            2 => ValidateCurrent(JsonSerializer.Deserialize<AppSettings>(json, serializerOptions)),
            _ => throw new InvalidDataException($"Unsupported settings schema {schemaVersion}.")
        };
    }

    private static AppSettings MigrateVersionZero(JsonElement root) => MigrateLegacySettings(root, root);

    private static AppSettings MigrateVersionOne(JsonElement root)
    {
        if (!TryGetProperty(root, "profile", out var profile) || profile.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Missing or invalid profile.");
        }

        return MigrateLegacySettings(root, profile);
    }

    private static AppSettings MigrateLegacySettings(JsonElement root, JsonElement profileRoot)
    {
        var defaults = AppSettings.Default;
        var profile = ThermalProfile.Create(
        [
            new ThermalPoint(GetRequiredDouble(profileRoot, "coldTemperature"), GetRequiredColor(profileRoot, "coldColor")),
            new ThermalPoint(GetRequiredDouble(profileRoot, "warmTemperature"), GetRequiredColor(profileRoot, "warmColor")),
            new ThermalPoint(GetRequiredDouble(profileRoot, "hotTemperature"), GetRequiredColor(profileRoot, "hotColor"))
        ],
        GetRequiredDouble(profileRoot, "smoothingSeconds"));
        var settings = new AppSettings(
            2,
            profile,
            GetBooleanOrDefault(root, "isModeEnabled", defaults.IsModeEnabled),
            GetBooleanOrDefault(root, "isAutostartEnabled", defaults.IsAutostartEnabled),
            GetBooleanOrDefault(root, "minimizeToTray", defaults.MinimizeToTray),
            GetBooleanOrDefault(root, "notificationsEnabled", defaults.NotificationsEnabled),
            GetStringOrDefault(root, "preferredLightingDeviceId", defaults.PreferredLightingDeviceId),
            defaults.Language,
            defaults.TrayMenu);

        return ValidateCurrent(settings);
    }

    private static AppSettings ValidateCurrent(AppSettings? settings)
    {
        if (settings is null || settings.Profile is null || settings.TrayMenu is null)
        {
            throw new InvalidDataException("Settings are incomplete.");
        }

        try
        {
            return settings.Validate();
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Settings contain invalid values.", exception);
        }
    }

    private static double GetRequiredDouble(JsonElement root, string name)
    {
        if (!TryGetProperty(root, name, out var element) || !element.TryGetDouble(out var value))
        {
            throw new InvalidDataException($"Missing or invalid {name}.");
        }

        return value;
    }

    private static RgbColor GetRequiredColor(JsonElement root, string name)
    {
        if (!TryGetProperty(root, name, out var element) || element.ValueKind != JsonValueKind.String || !RgbColor.TryParseHex(element.GetString(), out var color))
        {
            throw new InvalidDataException($"Missing or invalid {name}.");
        }

        return color;
    }

    private static bool GetBooleanOrDefault(JsonElement root, string name, bool defaultValue)
    {
        if (!TryGetProperty(root, name, out var element))
        {
            return defaultValue;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException($"Invalid {name}.")
        };
    }

    private static string? GetStringOrDefault(JsonElement root, string name, string? defaultValue)
    {
        if (!TryGetProperty(root, name, out var element))
        {
            return defaultValue;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            _ => throw new InvalidDataException($"Invalid {name}.")
        };
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
