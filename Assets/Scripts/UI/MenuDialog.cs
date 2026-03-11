using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Logging;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
///     メニューのビュー
/// </summary>
public class MenuDialog : DialogBase
{
    /// <summary>
    ///    メニューのRectTransform
    /// </summary>
    private RectTransform _menuRectTransform;

    private protected override void Awake()
    {
        base.Awake();
        _menuRectTransform = GetComponent<RectTransform>();

        Hide();
    }

    /// <summary>
    ///    メニューを表示する
    /// </summary>
    /// <param name="screenPosition"></param>
    /// <param name="cancellationToken"></param>
    public async UniTask Show(CancellationToken cancellationToken)
    {
        await ShowAsync(cancellationToken);
    }
}