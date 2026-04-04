using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework.Input;
using MTEngine.Core;

namespace SandboxGame.Settings;

public class GameSettings : IKeyBindingSource
{
    public bool DevMode { get; set; }
    public Dictionary<string, string> KeyBindings { get; set; } = new();

    [JsonIgnore]
    private static readonly string SettingsPath = Path.Combine(
        AppContext.BaseDirectory, "settings.json");

    [JsonIgnore]
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static readonly Dictionary<string, Keys> DefaultKeys = new()
    {
        ["MoveUp"] = Keys.W,
        ["MoveDown"] = Keys.S,
        ["MoveLeft"] = Keys.A,
        ["MoveRight"] = Keys.D,
        ["Use"] = Keys.E,
        ["Drop"] = Keys.Q,
        ["SwapHand"] = Keys.Tab,
        ["Metabolism"] = Keys.M,
        ["Pause"] = Keys.Escape,
        ["DevMode"] = Keys.F3,
        ["Console"] = Keys.OemTilde,
        ["InspectContainer"] = Keys.P,
        ["InspectMemory"] = Keys.O,
        ["Fullscreen"] = Keys.F11,
    };

    public static readonly Dictionary<string, string> ActionLabels = new()
    {
        ["MoveUp"] = "Вверх",
        ["MoveDown"] = "Вниз",
        ["MoveLeft"] = "Влево",
        ["MoveRight"] = "Вправо",
        ["Use"] = "Использовать",
        ["Drop"] = "Бросить",
        ["SwapHand"] = "Сменить руку",
        ["Metabolism"] = "Статус",
        ["Pause"] = "Пауза",
        ["DevMode"] = "Режим разработчика",
        ["Console"] = "Консоль разработчика",
        ["InspectContainer"] = "Показать состав тары",
        ["InspectMemory"] = "Показать справочник памяти",
        ["Fullscreen"] = "Полный экран",
    };

    public Keys GetKey(string action)
    {
        if (KeyBindings.TryGetValue(action, out var keyName) && Enum.TryParse<Keys>(keyName, out var key))
            return key;
        return DefaultKeys.GetValueOrDefault(action, Keys.None);
    }

    public void SetKey(string action, Keys key)
    {
        KeyBindings[action] = key.ToString();
    }

    public void ResetToDefaults()
    {
        DevMode = true;
        KeyBindings.Clear();
        foreach (var (action, key) in DefaultKeys)
            KeyBindings[action] = key.ToString();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    public static GameSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<GameSettings>(json, JsonOptions);
                if (settings != null)
                {
                    // Fill in any missing keys with defaults
                    foreach (var (action, key) in DefaultKeys)
                    {
                        if (!settings.KeyBindings.ContainsKey(action))
                            settings.KeyBindings[action] = key.ToString();
                    }
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
        }

        var defaults = new GameSettings();
        defaults.ResetToDefaults();
        return defaults;
    }
}
