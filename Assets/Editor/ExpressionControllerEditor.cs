using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ExpressionController))]
public sealed class ExpressionControllerEditor : Editor
{
    private bool showSliders = true;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("characterManager"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("autoApply"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("clearOthersBeforeApply"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("useInspectorWeights"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Auto Blink", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("autoBlinkEnabled"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("autoBlinkOverridesManual"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("useUnscaledTime"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("minIntervalSeconds"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("maxIntervalSeconds"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("closeDurationSeconds"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("holdClosedSeconds"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("openDurationSeconds"));

        EditorGUILayout.Space();
        showSliders = EditorGUILayout.BeginFoldoutHeaderGroup(showSliders, "Expression Sliders");
        if (showSliders)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("happy"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("angry"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sad"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("relaxed"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("surprised"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("neutral"));

            EditorGUILayout.PropertyField(serializedObject.FindProperty("aa"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ih"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ou"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ee"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("oh"));

            EditorGUILayout.PropertyField(serializedObject.FindProperty("blink"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("blinkLeft"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("blinkRight"));

            EditorGUILayout.PropertyField(serializedObject.FindProperty("lookUp"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lookDown"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lookLeft"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lookRight"));
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        serializedObject.ApplyModifiedProperties();
    }
}
