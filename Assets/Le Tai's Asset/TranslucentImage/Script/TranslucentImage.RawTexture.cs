// Copyright (c) Le Loc Tai <leloctai.com> . All rights reserved. Do not redistribute.

using UnityEngine;
using UnityEngine.UI;

namespace LeTai.Asset.TranslucentImage
{
public partial class TranslucentImage
{
    public enum ImageMode
    {
        Sprite,
        Texture
    }

    [Tooltip("Whether to use a Sprite or a raw Texture as the foreground")]
    [SerializeField] ImageMode _imageMode = ImageMode.Sprite;
    [Tooltip("Texture to use when Image Mode is Texture")]
    [SerializeField] Texture _rawTexture;
    [Tooltip("Crop the raw texture")]
    [SerializeField] Rect _uvRect = new Rect(0f, 0f, 1f, 1f);

    public ImageMode imageMode
    {
        get => _imageMode;
        set
        {
            if (_imageMode == value)
                return;
            _imageMode = value;
            SetVerticesDirty();
            SetMaterialDirty();
        }
    }

    public Texture rawTexture
    {
        get => _rawTexture;
        set
        {
            if (_rawTexture == value)
                return;
            _rawTexture = value;
            SetVerticesDirty();
            SetMaterialDirty();
        }
    }

    public Rect uvRect
    {
        get => _uvRect;
        set
        {
            if (_uvRect == value)
                return;
            _uvRect = value;
            SetVerticesDirty();
        }
    }

    public override Texture mainTexture
    {
        get
        {
            if (_imageMode == ImageMode.Texture)
                return _rawTexture ? _rawTexture : s_WhiteTexture;
            return base.mainTexture;
        }
    }

    void PopulateRawTextureMesh(VertexHelper vh)
    {
        vh.Clear();

        Texture tex = mainTexture;
        if (tex == null)
            return;

        Rect r = GetPixelAdjustedRect();
        var  v = new Vector4(r.x, r.y, r.x + r.width, r.y + r.height);

        Vector2 texelSize = tex.texelSize;
        float   scaleX    = tex.width * texelSize.x;
        float   scaleY    = tex.height * texelSize.y;

        Color32 c = color;
        vh.AddVert(new Vector3(v.x, v.y), c, new Vector4(_uvRect.xMin * scaleX, _uvRect.yMin * scaleY));
        vh.AddVert(new Vector3(v.x, v.w), c, new Vector4(_uvRect.xMin * scaleX, _uvRect.yMax * scaleY));
        vh.AddVert(new Vector3(v.z, v.w), c, new Vector4(_uvRect.xMax * scaleX, _uvRect.yMax * scaleY));
        vh.AddVert(new Vector3(v.z, v.y), c, new Vector4(_uvRect.xMax * scaleX, _uvRect.yMin * scaleY));

        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(2, 3, 0);
    }

    public override void SetNativeSize()
    {
        if (_imageMode == ImageMode.Sprite)
        {
            base.SetNativeSize();
            return;
        }

        Texture tex = mainTexture;
        if (tex == null)
            return;

        int w = Mathf.RoundToInt(tex.width * _uvRect.width);
        int h = Mathf.RoundToInt(tex.height * _uvRect.height);
        rectTransform.anchorMax = rectTransform.anchorMin;
        rectTransform.sizeDelta = new Vector2(w, h);
    }
}
}
