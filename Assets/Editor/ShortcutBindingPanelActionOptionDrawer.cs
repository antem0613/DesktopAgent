#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

[CustomPropertyDrawer(typeof(ShortcutBindingPanel.ActionOption))]
public sealed class ShortcutBindingPanelActionOptionDrawer : PropertyDrawer
{
    private const double RefreshIntervalSec = 1.0d;

    private static readonly List<string> CachedMapNames = new List<string>();
    private static readonly Dictionary<string, List<string>> CachedActionNamesByMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    private static double _lastCacheUpdate;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float line = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;

        if (!property.isExpanded)
        {
            return line;
        }

        const int rowCount = 7;
        return rowCount * line + (rowCount - 1) * spacing;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        RefreshCatalogIfNeeded();

        SerializedProperty labelProp = property.FindPropertyRelative("Label");
        SerializedProperty actionMapProp = property.FindPropertyRelative("ActionMap");
        SerializedProperty actionNameProp = property.FindPropertyRelative("ActionName");
        SerializedProperty defaultKeyProp = property.FindPropertyRelative("DefaultKey");
        SerializedProperty defaultModifier1Prop = property.FindPropertyRelative("DefaultModifier1");
        SerializedProperty defaultModifier2Prop = property.FindPropertyRelative("DefaultModifier2");

        Rect rowRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

        EditorGUI.BeginProperty(position, label, property);

        string foldoutLabel = string.IsNullOrWhiteSpace(labelProp.stringValue) ? label.text : labelProp.stringValue;
        property.isExpanded = EditorGUI.Foldout(rowRect, property.isExpanded, foldoutLabel, true);
        if (!property.isExpanded)
        {
            EditorGUI.EndProperty();
            return;
        }

        rowRect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        EditorGUI.PropertyField(rowRect, labelProp);

        rowRect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        DrawActionMapPopup(rowRect, actionMapProp);

        rowRect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        DrawActionNamePopup(rowRect, actionMapProp, actionNameProp);

        rowRect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        EditorGUI.PropertyField(rowRect, defaultKeyProp);

        rowRect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        EditorGUI.PropertyField(rowRect, defaultModifier1Prop);

        rowRect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        EditorGUI.PropertyField(rowRect, defaultModifier2Prop);

        EditorGUI.EndProperty();
    }

    private static void DrawActionMapPopup(Rect rect, SerializedProperty actionMapProp)
    {
        if (CachedMapNames.Count == 0)
        {
            EditorGUI.PropertyField(rect, actionMapProp);
            return;
        }

        int currentIndex = CachedMapNames.IndexOf(actionMapProp.stringValue);
        if (currentIndex < 0)
        {
            currentIndex = 0;
            actionMapProp.stringValue = CachedMapNames[0];
        }

        int selected = EditorGUI.Popup(rect, "Action Map", currentIndex, CachedMapNames.ToArray());
        if (selected >= 0 && selected < CachedMapNames.Count)
        {
            actionMapProp.stringValue = CachedMapNames[selected];
        }
    }

    private static void DrawActionNamePopup(Rect rect, SerializedProperty actionMapProp, SerializedProperty actionNameProp)
    {
        string selectedMap = actionMapProp.stringValue;
        if (string.IsNullOrWhiteSpace(selectedMap) || !CachedActionNamesByMap.TryGetValue(selectedMap, out List<string> actionNames) || actionNames.Count == 0)
        {
            EditorGUI.PropertyField(rect, actionNameProp);
            return;
        }

        int currentIndex = actionNames.IndexOf(actionNameProp.stringValue);
        if (currentIndex < 0)
        {
            currentIndex = 0;
            actionNameProp.stringValue = actionNames[0];
        }

        int selected = EditorGUI.Popup(rect, "Action Name", currentIndex, actionNames.ToArray());
        if (selected >= 0 && selected < actionNames.Count)
        {
            actionNameProp.stringValue = actionNames[selected];
        }
    }

    private static void RefreshCatalogIfNeeded()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now - _lastCacheUpdate < RefreshIntervalSec)
        {
            return;
        }

        _lastCacheUpdate = now;

        CachedMapNames.Clear();
        CachedActionNamesByMap.Clear();

        HashSet<string> mapSet = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> actionSetByMap = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        string[] guids = AssetDatabase.FindAssets("t:InputActionAsset");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
            if (asset == null)
            {
                continue;
            }

            ReadOnlyArray<InputActionMap> maps = asset.actionMaps;
            for (int mapIndex = 0; mapIndex < maps.Count; mapIndex++)
            {
                InputActionMap map = maps[mapIndex];
                if (map == null || string.IsNullOrWhiteSpace(map.name))
                {
                    continue;
                }

                mapSet.Add(map.name);
                if (!actionSetByMap.TryGetValue(map.name, out HashSet<string> actionSet))
                {
                    actionSet = new HashSet<string>(StringComparer.Ordinal);
                    actionSetByMap[map.name] = actionSet;
                }

                ReadOnlyArray<InputAction> actions = map.actions;
                for (int actionIndex = 0; actionIndex < actions.Count; actionIndex++)
                {
                    InputAction action = actions[actionIndex];
                    if (action == null || string.IsNullOrWhiteSpace(action.name))
                    {
                        continue;
                    }

                    actionSet.Add(action.name);
                }
            }
        }

        CachedMapNames.AddRange(mapSet.OrderBy(name => name, StringComparer.Ordinal));

        foreach (KeyValuePair<string, HashSet<string>> pair in actionSetByMap)
        {
            List<string> sorted = pair.Value.OrderBy(name => name, StringComparer.Ordinal).ToList();
            CachedActionNamesByMap[pair.Key] = sorted;
        }
    }
}
#endif
