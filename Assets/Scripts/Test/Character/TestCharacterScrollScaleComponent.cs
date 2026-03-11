using UnityEngine;
using UnityEngine.InputSystem;

public class TestCharacterScrollScaleComponent : MonoBehaviour, ITestFeatureComponent
{
    private const string LogTag = nameof(TestCharacterScrollScaleComponent);

    [SerializeField] private bool runTest = true;
    private Camera targetCamera;
    private TestCharacterSpawnComponent characterSpawnComponent;
    private TestActionDecisionManager actionDecisionManager;

    [Header("Scroll Scale")]
    [SerializeField] private float scrollScaleStep = 0.1f;
    [SerializeField] private float minScale = 0.3f;
    [SerializeField] private float maxScale = 3.0f;
    [SerializeField] private bool enableScrollDecisionLog = false;
    [SerializeField] private bool resizeWindowWithCharacterScale = true;
    [SerializeField] private int minWindowWidth = 320;
    [SerializeField] private int minWindowHeight = 240;
    [SerializeField] private int maxWindowWidth = 3840;
    [SerializeField] private int maxWindowHeight = 2160;

    private int _lastScrollHandledFrame = -1;
    private bool _inputRegistered;
    private bool _inputRegistrationWarningLogged;
    private bool _hasEventScrollPending;
    private float _pendingEventScroll;
    private Vector2 _pendingEventScreenPosition;
    private bool _windowResizeBaselineReady;
    private float _windowResizeBaseScale = 1f;
    private int _windowResizeBaseWidth;
    private int _windowResizeBaseHeight;
    private float _windowResizeBaseAspect = 1f;

    public bool IsTestEnabled => runTest;

    public void OnTestStart()
    {
        _lastScrollHandledFrame = -1;
        _inputRegistrationWarningLogged = false;
        _hasEventScrollPending = false;
        _pendingEventScroll = 0f;
        _pendingEventScreenPosition = Vector2.zero;
        _windowResizeBaselineReady = false;
        _windowResizeBaseScale = 1f;
        _windowResizeBaseWidth = 0;
        _windowResizeBaseHeight = 0;
        _windowResizeBaseAspect = 1f;
        RegisterInputEvents();
    }

    public void OnTestTick(float deltaTime)
    {
        if (!_inputRegistered)
        {
            RegisterInputEvents();
        }

        if (Time.frameCount == _lastScrollHandledFrame)
        {
            return;
        }

        if (!TryResolveReferences())
        {
            return;
        }

        if (actionDecisionManager != null && actionDecisionManager.AnimatorIsDragging)
        {
            LogScrollDecision("skip: dragging active");
            return;
        }

        float scroll = 0f;
        Vector2 screenPosition = Vector2.zero;

        if (_hasEventScrollPending)
        {
            scroll = _pendingEventScroll;
            screenPosition = _pendingEventScreenPosition;
            _hasEventScrollPending = false;
            _pendingEventScroll = 0f;
            _pendingEventScreenPosition = Vector2.zero;
            LogScrollDecision($"input: event scroll={scroll:0.###} screen=({screenPosition.x:0.##},{screenPosition.y:0.##})");
        }
        else
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Approximately(scroll, 0f))
            {
                return;
            }

            screenPosition = mouse.position.ReadValue();
            LogScrollDecision($"input: polling scroll={scroll:0.###} screen=({screenPosition.x:0.##},{screenPosition.y:0.##})");
        }

        if (Mathf.Approximately(scroll, 0f))
        {
            return;
        }

        if (!IsPointerOnCharacter(screenPosition))
        {
            LogScrollDecision($"skip: pointer not on character screen=({screenPosition.x:0.##},{screenPosition.y:0.##})");
            return;
        }

        float notch = scroll / 120f;
        if (Mathf.Approximately(notch, 0f))
        {
            notch = Mathf.Sign(scroll);
        }

        Transform modelContainer = characterSpawnComponent.ModelContainer != null
            ? characterSpawnComponent.ModelContainer.transform
            : null;

        if (modelContainer == null)
        {
            LogScrollDecision("skip: model container missing");
            return;
        }

        float currentScale = modelContainer.localScale.x;
        float nextScale = currentScale + (notch * Mathf.Max(0f, scrollScaleStep));
        nextScale = Mathf.Clamp(nextScale, Mathf.Min(minScale, maxScale), Mathf.Max(minScale, maxScale));

        if (Mathf.Approximately(nextScale, currentScale))
        {
            LogScrollDecision($"skip: clamped current={currentScale:0.###} next={nextScale:0.###}");
            return;
        }

        modelContainer.localScale = Vector3.one * nextScale;

        var appSettings = ApplicationSettings.Instance;
        if (appSettings != null && appSettings.Character != null)
        {
            appSettings.Character.Scale = nextScale;
        }

        TryResizeWindowForScale(nextScale);

        _lastScrollHandledFrame = Time.frameCount;
    }

    public void OnTestStop()
    {
        UnregisterInputEvents();
    }

    private void RegisterInputEvents()
    {
        if (_inputRegistered)
        {
            return;
        }

        var controller = InputController.Instance;
        if (controller == null)
        {
            if (!_inputRegistrationWarningLogged)
            {
                _inputRegistrationWarningLogged = true;
                TestLog.Warning(LogTag, "InputController is not ready. Scroll event registration skipped.");
            }

            return;
        }

        controller.UI.ScrollWheel.performed += OnScrollWheel;
        _inputRegistered = true;
        _inputRegistrationWarningLogged = false;
    }

    private void UnregisterInputEvents()
    {
        if (!_inputRegistered)
        {
            return;
        }

        var controller = InputController.Instance;
        if (controller != null)
        {
            controller.UI.ScrollWheel.performed -= OnScrollWheel;
        }

        _inputRegistered = false;
    }

    private void OnScrollWheel(InputAction.CallbackContext context)
    {
        if (!TryResolveReferences())
        {
            LogScrollDecision("event ignored: references unresolved");
            return;
        }

        var mouse = Mouse.current;
        if (mouse == null)
        {
            LogScrollDecision("event ignored: mouse missing");
            return;
        }

        _pendingEventScroll = context.ReadValue<Vector2>().y;
        _pendingEventScreenPosition = mouse.position.ReadValue();
        _hasEventScrollPending = true;
        LogScrollDecision($"event queued: scroll={_pendingEventScroll:0.###} screen=({_pendingEventScreenPosition.x:0.##},{_pendingEventScreenPosition.y:0.##})");
    }

    private void LogScrollDecision(string message)
    {
        if (!enableScrollDecisionLog)
        {
            return;
        }

        TestLog.Info(LogTag, message);
    }

    private void TryResizeWindowForScale(float currentScale)
    {
        if (!resizeWindowWithCharacterScale)
        {
            return;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (Application.isEditor)
        {
            return;
        }

        if (!TryResolveWindowResizeBaseline(currentScale))
        {
            LogScrollDecision("windowResize skipped: baseline unavailable");
            return;
        }

        if (Mathf.Approximately(_windowResizeBaseScale, 0f))
        {
            LogScrollDecision("windowResize skipped: invalid base scale");
            return;
        }

        if (!WindowsAPI.TryGetCurrentWindowRect(out var currentRect))
        {
            LogScrollDecision("windowResize skipped: current rect unavailable");
            return;
        }

        float ratio = currentScale / _windowResizeBaseScale;
        int minH = Mathf.Min(minWindowHeight, maxWindowHeight);
        int maxH = Mathf.Max(minWindowHeight, maxWindowHeight);
        int targetHeight = Mathf.RoundToInt(_windowResizeBaseHeight * ratio);
        targetHeight = Mathf.Clamp(targetHeight, minH, maxH);

        float aspect = Mathf.Max(0.01f, _windowResizeBaseAspect);
        int targetWidth = Mathf.RoundToInt(targetHeight * aspect);

        int minW = Mathf.Min(minWindowWidth, maxWindowWidth);
        int maxW = Mathf.Max(minWindowWidth, maxWindowWidth);
        if (targetWidth < minW)
        {
            targetWidth = minW;
            targetHeight = Mathf.RoundToInt(targetWidth / aspect);
            targetHeight = Mathf.Clamp(targetHeight, minH, maxH);
            targetWidth = Mathf.RoundToInt(targetHeight * aspect);
        }
        else if (targetWidth > maxW)
        {
            targetWidth = maxW;
            targetHeight = Mathf.RoundToInt(targetWidth / aspect);
            targetHeight = Mathf.Clamp(targetHeight, minH, maxH);
            targetWidth = Mathf.RoundToInt(targetHeight * aspect);
        }

        int currentWidth = currentRect.right - currentRect.left;
        int currentHeight = currentRect.bottom - currentRect.top;
        if (targetWidth == currentWidth && targetHeight == currentHeight)
        {
            LogScrollDecision($"windowResize skipped: unchanged width={targetWidth}, height={targetHeight}");
            return;
        }

        int centerX = (currentRect.left + currentRect.right) / 2;
        int centerY = (currentRect.top + currentRect.bottom) / 2;
        int targetX = centerX - (targetWidth / 2);
        int targetY = centerY - (targetHeight / 2);

        bool ok = WindowsAPI.SetCurrentWindowRect(targetX, targetY, targetWidth, targetHeight);
        LogScrollDecision(ok
            ? $"windowResize applied scale={currentScale:0.###} width={targetWidth} height={targetHeight}"
            : "windowResize failed: SetCurrentWindowRect");
#endif
    }

    private bool TryResolveWindowResizeBaseline(float currentScale)
    {
        if (_windowResizeBaselineReady)
        {
            return true;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!WindowsAPI.TryGetCurrentWindowRect(out var rect))
        {
            return false;
        }

        int width = rect.right - rect.left;
        int height = rect.bottom - rect.top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        _windowResizeBaseScale = Mathf.Max(0.01f, currentScale);
        _windowResizeBaseWidth = width;
        _windowResizeBaseHeight = height;
        _windowResizeBaseAspect = height > 0 ? (float)width / height : 1f;
        _windowResizeBaselineReady = true;

        LogScrollDecision($"windowResize baseline captured scale={_windowResizeBaseScale:0.###} width={width} height={height} aspect={_windowResizeBaseAspect:0.###}");
        return true;
#else
        return false;
#endif
    }

    private bool TryResolveReferences()
    {
        if (characterSpawnComponent == null)
        {
            characterSpawnComponent = GetComponent<TestCharacterSpawnComponent>();
        }

        if (actionDecisionManager == null)
        {
            actionDecisionManager = GetComponent<TestActionDecisionManager>();
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
            if (targetCamera == null)
            {
                var cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
                if (cameras != null && cameras.Length > 0)
                {
                    targetCamera = cameras[0];
                }
            }
        }

        return targetCamera != null
            && characterSpawnComponent != null
            && characterSpawnComponent.ModelContainer != null;
    }

    private bool IsPointerOnCharacter(Vector2 screenPosition)
    {
        if (targetCamera == null || characterSpawnComponent == null || characterSpawnComponent.ModelContainer == null)
        {
            return false;
        }

        var ray = targetCamera.ScreenPointToRay(screenPosition);
        var renderers = characterSpawnComponent.ModelContainer.GetComponentsInChildren<Renderer>(true);

        float nearest = float.PositiveInfinity;
        bool hit = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            if (!renderer.bounds.IntersectRay(ray, out var distance))
            {
                continue;
            }

            if (distance < 0f || distance >= nearest)
            {
                continue;
            }

            nearest = distance;
            hit = true;
        }

        return hit;
    }
}
