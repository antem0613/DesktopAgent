using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Pool;
using Unity.Logging;

/// <summary>
/// UI管理クラス
/// </summary>
public class UIManager : SingletonMonoBehaviour<UIManager>
{
    /// <summary>
    /// ダイアログスタック
    /// </summary>
    private readonly Stack<DialogBase> _dialogStack = new Stack<DialogBase>();

    /// <summary>
    /// ダイアログが開いているかどうか
    /// </summary>
    public bool HasOpenedDialogs => _dialogStack.Count > 0;

    /// <summary>
    /// ダイアログを閉じる
    /// </summary>
    public async UniTask PopDialogAsync()
    {
        if (_dialogStack.Count > 0)
        {
            DialogBase dialog = _dialogStack.Pop();

            // ダイアログの非表示
            await dialog.HideAsync();
        } else
        {
            Log.Warning("ダイアログスタックが空です。");
        }
    }
}