// Copyright (c) Le Loc Tai <leloctai.com> . All rights reserved. Do not redistribute.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace LeTai.Paraform.Scaffold
{
[Serializable]
public struct ParaformConfig
{
    public static readonly ParaformConfig DEFAULT = new ParaformConfig {
        cornerCurvature = 1.5f,
        filletCurvature = 2.5f,
        bevelWidth      = 64,
        ringThickness   = 0,
        elevation       = 100,
        cornerRadii     = new Vector4(64, 64, 64, 64),
    };

    public event Action changed;


#if UNITY_EDITOR
    [SerializeField]
    internal bool isCornersLinked;
#endif

    [SerializeField]
    [Tooltip("Indiviual corner radii. You can drag the rounded corner symbols to fine tune each radius. Click the link button to keep all corners the same.")]
    internal Vector4 cornerRadii;

    public Vector4 CornerRadii
    {
        get => cornerRadii;
        set => Set(ref cornerRadii, value);
    }

    [SerializeField]
    [Range(0, 6)]
    [Tooltip("0 is a flat diagonal corner. 1 is the perfect circle. Value > 1 increase curvature continuity target: 2 for G2 continuity, 3 for G3, and so on. Note that curvature continuity requires increased transition length, and is not guaranteed if the corner radius is too large compared to side length.")]
    internal float cornerCurvature;

    public float CornerCurvature
    {
        get => cornerCurvature;
        set => Set(ref cornerCurvature, value);
    }

    [SerializeField]
    [Range(0, 6)]
    [Tooltip("0 is a flat diagonal corner. 1 is the perfect circle. Value > 1 increase curvature continuity target: 2 for G2 continuity, 3 for G3, and so on")]
    internal float filletCurvature;

    public float FilletCurvature
    {
        get => filletCurvature;
        set => Set(ref filletCurvature, value);
    }

    [FormerlySerializedAs("edgeWidth")]
    [SerializeField]
    [Min(0)]
    [Tooltip("Bevel width or thickness")]
    internal float bevelWidth;

    public float BevelWidth
    {
        get => bevelWidth;
        set => Set(ref bevelWidth, value);
    }

    [SerializeField]
    [Min(0)]
    [Tooltip("Thickness of the ring shape. 0 to disable")]
    internal float ringThickness;

    public float RingThickness
    {
        get => ringThickness;
        set => Set(ref ringThickness, value);
    }

    [SerializeField]
    [Range(0, 1000)]
    [Tooltip("Distance to the below surface, affecting refraction")]
    internal float elevation;

    public float Elevation
    {
        get => elevation;
        set => Set(ref elevation, value);
    }

    void Set(ref float field, float value)
    {
        if (!Mathf.Approximately(field, value))
        {
            field = value;
            NotifyChanged();
        }
    }

    void Set<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            NotifyChanged();
        }
    }

    public void NotifyChanged()
    {
        changed?.Invoke();
    }
}
}
