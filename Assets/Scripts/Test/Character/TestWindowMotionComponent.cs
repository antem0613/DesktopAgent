using UnityEngine;

public class TestWindowMotionComponent : MonoBehaviour, ITestFeatureComponent
{
    private const string LogTag = nameof(TestWindowMotionComponent);

    [SerializeField] private bool runTest = true;
    [SerializeField] private bool applySmallWindowOnStart = true;
    [SerializeField] private int startWindowWidth = 800;
    [SerializeField] private int startWindowHeight = 600;
    [SerializeField] private bool applyDpiScaleToMouseDelta = true;
    [SerializeField] private float mouseDeltaScaleMultiplier = 1f;
    [SerializeField] private bool useTransformMotionInEditor = true;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform editorMotionTarget;
    [SerializeField] private TestCharacterSpawnComponent characterSpawnComponent;

    private bool _editorModeLogged;

    public bool IsTestEnabled => runTest;

    public void OnTestStart()
    {
        if (Application.isEditor)
        {
            TryResolveEditorMotionReferences();
            if (!_editorModeLogged)
            {
                _editorModeLogged = true;
                TestLog.Info(LogTag, "Editor mode: skipping window size apply and using transform motion.");
            }
            return;
        }

        if (ShouldUseEditorTransformMotion())
        {
            TryResolveEditorMotionReferences();
            if (!_editorModeLogged)
            {
                _editorModeLogged = true;
                TestLog.Info(LogTag, "Editor mode: using transform motion instead of native window motion.");
            }
            return;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (applySmallWindowOnStart)
        {
            int width = Mathf.Clamp(startWindowWidth, 320, 1920);
            int height = Mathf.Clamp(startWindowHeight, 240, 1440);
            int x = Mathf.Max(0, (Screen.currentResolution.width - width) / 2);
            int y = Mathf.Max(0, (Screen.currentResolution.height - height) / 2);
            bool ok = WindowsAPI.SetCurrentWindowRect(x, y, width, height);
            TestLog.Info(LogTag, ok
                ? $"Window size applied. width={width}, height={height}, x={x}, y={y}"
                : "Window size apply failed.");
        }
#else
        TestLog.Warning(LogTag, "Window motion is only supported on Windows runtime.");
#endif
    }

    public void OnTestTick(float deltaTime)
    {
    }

    public void OnTestStop()
    {
    }

    public bool MoveWindowByPixels(float deltaX, float deltaY)
    {
        if (ShouldUseEditorTransformMotion())
        {
            return MoveEditorTransformByScreenDelta(deltaX, deltaY);
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        int dx = Mathf.RoundToInt(deltaX);
        int dy = Mathf.RoundToInt(-deltaY);
        if (dx == 0 && dy == 0)
        {
            return true;
        }

        return WindowsAPI.MoveCurrentWindowBy(dx, dy);
#else
        return false;
#endif
    }

    public bool MoveWindowByMouseDelta(float mouseDeltaX, float mouseDeltaY)
    {
        if (ShouldUseEditorTransformMotion())
        {
            return MoveWindowByPixels(mouseDeltaX, mouseDeltaY);
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        float scale = Mathf.Max(0.01f, mouseDeltaScaleMultiplier);
        if (applyDpiScaleToMouseDelta)
        {
            scale *= Mathf.Max(0.01f, WindowsExplorerUtility.GetDPIScale());
        }

        return MoveWindowByPixels(mouseDeltaX * scale, mouseDeltaY * scale);
#else
        return false;
#endif
    }

    private bool ShouldUseEditorTransformMotion()
    {
        return Application.isEditor && useTransformMotionInEditor;
    }

    private bool MoveEditorTransformByScreenDelta(float deltaX, float deltaY)
    {
        if (!TryResolveEditorMotionReferences())
        {
            return false;
        }

        if (!ScreenSpaceTransformUtility.TryGetScreenPosition(targetCamera, editorMotionTarget, out var screen))
        {
            return false;
        }

        var moved = new Vector3(screen.x + deltaX, screen.y + deltaY, screen.z);
        return ScreenSpaceTransformUtility.TrySetScreenPosition(targetCamera, editorMotionTarget, moved, screen.z);
    }

    private bool TryResolveEditorMotionReferences()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (characterSpawnComponent == null)
        {
            characterSpawnComponent = GetComponent<TestCharacterSpawnComponent>();
        }

        if (editorMotionTarget == null && characterSpawnComponent != null && characterSpawnComponent.ModelContainer != null)
        {
            editorMotionTarget = characterSpawnComponent.ModelContainer.transform;
        }

        if (editorMotionTarget == null)
        {
            editorMotionTarget = transform;
        }

        return targetCamera != null && editorMotionTarget != null;
    }
}
