using System;
using System.Collections.Generic;
using UnityEngine;

public class TestFeatureSwitchboard : MonoBehaviour
{
    private enum FeatureGroup
    {
        Common,
        Initialization,
        Behavior,
        Fall,
    }

    [Serializable]
    private struct FeatureOverride
    {
        public string featureTypeName;
        public bool enabled;
    }

    [Header("Master")]
    [SerializeField] private bool enableAllFeatures = true;

    [Header("Group Toggles")]
    [SerializeField] private bool enableCommonGroup = true;
    [SerializeField] private bool enableInitializationGroup = true;
    [SerializeField] private bool enableBehaviorGroup = true;
    [SerializeField] private bool enableFallGroup = true;

    [Header("Per Feature Overrides")]
    [SerializeField] private List<FeatureOverride> featureOverrides = new();

    private readonly Dictionary<string, bool> _overrideMap = new(StringComparer.Ordinal);
    private bool _cacheDirty = true;

    public bool IsFeatureEnabled(ITestFeatureComponent feature)
    {
        if (!enableAllFeatures)
        {
            return false;
        }

        if (feature == null)
        {
            return false;
        }

        RebuildOverrideCacheIfNeeded();

        Type type = feature.GetType();
        if (_overrideMap.TryGetValue(type.Name, out bool overrideEnabled))
        {
            return overrideEnabled;
        }

        return IsGroupEnabled(ResolveGroup(type.Name));
    }

    private void OnValidate()
    {
        _cacheDirty = true;
    }

    private void RebuildOverrideCacheIfNeeded()
    {
        if (!_cacheDirty)
        {
            return;
        }

        _overrideMap.Clear();
        if (featureOverrides != null)
        {
            for (int i = 0; i < featureOverrides.Count; i++)
            {
                string key = featureOverrides[i].featureTypeName;
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                _overrideMap[key.Trim()] = featureOverrides[i].enabled;
            }
        }

        _cacheDirty = false;
    }

    private bool IsGroupEnabled(FeatureGroup group)
    {
        switch (group)
        {
            case FeatureGroup.Initialization:
                return enableInitializationGroup;
            case FeatureGroup.Behavior:
                return enableBehaviorGroup;
            case FeatureGroup.Fall:
                return enableFallGroup;
            default:
                return enableCommonGroup;
        }
    }

    private static FeatureGroup ResolveGroup(string featureTypeName)
    {
        if (string.IsNullOrEmpty(featureTypeName))
        {
            return FeatureGroup.Common;
        }

        if (featureTypeName.Contains("Spawn") || featureTypeName.Contains("Initialization"))
        {
            return FeatureGroup.Initialization;
        }

        if (featureTypeName.Contains("Action")
            || featureTypeName.Contains("Walk")
            || featureTypeName.Contains("Drag")
            || featureTypeName.Contains("Scroll")
            || featureTypeName.Contains("Opacity"))
        {
            return FeatureGroup.Behavior;
        }

        if (featureTypeName.Contains("Fall")
            || featureTypeName.Contains("GroundHeight")
            || featureTypeName.Contains("Respawn"))
        {
            return FeatureGroup.Fall;
        }

        return FeatureGroup.Common;
    }
}