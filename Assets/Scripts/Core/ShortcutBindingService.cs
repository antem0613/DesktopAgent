using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Logging;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ショートカットの保存/適用を管理するサービス
/// </summary>
public static class ShortcutBindingService
{
    private const string UserShortcutGroup = "UserShortcut";
    private const int MaxSupportedModifiers = 4;

    private static bool _loaded;
    private static InputActionAsset _asset;
    private static ShortcutBindingsData _data = new ShortcutBindingsData();
    private static bool _compositeRegistered;

    public static void Initialize(InputActionAsset asset)
    {
        _asset = asset;
        RegisterCompositeIfNeeded();
        EnsureLoaded();
        ApplyBindingsToAsset(asset);
    }

    public static void ApplyToAction(InputAction action, string actionMap, string actionName, Key defaultKey, params Key[] defaultModifiers)
    {
        if (action == null)
        {
            return;
        }

        EnsureLoaded();

        var entry = FindEntry(actionMap, actionName);
        if (entry == null)
        {
            entry = ShortcutBindingEntry.FromKeys(actionMap, actionName, defaultKey, defaultModifiers);
        }

        ApplyEntryToAction(action, entry);
    }

    public static void SetShortcut(string actionMap, string actionName, Key primaryKey, params Key[] modifiers)
    {
        EnsureLoaded();

        var entry = ShortcutBindingEntry.FromKeys(actionMap, actionName, primaryKey, modifiers);
        UpsertEntry(entry);
        SaveToSettings();

        if (_asset != null)
        {
            var action = _asset.FindAction($"{actionMap}/{actionName}", false);
            if (action != null)
            {
                ApplyEntryToAction(action, entry);
            }
        }
    }

    public static IReadOnlyList<ShortcutBindingEntry> GetShortcuts()
    {
        EnsureLoaded();
        return _data.Bindings;
    }

    public static void RemoveShortcut(string actionMap, string actionName)
    {
        EnsureLoaded();

        var removed = _data.Bindings.RemoveAll(entry =>
            string.Equals(entry.ActionMap, actionMap, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.ActionName, actionName, StringComparison.OrdinalIgnoreCase));

        if (removed > 0)
        {
            SaveToSettings();
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        LoadFromSettings();
    }

    private static void LoadFromSettings()
    {
        var settings = ApplicationSettings.Instance?.Shortcuts;
        if (settings == null || string.IsNullOrWhiteSpace(settings.ShortcutBindingsJson))
        {
            _data = new ShortcutBindingsData();
            return;
        }

        try
        {
            _data = JsonUtility.FromJson<ShortcutBindingsData>(settings.ShortcutBindingsJson) ?? new ShortcutBindingsData();
        } catch (Exception ex)
        {
            Log.Warning($"ショートカット設定の読み込みに失敗しました: {ex.Message}");
            _data = new ShortcutBindingsData();
        }
    }

    private static void SaveToSettings()
    {
        var settings = ApplicationSettings.Instance?.Shortcuts;
        if (settings == null)
        {
            return;
        }

        settings.ShortcutBindingsJson = JsonUtility.ToJson(_data);
        ApplicationSettings.Instance.SaveSettings();
    }

    private static void ApplyBindingsToAsset(InputActionAsset asset)
    {
        if (asset == null)
        {
            return;
        }

        foreach (var entry in _data.Bindings)
        {
            var action = asset.FindAction($"{entry.ActionMap}/{entry.ActionName}", false);
            if (action == null)
            {
                continue;
            }

            ApplyEntryToAction(action, entry);
        }
    }

    private static void ApplyEntryToAction(InputAction action, ShortcutBindingEntry entry)
    {
        RemoveUserBindings(action);

        var primaryPath = ResolveKeyPath(entry.PrimaryKey);
        if (string.IsNullOrWhiteSpace(primaryPath))
        {
            return;
        }

        var modifiers = entry.Modifiers ?? Array.Empty<string>();
        var modifierPaths = modifiers
            .Select(ResolveKeyPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();

        if (modifierPaths.Length == 0)
        {
            action.AddBinding(primaryPath).WithGroup(UserShortcutGroup);
            return;
        }

        if (modifierPaths.Length > MaxSupportedModifiers)
        {
            Log.Warning($"修飾キーは最大{MaxSupportedModifiers}個までです。超過分は無視されます。");
        }

        int bindingStartIndex = action.bindings.Count;

        var composite = action.AddCompositeBinding("ButtonWithModifiers")
            .With("button", primaryPath);

        if (modifierPaths.Length > 0) composite.With("modifier1", modifierPaths[0]);
        if (modifierPaths.Length > 1) composite.With("modifier2", modifierPaths[1]);
        if (modifierPaths.Length > 2) composite.With("modifier3", modifierPaths[2]);
        if (modifierPaths.Length > 3) composite.With("modifier4", modifierPaths[3]);

        for (int i = bindingStartIndex; i < action.bindings.Count; i++)
        {
            action.ChangeBinding(i).WithGroup(UserShortcutGroup);
        }
    }

    private static void RegisterCompositeIfNeeded()
    {
        if (_compositeRegistered)
        {
            return;
        }

        _compositeRegistered = true;
        InputSystem.RegisterBindingComposite<ButtonWithModifiersComposite>("ButtonWithModifiers");
    }

    private static void RemoveUserBindings(InputAction action)
    {
        var indicesToErase = new List<int>();
        for (int i = action.bindings.Count - 1; i >= 0; i--)
        {
            var groups = action.bindings[i].groups;
            if (string.IsNullOrWhiteSpace(groups))
            {
                continue;
            }

            var hasGroup = groups.Split(';').Any(group =>
                string.Equals(group, UserShortcutGroup, StringComparison.OrdinalIgnoreCase));
            if (hasGroup)
            {
                indicesToErase.Add(i);
            }
        }

        foreach (var index in indicesToErase)
        {
            action.ChangeBinding(index).Erase();
        }
    }

    private static ShortcutBindingEntry FindEntry(string actionMap, string actionName)
    {
        return _data.Bindings.FirstOrDefault(entry =>
            string.Equals(entry.ActionMap, actionMap, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.ActionName, actionName, StringComparison.OrdinalIgnoreCase));
    }

    private static void UpsertEntry(ShortcutBindingEntry entry)
    {
        var existing = FindEntry(entry.ActionMap, entry.ActionName);
        if (existing == null)
        {
            _data.Bindings.Add(entry);
            return;
        }

        existing.PrimaryKey = entry.PrimaryKey;
        existing.Modifiers = entry.Modifiers;
    }

    private static string ResolveKeyPath(string keyOrPath)
    {
        if (string.IsNullOrWhiteSpace(keyOrPath))
        {
            return string.Empty;
        }

        if (keyOrPath.StartsWith("<", StringComparison.Ordinal))
        {
            return keyOrPath;
        }

        if (Enum.TryParse(keyOrPath, true, out Key key))
        {
            return GetKeyboardPath(key);
        }

        Log.Warning($"不明なキー名: {keyOrPath}");
        return string.Empty;
    }

    private static string GetKeyboardPath(Key key)
    {
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            var control = keyboard[key];
            if (control != null)
            {
                return control.path;
            }
        }

        var name = ToInputSystemKeyName(key);
        return $"<Keyboard>/{name}";
    }

    private static string ToInputSystemKeyName(Key key)
    {
        var raw = key.ToString();
        if (raw.Length == 1)
        {
            return raw.ToLowerInvariant();
        }

        if (raw.StartsWith("Numpad", StringComparison.OrdinalIgnoreCase))
        {
            return "numpad" + raw.Substring("Numpad".Length);
        }

        return char.ToLowerInvariant(raw[0]) + raw.Substring(1);
    }
}

[Serializable]
public sealed class ShortcutBindingsData
{
    public List<ShortcutBindingEntry> Bindings = new List<ShortcutBindingEntry>();
}

[Serializable]
public sealed class ShortcutBindingEntry
{
    public string ActionMap;
    public string ActionName;
    public string PrimaryKey;
    public string[] Modifiers;

    public static ShortcutBindingEntry FromKeys(string actionMap, string actionName, Key primaryKey, params Key[] modifiers)
    {
        return new ShortcutBindingEntry
        {
            ActionMap = actionMap,
            ActionName = actionName,
            PrimaryKey = primaryKey.ToString(),
            Modifiers = modifiers?.Select(mod => mod.ToString()).ToArray() ?? Array.Empty<string>()
        };
    }
}
