using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using LeTai.Common;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace LeTai.Asset.TranslucentImage
{
/// <summary>
/// Dynamic blur-behind UI element
/// </summary>
[HelpURL("https://leloctai.com/asset/translucentimage/docs/articles/customize.html#translucent-image")]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public partial class TranslucentImage : Image, IActiveRegionProvider
{
    /// <summary>
    /// Source of the blurred background for this image
    /// </summary>
    public TranslucentImageSource source
    {
        get => _source;
        set
        {
            _source = value;

            // We need a separated variable, as the backing field is set before the setter is called from EditorProperty
            if (_source == _sourcePrev)
                return;

            DisconnectSource(_sourcePrev);
            ConnectSource(_source);
            _sourcePrev = source;
        }
    }

    [Obsolete("Use foregroundOpacity instead")]
    public float spriteBlending
    {
        get => foregroundOpacity;
        set => foregroundOpacity = value;
    }

    /// <summary>
    /// How the Sprite/Color alpha is interpreted: as transparency, or as Foreground Opacity
    /// </summary>
    public TextureAlphaMode textureAlphaMode
    {
        get => _textureAlphaMode;
        set
        {
            _textureAlphaMode = value;
            SetVerticesDirty();
        }
    }

    /// <summary>
    /// How much Sprite and Color contribute to the Image. Use this instead of Color.alpha
    /// </summary>
    public float foregroundOpacity
    {
        get => _foregroundOpacity;
        set
        {
            _foregroundOpacity = value;
            SetVerticesDirty();
        }
    }

    /// <summary>
    /// (De)Saturate the image, 1 is normal, 0 is grey scale, below zero make the image negative
    /// </summary>
    public float vibrancy
    {
        get => _vibrancy;
        set
        {
            _vibrancy = value;
            SetVerticesDirty();
        }
    }

    /// <summary>
    /// In Normal Background Mode: Brighten/darken the background. In Colorful Background Mode: Set the background overall brightness.
    /// </summary>
    public float brightness
    {
        get => _brightness;
        set
        {
            _brightness = value;
            SetVerticesDirty();
        }
    }

    /// <summary>
    /// Flatten the color behind to maintain color contrast on varying backgrounds
    /// </summary>
    public float flatten
    {
        get => _flatten;
        set
        {
            _flatten = value;
            SetVerticesDirty();
        }
    }

    public override Material material
    {
        get => base.material;
        set
        {
            base.material = value;
            OnDirtyMaterial();
        }
    }

    public override Material defaultMaterial
    {
        get
        {
#if LETAI_PARAFORM
            return DefaultResources.Instance.paraformMaterial;
#else
            return DefaultResources.Instance.material;
#endif
        }
    }

    [FormerlySerializedAs("source")]
    [Tooltip("Source of the blurred background for this image")]
    [SerializeField] TranslucentImageSource _source;

    [Tooltip("How the Sprite alpha is interpreted")]
    [SerializeField] TextureAlphaMode _textureAlphaMode = TextureAlphaMode.Alpha;
    [FormerlySerializedAs("spriteBlending")]
    [FormerlySerializedAs("m_spriteBlending")]
    [Tooltip("How much Sprite and Color contribute to the Image. Use this instead of Color.alpha")]
    [SerializeField] [Range(0, 1)] float _foregroundOpacity = .5f;
    [FormerlySerializedAs("vibrancy")]
    [Tooltip("(De)Saturate the image, 1 is normal, 0 is grey scale, below zero make the image negative")]
    [SerializeField] [Range(-1, 2)] float _vibrancy = 1;
    [FormerlySerializedAs("brightness")]
    [Tooltip("In Normal Background Mode: Brighten/darken the background. In Colorful Background Mode: Set the background overall brightness.")]
    [SerializeField] [Range(-1, 1)] float _brightness = 0;
    [FormerlySerializedAs("flatten")]
    [Tooltip("Flatten the color behind to maintain color contrast on varying backgrounds")]
    [SerializeField] [Range(0, 1)] float _flatten = 0;

    bool                   shouldRun;
    bool                   isBirp;
    TranslucentImageSource _sourcePrev;

    protected override void Start()
    {
        isBirp = !GraphicsSettings.currentRenderPipeline;

        AutoAcquireSource();

        if (material && source)
        {
            material.SetTexture(ShaderID.BLUR_TEX, source.BlurredScreen);
        }

        if (canvas)
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1
                                             | AdditionalCanvasShaderChannels.TexCoord2
                                             | AdditionalCanvasShaderChannels.TexCoord3;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        SetVerticesDirty();

        ConnectSource(source);
        _sourcePrev = source;

        paraformConfig.changed    += ParaformConfigChanged;
        Canvas.willRenderCanvases += OnWillRenderCanvases;
        m_OnDirtyMaterialCallback += OnDirtyMaterial;

#if UNITY_EDITOR
        if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Start();
        }

        UnityEditor.Undo.undoRedoPerformed += OnUndoRedoPerformed;
#endif
    }

    void ParaformConfigChanged()
    {
        SetVerticesDirty();
    }

    protected override void OnDisable()
    {
        SetVerticesDirty();
        base.OnDisable();

        paraformConfig.changed    -= ParaformConfigChanged;
        Canvas.willRenderCanvases -= OnWillRenderCanvases;
        m_OnDirtyMaterialCallback -= OnDirtyMaterial;

        DisconnectSource(source);
#if UNITY_EDITOR
        UnityEditor.Undo.undoRedoPerformed -= OnUndoRedoPerformed;
#endif
    }

    void OnWillRenderCanvases()
    {
        SetParaformShaderGlobal();
    }

//     void Update()
//     {
// #if DEBUG
//         if (Application.isPlaying && !IsInPrefabMode())
//         {
//             if (!source)
//                 Debug.LogWarning("TranslucentImageSource is missing. " +
//                                  "Please add the TranslucentImageSource component to your main camera, " +
//                                  "then assign it to the Source field of the Translucent Image(s)");
//         }
// #endif
//     }

    public bool HaveActiveRegion()
    {
        return (bool)this && IsActive() && canvas && canvas.enabled;
    }

    public void GetActiveRegion(VPMatrixCache vpMatrixCache, out ActiveRegion activeRegion)
    {
        VPMatrixCache.Index vpMatrixIndex;
        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            vpMatrixIndex = VPMatrixCache.Index.INVALID;
        }
        else
        {
            var refCamera = canvas.worldCamera;
            if (!refCamera)
            {
                Debug.LogError("Translucent Image need an Event Camera for World Space Canvas");
                vpMatrixIndex = VPMatrixCache.Index.INVALID;
            }
            else
            {
                vpMatrixIndex = vpMatrixCache.IndexOf(refCamera);
                if (!vpMatrixIndex.IsValid())
                    vpMatrixIndex = vpMatrixCache.Add(refCamera);
            }
        }

        var rect = rectTransform.rect;
        PadRectForRefraction(ref rect);
        rect = RectUtils.Expand(rect, _meshPadding);

        activeRegion = new ActiveRegion(rect,
                                        rectTransform.localToWorldMatrix,
                                        vpMatrixIndex);
    }

    /// <summary>
    /// Copy material keywords state and properties, except stencil properties
    /// </summary>
    public static void CopyMaterialPropertiesTo(Material src, Material dst)
    {
        MaterialUtils.CopyKeyword(src, dst, ShaderID.KW_BACKGROUND_MODE_COLORFUL);
        MaterialUtils.CopyKeyword(src, dst, ShaderID.KW_BACKGROUND_MODE_NORMAL);
        MaterialUtils.CopyKeyword(src, dst, ShaderID.KW_BACKGROUND_MODE_OPAQUE);

        CopyParaformMaterialPropertiesTo(src, dst);
    }

    void ConnectSource(TranslucentImageSource source)
    {
        if (!source) return;

        source.RegisterActiveRegionProvider(this);
        source.blurredScreenChanged += SetBlurTex;
        source.blurRegionChanged    += SetBlurRegion;
        SetBlurTex();
        SetBlurRegion();
    }

    void DisconnectSource(TranslucentImageSource source)
    {
        if (!source) return;

        source.UnRegisterActiveRegionProvider(this);
        source.blurredScreenChanged -= SetBlurTex;
        source.blurRegionChanged    -= SetBlurRegion;
    }

    void SetBlurTex()
    {
        if (!source)
            return;

        materialForRendering.SetTexture(ShaderID.BLUR_TEX, source.BlurredScreen);
    }

    void SetBlurRegion()
    {
        if (
            !source
         || !canvas
         || !canvas.enabled
        )
            return;

        if (isBirp || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            var minMaxVector = RectUtils.ToMinMaxVector(source.BlurRegionNormalizedScreenSpace);
            materialForRendering.SetVector(ShaderID.CROP_REGION, minMaxVector);
        }
        else
        {
            materialForRendering.SetVector(ShaderID.CROP_REGION, RectUtils.ToMinMaxVector(source.BlurRegion));
        }
    }

    void OnDirtyMaterial()
    {
        SetBlurTex();
        SetBlurRegion();
    }

    bool IsInPrefabMode()
    {
#if !UNITY_EDITOR
        return false;
#else
        var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
        return stage != null;
#endif
    }

    bool sourceAcquiredOnStart = false;

    void AutoAcquireSource()
    {
        if (IsInPrefabMode()) return;
        if (sourceAcquiredOnStart) return;

        source                = source ? source : Shims.FindAnyObjectByType<TranslucentImageSource>();
        sourceAcquiredOnStart = true;
    }

    void OnUndoRedoPerformed()
    {
        source = _source;
        OnDirtyMaterial();
        UpdateTrueShadowHash();
    }

    Vector4 PackVertexData()
    {
        return Vec4Builder
              .Append(Packing.FloatPacker.Uniform(8, sign: _textureAlphaMode == TextureAlphaMode.ForegroundOpacity)
                             .Enqueue(foregroundOpacity, 1)
                             .Enqueue(flatten,           1))
              .Append(Packing.FloatPacker.Uniform(10)
                             .Enqueue(vibrancy,   -1, 2)
                             .Enqueue(brightness, -1, 1))
              .Append(0)
              .Append(0);
    }

    static readonly VertexHelper    VERTEX_HELPER  = new();
    static readonly List<Component> MESH_MODIFIERS = new();

    Vector4 _meshPadding;

    protected override void UpdateGeometry()
    {
        var rect = rectTransform.rect;

        if (rectTransform != null && rect.width >= 0 && rect.height >= 0)
            OnPopulateMesh(VERTEX_HELPER);
        else
            VERTEX_HELPER.Clear();

        GetComponents(typeof(IMeshModifier), MESH_MODIFIERS);
        var modCount = MESH_MODIFIERS.Count;
        for (var i = 0; i < modCount; i++)
        {
            var modifier = (IMeshModifier)MESH_MODIFIERS[i];

            // shadow expands vh into non-shared vertices
            var streamLength = VERTEX_HELPER.currentIndexCount;

            modifier.ModifyMesh(VERTEX_HELPER);

            if (modifier is Shadow) // also work for Outline
            {
                var shadowVertCount = VERTEX_HELPER.currentIndexCount - streamLength;
                MarkShadowVertices(VERTEX_HELPER, 0, shadowVertCount);
            }
        }
        MESH_MODIFIERS.Clear();

        VERTEX_HELPER.FillMesh(workerMesh);
        canvasRenderer.SetMesh(workerMesh);

        if (modCount > 0)
        {
            var meshBounds = workerMesh.bounds;
            var min        = meshBounds.min;
            var max        = meshBounds.max;

            // avoid shrinking defensively. may not hurt
            _meshPadding = new Vector4(Mathf.Max(0, rect.xMin - min.x),
                                       Mathf.Max(0, rect.yMin - min.y),
                                       Mathf.Max(0, max.x - rect.xMax),
                                       Mathf.Max(0, max.y - rect.yMax));
        }
        else
        {
            _meshPadding = Vector4.zero;
        }
    }

    static void MarkShadowVertices(VertexHelper vh, int start, int count)
    {
        UIVertex vert = default;

        var end = start + count;
        for (var i = start; i < end; i++)
        {
            vh.PopulateUIVertex(ref vert, i);

            var uv1 = vert.uv1;
            uv1.y    = -Mathf.Abs(uv1.y);
            vert.uv1 = uv1;

            vh.SetUIVertex(vert, i);
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        PopulateBaseGeometry(vh);

        var uv1 = PackVertexData();

#if LETAI_PARAFORM
        var paraformEncoder = new Paraform.ParaformVertexDataEncoder(rectTransform, paraformConfig);
        var (uv2, uvCommonPart) = paraformEncoder.PackVertexDataCommon();
#endif

        UIVertex vert = default;
        for (var i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref vert, i);

            vert.uv1 = uv1;

#if LETAI_PARAFORM
            vert.uv2 = uv2;
            vert.uv3 = paraformEncoder.PackVertexDataSpecific(uvCommonPart, vert.position);
#endif

            vh.SetUIVertex(vert, i);
        }


        UpdateTrueShadowHash();
    }

    void PopulateBaseGeometry(VertexHelper vh)
    {
        if (_imageMode == ImageMode.Sprite)
            base.OnPopulateMesh(vh);
        else
            PopulateRawTextureMesh(vh);
    }

    public enum TextureAlphaMode
    {
        Alpha,
        ForegroundOpacity,
    }
}
}
