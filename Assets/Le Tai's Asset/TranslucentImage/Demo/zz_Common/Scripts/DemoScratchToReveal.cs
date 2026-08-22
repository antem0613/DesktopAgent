using UnityEngine;
using UnityEngine.EventSystems;

namespace LeTai.Asset.TranslucentImage.Demo
{
[ExecuteAlways]
public class DemoScratchToReveal : TranslucentImageProceduralTexture,
                                   IPointerDownHandler,
                                   IBeginDragHandler,
                                   IDragHandler
{
    [SerializeField] Material _brushMaterial;
    [SerializeField] Texture  _resetTexture;
    [Range(0.01f, 0.3f)]
    [SerializeField] float _brushRadius = 0.08f;

    Vector2 _lastUV;

    public void ResetTexture()
    {
        RenderToOutput();
    }

    protected override void Render(RenderTexture target)
    {
        if (_resetTexture)
        {
            Graphics.Blit(_resetTexture, target);
            return;
        }

        var prev = RenderTexture.active;
        RenderTexture.active = target;
        GL.Clear(false, true, Color.white);
        RenderTexture.active = prev;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!Application.isPlaying)
            return;

        if (TryGetPointerUV(eventData, out var uv))
        {
            Stamp(uv);
            _lastUV = uv;
        }
    }

    public void OnBeginDrag(PointerEventData eventData) { } // prevents dragging

    public void OnDrag(PointerEventData eventData)
    {
        if (!Application.isPlaying)
            return;

        if (!TryGetPointerUV(eventData, out var uv))
            return;

        // Stamp along the segment so fast drags stay continuous
        float spacing = Mathf.Max(0.001f, _brushRadius * 0.1f);
        int   steps   = Mathf.Max(1,      Mathf.CeilToInt(Vector2.Distance(_lastUV, uv) / spacing));
        for (int i = 1; i <= steps; i++)
            Stamp(Vector2.Lerp(_lastUV, uv, i / (float)steps));

        _lastUV = uv;
    }

    bool TryGetPointerUV(PointerEventData eventData, out Vector2 uv)
    {
        uv = default;

        var rectTransform = Image.rectTransform;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform, eventData.position, eventData.pressEventCamera, out var local))
            return false;

        var rect = rectTransform.rect;
        uv = new Vector2((local.x - rect.xMin) / rect.width,
                         (local.y - rect.yMin) / rect.height);
        return true;
    }

    void Stamp(Vector2 uv)
    {
        var rt = OutputTexture;
        if (rt == null || _brushMaterial == null)
            return;

        float w        = rt.width;
        float h        = rt.height;
        float radiusPx = _brushRadius * Mathf.Min(w, h);
        float cx       = uv.x * w;
        float cy       = uv.y * h;

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.PushMatrix();
        GL.LoadPixelMatrix(0, w, 0, h);
        _brushMaterial.SetPass(0);
        GL.Begin(GL.QUADS);
        GL.TexCoord2(0, 0);
        GL.Vertex3(cx - radiusPx, cy - radiusPx, 0);
        GL.TexCoord2(1, 0);
        GL.Vertex3(cx + radiusPx, cy - radiusPx, 0);
        GL.TexCoord2(1, 1);
        GL.Vertex3(cx + radiusPx, cy + radiusPx, 0);
        GL.TexCoord2(0, 1);
        GL.Vertex3(cx - radiusPx, cy + radiusPx, 0);
        GL.End();
        GL.PopMatrix();
        RenderTexture.active = prev;
    }
}
}
