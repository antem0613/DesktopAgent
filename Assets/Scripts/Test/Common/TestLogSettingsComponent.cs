using System.Collections.Generic;
using UnityEngine;

public class TestLogSettingsComponent : MonoBehaviour
{
    [SerializeField] private bool applyOnAwake = true;
    [SerializeField] private bool infoEnabled = true;
    [SerializeField] private bool warningEnabled = true;
    [SerializeField] private bool errorEnabled = true;
    [SerializeField] private bool useTagWhitelist = false;
    [SerializeField] private List<string> tagWhitelist = new();

    private void Awake()
    {
        if (applyOnAwake)
        {
            Apply();
        }
    }

    public void Apply()
    {
        TestLog.Configure(
            infoEnabled,
            warningEnabled,
            errorEnabled,
            useTagWhitelist,
            tagWhitelist);
    }

    public void ResetToDefault()
    {
        TestLog.ResetConfiguration();
    }
}