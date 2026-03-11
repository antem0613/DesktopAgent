using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using Unity.Logging;
using System.Reflection;

/// <summary>
///     アプリケーション設定を管理するクラス
/// </summary>
public class ApplicationSettings : Singleton<ApplicationSettings>
{
    /// <summary>
    ///    設定ファイルのパス
    /// </summary>
    private static string _settingsFilePath;

    /// <summary>
    ///   キャラクター設定
    /// </summary>
    public CharacterSettings Character { get; private set; }

    /// <summary>
    ///  サウンド設定
    /// </summary>
    public SoundSettings Sound { get; private set; }

    /// <summary>
    /// ディスプレイ設定
    /// </summary>
    public DisplaySettings Display { get; private set; }

    /// <summary>
    /// パフォーマンス設定
    /// </summary>
    public PerformanceSettings Performance { get; private set; }

    /// <summary>
    /// バックエンド設定
    /// </summary>
    public BackendSettings Backend { get; private set; }

    /// <summary>
    /// ショートカット設定
    /// </summary>
    public ShortcutSettings Shortcuts { get; private set; }

    /// <summary>
    /// 設定ファイルが存在するかどうか
    /// </summary>
    public bool IsLoaded { get; private set; } = false;

    /// <summary>
    ///   コンストラクタ
    /// </summary>
    public ApplicationSettings()
    {
        try
        {
            var provider = SettingsProvider.Instance;
            _settingsFilePath = provider?.FilePath;

            // SettingsProvider(JSON)を単一の真実のソースとして利用する。
            Character = provider?.Character ?? new CharacterSettings();
            Sound = provider?.Sound ?? new SoundSettings();
            Display = provider?.Display ?? new DisplaySettings();
            Performance = provider?.Performance ?? new PerformanceSettings();
            Backend = provider?.Backend ?? new BackendSettings();
            Shortcuts = provider?.Shortcuts ?? new ShortcutSettings();

            LoadSettings();

            IsLoaded = provider != null && provider.IsLoaded;
        } catch (Exception ex)
        {
            Log.Error("設定の初期化中にエラーが発生しました: " + ex.Message);
        }
    }

    /// <summary>
    ///     設定を読み込む
    /// </summary>
    private void LoadSettings()
    {
        var provider = SettingsProvider.Instance;
        if (provider != null)
        {
            Character = provider.Character;
            Sound = provider.Sound;
            Display = provider.Display;
            Performance = provider.Performance;
            Backend = provider.Backend;
            Shortcuts = provider.Shortcuts;

            _settingsFilePath = provider.FilePath;
            Log.Info("設定ファイル(JSON)を読み込みました: " + _settingsFilePath);

            // 設定値の検証と修正
            ValidateSettings();
        } else
        {
            Log.Warning("SettingsProvider が利用できないため、既定値で初期化します。");
        }
    }

    /// <summary>
    ///    設定をファイルに保存する
    /// </summary>
    /// <param name="settingsData"></param>
    private void AssignValues(Dictionary<string, Dictionary<string, string>> settingsData)
    {
        if (settingsData == null || settingsData.Count == 0)
        {
            // データがない場合は何もしない
            return;
        }

        foreach (var section in settingsData)
        {
            switch (section.Key)
            {
                case "Character":
                    AssignSettings(Character, section.Value);
                    break;
                case "Sound":
                    AssignSettings(Sound, section.Value);
                    break;
                case "Display":
                    AssignSettings(Display, section.Value);
                    break;
                case "Performance":
                    AssignSettings(Performance, section.Value);
                    break;
                case "Backend":
                    AssignSettings(Backend, section.Value);
                    break;
                case "Shortcuts":
                    AssignSettings(Shortcuts, section.Value);
                    break;
                default:
                    Log.Warning($"未知の設定セクションが見つかりました: {section.Key}");
                    break;
            }
        }
    }

    /// <summary>
    ///    設定クラスに値を設定する
    /// </summary>
    /// <param name="settingsInstance"></param>
    /// <param name="values"></param>
    /// <typeparam name="T"></typeparam>
    private void AssignSettings<T>(T settingsInstance, Dictionary<string, string> values)
    {
        if (settingsInstance == null || values == null)
        {
            return;
        }

        var type = typeof(T);
        foreach (var kvp in values)
        {
            var property = type.GetProperty(kvp.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property != null && property.CanWrite)
            {
                try
                {
                    object value;
                    if (property.PropertyType == typeof(List<string>))
                    {
                        var list = kvp.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(item => item.Trim())
                            .ToList();
                        value = list;
                    } else
                    {
                        // 通常の値の変換
                        value = ConvertValue(property.PropertyType, kvp.Value, property.GetValue(settingsInstance));
                    }
                    property.SetValue(settingsInstance, value);
                } catch (Exception ex)
                {
                    Log.Warning($"セクション '{type.Name}' のプロパティ '{property.Name}' に値 '{kvp.Value}' を設定できませんでした: {ex.Message}");
                }
            } else
            {
                Log.Warning($"プロパティ '{kvp.Key}' が設定クラス '{type.Name}' に見つかりませんでした。");
            }
        }
    }

    /// <summary>
    ///   文字列を指定した型に変換する
    /// </summary>
    /// <param name="targetType"></param>
    /// <param name="value"></param>
    /// <param name="defaultValue"></param>
    /// <returns></returns>
    private object ConvertValue(Type targetType, string value, object defaultValue)
    {
        try
        {
            if (targetType == typeof(string))
            {
                return value;
            } else if (targetType == typeof(int))
            {
                if (int.TryParse(value, out var intValue))
                {
                    return intValue;
                }
            } else if (targetType == typeof(float))
            {
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
                {
                    return floatValue;
                }
            } else if (targetType == typeof(bool))
            {
                if (bool.TryParse(value, out var boolValue))
                {
                    return boolValue;
                } else
                {
                    // "1", "0" 形式の真偽値に対応
                    if (value == "1") return true;
                    if (value == "0") return false;
                }
            } else if (targetType.IsEnum)
            {
                if (Enum.TryParse(targetType, value, true, out var enumValue))
                {
                    return enumValue;
                }

                Log.Warning($"値 '{value}' を列挙型 '{targetType.Name}' に変換できませんでした。デフォルト値を使用します。");
                return defaultValue;
            }

            // 変換できない場合はデフォルト値を返す
            Log.Warning($"値 '{value}' を型 '{targetType.Name}' に変換できませんでした。デフォルト値を使用します。");
            return defaultValue;
        } catch (Exception ex)
        {
            Log.Warning($"値 '{value}' を型 '{targetType.Name}' に変換中にエラーが発生しました: {ex.Message}。デフォルト値を使用します。");
            return defaultValue;
        }
    }

    /// <summary>
    ///     設定をファイルに保存する
    /// </summary>
    public void SaveSettings()
    {
        try
        {
            var provider = SettingsProvider.Instance;
            if (provider != null)
            {
                provider.Save();
                _settingsFilePath = provider.FilePath;
                Log.Info("設定ファイル(JSON)を保存しました: " + _settingsFilePath);
                return;
            }

            Log.Warning("SettingsProvider が利用できないため、設定保存をスキップしました。");
        } catch (Exception ex)
        {
            Log.Error("設定ファイルの保存中にエラーが発生しました: " + ex.Message);
        }
    }

    /// <summary>
    /// 設定値を検証し、無効な値を修正する
    /// </summary>
    private void ValidateSettings()
    {
        bool settingsModified = false;

        // PerformanceSettings の検証
        var qualityLevel = Performance.QualityLevel;
        if (qualityLevel < 0 || qualityLevel >= QualitySettings.names.Length)
        {
            // 無効な QualityLevel の場合、動的に調整
            Performance.QualityLevel = QualityLevelUtility.AdjustQualityLevel();
            Log.Warning($"無効な QualityLevel が設定されていたため、{Performance.QualityLevel} に設定しました。");
            settingsModified = true;
        }

        // 他の設定値も同様に検証
        // 例: TargetFrameRate
        if (Performance.TargetFrameRate <= 0)
        {
            Performance.TargetFrameRate = 60; // デフォルト値
            Log.Warning($"無効な TargetFrameRate が設定されていたため、デフォルト値 {Performance.TargetFrameRate} に設定しました。");
            settingsModified = true;
        }

        // 設定値が変更された場合、設定ファイルを保存
        if (settingsModified)
        {
            SaveSettings();
            Log.Info("検証結果を設定ファイルに保存しました。");
        }
    }

    /// <summary>
    ///    セクションをファイルに書き込む
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="sectionName"></param>
    /// <param name="settingsInstance"></param>
    /// <typeparam name="T"></typeparam>
    private void WriteSection<T>(StreamWriter writer, string sectionName, T settingsInstance) where T : class
    {
        writer.WriteLine($"[{sectionName}]");
        var type = typeof(T);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var property in properties)
        {
            var value = property.GetValue(settingsInstance);
            writer.WriteLine($"{property.Name}={value}");
        }
        writer.WriteLine(); // セクション間の空行
    }
}