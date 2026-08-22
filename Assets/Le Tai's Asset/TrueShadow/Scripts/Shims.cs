// Copyright (c) Le Loc Tai <leloctai.com> . All rights reserved. Do not redistribute.

using System.Runtime.CompilerServices;
using UnityEngine;

namespace LeTai.TrueShadow
{
public static class Shims
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T FindAnyObjectByType<T>(bool includeInactive = false) where T : Object
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindAnyObjectByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
#elif UNITY_2020_1_OR_NEWER
        return Object.FindObjectOfType<T>(includeInactive);
#else
        return Object.FindObjectOfType<T>();
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T[] FindObjectsOfType<T>(bool includeInactive = false) where T : Object
    {
#if UNITY_6000_4_OR_NEWER
        return Object.FindObjectsByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
#elif UNITY_2023_1_OR_NEWER
        return Object.FindObjectsByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                                           FindObjectsSortMode.None);
#elif UNITY_2020_1_OR_NEWER
        return Object.FindObjectsOfType<T>(includeInactive);
#else
        return Object.FindObjectsOfType<T>();
#endif
    }
}
}
