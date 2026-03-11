using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using Kirurobo;

public class TestFallPhysicsComponent : MonoBehaviour, ITestFeatureComponent
{
    private const string LogTag = nameof(TestFallPhysicsComponent);

    private enum GroundSurfaceType
    {
        None = 0,
        Taskbar = 1,
        WindowTop = 2,
    }

    [SerializeField] private bool runTest = true;
    private Camera targetCamera;
    private Transform targetTransform;
    private TestWindowMotionComponent windowMotionComponent;
    private TestActionDecisionManager actionDecisionManager;
    private TestGroundHeightSource groundHeightSource;
    private TestCharacterSpawnComponent characterSpawnComponent;
    [SerializeField] private UniWindowMoveHandle uniWindowMoveHandle;
    [SerializeField] private bool useWindowBottomAsGround = true;
    [SerializeField] private bool suspendGravityWhileUniWindowDragging = true;
    [SerializeField] private bool useWindowTopAsGround = true;
    [SerializeField] private bool followWindowTopMotionWhileGrounded = true;
    [SerializeField] private bool useRendererBottomForGround = false;
    [SerializeField] private bool enableWindowTopDiagnosticLog = false;
    [SerializeField] private bool enableLandingZOrderDiagnosticLog = true;
    [SerializeField] private bool enableContactPointDiagnosticLog = true;
    [SerializeField] private bool keepTopmostOnTaskbarLanding = true;
    [SerializeField] private int supportWindowLookupGraceFrames = 15;
    [Header("Window Exclusions")]
    [SerializeField] private bool excludeExternalUiProcessFromOcclusionCheck = true;
    [SerializeField] private string[] excludedFullscreenOccluderProcessNames = { "DesktopAgentUI" };
    [SerializeField] private float gravityPixelsPerSec2 = 2800f;
    [SerializeField] private float maxFallSpeedPixelsPerSec = 6000f;
    [SerializeField] private float initialFallSpeedPixelsPerSec = 0f;
    [SerializeField] private float groundedEpsilonPixels = 0.5f;

    private float _fallSpeedPixelsPerSec;
    private bool _lastGroundedState;
    private bool _missingReferenceWarningLogged;
    private bool _cameraResolvedLogged;
    private bool _targetResolvedLogged;
    private bool _groundSourceResolvedLogged;
    private bool _rendererFallbackWarningLogged;
    private bool _rendererGroundEnabledLogged;
    private bool _landingMetricsLogged;
    private float _virtualScreenY;
    private bool _virtualScreenYInitialized;
    private bool _windowGroundUnavailableLogged;
    private bool _gravitySuspendedByDrag;
    private bool _draggingDrivenByFallPhysics;
    private GroundSurfaceType _lastGroundSurfaceType = GroundSurfaceType.None;
    private string _lastWindowTopDiagnosticMessage;
    private IntPtr _followingWindowHandle;
    private WindowsAPI.RECT _lastFollowingWindowRect;
    private bool _hasFollowingWindowRect;
    private float _followingWindowOffsetX;
    private bool _wasSupportFocusedLastFrame;
    private IntPtr _lastFocusedSupportWindowHandle;
    private bool _isTopmostPinnedBySupportFocus;
    private int _supportWindowLookupFailureFrames;
    public bool IsTestEnabled => runTest;
    public bool IsGrounded { get; private set; }
    public float CurrentFallSpeedPixelsPerSec => _fallSpeedPixelsPerSec;

    public void OnTestStart()
    {
        _fallSpeedPixelsPerSec = Mathf.Max(0f, initialFallSpeedPixelsPerSec);
        IsGrounded = false;
        _lastGroundedState = IsGrounded;
        _missingReferenceWarningLogged = false;
        _cameraResolvedLogged = false;
        _targetResolvedLogged = false;
        _groundSourceResolvedLogged = false;
        _rendererFallbackWarningLogged = false;
        _rendererGroundEnabledLogged = false;
        _landingMetricsLogged = false;
        _virtualScreenY = 0f;
        _virtualScreenYInitialized = false;
        _windowGroundUnavailableLogged = false;
        _gravitySuspendedByDrag = false;
        _draggingDrivenByFallPhysics = false;
        _lastGroundSurfaceType = GroundSurfaceType.None;
        _lastWindowTopDiagnosticMessage = null;
        _followingWindowHandle = IntPtr.Zero;
        _lastFollowingWindowRect = default;
        _hasFollowingWindowRect = false;
        _followingWindowOffsetX = 0f;
        _wasSupportFocusedLastFrame = false;
        _lastFocusedSupportWindowHandle = IntPtr.Zero;
        _isTopmostPinnedBySupportFocus = false;
        _supportWindowLookupFailureFrames = 0;
        SyncWindowSurfaceState(false);
        SyncDraggingState(false);
        SyncFallingState(false);
        TestLog.Info(LogTag, $"Started. gravity={gravityPixelsPerSec2}, maxSpeed={maxFallSpeedPixelsPerSec}, initialSpeed={_fallSpeedPixelsPerSec}");
    }

    public void OnTestTick(float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return;
        }

        if (!TryResolveReferences())
        {
            if (!_missingReferenceWarningLogged)
            {
                _missingReferenceWarningLogged = true;
            }
            return;
        }

        _missingReferenceWarningLogged = false;

        bool isDraggingByUniWindow = suspendGravityWhileUniWindowDragging
                                     && uniWindowMoveHandle != null
                                     && uniWindowMoveHandle.IsDragging;

        if (_draggingDrivenByFallPhysics && !isDraggingByUniWindow)
        {
            SyncDraggingState(false);
            _draggingDrivenByFallPhysics = false;
        }

        bool isDraggingByActionManager = actionDecisionManager != null && actionDecisionManager.AnimatorIsDragging;

        if (isDraggingByActionManager || isDraggingByUniWindow)
        {
            if (!_gravitySuspendedByDrag)
            {
                ResetNonDragStateOnDragStart();
                _gravitySuspendedByDrag = true;
                _fallSpeedPixelsPerSec = 0f;
                SyncWindowSurfaceState(false);
                SyncDraggingState(true);
                _draggingDrivenByFallPhysics = isDraggingByUniWindow;
                SyncFallingState(false);
                TestLog.Info(LogTag, $"Gravity suspended while dragging. source={(isDraggingByActionManager ? "AnimatorFlag" : "UniWindowMoveHandle")}");
            }

            return;
        }

        if (_gravitySuspendedByDrag)
        {
            _gravitySuspendedByDrag = false;
            SyncDraggingState(false);
            _draggingDrivenByFallPhysics = false;
            TestLog.Info(LogTag, "Gravity resumed after drag.");
        }

        if (followWindowTopMotionWhileGrounded)
        {
            FollowSupportingWindowIfMoved();
        }

        if (!ScreenSpaceTransformUtility.TryGetScreenPosition(targetCamera, targetTransform, out var modelScreen))
        {
            return;
        }

        if (!_virtualScreenYInitialized)
        {
            _virtualScreenY = modelScreen.y;
            _virtualScreenYInitialized = true;
        }

        float groundContactOffsetPixels = GetGroundContactOffsetPixels(modelScreen);
        float sitYOffsetPixels = 0f;
        float groundYOffsetPixels = 0f;
        float groundY = GetGroundY(groundYOffsetPixels);
        bool wasGrounded = IsGrounded;

        if (useWindowBottomAsGround && TryGetWindowBottomGapToGround(sitYOffsetPixels, out float signedGapToTaskbar, out GroundSurfaceType groundSurfaceType, out _, out _, out IntPtr supportWindowHandle, out WindowsAPI.RECT supportWindowRect))
        {
            _windowGroundUnavailableLogged = false;
            bool trackedSupportWindow = followWindowTopMotionWhileGrounded
                                        && _followingWindowHandle != IntPtr.Zero
                                        && supportWindowHandle == _followingWindowHandle
                                        && groundSurfaceType == GroundSurfaceType.WindowTop;

            if (trackedSupportWindow)
            {
                float correctionDeltaY = -signedGapToTaskbar;
                if (Mathf.Abs(correctionDeltaY) > 0.001f && windowMotionComponent != null)
                {
                    windowMotionComponent.MoveWindowByPixels(0f, correctionDeltaY);
                }

                SyncDraggingState(false);
                _fallSpeedPixelsPerSec = 0f;
                IsGrounded = true;
                _lastGroundSurfaceType = groundSurfaceType;
                UpdateSupportingWindowFollowTarget(groundSurfaceType, supportWindowHandle, supportWindowRect);
                SyncWindowSurfaceState(true);
                SyncFallingState(false);
                EnsureTopmostForTaskbarLanding();
                if (!wasGrounded)
                {
                    ApplyLandingZOrder(groundSurfaceType, supportWindowHandle);
                }
                _landingMetricsLogged = false;
                LogGroundedStateChangeIfNeeded();
                return;
            }

            float groundedEpsilon = Mathf.Max(0f, groundedEpsilonPixels);
            if (signedGapToTaskbar <= groundedEpsilon)
            {
                float correctionDeltaY = -signedGapToTaskbar;
                if (Mathf.Abs(correctionDeltaY) > 0.001f && windowMotionComponent != null)
                {
                    windowMotionComponent.MoveWindowByPixels(0f, correctionDeltaY);
                }

                SyncDraggingState(false);
                _fallSpeedPixelsPerSec = 0f;
                IsGrounded = true;
                _lastGroundSurfaceType = groundSurfaceType;
                UpdateSupportingWindowFollowTarget(groundSurfaceType, supportWindowHandle, supportWindowRect);
                SyncWindowSurfaceState(groundSurfaceType == GroundSurfaceType.WindowTop);
                SyncFallingState(false);
                EnsureTopmostForTaskbarLanding();
                if (!wasGrounded)
                {
                    ApplyLandingZOrder(groundSurfaceType, supportWindowHandle);
                }
                _landingMetricsLogged = false;
                LogGroundedStateChangeIfNeeded();
                return;
            }

            float gravityWindow = Mathf.Max(0f, gravityPixelsPerSec2);
            float maxSpeedWindow = Mathf.Max(0f, maxFallSpeedPixelsPerSec);
            _fallSpeedPixelsPerSec = Mathf.Min(maxSpeedWindow, _fallSpeedPixelsPerSec + gravityWindow * deltaTime);
            float desiredDownMove = _fallSpeedPixelsPerSec * deltaTime;
            float clampedDownMove = Mathf.Clamp(desiredDownMove, 0f, signedGapToTaskbar);

            if (clampedDownMove > 0f && windowMotionComponent != null)
            {
                windowMotionComponent.MoveWindowByPixels(0f, -clampedDownMove);
            }

            bool landedThisTick = clampedDownMove >= signedGapToTaskbar - groundedEpsilon;
            if (landedThisTick)
            {
                _fallSpeedPixelsPerSec = 0f;
                IsGrounded = true;
                _lastGroundSurfaceType = groundSurfaceType;
                UpdateSupportingWindowFollowTarget(groundSurfaceType, supportWindowHandle, supportWindowRect);
                SyncWindowSurfaceState(groundSurfaceType == GroundSurfaceType.WindowTop);
                SyncFallingState(false);
                EnsureTopmostForTaskbarLanding();
                if (!wasGrounded)
                {
                    ApplyLandingZOrder(groundSurfaceType, supportWindowHandle);
                }
                LogLandingMetricsOnce(_virtualScreenY, groundContactOffsetPixels, groundY, sitYOffsetPixels, groundYOffsetPixels);
            }
            else
            {
                IsGrounded = false;
                _lastGroundSurfaceType = GroundSurfaceType.None;
                ClearSupportingWindowFollowTarget();
                SyncWindowSurfaceState(false);
                SyncFallingState(true);
                _landingMetricsLogged = false;
            }

            LogGroundedStateChangeIfNeeded();
            return;
        }

        if (useWindowBottomAsGround && !_windowGroundUnavailableLogged)
        {
            _windowGroundUnavailableLogged = true;
            TestLog.Warning(LogTag, "Window-bottom ground mode could not read window/workarea. Fallback to screen-ground mode.");
        }

        float currentContactY = _virtualScreenY - groundContactOffsetPixels;
        if (currentContactY <= groundY + Mathf.Max(0f, groundedEpsilonPixels))
        {
            float correctedY = groundY + groundContactOffsetPixels;
            float deltaY = correctedY - _virtualScreenY;
            _virtualScreenY = correctedY;
            if (windowMotionComponent != null)
            {
                windowMotionComponent.MoveWindowByPixels(0f, deltaY);
            }
            _fallSpeedPixelsPerSec = 0f;
            IsGrounded = true;
            _lastGroundSurfaceType = GroundSurfaceType.None;
            ClearSupportingWindowFollowTarget();
            SyncWindowSurfaceState(false);
            SyncFallingState(false);
            LogLandingMetricsOnce(_virtualScreenY, groundContactOffsetPixels, groundY, sitYOffsetPixels, groundYOffsetPixels);
            LogGroundedStateChangeIfNeeded();
            return;
        }

        float gravity = Mathf.Max(0f, gravityPixelsPerSec2);
        float maxSpeed = Mathf.Max(0f, maxFallSpeedPixelsPerSec);

        _fallSpeedPixelsPerSec = Mathf.Min(maxSpeed, _fallSpeedPixelsPerSec + gravity * deltaTime);
        float nextScreenY = _virtualScreenY - _fallSpeedPixelsPerSec * deltaTime;
        float nextContactY = nextScreenY - groundContactOffsetPixels;

        if (nextContactY <= groundY)
        {
            nextScreenY = groundY + groundContactOffsetPixels;
            _fallSpeedPixelsPerSec = 0f;
            IsGrounded = true;
            _lastGroundSurfaceType = GroundSurfaceType.None;
            ClearSupportingWindowFollowTarget();
            SyncWindowSurfaceState(false);
            SyncFallingState(false);
            LogLandingMetricsOnce(nextScreenY, groundContactOffsetPixels, groundY, sitYOffsetPixels, groundYOffsetPixels);
        }
        else
        {
            IsGrounded = false;
            _lastGroundSurfaceType = GroundSurfaceType.None;
            ClearSupportingWindowFollowTarget();
            SyncWindowSurfaceState(false);
            SyncFallingState(true);
            _landingMetricsLogged = false;
        }

        float moveDeltaY = nextScreenY - _virtualScreenY;
        _virtualScreenY = nextScreenY;
        if (windowMotionComponent != null)
        {
            windowMotionComponent.MoveWindowByPixels(0f, moveDeltaY);
        }

        LogGroundedStateChangeIfNeeded();
    }

    public void OnTestStop()
    {
        TestLog.Info(LogTag, "Stopped.");
    }

    public void ResetFallState(float newFallSpeedPixelsPerSec = 0f, bool grounded = false)
    {
        _fallSpeedPixelsPerSec = Mathf.Max(0f, newFallSpeedPixelsPerSec);
        IsGrounded = grounded;
        SyncFallingState(!grounded);
        LogGroundedStateChangeIfNeeded();
        TestLog.Info(LogTag, $"Fall state reset. speed={_fallSpeedPixelsPerSec}, grounded={IsGrounded}");
    }

    private bool TryResolveReferences()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
            if (targetCamera != null && !_cameraResolvedLogged)
            {
                _cameraResolvedLogged = true;
            }
        }

        if (characterSpawnComponent == null)
        {
            characterSpawnComponent = GetComponent<TestCharacterSpawnComponent>();
        }

        if (targetTransform == null && characterSpawnComponent != null && characterSpawnComponent.ModelContainer != null)
        {
            targetTransform = characterSpawnComponent.ModelContainer.transform;
            if (!_targetResolvedLogged)
            {
                _targetResolvedLogged = true;
            }
        }

        if (groundHeightSource == null)
        {
            groundHeightSource = GetComponent<TestGroundHeightSource>();
            if (groundHeightSource != null && !_groundSourceResolvedLogged)
            {
                _groundSourceResolvedLogged = true;
            }
        }

        if (actionDecisionManager == null)
        {
            actionDecisionManager = GetComponent<TestActionDecisionManager>();
        }

        if (windowMotionComponent == null)
        {
            windowMotionComponent = GetComponent<TestWindowMotionComponent>();
        }

        if (uniWindowMoveHandle == null)
        {
            uniWindowMoveHandle = FindFirstObjectByType<UniWindowMoveHandle>();
        }

        return targetCamera != null && targetTransform != null;
    }

    private float GetGroundY(float groundYOffsetPixels)
    {
        if (useWindowBottomAsGround)
        {
            return 0f;
        }

        float baseGroundY = groundHeightSource != null ? groundHeightSource.GroundY : 0f;
        return baseGroundY + groundYOffsetPixels;
    }

    private float GetGroundContactOffsetPixels(Vector3 modelScreen)
    {
        if (useWindowBottomAsGround)
        {
            return 0f;
        }

        if (!useRendererBottomForGround || targetCamera == null || targetTransform == null)
        {
            return 0f;
        }

        var renderers = targetTransform.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            LogRendererFallbackWarningOnce("Renderer not found. Falling back to transform pivot for ground contact.");
            return 0f;
        }

        float minY = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            var bounds = renderer.bounds;
            var min = bounds.min;
            var max = bounds.max;

            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        var corner = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);
                        var screen = targetCamera.WorldToScreenPoint(corner);
                        if (screen.z <= 0f)
                        {
                            continue;
                        }

                        minY = Mathf.Min(minY, screen.y);
                        found = true;
                    }
                }
            }
        }

        if (!found)
        {
            LogRendererFallbackWarningOnce("Renderer bounds could not be projected. Falling back to transform pivot for ground contact.");
            return 0f;
        }

        if (!_rendererGroundEnabledLogged)
        {
            _rendererGroundEnabledLogged = true;
            TestLog.Info(LogTag, "Renderer-bottom ground contact is active.");
        }

        _rendererFallbackWarningLogged = false;
        return Mathf.Max(0f, modelScreen.y - minY);
    }

    private void LogRendererFallbackWarningOnce(string message)
    {
        if (_rendererFallbackWarningLogged)
        {
            return;
        }

        _rendererFallbackWarningLogged = true;
        TestLog.Warning(LogTag, message);
    }

    private void LogGroundedStateChangeIfNeeded()
    {
        if (_lastGroundedState == IsGrounded)
        {
            return;
        }

        _lastGroundedState = IsGrounded;
        TestLog.Info(LogTag, $"Grounded state changed. grounded={IsGrounded}");
    }

    private void LogLandingMetricsOnce(float pivotY, float contactOffsetPixels, float groundY, float sitYOffsetPixels, float groundYOffsetPixels)
    {
        if (_landingMetricsLogged)
        {
            return;
        }

        _landingMetricsLogged = true;
        if (!enableContactPointDiagnosticLog)
        {
            return;
        }

        float contactY = pivotY - contactOffsetPixels;
        TestLog.Info(
            LogTag,
            $"ContactDiagnostic: pivotY={pivotY:0.###}, contactOffset={contactOffsetPixels:0.###}, contactY={contactY:0.###}, groundY(afterOffset)={groundY:0.###}, sitOffsetPx={sitYOffsetPixels:0.###}, groundOffsetPx={groundYOffsetPixels:0.###}, rendererBottomMode={useRendererBottomForGround}, windowBottomGroundMode={useWindowBottomAsGround}");
    }

    private bool TryGetWindowBottomGapToGround(float sitYOffsetPixels, out float signedGapPixels, out GroundSurfaceType surfaceType, out float windowBottom, out float groundSupportY, out IntPtr supportWindowHandle, out WindowsAPI.RECT supportWindowRect)
    {
        signedGapPixels = 0f;
        surfaceType = GroundSurfaceType.None;
        windowBottom = 0f;
        groundSupportY = 0f;
        supportWindowHandle = IntPtr.Zero;
        supportWindowRect = default;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!WindowsAPI.TryGetCurrentWindowRect(out var windowRect))
        {
            return false;
        }

        if (!WindowsAPI.TryGetWorkArea(out var workArea))
        {
            return false;
        }

        float currentWindowCenterY = (windowRect.top + windowRect.bottom) * 0.5f;
        windowBottom = currentWindowCenterY;
        float centerX = (windowRect.left + windowRect.right) * 0.5f;
        float fallbackSupportY = workArea.bottom;
        bool hasTopLevelCandidate = false;
        string fallbackReason = "windowTopCandidates=none";

        if (followWindowTopMotionWhileGrounded && _followingWindowHandle != IntPtr.Zero && _hasFollowingWindowRect)
        {
            if (TryGetTrackedSupportWindowRect(_followingWindowHandle, out var trackedRect, out bool supportWindowMinimized))
            {
                _supportWindowLookupFailureFrames = 0;
                float trackedSupportTop = trackedRect.top + sitYOffsetPixels;
                float trackedGap = trackedSupportTop - currentWindowCenterY;
                signedGapPixels = trackedGap;
                surfaceType = GroundSurfaceType.WindowTop;
                groundSupportY = trackedSupportTop;
                supportWindowHandle = _followingWindowHandle;
                supportWindowRect = trackedRect;
                return true;
            }

            _supportWindowLookupFailureFrames++;
            int graceFrames = Mathf.Max(0, supportWindowLookupGraceFrames);

            if (!supportWindowMinimized && _supportWindowLookupFailureFrames <= graceFrames)
            {
                float trackedSupportTop = _lastFollowingWindowRect.top + sitYOffsetPixels;
                float trackedGap = trackedSupportTop - currentWindowCenterY;
                surfaceType = GroundSurfaceType.WindowTop;
                groundSupportY = trackedSupportTop;
                supportWindowHandle = _followingWindowHandle;
                supportWindowRect = _lastFollowingWindowRect;
                LogWindowTopDiagnosticIfChanged($"windowTopReuseLastRect hwnd={_followingWindowHandle} failures={_supportWindowLookupFailureFrames} grace={graceFrames}");
                return true;
            }
        }

        if (useWindowTopAsGround)
        {
            IntPtr currentHwnd = WindowsAPI.GetCurrentWindowHandle();
            var candidateWindows = WindowsExplorerUtility.GetTopLevelWindows();
            for (int i = 0; i < candidateWindows.Count; i++)
            {
                var info = candidateWindows[i];
                if (info.hWnd == IntPtr.Zero || info.hWnd == currentHwnd)
                {
                    continue;
                }

                if (IsExcludedWindowFromFullscreenOcclusion(info.hWnd))
                {
                    fallbackReason = $"windowTopRejected=excludedProcess hwnd={info.hWnd}";
                    continue;
                }

                hasTopLevelCandidate = true;

                var rect = info.rect;
                float width = rect.right - rect.left;
                float height = rect.bottom - rect.top;
                if (width < 50f || height < 50f)
                {
                    fallbackReason = $"windowTopRejected=tooSmall hwnd={info.hWnd} size=({width:0.#},{height:0.#})";
                    continue;
                }

                bool outsideWorkArea = rect.right <= workArea.left
                                       || rect.left >= workArea.right
                                       || rect.bottom <= workArea.top
                                       || rect.top >= workArea.bottom;
                if (outsideWorkArea)
                {
                    fallbackReason = $"windowTopRejected=outsideWorkArea hwnd={info.hWnd} rect=({rect.left},{rect.top},{rect.right},{rect.bottom})";
                    continue;
                }

                bool isMaximized = NativeWindowApi.IsWindowMaximized(info.hWnd) || NativeWindowApi.IsWindowFullscreen(info.hWnd);

                if (centerX < rect.left || centerX > rect.right)
                {
                    fallbackReason = $"windowTopRejected=centerXOutOfRange hwnd={info.hWnd} centerX={centerX:0.#} range=[{rect.left},{rect.right}]";
                    if (isMaximized)
                    {
                        fallbackReason += " breakByMaximized";
                        break;
                    }

                    continue;
                }

                float supportTopY = rect.top + sitYOffsetPixels;
                float windowTopGap = supportTopY - currentWindowCenterY;
                if (windowTopGap < 0f)
                {
                    fallbackReason = $"windowTopRejected=windowTopBelowCurrent hwnd={info.hWnd} gap={windowTopGap:0.###}";
                    if (isMaximized)
                    {
                        fallbackReason += " breakByMaximized";
                        break;
                    }

                    continue;
                }

                LogWindowTopDiagnosticIfChanged($"windowTopSelected hwnd={info.hWnd} gap={windowTopGap:0.###} top={supportTopY:0.###} rawTop={rect.top} centerX={centerX:0.#} sitOffset={sitYOffsetPixels:0.###}");
                signedGapPixels = windowTopGap;
                surfaceType = GroundSurfaceType.WindowTop;
                groundSupportY = supportTopY;
                supportWindowHandle = info.hWnd;
                supportWindowRect = rect;
                return true;
            }
        }

        if (!useWindowTopAsGround)
        {
            fallbackReason = "windowTopDisabled";
        }
        else if (!hasTopLevelCandidate)
        {
            fallbackReason = "windowTopCandidates=none";
        }

        LogWindowTopDiagnosticIfChanged($"fallbackTaskbar reason={fallbackReason} supportY={fallbackSupportY:0.###} windowCenterY={currentWindowCenterY:0.###}");

        signedGapPixels = fallbackSupportY - currentWindowCenterY;
        surfaceType = GroundSurfaceType.Taskbar;
        groundSupportY = fallbackSupportY;
        return true;
#else
        return false;
#endif
    }

    private void UpdateSupportingWindowFollowTarget(GroundSurfaceType groundSurfaceType, IntPtr supportWindowHandle, WindowsAPI.RECT supportWindowRect)
    {
        if (!followWindowTopMotionWhileGrounded || groundSurfaceType != GroundSurfaceType.WindowTop || supportWindowHandle == IntPtr.Zero)
        {
            ClearSupportingWindowFollowTarget();
            return;
        }

        bool isNewSupportTarget = _followingWindowHandle != supportWindowHandle || !_hasFollowingWindowRect;

        _followingWindowHandle = supportWindowHandle;
        _lastFollowingWindowRect = supportWindowRect;
        _hasFollowingWindowRect = true;
        _supportWindowLookupFailureFrames = 0;

        if (isNewSupportTarget && WindowsAPI.TryGetCurrentWindowRect(out var appRect))
        {
            _followingWindowOffsetX = appRect.left - supportWindowRect.left;
        }
    }

    private void ClearSupportingWindowFollowTarget()
    {
        _followingWindowHandle = IntPtr.Zero;
        _lastFollowingWindowRect = default;
        _hasFollowingWindowRect = false;
        _followingWindowOffsetX = 0f;
        _wasSupportFocusedLastFrame = false;
        _lastFocusedSupportWindowHandle = IntPtr.Zero;
        bool keepTopmost = keepTopmostOnTaskbarLanding
                           && IsGrounded
                           && _lastGroundSurfaceType == GroundSurfaceType.Taskbar;
        if (_isTopmostPinnedBySupportFocus && !keepTopmost)
        {
            bool released = WindowsAPI.SetCurrentWindowTopmost(false);
            LogLandingZOrderDiagnostic($"clearFollow releaseTopmost result={released}");
            _isTopmostPinnedBySupportFocus = false;
        }

        if (_isTopmostPinnedBySupportFocus && keepTopmost)
        {
            bool ensuredTopmost = WindowsAPI.SetCurrentWindowTopmost(true);
            _isTopmostPinnedBySupportFocus = ensuredTopmost;
            LogLandingZOrderDiagnostic("clearFollow keepTopmost on taskbar landing.");
        }
        _supportWindowLookupFailureFrames = 0;
    }

    private void EnsureTopmostForTaskbarLanding()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!keepTopmostOnTaskbarLanding || !IsGrounded || _lastGroundSurfaceType != GroundSurfaceType.Taskbar)
        {
            return;
        }

        if (_isTopmostPinnedBySupportFocus)
        {
            return;
        }

        bool topmostEnabled = WindowsAPI.SetCurrentWindowTopmost(true);
        _isTopmostPinnedBySupportFocus = topmostEnabled;
        LogLandingZOrderDiagnostic($"taskbarTopmost ensure result={topmostEnabled}");
#endif
    }

    private void FollowSupportingWindowIfMoved()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!IsGrounded || _lastGroundSurfaceType != GroundSurfaceType.WindowTop)
        {
            return;
        }

        if (_followingWindowHandle == IntPtr.Zero || !_hasFollowingWindowRect)
        {
            return;
        }

        if (!TryGetTrackedSupportWindowRect(_followingWindowHandle, out var currentRect, out bool supportWindowMinimized))
        {
            _supportWindowLookupFailureFrames++;
            int graceFrames = Mathf.Max(0, supportWindowLookupGraceFrames);
            if (supportWindowMinimized || _supportWindowLookupFailureFrames > graceFrames)
            {
                LogLandingZOrderDiagnostic($"followFocus detach reason=trackedRectUnavailable support={FormatHwnd(_followingWindowHandle)} minimized={supportWindowMinimized} failures={_supportWindowLookupFailureFrames} grace={graceFrames}");
                DetachFromWindowTopSupport();
            }
            return;
        }

        _supportWindowLookupFailureFrames = 0;

        EnsureCharacterFrontWhenSupportFocused(_followingWindowHandle);

        if (WindowsAPI.IsWindowMinimized(_followingWindowHandle))
        {
            LogLandingZOrderDiagnostic($"followFocus detach reason=supportMinimized support={FormatHwnd(_followingWindowHandle)}");
            DetachFromWindowTopSupport();
            return;
        }

        if (IsSupportWindowHiddenByFrontWindow(_followingWindowHandle, currentRect))
        {
            IntPtr foregroundHwnd = NativeWindowApi.GetForegroundWindowHandle();
            LogLandingZOrderDiagnostic($"followFocus detach reason=hiddenByFront support={FormatHwnd(_followingWindowHandle)} foreground={FormatHwnd(foregroundHwnd)}");
            DetachFromWindowTopSupport();
            return;
        }

        float deltaY = currentRect.top - _lastFollowingWindowRect.top;
        if (windowMotionComponent != null && WindowsAPI.TryGetCurrentWindowRect(out var currentAppRect))
        {
            float appCenterX = (currentAppRect.left + currentAppRect.right) * 0.5f;
            if (appCenterX < currentRect.left || appCenterX > currentRect.right)
            {
                LogLandingZOrderDiagnostic($"followFocus detach reason=appCenterOutOfSupportRange support={FormatHwnd(_followingWindowHandle)} appCenterX={appCenterX:0.###} supportLeft={currentRect.left} supportRight={currentRect.right}");
                DetachFromWindowTopSupport();
                return;
            }

            float desiredLeft = currentRect.left + _followingWindowOffsetX;
            float deltaXToTarget = desiredLeft - currentAppRect.left;
            if (Mathf.Abs(deltaXToTarget) > 0.001f || Mathf.Abs(deltaY) > 0.001f)
            {
                windowMotionComponent.MoveWindowByPixels(deltaXToTarget, -deltaY);
            }

            float sitYOffsetPixels = 0f;
            float supportTop = currentRect.top + sitYOffsetPixels;
            float appCenterY = (currentAppRect.top + currentAppRect.bottom) * 0.5f;
            float gapToSupportTop = supportTop - appCenterY;
            if (Mathf.Abs(gapToSupportTop) > Mathf.Max(0.001f, groundedEpsilonPixels))
            {
                windowMotionComponent.MoveWindowByPixels(0f, -gapToSupportTop);
            }
        }

        _lastFollowingWindowRect = currentRect;
#endif
    }

    private void DetachFromWindowTopSupport()
    {
        LogLandingZOrderDiagnostic($"followFocus detach execute support={FormatHwnd(_followingWindowHandle)} wasPinned={_isTopmostPinnedBySupportFocus} wasGrounded={IsGrounded} lastSurface={_lastGroundSurfaceType}");
        ClearSupportingWindowFollowTarget();
        IsGrounded = false;
        _lastGroundSurfaceType = GroundSurfaceType.None;
        _landingMetricsLogged = false;
        SyncWindowSurfaceState(false);
        SyncFallingState(true);
        LogGroundedStateChangeIfNeeded();
    }

    private void ResetNonDragStateOnDragStart()
    {
        LogLandingZOrderDiagnostic($"followFocus resetByDragStart support={FormatHwnd(_followingWindowHandle)} pinned={_isTopmostPinnedBySupportFocus}");
        IsGrounded = false;
        _lastGroundSurfaceType = GroundSurfaceType.None;
        _landingMetricsLogged = false;
        _windowGroundUnavailableLogged = false;
        _rendererGroundEnabledLogged = false;
        _lastWindowTopDiagnosticMessage = null;
        ClearSupportingWindowFollowTarget();
        _virtualScreenYInitialized = false;
    }

    private bool IsSupportWindowHiddenByFrontWindow(IntPtr supportHwnd, WindowsAPI.RECT supportRect)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (supportHwnd == IntPtr.Zero)
        {
            return false;
        }

        IntPtr currentHwnd = WindowsAPI.GetCurrentWindowHandle();
        IntPtr foregroundHwnd = NativeWindowApi.GetForegroundWindowHandle();
        if (foregroundHwnd == IntPtr.Zero || foregroundHwnd == currentHwnd || foregroundHwnd == supportHwnd)
        {
            return false;
        }

        if (IsExcludedWindowFromFullscreenOcclusion(foregroundHwnd))
        {
            return false;
        }

        if (!WindowsAPI.IsWindowVisible(foregroundHwnd) || WindowsAPI.IsWindowMinimized(foregroundHwnd))
        {
            return false;
        }

        if (!WindowsAPI.GetWindowRect(foregroundHwnd, out var rect))
        {
            return false;
        }

        float width = rect.right - rect.left;
        float height = rect.bottom - rect.top;
        if (width < 50f || height < 50f)
        {
            return false;
        }

        float probeCenterX = (supportRect.left + supportRect.right) * 0.5f;
        if (WindowsAPI.TryGetCurrentWindowRect(out var appRect))
        {
            probeCenterX = (appRect.left + appRect.right) * 0.5f;
        }

        float supportWidth = Mathf.Max(1f, supportRect.right - supportRect.left);
        float overlapLeft = Mathf.Max(rect.left, supportRect.left);
        float overlapRight = Mathf.Min(rect.right, supportRect.right);
        float overlapWidth = Mathf.Max(0f, overlapRight - overlapLeft);
        bool largeOccluder = overlapWidth >= supportWidth * 0.75f;

        bool dominantWindow = NativeWindowApi.IsWindowMaximized(foregroundHwnd)
                              || NativeWindowApi.IsWindowFullscreen(foregroundHwnd)
                              || largeOccluder;
        if (!dominantWindow)
        {
            return false;
        }

        bool blocksX = probeCenterX >= rect.left && probeCenterX <= rect.right;
        bool blocksY = rect.top <= supportRect.top && rect.bottom > supportRect.top;
        if (blocksX && blocksY)
        {
            return true;
        }

#endif
    return false;
    }

    private void EnsureCharacterFrontWhenSupportFocused(IntPtr supportHwnd)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (supportHwnd == IntPtr.Zero)
        {
            _wasSupportFocusedLastFrame = false;
            _lastFocusedSupportWindowHandle = IntPtr.Zero;
            if (_isTopmostPinnedBySupportFocus)
            {
                bool released = WindowsAPI.SetCurrentWindowTopmost(false);
                LogLandingZOrderDiagnostic($"followFocus support=0 releaseTopmost result={released}");
                _isTopmostPinnedBySupportFocus = false;
            }
            LogLandingZOrderDiagnostic("followFocus skip reason=supportZero");
            return;
        }

        IntPtr foregroundHwnd = NativeWindowApi.GetForegroundWindowHandle();
        bool topmostEnabled = false;

        if (foregroundHwnd != supportHwnd)
        {
            if (topmostEnabled)
            {
                bool topmostReleased = WindowsAPI.SetCurrentWindowTopmost(false);
                LogLandingZOrderDiagnostic($"followFocus support=0 releaseTopmost result={topmostReleased}");
                _isTopmostPinnedBySupportFocus = false;
                topmostEnabled = false;
            }

            bool followingWindowTopSupport = IsGrounded
                                                    && _lastGroundSurfaceType == GroundSurfaceType.WindowTop
                                                    && _followingWindowHandle == supportHwnd;
            bool keepFrontWhileFollowingWindowTop = followingWindowTopSupport && (_isTopmostPinnedBySupportFocus);

            LogLandingZOrderDiagnostic($"_lastFocusedSupportWindowHandle={FormatHwnd(_lastFocusedSupportWindowHandle)} supportHwnd={FormatHwnd(supportHwnd)} foregroundHwnd={FormatHwnd(foregroundHwnd)} followingWindowTopSupport={followingWindowTopSupport} keepFrontWhileFollowingWindowTop={keepFrontWhileFollowingWindowTop} wasSupportFocusedLastFrame={_wasSupportFocusedLastFrame} pinnedBySupportFocus={_isTopmostPinnedBySupportFocus}");
            if (keepFrontWhileFollowingWindowTop)
            {
                
                if (!_isTopmostPinnedBySupportFocus)
                {
                    topmostEnabled = WindowsAPI.SetCurrentWindowTopmost(true);
                    _isTopmostPinnedBySupportFocus = true;
                }

                bool _placed = WindowsAPI.PlaceCurrentWindowAbove(supportHwnd);
                bool _brought = false;
                bool _supportChanged = _lastFocusedSupportWindowHandle != supportHwnd;
                if (!_wasSupportFocusedLastFrame || _supportChanged || topmostEnabled)
                {
                    _brought = WindowsAPI.BringCurrentWindowToFront();
                }
                LogLandingZOrderDiagnostic($"followFocus supportNotForeground keepFront support={FormatHwnd(supportHwnd)} foreground={FormatHwnd(foregroundHwnd)} topmostEnabled={topmostEnabled} brought={_brought} placeAbove={_placed} supportChanged={_supportChanged}");

                _wasSupportFocusedLastFrame = true;
                _lastFocusedSupportWindowHandle = supportHwnd;
                return;
            }

            _wasSupportFocusedLastFrame = false;
            _lastFocusedSupportWindowHandle = IntPtr.Zero;
            LogLandingZOrderDiagnostic($"followFocus skip reason=notFollowingWindowTop support={FormatHwnd(supportHwnd)} foreground={FormatHwnd(foregroundHwnd)} isGrounded={IsGrounded} lastSurface={_lastGroundSurfaceType} followHandle={FormatHwnd(_followingWindowHandle)} wasSupportFocused={_wasSupportFocusedLastFrame} pinned={_isTopmostPinnedBySupportFocus}");

            return;
        }

        bool supportChanged = _lastFocusedSupportWindowHandle != supportHwnd;

        if(!_isTopmostPinnedBySupportFocus || !topmostEnabled)
        {
             topmostEnabled = WindowsAPI.SetCurrentWindowTopmost(true);
            _isTopmostPinnedBySupportFocus = topmostEnabled;
        }

        bool brought = false;
        if (!_wasSupportFocusedLastFrame || supportChanged || topmostEnabled)
        {
            brought = WindowsAPI.BringCurrentWindowToFront();
        }
        LogLandingZOrderDiagnostic($"followFocus supportForeground support={FormatHwnd(supportHwnd)} topmostEnabled={topmostEnabled} brought={brought} placeAbove={false} supportChanged={supportChanged}");

        _wasSupportFocusedLastFrame = true;
        _lastFocusedSupportWindowHandle = supportHwnd;
#endif
    }

    private bool TryGetTopLevelWindowRect(IntPtr hWnd, out WindowsAPI.RECT rect)
    {
        rect = default;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        var candidateWindows = WindowsExplorerUtility.GetTopLevelWindows();
        for (int i = 0; i < candidateWindows.Count; i++)
        {
            var info = candidateWindows[i];
            if (info.hWnd != hWnd)
            {
                continue;
            }

            rect = info.rect;
            return true;
        }
#endif

        return false;
    }

    private bool TryGetTrackedSupportWindowRect(IntPtr hWnd, out WindowsAPI.RECT rect, out bool minimized)
    {
        rect = default;
        minimized = false;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        if (!WindowsAPI.IsWindowVisible(hWnd))
        {
            minimized = true;
            return false;
        }

        if (WindowsAPI.IsWindowMinimized(hWnd))
        {
            minimized = true;
            return false;
        }

        if (TryGetTopLevelWindowRect(hWnd, out rect))
        {
            return true;
        }

        if (WindowsAPI.GetWindowRect(hWnd, out rect))
        {
            return true;
        }
#endif

        return false;
    }

    private void SyncFallingState(bool falling)
    {
        if (actionDecisionManager == null)
        {
            return;
        }

        actionDecisionManager.SetFallingStateForTest(falling, LogTag);
    }

    private void SyncDraggingState(bool dragging)
    {
        if (actionDecisionManager == null)
        {
            return;
        }

        actionDecisionManager.SetDraggingStateForTest(dragging, LogTag);
    }

    private void SyncWindowSurfaceState(bool onWindow)
    {
        if (actionDecisionManager == null)
        {
            return;
        }

        actionDecisionManager.SetOnWindowStateForTest(onWindow, LogTag);
        bool sittingOnWindow = onWindow && actionDecisionManager.CurrentAction == CharacterManager.GroundAction.Sit;
        actionDecisionManager.SetSittingWindowStateForTest(sittingOnWindow, LogTag);
    }

    private void ApplyLandingZOrder(GroundSurfaceType groundSurfaceType, IntPtr supportWindowHandle)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        IntPtr currentHwnd = WindowsAPI.GetCurrentWindowHandle();
        IntPtr foregroundHwnd = NativeWindowApi.GetForegroundWindowHandle();
        IntPtr targetHwnd = IntPtr.Zero;
        if (groundSurfaceType == GroundSurfaceType.WindowTop)
        {
            targetHwnd = supportWindowHandle;
        }
        else if (groundSurfaceType == GroundSurfaceType.Taskbar)
        {
            targetHwnd = NativeWindowApi.GetTaskbarHandle();
        }

        LogLandingZOrderDiagnostic($"landingZOrder begin surface={groundSurfaceType} target={FormatHwnd(targetHwnd)} support={FormatHwnd(supportWindowHandle)} current={FormatHwnd(currentHwnd)} foreground={FormatHwnd(foregroundHwnd)} pinned={_isTopmostPinnedBySupportFocus} followHandle={FormatHwnd(_followingWindowHandle)}");

        if (targetHwnd == IntPtr.Zero)
        {
            bool broughtNoTarget = WindowsAPI.BringCurrentWindowToFront();
            LogLandingZOrderDiagnostic($"landingZOrder target=0 bringToFront={broughtNoTarget}");
            return;
        }

        if (groundSurfaceType == GroundSurfaceType.Taskbar && keepTopmostOnTaskbarLanding)
        {
            bool topmostEnabled = WindowsAPI.SetCurrentWindowTopmost(true);
            _isTopmostPinnedBySupportFocus = topmostEnabled;
            LogLandingZOrderDiagnostic($"landingZOrder taskbar keepTopmost result={topmostEnabled}");
        }

        bool placed = WindowsAPI.PlaceCurrentWindowAbove(targetHwnd);
        bool brought = WindowsAPI.BringCurrentWindowToFront();
        LogLandingZOrderDiagnostic($"landingZOrder applied surface={groundSurfaceType} target={FormatHwnd(targetHwnd)} placeAbove={placed} bringToFront={brought}");
#endif
    }

    private void LogLandingZOrderDiagnostic(string message)
    {
        if (!enableLandingZOrderDiagnosticLog)
        {
            return;
        }

        TestLog.Info(LogTag, $"LandingZOrderDiagnostic: {message}");
    }

    private static string FormatHwnd(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return "0x0";
        }

        return $"0x{hWnd.ToInt64():X}";
    }

    private bool IsExcludedWindowFromFullscreenOcclusion(IntPtr hWnd)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!excludeExternalUiProcessFromOcclusionCheck || hWnd == IntPtr.Zero)
        {
            return false;
        }

        if (excludedFullscreenOccluderProcessNames == null || excludedFullscreenOccluderProcessNames.Length == 0)
        {
            return false;
        }

        if (!TryGetWindowProcessName(hWnd, out var processName) || string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        for (int i = 0; i < excludedFullscreenOccluderProcessNames.Length; i++)
        {
            string candidate = excludedFullscreenOccluderProcessNames[i];
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (string.Equals(processName, candidate.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
#endif

        return false;
    }

    private static bool TryGetWindowProcessName(IntPtr hWnd, out string processName)
    {
        processName = string.Empty;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            GetWindowThreadProcessId(hWnd, out uint processId);
            if (processId == 0)
            {
                return false;
            }

            using var process = Process.GetProcessById((int)processId);
            processName = process?.ProcessName ?? string.Empty;
            return !string.IsNullOrWhiteSpace(processName);
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }

    private void LogWindowTopDiagnosticIfChanged(string message)
    {
        if (!enableWindowTopDiagnosticLog)
        {
            return;
        }

        if (string.Equals(_lastWindowTopDiagnosticMessage, message, StringComparison.Ordinal))
        {
            return;
        }

        _lastWindowTopDiagnosticMessage = message;
        TestLog.Info(LogTag, $"WindowTopDiagnostic: {message}");
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
#endif
}