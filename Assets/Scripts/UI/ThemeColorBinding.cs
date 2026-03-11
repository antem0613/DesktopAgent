using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LeTai.TrueShadow;

public sealed class ThemeColorBinding : MonoBehaviour
{
    public static ThemeColorBinding Instance { get; private set; }

    [Header("Primary Color Picker")]
    [SerializeField] private FlexibleColorPicker primaryColorPicker;
    [Header("Secondary Color Picker")]
    [SerializeField] private FlexibleColorPicker secondaryColorPicker;
    [Header("Text Color Picker")]
    [SerializeField] FlexibleColorPicker textColorPicker;

    private readonly HashSet<Graphic> _primaryTargets = new HashSet<Graphic>();
    readonly HashSet<Graphic> _secondaryTargets = new HashSet<Graphic>();
    private readonly HashSet<TMP_Text> _textTargets = new HashSet<TMP_Text>();
    private readonly HashSet<Graphic> _textGraphicTargets = new HashSet<Graphic>();
    private readonly HashSet<TrueShadow> _shadowTargets = new HashSet<TrueShadow>();

    [Header("Persistence")]
    [SerializeField] private bool saveToPlayerPrefs = true;
    [SerializeField] private string primaryColorPrefKey = "PrimaryColor";
    [SerializeField] private string secondaryColorPrefKey = "SecondaryColor";
    [SerializeField] string textColorPrefKey = "TextColor";
    [SerializeField] private bool loadOnEnable = true;

    [Header("Rainbow")]
    [SerializeField] private float RainbowHueSpeed = 0.5f;

    private bool _primaryRainbowActive;
    private bool _secondaryRainbowActive;
    bool _textRainbowActive;
    private bool _primaryRainbowInitialized;
    private bool _secondaryRainbowInitialized;
    bool _textRainbowInitialized;
    private bool _sharedRainbowInitialized;
    private float _primaryRainbowHue;
    private float _secondaryRainbowHue;
    float _textRainbowHue;
    private float _primaryRainbowS = 1f;
    private float _secondaryRainbowS = 1f;
    float _textRainbowS = 1f;
    private float _primaryRainbowV = 1f;
    private float _secondaryRainbowV = 1f;
    float _textRainbowV = 1f;
    private float _sharedRainbowHue;
    private Color _lastPrimaryColor = Color.white;
    private Color _lastSecondaryColor = Color.white;
    Color _lastTextColor = Color.white;

    private const string RainbowMarkerValue = "RAINBOW";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("ThemeColorBinding: Multiple instances found. Using the latest one.", this);
        }

        Instance = this;
    }

    private void OnEnable()
    {
        RegisterPickers();
        if (loadOnEnable)
        {
            LoadColorsFromPrefs();
        }
        ApplyPrimaryPicker();
        ApplySecondaryPicker();
        ApplyTextPicker();
    }

    private void OnDisable()
    {
        UnregisterPickers();
    }

    private void Update()
    {
        SyncRainbowState();
        UpdateRainbowIfNeeded();
    }

    private void OnApplicationQuit()
    {
        SaveRainbowMarkersIfNeeded();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void RegisterPickers()
    {
        if (primaryColorPicker != null)
        {
            primaryColorPicker.onColorChange.AddListener(HandlePrimaryColorChanged);
        }

        if (secondaryColorPicker != null)
        {
            secondaryColorPicker.onColorChange.AddListener(HandleSecondaryColorChanged);
        }

        if(textColorPicker != null)
        {
            textColorPicker.onColorChange.AddListener(HandleTextColorChanged);
        }
    }

    private void UnregisterPickers()
    {
        if (primaryColorPicker != null)
        {
            primaryColorPicker.onColorChange.RemoveListener(HandlePrimaryColorChanged);
        }

        if (secondaryColorPicker != null)
        {
            secondaryColorPicker.onColorChange.RemoveListener(HandleSecondaryColorChanged);
        }

        if(textColorPicker != null)
        {
            textColorPicker.onColorChange.RemoveListener(HandleTextColorChanged);
        }
    }

    private void HandlePrimaryColorChanged(Color color)
    {
        _lastPrimaryColor = color;
        Debug.Log("primary color changed : " + color);
        UpdatePickerStartingColor(primaryColorPicker, color);
        ApplyPrimaryColor(color);

        if (primaryColorPicker != null)
        {
            _primaryRainbowActive = primaryColorPicker.IsRainbowMode;
        }

        if (_primaryRainbowActive)
        {
            SaveRainbowMarker(primaryColorPrefKey);
            return;
        }

        SaveColor(primaryColorPrefKey, color);
    }

    private void HandleSecondaryColorChanged(Color color)
    {
        _lastSecondaryColor = color;
        Debug.Log("secondary color changed : " + color);
        UpdatePickerStartingColor(secondaryColorPicker, color);
        ApplySecondaryColor(color);

        if (secondaryColorPicker != null)
        {
            _secondaryRainbowActive = secondaryColorPicker.IsRainbowMode;
        }

        if (_secondaryRainbowActive)
        {
            SaveRainbowMarker(secondaryColorPrefKey);
            return;
        }

        SaveColor(secondaryColorPrefKey, color);
    }

    private void HandleTextColorChanged(Color color)
    {
        _lastTextColor = color;
        Debug.Log("text color changed : " + color);
        UpdatePickerStartingColor(textColorPicker, color);
        ApplyTextColor(color);

        if (textColorPicker != null)
        {
            _textRainbowActive = textColorPicker.IsRainbowMode;
        }

        if (_textRainbowActive)
        {
            SaveRainbowMarker(textColorPrefKey);
            return;
        }

        SaveColor(textColorPrefKey, color);
    }

    public void ApplyPrimaryPicker()
    {
        if (primaryColorPicker == null)
        {
            return;
        }

        _lastPrimaryColor = primaryColorPicker.color;
        ApplyPrimaryColor(primaryColorPicker.color);
    }

    public void ApplySecondaryPicker()
    {
        if (secondaryColorPicker == null)
        {
            return;
        }

        _lastSecondaryColor = secondaryColorPicker.color;
        ApplySecondaryColor(secondaryColorPicker.color);
    }

    public void ApplyTextPicker()
    {
        if (textColorPicker == null)
        {
            return;
        }

        _lastTextColor = textColorPicker.color;
        ApplyTextColor(textColorPicker.color);
    }

    private void ApplyPrimaryColor(Color color)
    {
        foreach (var target in _primaryTargets)
        {
            if (target == null)
            {
                continue;
            }

            target.color = MergeColor(target.color, color);
        }
    }

    private void ApplySecondaryColor(Color color)
    {
        foreach (var target in _secondaryTargets)
        {
            if (target == null)
            {
                continue;
            }

            target.color = MergeColor(target.color, color);
        }

        foreach (var shadow in _shadowTargets)
        {
            if (shadow == null)
            {
                continue;
            }

            shadow.Color = MergeColor(shadow.Color, color);
        }
    }

    private void ApplyTextColor(Color color)
    {
        foreach (var target in _textTargets)
        {
            if (target == null)
            {
                continue;
            }

            target.color = MergeColor(target.color, color);
        }

        foreach (var target in _textGraphicTargets)
        {
            if (target == null)
            {
                continue;
            }

            target.color = MergeColor(target.color, color);
        }
    }

    private static Color MergeColor(Color current, Color incoming)
    {
        return new Color(incoming.r, incoming.g, incoming.b, current.a);
    }

    public void RegisterPrimaryGraphic(Graphic target)
    {
        if (target == null)
        {
            return;
        }

        _primaryTargets.Add(target);
        if (primaryColorPicker != null)
        {
            target.color = MergeColor(target.color, primaryColorPicker.color);
        }
    }

    public void UnregisterPrimaryGraphic(Graphic target)
    {
        if (target == null)
        {
            return;
        }

        _primaryTargets.Remove(target);
    }

    public void RegisterSecondaryGraphic(Graphic target)
    {
        if (target == null)
        {
            return;
        }

        _secondaryTargets.Add(target);
        if (secondaryColorPicker != null)
        {
            target.color = MergeColor(target.color, secondaryColorPicker.color);
        }
    }

    public void UnregisterSecondaryGraphic(Graphic target)
    {
        if (target == null)
        {
            return;
        }

        _secondaryTargets.Remove(target);
    }

    public void RegisterTextGraphic(Graphic target)
    {
        if (target == null)
        {
            return;
        }

        _textGraphicTargets.Add(target);
        if (textColorPicker != null)
        {
            target.color = MergeColor(target.color, textColorPicker.color);
        }
    }

    public void UnregisterTextGraphic(Graphic target)
    {
        if (target == null)
        {
            return;
        }

        _textGraphicTargets.Remove(target);
    }

    public void RegisterTextTMP(TMP_Text target)
    {
        if (target == null)
        {
            return;
        }

        _textTargets.Add(target);
        if (textColorPicker != null)
        {
            target.color = MergeColor(target.color, textColorPicker.color);
        }
    }

    public void UnregisterTextTMP(TMP_Text target)
    {
        if (target == null)
        {
            return;
        }

        _textTargets.Remove(target);
    }

    public void RegisterShadow(TrueShadow target)
    {
        if (target == null)
        {
            return;
        }

        _shadowTargets.Add(target);
        if (secondaryColorPicker != null)
        {
            target.Color = MergeColor(target.Color, secondaryColorPicker.color);
        }
    }

    public void UnregisterShadow(TrueShadow target)
    {
        if (target == null)
        {
            return;
        }

        _shadowTargets.Remove(target);
    }

    private void LoadColorsFromPrefs()
    {
        if (!saveToPlayerPrefs)
        {
            return;
        }

        if (TryLoadRainbowMarker(primaryColorPrefKey))
        {
            _primaryRainbowActive = true;
            _primaryRainbowInitialized = false;
            _sharedRainbowInitialized = false;
            if (primaryColorPicker != null)
            {
                primaryColorPicker.FinishTypeHex(RainbowMarkerValue);
            }
        }
        else if (primaryColorPicker != null && TryLoadColor(primaryColorPrefKey, out var primaryColor))
        {
            Debug.Log("primary color loaded : " + primaryColor);
            primaryColor.a = primaryColorPicker.color.a;
            primaryColorPicker.SetColor(primaryColor);
            UpdatePickerStartingColor(primaryColorPicker, primaryColor);
            _lastPrimaryColor = primaryColor;
        }

        if (TryLoadRainbowMarker(secondaryColorPrefKey))
        {
            _secondaryRainbowActive = true;
            _secondaryRainbowInitialized = false;
            _sharedRainbowInitialized = false;
            if (secondaryColorPicker != null)
            {
                secondaryColorPicker.FinishTypeHex(RainbowMarkerValue);
            }
        }
        else if (secondaryColorPicker != null && TryLoadColor(secondaryColorPrefKey, out var secondaryColor))
        {
            Debug.Log("secondary color loaded : " + secondaryColor);
            secondaryColor.a = secondaryColorPicker.color.a;
            secondaryColorPicker.SetColor(secondaryColor);
            UpdatePickerStartingColor(secondaryColorPicker, secondaryColor);
            _lastSecondaryColor = secondaryColor;
        }

        if (TryLoadRainbowMarker(textColorPrefKey))
        {
            _textRainbowActive = true;
            _textRainbowInitialized = false;
            _sharedRainbowInitialized = false;
            if (textColorPicker != null)
            {
                textColorPicker.FinishTypeHex(RainbowMarkerValue);
            }
        } else if (textColorPicker != null && TryLoadColor(textColorPrefKey, out var textColor))
        {
            Debug.Log("text color loaded" + textColor);
            textColor.a = textColorPicker.color.a;
            textColorPicker.SetColor(textColor);
            UpdatePickerStartingColor(textColorPicker, textColor);
            _lastTextColor = textColor;
        }
    }

    private void SyncRainbowState()
    {
        bool nextPrimaryRainbow = primaryColorPicker != null && primaryColorPicker.IsRainbowMode;
        if (nextPrimaryRainbow != _primaryRainbowActive)
        {
            _primaryRainbowActive = nextPrimaryRainbow;
            _primaryRainbowInitialized = false;
            _sharedRainbowInitialized = false;
        }

        bool nextSecondaryRainbow = secondaryColorPicker != null && secondaryColorPicker.IsRainbowMode;
        if (nextSecondaryRainbow != _secondaryRainbowActive)
        {
            _secondaryRainbowActive = nextSecondaryRainbow;
            _secondaryRainbowInitialized = false;
            _sharedRainbowInitialized = false;
        }

        bool nextTextRainbow = textColorPicker != null && textColorPicker.IsRainbowMode;
        if (nextTextRainbow != _textRainbowActive)
        {
            _textRainbowActive = nextTextRainbow;
            _textRainbowInitialized = false;
            _sharedRainbowInitialized = false;
        }
    }

    private void UpdateRainbowIfNeeded()
    {
        bool primaryPickerActive = primaryColorPicker != null && primaryColorPicker.isActiveAndEnabled;
        bool secondaryPickerActive = secondaryColorPicker != null && secondaryColorPicker.isActiveAndEnabled;
        bool textPickerActive = textColorPicker != null && textColorPicker.isActiveAndEnabled;

        if (_primaryRainbowActive && _secondaryRainbowActive && _textRainbowActive && (!primaryPickerActive || !secondaryPickerActive || !textPickerActive))
        {
            if (!_sharedRainbowInitialized)
            {
                InitSharedRainbow();
            }

            float speed = RainbowHueSpeed;
            _sharedRainbowHue = Mathf.Repeat(_sharedRainbowHue + (Time.unscaledDeltaTime * speed), 6f);

            var primaryColor = Color.HSVToRGB(_sharedRainbowHue / 6f, _primaryRainbowS, _primaryRainbowV);
            primaryColor.a = _lastPrimaryColor.a;
            _lastPrimaryColor = primaryColor;
            ApplyPrimaryColor(primaryColor);

            var secondaryColor = Color.HSVToRGB(_sharedRainbowHue / 6f, _secondaryRainbowS, _secondaryRainbowV);
            secondaryColor.a = _lastSecondaryColor.a;
            _lastSecondaryColor = secondaryColor;
            ApplySecondaryColor(secondaryColor);

            var textColor = Color.HSVToRGB(_sharedRainbowHue / 6f, _textRainbowS, _textRainbowV);
            textColor.a = _lastTextColor.a;
            _lastTextColor = textColor;
            ApplyTextColor(textColor);
            return;
        }

        if (_primaryRainbowActive && _secondaryRainbowActive && (!primaryPickerActive || !secondaryPickerActive))
        {
            if (!_sharedRainbowInitialized)
            {
                InitSharedRainbow(isTextShared : false);
            }

            float speed = RainbowHueSpeed;
            _sharedRainbowHue = Mathf.Repeat(_sharedRainbowHue + (Time.unscaledDeltaTime * speed), 6f);

            var primaryColor = Color.HSVToRGB(_sharedRainbowHue / 6f, _primaryRainbowS, _primaryRainbowV);
            primaryColor.a = _lastPrimaryColor.a;
            _lastPrimaryColor = primaryColor;
            ApplyPrimaryColor(primaryColor);

            var secondaryColor = Color.HSVToRGB(_sharedRainbowHue / 6f, _secondaryRainbowS, _secondaryRainbowV);
            secondaryColor.a = _lastSecondaryColor.a;
            _lastSecondaryColor = secondaryColor;
            ApplySecondaryColor(secondaryColor);

            return;
        }

        if (_primaryRainbowActive && _textRainbowActive && (!primaryPickerActive || !textPickerActive))
        {
            if (!_sharedRainbowInitialized)
            {
                InitSharedRainbow(isSecondaryShared : false);
            }

            float speed = RainbowHueSpeed;
            _sharedRainbowHue = Mathf.Repeat(_sharedRainbowHue + (Time.unscaledDeltaTime * speed), 6f);

            var primaryColor = Color.HSVToRGB(_sharedRainbowHue / 6f, _primaryRainbowS, _primaryRainbowV);
            primaryColor.a = _lastPrimaryColor.a;
            _lastPrimaryColor = primaryColor;
            ApplyPrimaryColor(primaryColor);

            var textColor = Color.HSVToRGB(_sharedRainbowHue / 6f, _textRainbowS, _textRainbowV);
            textColor.a = _lastTextColor.a;
            _lastTextColor = textColor;
            ApplyTextColor(textColor);
            return;
        }

        if (_secondaryRainbowActive && _textRainbowActive && (!secondaryPickerActive || !textPickerActive))
        {
            if (!_sharedRainbowInitialized)
            {
                InitSharedRainbow(isPrimaryShared: false);
            }

            float speed = RainbowHueSpeed;
            _sharedRainbowHue = Mathf.Repeat(_sharedRainbowHue + (Time.unscaledDeltaTime * speed), 6f);

            var secondaryColor = Color.HSVToRGB(_sharedRainbowHue / 6f, _secondaryRainbowS, _secondaryRainbowV);
            secondaryColor.a = _lastSecondaryColor.a;
            _lastSecondaryColor = secondaryColor;
            ApplySecondaryColor(secondaryColor);

            var textColor = Color.HSVToRGB(_sharedRainbowHue / 6f, _textRainbowS, _textRainbowV);
            textColor.a = _lastTextColor.a;
            _lastTextColor = textColor;
            ApplyTextColor(textColor);
            return;
        }

        if (_primaryRainbowActive && !primaryPickerActive)
        {
            if (!_primaryRainbowInitialized)
            {
                InitPrimaryRainbow();
            }

            float speed = RainbowHueSpeed;
            _primaryRainbowHue = Mathf.Repeat(_primaryRainbowHue + (Time.unscaledDeltaTime * speed), 6f);
            var rainbowColor = Color.HSVToRGB(_primaryRainbowHue / 6f, _primaryRainbowS, _primaryRainbowV);
            rainbowColor.a = _lastPrimaryColor.a;
            _lastPrimaryColor = rainbowColor;
            ApplyPrimaryColor(rainbowColor);
        }

        if (_secondaryRainbowActive && !secondaryPickerActive)
        {
            if (!_secondaryRainbowInitialized)
            {
                InitSecondaryRainbow();
            }

            float speed = RainbowHueSpeed;
            _secondaryRainbowHue = Mathf.Repeat(_secondaryRainbowHue + (Time.unscaledDeltaTime * speed), 6f);
            var rainbowColor = Color.HSVToRGB(_secondaryRainbowHue / 6f, _secondaryRainbowS, _secondaryRainbowV);
            rainbowColor.a = _lastSecondaryColor.a;
            _lastSecondaryColor = rainbowColor;
            ApplySecondaryColor(rainbowColor);
        }

        if (_textRainbowActive && !textPickerActive)
        {
            if (!_textRainbowInitialized)
            {
                InitTextRainbow();
            }

            float speed = RainbowHueSpeed;
            _textRainbowHue = Mathf.Repeat(_textRainbowHue + (Time.unscaledDeltaTime * speed), 6f);
            var rainbowColor = Color.HSVToRGB(_textRainbowHue / 6f, _textRainbowS, _textRainbowV);
            rainbowColor.a = _lastTextColor.a;
            _lastTextColor = rainbowColor;
            ApplyTextColor(rainbowColor);
        }
    }

    private void InitPrimaryRainbow()
    {
        Color seed = _lastPrimaryColor != default ? _lastPrimaryColor : Color.white;
        Color.RGBToHSV(seed, out var h, out var s, out var v);
        _primaryRainbowHue = h * 6f;
        _primaryRainbowS = 1f;
        _primaryRainbowV = 1f;
        _primaryRainbowInitialized = true;
    }

    private void InitSecondaryRainbow()
    {
        Color seed = _lastSecondaryColor != default ? _lastSecondaryColor : Color.white;
        Color.RGBToHSV(seed, out var h, out var s, out var v);
        _secondaryRainbowHue = h * 6f;
        _secondaryRainbowS = 1f;
        _secondaryRainbowV = 1f;
        _secondaryRainbowInitialized = true;
    }

    private void InitTextRainbow()
    {
        Color seed = _lastTextColor != default ? _lastTextColor : Color.white;
        Color.RGBToHSV(seed, out var h, out var s, out var v);
        _textRainbowHue = h * 6f;
        _textRainbowS = 1f;
        _textRainbowV = 1f;
        _textRainbowInitialized = true;
    }

    private void InitSharedRainbow(bool isPrimaryShared = true, bool isSecondaryShared = true,bool isTextShared = true)
    {
        Color seed = _lastPrimaryColor != default ? _lastPrimaryColor : _lastSecondaryColor;
        if (seed == default)
        {
            seed = Color.white;
        }

        Color.RGBToHSV(seed, out var h, out var s, out var v);
        _sharedRainbowHue = h * 6f;

        if (isPrimaryShared)
        {
            _primaryRainbowS = 1f;
            _primaryRainbowV = 1f;
            _primaryRainbowInitialized = true;
        }

        if (isSecondaryShared)
        {
            _secondaryRainbowS = 1f;
            _secondaryRainbowV = 1f;
            _secondaryRainbowInitialized = true;
        }

        if (isTextShared)
        {
            _textRainbowS = 1f;
            _textRainbowV = 1f;
            _textRainbowInitialized = true;
        }

        _sharedRainbowInitialized = true;
    }

    private static void UpdatePickerStartingColor(FlexibleColorPicker picker, Color color)
    {
        if (picker == null)
        {
            return;
        }

        var field = typeof(FlexibleColorPicker).GetField("startingColor", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
        {
            return;
        }

        field.SetValue(picker, color);
    }

    private void SaveColor(string key, Color color)
    {
        if (!saveToPlayerPrefs || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var hex = ColorUtility.ToHtmlStringRGB(color);
        PlayerPrefs.SetString(key, "#" + hex);
    }

    private void SaveRainbowMarkersIfNeeded()
    {
        if (_primaryRainbowActive)
        {
            SaveRainbowMarker(primaryColorPrefKey);
        }

        if (_secondaryRainbowActive)
        {
            SaveRainbowMarker(secondaryColorPrefKey);
        }

        if (_textRainbowActive)
        {
            SaveRainbowMarker(textColorPrefKey);
        }
    }

    private void SaveRainbowMarker(string key)
    {
        if (!saveToPlayerPrefs || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        PlayerPrefs.SetString(key, RainbowMarkerValue);
    }

    private static bool TryLoadRainbowMarker(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !PlayerPrefs.HasKey(key))
        {
            return false;
        }

        var raw = PlayerPrefs.GetString(key, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return string.Equals(raw.Trim(), RainbowMarkerValue, System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryLoadColor(string key, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(key) || !PlayerPrefs.HasKey(key))
        {
            return false;
        }

        var raw = PlayerPrefs.GetString(key, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return ColorUtility.TryParseHtmlString(raw, out color);
    }
}
