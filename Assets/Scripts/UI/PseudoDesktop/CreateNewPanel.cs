using UnityEngine;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Buttonにアタッチして、指定したUI PanelプレハブをCanvas配下に生成するコンポーネント。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class CreateNewPanel : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] private RectTransform panelPrefab;

    [Header("Spawn Parent")]
    [Tooltip("生成先のRectTransform。未指定なら、親階層のCanvas配下に生成します")]
    [SerializeField] private RectTransform spawnParent;

    [Header("Spawn Position")]
    [Tooltip("trueの場合、ポインタ位置に生成します。falseの場合はCanvas中央")]
    [SerializeField] private bool spawnAtPointer = false;

    [Tooltip("生成時の追加オフセット(親のローカル座標)")]
    [SerializeField] private Vector2 spawnOffset = Vector2.zero;

    [Tooltip("生成後に最前面にする")]
    [SerializeField] private bool bringToFront = true;

    [Header("Auto Configure")]
    [Tooltip("生成したウィンドウに必要コンポーネントを自動付与します")]
    [SerializeField] private bool autoAddWindowComponents = true;

    private Button _button;
    private int _lastCreateFrame = -1;

    private void Awake()
    {
        _button = GetComponent<Button>();
    }

    public void Create()
    {
        // Inspector側のonClick設定等で同フレームに二重呼び出しされるケースを抑止
        if (_lastCreateFrame == Time.frameCount)
        {
            return;
        }
        _lastCreateFrame = Time.frameCount;

        if (panelPrefab == null)
        {
            Debug.LogWarning("CreateNewPanel: panelPrefab が未設定です", this);
            return;
        }

        var parent = ResolveSpawnParent();
        if (parent == null)
        {
            Debug.LogWarning("CreateNewPanel: 生成先のCanvas/RectTransformが見つかりません", this);
            return;
        }

        var instance = Instantiate(panelPrefab, parent, false);
        instance.name = panelPrefab.name;

        if (autoAddWindowComponents)
        {
            // 座り判定のRect供給
            if (instance.GetComponent<CanvasWindowRect>() == null)
            {
                instance.gameObject.AddComponent<CanvasWindowRect>();
            }

            // GameViewでの移動/リサイズ
            if (instance.GetComponent<CanvasWindowDragResize>() == null)
            {
                instance.gameObject.AddComponent<CanvasWindowDragResize>();
            }

            // 前面化/選択色（必要なら）
            if (instance.GetComponent<SelectableWindow>() == null)
            {
                instance.gameObject.AddComponent<SelectableWindow>();
            }
        }

        if (bringToFront)
        {
            instance.SetAsLastSibling();
        }

        var selectable = instance.GetComponent<SelectableWindow>();
        if (selectable != null)
        {
            selectable.Focus();
        }

        var cam = ResolveEventCamera(parent);
        var anchoredPos = spawnAtPointer
            ? ScreenToAnchoredPosition(parent, cam, GetPointerScreenPosition())
            : Vector2.zero;

        instance.anchoredPosition = anchoredPos + spawnOffset;
    }

    private RectTransform ResolveSpawnParent()
    {
        if (spawnParent != null)
        {
            return spawnParent;
        }

        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            return canvas.transform as RectTransform;
        }

        return null;
    }

    private static Camera ResolveEventCamera(RectTransform parent)
    {
        var canvas = parent != null ? parent.GetComponentInParent<Canvas>() : null;
        if (canvas == null)
        {
            return null;
        }

        return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }

    private static Vector2 ScreenToAnchoredPosition(RectTransform parent, Camera cam, Vector2 screen)
    {
        if (parent == null)
        {
            return Vector2.zero;
        }

        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, cam, out var local);
        return local;
    }

    private static Vector2 GetPointerScreenPosition()
    {
#if ENABLE_INPUT_SYSTEM
        var mouse = Mouse.current;
        if (mouse != null)
        {
            return mouse.position.ReadValue();
        }
#endif
        return Input.mousePosition;
    }
}
