using UnityEngine.InputSystem;

/// <summary>
///   入力コントローラー
/// </summary>
public class InputController : Singleton<InputController>
{
    /// <summary>
    /// Input Systemのアクション
    /// </summary>
    private readonly UDMInputActions _inputActions;

    /// <summary>
    /// UIアクション
    /// </summary>
    public UDMInputActions.UIActions UI => _inputActions.UI;

    /// <summary>
    /// デバッグアクション
    /// </summary>
    public UDMInputActions.DebugActions Debug => _inputActions.Debug;

    /// <summary>
    /// InputActionAsset
    /// </summary>
    public InputActionAsset Asset => _inputActions.asset;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public InputController()
    {
        _inputActions = new UDMInputActions();
        ShortcutBindingService.Initialize(_inputActions.asset);
        _inputActions.UI.Enable();
        _inputActions.Debug.Enable();
    }

}