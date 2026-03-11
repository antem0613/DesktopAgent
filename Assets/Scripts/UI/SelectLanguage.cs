using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Localization.Settings;
using System.Collections.Generic;

public class SelectLanguage : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown languageDropdown;
    List<string> languages = new List<string>();

    void Start()
    {
        var locales = LocalizationSettings.AvailableLocales.Locales;
        languageDropdown.ClearOptions();

        foreach (var locale in locales)
        {
            languages.Add(locale.Identifier.Code);
            languageDropdown.options.Add(new TMP_Dropdown.OptionData(locale.LocaleName));
        }

        languageDropdown.value = languages.IndexOf(LocalizationSettings.SelectedLocale.Identifier.Code);
    }

    void Update()
    {
        
    }

    public void OnLanguageSelected(int index)
    {
        string selectedLanguage = languages[index];
        
        LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale(selectedLanguage);

    }
}
