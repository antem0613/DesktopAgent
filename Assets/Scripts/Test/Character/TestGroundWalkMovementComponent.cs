using UnityEngine;

public class TestGroundWalkMovementComponent : MonoBehaviour, ITestFeatureComponent
{
    private const string LogTag = nameof(TestGroundWalkMovementComponent);

    [SerializeField] private bool runTest = true;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform targetTransform;
    [SerializeField] private TestWindowMotionComponent windowMotionComponent;
    [SerializeField] private TestActionDecisionManager actionDecisionManager;
    [SerializeField] private TestGroundHeightSource groundHeightSource;
    [SerializeField] private TestCharacterSpawnComponent characterSpawnComponent;

    [Header("Walk")]
    [SerializeField] private float walkSpeedPixelsPerSec = 160f;
    [SerializeField] private float walkScreenPaddingPixels = 120f;
    [SerializeField] private float walkReachThresholdPixels = 2f;
    [SerializeField] private float walkMinDistancePixels = 120f;
    [SerializeField] private float walkMaxDistancePixels = 520f;
    [SerializeField] private int walkBlockedFramesToStop = 2;

    [Header("Facing")]
    [SerializeField] private bool faceMovementDirection = true;
    [SerializeField] private bool faceFrontAfterWalk = true;
    [SerializeField] private float faceRightYaw = 90f;
    [SerializeField] private float faceLeftYaw = -90f;

    private float _walkTargetScreenX;
    private float _walkCurrentScreenX;
    private bool _walkActive;
    private bool _waitingForActionTransition;
    private bool _missingReferenceWarningLogged;
    private int _lastFacingDirectionSign;
    private bool _frontYawCaptured;
    private float _frontYaw;
    private int _blockedMoveFrames;

    public bool IsTestEnabled => runTest;

    public void OnTestStart()
    {
        _walkActive = false;
        _waitingForActionTransition = false;
        _missingReferenceWarningLogged = false;
        _lastFacingDirectionSign = 0;
        _frontYawCaptured = false;
        _frontYaw = 0f;
        _blockedMoveFrames = 0;
        TestLog.Info(LogTag, "Started.");
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

        CaptureFrontYawIfNeeded();

        if (actionDecisionManager.CurrentAction != CharacterManager.GroundAction.Walk)
        {
            _walkActive = false;
            _waitingForActionTransition = false;
            _lastFacingDirectionSign = 0;
            _blockedMoveFrames = 0;
            return;
        }

        if (actionDecisionManager.AnimatorIsDragging)
        {
            if (_walkActive)
            {
                ApplyFrontFacingAfterWalk();
                TestLog.Info(LogTag, "Drag started during walk. Facing reset to front.");
            }

            _walkActive = false;
            _lastFacingDirectionSign = 0;
            _blockedMoveFrames = 0;
            return;
        }

        if (_waitingForActionTransition)
        {
            return;
        }

        if (!_walkActive)
        {
            if (!actionDecisionManager.ConsumeWalkStartRequestedByResetter())
            {
                return;
            }

            GetWalkRange(out float minX, out float maxX, out float currentReferenceX);
            _walkTargetScreenX = PickWalkTargetXByDistance(currentReferenceX, minX, maxX);
            _walkCurrentScreenX = currentReferenceX;
            _walkActive = true;
            TestLog.Info(LogTag, $"Walk started. fromX={currentReferenceX:0.###}, targetX={_walkTargetScreenX:0.###}, minX={minX:0.###}, maxX={maxX:0.###}");
        }

        float nextScreenX = Mathf.MoveTowards(
            _walkCurrentScreenX,
            _walkTargetScreenX,
            Mathf.Max(0f, walkSpeedPixelsPerSec) * deltaTime);
        float currentScreenX = _walkCurrentScreenX;
        float deltaScreenX = nextScreenX - currentScreenX;

        bool hasMoveIntent = Mathf.Abs(deltaScreenX) > 0.001f;
        bool moved = true;

        ApplyFacingFromMovement(deltaScreenX);

        if (windowMotionComponent != null)
        {
            moved = windowMotionComponent.MoveWindowByPixels(deltaScreenX, 0f);
        }

        if (!hasMoveIntent)
        {
            _blockedMoveFrames = 0;
            _walkCurrentScreenX = nextScreenX;
        }
        else if (moved)
        {
            _blockedMoveFrames = 0;
            _walkCurrentScreenX = nextScreenX;
        }
        else
        {
            _blockedMoveFrames++;
            int threshold = Mathf.Max(1, walkBlockedFramesToStop);
            if (_blockedMoveFrames >= threshold)
            {
                CompleteWalkMovement("edgeBlocked");
                return;
            }
        }

        float reachedDistance = Mathf.Abs(_walkTargetScreenX - _walkCurrentScreenX);

        if (reachedDistance <= Mathf.Max(0.1f, walkReachThresholdPixels))
        {
            CompleteWalkMovement("reachedTarget");
        }
    }

    public void OnTestStop()
    {
        TestLog.Info(LogTag, "Stopped.");
    }

    private bool TryResolveReferences()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (actionDecisionManager == null)
        {
            actionDecisionManager = GetComponent<TestActionDecisionManager>();
        }

        if (groundHeightSource == null)
        {
            groundHeightSource = GetComponent<TestGroundHeightSource>();
        }

        if (characterSpawnComponent == null)
        {
            characterSpawnComponent = GetComponent<TestCharacterSpawnComponent>();
        }

        if (windowMotionComponent == null)
        {
            windowMotionComponent = GetComponent<TestWindowMotionComponent>();
        }

        if (targetTransform == null && characterSpawnComponent != null && characterSpawnComponent.ModelContainer != null)
        {
            targetTransform = characterSpawnComponent.ModelContainer.transform;
        }

        return targetCamera != null && targetTransform != null && actionDecisionManager != null;
    }

    private float PickWalkTargetXByDistance(float currentX, float minX, float maxX)
    {
        if (Mathf.Approximately(minX, maxX))
        {
            return minX;
        }

        float minDistance = Mathf.Max(0f, walkMinDistancePixels);
        float maxDistance = Mathf.Max(minDistance, walkMaxDistancePixels);
        float randomDistance = Random.Range(minDistance, maxDistance);

        int direction = Random.value < 0.5f ? -1 : 1;
        float targetX = currentX + randomDistance * direction;
        targetX = Mathf.Clamp(targetX, minX, maxX);

        if (Mathf.Abs(targetX - currentX) < Mathf.Max(8f, minDistance * 0.5f))
        {
            int opposite = -direction;
            float oppositeTarget = Mathf.Clamp(currentX + randomDistance * opposite, minX, maxX);
            if (Mathf.Abs(oppositeTarget - currentX) > Mathf.Abs(targetX - currentX))
            {
                targetX = oppositeTarget;
            }
        }

        return targetX;
    }

    private void GetWalkRange(out float minX, out float maxX, out float currentX)
    {
        float padding = Mathf.Max(0f, walkScreenPaddingPixels);

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (WindowsAPI.TryGetCurrentWindowRect(out var rect) && WindowsAPI.TryGetWorkArea(out var workArea))
        {
            float workLeft = workArea.left;
            float workRight = workArea.right;
            minX = workLeft + padding;
            maxX = Mathf.Max(minX, workRight - padding);

            float windowCenterX = (rect.left + rect.right) * 0.5f;
            currentX = windowCenterX;
            currentX = Mathf.Clamp(currentX, minX, maxX);
            return;
        }
#endif

        if (ScreenSpaceTransformUtility.TryGetScreenPosition(targetCamera, targetTransform, out var modelScreen))
        {
            minX = padding;
            maxX = Mathf.Max(minX, Screen.width - padding);
            currentX = Mathf.Clamp(modelScreen.x, minX, maxX);
            return;
        }

        minX = padding;
        maxX = Mathf.Max(minX, Screen.width - padding);
        currentX = (minX + maxX) * 0.5f;
    }

    private void ApplyFacingFromMovement(float deltaWorldX)
    {
        if (!faceMovementDirection)
        {
            return;
        }

        if (Mathf.Abs(deltaWorldX) <= Mathf.Epsilon)
        {
            return;
        }

        int directionSign = deltaWorldX > 0f ? 1 : -1;
        if (_lastFacingDirectionSign == directionSign)
        {
            return;
        }

        _lastFacingDirectionSign = directionSign;
        Vector3 euler = targetTransform.rotation.eulerAngles;
        float yaw = directionSign > 0 ? faceRightYaw : faceLeftYaw;
        targetTransform.rotation = Quaternion.Euler(euler.x, yaw, euler.z);
        TestLog.Info(LogTag, $"Facing updated. direction={(directionSign > 0 ? "Right" : "Left")}, yaw={yaw:0.###}");
    }

    private void CaptureFrontYawIfNeeded()
    {
        if (_frontYawCaptured || targetTransform == null)
        {
            return;
        }

        _frontYaw = targetTransform.rotation.eulerAngles.y;
        _frontYawCaptured = true;
    }

    private void ApplyFrontFacingAfterWalk()
    {
        if (!faceFrontAfterWalk || !_frontYawCaptured || targetTransform == null)
        {
            return;
        }

        Vector3 euler = targetTransform.rotation.eulerAngles;
        targetTransform.rotation = Quaternion.Euler(euler.x, _frontYaw, euler.z);
        _lastFacingDirectionSign = 0;
        TestLog.Info(LogTag, $"Facing reset to front. yaw={_frontYaw:0.###}");
    }

    private void CompleteWalkMovement(string reason)
    {
        _walkActive = false;
        _waitingForActionTransition = true;
        _blockedMoveFrames = 0;
        ApplyFrontFacingAfterWalk();
        TestLog.Info(LogTag, $"Walk completed. reason={reason}, screenX={_walkCurrentScreenX:0.###}, targetX={_walkTargetScreenX:0.###}");
        actionDecisionManager.NotifyWalkMovementCompleted();
    }
}