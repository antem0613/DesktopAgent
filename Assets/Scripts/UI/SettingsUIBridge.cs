using Unity.Logging;
using UnityEngine;
using UnityEngine.Localization.Settings;

/// <summary>
/// UIイベントからアプリ設定を更新し、必要に応じて即時保存します。
/// </summary>
public sealed class SettingsUIBridge : MonoBehaviour
{
    [SerializeField] private bool saveImmediately = true;

    /// <summary>
    /// キャラクタースケールを更新し、表示にも即時反映します。
    /// </summary>
    public void SetCharacterScale(float value)
    {
        var manager = CharacterManager.Instance;
        if (manager != null)
        {
            manager.SetCharacterScale(value, saveImmediately);
            return;
        }

        var appSettings = ApplicationSettings.Instance;
        if (appSettings?.Character == null)
        {
            return;
        }

        appSettings.Character.Scale = value;
        Save(appSettings);
    }

    /// <summary>
    /// ボイス音量を更新します。
    /// </summary>
    public void SetVoiceVolume(float value)
    {
        var appSettings = ApplicationSettings.Instance;
        if (appSettings?.Sound == null)
        {
            return;
        }

        appSettings.Sound.VoiceVolume = value;
        Save(appSettings);
    }

    /// <summary>
    /// 効果音音量を更新します。
    /// </summary>
    public void SetSeVolume(float value)
    {
        var appSettings = ApplicationSettings.Instance;
        if (appSettings?.Sound == null)
        {
            return;
        }

        appSettings.Sound.SEVolume = value;
        Save(appSettings);
    }

    /// <summary>
    /// ターゲットFPSを更新し、ランタイムにも適用します。
    /// </summary>
    public void SetFrameRate(float value)
    {
        var appSettings = ApplicationSettings.Instance;
        if (appSettings?.Performance == null)
        {
            return;
        }

        int target = Mathf.RoundToInt(value);
        appSettings.Performance.TargetFrameRate = target;
        Application.targetFrameRate = appSettings.Performance.TargetFrameRate;
        Save(appSettings);
    }

    /// <summary>
    /// 品質レベルを更新し、ランタイムにも適用します。
    /// </summary>
    public void SetQualityLevel(float value)
    {
        var appSettings = ApplicationSettings.Instance;
        if (appSettings?.Performance == null)
        {
            return;
        }

        int next = Mathf.RoundToInt(value);
        appSettings.Performance.QualityLevel = next;
        QualitySettings.SetQualityLevel(appSettings.Performance.QualityLevel, true);
        Save(appSettings);
    }

    /// <summary>
    /// 言語コードを更新し、ロケールも切り替えます。
    /// </summary>
    public void SetLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return;
        }

        var locale = LocalizationSettings.AvailableLocales.GetLocale(languageCode);
        if (locale == null)
        {
            Log.Warning($"[SettingsUIBridge] Locale not found: {languageCode}");
            return;
        }

        LocalizationSettings.SelectedLocale = locale;

        var appSettings = ApplicationSettings.Instance;
        if (appSettings?.Display == null)
        {
            return;
        }

        appSettings.Display.Language = languageCode;
        Save(appSettings);
    }

    /// <summary>
    /// AvailableLocalesの並び順インデックスで言語を更新します。
    /// </summary>
    public void SetLanguageIndex(int index)
    {
        var locales = LocalizationSettings.AvailableLocales.Locales;
        if (locales == null || index < 0 || index >= locales.Count)
        {
            Log.Warning($"[SettingsUIBridge] Invalid locale index: {index}");
            return;
        }

        SetLanguage(locales[index].Identifier.Code);
    }

    /// <summary>
    /// 即時保存がOFFのときに手動保存するための公開メソッドです。
    /// </summary>
    public void SaveNow()
    {
        ApplicationSettings.Instance?.SaveSettings();
    }

    private void Save(ApplicationSettings settings)
    {
        if (saveImmediately)
        {
            settings.SaveSettings();
        }
    }
}
