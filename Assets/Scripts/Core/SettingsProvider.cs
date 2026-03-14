using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Unity.Logging;
using UnityEngine;

public class SettingsProvider : Singleton<SettingsProvider>
{
    private const int CurrentSchemaVersion = 1;
    private const string SettingsDirectoryName = "Settings";
    private const string SettingsFileName = "application_settings.json";

    public CharacterSettings Character { get; private set; }
    public SoundSettings Sound { get; private set; }
    public DisplaySettings Display { get; private set; }
    public PerformanceSettings Performance { get; private set; }
    public BackendSettings Backend { get; private set; }
    public ShortcutSettings Shortcuts { get; private set; }

    public bool IsLoaded { get; private set; }
    public string FilePath { get; private set; }

    public SettingsProvider()
    {
        InitializeDefaults();
        FilePath = GetDefaultJsonPath();
        Load();
    }

    public bool Load()
    {
        InitializeDefaults();

        if (TryLoadFromJson(FilePath, out var document))
        {
            ApplyDocument(document);
            IsLoaded = true;
            ValidateSettings();
            return true;
        }

        IsLoaded = false;
        ValidateSettings();
        Save();
        return false;
    }

    public void Save()
    {
        try
        {
            string directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var document = BuildDocument();
            string json = JsonUtility.ToJson(document, true);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Log.Error($"[SettingsProvider] Failed to save settings: {ex.Message}");
        }
    }

    public void ResetToDefaults(bool save = true)
    {
        InitializeDefaults();
        ValidateSettings();
        if (save)
        {
            Save();
        }
    }

    private void InitializeDefaults()
    {
        Character = new CharacterSettings();
        Sound = new SoundSettings();
        Display = new DisplaySettings();
        Performance = new PerformanceSettings();
        Backend = new BackendSettings();
        Shortcuts = new ShortcutSettings();
    }

    private static string GetDefaultJsonPath()
    {
        return Path.Combine(Application.persistentDataPath, SettingsDirectoryName, SettingsFileName);
    }

    private bool TryLoadFromJson(string filePath, out SettingsDocument document)
    {
        document = null;

        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return false;
            }

            string json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            document = JsonUtility.FromJson<SettingsDocument>(json);
            if (document != null
                && (document.shortcuts == null || document.shortcuts.Count == 0)
                && TryReadLegacyShortcuts(json, out var migrated))
            {
                document.shortcuts = migrated;
            }

            return document != null;
        }
        catch (Exception ex)
        {
            Log.Warning($"[SettingsProvider] Failed to load JSON settings: {ex.Message}");
            return false;
        }
    }

    private static bool TryReadLegacyShortcuts(string json, out List<ShortcutBindingEntry> bindings)
    {
        bindings = null;

        try
        {
            var legacyContainer = JsonUtility.FromJson<LegacyShortcutsContainer>(json);
            string legacyJson = legacyContainer?.shortcuts?.shortcutBindingsJson;
            if (string.IsNullOrWhiteSpace(legacyJson))
            {
                return false;
            }

            var legacyData = JsonUtility.FromJson<ShortcutBindingsData>(legacyJson);
            if (legacyData?.Bindings == null || legacyData.Bindings.Count == 0)
            {
                return false;
            }

            bindings = CloneShortcutBindings(legacyData.Bindings);
            return bindings.Count > 0;
        } catch (Exception ex)
        {
            Log.Warning($"[SettingsProvider] Failed to migrate legacy shortcuts: {ex.Message}");
            return false;
        }
    }

    private static List<ShortcutBindingEntry> CloneShortcutBindings(IReadOnlyList<ShortcutBindingEntry> source)
    {
        var list = new List<ShortcutBindingEntry>();
        if (source == null)
        {
            return list;
        }

        for (int i = 0; i < source.Count; i++)
        {
            var entry = source[i];
            if (entry == null)
            {
                continue;
            }

            list.Add(new ShortcutBindingEntry
            {
                ActionMap = entry.ActionMap,
                ActionName = entry.ActionName,
                PrimaryKey = entry.PrimaryKey,
                Modifiers = entry.Modifiers != null ? (string[])entry.Modifiers.Clone() : Array.Empty<string>()
            });
        }

        return list;
    }

    private bool TryLoadFromLegacyIni(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return false;
            }

            var settingsData = IniFileParser.Parse(filePath);
            if (settingsData == null || settingsData.Count == 0)
            {
                return false;
            }

            foreach (var section in settingsData)
            {
                switch (section.Key)
                {
                    case "Character":
                        AssignSettings(Character, section.Value);
                        break;
                    case "Sound":
                        AssignSettings(Sound, section.Value);
                        break;
                    case "Display":
                        AssignSettings(Display, section.Value);
                        break;
                    case "Performance":
                        AssignSettings(Performance, section.Value);
                        break;
                    case "Backend":
                        AssignSettings(Backend, section.Value);
                        break;
                    case "Shortcuts":
                        AssignSettings(Shortcuts, section.Value);
                        break;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"[SettingsProvider] Failed to load legacy settings: {ex.Message}");
            return false;
        }
    }

    private void ValidateSettings()
    {
        int qualityLevel = Performance.QualityLevel;
        if (qualityLevel < 0 || qualityLevel >= QualitySettings.names.Length)
        {
            Performance.QualityLevel = QualityLevelUtility.AdjustQualityLevel();
        }

        if (Performance.TargetFrameRate <= 0)
        {
            Performance.TargetFrameRate = 60;
        }
    }

    private SettingsDocument BuildDocument()
    {
        return new SettingsDocument
        {
            schemaVersion = CurrentSchemaVersion,
            character = CharacterSection.From(Character),
            sound = SoundSection.From(Sound),
            display = DisplaySection.From(Display),
            performance = PerformanceSection.From(Performance),
            backend = BackendSection.From(Backend),
            shortcuts = CloneShortcutBindings(Shortcuts?.Shortcuts)
        };
    }

    private void ApplyDocument(SettingsDocument document)
    {
        if (document == null)
        {
            return;
        }

        if (document.character != null)
        {
            document.character.ApplyTo(Character);
        }

        if (document.sound != null)
        {
            document.sound.ApplyTo(Sound);
        }

        if (document.display != null)
        {
            document.display.ApplyTo(Display);
        }

        if (document.performance != null)
        {
            document.performance.ApplyTo(Performance);
        }

        if (document.backend != null)
        {
            document.backend.ApplyTo(Backend);
        }

        Shortcuts.Shortcuts = CloneShortcutBindings(document.shortcuts);
    }

    private void AssignSettings<T>(T settingsInstance, Dictionary<string, string> values)
    {
        if (settingsInstance == null || values == null)
        {
            return;
        }

        var type = typeof(T);
        foreach (var kvp in values)
        {
            var property = type.GetProperty(kvp.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property == null || !property.CanWrite)
            {
                continue;
            }

            try
            {
                object value = ConvertValue(property.PropertyType, kvp.Value, property.GetValue(settingsInstance));
                property.SetValue(settingsInstance, value);
            }
            catch (Exception ex)
            {
                Log.Warning($"[SettingsProvider] Failed assigning '{kvp.Key}' on '{type.Name}': {ex.Message}");
            }
        }
    }

    private static object ConvertValue(Type targetType, string value, object defaultValue)
    {
        try
        {
            if (targetType == typeof(string))
            {
                return value;
            }

            if (targetType == typeof(int))
            {
                return int.TryParse(value, out var intValue) ? intValue : defaultValue;
            }

            if (targetType == typeof(float))
            {
                return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue)
                    ? floatValue
                    : defaultValue;
            }

            if (targetType == typeof(bool))
            {
                if (bool.TryParse(value, out var boolValue))
                {
                    return boolValue;
                }

                if (value == "1")
                {
                    return true;
                }

                if (value == "0")
                {
                    return false;
                }

                return defaultValue;
            }

            if (targetType.IsEnum)
            {
                return Enum.TryParse(targetType, value, true, out var enumValue) ? enumValue : defaultValue;
            }

            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    [Serializable]
    private class SettingsDocument
    {
        public int schemaVersion = CurrentSchemaVersion;
        public CharacterSection character = new CharacterSection();
        public SoundSection sound = new SoundSection();
        public DisplaySection display = new DisplaySection();
        public PerformanceSection performance = new PerformanceSection();
        public BackendSection backend = new BackendSection();
        public List<ShortcutBindingEntry> shortcuts = new List<ShortcutBindingEntry>();
    }

    [Serializable]
    private class CharacterSection
    {
        public string modelPath = "default.vrm";
        public float scale = 3f;
        public float positionX;
        public float positionY;
        public float positionZ;
        public float rotationX;
        public float rotationY;
        public float rotationZ;
        public bool useLilToon;

        public static CharacterSection From(CharacterSettings source)
        {
            return new CharacterSection
            {
                modelPath = source.ModelPath,
                scale = source.Scale,
                positionX = source.PositionX,
                positionY = source.PositionY,
                positionZ = source.PositionZ,
                rotationX = source.RotationX,
                rotationY = source.RotationY,
                rotationZ = source.RotationZ,
                useLilToon = source.UseLilToon
            };
        }

        public void ApplyTo(CharacterSettings target)
        {
            target.ModelPath = modelPath;
            target.Scale = scale;
            target.PositionX = positionX;
            target.PositionY = positionY;
            target.PositionZ = positionZ;
            target.RotationX = rotationX;
            target.RotationY = rotationY;
            target.RotationZ = rotationZ;
            target.UseLilToon = useLilToon;
        }
    }

    [Serializable]
    private class SoundSection
    {
        public float voiceVolume = 1f;
        public float seVolume = 1f;

        public static SoundSection From(SoundSettings source)
        {
            return new SoundSection
            {
                voiceVolume = source.VoiceVolume,
                seVolume = source.SEVolume
            };
        }

        public void ApplyTo(SoundSettings target)
        {
            target.VoiceVolume = voiceVolume;
            target.SEVolume = seVolume;
        }
    }

    [Serializable]
    private class DisplaySection
    {
        public string language = string.Empty;

        public static DisplaySection From(DisplaySettings source)
        {
            return new DisplaySection
            {
                language = source.Language
            };
        }

        public void ApplyTo(DisplaySettings target)
        {
            target.Language = language;
        }
    }

    [Serializable]
    private class PerformanceSection
    {
        public int targetFrameRate = 60;
        public int qualityLevel = 2;

        public static PerformanceSection From(PerformanceSettings source)
        {
            return new PerformanceSection
            {
                targetFrameRate = source.TargetFrameRate,
                qualityLevel = source.QualityLevel
            };
        }

        public void ApplyTo(PerformanceSettings target)
        {
            target.TargetFrameRate = targetFrameRate;
            target.QualityLevel = qualityLevel;
        }
    }

    [Serializable]
    private class BackendSection
    {
        [Serializable]
        public class LangGraphSection
        {
            public string host = string.Empty;
            public int port = Constant.BackendPort;
        }

        [Serializable]
        public class CoeiroinkSection
        {
            public string host = string.Empty;
            public int port = Constant.CoeiroinkHealthPort;
            public string speakerUuid = string.Empty;
            public int styleId;
        }

        [Serializable]
        public class OllamaSection
        {
            public string host = string.Empty;
            public int port = Constant.OllamaHealthPort;
        }

        public LangGraphSection LangGraph = new LangGraphSection();
        public CoeiroinkSection Coeiroink = new CoeiroinkSection();
        public OllamaSection Ollama = new OllamaSection();
        public float startupTimeoutSeconds = 4f;
        public float pollIntervalSeconds = 0.25f;
        public int tcpTimeoutMs = 200;

        public static BackendSection From(BackendSettings source)
        {
            return new BackendSection
            {
                LangGraph = new LangGraphSection
                {
                    host = source.LangGraphHost,
                    port = source.LangGraphPort
                },
                Coeiroink = new CoeiroinkSection
                {
                    host = source.CoeiroinkHost,
                    port = source.CoeiroinkPort,
                    speakerUuid = source.CoeiroinkSpeakerUuid,
                    styleId = source.CoeiroinkStyleId
                },
                Ollama = new OllamaSection
                {
                    host = source.OllamaHost,
                    port = source.OllamaPort
                },
                startupTimeoutSeconds = source.StartupTimeoutSeconds,
                pollIntervalSeconds = source.PollIntervalSeconds,
                tcpTimeoutMs = source.TcpTimeoutMs
            };
        }

        public void ApplyTo(BackendSettings target)
        {
            if (LangGraph != null)
            {
                target.LangGraphHost = LangGraph.host;
                target.LangGraphPort = LangGraph.port;
            }

            if (Coeiroink != null)
            {
                target.CoeiroinkHost = Coeiroink.host;
                target.CoeiroinkPort = Coeiroink.port;
                target.CoeiroinkSpeakerUuid = Coeiroink.speakerUuid;
                target.CoeiroinkStyleId = Coeiroink.styleId;
            }

            if (Ollama != null)
            {
                target.OllamaHost = Ollama.host;
                target.OllamaPort = Ollama.port;
            }

            target.StartupTimeoutSeconds = startupTimeoutSeconds;
            target.PollIntervalSeconds = pollIntervalSeconds;
            target.TcpTimeoutMs = tcpTimeoutMs;
        }
    }

    [Serializable]
    private class LegacyShortcutsContainer
    {
        public LegacyShortcutsSection shortcuts;
    }

    [Serializable]
    private class LegacyShortcutsSection
    {
        public string shortcutBindingsJson;
    }
}