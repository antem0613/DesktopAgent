using UnityEngine;

/// <summary>
/// タスクバーの疑似UI用コンポーネント。
/// UI Panel(RectTransform)にアタッチして、スクリーン座標上の矩形を取得します。
/// </summary>
[DisallowMultipleComponent]
public sealed class PseudoTaskbar : MonoBehaviour
{
    [SerializeField] private bool isEnabled = true;

    public bool IsEnabled => isEnabled;

    public bool TryGetScreenRect(out Rect screenRect)
    {
        screenRect = default;

        if (!isEnabled)
        {
            return false;
        }

        var rectTransform = transform as RectTransform;
        if (rectTransform == null)
        {
            return false;
        }

        var canvas = rectTransform.GetComponentInParent<Canvas>();
        Camera cam = null;
        if (canvas != null)
        {
            cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }

        var corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);

        // corners: 0=BL, 1=TL, 2=TR, 3=BR
        Vector2 p0 = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 p2 = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);

        float xMin = Mathf.Min(p0.x, p2.x);
        float xMax = Mathf.Max(p0.x, p2.x);
        float yMin = Mathf.Min(p0.y, p2.y);
        float yMax = Mathf.Max(p0.y, p2.y);

        float w = Mathf.Max(0f, xMax - xMin);
        float h = Mathf.Max(0f, yMax - yMin);
        if (w <= 0.5f || h <= 0.5f)
        {
            return false;
        }

        screenRect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return true;
    }
}
