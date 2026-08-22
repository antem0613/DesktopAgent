// Copyright (c) Le Loc Tai <leloctai.com> . All rights reserved. Do not redistribute.

using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace LeTai.Asset.TranslucentImage.Demo
{
[ExecuteAlways]
[AddComponentMenu("UI/Translucent Image Demo/Paraform Material Selector")]
public class ParaformMaterialSelector : MonoBehaviour
{
    [SerializeField] Material _defaultMaterial;
    [SerializeField] Material _paraformMaterial;
    [SerializeField] bool     _forceDefault;

    Graphic _target;

    public bool UsingParaform =>
#if LETAI_PARAFORM
        !_forceDefault && _paraformMaterial;
#else
        false;
#endif

    public void Apply()
    {
        var target = _target ? _target : _target = GetComponent<TranslucentImage>();
        if (target == null)
            return;

        var material = UsingParaform ? _paraformMaterial : _defaultMaterial;
        if (material != null && target.material != material)
            target.material = material;
    }

    void OnEnable()
    {
        Apply();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (enabled)
            Apply();
    }
#endif
}
}
