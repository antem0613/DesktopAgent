using Cysharp.Threading.Tasks;
using Kirurobo;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Localization.Settings;
using Unity.Logging;

public class CharacterManager : SingletonMonoBehaviour<CharacterManager>
{
    [Header("Animator")]
    [SerializeField] private bool createAnimatorIfMissing = true;
    [SerializeField] private bool createGenericAvatarIfMissing = true;

    [Header("Startup Order")]
    [SerializeField] private BackendManager backendManager;
    [SerializeField] private float backendPollIntervalSeconds = 0.1f;
    [SerializeField] private float backendWaitTimeoutSeconds = 6f;

    [Header("Startup Order / External UI")]
    [SerializeField] private float externalUiPollIntervalSeconds = 0.1f;
    [SerializeField] private float externalUiReadyExtraDelaySeconds = 0.15f;
    [SerializeField] private float externalUiWaitTimeoutSeconds = 1.5f;
    [SerializeField] private int uiLoadingUdpAckTimeoutMs = 120;
    [SerializeField] private int uiLoadingUdpRetryCount = 3;

    [Header("Action Duration")]
    [SerializeField] private float idleMaxSeconds = 3.5f;
    [SerializeField] private float idleMinSeconds = 1.5f;
    [SerializeField] private float sitMaxSeconds = 4.5f;
    [SerializeField] private float sitMinSeconds = 2.0f;

    [Header("Action Bias")]
    [SerializeField] private float idleBias = 1f;
    [SerializeField] private float sitBias = 1f;
    [SerializeField] private float walkBias = 1f;

    [Header("Window Exclusions")]
    [SerializeField] private float gravityPixelsPerSec2 = 2800f;
    [SerializeField] private float groundedEpsilonPixels = 0.5f;
    [SerializeField] private float initialFallSpeedPixelsPerSec = 0f;
    [SerializeField] private float maxFallSpeedPixelsPerSec = 6000f;
    [SerializeField] private int supportWindowLookupGraceFrames = 15;
    [SerializeField] private UniWindowMoveHandle uniWindowMoveHandle;

    [Header("Window Startup")]
    [SerializeField] private float mouseDeltaScaleMultiplier = 1f;
    [SerializeField] private int startWindowHeight = 600;
    [SerializeField] private int startWindowWidth = 800;

    [Header("Scroll Scale")]
    [SerializeField] private float maxScale = 3.0f;
    [SerializeField] private float minScale = 0.3f;
    [SerializeField] private float scrollScaleStep = 0.1f;
    [SerializeField] private int maxWindowHeight = 2160;
    [SerializeField] private int maxWindowWidth = 3840;
    [SerializeField] private int minWindowHeight = 240;
    [SerializeField] private int minWindowWidth = 320;

    [Header("Walk")]
    [SerializeField] private float walkMaxDistancePixels = 520f;
    [SerializeField] private float walkMinDistancePixels = 120f;
    [SerializeField] private float walkReachThresholdPixels = 2f;
    [SerializeField] private float walkScreenPaddingPixels = 120f;
    [SerializeField] private float walkSpeedPixelsPerSec = 160f;
    [SerializeField] private int walkBlockedFramesToStop = 2;

    [Header("Facing")]
    [SerializeField] private float faceLeftYaw = -90f;
    [SerializeField] private float faceRightYaw = 90f;

    public enum GroundAction
    {
        Idle = 0,
        Sit = 1,
        Walk = 2,
    }


    public Animator ModelAnimator => _modelAnimator;
    public GameObject ModelContainer => _modelContainer;
    public GroundAction CurrentAction => _currentAction;
    public LoadedVRMInfo CurrentVrmInfo => _loadedVrmInfo;
    public bool AnimatorIsDragging => _animatorSync != null && _animatorSync.GetBool(Constant.AnimatorIsDragging);
    public bool AnimatorIsFalling => _animatorSync != null && _animatorSync.GetBool(Constant.AnimatorIsFalling);
    public bool AnimatorIsOnWindow => _animatorSync != null && _animatorSync.GetBool(Constant.AnimatorIsOnWindow);
    public bool IsGrounded { get; private set; }

    private bool _isSpawning;
    private float _lastUiTopmostRequestAt = -1f;
    private string _lastUiFrontRequestLogToken;
    private string _lastClearFollowTargetLogToken;
    private CharacterZOrderHelper _zOrderHelper;

    private Animator _boundAnimator;
    private Animator _modelAnimator;
    private AnimatorSync _animatorSync;
    private Camera _targetCamera;
    private CancellationTokenSource _cancellationTokenSource;
    private GameObject _modelContainer;
    private GameObject _spawnedModel;
    private LoadedVRMInfo _loadedVrmInfo;
    private bool _isAnimatorInitialized;

    private bool _isActionTimerActive;
    private bool _hasPendingAction;
    private bool _isWaitingLandingAnimation;
    private bool _isWalkStartRequested;
    private float _currentActionRemainSeconds;
    private GroundAction _currentAction = GroundAction.Idle;
    private GroundAction _pendingAction = GroundAction.Idle;

    private bool _hasOffsetBasePositionCaptured;
    private float _groundYOffset;
    private float _lastAppliedTransformYOffset;
    private float _sitYOffset;
    private OffsetApplyMode _offsetApplyMode;
    private Transform _offsetTargetTransform;
    private Vector3 _offsetBaseLocalPosition;

    private FallRuntimeState _fallRuntime;
    private ZOrderRuntimeState _zOrderRuntime;
    private bool _wasGroundedLastState;
    private bool _hasVirtualScreenY;
    private bool _wasSupportFocusedLastFrame;
    private float _fallSpeedPixelsPerSec;
    private float _followingWindowOffsetX;
    private float _virtualScreenY;
    private GroundSurfaceType _lastGroundSurfaceType = GroundSurfaceType.None;
    private int _supportLookupFailFrames;
    private IntPtr _followingWindowHandle;
    private IntPtr _lastFocusedSupportHwnd;
    private WindowsAPI.RECT _lastFollowingWindowRect;

    private bool _hasEventScrollPending;
    private bool _hasEventRightClickPending;
    private bool _isInputRegistered;
    private bool _isWindowResizeBaselineReady;
    private float _pendingEventScroll;
    private float _windowResizeBaseAspect = 1f;
    private float _windowResizeBaseScale = 1f;
    private int _lastScrollHandledFrame = -1;
    private int _windowResizeBaseHeight;
    private int _windowResizeBaseWidth;
    private Vector2 _pendingEventScreenPosition;
    private Vector2 _pendingRightClickScreenPosition;

    private bool _hasFrontYawCaptured;
    private bool _isWalkActive;
    private bool _isWaitingForActionTransition;
    private float _frontYaw;
    private float _walkCurrentScreenX;
    private float _walkTargetScreenX;
    private int _blockedMoveFrames;
    private int _lastFacingDirectionSign;
    private bool _wasAnimatorResolvedLastLog;
    private bool _hasLogStateInitialized;
    private bool _wasGroundedLastLog;
    private bool _wasFallingLastLog;
    private bool _wasDraggingLastLog;
    private bool _wasWalkActiveLastLog;
    private GroundAction _logLastAction;

    private enum OffsetApplyMode
    {
        None = 0,
        Sit = 1,
        Ground = 2,
    }

    private enum GroundSurfaceType
    {
        None = 0,
        Taskbar = 1,
        WindowTop = 2,
    }

    private struct FallRuntimeState
    {
        public bool IsDragDrivenByFall;
        public bool IsGravityPausedByDrag;
        public bool HasFollowingWindowRect;
    }

    private struct ZOrderRuntimeState
    {
        public bool IsTopmostPinnedByDrag;
        public bool IsTopmostPinnedBySupport;
        public bool HasFollowPlaceLogState;
        public bool WasLastFollowPlaceSuccess;
    }

    private struct GroundProbeResult
    {
        public float SignedGapPixels;
        public GroundSurfaceType SurfaceType;
        public float WindowBottom;
        public float GroundSupportY;
        public IntPtr SupportWindowHandle;
        public WindowsAPI.RECT SupportWindowRect;
    }

    // 起動時に必要なリソースを初期化し、Zオーダー補助を準備する。
    private protected override void Awake()
    {
        base.Awake();
        _cancellationTokenSource = new CancellationTokenSource();
        _zOrderHelper = new CharacterZOrderHelper(this);
        Log.Info("[CharacterManager] Awake completed.");
    }

    // 起動時の状態を初期化し、キャラクター生成を開始する。
    private void Start()
    {
        Log.Info("[CharacterManager] Start begin.");
        SpawnCharacter();

        if (!TryResolveAnimator())
        {
            Log.Warning("[CharacterManager] Start deferred: animator is not ready yet.");
            return;
        }

        InitializeAnimatorSync();

        _lastScrollHandledFrame = -1;
        _hasEventScrollPending = false;
        _hasEventRightClickPending = false;

        _pendingEventScroll = 0f;
        _pendingEventScreenPosition = Vector2.zero;
        _pendingRightClickScreenPosition = Vector2.zero;
        _isWindowResizeBaselineReady = false;
        _windowResizeBaseScale = 1f;
        _windowResizeBaseWidth = 0;
        _windowResizeBaseHeight = 0;
        _windowResizeBaseAspect = 1f;
        RegisterInputEvents();

        _currentAction = GroundAction.Idle;
        _hasPendingAction = false;
        _isWaitingLandingAnimation = false;
        _isActionTimerActive = false;
        _currentActionRemainSeconds = 0f;
        _isWalkStartRequested = false;
        _offsetApplyMode = OffsetApplyMode.None;
        _offsetTargetTransform = null;
        _hasOffsetBasePositionCaptured = false;
        _offsetBaseLocalPosition = Vector3.zero;
        _lastAppliedTransformYOffset = 0f;

        _animatorSync.SetFalling(false);
        _animatorSync.SetDragging(false);
        _animatorSync.SetNextGroundAction(_currentAction);
        _animatorSync.SetShouldGoNext(false);

        _fallSpeedPixelsPerSec = Mathf.Max(0f, initialFallSpeedPixelsPerSec);
        IsGrounded = false;
        _wasGroundedLastState = IsGrounded;
        _virtualScreenY = 0f;
        _hasVirtualScreenY = false;
        _fallRuntime.IsGravityPausedByDrag = false;
        _fallRuntime.IsDragDrivenByFall = false;
        _lastGroundSurfaceType = GroundSurfaceType.None;
        _followingWindowHandle = IntPtr.Zero;
        _lastFollowingWindowRect = default;
        _fallRuntime.HasFollowingWindowRect = false;
        _followingWindowOffsetX = 0f;
        _wasSupportFocusedLastFrame = false;
        _lastFocusedSupportHwnd = IntPtr.Zero;
        _zOrderRuntime.IsTopmostPinnedBySupport = false;
        _supportLookupFailFrames = 0;
        SyncWindowSurfaceState(false);
        SetDraggingState(false);
        SetFallingState(false);

        _isWalkActive = false;
        _isWaitingForActionTransition = false;
        _lastFacingDirectionSign = 0;
        _hasFrontYawCaptured = false;
        _frontYaw = 0f;
        _blockedMoveFrames = 0;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        int width = Mathf.Clamp(startWindowWidth, 320, 1920);
        int height = Mathf.Clamp(startWindowHeight, 240, 1440);
        int x = Mathf.Max(0, (Screen.currentResolution.width - width) / 2);
        int y = Mathf.Max(0, (Screen.currentResolution.height - height) / 2);
        bool isWindowRectApplied = WindowsAPI.SetCurrentWindowRect(x, y, width, height);
#endif

        Log.Info("[CharacterManager] Start initialization completed.");
    }

    // 毎フレームの行動、落下、スクロール処理を更新する。
    void Update()
    {
        bool isAnimatorResolved = TryResolveAnimator();
        if (!isAnimatorResolved)
        {
           if (_wasAnimatorResolvedLastLog)
           {
               Log.Warning("[CharacterManager] Animator became unavailable.");
               _wasAnimatorResolvedLastLog = false;
           }

           return;
        }

        if (!_wasAnimatorResolvedLastLog)
        {
            _wasAnimatorResolvedLastLog = true;
            Log.Info("[CharacterManager] Animator resolved and update loop is active.");
        }

        if(!_isAnimatorInitialized)
        {
            InitializeAnimatorSync();
            if (!_isAnimatorInitialized)
            {
                return;
            }
        }

        ActionDecisionUpdate();

        WalkUpdate();

        FallingUpdate();

        ScrollUpdate();

        RightClickMenuUpdate();

        LogRuntimeStateChanges();
    }

    // アプリ終了時に非同期処理と入力購読を停止する。
    public void OnApplicationQuit()
    {
        Log.Info("[CharacterManager] Application quit requested. Canceling tasks and unregistering inputs.");
        _cancellationTokenSource?.Cancel();
        UnregisterInputEvents();
    }

    // 破棄時にキャンセルトークンと購読を解放する。
    private void OnDestroy()
    {
        Log.Info("[CharacterManager] OnDestroy called. Releasing resources.");
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    // キャラクター生成を非同期で開始する。
    public bool SpawnCharacter()
    {
        if (_isSpawning)
        {
            Log.Info("[CharacterManager] Spawn request skipped: spawn is already in progress.");
            return false;
        }

        Log.Info("[CharacterManager] Spawn request accepted.");
        SpawnCharacterAsync().Forget();
        return true;
    }

    // 現在のAnimatorに同期ヘルパを結び直す。
    public bool InitializeAnimatorSync()
    {
        _isAnimatorInitialized = false;

        if (ModelAnimator == null)
        {
            _animatorSync = null;

            return false;
        }

        LoadVRM.UpdateAnimationController(ModelAnimator);

        _isAnimatorInitialized = true;
        Log.Info("[CharacterManager] Animator sync initialized.");
        return true;
    }

    // 生成済みモデルと関連参照を破棄する。
    public void ClearSpawnedCharacter()
    {
        if (_modelContainer != null)
        {
            Destroy(_modelContainer);
        }

        _modelContainer = null;
        _spawnedModel = null;
        _modelAnimator = null;
        _loadedVrmInfo = null;
    }

    // 歩行開始要求を一度だけ消費する。
    public bool ConsumeWalkStart()
    {
        if (!_isWalkStartRequested)
        {
            return false;
        }

        _isWalkStartRequested = false;
        return true;
    }

    // 落下状態を更新し、必要なAnimatorフラグも合わせて整える。
    public void SetFallingState(bool falling, string source = null, [CallerMemberName] string caller = "unknown")
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
            _isActionTimerActive = false;
            _isWalkStartRequested = false;
        }
    }

    // ドラッグ状態を更新し、前面制御も切り替える。
    public void SetDraggingState(bool dragging, string source = null, [CallerMemberName] string caller = "unknown")
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
            _isActionTimerActive = false;
            EnsureCharacterFrontOnDragStart();
        }
        else
        {
            ReleaseCharacterFrontAfterDragEnd();
        }
    }

    // ドラッグ開始時にキャラクターを前面へ出す。
    private void EnsureCharacterFrontOnDragStart()
    {
        _zOrderHelper?.EnsureCharacterFrontOnDragStart();
    }

    // ドラッグ終了時に前面固定を解除する。
    private void ReleaseCharacterFrontAfterDragEnd()
    {
        _zOrderHelper?.ReleaseCharacterFrontAfterDragEnd();
    }

    // 保留中の地上アクションを確定し、Animatorへ反映する。
    public void ApplyPendingGroundAction()
    {
        if (!TryResolveAnimator())
        {
                return;
        }

        if (AnimatorIsOnWindow)
        {
            _currentAction = GroundAction.Sit;
            _pendingAction = GroundAction.Sit;
            _hasPendingAction = false;
            _currentActionRemainSeconds = 0f;
            _isActionTimerActive = false;
            _animatorSync.SetShouldGoNext(false);
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
        _isActionTimerActive = false;

        EnsurePendingAction();
        var nextAction = _pendingAction;

        _animatorSync.SetNextGroundAction(appliedAction);
        _animatorSync.SetShouldGoNext(false);
    }

    // 歩行移動の完了を受けて次のアクションへ進める。
    public void NotifyWalkMovementCompleted()
    {
        if (!TryResolveAnimator())
        {
            return;
        }

        if (_currentAction != GroundAction.Walk || _isWaitingLandingAnimation || AnimatorIsFalling)
        {
            return;
        }

        EnsurePendingAction();
        var pendingAction = _pendingAction;
        ApplyPendingGroundAction();
        _animatorSync.SetShouldGoNext(true);
        _isActionTimerActive = false;
        _currentActionRemainSeconds = 0f;
    }

    // 地上アクション状態に入った直後のタイマーと次アクションを初期化する。
    public void OnEnterGroundActionState()
    {
        if (!TryResolveAnimator())
        {
            return;
        }

        if (_isWaitingLandingAnimation)
        {
            return;
        }

        _currentAction = GetCurrentGroundAction();

        if (AnimatorIsOnWindow)
        {
            _currentAction = GroundAction.Sit;
            _pendingAction = GroundAction.Sit;
            _hasPendingAction = false;
            _isActionTimerActive = false;
            _currentActionRemainSeconds = 0f;
            _animatorSync.SetShouldGoNext(false);
            return;
        }

        _isActionTimerActive = true;
        _currentActionRemainSeconds = PickActionDuration(_currentAction);
        EnsurePendingAction();
        _animatorSync.SetNextGroundAction(_pendingAction);

        _animatorSync.SetShouldGoNext(false);

        if (_animatorSync.GetBool(Constant.AnimatorIsWalking))
        {
            _isWalkStartRequested = true;
        }
    }

    // 着地アニメーション開始時の保留アクションを設定する。
    public void OnEnterLandingAnimation()
    {
        if (!TryResolveAnimator())
        {
            return;
        }

        bool isOnWindowExpected = AnimatorIsOnWindow;

        _isWaitingLandingAnimation = true;
        _isActionTimerActive = false;
        _pendingAction = isOnWindowExpected
            ? GroundAction.Sit
            : GroundAction.Idle;
        _hasPendingAction = true;
        _currentActionRemainSeconds = 0f;
        if (!isOnWindowExpected)
        {
            _animatorSync.SetNextGroundAction(_pendingAction);
        }
        _animatorSync.SetShouldGoNext(false);
    }

    // 着地アニメーション終了後の地上状態を確定する。
    public void OnExitLandingAnimation()
    {
        if (!TryResolveAnimator())
        {
            return;
        }

        _isWaitingLandingAnimation = false;
        bool isOnWindowAfterLanding = false;
        if (_animatorSync != null)
        {
            bool isCurrentOnWindow = _animatorSync.GetBool(Constant.AnimatorIsOnWindow);
            isOnWindowAfterLanding = isCurrentOnWindow;
            _animatorSync.SetIsOnWindow(isCurrentOnWindow);

            bool isSitRequested = _animatorSync.GetBool(Constant.AnimatorIsSittingWindow);
            _animatorSync.SetIsSittingWindow(isCurrentOnWindow && isSitRequested);
        }
        _isActionTimerActive = false;

        if (isOnWindowAfterLanding)
        {
            _currentAction = GroundAction.Sit;
            _pendingAction = GroundAction.Sit;
            _hasPendingAction = false;
            _animatorSync.SetShouldGoNext(false);
            return;
        }

        _currentAction = GroundAction.Idle;
        _pendingAction = GroundAction.Idle;
        _hasPendingAction = false;
        _animatorSync.SetNextGroundAction(GroundAction.Idle);
        _animatorSync.SetShouldGoNext(true);
    }

    // 座り時のYオフセットを設定する。
    public void SetSitYOffset(float yOffsetRatio)
    {
        _sitYOffset = yOffsetRatio;
        _offsetApplyMode = OffsetApplyMode.Sit;
    }

    // 座り時のYオフセットを解除する。
    public void ResetSitYOffset()
    {
        _sitYOffset = 0f;
        if (_offsetApplyMode == OffsetApplyMode.Sit)
        {
            _offsetApplyMode = OffsetApplyMode.None;
        }
    }

    // ウィンドウ上にいるかどうかの状態を同期する。
    public void SetOnWindowState(bool onWindow, string source = null, [CallerMemberName] string caller = "unknown")
    {
        if (!TryResolveAnimator())
        {
            return;
        }

        bool isCurrentOnWindow = _animatorSync.GetBool(Constant.AnimatorIsOnWindow);

        if (AnimatorIsDragging)
        {
            if (!onWindow && isCurrentOnWindow)
            {
                _animatorSync.SetIsOnWindow(false);
                _animatorSync.SetIsSittingWindow(false);
                return;
            }

            return;
        }

        bool isCurrentSittingWindow = _animatorSync.GetBool(Constant.AnimatorIsSittingWindow);

        if (isCurrentOnWindow == onWindow && (onWindow || !isCurrentSittingWindow))
        {
            return;
        }

        _animatorSync.SetIsOnWindow(onWindow);
        if (!onWindow)
        {
            _animatorSync.SetIsSittingWindow(false);
        }
    }

    // ウィンドウ上で座る状態を同期する。
    public void SetSittingWindowState(bool sittingWindow, string source = null, [CallerMemberName] string caller = "unknown")
    {
        if (!TryResolveAnimator())
        {
            return;
        }

        if (AnimatorIsDragging)
        {
            return;
        }

        bool isOnWindow = _animatorSync.GetBool(Constant.AnimatorIsOnWindow);
        bool isApplied = sittingWindow && isOnWindow;
        bool isCurrentSittingWindow = _animatorSync.GetBool(Constant.AnimatorIsSittingWindow);

        if (isCurrentSittingWindow == isApplied)
        {
            return;
        }

        _animatorSync.SetIsSittingWindow(isApplied);
    }

    // 落下速度と接地状態を初期化する。
    public void ResetFallState(float newFallSpeedPixelsPerSec = 0f, bool grounded = false)
    {
        _fallSpeedPixelsPerSec = Mathf.Max(0f, newFallSpeedPixelsPerSec);
        IsGrounded = grounded;
        SetFallingState(!grounded);
    }

    // 現在の状態に応じて次の地上アクション候補を保持する。
    private void EnsurePendingAction()
    {
        if (_hasPendingAction && _pendingAction != _currentAction)
        {
            return;
        }

        _pendingAction = PickNextAction();
        _hasPendingAction = true;
    }

    // 重み付けと現在状態から次の地上アクションを選ぶ。
    private GroundAction PickNextAction()
    {
        if (AnimatorIsOnWindow)
        {
            return GroundAction.Sit;
        }

        float idle = _currentAction == GroundAction.Idle ? 0f : Mathf.Max(0f, idleBias);
        float sit = _currentAction == GroundAction.Sit ? 0f : Mathf.Max(0f, sitBias);
        float walk = _currentAction == GroundAction.Walk ? 0f : Mathf.Max(0f, walkBias);
        float total = idle + sit + walk;

        if (total <= 0f)
        {
            // 重みの合計が0なら現在と異なる行動へフォールバックする。
            return PickDifferentActionFallback(_currentAction);
        }

        float roll = UnityEngine.Random.Range(0f, total);
        if (roll < idle)
        {
            return GroundAction.Idle;
        }

        roll -= idle;
        if (roll < sit)
        {
            return GroundAction.Sit;
        }

        return GroundAction.Walk;
    }

    // 現在と異なるアクションを最低限返すフォールバック。
    private GroundAction PickDifferentActionFallback(GroundAction current)
    {
        if (current != GroundAction.Idle)
        {
            return GroundAction.Idle;
        }

        if (current != GroundAction.Sit)
        {
            return GroundAction.Sit;
        }

        return GroundAction.Walk;
    }

    // Animatorのパラメータから現在の地上アクションを推定する。
    private GroundAction GetCurrentGroundAction()
    {
        if (_animatorSync == null)
        {
            return _currentAction;
        }

        if (_animatorSync.GetBool(Constant.AnimatorIsWalking))
        {
            return GroundAction.Walk;
        }

        if (_animatorSync.GetBool(Constant.AnimatorIsSitting))
        {
            return GroundAction.Sit;
        }

        return GroundAction.Idle;
    }

    // 指定アクションの継続時間を設定範囲から決める。
    private float PickActionDuration(GroundAction action)
    {
        switch (action)
        {
            case GroundAction.Sit:
                return UnityEngine.Random.Range(Mathf.Max(0f, sitMinSeconds), Mathf.Max(sitMinSeconds, sitMaxSeconds));
            case GroundAction.Idle:
                return UnityEngine.Random.Range(Mathf.Max(0f, idleMinSeconds), Mathf.Max(idleMinSeconds, idleMaxSeconds));
            default:
                return 0f;
        }
    }

    // Animator参照を解決し、必要ならAnimatorSyncを再作成する。
    private bool TryResolveAnimator()
    {
        if (ModelAnimator == null)
        {
            return false;
        }

        if (_animatorSync == null || _boundAnimator != ModelAnimator)
        {
            _animatorSync = new AnimatorSync(ModelAnimator);
            _boundAnimator = ModelAnimator;
        }

        return true;
    }

    // モデル読み込みから初期設定までの生成処理を非同期で進める。
    private async UniTaskVoid SpawnCharacterAsync()
    {
        _isSpawning = true;
        Log.Info("[CharacterManager] SpawnCharacterAsync started.");
        try
        {
            WaitForBackend(_cancellationTokenSource.Token).Forget();
            await WaitForUiProcess(_cancellationTokenSource.Token);
            await RequestUiLoadingVisibilityAsync(true, _cancellationTokenSource.Token);

            if (!TryResolveReferences())
            {
                return;
            }

            ClearSpawnedCharacter();

            var loadedModelInfo = await LoadModel(_cancellationTokenSource.Token);
            if (loadedModelInfo == null || loadedModelInfo.Model == null)
            {
                Log.Error("[CharacterManager] Model load failed: loaded model is null.");
                return;
            }

            _loadedVrmInfo = loadedModelInfo;
            _spawnedModel = loadedModelInfo.Model;
            _spawnedModel.name = _spawnedModel.name + "_Model";

            _modelContainer = new GameObject("ModelContainer");
            _modelContainer.transform.SetParent(null, false);

            _spawnedModel.transform.SetParent(_modelContainer.transform, false);

            var cameraTransform = _targetCamera.transform;
            var cameraPosition = new Vector3(cameraTransform.position.x, 0, cameraTransform.position.z);
            _modelContainer.transform.LookAt(cameraPosition, Vector3.up);

            ApplyCharacterSettings();

            _modelContainer.transform.rotation = Quaternion.Euler(_modelContainer.transform.rotation.eulerAngles);

            _modelAnimator = _modelContainer.GetComponentInChildren<Animator>();
            if (_modelAnimator == null && createAnimatorIfMissing)
            {
                _modelAnimator = _spawnedModel.AddComponent<Animator>();
            }

            if (_modelAnimator != null && createGenericAvatarIfMissing && _modelAnimator.avatar == null)
            {
                var generatedAvatar = CreateAvatarFromModel(_spawnedModel);
                if (generatedAvatar != null)
                {
                    _modelAnimator.avatar = generatedAvatar;
                }
            }

            Log.Info($"[CharacterManager] Character spawn completed. model={_spawnedModel.name}, animator={(_modelAnimator != null)}");

        } catch (System.OperationCanceledException)
        {
            Log.Warning("[CharacterManager] SpawnCharacterAsync canceled.");
        } finally
        {
            await RequestUiLoadingVisibilityAsync(false, CancellationToken.None);
            _isSpawning = false;
            Log.Info("[CharacterManager] SpawnCharacterAsync finished.");
        }
    }

    // 状態変化があったときだけ実行時ログを出す。
    private void LogRuntimeStateChanges()
    {
        bool isGrounded = IsGrounded;
        bool isFalling = AnimatorIsFalling;
        bool isDragging = AnimatorIsDragging;
        bool isWalkActiveNow = _isWalkActive;
        GroundAction action = _currentAction;

        if (!_hasLogStateInitialized)
        {
            _hasLogStateInitialized = true;
            _wasGroundedLastLog = isGrounded;
            _wasFallingLastLog = isFalling;
            _wasDraggingLastLog = isDragging;
            _wasWalkActiveLastLog = isWalkActiveNow;
            _logLastAction = action;
            Log.Info($"[CharacterManager] State initialized. grounded={isGrounded}, falling={isFalling}, dragging={isDragging}, walkActive={isWalkActiveNow}, action={action}");
            return;
        }

        if (_wasGroundedLastLog != isGrounded)
        {
            _wasGroundedLastLog = isGrounded;
            Log.Info($"[CharacterManager] Grounded changed: {isGrounded}");
        }

        if (_wasFallingLastLog != isFalling)
        {
            _wasFallingLastLog = isFalling;
            Log.Info($"[CharacterManager] Falling changed: {isFalling}");
        }

        if (_wasDraggingLastLog != isDragging)
        {
            _wasDraggingLastLog = isDragging;
            Log.Info($"[CharacterManager] Dragging changed: {isDragging}");
        }

        if (_wasWalkActiveLastLog != isWalkActiveNow)
        {
            _wasWalkActiveLastLog = isWalkActiveNow;
            Log.Info($"[CharacterManager] Walk active changed: {isWalkActiveNow}");
        }

        if (_logLastAction != action)
        {
            _logLastAction = action;
            Log.Info($"[CharacterManager] Action changed: {action}");
        }
    }

    // バックエンドが応答可能になるまで待機する。
    private async UniTaskVoid WaitForBackend(CancellationToken cancellationToken)
    {
        if (backendManager == null)
        {
            backendManager = GetComponent<BackendManager>();
            if (backendManager == null)
            {
                backendManager = FindFirstObjectByType<BackendManager>();
            }
        }

        if (backendManager == null)
        {
            return;
        }

        backendManager.StartBackend();

        float timeout = Mathf.Max(0.1f, backendWaitTimeoutSeconds);
        float poll = Mathf.Max(0.02f, backendPollIntervalSeconds);
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (backendManager.IsBackendAlive())
            {
                return;
            }

            int delayMs = Mathf.RoundToInt(poll * 1000f);
            await UniTask.Delay(delayMs, cancellationToken: cancellationToken);
            elapsed += poll;
        }
    }

    // 外部UIプロセスの起動完了を待ってから処理を進める。
    private async UniTask WaitForUiProcess(CancellationToken cancellationToken)
    {
        float timeout = Mathf.Max(0.1f, externalUiWaitTimeoutSeconds);
        float poll = Mathf.Max(0.02f, externalUiPollIntervalSeconds);
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsUiProcessRunning())
            {
                float extraDelay = Mathf.Max(0f, externalUiReadyExtraDelaySeconds);
                if (extraDelay > 0f)
                {
                    int extraDelayMs = Mathf.RoundToInt(extraDelay * 1000f);
                    await UniTask.Delay(extraDelayMs, cancellationToken: cancellationToken);
                }

                return;
            }

            int delayMs = Mathf.RoundToInt(poll * 1000f);
            await UniTask.Delay(delayMs, cancellationToken: cancellationToken);
            elapsed += poll;
        }

        Log.Warning($"[CharacterManager] External UI wait timed out ({timeout:0.##}s). Continue spawning character.");
    }

    private async UniTask RequestUiLoadingVisibilityAsync(bool visible, CancellationToken cancellationToken)
    {
        if (IsUiProcessCommandLine())
        {
            return;
        }

        string message = visible ? Constant.UILoadingShowMessage : Constant.UILoadingHideMessage;
        string ack = Constant.UILoadingAckMessage;
        int timeoutMs = Mathf.Max(50, uiLoadingUdpAckTimeoutMs);
        int retries = Mathf.Max(1, uiLoadingUdpRetryCount);

        for (int attempt = 1; attempt <= retries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var client = new UdpClient();
                client.Client.ReceiveTimeout = timeoutMs;
                client.Client.SendTimeout = timeoutMs;

                byte[] payload = Encoding.UTF8.GetBytes(message);
                client.Send(payload, payload.Length, Constant.BackendHost, Constant.UIHealthCheckUdpPort);

                IPEndPoint remote = null;
                byte[] response = client.Receive(ref remote);
                if (response != null && response.Length > 0)
                {
                    string responseText = Encoding.UTF8.GetString(response).Trim();
                    if (string.Equals(responseText, ack, StringComparison.Ordinal))
                    {
                        Log.Info($"[CharacterManager] UI loading visibility requested. visible={visible}, attempt={attempt}");
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt >= retries)
                {
                    Log.Warning($"[CharacterManager] Failed to request UI loading visibility. visible={visible}, attempts={attempt}, error={ex.Message}");
                    return;
                }
            }

            if (attempt < retries)
            {
                await UniTask.Delay(timeoutMs, cancellationToken: cancellationToken);
            }
        }
    }

    private static bool IsUiProcessCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], Constant.UIProcessArgument, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // 設定に一致する外部UIプロセスが動作中か調べる。
    private bool IsUiProcessRunning()
    {
        string processName = (Constant.UIProcessName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        string normalizedTargetPath = string.Empty;
        if (!string.IsNullOrWhiteSpace(Constant.UIProcessExecutablePath))
        {
            try
            {
                if (Path.IsPathRooted(Constant.UIProcessExecutablePath))
                {
                    normalizedTargetPath = NormalizePath(Constant.UIProcessExecutablePath);
                }
                else
                {
                    string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
                    string combined = Path.Combine(projectRoot, Constant.UIProcessExecutablePath);
                    if (File.Exists(combined))
                    {
                        normalizedTargetPath = NormalizePath(combined);
                    }
                }
            } catch
            {
                normalizedTargetPath = string.Empty;
            }
        }

        try
        {
            var processes = Process.GetProcessesByName(processName);
            for (int i = 0; i < processes.Length; i++)
            {
                var process = processes[i];
                if (!IsProcessAlive(process))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(normalizedTargetPath))
                {
                    return true;
                }

                try
                {
                    string processPath = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(processPath))
                    {
                        return true;
                    }

                    processPath = NormalizePath(processPath);
                    if (string.Equals(processPath, normalizedTargetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                } catch
                {
                    // MainModule取得失敗はよくあるため、プロセス名一致だけで起動済みとみなす。
                    return true;
                }
            }
        } catch
        {
            return false;
        }

        return false;
    }

    // Processがまだ終了していないか安全に確認する。
    private static bool IsProcessAlive(Process process)
    {
        if (process == null)
        {
            return false;
        }

        try
        {
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    // 比較しやすいようにパスを正規化する。
    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).Replace('/', '\\');
    }

    // 設定に従ってキャラクターモデルを読み込む。
    private async UniTask<LoadedVRMInfo> LoadModel(CancellationToken cancellationToken)
    {
        var characterSettings = ApplicationSettings.Instance.Character;
        string originalModelPath = characterSettings.ModelPath;

        return await LoadCharacterModel.LoadModel(cancellationToken);
    }

    // 読み込んだモデルへ位置、回転、拡大率を反映する。
    private void ApplyCharacterSettings()
    {
        if (_modelContainer == null)
        {
            return;
        }

        var characterSettings = ApplicationSettings.Instance.Character;

        float configuredScale = Mathf.Clamp(
            characterSettings.Scale,
            Mathf.Min(minScale, maxScale),
            Mathf.Max(minScale, maxScale));

        _modelContainer.transform.localScale = Vector3.one * configuredScale;
        characterSettings.Scale = configuredScale;
        _modelContainer.transform.position += new Vector3(
            characterSettings.PositionX,
            characterSettings.PositionY,
            0);

        Vector3 currentRotation = _modelContainer.transform.rotation.eulerAngles;
        _modelContainer.transform.rotation = Quaternion.Euler(
            currentRotation.x + characterSettings.RotationX,
            currentRotation.y + characterSettings.RotationY,
            currentRotation.z + characterSettings.RotationZ);

        TryResizeWindowForScale(configuredScale);
    }

    // UI操作でキャラクター拡大率を変更し、必要に応じて設定保存する。
    public void SetCharacterScale(float value, bool saveSettings = true)
    {
        float nextScale = Mathf.Clamp(value, Mathf.Min(minScale, maxScale), Mathf.Max(minScale, maxScale));

        if (_modelContainer != null)
        {
            _modelContainer.transform.localScale = Vector3.one * nextScale;
            TryResizeWindowForScale(nextScale);
        }

        var appSettings = ApplicationSettings.Instance;
        if (appSettings?.Character != null)
        {
            appSettings.Character.Scale = nextScale;
            if (saveSettings)
            {
                appSettings.SaveSettings();
            }
        }
    }

    // モデルから利用可能なAvatarを取得し、必要なら生成する。
    private static Avatar CreateAvatarFromModel(GameObject model)
    {
        if (model == null)
        {
            return null;
        }

        var animator = model.GetComponent<Animator>();
        if (animator != null && animator.avatar != null)
        {
            return animator.avatar;
        }

        var skinnedMeshRenderer = model.GetComponentInChildren<SkinnedMeshRenderer>();
        if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null)
        {
            var avatar = AvatarBuilder.BuildGenericAvatar(model, "");
            if (avatar != null)
            {
                avatar.name = model.name + "_Avatar";
            }

            return avatar;
        }

        return null;
    }

    // 現在のオフセット設定をモデルのTransformへ反映する。
    private void ApplyCharacterYOffset()
    {
        if (!TryInitOffsetTarget())
        {
            return;
        }

        if (!_hasOffsetBasePositionCaptured)
        {
            _offsetBaseLocalPosition = _offsetTargetTransform.localPosition;
            _hasOffsetBasePositionCaptured = true;
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
    }

    // オフセット適用対象のTransformを解決して基準値を更新する。
    private bool TryInitOffsetTarget()
    {
        Transform resolved = null;
        if (ModelContainer != null)
        {
            resolved = ModelContainer.transform;
        }

        if (resolved == null)
        {
            return false;
        }

        if (_offsetTargetTransform != resolved)
        {
            _offsetTargetTransform = resolved;
            _hasOffsetBasePositionCaptured = false;
            _lastAppliedTransformYOffset = 0f;
        }

        return true;
    }

    // 地上アクションの待機時間を監視し、次の遷移を決める。
    void ActionDecisionUpdate() 
    {
        ApplyCharacterYOffset();

        if (_isWaitingLandingAnimation || AnimatorIsFalling || AnimatorIsDragging || AnimatorIsOnWindow || !_isActionTimerActive || _currentAction == GroundAction.Walk)
        {
            return;
        }

        _currentActionRemainSeconds -= Time.deltaTime;
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
                return;
            }
        }

        _animatorSync.SetShouldGoNext(true);
        _isActionTimerActive = false;
        _currentActionRemainSeconds = 0f;
    }

    // 歩行中の移動と向き、完了判定を更新する。
    void WalkUpdate()
    {
        CaptureFrontYaw();

        if (CurrentAction != GroundAction.Walk)
        {
            _isWalkActive = false;
            _isWaitingForActionTransition = false;
            _lastFacingDirectionSign = 0;
            _blockedMoveFrames = 0;
            return;
        }

        if (AnimatorIsDragging)
        {
            if (_isWalkActive)
            {
                ApplyFrontFacingAfterWalk();
            }

            _isWalkActive = false;
            _lastFacingDirectionSign = 0;
            _blockedMoveFrames = 0;
            return;
        }

        if (_isWaitingForActionTransition)
        {
            return;
        }

        if (!_isWalkActive)
        {
            if (!ConsumeWalkStart())
            {
                return;
            }

            GetWalkRange(out float minX, out float maxX, out float currentReferenceX);
            _walkTargetScreenX = PickWalkTargetX(currentReferenceX, minX, maxX);
            _walkCurrentScreenX = currentReferenceX;
            _isWalkActive = true;
        }

        float nextScreenX = Mathf.MoveTowards(
            _walkCurrentScreenX,
            _walkTargetScreenX,
            Mathf.Max(0f, walkSpeedPixelsPerSec) * Time.deltaTime);
        float currentScreenX = _walkCurrentScreenX;
        float deltaScreenX = nextScreenX - currentScreenX;

        bool hasMoveIntent = Mathf.Abs(deltaScreenX) > 0.001f;
        bool moved = true;

        ApplyFacingFromMovement(deltaScreenX);

        moved = MoveWindowByPixels(deltaScreenX, 0f);

        if (!hasMoveIntent)
        {
            _blockedMoveFrames = 0;
            _walkCurrentScreenX = nextScreenX;
        } else if (moved)
        {
            _blockedMoveFrames = 0;
            _walkCurrentScreenX = nextScreenX;
        } else
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

    // 重力、接地、支持ウィンドウ追従を含む落下処理を更新する。
    void FallingUpdate()
    {
        bool isDraggingByUniWindow = uniWindowMoveHandle != null && uniWindowMoveHandle.IsDragging;

        if (_fallRuntime.IsDragDrivenByFall && !isDraggingByUniWindow)
        {
            SetDraggingState(false);
            _fallRuntime.IsDragDrivenByFall = false;
        }

        bool isDraggingByActionManager = AnimatorIsDragging;

        if (isDraggingByActionManager || isDraggingByUniWindow)
        {
            if (!_fallRuntime.IsGravityPausedByDrag)
            {
                ResetNonDragStateOnDragStart();
                _fallRuntime.IsGravityPausedByDrag = true;
                _fallSpeedPixelsPerSec = 0f;
                SyncWindowSurfaceState(false);
                SetDraggingState(true);
                _fallRuntime.IsDragDrivenByFall = isDraggingByUniWindow;
                SetFallingState(false);
            }

            return;
        }

        if (_fallRuntime.IsGravityPausedByDrag)
        {
            _fallRuntime.IsGravityPausedByDrag = false;
            SetDraggingState(false);
            _fallRuntime.IsDragDrivenByFall = false;
        }

        FollowSupportingWindow();

        if (!ScreenSpaceTransformUtility.TryGetScreenPosition(_targetCamera, ModelContainer.transform, out var modelScreen))
        {
            return;
        }

        if (!_hasVirtualScreenY)
        {
            _virtualScreenY = modelScreen.y;
            _hasVirtualScreenY = true;
        }

        float groundContactOffsetPixels = GetGroundContactOffsetPixels(modelScreen);
        float sitYOffsetPixels = 0f;
        float groundY = GetGroundY();
        bool wasGrounded = IsGrounded;

        if (TryProbeGroundGap(sitYOffsetPixels, out GroundProbeResult groundProbe))
        {
            float signedGapToTaskbar = groundProbe.SignedGapPixels;
            GroundSurfaceType groundSurfaceType = groundProbe.SurfaceType;
            IntPtr supportWindowHandle = groundProbe.SupportWindowHandle;
            WindowsAPI.RECT supportWindowRect = groundProbe.SupportWindowRect;

            bool trackedSupportWindow = _followingWindowHandle != IntPtr.Zero
                                        && supportWindowHandle == _followingWindowHandle
                                        && groundSurfaceType == GroundSurfaceType.WindowTop;

            if (trackedSupportWindow)
            {
                CommitGroundedTransitionFromProbe(
                    -signedGapToTaskbar,
                    groundSurfaceType,
                    supportWindowHandle,
                    supportWindowRect,
                    onWindowState: true,
                    wasGrounded: wasGrounded);
                return;
            }

            float groundedEpsilon = Mathf.Max(0f, groundedEpsilonPixels);
            if (signedGapToTaskbar <= groundedEpsilon)
            {
                CommitGroundedTransitionFromProbe(
                    -signedGapToTaskbar,
                    groundSurfaceType,
                    supportWindowHandle,
                    supportWindowRect,
                    onWindowState: groundSurfaceType == GroundSurfaceType.WindowTop,
                    wasGrounded: wasGrounded);
                return;
            }

            float gravityWindow = Mathf.Max(0f, gravityPixelsPerSec2);
            float maxSpeedWindow = Mathf.Max(0f, maxFallSpeedPixelsPerSec);
            _fallSpeedPixelsPerSec = Mathf.Min(maxSpeedWindow, _fallSpeedPixelsPerSec + gravityWindow * Time.deltaTime);
            float desiredDownMove = _fallSpeedPixelsPerSec * Time.deltaTime;
            float clampedDownMove = Mathf.Clamp(desiredDownMove, 0f, signedGapToTaskbar);

            if (clampedDownMove > 0f)
            {
                MoveWindowByPixels(0f, -clampedDownMove);
            }

            bool hasLandedThisTick = clampedDownMove >= signedGapToTaskbar - groundedEpsilon;
            if (hasLandedThisTick)
            {
                CommitGroundedTransitionFromProbe(
                    correctionDeltaY: 0f,
                    groundSurfaceType,
                    supportWindowHandle,
                    supportWindowRect,
                    onWindowState: groundSurfaceType == GroundSurfaceType.WindowTop,
                    wasGrounded: wasGrounded);
            } else
            {
                CommitAirborneTransition();
            }

            return;
        }

        float currentContactY = _virtualScreenY - groundContactOffsetPixels;
        if (currentContactY <= groundY + Mathf.Max(0f, groundedEpsilonPixels))
        {
            float correctedY = groundY + groundContactOffsetPixels;
            float deltaY = correctedY - _virtualScreenY;
            _virtualScreenY = correctedY;

            MoveWindowByPixels(0f, deltaY);

            CommitGroundedNoSupport();

            return;
        }

        float gravity = Mathf.Max(0f, gravityPixelsPerSec2);
        float maxSpeed = Mathf.Max(0f, maxFallSpeedPixelsPerSec);

        _fallSpeedPixelsPerSec = Mathf.Min(maxSpeed, _fallSpeedPixelsPerSec + gravity * Time.deltaTime);
        float nextScreenY = _virtualScreenY - _fallSpeedPixelsPerSec * Time.deltaTime;
        float nextContactY = nextScreenY - groundContactOffsetPixels;

        if (nextContactY <= groundY)
        {
            nextScreenY = groundY + groundContactOffsetPixels;
            CommitGroundedNoSupport();
        } else
        {
            CommitAirborneTransition();
        }

        float moveDeltaY = nextScreenY - _virtualScreenY;
        _virtualScreenY = nextScreenY;
        MoveWindowByPixels(0f, moveDeltaY);
    }

    // 接地時の縦方向補正だけを適用する。
    private void ApplyVerticalCorrection(float correctionDeltaY)
    {
        if (Mathf.Abs(correctionDeltaY) > 0.001f)
        {
            MoveWindowByPixels(0f, correctionDeltaY);
        }
    }

    // プローブ結果を使って接地遷移を確定する。
    private void CommitGroundedTransitionFromProbe(
        float correctionDeltaY,
        GroundSurfaceType groundSurfaceType,
        IntPtr supportWindowHandle,
        WindowsAPI.RECT supportWindowRect,
        bool onWindowState,
        bool wasGrounded)
    {
        ApplyVerticalCorrection(correctionDeltaY);

        SetDraggingState(false);
        _fallSpeedPixelsPerSec = 0f;
        IsGrounded = true;
        _lastGroundSurfaceType = groundSurfaceType;
        UpdateSupportingWindowFollowTarget(groundSurfaceType, supportWindowHandle, supportWindowRect);
        SyncWindowSurfaceState(onWindowState);
        SetFallingState(false);
        EnsureTopmostForTaskbarLanding();

        if (!wasGrounded)
        {
            ApplyLandingZOrder(groundSurfaceType, supportWindowHandle);
        }
    }

    // 支持物なしで地面へ接地した状態を確定する。
    private void CommitGroundedNoSupport()
    {
        _fallSpeedPixelsPerSec = 0f;
        IsGrounded = true;
        _lastGroundSurfaceType = GroundSurfaceType.None;
        ClearSupportingWindowFollowTarget();
        SyncWindowSurfaceState(false);
        SetFallingState(false);
    }

    // 空中状態への遷移を確定する。
    private void CommitAirborneTransition()
    {
        IsGrounded = false;
        _lastGroundSurfaceType = GroundSurfaceType.None;
        ClearSupportingWindowFollowTarget();
        SyncWindowSurfaceState(false);
        SetFallingState(true);
    }

    // ホイール入力に応じてキャラクターとウィンドウの拡大率を更新する。
    void ScrollUpdate()
    {
        if (!_isInputRegistered)
        {
            RegisterInputEvents();
        }

        if (Time.frameCount == _lastScrollHandledFrame || !TryResolveReferences() || AnimatorIsDragging)
        {
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
        } else
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
        }

        if (Mathf.Approximately(scroll, 0f))
        {
            return;
        }

        if (!IsPointerOnCharacter(screenPosition))
        {
            return;
        }

        float notch = scroll / 120f;
        if (Mathf.Approximately(notch, 0f))
        {
            notch = Mathf.Sign(scroll);
        }

        Transform modelContainer = ModelContainer.transform;

        float currentScale = modelContainer.localScale.x;
        float nextScale = currentScale + (notch * Mathf.Max(0f, scrollScaleStep));
        nextScale = Mathf.Clamp(nextScale, Mathf.Min(minScale, maxScale), Mathf.Max(minScale, maxScale));

        if (Mathf.Approximately(nextScale, currentScale))
        {
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

    // 更新処理に必要な参照をその場で補完する。
    private bool TryResolveReferences()
    {
        if (_targetCamera == null)
        {
            _targetCamera = Camera.main;
        }

        if (uniWindowMoveHandle == null)
        {
            uniWindowMoveHandle = FindFirstObjectByType<UniWindowMoveHandle>();
        }

        return _targetCamera != null;
    }

    // スクロール入力イベントを購読する。
    private void RegisterInputEvents()
    {
        if (_isInputRegistered)
        {
            return;
        }

        var controller = InputController.Instance;
        if (controller == null)
        {
            Log.Warning("[CharacterManager] RegisterInputEvents skipped: InputController is null.");
            return;
        }

        controller.UI.ScrollWheel.performed += OnScrollWheel;
        controller.UI.RightClick.performed += OnRightClick;
        _isInputRegistered = true;
        Log.Info("[CharacterManager] Input events registered. handlers=ScrollWheel,RightClick");
    }

    // スクロール入力イベントの購読を解除する。
    private void UnregisterInputEvents()
    {
        if (!_isInputRegistered)
        {
            return;
        }

        var controller = InputController.Instance;
        if (controller != null)
        {
            controller.UI.ScrollWheel.performed -= OnScrollWheel;
            controller.UI.RightClick.performed -= OnRightClick;
        }
        else
        {
            Log.Warning("[CharacterManager] UnregisterInputEvents: InputController already missing.");
        }

        _isInputRegistered = false;
        Log.Info("[CharacterManager] Input events unregistered. handlers=ScrollWheel,RightClick");
    }

    // スクロール入力をバッファへ保存して次フレームで処理する。
    private void OnScrollWheel(InputAction.CallbackContext context)
    {
        if (!TryResolveReferences())
        {
            return;
        }

        var mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        _pendingEventScroll = context.ReadValue<Vector2>().y;
        _pendingEventScreenPosition = mouse.position.ReadValue();
        _hasEventScrollPending = true;
    }

    // 右クリック入力をバッファへ保存して次フレームで処理する。
    private void OnRightClick(InputAction.CallbackContext context)
    {
        if (!context.performed || !TryResolveReferences())
        {
            if (context.performed)
            {
                Log.Warning("[CharacterManager] RightClick ignored: references are unresolved.");
            }
            return;
        }

        var mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        _pendingRightClickScreenPosition = mouse.position.ReadValue();
        _hasEventRightClickPending = true;
        Log.Info($"[CharacterManager] RightClick captured. screenPos={_pendingRightClickScreenPosition}");
    }

    // キャラクター右クリック時にUIプロセスへメニュー表示要求を送る。
    private void RightClickMenuUpdate()
    {
        if (!_hasEventRightClickPending)
        {
            return;
        }

        _hasEventRightClickPending = false;
        bool isPointerOnCharacter = IsPointerOnCharacter(_pendingRightClickScreenPosition);
        if (!isPointerOnCharacter)
        {
            Log.Info($"[CharacterManager] RightClick ignored: pointer is not on character. screenPos={_pendingRightClickScreenPosition}");
            return;
        }

        Log.Info($"[CharacterManager] RightClick accepted on character. screenPos={_pendingRightClickScreenPosition}");

        RequestUiMenuOpen();
    }

    // 既存のUIヘルスチェックUDPポートへメニュー表示要求を送信する。
    private void RequestUiMenuOpen()
    {
        try
        {
            using var client = new UdpClient();
            byte[] payload = Encoding.UTF8.GetBytes(Constant.UIOpenMenuMessage);
            client.Send(payload, payload.Length, Constant.BackendHost, Constant.UIHealthCheckUdpPort);
            Log.Info($"[CharacterManager] Requested UI menu open via UDP. host={Constant.BackendHost}, port={Constant.UIHealthCheckUdpPort}, message={Constant.UIOpenMenuMessage}");
        }
        catch (Exception ex)
        {
            Log.Warning($"[CharacterManager] Failed to request UI menu open: {ex.Message}");
        }
    }

    // 現在スケールに合わせてウィンドウサイズを調整する。
    private void TryResizeWindowForScale(float currentScale)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (Application.isEditor)
        {
            return;
        }

        if (!TryInitResizeBaseline(currentScale))
        {
            return;
        }

        if (Mathf.Approximately(_windowResizeBaseScale, 0f))
        {
            return;
        }

        if (!WindowsAPI.TryGetCurrentWindowRect(out var currentRect))
        {
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
        } else if (targetWidth > maxW)
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
            return;
        }

        int centerX = (currentRect.left + currentRect.right) / 2;
        int centerY = (currentRect.top + currentRect.bottom) / 2;
        int targetX = centerX - (targetWidth / 2);
        int targetY = centerY - (targetHeight / 2);

        bool isWindowResized = WindowsAPI.SetCurrentWindowRect(targetX, targetY, targetWidth, targetHeight);
#endif
    }

    // 現在のウィンドウサイズを基準値として記録する。
    private bool TryInitResizeBaseline(float currentScale)
    {
        if (_isWindowResizeBaselineReady)
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

        _windowResizeBaseScale = 1f;
        _windowResizeBaseWidth = width;
        _windowResizeBaseHeight = height;
        _windowResizeBaseAspect = height > 0 ? (float)width / height : 1f;
        _isWindowResizeBaselineReady = true;

        return true;
#else
        return false;
#endif
    }

    // 指定した画面座標がキャラクター上かどうかを判定する。
    private bool IsPointerOnCharacter(Vector2 screenPosition)
    {
        if (_targetCamera == null || ModelContainer == null)
        {
            return false;
        }

        var ray = _targetCamera.ScreenPointToRay(screenPosition);
        var renderers = ModelContainer.GetComponentsInChildren<Renderer>(true);

        float nearest = float.PositiveInfinity;
        bool isHit = false;

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
            isHit = true;
        }

        return isHit;
    }

    // タスクバーや疑似タスクバーを考慮した地面Y座標を返す。
    private float GetGroundY()
    {
#if UNITY_EDITOR
        var pseudoBars = FindObjectsByType<PseudoTaskbar>(FindObjectsSortMode.None);
        if (pseudoBars != null && pseudoBars.Length > 0)
        {
            float bestPseudo = 0f;
            for (int i = 0; i < pseudoBars.Length; i++)
            {
                var bar = pseudoBars[i];
                if (bar == null || !bar.IsEnabled)
                {
                    continue;
                }

                if (bar.TryGetScreenRect(out var rect))
                {
                    bestPseudo = Mathf.Max(bestPseudo, rect.yMax);
                }
            }

            if (bestPseudo > 0f)
            {
                return Mathf.Clamp(bestPseudo, 0f, Screen.height);
            }
        }
#endif

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        float best = 0f;

        if (NativeWindowApi.TryGetTaskbarRect(out var taskbarRect))
        {
            float nativeRectY = Mathf.Ceil(taskbarRect.yMax);
            if (nativeRectY > 0f)
            {
                best = Mathf.Max(best, nativeRectY);
            }
        }

        if (WindowsAPI.TryGetWorkArea(out var workArea))
        {
            float dpiScale = Mathf.Max(0.01f, WindowsExplorerUtility.GetDPIScale());

            float rawY = Mathf.Ceil(Screen.height - workArea.bottom);
            if (rawY > best)
            {
                best = rawY;
            }

            float scaledY = Mathf.Ceil(Screen.height - (workArea.bottom / dpiScale));
            if (scaledY > best)
            {
                best = scaledY;
            }
        }

        return Mathf.Clamp(best, 0f, Screen.height);
#else
        return 0f;
#endif
    }

    // モデルの最下端から接地点までのオフセットを画面座標で求める。
    private float GetGroundContactOffsetPixels(Vector3 modelScreen)
    {
        if (_targetCamera == null)
        {
            return 0f;
        }

        var renderers = ModelContainer.transform.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            return 0f;
        }

        float minY = float.PositiveInfinity;
        bool isFound = false;
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
                        var screen = _targetCamera.WorldToScreenPoint(corner);
                        if (screen.z <= 0f)
                        {
                            continue;
                        }

                        minY = Mathf.Min(minY, screen.y);
                        isFound = true;
                    }
                }
            }
        }

        if (!isFound)
        {
            return 0f;
        }

        return Mathf.Max(0f, modelScreen.y - minY);
    }

    // 現在ウィンドウと支持面との距離を調べる。
    private bool TryProbeGroundGap(float sitYOffsetPixels, out GroundProbeResult result)
    {
        result = default;
        result.SurfaceType = GroundSurfaceType.None;

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
        result.WindowBottom = currentWindowCenterY;
        float centerX = (windowRect.left + windowRect.right) * 0.5f;
        float fallbackSupportY = workArea.bottom;
        float fallbackGap = fallbackSupportY - currentWindowCenterY;

        if (_followingWindowHandle != IntPtr.Zero && _fallRuntime.HasFollowingWindowRect)
        {
            if (TryGetTrackedSupportWindowRect(_followingWindowHandle, out var trackedRect, out bool supportWindowMinimized))
            {
                _supportLookupFailFrames = 0;
                float trackedSupportTop = trackedRect.top + sitYOffsetPixels;
                float trackedGap = trackedSupportTop - currentWindowCenterY;
                result.SignedGapPixels = trackedGap;
                result.SurfaceType = GroundSurfaceType.WindowTop;
                result.GroundSupportY = trackedSupportTop;
                result.SupportWindowHandle = _followingWindowHandle;
                result.SupportWindowRect = trackedRect;
                return true;
            }

            _supportLookupFailFrames++;
            int graceFrames = Mathf.Max(0, supportWindowLookupGraceFrames);

            if (!supportWindowMinimized && _supportLookupFailFrames <= graceFrames)
            {
                float trackedSupportTop = _lastFollowingWindowRect.top + sitYOffsetPixels;
                float trackedGap = trackedSupportTop - currentWindowCenterY;
                result.SignedGapPixels = trackedGap;
                result.SurfaceType = GroundSurfaceType.WindowTop;
                result.GroundSupportY = trackedSupportTop;
                result.SupportWindowHandle = _followingWindowHandle;
                result.SupportWindowRect = _lastFollowingWindowRect;
                return true;
            }
        }

        if (TryFindSupportWindowUnderCharacter(
                centerX,
                currentWindowCenterY,
                sitYOffsetPixels,
                fallbackSupportY,
                out float supportGap,
                out IntPtr candidateHandle,
                out WindowsAPI.RECT candidateRect))
        {
            result.SignedGapPixels = supportGap;
            result.SurfaceType = GroundSurfaceType.WindowTop;
            result.GroundSupportY = candidateRect.top + sitYOffsetPixels;
            result.SupportWindowHandle = candidateHandle;
            result.SupportWindowRect = candidateRect;
            return true;
        }

        result.SignedGapPixels = fallbackGap;
        result.SurfaceType = GroundSurfaceType.Taskbar;
        result.GroundSupportY = fallbackSupportY;
        return true;
#else
        return false;
#endif
    }

    // キャラクター直下で支持面になれるウィンドウを探す。
    private bool TryFindSupportWindowUnderCharacter(
        float currentCenterX,
        float currentCenterY,
        float sitYOffsetPixels,
        float fallbackSupportY,
        out float supportGap,
        out IntPtr supportWindowHandle,
        out WindowsAPI.RECT supportWindowRect)
    {
        supportGap = 0f;
        supportWindowHandle = IntPtr.Zero;
        supportWindowRect = default;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        IntPtr currentHwnd = WindowsAPI.GetCurrentWindowHandle();
    IntPtr foregroundHwnd = NativeWindowApi.GetForegroundWindowHandle();
    bool isDominantForeground = foregroundHwnd != IntPtr.Zero
                    && foregroundHwnd != currentHwnd
                    && !IsExcludedWindowFromFullscreenOcclusion(foregroundHwnd)
                    && WindowsAPI.IsWindowVisible(foregroundHwnd)
                    && !WindowsAPI.IsWindowMinimized(foregroundHwnd)
                    && (NativeWindowApi.IsWindowMaximized(foregroundHwnd) || NativeWindowApi.IsWindowFullscreen(foregroundHwnd));
    WindowsAPI.RECT foregroundRect = default;
    if (isDominantForeground)
    {
        isDominantForeground = WindowsAPI.GetWindowRect(foregroundHwnd, out foregroundRect);
    }

        float bestGap = float.MaxValue;
        bool isFound = false;

        var windows = WindowsExplorerUtility.GetTopLevelWindows();
        for (int i = 0; i < windows.Count; i++)
        {
            var info = windows[i];
            if (info == null)
            {
                continue;
            }

            IntPtr hWnd = info.hWnd;
            if (hWnd == IntPtr.Zero || hWnd == currentHwnd)
            {
                continue;
            }

            if (!WindowsAPI.IsWindowVisible(hWnd) || WindowsAPI.IsWindowMinimized(hWnd))
            {
                continue;
            }

            WindowsAPI.RECT rect = info.rect;
            float width = rect.right - rect.left;
            float height = rect.bottom - rect.top;
            if (width < 64f || height < 32f)
            {
                continue;
            }

            if (currentCenterX < rect.left || currentCenterX > rect.right)
            {
                continue;
            }

            if (isDominantForeground
                && !NativeWindowApi.AreSameRootOwnerWindow(foregroundHwnd, hWnd)
                && currentCenterX >= foregroundRect.left
                && currentCenterX <= foregroundRect.right
                && foregroundRect.bottom > rect.top)
            {
                continue;
            }

            int probeX = Mathf.Clamp(Mathf.RoundToInt(currentCenterX), rect.left + 1, rect.right - 1);
            int probeY = Mathf.Clamp(rect.top + 1, rect.top, rect.bottom - 1);
            IntPtr topWindowAtProbe = NativeWindowApi.GetWindowFromScreenPoint(probeX, probeY);

            if (topWindowAtProbe == currentHwnd)
            {
                IntPtr underlyingWindowAtProbe = FindUnderlyingWindowAtProbePoint(currentHwnd, probeX, probeY);
                if (underlyingWindowAtProbe != IntPtr.Zero)
                {
                    topWindowAtProbe = underlyingWindowAtProbe;
                }
            }

            if (topWindowAtProbe != IntPtr.Zero
                && topWindowAtProbe != currentHwnd
                && !NativeWindowApi.AreSameRootOwnerWindow(topWindowAtProbe, hWnd))
            {
                continue;
            }

            if (IsSupportOccludedByFront(hWnd, rect))
            {
                continue;
            }

            float supportTopY = rect.top + sitYOffsetPixels;
            if (supportTopY > fallbackSupportY + Mathf.Max(1f, groundedEpsilonPixels))
            {
                continue;
            }

            float gap = supportTopY - currentCenterY;
            if (gap < -Mathf.Max(1f, groundedEpsilonPixels))
            {
                continue;
            }

            if (gap < bestGap)
            {
                bestGap = gap;
                supportWindowHandle = hWnd;
                supportWindowRect = rect;
                isFound = true;
            }
        }

        if (!isFound)
        {
            return false;
        }

        supportGap = bestGap;
        return true;
#else
        return false;
#endif
    }

    // 指定座標で現在ウィンドウの下にある次のウィンドウを探す。
    private IntPtr FindUnderlyingWindowAtProbePoint(IntPtr currentHwnd, int x, int y)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (currentHwnd == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr walker = currentHwnd;
        for (int i = 0; i < 64; i++)
        {
            walker = GetWindow(walker, GW_HWNDNEXT);
            if (walker == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            if (!WindowsAPI.IsWindowVisible(walker) || WindowsAPI.IsWindowMinimized(walker))
            {
                continue;
            }

            if (!WindowsAPI.GetWindowRect(walker, out var rect))
            {
                continue;
            }

            if (x >= rect.left && x <= rect.right && y >= rect.top && y <= rect.bottom)
            {
                return walker;
            }
        }
#endif

        return IntPtr.Zero;
    }

    // 支持対象ウィンドウと追従用オフセットを更新する。
    private void UpdateSupportingWindowFollowTarget(GroundSurfaceType groundSurfaceType, IntPtr supportWindowHandle, WindowsAPI.RECT supportWindowRect)
    {
        if (groundSurfaceType != GroundSurfaceType.WindowTop || supportWindowHandle == IntPtr.Zero)
        {
            ClearSupportingWindowFollowTarget();
            return;
        }

        bool isNewSupportTarget = _followingWindowHandle != supportWindowHandle || !_fallRuntime.HasFollowingWindowRect;

        _followingWindowHandle = supportWindowHandle;
        _lastFollowingWindowRect = supportWindowRect;
        _fallRuntime.HasFollowingWindowRect = true;
        _supportLookupFailFrames = 0;

        if (isNewSupportTarget && WindowsAPI.TryGetCurrentWindowRect(out var appRect))
        {
            _followingWindowOffsetX = appRect.left - supportWindowRect.left;
        }
    }

    // 支持ウィンドウ追従状態を解除する。
    private void ClearSupportingWindowFollowTarget()
    {
        bool isKeepTopmost = IsGrounded && _lastGroundSurfaceType == GroundSurfaceType.Taskbar;
        bool hasFollowState = _followingWindowHandle != IntPtr.Zero
                              || _fallRuntime.HasFollowingWindowRect
                              || Mathf.Abs(_followingWindowOffsetX) > 0.001f
                              || _wasSupportFocusedLastFrame
                              || _lastFocusedSupportHwnd != IntPtr.Zero
                              || _supportLookupFailFrames != 0;

        if (!hasFollowState)
        {
            if (_zOrderRuntime.IsTopmostPinnedBySupport && !isKeepTopmost)
            {
                bool released = WindowsAPI.SetCurrentWindowTopmost(false);
                LogClearFollowTargetIfChanged("set-topmost-false", isKeepTopmost, released);
                _zOrderRuntime.IsTopmostPinnedBySupport = false;
            }

            return;
        }

        _followingWindowHandle = IntPtr.Zero;
        _lastFollowingWindowRect = default;
        _fallRuntime.HasFollowingWindowRect = false;
        _followingWindowOffsetX = 0f;
        _wasSupportFocusedLastFrame = false;
        _lastFocusedSupportHwnd = IntPtr.Zero;
        if (_zOrderRuntime.IsTopmostPinnedBySupport && !isKeepTopmost)
        {
            bool released = WindowsAPI.SetCurrentWindowTopmost(false);
            LogClearFollowTargetIfChanged("set-topmost-false", isKeepTopmost, released);
            _zOrderRuntime.IsTopmostPinnedBySupport = false;
        }

        if (_zOrderRuntime.IsTopmostPinnedBySupport && isKeepTopmost)
        {
            bool ensuredTopmost = WindowsAPI.SetCurrentWindowTopmost(true);
            LogClearFollowTargetIfChanged("set-topmost-true", isKeepTopmost, ensuredTopmost);
            _zOrderRuntime.IsTopmostPinnedBySupport = ensuredTopmost;
        }
        _supportLookupFailFrames = 0;
    }

    // タスクバー着地時に最前面固定を確保する。
    private void EnsureTopmostForTaskbarLanding()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!IsGrounded || _lastGroundSurfaceType != GroundSurfaceType.Taskbar)
        {
            return;
        }

        if (_zOrderRuntime.IsTopmostPinnedBySupport)
        {
            return;
        }

        bool topmostEnabled = WindowsAPI.SetCurrentWindowTopmost(true);
        Log.Info($"[CharacterManager] ZOrder taskbar-landing set-topmost true. success={topmostEnabled}");
        _zOrderRuntime.IsTopmostPinnedBySupport = topmostEnabled;
#endif
    }

    // キャラクター前面化の後にUI側の前面化も要求する。
    private void RequestUiTopmostAfterCharacterTopmost(string reason)
    {
        _zOrderHelper?.RequestUiTopmostAfterCharacterTopmost(reason);
    }

    // UI前面化要求ログを重複なく出す。
    private void LogUiFrontRequestIfChanged(string mode, string reason)
    {
        string token = $"{mode}:{reason}";
        if (string.Equals(_lastUiFrontRequestLogToken, token, StringComparison.Ordinal))
        {
            return;
        }

        _lastUiFrontRequestLogToken = token;
        if (string.Equals(mode, "udp", StringComparison.Ordinal))
        {
            Log.Info($"[CharacterManager] ZOrder UI re-front requested via UDP fallback. reason={reason}, host={Constant.BackendHost}, port={Constant.UIHealthCheckUdpPort}");
            return;
        }

        Log.Info($"[CharacterManager] ZOrder UI re-front requested via direct process control. reason={reason}");
    }

    // 追従解除ログを重複なく出す。
    private void LogClearFollowTargetIfChanged(string action, bool isKeepTopmost, bool success)
    {
        string token = $"{action}:{isKeepTopmost}:{success}";
        if (string.Equals(_lastClearFollowTargetLogToken, token, StringComparison.Ordinal))
        {
            return;
        }

        _lastClearFollowTargetLogToken = token;
        Log.Info($"[CharacterManager] ZOrder clear-follow-target {action}. keepTopmost={isKeepTopmost}, success={success}");
    }

    private sealed class CharacterZOrderHelper
    {
        private readonly CharacterManager _owner;

    // キャラクターのZオーダー制御を担当する補助クラスを初期化する。
        public CharacterZOrderHelper(CharacterManager owner)
        {
            _owner = owner;
        }

    // ドラッグ開始時にキャラクターを前面へ出す。
        public void EnsureCharacterFrontOnDragStart()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            IntPtr currentHwnd = WindowsAPI.GetCurrentWindowHandle();
            IntPtr foregroundHwnd = NativeWindowApi.GetForegroundWindowHandle();

            if (foregroundHwnd != IntPtr.Zero && foregroundHwnd != currentHwnd)
            {
                bool placedAboveForeground = WindowsAPI.PlaceCurrentWindowAbove(foregroundHwnd);
                Log.Info($"[CharacterManager] ZOrder drag-start place-above foreground. target=0x{foregroundHwnd.ToInt64():X}, success={placedAboveForeground}");
            }

            bool topmostEnabled = WindowsAPI.SetCurrentWindowTopmost(true);
            Log.Info($"[CharacterManager] ZOrder drag-start set-topmost true. success={topmostEnabled}");
            _owner._zOrderRuntime.IsTopmostPinnedByDrag = topmostEnabled;
            _owner._zOrderRuntime.IsTopmostPinnedBySupport = _owner._zOrderRuntime.IsTopmostPinnedBySupport || topmostEnabled;

            bool broughtFront = WindowsAPI.BringCurrentWindowToFront();
            Log.Info($"[CharacterManager] ZOrder drag-start bring-front. success={broughtFront}");
            RequestUiTopmostAfterCharacterTopmost("drag-start");
#endif
        }

    // ドラッグ終了時に前面固定を解除する。
        public void ReleaseCharacterFrontAfterDragEnd()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (!_owner._zOrderRuntime.IsTopmostPinnedByDrag)
            {
                return;
            }

            bool isKeepTopmost = _owner.IsGrounded && _owner._lastGroundSurfaceType == GroundSurfaceType.Taskbar;
            if (isKeepTopmost)
            {
                _owner._zOrderRuntime.IsTopmostPinnedBySupport = true;
                _owner._zOrderRuntime.IsTopmostPinnedByDrag = false;
                return;
            }

            bool released = WindowsAPI.SetCurrentWindowTopmost(false);
            Log.Info($"[CharacterManager] ZOrder drag-end set-topmost false. success={released}");
            _owner._zOrderRuntime.IsTopmostPinnedByDrag = false;
            _owner._zOrderRuntime.IsTopmostPinnedBySupport = false;
#endif
        }

    // キャラクター前面化の後にUI前面化要求を送る。
        public void RequestUiTopmostAfterCharacterTopmost(string reason)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            float now = Time.unscaledTime;
            if (_owner._lastUiTopmostRequestAt > 0f && now - _owner._lastUiTopmostRequestAt < 0.15f)
            {
                return;
            }

            _owner._lastUiTopmostRequestAt = now;

            try
            {
                var backendManager = FindFirstObjectByType<BackendManager>();
                if (backendManager != null
                    && backendManager.TryRaiseUiWindow())
                {
                    _owner.LogUiFrontRequestIfChanged("direct", reason);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[CharacterManager] Direct UI z-order request failed (reason={reason}): {ex.Message}");
            }

            try
            {
                using var client = new UdpClient();
                byte[] payload = Encoding.UTF8.GetBytes(Constant.UIForceTopmostMessage);
                client.Send(payload, payload.Length, Constant.BackendHost, Constant.UIHealthCheckUdpPort);
                _owner.LogUiFrontRequestIfChanged("udp", reason);
            }
            catch (Exception ex)
            {
                Log.Warning($"[CharacterManager] Failed to request UI topmost (reason={reason}): {ex.Message}");
            }
#endif
        }

    // 着地先に応じてキャラクターのZオーダーを調整する。
        public void ApplyLandingZOrder(GroundSurfaceType groundSurfaceType, IntPtr supportWindowHandle)
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
                bool canUseForeground = foregroundHwnd != IntPtr.Zero
                                        && foregroundHwnd != currentHwnd
                                        && WindowsAPI.IsWindowVisible(foregroundHwnd)
                                        && !WindowsAPI.IsWindowMinimized(foregroundHwnd);

                targetHwnd = canUseForeground ? foregroundHwnd : NativeWindowApi.GetTaskbarHandle();
                if (canUseForeground)
                {
                    Log.Info($"[CharacterManager] ZOrder landing target switched to foreground. target=0x{foregroundHwnd.ToInt64():X}");
                }
            }

            if (targetHwnd == IntPtr.Zero)
            {
                bool broughtNoTarget = WindowsAPI.BringCurrentWindowToFront();
                Log.Info($"[CharacterManager] ZOrder landing bring-front without target. success={broughtNoTarget}");
                RequestUiTopmostAfterCharacterTopmost("landing-zorder-no-target");
                return;
            }

            if (groundSurfaceType == GroundSurfaceType.Taskbar)
            {
                bool topmostEnabled = WindowsAPI.SetCurrentWindowTopmost(true);
                Log.Info($"[CharacterManager] ZOrder landing set-topmost true for taskbar. success={topmostEnabled}");
                _owner._zOrderRuntime.IsTopmostPinnedBySupport = topmostEnabled;
            }

            bool placed = WindowsAPI.PlaceCurrentWindowAbove(targetHwnd);
            bool brought = WindowsAPI.BringCurrentWindowToFront();
            Log.Info($"[CharacterManager] ZOrder landing place-above and bring-front. target=0x{targetHwnd.ToInt64():X}, placed={placed}, brought={brought}, surface={groundSurfaceType}");
            RequestUiTopmostAfterCharacterTopmost("landing-zorder");
#endif
        }
    }

    // 支持面に応じたAnimator状態をまとめて同期する。
    private void SyncWindowSurfaceState(bool onWindow)
    {
        SetOnWindowState(onWindow);
        bool sittingOnWindow = onWindow && CurrentAction == GroundAction.Sit;
        SetSittingWindowState(sittingOnWindow);
    }

    // 着地先に応じたZオーダー調整をヘルパーへ委譲する。
    private void ApplyLandingZOrder(GroundSurfaceType groundSurfaceType, IntPtr supportWindowHandle)
    {
        _zOrderHelper?.ApplyLandingZOrder(groundSurfaceType, supportWindowHandle);
    }

    // 支持中のウィンドウ移動に追従して自ウィンドウを補正する。
    private void FollowSupportingWindow()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!IsGrounded || _lastGroundSurfaceType != GroundSurfaceType.WindowTop)
        {
            return;
        }

        if (_followingWindowHandle == IntPtr.Zero || !_fallRuntime.HasFollowingWindowRect)
        {
            return;
        }

        if (!TryGetTrackedSupportWindowRect(_followingWindowHandle, out var currentRect, out bool supportWindowMinimized))
        {
            _supportLookupFailFrames++;
            int graceFrames = Mathf.Max(0, supportWindowLookupGraceFrames);
            if (supportWindowMinimized || _supportLookupFailFrames > graceFrames)
            {
                DetachFromWindowTopSupport();
            }
            return;
        }

        _supportLookupFailFrames = 0;

        if (WindowsAPI.IsWindowMinimized(_followingWindowHandle))
        {
            DetachFromWindowTopSupport();
            return;
        }

        if (IsSupportOccludedByFront(_followingWindowHandle, currentRect))
        {
            DetachFromWindowTopSupport();
            return;
        }

        float deltaY = currentRect.top - _lastFollowingWindowRect.top;
        bool supportWindowMoved = Mathf.Abs(deltaY) > 0.001f;
        if (WindowsAPI.TryGetCurrentWindowRect(out var currentAppRect))
        {
            float appCenterX = (currentAppRect.left + currentAppRect.right) * 0.5f;
            if (appCenterX < currentRect.left || appCenterX > currentRect.right)
            {
                DetachFromWindowTopSupport();
                return;
            }

            float desiredLeft = currentRect.left + _followingWindowOffsetX;
            float deltaXToTarget = desiredLeft - currentAppRect.left;
            if (Mathf.Abs(deltaXToTarget) > 0.001f || Mathf.Abs(deltaY) > 0.001f)
            {
                supportWindowMoved = true;
                MoveWindowByPixels(deltaXToTarget, -deltaY);
            }

            float sitYOffsetPixels = 0f;
            float supportTop = currentRect.top + sitYOffsetPixels;
            float appCenterY = (currentAppRect.top + currentAppRect.bottom) * 0.5f;
            float gapToSupportTop = supportTop - appCenterY;
            if (Mathf.Abs(gapToSupportTop) > Mathf.Max(0.001f, groundedEpsilonPixels))
            {
                supportWindowMoved = true;
                MoveWindowByPixels(0f, -gapToSupportTop);
            }

            if (supportWindowMoved)
            {
                // 支持ウィンドウの移動中はキャラクターを常にその前面へ置く。
                bool placed = WindowsAPI.PlaceCurrentWindowAbove(_followingWindowHandle);
                if (!_zOrderRuntime.HasFollowPlaceLogState || _zOrderRuntime.WasLastFollowPlaceSuccess != placed)
                {
                    Log.Info($"[CharacterManager] ZOrder follow-support place-above. target=0x{_followingWindowHandle.ToInt64():X}, success={placed}");
                    _zOrderRuntime.HasFollowPlaceLogState = true;
                    _zOrderRuntime.WasLastFollowPlaceSuccess = placed;
                }
                RequestUiTopmostAfterCharacterTopmost("follow-support-window");
            } else
            {
                _zOrderRuntime.HasFollowPlaceLogState = false;
            }
        }

        _lastFollowingWindowRect = currentRect;
#endif
    }

    // 支持ウィンドウから外れたら通常の落下状態へ戻す。
    private void DetachFromWindowTopSupport()
    {
        ClearSupportingWindowFollowTarget();
        IsGrounded = false;
        _lastGroundSurfaceType = GroundSurfaceType.None;
        SyncWindowSurfaceState(false);
        SetFallingState(true);
    }

    // ドラッグ開始時に非ドラッグ由来の状態をリセットする。
    private void ResetNonDragStateOnDragStart()
    {
        IsGrounded = false;
        _lastGroundSurfaceType = GroundSurfaceType.None;
        ClearSupportingWindowFollowTarget();
        _hasVirtualScreenY = false;
    }

    // 前面ウィンドウが支持ウィンドウを覆っているか判定する。
    private bool IsSupportOccludedByFront(IntPtr supportHwnd, WindowsAPI.RECT supportRect)
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

    // トップレベルウィンドウ一覧から対象矩形を取得する。
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

    // 追従中ウィンドウの矩形を取得し、最小化状態も返す。
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
            // 非表示の支持ウィンドウは即座に見失ったものとして扱う。
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

    // ウィンドウハンドルをログ用の16進表記へ変換する。
    private static string FormatHwnd(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return "0x0";
        }

        return $"0x{hWnd.ToInt64():X}";
    }

    // 全画面遮蔽判定から除外するウィンドウか調べる。
    private bool IsExcludedWindowFromFullscreenOcclusion(IntPtr hWnd)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        if (!TryGetWindowProcessName(hWnd, out var processName) || string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }
#endif

        return false;
    }

    // ウィンドウハンドルに対応するプロセス名を取得する。
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
        } catch
        {
            return false;
        }
#else
        return false;
#endif
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [DllImport("user32.dll")]
    // ウィンドウハンドルに対応するプロセスIDを取得する。
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    // Zオーダー上で隣接するウィンドウを取得する。
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    private const uint GW_HWNDNEXT = 2;
#endif

    // Editor上ではTransformを直接動かして移動を再現する。
    private bool MoveEditorTransform(float deltaX, float deltaY)
    {

        if (!ScreenSpaceTransformUtility.TryGetScreenPosition(_targetCamera, ModelContainer.transform, out var screen))
        {
            return false;
        }

        var moved = new Vector3(screen.x + deltaX, screen.y + deltaY, screen.z);
        return ScreenSpaceTransformUtility.TrySetScreenPosition(_targetCamera, ModelContainer.transform, moved, screen.z);
    }

    // マウス移動量をDPI考慮込みでウィンドウ移動量へ変換する。
    private bool MoveWindowByMouse(float mouseDeltaX, float mouseDeltaY)
    {
        if (Application.isEditor)
        {
            return MoveWindowByPixels(mouseDeltaX, mouseDeltaY);
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        float scale = Mathf.Max(0.01f, mouseDeltaScaleMultiplier);
        scale *= Mathf.Max(0.01f, WindowsExplorerUtility.GetDPIScale());

        return MoveWindowByPixels(mouseDeltaX * scale, mouseDeltaY * scale);
#else
        return false;
#endif
    }

    // ピクセル単位でウィンドウまたはEditor上のTransformを移動する。
    private bool MoveWindowByPixels(float deltaX, float deltaY)
    {
        if (Application.isEditor)
        {
            return MoveEditorTransform(deltaX, deltaY);
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

    // 移動方向に応じてキャラクターの向きを切り替える。
    private void ApplyFacingFromMovement(float deltaWorldX)
    {
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
        Vector3 euler = ModelContainer.transform.rotation.eulerAngles;
        float yaw = directionSign > 0 ? faceRightYaw : faceLeftYaw;
        ModelContainer.transform.rotation = Quaternion.Euler(euler.x, yaw, euler.z);
    }

    // 歩行終了後に正面向きへ戻す。
    private void ApplyFrontFacingAfterWalk()
    {
        if (!_hasFrontYawCaptured || ModelContainer == null)
        {
            return;
        }

        Vector3 euler = ModelContainer.transform.rotation.eulerAngles;
        ModelContainer.transform.rotation = Quaternion.Euler(euler.x, _frontYaw, euler.z);
        _lastFacingDirectionSign = 0;
    }

    // 現在の正面向きYawを一度だけ記録する。
    private void CaptureFrontYaw()
    {
        if (_hasFrontYawCaptured || ModelContainer == null)
        {
            return;
        }

        _frontYaw = ModelContainer.transform.rotation.eulerAngles.y;
        _hasFrontYawCaptured = true;
    }

    // 歩行を完了扱いにして次アクション遷移へ進める。
    private void CompleteWalkMovement(string reason)
    {
        _isWalkActive = false;
        _isWaitingForActionTransition = true;
        _blockedMoveFrames = 0;
        ApplyFrontFacingAfterWalk();
        NotifyWalkMovementCompleted();
    }

    // 歩行可能範囲と現在位置を取得する。
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

        if (ScreenSpaceTransformUtility.TryGetScreenPosition(_targetCamera, ModelContainer.transform, out var modelScreen))
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

    // 現在位置と移動範囲から歩行先X座標を決める。
    private float PickWalkTargetX(float currentX, float minX, float maxX)
    {
        if (Mathf.Approximately(minX, maxX))
        {
            return minX;
        }

        float minDistance = Mathf.Max(0f, walkMinDistancePixels);
        float maxDistance = Mathf.Max(minDistance, walkMaxDistancePixels);
        float randomDistance = UnityEngine.Random.Range(minDistance, maxDistance);

        int direction = UnityEngine.Random.value < 0.5f ? -1 : 1;
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
}
