using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using LitMotion.Extensions;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
///   ダイアログのベースクラス
/// </summary>
public class DialogBase : MonoBehaviour
{
    /// <summary>
    /// キャンバスグループ
    /// </summary>
    private protected CanvasGroup CanvasGroup;

    /// <summary>
    ///  awake
    /// </summary>
    private protected virtual void Awake()
    {
        CanvasGroup = GetComponent<CanvasGroup>();
    }

    /// <summary>
    /// ダイアログの初期化処理
    /// </summary>
    public virtual void Initialize()
    {
        Show();
    }

    /// <summary>
    /// ダイアログの状態をリセットする
    /// </summary>
    public virtual void Reset()
    {
    }

    /// <summary>
    ///  ダイアログを表示する
    /// </summary>
    public virtual void Show()
    {
        CanvasGroup.alpha = 1f;
        CanvasGroup.interactable = true;
        CanvasGroup.blocksRaycasts = true;
    }

    /// <summary>
    /// ダイアログを表示する
    /// </summary>
    public virtual async UniTask ShowAsync(CancellationToken cancellationToken = default, float fadeAnimationTime = Constant.UIAnimationTime, Ease ease = Constant.UIAnimationDefaultEase)
    {
        // フェードイン
        await LMotion.Create(0f, 1f, fadeAnimationTime)
            .WithEase(ease)
            .BindToAlpha(CanvasGroup).ToUniTask(cancellationToken: cancellationToken);

        Show();
    }

    /// <summary>
    /// ダイアログを非表示にする
    /// </summary>
    public virtual void Hide()
    {
        CanvasGroup.alpha = 0f;
        CanvasGroup.interactable = false;
        CanvasGroup.blocksRaycasts = false;
    }

    /// <summary>
    /// ダイアログを非表示にする
    /// </summary>
    public virtual async UniTask HideAsync(CancellationToken cancellationToken = default, float fadeAnimationTime = Constant.UIAnimationTime, Ease ease = Constant.UIAnimationDefaultEase)
    {
        // フェードアウト
        await LMotion.Create(1f, 0f, fadeAnimationTime)
            .WithEase(ease)
            .BindToAlpha(CanvasGroup).ToUniTask(cancellationToken: cancellationToken);

        Hide();
    }
}