using UnityEngine;
using System.Collections.Generic;

public static class TestLog
{
    private static bool _infoEnabled = true;
    private static bool _warningEnabled = true;
    private static bool _errorEnabled = true;
    private static bool _useTagWhitelist;
    private static readonly HashSet<string> _tagWhitelist = new(System.StringComparer.OrdinalIgnoreCase);

    public static void Configure(bool infoEnabled, bool warningEnabled, bool errorEnabled, bool useTagWhitelist, IReadOnlyList<string> tags)
    {
        _infoEnabled = infoEnabled;
        _warningEnabled = warningEnabled;
        _errorEnabled = errorEnabled;
        _useTagWhitelist = useTagWhitelist;

        _tagWhitelist.Clear();
        if (tags == null)
        {
            return;
        }

        for (int i = 0; i < tags.Count; i++)
        {
            string tag = tags[i];
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            _tagWhitelist.Add(tag.Trim());
        }
    }

    public static void ResetConfiguration()
    {
        _infoEnabled = true;
        _warningEnabled = true;
        _errorEnabled = true;
        _useTagWhitelist = false;
        _tagWhitelist.Clear();
    }

    public static void Info(string scriptName, string message)
    {
        if (!_infoEnabled || !IsTagEnabled(scriptName))
        {
            return;
        }

        Debug.Log($"[{scriptName}] {message}");
    }

    public static void Warning(string scriptName, string message)
    {
        if (!_warningEnabled || !IsTagEnabled(scriptName))
        {
            return;
        }

        Debug.LogWarning($"[{scriptName}] {message}");
    }

    public static void Error(string scriptName, string message)
    {
        if (!_errorEnabled || !IsTagEnabled(scriptName))
        {
            return;
        }

        Debug.LogError($"[{scriptName}] {message}");
    }

    private static bool IsTagEnabled(string scriptName)
    {
        if (!_useTagWhitelist)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(scriptName))
        {
            return false;
        }

        return _tagWhitelist.Contains(scriptName);
    }
}