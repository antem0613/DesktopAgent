using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Logging;
using UnityEngine;

/// <summary>
///    メニューのプレゼンター
/// </summary>
public partial class MenuPresenter : IDisposable
{
    /// <summary>
    ///    メニューのビュー
    /// </summary>
    private readonly MenuDialog _menuDialog;

    /// <summary>
    ///   メニューが開かれているかどうか
    /// </summary>
    public bool IsOpened { get; private set; }

    /// <summary>
    /// キャンセルトークンソース
    /// </summary>
    private readonly CancellationTokenSource _cancellationTokenSource;

    /// <summary>
    ///  メニューの表示位置のオフセット
    /// </summary>
    private static readonly Vector3 MenuOffset = new Vector3(2.5f, 2, -1);

    public MenuPresenter(MenuDialog menuDialog)
    {
        this._menuDialog = menuDialog;

        IsOpened = false;

        _cancellationTokenSource = new CancellationTokenSource();

        Hide();
    }

    /// <summary>
    ///   メニューを表示する
    /// </summary>
    /// <param name="screenPosition"></param>
    public void Show()
    {
        IsOpened = true;
        _menuDialog.Show(_cancellationTokenSource.Token).Forget();
    }

    /// <summary>
    ///  メニューを非表示にする
    /// </summary>
    public void Hide()
    {
        IsOpened = false;
        _menuDialog.Hide();
    }

    /// <summary>
    /// 設定ファイルおよびフォルダを開く
    /// </summary>
    private void OpenAppSetting()
    {
        string folderPath = Application.streamingAssetsPath;
        string filePath = ApplicationSettingsPath.GetPath();

        Log.Info($"Opening settings file: {filePath}");
        Log.Info($"Opening settings folder: {folderPath}");

#if UNITY_EDITOR
        // In Unity Editor, open the folder and file
        if (Directory.Exists(folderPath))
        {
            UnityEditor.EditorUtility.OpenWithDefaultApp(folderPath);
        } else
        {
            Log.Warning($"Folder not found: {folderPath}");
        }

        if (File.Exists(filePath))
        {
            UnityEditor.EditorUtility.OpenWithDefaultApp(filePath);
        } else
        {
            Log.Warning($"File not found: {filePath}");
        }
#else
        bool openedFolder = false;
        bool openedFile = false;
        try
        {
            // Open the folder
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
            {
                FileName = folderPath,
                UseShellExecute = true,
                Verb = "open"
            });
            openedFolder = true;
        } catch (Exception e)
        {
            Log.Warning("Process.Start failed to open folder: " + e);
        }

        try
        {
            // Open the file
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
            {
                FileName = filePath,
                UseShellExecute = true,
                Verb = "open"
            });
            openedFile = true;
        } catch (Exception e)
        {
            Log.Warning("Process.Start failed to open file: " + e);
        }

        if (!openedFolder)
        {
            // Fallback to Application.OpenURL for folder
            Application.OpenURL("file://" + folderPath.Replace("\\", "/"));
        }

        if (!openedFile)
        {
            // Fallback to Application.OpenURL for file
            Application.OpenURL("file://" + filePath.Replace("\\", "/"));
        }
#endif
    }


    /// <summary>
    /// アプリ終了
    /// </summary>
    private void CloseApp()
    {
        Log.Debug("Close App");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }
}