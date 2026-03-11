using UnityEngine;
using System.Runtime.CompilerServices;

public class TestActionDecisionManager : MonoBehaviour, ITestFeatureComponent
{
    private const string LogTag = nameof(TestActionDecisionManager);
    private enum OffsetApplyMode
    {
        None = 0,
        Sit = 1,
        Ground = 2,
    }

    [SerializeField] private bool runTest = true;
    [SerializeField] private Animator targetAnimator;
    [SerializeField] private TestCharacterSpawnComponent characterSpawnComponent;
    [SerializeField] private bool enableAnimatorFlagLog = false;
    [SerializeField] private bool enableOffsetDiagnosticLog = true;
    [SerializeField] private bool enableOnWindowChangeLog = true;

    [Header("Action Duration")]
    [SerializeField] private float idleMinSeconds = 1.5f;
    [SerializeField] private float idleMaxSeconds = 3.5f;
    [SerializeField] private float sitMinSeconds = 2.0f;
    [SerializeField] private float sitMaxSeconds = 4.5f;

    [Header("Action Bias")]
    [SerializeField] private float idleBias = 1f;
    [SerializeField] private float sitBias = 1f;
    [SerializeField] private float walkBias = 1f;

    private AnimatorSync _animatorSync;
    private Animator _boundAnimator;
    private CharacterManager.GroundAction _currentAction = CharacterManager.GroundAction.Idle;
    private CharacterManager.GroundAction _pendingAction = CharacterManager.GroundAction.Idle;
    private bool _hasPendingAction;
    private float _currentActionRemainSeconds;
    private bool _waitingLandingAnimation;
    private bool _actionTimerActive;
    private bool _walkStartRequestedByResetter;

    private float _sitYOffset;
    private float _groundYOffset;
    private OffsetApplyMode _offsetApplyMode;
    private Transform _offsetTargetTransform;
    private bool _offsetBasePositionCaptured;
    private Vector3 _offsetBaseLocalPosition;
    private float _lastAppliedTransformYOffset;

    public bool IsTestEnabled => runTest;
    public bool AnimatorIsFalling => _animatorSync != null && _animatorSync.GetBool(Constant.AnimatorIsFalling);
    public bool AnimatorIsDragging => _animatorSync != null && _animatorSync.GetBool(Constant.AnimatorIsDragging);
    public bool AnimatorIsOnWindow => _animatorSync != null && _animatorSync.GetBool(Constant.AnimatorIsOnWindow);
    public bool IsGroundBehaviorActive => !_waitingLandingAnimation;
    public CharacterManager.GroundAction CurrentAction => _currentAction;

    public void OnTestStart()
    {
        if (!TryResolveAnimator())
        {
            return;
        }

        _currentAction = CharacterManager.GroundAction.Idle;
        _hasPendingAction = false;
        _waitingLandingAnimation = false;
        _actionTimerActive = false;
        _currentActionRemainSeconds = 0f;
        _walkStartRequestedByResetter = false;
        _offsetApplyMode = OffsetApplyMode.None;
        _offsetTargetTransform = null;
        _offsetBasePositionCaptured = false;
        _offsetBaseLocalPosition = Vector3.zero;
        _lastAppliedTransformYOffset = 0f;

        _animatorSync.SetFalling(false);
        _animatorSync.SetDragging(false);
        _animatorSync.SetNextGroundAction(_currentAction);
        _animatorSync.SetShouldGoNext(false);

        LogAnimatorFlags("initialized");
    }

    public void OnTestTick(float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return;
        }

        if (!TryResolveAnimator())
        {
            return;
        }

        ApplyYOffsetToCharacterTransform();

        if (_waitingLandingAnimation || AnimatorIsFalling)
        {
            return;
        }

        if (AnimatorIsDragging)
        {
            return;
        }

        if (IsOnWindowActive())
        {
            return;
        }

        if (!_actionTimerActive)
        {
            return;
        }

        if (_currentAction == CharacterManager.GroundAction.Walk)
        {
            return;
        }

        _currentActionRemainSeconds -= deltaTime;
        if (_currentActionRemainSeconds > 0f)
        {
            return;
        }

        EnsurePendingAction();
        if (_pendingAction == _currentAction)
        {
            _hasPendingAction = false;
            EnsurePendingAction();
            if (_pendingAction == _currentAction)
            {
                TestLog.Warning(LogTag, $"Skip ShouldGoNext request: pending equals current. action={_currentAction}");
                return;
            }
        }

        _animatorSync.SetShouldGoNext(true);
        _actionTimerActive = false;
        _currentActionRemainSeconds = 0f;
        LogAnimatorFlags($"shouldGoNextRequested current={_currentAction} pending={_pendingAction}");
    }

    public void OnTestStop()
    {
        ResetAppliedYOffsetOnStop();
        if (TryResolveAnimator())
        {
            LogAnimatorFlags("stopped");
        }
    }

    public bool ConsumeWalkStartRequestedByResetter()
    {
        if (!_walkStartRequestedByResetter)
        {
            return false;
        }

        _walkStartRequestedByResetter = false;
        return true;
    }

    public void SetFallingStateForTest(bool falling, string source = null, [CallerMemberName] string caller = "unknown")
    {
        if (!TryResolveAnimator())
        {
            return;
        }

        if (_animatorSync.GetBool(Constant.AnimatorIsFalling) == falling)
        {
            return;
        }

        _animatorSync.SetFalling(falling);
        if (falling)
        {
            _animatorSync.SetShouldGoNext(false);
            _actionTimerActive = false;
            _walkStartRequestedByResetter = false;
        }
    }

    public void SetDraggingStateForTest(bool dragging, string source = null, [CallerMemberName] string caller = "unknown")
    {
        if (!TryResolveAnimator())
        {
            return;
        }

        if (_animatorSync.GetBool(Constant.AnimatorIsDragging) == dragging)
        {
            return;
        }

        _animatorSync.SetDragging(dragging);
        if (dragging)
        {
            _animatorSync.SetShouldGoNext(false);
            _actionTimerActive = false;
        }

        LogAnimatorFlags($"draggingUpdated value={dragging}", source, caller);
    }

    public void ApplyPendingGroundAction()
    {
        if (!TryResolveAnimator())
        {
            return;
        }

        if (AnimatorIsOnWindow)
        {
            _currentAction = CharacterManager.GroundAction.Sit;
            _pendingAction = CharacterManager.GroundAction.Sit;
            _hasPendingAction = false;
            _currentActionRemainSeconds = 0f;
            _actionTimerActive = false;
            _animatorSync.SetShouldGoNext(false);
            LogAnimatorFlags("appliedAction onWindowDirect current=Sit");
            return;
        }

        if (!_hasPendingAction)
        {
            EnsurePendingAction();
        }

        if (_pendingAction == _currentAction)
        {
            _hasPendingAction = false;
            EnsurePendingAction();
        }

        var appliedAction = _pendingAction;
        _currentAction = appliedAction;
        _hasPendingAction = false;
        _currentActionRemainSeconds = PickActionDuration(_currentAction);
        _actionTimerActive = false;

        EnsurePendingAction();
        var nextAction = _pendingAction;

        _animatorSync.SetNextGroundAction(appliedAction);
        _animatorSync.SetShouldGoNext(false);
        LogAnimatorFlags($"appliedAction current={appliedAction} next={nextAction}");
    }

    public void NotifyWalkMovementCompleted()
    {
        if (!TryResolveAnimator())
        {
            return;
        }

        if (_currentAction != CharacterManager.GroundAction.Walk || _waitingLandingAnimation || AnimatorIsFalling)
        {
            return;
        }

        EnsurePendingAction();
        var pendingAction = _pendingAction;
        ApplyPendingGroundAction();
        _animatorSync.SetShouldGoNext(true);
        _actionTimerActive = false;
        _currentActionRemainSeconds = 0f;
        LogAnimatorFlags($"walkMovementCompleted pending={pendingAction}");
    }

    public void OnEnterGroundActionState()
    {
        if (!TryResolveAnimator())
        {
            TestLog.Warning(LogTag, "OnEnterGroundActionState skipped: animator not resolved.");
            return;
        }

        if (_waitingLandingAnimation)
        {
            TestLog.Warning(LogTag, "OnEnterGroundActionState skipped: waiting landing animation.");
            return;
        }

        _currentAction = GetCurrentGroundActionFromAnimatorFlags();

        if (IsOnWindowActive())
        {
            _currentAction = CharacterManager.GroundAction.Sit;
            _pendingAction = CharacterManager.GroundAction.Sit;
            _hasPendingAction = false;
            _actionTimerActive = false;
            _currentActionRemainSeconds = 0f;
            _animatorSync.SetShouldGoNext(false);
            LogAnimatorFlags("enterGroundAction onWindowDirectSit");
            return;
        }

        _actionTimerActive = true;
        _currentActionRemainSeconds = PickActionDuration(_currentAction);
        EnsurePendingAction();
        _animatorSync.SetNextGroundAction(_pendingAction);

        _animatorSync.SetShouldGoNext(false);

        if (_animatorSync.GetBool(Constant.AnimatorIsWalking))
        {
            _walkStartRequestedByResetter = true;
        }

        LogAnimatorFlags($"enterGroundAction current={_currentAction} pending={_pendingAction} duration={_currentActionRemainSeconds:0.###}");
    }

    public void OnEnterLandingAnimation()
    {
        if (!TryResolveAnimator())
        {
            return;
        }

        bool onWindowExpected = IsOnWindowActive();

        _waitingLandingAnimation = true;
        _actionTimerActive = false;
        _pendingAction = onWindowExpected
            ? CharacterManager.GroundAction.Sit
            : CharacterManager.GroundAction.Idle;
        _hasPendingAction = true;
        _currentActionRemainSeconds = 0f;
        if (!onWindowExpected)
        {
            _animatorSync.SetNextGroundAction(_pendingAction);
        }
        _animatorSync.SetShouldGoNext(false);
        LogAnimatorFlags("enterLanding");
    }

    public void OnExitLandingAnimation()
    {
        if (!TryResolveAnimator())
        {
            return;
        }

        _waitingLandingAnimation = false;
        bool onWindowAfterLanding = ApplyPendingWindowStatesAfterLanding();
        _actionTimerActive = false;

        if (onWindowAfterLanding)
        {
            _currentAction = CharacterManager.GroundAction.Sit;
            _pendingAction = CharacterManager.GroundAction.Sit;
            _hasPendingAction = false;
            _animatorSync.SetShouldGoNext(false);
            LogAnimatorFlags("exitLanding onWindowDirect");
            return;
        }

        _currentAction = CharacterManager.GroundAction.Idle;
        _pendingAction = CharacterManager.GroundAction.Idle;
        _hasPendingAction = false;
        _animatorSync.SetNextGroundAction(CharacterManager.GroundAction.Idle);
        _animatorSync.SetShouldGoNext(true);
        LogAnimatorFlags("exitLanding");
    }

    public void SetSitYOffset(float yOffsetRatio)
    {
        _sitYOffset = yOffsetRatio;
        _offsetApplyMode = OffsetApplyMode.Sit;
    }

    public void ResetSitYOffset()
    {
        _sitYOffset = 0f;
        if (_offsetApplyMode == OffsetApplyMode.Sit)
        {
            _offsetApplyMode = OffsetApplyMode.None;
        }
    }

    public void SetGroundYOffset(float yOffsetPixels)
    {
        _groundYOffset = yOffsetPixels;
        _offsetApplyMode = OffsetApplyMode.Ground;
    }

    public void ResetGroundYOffset()
    {
        _groundYOffset = 0f;
        if (_offsetApplyMode == OffsetApplyMode.Ground)
        {
            _offsetApplyMode = OffsetApplyMode.None;
        }
    }

    private void ApplyYOffsetToCharacterTransform()
    {
        if (!TryResolveOffsetTargetTransform())
        {
            return;
        }

        if (!_offsetBasePositionCaptured)
        {
            _offsetBaseLocalPosition = _offsetTargetTransform.localPosition;
            _offsetBasePositionCaptured = true;
            _lastAppliedTransformYOffset = 0f;
        }

        float targetYOffset = _offsetApplyMode switch
        {
            OffsetApplyMode.Sit => _sitYOffset,
            OffsetApplyMode.Ground => _groundYOffset,
            _ => 0f,
        };

        float scaleY = Mathf.Abs(_offsetTargetTransform.localScale.y);
        float scaledYOffset = targetYOffset * Mathf.Max(0.0001f, scaleY);

        if (Mathf.Abs(_lastAppliedTransformYOffset - scaledYOffset) <= 0.0001f)
        {
            return;
        }

        var local = _offsetBaseLocalPosition;
        local.y = _offsetBaseLocalPosition.y + scaledYOffset;
        _offsetTargetTransform.localPosition = local;
        _lastAppliedTransformYOffset = scaledYOffset;

        if (enableOffsetDiagnosticLog)
        {
            TestLog.Info(LogTag, $"TransformYOffsetApplied mode={_offsetApplyMode} offset={targetYOffset:0.###} scaleY={scaleY:0.###} scaledOffset={scaledYOffset:0.###} localY={local.y:0.###}");
        }
    }

    private bool TryResolveOffsetTargetTransform()
    {
        if (characterSpawnComponent == null)
        {
            characterSpawnComponent = GetComponent<TestCharacterSpawnComponent>();
        }

        Transform resolved = null;
        if (characterSpawnComponent != null && characterSpawnComponent.ModelContainer != null)
        {
            resolved = characterSpawnComponent.ModelContainer.transform;
        }
        else if (targetAnimator != null)
        {
            resolved = targetAnimator.transform;
        }

        if (resolved == null)
        {
            return false;
        }

        if (_offsetTargetTransform != resolved)
        {
            _offsetTargetTransform = resolved;
            _offsetBasePositionCaptured = false;
            _lastAppliedTransformYOffset = 0f;
        }

        return true;
    }

    private void ResetAppliedYOffsetOnStop()
    {
        if (_offsetTargetTransform != null && _offsetBasePositionCaptured)
        {
            _offsetTargetTransform.localPosition = _offsetBaseLocalPosition;
        }

        _offsetBasePositionCaptured = false;
        _lastAppliedTransformYOffset = 0f;
    }

    public void SetOnWindowStateForTest(bool onWindow, string source = null, [CallerMemberName] string caller = "unknown")
    {
        if (!TryResolveAnimator())
        {
            return;
        }

        bool currentOnWindow = _animatorSync.GetBool(Constant.AnimatorIsOnWindow);

        if (AnimatorIsDragging)
        {
            if (!onWindow && currentOnWindow)
            {
                _animatorSync.SetIsOnWindow(false);
                _animatorSync.SetIsSittingWindow(false);
                LogOnWindowChange(
                    phase: "dragStartForceFalse",
                    applied: true,
                    from: true,
                    to: false,
                    source: source,
                    caller: caller);
                return;
            }

            LogOnWindowChange(
                phase: "ignoredDragging",
                applied: false,
                from: currentOnWindow,
                to: onWindow,
                source: source,
                caller: caller);
            return;
        }

        bool currentSittingWindow = _animatorSync.GetBool(Constant.AnimatorIsSittingWindow);

        if (currentOnWindow == onWindow && (onWindow || !currentSittingWindow))
        {
            return;
        }

        _animatorSync.SetIsOnWindow(onWindow);
        if (!onWindow)
        {
            _animatorSync.SetIsSittingWindow(false);
        }

        LogOnWindowChange(
            phase: "immediate",
            applied: true,
            from: currentOnWindow,
            to: onWindow,
            source: source,
            caller: caller);

        LogAnimatorFlags($"setOnWindow onWindow={onWindow}", source, caller);
    }

    public void SetSittingWindowStateForTest(bool sittingWindow, string source = null, [CallerMemberName] string caller = "unknown")
    {
        if (!TryResolveAnimator())
        {
            return;
        }

        if (AnimatorIsDragging)
        {
            return;
        }

        bool onWindow = _animatorSync.GetBool(Constant.AnimatorIsOnWindow);
        bool applied = sittingWindow && onWindow;
        bool currentSittingWindow = _animatorSync.GetBool(Constant.AnimatorIsSittingWindow);

        if (currentSittingWindow == applied)
        {
            return;
        }

        _animatorSync.SetIsSittingWindow(applied);
        LogAnimatorFlags($"setSittingWindow sittingWindow={sittingWindow} applied={applied}", source, caller);
    }

    public void LogAnimatorFlagsFromExternal(string source, string message, [CallerMemberName] string caller = "unknown")
    {
        if (!TryResolveAnimator())
        {
            return;
        }

        LogAnimatorFlags(message, source, caller);
    }

    private bool ApplyPendingWindowStatesAfterLanding()
    {
        if (_animatorSync == null)
        {
            return false;
        }

        bool currentOnWindow = _animatorSync.GetBool(Constant.AnimatorIsOnWindow);
        bool onWindow = currentOnWindow;
        _animatorSync.SetIsOnWindow(onWindow);

        bool sitRequested = _animatorSync.GetBool(Constant.AnimatorIsSittingWindow);
        _animatorSync.SetIsSittingWindow(onWindow && sitRequested);

        LogAnimatorFlags($"applyPendingWindowStates onWindow={onWindow} sitRequested={sitRequested}");
        return onWindow;
    }

    private void LogOnWindowChange(string phase, bool applied, bool from, bool to, string source, string caller)
    {
        if (!enableOnWindowChangeLog)
        {
            return;
        }

        string callerLabel = string.IsNullOrEmpty(source) ? caller : $"{source}.{caller}";
        TestLog.Info(
            LogTag,
            $"IsOnWindowChange[{phase}] caller={callerLabel} applied={applied} {from} -> {to} waitingLanding={_waitingLandingAnimation} isFalling={AnimatorIsFalling}");
    }

    private void EnsurePendingAction()
    {
        if (_hasPendingAction && _pendingAction != _currentAction)
        {
            return;
        }

        _pendingAction = PickNextAction();
        _hasPendingAction = true;
    }

    private CharacterManager.GroundAction PickNextAction()
    {
        if (IsOnWindowActive())
        {
            return CharacterManager.GroundAction.Sit;
        }

        float idle = _currentAction == CharacterManager.GroundAction.Idle ? 0f : Mathf.Max(0f, idleBias);
        float sit = _currentAction == CharacterManager.GroundAction.Sit ? 0f : Mathf.Max(0f, sitBias);
        float walk = _currentAction == CharacterManager.GroundAction.Walk ? 0f : Mathf.Max(0f, walkBias);
        float total = idle + sit + walk;

        if (total <= 0f)
        {
            return PickDifferentActionFallback(_currentAction);
        }

        float roll = Random.Range(0f, total);
        if (roll < idle)
        {
            return CharacterManager.GroundAction.Idle;
        }

        roll -= idle;
        if (roll < sit)
        {
            return CharacterManager.GroundAction.Sit;
        }

        return CharacterManager.GroundAction.Walk;
    }

    private CharacterManager.GroundAction PickDifferentActionFallback(CharacterManager.GroundAction current)
    {
        if (current != CharacterManager.GroundAction.Idle)
        {
            return CharacterManager.GroundAction.Idle;
        }

        if (current != CharacterManager.GroundAction.Sit)
        {
            return CharacterManager.GroundAction.Sit;
        }

        return CharacterManager.GroundAction.Walk;
    }

    private CharacterManager.GroundAction GetCurrentGroundActionFromAnimatorFlags()
    {
        if (_animatorSync == null)
        {
            return _currentAction;
        }

        if (_animatorSync.GetBool(Constant.AnimatorIsWalking))
        {
            return CharacterManager.GroundAction.Walk;
        }

        if (_animatorSync.GetBool(Constant.AnimatorIsSitting))
        {
            return CharacterManager.GroundAction.Sit;
        }

        return CharacterManager.GroundAction.Idle;
    }

    private float PickActionDuration(CharacterManager.GroundAction action)
    {
        switch (action)
        {
            case CharacterManager.GroundAction.Sit:
                return Random.Range(Mathf.Max(0f, sitMinSeconds), Mathf.Max(sitMinSeconds, sitMaxSeconds));
            case CharacterManager.GroundAction.Idle:
                return Random.Range(Mathf.Max(0f, idleMinSeconds), Mathf.Max(idleMinSeconds, idleMaxSeconds));
            default:
                return 0f;
        }
    }

    private bool IsOnWindowActive()
    {
        return _animatorSync != null && _animatorSync.GetBool(Constant.AnimatorIsOnWindow);
    }

    private bool TryResolveAnimator()
    {
        if (targetAnimator == null)
        {
            if (characterSpawnComponent == null)
            {
                characterSpawnComponent = GetComponent<TestCharacterSpawnComponent>();
            }

            if (characterSpawnComponent != null)
            {
                targetAnimator = characterSpawnComponent.ModelAnimator;
            }

            if (targetAnimator == null)
            {
                targetAnimator = GetComponentInChildren<Animator>();
            }
        }

        if (targetAnimator == null)
        {
            return false;
        }

        if (_animatorSync == null || _boundAnimator != targetAnimator)
        {
            _animatorSync = new AnimatorSync(targetAnimator);
            _boundAnimator = targetAnimator;
        }

        return true;
    }

    private void LogAnimatorFlags(string message, [CallerMemberName] string caller = "unknown")
    {
        LogAnimatorFlags(message, null, caller);
    }

    private void LogAnimatorFlags(string message, string source, string caller)
    {
        if (!enableAnimatorFlagLog)
        {
            return;
        }

        if (_animatorSync == null)
        {
            return;
        }

        var callerLabel = string.IsNullOrEmpty(source) ? caller : $"{source}.{caller}";

        TestLog.Info(
            LogTag,
            $"[{callerLabel}] {message} | IsIdling={_animatorSync.GetBool(Constant.AnimatorIsIdling)} "
            + $"IsWalking={_animatorSync.GetBool(Constant.AnimatorIsWalking)} "
            + $"IsSitting={_animatorSync.GetBool(Constant.AnimatorIsSitting)} "
            + $"IsDragging={_animatorSync.GetBool(Constant.AnimatorIsDragging)} "
            + $"IsFalling={_animatorSync.GetBool(Constant.AnimatorIsFalling)} "
            + $"ShouldGoNext={_animatorSync.GetBool(Constant.AnimatorShouldGoNext)} "
            + $"IsOnWindow={_animatorSync.GetBool(Constant.AnimatorIsOnWindow)} "
            + $"IsSittingWindow={_animatorSync.GetBool(Constant.AnimatorIsSittingWindow)}");
    }
}