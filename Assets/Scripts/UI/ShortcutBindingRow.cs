using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// ショートカット割り当ての行UI
/// </summary>
public sealed class ShortcutBindingRow : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private TMP_InputField primaryKeyInput;
    [SerializeField] private TMP_InputField modifier1Input;
    [SerializeField] private TMP_InputField modifier2Input;
    [SerializeField] private Button applyButton;
    [SerializeField] private Button resetButton;

    private ShortcutBindingPanel.ActionOption _option;
    private Action<ShortcutBindingRow> _onApply;
    private Action<ShortcutBindingRow> _onReset;
    private Action<string> _setStatus;

    private TMP_InputField _captureField;
    private bool _isCapturing;

    public ShortcutBindingPanel.ActionOption Option => _option;

    public string PrimaryKeyText => primaryKeyInput != null ? primaryKeyInput.text : string.Empty;

    public IEnumerable<string> ModifierKeyTexts
    {
        get
        {
            yield return modifier1Input != null ? modifier1Input.text : string.Empty;
            yield return modifier2Input != null ? modifier2Input.text : string.Empty;
        }
    }

    public void Initialize(
        ShortcutBindingPanel.ActionOption option,
        ShortcutBindingEntry entry,
        Action<ShortcutBindingRow> onApply,
        Action<ShortcutBindingRow> onReset,
        Action<string> setStatus)
    {
        _option = option;
        _onApply = onApply;
        _onReset = onReset;
        _setStatus = setStatus;

        if (labelText != null)
        {
            labelText.text = option.Label;
        }

        SetReadOnly(primaryKeyInput);
        SetReadOnly(modifier1Input);
        SetReadOnly(modifier2Input);

        if (entry != null)
        {
            ApplyEntry(entry);
        } else
        {
            SetKeys(option.DefaultKey, GetDefaultModifiers(option));
        }

        RegisterCapture(primaryKeyInput);
        RegisterCapture(modifier1Input);
        RegisterCapture(modifier2Input);

        if (applyButton != null)
        {
            applyButton.onClick.AddListener(HandleApply);
        }

        if (resetButton != null)
        {
            resetButton.onClick.AddListener(HandleReset);
        }
    }

    public void SetKeys(Key primary, IReadOnlyList<Key> modifiers)
    {
        if (primaryKeyInput != null)
        {
            primaryKeyInput.text = primary != Key.None ? primary.ToString() : string.Empty;
        }

        SetModifier(modifier1Input, modifiers, 0);
        SetModifier(modifier2Input, modifiers, 1);
    }

    private void Update()
    {
        if (!_isCapturing || _captureField == null)
        {
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        foreach (var key in keyboard.allKeys)
        {
            if (!key.wasPressedThisFrame)
            {
                continue;
            }

            if (key.keyCode == Key.Escape)
            {
                EndCapture(false);
                return;
            }

            _captureField.text = key.keyCode.ToString();
            EndCapture(true);
            return;
        }
    }

    private void OnDisable()
    {
        UnregisterCapture(primaryKeyInput);
        UnregisterCapture(modifier1Input);
        UnregisterCapture(modifier2Input);

        if (applyButton != null)
        {
            applyButton.onClick.RemoveListener(HandleApply);
        }

        if (resetButton != null)
        {
            resetButton.onClick.RemoveListener(HandleReset);
        }
    }

    private void ApplyEntry(ShortcutBindingEntry entry)
    {
        if (primaryKeyInput != null)
        {
            primaryKeyInput.text = entry.PrimaryKey ?? string.Empty;
        }

        SetModifier(modifier1Input, entry.Modifiers, 0);
        SetModifier(modifier2Input, entry.Modifiers, 1);
    }

    private void HandleApply()
    {
        _onApply?.Invoke(this);
    }

    private void HandleReset()
    {
        _onReset?.Invoke(this);
    }

    private static IReadOnlyList<Key> GetDefaultModifiers(ShortcutBindingPanel.ActionOption option)
    {
        var list = new List<Key>();
        if (option.DefaultModifier1 != Key.None) list.Add(option.DefaultModifier1);
        if (option.DefaultModifier2 != Key.None) list.Add(option.DefaultModifier2);
        return list;
    }

    private static void SetReadOnly(TMP_InputField field)
    {
        if (field != null)
        {
            field.readOnly = true;
        }
    }

    private void RegisterCapture(TMP_InputField field)
    {
        if (field == null)
        {
            return;
        }

        field.onSelect.AddListener(_ => BeginCapture(field));
        field.onDeselect.AddListener(_ => EndCapture(false));
    }

    private void UnregisterCapture(TMP_InputField field)
    {
        if (field == null)
        {
            return;
        }

        field.onSelect.RemoveAllListeners();
        field.onDeselect.RemoveAllListeners();
    }

    private void BeginCapture(TMP_InputField field)
    {
        _captureField = field;
        _isCapturing = true;
        _setStatus?.Invoke("Press any key...");
    }

    private void EndCapture(bool applied)
    {
        _isCapturing = false;
        _captureField = null;
        if (!applied)
        {
            _setStatus?.Invoke("Canceled");
        }
    }

    private static void SetModifier(TMP_InputField field, IReadOnlyList<Key> modifiers, int index)
    {
        if (field == null)
        {
            return;
        }

        if (modifiers != null && modifiers.Count > index)
        {
            field.text = modifiers[index] != Key.None ? modifiers[index].ToString() : string.Empty;
        } else
        {
            field.text = string.Empty;
        }
    }

    private static void SetModifier(TMP_InputField field, string[] modifiers, int index)
    {
        if (field == null)
        {
            return;
        }

        if (modifiers != null && modifiers.Length > index)
        {
            field.text = modifiers[index] ?? string.Empty;
        } else
        {
            field.text = string.Empty;
        }
    }
}
