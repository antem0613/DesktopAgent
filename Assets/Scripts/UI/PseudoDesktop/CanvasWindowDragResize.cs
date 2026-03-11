using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Canvas上の疑似ウィンドウ(UI Panel)を、GameViewでドラッグ移動/端辺ドラッグでリサイズするためのコンポーネント。
/// - アタッチ先は「ウィンドウのRectTransform」想定
/// - ドラッグ可能範囲(タイトルバー等)をRectTransformで指定可能
/// </summary>
[DisallowMultipleComponent]
public sealed class CanvasWindowDragResize : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler
{
    private enum Mode
    {
        None,
        Move,
        ResizeLeft,
        ResizeRight,
        ResizeTop,
        ResizeBottom,
        ResizeTopLeft,
        ResizeTopRight,
        ResizeBottomLeft,
        ResizeBottomRight,
    }

    [Header("Target")]
    [SerializeField] private RectTransform windowRect;

    [Tooltip("ドラッグ開始を許可する範囲（タイトルバー等）。未指定ならウィンドウ全体")]
    [SerializeField] private RectTransform dragRegion;

    [Header("Behavior")]
    [SerializeField] private bool enableMove = true;
    [SerializeField] private bool enableResize = true;

    [Tooltip("端辺リサイズの判定幅(px)")]
    [SerializeField] private float resizeBorderPixels = 10f;

    [Tooltip("最小サイズ(px)")]
    [SerializeField] private Vector2 minSize = new Vector2(120f, 80f);

    private Mode _mode;
    private Vector2 _prevLocalInParent;
    private RectTransform _parent;

    private void Reset()
    {
        windowRect = transform as RectTransform;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        if (windowRect == null)
        {
            windowRect = transform as RectTransform;
        }

        if (windowRect == null)
        {
            return;
        }

        _parent = windowRect.parent as RectTransform;
        if (_parent == null)
        {
            return;
        }

        var cam = eventData.pressEventCamera;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, eventData.position, cam, out _prevLocalInParent))
        {
            return;
        }

        _mode = DecideMode(eventData, cam);
        if (_mode == Mode.None)
        {
            return;
        }

        eventData.Use();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        // OnPointerDownでモードを決めるだけ
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        if (_mode == Mode.None || windowRect == null)
        {
            return;
        }

        var cam = eventData.pressEventCamera;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, eventData.position, cam, out var localInParent))
        {
            return;
        }

        var delta = localInParent - _prevLocalInParent;
        _prevLocalInParent = localInParent;

        if (_mode == Mode.Move)
        {
            if (!enableMove)
            {
                return;
            }

            // Screen Space - Camera のCanvasでも破綻しにくいよう anchoredPosition で移動
            windowRect.anchoredPosition += delta;
            eventData.Use();
            return;
        }

        if (!enableResize)
        {
            return;
        }

        ApplyResize(delta);
        eventData.Use();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _mode = Mode.None;
    }

    private Mode DecideMode(PointerEventData eventData, Camera cam)
    {
        // 端辺判定
        if (enableResize && RectTransformUtility.ScreenPointToLocalPointInRectangle(windowRect, eventData.position, cam, out var localInWindow))
        {
            var r = windowRect.rect;
            float border = Mathf.Max(0f, resizeBorderPixels);

            bool left = Mathf.Abs(localInWindow.x - r.xMin) <= border;
            bool right = Mathf.Abs(localInWindow.x - r.xMax) <= border;
            bool bottom = Mathf.Abs(localInWindow.y - r.yMin) <= border;
            bool top = Mathf.Abs(localInWindow.y - r.yMax) <= border;

            if (left && top) return Mode.ResizeTopLeft;
            if (right && top) return Mode.ResizeTopRight;
            if (left && bottom) return Mode.ResizeBottomLeft;
            if (right && bottom) return Mode.ResizeBottomRight;

            if (left) return Mode.ResizeLeft;
            if (right) return Mode.ResizeRight;
            if (top) return Mode.ResizeTop;
            if (bottom) return Mode.ResizeBottom;
        }

        // ドラッグ範囲判定
        if (!enableMove)
        {
            return Mode.None;
        }

        var region = dragRegion != null ? dragRegion : windowRect;
        if (RectTransformUtility.RectangleContainsScreenPoint(region, eventData.position, cam))
        {
            return Mode.Move;
        }

        return Mode.None;
    }

    private void ApplyResize(Vector2 delta)
    {
        // anchoredPosition + size を操作（Layout/アンカー構成でも効きやすい）
        var pivot = windowRect.pivot;
        var pos = windowRect.anchoredPosition;
        var rect = windowRect.rect;

        float minW = Mathf.Max(1f, minSize.x);
        float minH = Mathf.Max(1f, minSize.y);

        float oldW = rect.width;
        float oldH = rect.height;

        bool resizeLeft = _mode is Mode.ResizeLeft or Mode.ResizeTopLeft or Mode.ResizeBottomLeft;
        bool resizeRight = _mode is Mode.ResizeRight or Mode.ResizeTopRight or Mode.ResizeBottomRight;
        bool resizeBottom = _mode is Mode.ResizeBottom or Mode.ResizeBottomLeft or Mode.ResizeBottomRight;
        bool resizeTop = _mode is Mode.ResizeTop or Mode.ResizeTopLeft or Mode.ResizeTopRight;

        float dwWanted = 0f;
        if (resizeLeft && !resizeRight) dwWanted = -delta.x;
        if (resizeRight && !resizeLeft) dwWanted = delta.x;

        float dhWanted = 0f;
        if (resizeBottom && !resizeTop) dhWanted = -delta.y;
        if (resizeTop && !resizeBottom) dhWanted = delta.y;

        // 角ドラッグは両方更新
        if (resizeLeft && resizeRight) dwWanted = 0f;
        if (resizeBottom && resizeTop) dhWanted = 0f;

        float newW = Mathf.Max(minW, oldW + dwWanted);
        float newH = Mathf.Max(minH, oldH + dhWanted);

        float actualDw = newW - oldW;
        float actualDh = newH - oldH;

        // 反対側の辺が固定されるように pivot で位置補正
        if (resizeRight && !resizeLeft)
        {
            // 右辺ドラッグ: 左辺固定
            pos.x += pivot.x * actualDw;
        } else if (resizeLeft && !resizeRight)
        {
            // 左辺ドラッグ: 右辺固定
            pos.x -= (1f - pivot.x) * actualDw;
        }

        if (resizeTop && !resizeBottom)
        {
            // 上辺ドラッグ: 下辺固定
            pos.y += pivot.y * actualDh;
        } else if (resizeBottom && !resizeTop)
        {
            // 下辺ドラッグ: 上辺固定
            pos.y -= (1f - pivot.y) * actualDh;
        }

        windowRect.anchoredPosition = pos;
        windowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, newW);
        windowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, newH);
    }
}
