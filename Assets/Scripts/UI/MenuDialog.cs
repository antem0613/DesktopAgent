using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Logging;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
///     メニューのビュー
/// </summary>
public class MenuDialog : DialogBase
{
    /// <summary>
    ///    メニューのRectTransform
    /// </summary>
    private RectTransform _menuRectTransform;
    private CancellationTokenSource _shortcutCancellationTokenSource;

    private protected override void Awake()
    {
        base.Awake();
        _menuRectTransform = GetComponent<RectTransform>();
        _shortcutCancellationTokenSource = new CancellationTokenSource();

        Hide();
    }

    private void OnEnable()
    {
        RegisterShortcutCallbacks();
    }

    private void OnDisable()
    {
        UnregisterShortcutCallbacks();
    }

    private void OnDestroy()
    {
        UnregisterShortcutCallbacks();
        _shortcutCancellationTokenSource?.Cancel();
        _shortcutCancellationTokenSource?.Dispose();
        _shortcutCancellationTokenSource = null;
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

    private void RegisterShortcutCallbacks()
    {
        var inputController = InputController.Instance;
        if (inputController == null)
        {
            return;
        }

        inputController.Shortcut.ToggleShowDialog.performed -= OnToggleShowDialogPerformed;
        inputController.Shortcut.ToggleShowDialog.performed += OnToggleShowDialogPerformed;
    }

    private void UnregisterShortcutCallbacks()
    {
        var inputController = InputController.Instance;
        if (inputController == null)
        {
            return;
        }

        inputController.Shortcut.ToggleShowDialog.performed -= OnToggleShowDialogPerformed;
    }

    private void OnToggleShowDialogPerformed(InputAction.CallbackContext _)
    {
        if (CanvasGroup != null && CanvasGroup.blocksRaycasts)
        {
            Hide();
            return;
        }

        if (_shortcutCancellationTokenSource == null || _shortcutCancellationTokenSource.IsCancellationRequested)
        {
            _shortcutCancellationTokenSource?.Dispose();
            _shortcutCancellationTokenSource = new CancellationTokenSource();
        }

        Show(_shortcutCancellationTokenSource.Token).Forget();
    }
}