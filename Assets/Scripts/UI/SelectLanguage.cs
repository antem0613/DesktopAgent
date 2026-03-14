using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Localization.Settings;
using System.Collections.Generic;
using Unity.Logging;

public class SelectLanguage : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown languageDropdown;
    private readonly List<string> languages = new List<string>();

    void Start()
    {
        var locales = LocalizationSettings.AvailableLocales.Locales;
        languageDropdown.ClearOptions();
        languages.Clear();

        foreach (var locale in locales)
        {
            languages.Add(locale.Identifier.Code);
            languageDropdown.options.Add(new TMP_Dropdown.OptionData(locale.LocaleName));
        }

        string configuredLanguage = ApplicationSettings.Instance?.Display?.Language ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(configuredLanguage))
        {
            var configuredLocale = LocalizationSettings.AvailableLocales.GetLocale(configuredLanguage);
            if (configuredLocale != null)
            {
                LocalizationSettings.SelectedLocale = configuredLocale;
            }
        }

        int selectedIndex = languages.IndexOf(LocalizationSettings.SelectedLocale.Identifier.Code);
        languageDropdown.value = selectedIndex >= 0 ? selectedIndex : 0;
    }

    void Update()
    {
        
    }

    public void OnLanguageSelected(int index)
    {
        if (index < 0 || index >= languages.Count)
        {
            Log.Warning($"[SelectLanguage] Invalid language index: {index}");
            return;
        }

        string selectedLanguage = languages[index];

        LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale(selectedLanguage);

        var appSettings = ApplicationSettings.Instance;
        if (appSettings?.Display != null)
        {
            appSettings.Display.Language = selectedLanguage;
            appSettings.SaveSettings();
        }

    }
}
