using UnityEngine;

namespace LeTai.Asset.TranslucentImage
{
[ExecuteAlways]
[RequireComponent(typeof(TranslucentImage))]
[AddComponentMenu("UI/Translucent Image Procedural Texture")]
public class TranslucentImageProceduralTexture : MonoBehaviour
{
    [Tooltip("Material to generate the texture")]
    [SerializeField] Material _material;
    [Tooltip("Output texture size as a multiple of the Image's pixel size")]
    [SerializeField] [Range(0.1f, 2f)] float _resolutionScale = 1f;
    [Tooltip("Re-render every frame. Enable for animated, time-based shaders")]
    [SerializeField] bool _renderEveryFrame;

    static readonly int ID_OUTPUT_SIZE = Shader.PropertyToID("_OutputSize");

    RenderTexture    _texture;
    TranslucentImage _image;
    Vector2Int       _size = new Vector2Int(-1, -1);

    public RenderTexture OutputTexture => _texture;

    public bool RenderEveryFrame
    {
        get => _renderEveryFrame;
        set => _renderEveryFrame = value;
    }

    protected Material Material => _material;

    protected TranslucentImage Image => _image ? _image : _image = GetComponent<TranslucentImage>();

    protected virtual void OnEnable()
    {
        Image.RegisterDirtyVerticesCallback(EnsureTexture);
        Image.imageMode = TranslucentImage.ImageMode.Texture;
        EnsureTexture();
    }

    protected virtual void OnDisable()
    {
        if (_image)
            _image.UnregisterDirtyVerticesCallback(EnsureTexture);
        ReleaseTexture();
    }

    protected virtual void OnDestroy()
    {
        ReleaseTexture();
    }

    protected virtual void Update()
    {
        EnsureTexture();
        if (_renderEveryFrame)
            RenderToOutput();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        Regenerate();
    }
#endif

    public void Regenerate()
    {
        EnsureTexture();
        RenderToOutput();
    }

    protected void EnsureTexture()
    {
        var rect = Image.rectTransform.rect;
        var newSize = new Vector2Int(Mathf.RoundToInt(rect.width * _resolutionScale),
                                     Mathf.RoundToInt(rect.height * _resolutionScale));
        if (newSize.x < 1 || newSize.y < 1)
            return;
        if (_texture && _texture.IsCreated() && newSize == _size)
            return;

        ReleaseTexture();

        _size    = newSize;
        _texture = CreateTexture(newSize);

        Image.rawTexture = _texture;
        RenderToOutput();
    }

    protected virtual RenderTexture CreateTexture(Vector2Int size)
    {
        var tex = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32, 0) {
            antiAliasing = 1,
            filterMode   = FilterMode.Bilinear,
            wrapMode     = TextureWrapMode.Clamp
        };
        tex.Create();

        return tex;
    }

    protected void RenderToOutput()
    {
        if (_texture)
            Render(_texture);
    }

    protected virtual void Render(RenderTexture target)
    {
        if (_material)
        {
            _material.SetVector(ID_OUTPUT_SIZE, new Vector4(target.width,
                                                            target.height,
                                                            1f / target.width,
                                                            1f / target.height));
            Graphics.Blit(null, target, _material, 0);
        }
        else
        {
            var prev = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = prev;
        }
    }

    void ReleaseTexture()
    {
        if (_texture)
        {
            if (RenderTexture.active == _texture)
                RenderTexture.active = null;
            _texture.Release();
            if (Application.isPlaying)
                Destroy(_texture);
            else
                DestroyImmediate(_texture);
            _texture = null;
        }
        _size = new Vector2Int(-1, -1);
    }
}
}
