using System.IO;
using UnityEngine;

/// <summary>
/// アプリケーション設定ファイルのパスを返すだけのユーティリティ。
/// 責務は「パス解決」のみ。
/// </summary>
public static class ApplicationSettingsPath
{
    public const string FileName = "application_settings.txt";

    /// <summary>
    /// StreamingAssets 直下の application_settings.txt のフルパスを返す。
    /// </summary>
    public static string GetPath()
    {
        return Path.Combine(Application.streamingAssetsPath, FileName);
    }
}
