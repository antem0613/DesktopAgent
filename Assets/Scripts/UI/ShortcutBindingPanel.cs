using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// ショートカット割り当て用のUI
/// </summary>
public sealed class ShortcutBindingPanel : MonoBehaviour
{
    [Serializable]
    public class ActionOption
    {
        public string Label;
        public string ActionMap;
        public string ActionName;
        public Key DefaultKey = Key.None;
        public Key DefaultModifier1 = Key.None;
        public Key DefaultModifier2 = Key.None;
    }

    [Header("UI")]
    [SerializeField] private Transform listContainer;
    [SerializeField] private ShortcutBindingRow rowPrefab;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Options")]
    [SerializeField] private List<ActionOption> actionOptions = new List<ActionOption>();

    private readonly List<ShortcutBindingRow> _rows = new List<ShortcutBindingRow>();

    private void Awake()
    {
        if (actionOptions == null || actionOptions.Count == 0)
        {
            actionOptions = new List<ActionOption>
            {
                new ActionOption
                {
                    Label = "Menu Toggle",
                    ActionMap = "Debug",
                    ActionName = "SwitchShowMenu",
                    DefaultKey = Key.F1
                },
                new ActionOption
                {
                    Label = "Help",
                    ActionMap = "Debug",
                    ActionName = "HelpButton",
                    DefaultKey = Key.F2
                },
                new ActionOption
                {
                    Label = "Quit",
                    ActionMap = "Debug",
                    ActionName = "Quit",
                    DefaultKey = Key.F3
                },
                new ActionOption
                {
                    Label = "Push To Talk",
                    ActionMap = "Shortcut",
                    ActionName = "PushToTalk",
                    DefaultKey = Key.Space
                },
                new ActionOption
                {
                    Label = "Chat Submit",
                    ActionMap = "UI",
                    ActionName = "Submit",
                    DefaultKey = Key.Enter
                }
            };
        }

        ShortcutBindingService.RegisterDefaultBindings(BuildDefaultDefinitions(actionOptions));
    }

    private static IEnumerable<ShortcutBindingService.DefaultShortcutDefinition> BuildDefaultDefinitions(IEnumerable<ActionOption> options)
    {
        if (options == null)
        {
            yield break;
        }

        foreach (var option in options)
        {
            if (option == null)
            {
                continue;
            }

            var modifiers = new List<Key>();
            if (option.DefaultModifier1 != Key.None)
            {
                modifiers.Add(option.DefaultModifier1);
            }

            if (option.DefaultModifier2 != Key.None)
            {
                modifiers.Add(option.DefaultModifier2);
            }

            yield return new ShortcutBindingService.DefaultShortcutDefinition(
                option.ActionMap,
                option.ActionName,
                option.DefaultKey,
                modifiers.ToArray());
        }
    }

    private void OnEnable()
    {
        BuildList();
    }

    private void OnDisable()
    {
        ClearList();
    }

    private void BuildList()
    {
        if (listContainer == null || rowPrefab == null)
        {
            return;
        }

        ClearList();

        foreach (var option in actionOptions)
        {
            var entry = ShortcutBindingService.GetShortcuts()
                .FirstOrDefault(item =>
                    string.Equals(item.ActionMap, option.ActionMap, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.ActionName, option.ActionName, StringComparison.OrdinalIgnoreCase));

            var row = Instantiate(rowPrefab, listContainer);
            row.Initialize(option, entry, HandleApply, HandleReset, SetStatus);
            _rows.Add(row);
        }
    }

    private void ClearList()
    {
        foreach (var row in _rows)
        {
            if (row != null)
            {
                Destroy(row.gameObject);
            }
        }

        _rows.Clear();
    }

    private void HandleApply(ShortcutBindingRow row)
    {
        if (row == null)
        {
            return;
        }

        if (!TryParseKey(row.PrimaryKeyText, out var primaryKey))
        {
            SetStatus("Invalid primary key");
            return;
        }

        if (!TryCollectModifiers(primaryKey, row.ModifierKeyTexts, out var modifiers, out var warning))
        {
            SetStatus(warning);
            return;
        }

        var option = row.Option;
        ShortcutBindingService.SetShortcut(option.ActionMap, option.ActionName, primaryKey, modifiers.ToArray());
        RefreshPushToTalkBindingIfNeeded(option);
    }

    private void HandleReset(ShortcutBindingRow row)
    {
        if (row == null)
        {
            return;
        }

        var option = row.Option;
        var modifiers = new List<Key>();
        if (option.DefaultModifier1 != Key.None) modifiers.Add(option.DefaultModifier1);
        if (option.DefaultModifier2 != Key.None) modifiers.Add(option.DefaultModifier2);

        ShortcutBindingService.SetShortcut(option.ActionMap, option.ActionName, option.DefaultKey, modifiers.ToArray());
        row.SetKeys(option.DefaultKey, modifiers.ToArray());
        RefreshPushToTalkBindingIfNeeded(option);
    }

    private static void RefreshPushToTalkBindingIfNeeded(ActionOption option)
    {
        if (option == null)
        {
            return;
        }

        if (!string.Equals(option.ActionMap, "Shortcut", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(option.ActionName, "PushToTalk", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var sttManager = FindFirstObjectByType<STTManager>();
        if (sttManager != null)
        {
            sttManager.RefreshPushToTalkBinding();
        }
    }

    private static bool TryParseKey(string text, out Key key)
    {
        key = Key.None;
        var trimmed = text == null ? string.Empty : text.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        return Enum.TryParse(trimmed, true, out key);
    }

    private bool TryCollectModifiers(Key primaryKey, IEnumerable<string> modifierTexts, out List<Key> modifiers, out string warning)
    {
        modifiers = new List<Key>();
        warning = string.Empty;

        var unique = new HashSet<Key>();
        foreach (var rawValue in modifierTexts)
        {
            var raw = rawValue == null ? string.Empty : rawValue.Trim();
            if (string.IsNullOrEmpty(raw))
            {
                continue;
            }

            if (!TryParseKey(raw, out var key))
            {
                warning = $"Invalid modifier key: {raw}";
                return false;
            }

            if (key == Key.None || key == primaryKey)
            {
                warning = "Modifier key duplicates primary key";
                return false;
            }

            if (!unique.Add(key))
            {
                warning = "Duplicate modifier key";
                return false;
            }
        }

        modifiers = unique.ToList();
        return true;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }
}
