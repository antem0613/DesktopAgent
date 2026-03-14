using UnityEngine.InputSystem;

/// <summary>
///   入力コントローラー
/// </summary>
public class InputController : Singleton<InputController>
{
    /// <summary>
    /// Input Systemのアクション
    /// </summary>
    private readonly MainInputActions _inputActions;

    /// <summary>
    /// UIアクション
    /// </summary>
    public MainInputActions.UIActions UI => _inputActions.UI;

    /// <summary>
    /// デバッグアクション
    /// </summary>
    public MainInputActions.DebugActions Debug => _inputActions.Debug;

    /// <summary>
    /// ショートカットアクション
    /// </summary>
    public MainInputActions.ShortcutActions Shortcut => _inputActions.Shortcut;

    /// <summary>
    /// InputActionAsset
    /// </summary>
    public InputActionAsset Asset => _inputActions.asset;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public InputController()
    {
        _inputActions = new MainInputActions();
        ShortcutBindingService.Initialize(_inputActions.asset);
        _inputActions.UI.Enable();
        _inputActions.Debug.Enable();
        _inputActions.Shortcut.Enable();
    }

}