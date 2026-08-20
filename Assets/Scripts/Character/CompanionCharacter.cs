using Cysharp.Threading.Tasks;
using Kirurobo;
using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using WebSocketSharp;

public enum GroundAction
{
    Idle = 0,
    Sit = 1,
    Walk = 2,
}

public class CompanionCharacter : SingletonMonoBehaviour<CompanionCharacter>
{
    const string LogPrefix = "[CompanionCharacter]";

    //重みと行動の範囲をもった行動定義
    [SerializeField] ActionState idleAction = new ActionState(1f, 1.5f, 3.5f);
    [SerializeField] ActionState sitAction = new ActionState(1f, 2.0f, 4.5f);
    [SerializeField] ActionState walkAction = new ActionState(1f, 520f, 120f);

    //重力(pixels/s^2)
    [SerializeField] float gravity = 2800f;
    //地面とみなすY座標の閾値(pixels)
    [SerializeField] float groundedPixels = 0.5f;
    //落下時の最大速度(pixels/s)
    [SerializeField] float maxFallSpeed = 6000f;
    //ウィンドウの上にいるとみなすための猶予フレーム
    [SerializeField] int supportGraceFrames = 15;
    [SerializeField] UniWindowMoveHandle uniWindowMoveHandle;

    //スクロールの感度
    [SerializeField] float scrollMultiplier = 1f;
    [SerializeField] float scrollStep = 0.1f;
    //キャラクターの最大スケール
    [SerializeField] float maxScale = 3.0f;
    //キャラクターの最小スケール
    [SerializeField] float minScale = 0.3f;

    //歩き到達の閾値
    [SerializeField] float walkReachThreshold = 2f;
    //歩ける画面範囲のパディング
    [SerializeField] float walkPadding = 120f;
    [SerializeField] float walkSpeed = 160f;

    //歩きがブロックされる猶予フレーム
    int walkBlockFrame = 2;
    bool _isSpawning;
    Animator _modelAnimator;
    AnimatorSync _animatorSync;
    Camera _targetCamera;
    CancellationTokenSource _cancellationTokenSource;
    GameObject _modelContainer;
    GameObject _spawnedModel;
    AssetBundle _loadedBundle;
    bool _isAnimatorInitialized;
    bool _isCharacterStartupInitialized;
    bool _isTimerActive;
    bool _hasPendingAction;
    bool _isWaitingLanding;
    bool _isWalkRequested;
    float _currentActionLimit;
    GroundAction _currentAction = GroundAction.Idle;
    GroundAction _pendingAction = GroundAction.Idle;

    bool _hasOffsetCaptured;
    float _groundYOffset;
    float _lastYOffset;
    float _sitYOffset;
    OffsetMode _offsetApplyMode;
    Transform _offsetTransform;
    Vector3 _offsetPosition;

    FallRuntimeState _fallRuntime;
    ZOrderRuntimeState _zOrderRuntime;
    // 常時最前面固定の方針と矛盾しないよう、既存の解除ロジックを無効化するためのガード
    readonly bool _alwaysTopmostLocked = true;
    bool _hasVirtualScreenY;
    bool _wasSupportFocused;
    float _fallSpeed;
    float _windowOffsetX;
    float _virtualScreenY;
    GroundSurfaceType _lastGroundSurfaceType = GroundSurfaceType.None;
    int _supportFailFrames;
    IntPtr _followWindow;
    IntPtr _lastFocusedHwnd;
    WindowsAPI.RECT _lastFollowWindowRect;

    bool _hasScrollPending;
    bool _hasRightClickPending;
    bool _isInputRegistered;
    bool _isWindowResizeReady;
    // フォーカスなしマウス入力用の Raw Input 登録フラグ
    bool _rawInputRegistered;
    float _lastRawInputCheckTime = float.NegativeInfinity;
    const float RawInputReassertIntervalSeconds = 2f;
    // 自前の不透明ピクセル判定結果（フォーカス非依存）
    bool _isPointerOnOpaquePixel;
    bool _pendingOpaqueReading;
    int _pendingOpaqueStableFrames;
    const int OpaqueStableFrameThreshold = 3;
    Texture2D _clickThroughPixelTexture;
    float _pendingScroll;
    float _windowResizeAspect = 1f;
    int _lastScrollFrame = -1;
    int _windowResizeHeight;
    Vector2 _pendingScreenPosition;
    Vector2 _pendingRightClickPosition;

    bool _hasYawCaptured;
    bool _isWalkActive;
    bool _isWaitingTransition;
    float _frontYaw;
    float _walkCurrentX;
    bool _hasLoggedAnimatorMissing;

    // WebSocket character state publisher
    private const int CHARACTER_STATE_PORT = Constant.CharacterStateWebSocketPort;
    private const string CHARACTER_STATE_PATH = Constant.CharacterStateWebSocketPath;
    private const int CHARACTER_SPAWN_NOTIFICATION_RETRY_COUNT = 1;
    // optionVal から初期化するため定数ではなくフィールドとして保持する
    private string _characterId = "0";
    private const float CHARACTER_SPAWN_NOTIFICATION_RETRY_INTERVAL_SECONDS = 0.5f;
    float _walkTargetX;
    int _blockedMoveFrames;
    int _lastFacingDir;

    Animator ModelAnimator => _modelAnimator;
    GameObject ModelContainer => _modelContainer;

    public bool IsDragging => _animatorSync != null && _animatorSync.GetBool(Constant.AnimatorIsDragging);
    public bool IsFalling => _animatorSync != null && _animatorSync.GetBool(Constant.AnimatorIsFalling);
    public bool IsOnWindow => _animatorSync != null && _animatorSync.GetBool(Constant.AnimatorIsOnWindow);
    public bool IsGrounded { get; private set; }
    public bool HasSpawnedCharacter => _modelContainer != null;
    private string CharacterStateWebSocketUrl => $"ws://127.0.0.1:{CHARACTER_STATE_PORT}{CHARACTER_STATE_PATH}";

    /// <summary>
    /// 行動の重みと継続する限度をもったクラス
    /// </summary>
    class ActionState{
        public float bias;
        public float minRange;
        public float maxRange;

        public ActionState(float bias, float minRange, float maxRange)
        {
            this.bias = bias;
            this.minRange = minRange;
            this.maxRange = maxRange;
        }
    }

    /// <summary>
    /// オフセットの適用モード
    /// </summary>
    enum OffsetMode
    {
        None = 0,
        Sit = 1,
        Ground = 2,
    }

    enum GroundSurfaceType
    {
        None = 0,
        Taskbar = 1,
        WindowTop = 2,
    }

    struct FallRuntimeState
    {
        public bool IsDragDriven;
        public bool IsGravityPaused;
        public bool HasFollowingWindow;
    }

    struct ZOrderRuntimeState
    {
        public bool IsTopmostDrag;
        public bool IsTopmostSupport;
        public bool WasLastFollowSuccess;
    }

    struct SupportDistProbeResult
    {
        public float SignedDistance;
        public GroundSurfaceType SurfaceType;
        public float Bottom;
        public float SupportY;
        public IntPtr SupportHandle;
        public WindowsAPI.RECT SupportRect;
    }

    ///<summary>
    /// カメラとウィンドウ移動ハンドルの参照を解決する。
    ///</summary>
    bool TryResolveReferences()
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

    private protected override void Awake()
    {
        base.Awake();
        _cancellationTokenSource = new CancellationTokenSource();
        EnableBackgroundInput();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        WindowsAPI.DisableFullscreenOptimizations();
#endif
        StartCoroutine(OpacityHitTestLoop());
        UnityEngine.Debug.Log($"{LogPrefix} Awake");
    }

    // フォーカス状態やクリックスルーには一切関与せず、背景入力と最前面のみ維持する
    private void ApplyAlwaysActiveWindowMode()
    {
        Application.runInBackground = true;

        // 毎フレーム同じ値を再代入すると InputSystem 内部で再設定処理が走るため、変化時のみ書き込む
        if (InputSystem.settings.backgroundBehavior != InputSettings.BackgroundBehavior.IgnoreFocus)
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        }

        if (WindowsAPI.GetCurrentWindowHandle() != IntPtr.Zero)
        {
            WindowsAPI.SetCurrentWindowTopmost(true);
        }

        var hwnd = WindowsAPI.GetUniWinWindowHandle();
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // 外部要因で上書きされうるため定期的に再アサートするが、毎フレームは行わない
        if (_rawInputRegistered && Time.unscaledTime - _lastRawInputCheckTime < RawInputReassertIntervalSeconds)
        {
            return;
        }

        _lastRawInputCheckTime = Time.unscaledTime;

        bool registered = WindowsAPI.RegisterMouseRawInputSink();
        if (registered && !_rawInputRegistered)
        {
            _rawInputRegistered = true;
            UnityEngine.Debug.Log($"{LogPrefix} RegisterMouseRawInputSink succeeded.");
        }
    }

    /// <summary>
    /// UniWindowController 純正のヒットテストはフォーカス依存で判定が凍結するため、
    /// フォーカスが常に無効である前提で自前判定に置き換える。
    /// </summary>
    void UpdateManualClickThrough()
    {
        var uwc = UniWindowController.current;
        if (uwc == null) return;

        if (uwc.isHitTestEnabled)
        {
            uwc.isHitTestEnabled = false;
        }

        // ドラッグ中は UniWindowMoveHandle 自身が isClickThrough を管理するため上書きしない
        if (uniWindowMoveHandle != null && uniWindowMoveHandle.IsDragging)
        {
            return;
        }

        bool shouldClickThrough = !_isPointerOnOpaquePixel;
        if (uwc.isClickThrough != shouldClickThrough)
        {
            uwc.isClickThrough = shouldClickThrough;
        }
    }

    // Mouse.current.position はフォーカスが無いと更新されないため、OSのカーソル座標から算出する
    // UniWindowController 内部の変換式と同一（ボーダーレス前提でクライアント原点オフセットは0）
    static Vector2 GetCursorScreenPosition(UniWindowController uwc)
    {
        Vector2 globalCursor = UniWindowController.GetCursorPosition();
        Vector2 winPos = uwc.windowPosition;
        Vector2 clientSize = uwc.clientSize;

        float scaleX = Screen.width / Mathf.Max(1f, clientSize.x);
        float scaleY = Screen.height / Mathf.Max(1f, clientSize.y);

        return new Vector2(
            (globalCursor.x - winPos.x) * scaleX,
            (globalCursor.y - winPos.y) * scaleY);
    }

    /// <summary>
    /// UniWindowController のピクセル判定はフォーカス依存で凍結するため、
    /// 同じ alpha しきい値判定を自前で WaitForEndOfFrame ループとして実行する。
    /// </summary>
    IEnumerator OpacityHitTestLoop()
    {
        while (true)
        {
            yield return new WaitForEndOfFrame();
            RecomputeOpacityHit();
        }
    }

    void RecomputeOpacityHit()
    {
        var uwc = UniWindowController.current;
        if (uwc == null || uwc.currentCamera == null)
        {
            _isPointerOnOpaquePixel = false;
            return;
        }

        Vector2 screenPos = GetCursorScreenPosition(uwc);
        if (screenPos.x < 0f || screenPos.x >= Screen.width || screenPos.y < 0f || screenPos.y >= Screen.height)
        {
            _isPointerOnOpaquePixel = false;
            return;
        }

        // 透過していないウィンドウは常に不透明扱い
        if (!uwc.isTransparent)
        {
            _isPointerOnOpaquePixel = true;
            return;
        }

        if (_clickThroughPixelTexture == null)
        {
            _clickThroughPixelTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
        }

        try
        {
            _clickThroughPixelTexture.ReadPixels(new Rect(screenPos, Vector2.one), 0, 0);
            Color color = _clickThroughPixelTexture.GetPixels32()[0];
            bool rawHit = color.a >= uwc.opacityThreshold;

            // 輪郭のアンチエイリアスで 1 フレームだけ値が揺れると WS_EX_TRANSPARENT が連続で切り替わるため、
            // 連続して同じ値になったときだけ確定させる
            if (rawHit == _pendingOpaqueReading)
            {
                _pendingOpaqueStableFrames++;
            }
            else
            {
                _pendingOpaqueReading = rawHit;
                _pendingOpaqueStableFrames = 1;
            }

            if (_pendingOpaqueStableFrames >= OpaqueStableFrameThreshold)
            {
                _isPointerOnOpaquePixel = rawHit;
            }
        }
        catch (Exception)
        {
            // フレーム外 ReadPixels は断続的に発生しうるため、直前の判定を維持する
        }
    }

    /// <summary>
    /// フォーカスがなくてもマウス入力を受け取れるようにする。
    /// </summary>
    static void EnableBackgroundInput()
    {
        Application.runInBackground = true;
        // InputSystem.inputsettings.asset で m_BackgroundBehavior=2(IgnoreFocus) を設定済みだが、
        // ランタイムでも明示的に上書きすることで確実に適用する。
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        UnityEngine.Debug.Log($"{LogPrefix} EnableBackgroundInput: backgroundBehavior=IgnoreFocus");
    }

    void Start()
    {
        UnityEngine.Debug.Log($"{LogPrefix} Start");
        ResetCharacterRuntimeState();
        // キャラクター識別子を ProcessIdentifier.OptionVal から取得する
        _characterId = (ProcessIdentifier.Instance?.OptionVal ?? 0).ToString();
        UnityEngine.Debug.Log($"{LogPrefix} Start: characterId={_characterId}");
        InitializeCharacterStatePublisher();

        bool shouldSpawnDefault = ShouldSpawnDefaultCharacter();
        UnityEngine.Debug.Log($"{LogPrefix} ShouldSpawnDefaultCharacter={shouldSpawnDefault}");

        Spawn();
    }

    bool ShouldSpawnDefaultCharacter()
    {
        var processIdentifier = ProcessIdentifier.Instance;

        if (processIdentifier == null)
        {
            UnityEngine.Debug.LogWarning($"{LogPrefix} ProcessIdentifier is null. Default character spawn will be used.");
            return true;
        }

        return processIdentifier.OptionVal == 0;
    }

    void ResetCharacterRuntimeState()
    {
        _lastScrollFrame = -1;
        _hasScrollPending = false;
        _hasRightClickPending = false;
        _pendingScroll = 0f;
        _pendingScreenPosition = Vector2.zero;
        _pendingRightClickPosition = Vector2.zero;
        _isWindowResizeReady = false;

        _currentAction = GroundAction.Idle;
        _pendingAction = GroundAction.Idle;
        _hasPendingAction = false;
        _isWaitingLanding = false;
        _isTimerActive = false;
        _currentActionLimit = 0f;
        _isWalkRequested = false;
        _offsetApplyMode = OffsetMode.None;
        _offsetTransform = null;
        _hasOffsetCaptured = false;
        _offsetPosition = Vector3.zero;
        _lastYOffset = 0f;

        _fallSpeed = 0f;
        IsGrounded = false;
        _virtualScreenY = 0f;
        _hasVirtualScreenY = false;
        _fallRuntime.IsGravityPaused = false;
        _fallRuntime.IsDragDriven = false;
        _fallRuntime.HasFollowingWindow = false;
        _lastGroundSurfaceType = GroundSurfaceType.None;
        _followWindow = IntPtr.Zero;
        _lastFollowWindowRect = default;
        _windowOffsetX = 0f;
        _wasSupportFocused = false;
        _lastFocusedHwnd = IntPtr.Zero;
        _zOrderRuntime.IsTopmostSupport = false;
        _zOrderRuntime.IsTopmostDrag = false;
        _supportFailFrames = 0;

        _isWalkActive = false;
        _isWaitingTransition = false;
        _lastFacingDir = 0;
        _hasYawCaptured = false;
        _frontYaw = 0f;
        _blockedMoveFrames = 0;
        _isCharacterStartupInitialized = false;
        _isAnimatorInitialized = false;
    }

    void InitializeCharacterStartupState()
    {
        if (_isCharacterStartupInitialized || _animatorSync == null)
        {
            return;
        }

        CaptureFrontYaw();
        RegisterInputEvents();

        _animatorSync.SetFalling(false);
        _animatorSync.SetDragging(false);
        _animatorSync.SetNextGroundAction(_currentAction);
        _animatorSync.SetShouldGoNext(false);

        SetDraggingState(false);
        SyncOnWindowState(false);
        SetFallingState(false);

        _isCharacterStartupInitialized = true;
    }

    ///<summary>
    /// キャラクターをスポーンさせる
    /// </summary>
    /// <returns>
    /// スポーン処理を完了したらtrue。
    /// </returns>
    bool Spawn()
    {
        //すでにスポーン中なら離脱
        if (_isSpawning)
        {
            UnityEngine.Debug.Log($"{LogPrefix} Spawn skipped because spawning is already in progress.");
            return false;
        }

        UnityEngine.Debug.Log($"{LogPrefix} Spawn requested.");

        //非同期でスポーン処理を開始
        SpawnAsync().Forget();
        return true;
    }

    ///<summary>
    /// キャラクタースポーンの非同期処理
    /// </summary>
    async UniTaskVoid SpawnAsync()
    {
        _isSpawning = true;
        UnityEngine.Debug.Log($"{LogPrefix} SpawnAsync started.");

        try
        {
            if (!TryResolveReferences())
            {
                UnityEngine.Debug.LogWarning($"{LogPrefix} SpawnAsync aborted. Failed to resolve references.");
                return;
            }

            ClearCharacter();

            // バンドルパス・プレハブ名はメインプロセスが起動引数で渡す。キャラクタープロセスはカタログを使わない。
            int optionVal = ProcessIdentifier.Instance?.OptionVal ?? 0;
            string bundlePath = ProcessIdentifier.Instance?.BundlePath;
            string prefabName = ProcessIdentifier.Instance?.PrefabName;

            if (string.IsNullOrWhiteSpace(bundlePath) || string.IsNullOrWhiteSpace(prefabName))
            {
                UnityEngine.Debug.LogWarning($"{LogPrefix} SpawnAsync aborted. BundlePath or PrefabName not set in ProcessIdentifier. optionVal={optionVal}");
                return;
            }

            var (prefab, bundle) = await CharacterBundleLoader.LoadPrefabAsync(
                bundlePath, prefabName, _cancellationTokenSource.Token);

            if (prefab == null)
            {
                UnityEngine.Debug.LogWarning($"{LogPrefix} SpawnAsync aborted. Failed to load prefab for optionVal={optionVal}.");
                return;
            }

            _loadedBundle = bundle;
            UnityEngine.Debug.Log($"{LogPrefix} SpawnAsync: loaded prefab '{prefab.name}' for optionVal={optionVal}.");

            // プレハブを Instantiate してコンテナとする
            _modelContainer = Instantiate(prefab);
            _modelContainer.name = "ModelContainer";
            _modelContainer.transform.SetParent(null, false);
            _spawnedModel = _modelContainer;

            var cameraPosition = new Vector3(_targetCamera.transform.position.x, 0, _targetCamera.transform.position.z);
            _modelContainer.transform.LookAt(cameraPosition, Vector3.up);

            //設定からキャラクターの位置、回転、スケールを適用
            ApplyCharacterSettings();

            _modelContainer.transform.rotation = Quaternion.Euler(_modelContainer.transform.rotation.eulerAngles);

            // Animator はプレハブルートにアタッチ済みの前提。なければ子孯をフォールバック検索する。
            _modelAnimator = _modelContainer.GetComponent<Animator>();
            if (_modelAnimator == null)
            {
                _modelAnimator = _modelContainer.GetComponentInChildren<Animator>();
                if (_modelAnimator != null)
                {
                    UnityEngine.Debug.LogWarning($"{LogPrefix} Animator found in child, not prefab root. Consider attaching to root.");
                }
            }

            if (_modelAnimator == null)
            {
                UnityEngine.Debug.LogWarning($"{LogPrefix} SpawnAsync: Animator not found in prefab '{prefab.name}' (optionVal={optionVal}).");
            }

            var descriptr = _modelContainer.GetComponent<CompanionDescripter>();
            string charName = descriptr != null ? descriptr.CharacterName : _modelContainer.name;
            UnityEngine.Debug.Log($"{LogPrefix} SpawnAsync completed. character={charName}");

        } catch (OperationCanceledException)
        {
            UnityEngine.Debug.LogWarning($"{LogPrefix} SpawnAsync canceled.");
        } catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"{LogPrefix} SpawnAsync failed: {ex}");
        } finally
        {
            _isSpawning = false;
            UnityEngine.Debug.Log($"{LogPrefix} SpawnAsync finished. isSpawning={_isSpawning}");
            StartCoroutine(PublishCharacterSpawnCompletedRepeatedly());
        }
    }

    /// <summary>
    /// SkinnedMeshRendererをもとにAvatarを生成する。生成に失敗した場合はnullを返す。
    /// </summary>
    /// <param name="model">Avatarを生成するもとになるモデル</param>
    static Avatar CreateAvatarFromModel(GameObject model)
    {
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

    ///<summary>
    /// キャラクターに関連したリソースを解放する。AssetBundle もアンロードする。
    /// </summary>
    void ClearCharacter()
    {
        if (_modelContainer != null)
        {
            Destroy(_modelContainer);
        }

        _modelContainer = null;
        _spawnedModel = null;
        _modelAnimator = null;
        _loadedBundle?.Unload(false);
        _loadedBundle = null;
    }

    /// <summary>
    /// キャラクターに関する設定を適用する
    /// </summary>
    void ApplyCharacterSettings()
    {
        if (_modelContainer == null)
        {
            return;
        }

        var characterSettings = ApplicationSettings.Instance.Character;

        //スケールを調整
        float configuredScale = Mathf.Clamp(
            characterSettings.Scale,
            Mathf.Min(minScale, maxScale),
            Mathf.Max(minScale, maxScale));

        //スケールを適用して設定を更新
        _modelContainer.transform.localScale = Vector3.one * configuredScale;
        characterSettings.Scale = configuredScale;

        //キャラクターの位置
        _modelContainer.transform.position += new Vector3(characterSettings.PositionX, characterSettings.PositionY, 0);

        //キャラクターの回転
        Vector3 currentRotation = _modelContainer.transform.rotation.eulerAngles;
        _modelContainer.transform.rotation = Quaternion.Euler(
            currentRotation.x + characterSettings.RotationX,
            currentRotation.y + characterSettings.RotationY,
            currentRotation.z + characterSettings.RotationZ);

        //キャラクターのスケールにあわせてウィンドウサイズを調整
        TryResizeWindow(configuredScale * 2f);
    }

    /// <summary>
    /// 正面の向きを一度だけ更新する
    /// </summary>
    void CaptureFrontYaw()
    {
        if (_hasYawCaptured || ModelContainer == null)
        {
            return;
        }

        _frontYaw = ModelContainer.transform.rotation.eulerAngles.y;
        _hasYawCaptured = true;
    }

    // UniWindowController.Update() の後に実行されるよう LateUpdate に配置する
    void LateUpdate()
    {
        ApplyAlwaysActiveWindowMode();
        UpdateManualClickThrough();
    }

    void Update()
    {
        if(!_isAnimatorInitialized)
        {
            InitializeAnimatorSync();

            if (!_isAnimatorInitialized) return;
        }

        InitializeCharacterStartupState();

        ActionDecisionUpdate();

        WalkUpdate();

        FallingUpdate();

        ScrollUpdate();

        RightClickMenuUpdate();
    }

    /// <summary>
    /// アニメーターを初期化する
    /// </summary>
    /// <returns></returns>
    bool InitializeAnimatorSync()
    {
        _isAnimatorInitialized = false;

        if (ModelAnimator == null)
        {
            _animatorSync = null;

            // モデル生成前のフレームでは警告を抑制し、生成完了後の異常のみ1回だけ通知する。
            if (!_isSpawning && _modelContainer != null && !_hasLoggedAnimatorMissing)
            {
                UnityEngine.Debug.LogWarning($"{LogPrefix} InitializeAnimatorSync failed. ModelAnimator is null.");
                _hasLoggedAnimatorMissing = true;
            }

            return false;
        }

        // AnimationController はプレハブに事前設定済みのため動的注入不要
        _animatorSync = new AnimatorSync(ModelAnimator);
        UnityEngine.Debug.Log($"{LogPrefix} AnimatorSync initialized.");

        _hasLoggedAnimatorMissing = false;

        _isAnimatorInitialized = true;

        return true;
    }

    /// <summary>
    /// 次の行動決定の更新
    /// </summary>
    void ActionDecisionUpdate() 
    {
        if (_isWaitingLanding || IsFalling || IsDragging || IsOnWindow || !_isTimerActive || _currentAction == GroundAction.Walk) return;

        ApplyCharacterYOffset();
        _currentActionLimit -= Time.deltaTime;

        if (_currentActionLimit > 0f) return;

        SetPendingAction();

        if (_pendingAction == _currentAction)
        {
            _hasPendingAction = false;
            SetPendingAction();
        }

        _animatorSync.SetShouldGoNext(true);
        _isTimerActive = false;
        _currentActionLimit = 0f;
    }

    /// <summary>
    /// キャラクターのYオフセットを適用する
    /// </summary>
    void ApplyCharacterYOffset()
    {
        if (!TryInitOffsetTarget()) return;

        if (!_hasOffsetCaptured)
        {
            _offsetPosition = _offsetTransform.localPosition;
            _hasOffsetCaptured = true;
        }

        float targetYOffset = _offsetApplyMode switch
        {
            OffsetMode.Sit => _sitYOffset,
            OffsetMode.Ground => _groundYOffset,
            _ => 0f
        };

        float scaledYOffset = targetYOffset * Mathf.Abs(_offsetTransform.localScale.y);

        if (Mathf.Abs(_lastYOffset - scaledYOffset) <= 0.0001f) return;

        var local = _offsetPosition;
        local.y = _offsetPosition.y + scaledYOffset;
        _offsetTransform.localPosition = local;
        _lastYOffset = scaledYOffset;
    }

    /// <summary>
    /// オフセットを適用する対象のTransformを初期化する
    /// </summary>
    /// <returns>初期化に成功した場合はtrue</returns>
    bool TryInitOffsetTarget()
    {
        if (ModelContainer == null)
        {
            return false;
        }

        Transform resolved = ModelContainer.transform;

        if (resolved == null) return false;

        if (_offsetTransform != resolved)
        {
            _offsetTransform = resolved;
            _hasOffsetCaptured = false;
            _lastYOffset = 0f;
        }

        return true;
    }

    /// <summary>
    /// 歩きの行動における更新
    /// </summary>
    void WalkUpdate()
    {
        if (_currentAction != GroundAction.Walk || _isWaitingTransition) return;

        //ドラッグ中は歩きの更新を行わない
        if (IsDragging)
        {
            if (_isWalkActive)
            {
                ApplyFrontFacing();
            }

            _isWalkActive = false;
            _lastFacingDir = 0;
            _blockedMoveFrames = 0;
            return;
        }

        //歩きのリクエストがあれば歩きを開始する
        if (!_isWalkActive)
        {
            if (!StartWalk()) return;

            GetWalkRange(out float minX, out float maxX, out float currentRefX);
            _walkTargetX = PickWalkTargetX(currentRefX, minX, maxX);
            _walkCurrentX = currentRefX;
            _isWalkActive = true;
        }

        float nextX = Mathf.MoveTowards(
            _walkCurrentX,
            _walkTargetX,
            walkSpeed * Time.deltaTime);

        float currentX = _walkCurrentX;
        float deltaX = nextX - currentX;

        ApplyFacing(deltaX);

        bool moved = MoveWindow(deltaX, 0f);

        //ほとんど移動していない、もしくは移動がブロックされている場合は猶予フレームをカウントする
        if (Mathf.Abs(deltaX) <= 0.001f || !moved)
        {
           _blockedMoveFrames++;

            if (_blockedMoveFrames >= walkBlockFrame)
            {
                CompleteWalk();
                return;
            }
        } 
        else
        {
            _blockedMoveFrames = 0;
            _walkCurrentX = nextX;
        }

        float reachedDistance = Mathf.Abs(_walkTargetX - _walkCurrentX);

        //目的地への到達が閾値以内なら歩きを完了する
        if (reachedDistance <= Mathf.Max(0.1f, walkReachThreshold))
        {
            CompleteWalk();
        }
    }

    /// <summary>
    /// 歩きのリクエストがあれば開始する
    /// </summary>
    /// <returns>リクエストが承認された場合はtrue</returns>
    bool StartWalk()
    {
        if (!_isWalkRequested) return false;

        _isWalkRequested = false;
        return true;
    }

    /// <summary>
    /// 歩くことのできる範囲を取得する。現在のX座標も返す。
    /// </summary>
    /// <param name="minX">左端</param>
    /// <param name="maxX">右端</param>
    /// <param name="currentX">現在のX座標</param>
    void GetWalkRange(out float minX, out float maxX, out float currentX)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        //ウィンドウの移動が制限される範囲を考慮して歩ける範囲を決定する
        if (WindowsAPI.TryGetCurrentWindowRect(out var rect) && WindowsAPI.TryGetWorkArea(out var workArea))
        {
            float workLeft = workArea.left;
            float workRight = workArea.right;
            minX = workLeft + walkPadding;
            maxX = Mathf.Max(minX, workRight - walkPadding);

            float windowCenterX = (rect.left + rect.right) * 0.5f;
            currentX = windowCenterX;
            return;
        }
#endif

        //タスクバーや擬似タスクバーを考慮して歩ける範囲を決定する
        if (ScreenSpaceTransformUtility.TryGetScreenPosition(_targetCamera, ModelContainer.transform, out var modelScreen))
        {
            minX = walkPadding;
            maxX = Mathf.Max(minX, Screen.width - walkPadding);
            currentX = Mathf.Clamp(modelScreen.x, minX, maxX);
            return;
        }

        minX = walkPadding;
        maxX = Mathf.Max(minX, Screen.width - walkPadding);
        currentX = (minX + maxX) * 0.5f;
    }

    /// <summary>
    /// 目的地への距離と歩ける範囲を考慮して次の歩きの目的地を決定する
    /// </summary>
    /// <param name="currentX">現在のX座標</param>
    /// <param name="minX">歩行可能な最小X座標</param>
    /// <param name="maxX">歩行可能な最大X座標</param>
    /// <returns>次の歩きの目的地X座標</returns>
    float PickWalkTargetX(float currentX, float minX, float maxX)
    {
        float randomDistance = UnityEngine.Random.Range(walkAction.minRange, walkAction.maxRange);

        int direction = UnityEngine.Random.value < 0.5f ? -1 : 1;
        float targetX = currentX + randomDistance * direction;
        targetX = Mathf.Clamp(targetX, minX, maxX);

        if (Mathf.Abs(targetX - currentX) < Mathf.Max(8f, walkAction.minRange * 0.5f))
        {
            float oppositeTarget = Mathf.Clamp(currentX + randomDistance * -direction, minX, maxX);
            if (Mathf.Abs(oppositeTarget - currentX) > Mathf.Abs(targetX - currentX))
            {
                targetX = oppositeTarget;
            }
        }

        return targetX;
    }

    /// <summary>
    /// キャラクターを正面に向ける
    /// </summary>
    void ApplyFrontFacing()
    {
        Vector3 euler = ModelContainer.transform.rotation.eulerAngles;
        ModelContainer.transform.rotation = Quaternion.Euler(euler.x, _frontYaw, euler.z);
        _lastFacingDir = 0;
    }

    /// <summary>
    /// キャラクターの向きを更新する
    /// </summary>
    /// <param name="deltaX">X方向の移動量</param>
    void ApplyFacing(float deltaX)
    {
        if (Mathf.Abs(deltaX) <= Mathf.Epsilon) return;

        int directionSign = deltaX > 0f ? 1 : -1;

        if (_lastFacingDir == directionSign) return;

        _lastFacingDir = directionSign;
        Vector3 euler = ModelContainer.transform.rotation.eulerAngles;
        float yaw = directionSign > 0 ?  -90 : 90;
        ModelContainer.transform.rotation = Quaternion.Euler(euler.x, yaw, euler.z);
    }

    /// <summary>
    /// 歩行のフラグをリセットして正面を向かせる。歩行の完了を通知するための前処理も行う。 
    /// </summary>
    void CompleteWalk()
    {
        _isWalkActive = false;
        _isWaitingTransition = true;
        _blockedMoveFrames = 0;
        ApplyFrontFacing();
        NotifyWalkCompleted();
    }

    /// <summary>
    /// 歩きの完了を通知する。次の行動を選択して保留し、アニメーターに次の行動への遷移を許可する。
    /// </summary>
    void NotifyWalkCompleted()
    {
        if (_currentAction != GroundAction.Walk || _isWaitingLanding || IsFalling) return;

        SetPendingAction();
        ApplyPendingGroundAction();
        _animatorSync.SetShouldGoNext(true);
        _isTimerActive = false;
        _currentActionLimit = 0f;
    }

    /// <summary>
    /// 保留されている次の行動を現在の行動に適用する
    /// </summary>
    public void ApplyPendingGroundAction()
    {
        //ウィンドウの上にいる場合は座る行動に固定する
        if (IsOnWindow)
        {
            _currentAction = GroundAction.Sit;
            _hasPendingAction = false;
            _currentActionLimit = 0f;
            _isTimerActive = false;
            _animatorSync.SetShouldGoNext(false);

            return;
        }

        if (!_hasPendingAction)
        {
            SetPendingAction();
        }
        
        _currentAction = _pendingAction;
        _hasPendingAction = false;
        _currentActionLimit = PickActionDuration(_currentAction);
        _isTimerActive = false;

        SetPendingAction();

        _animatorSync.SetNextGroundAction(_currentAction);
        _animatorSync.SetShouldGoNext(false);
    }

    /// <summary>
    /// 落下の更新処理
    /// </summary>
    void FallingUpdate()
    {
        // uniWindowMoveHandle はフォーカス喪失時に固まった古いマウス座標で誤ってドラッグ判定することがあるため、
        // 自前のネイティブカーソル座標ベースの判定で実際にキャラクター上か確認する
        bool isDraggingUniwindow = uniWindowMoveHandle.IsDragging && _isPointerOnOpaquePixel;

        //uniWindowMoveHandleによるドラッグがされてなく、落下の更新がドラッグ状態で行われている場合はドラッグ状態を解除する
        if (_fallRuntime.IsDragDriven && !isDraggingUniwindow)
        {
            SetDraggingState(false);
            _fallRuntime.IsDragDriven = false;
        }

        //ドラッグ状態であれば落下の更新は行わない
        if (IsDragging || isDraggingUniwindow)
        {
            //ドラッグ状態で重力が無効でなければ重力を無効にする
            if (!_fallRuntime.IsGravityPaused)
            {
                IsGrounded = false;
                _lastGroundSurfaceType = GroundSurfaceType.None;
                ClearFollowTarget();
                _hasVirtualScreenY = false;
                _fallRuntime.IsGravityPaused = true;
                _fallSpeed = 0f;
                SyncOnWindowState(false);
                SetDraggingState(true);
                _fallRuntime.IsDragDriven = isDraggingUniwindow;
                SetFallingState(false);
            }

            return;
        }

        //ドラッグ状態でないときに重力が無効になっている場合は重力を有効にする
        if (_fallRuntime.IsGravityPaused)
        {
            _fallRuntime.IsGravityPaused = false;
            SetDraggingState(false);
            _fallRuntime.IsDragDriven = false;
        }

        FollowSupportWindow();

        if (!ScreenSpaceTransformUtility.TryGetScreenPosition(_targetCamera, ModelContainer.transform, out var modelScreen)) return;

        if (!_hasVirtualScreenY)
        {
            _virtualScreenY = modelScreen.y;
            _hasVirtualScreenY = true;
        }

        float groundY = GetGroundY();
        bool wasGrounded = IsGrounded;

        if (TryProbeSupportDistance(out SupportDistProbeResult supportProbe))
        {
            //すでに追従中のウィンドウなら着地を確定させる
            if (_followWindow != IntPtr.Zero
                     && supportProbe.SupportHandle == _followWindow
                    && supportProbe.SurfaceType == GroundSurfaceType.WindowTop)
            {
                CommitGroundedTransition(
                    -supportProbe.SignedDistance,
                    supportProbe.SurfaceType,
                    supportProbe.SupportHandle,
                    supportProbe.SupportRect,
                    onWindow: true,
                    wasGrounded: wasGrounded);

                return;
            }

            //対象との距離が閾値以下なら着地を確定させる
            if (supportProbe.SignedDistance <= groundedPixels)
            {
                CommitGroundedTransition(
                    -supportProbe.SignedDistance,
                    supportProbe.SurfaceType,
                    supportProbe.SupportHandle,
                    supportProbe.SupportRect,
                    onWindow: supportProbe.SurfaceType == GroundSurfaceType.WindowTop,
                    wasGrounded: wasGrounded);

                return;
            }

            //落下速度を計算してウィンドウを移動させる
            _fallSpeed = Mathf.Min(maxFallSpeed, _fallSpeed + gravity * Time.deltaTime);
            float clampedDownMove = Mathf.Clamp(_fallSpeed * Time.deltaTime, 0f, supportProbe.SignedDistance);

            if (clampedDownMove > 0f)
            {
                MoveWindow(0f, -clampedDownMove);
            }

            //移動によって着地できるようになったら着地を確定させる、そうでなければ引き続き落下状態を維持する
            if (clampedDownMove >= supportProbe.SignedDistance - groundedPixels)
            {
                CommitGroundedTransition(
                    deltaY: 0f,
                    supportProbe.SurfaceType,
                    supportProbe.SupportHandle,
                    supportProbe.SupportRect,
                    onWindow: supportProbe.SurfaceType == GroundSurfaceType.WindowTop,
                    wasGrounded: wasGrounded);
            } 
            else
            {
                CommitAirTransition();
            }

            return;
        }

        //落下対象が見つからなければ落下を継続し、地面とみなすY座標に達していれば着地を確定させる
        if (_virtualScreenY <= groundY + groundedPixels)
        {
            float deltaY = groundY - _virtualScreenY;
            _virtualScreenY = groundY;

            MoveWindow(0f, deltaY);

            CommitGroundedNoSupport();

            return;
        }

        _fallSpeed = Mathf.Min(maxFallSpeed, _fallSpeed + gravity * Time.deltaTime);
        float nextScreenY = _virtualScreenY - _fallSpeed * Time.deltaTime;

        if (nextScreenY <= groundY)
        {
            nextScreenY = groundY;
            CommitGroundedNoSupport();
        } else
        {
            CommitAirTransition();
        }

        float moveDeltaY = nextScreenY - _virtualScreenY;
        _virtualScreenY = nextScreenY;
        MoveWindow(0f, moveDeltaY);
    }

    /// <summary>
    /// ウィンドウに乗っているときに追従させる。条件を満たさないときは解除する
    /// </summary>
    void FollowSupportWindow()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!IsGrounded || _lastGroundSurfaceType != GroundSurfaceType.WindowTop) return;

        if (_followWindow == IntPtr.Zero || !_fallRuntime.HasFollowingWindow) return;

        //対象が追従に適さないとき解除する
        if (!TryGetWindowTrackable(_followWindow, out var rect, out bool isMinimized))
        {
            _supportFailFrames++;
            int graceFrames = Mathf.Max(0, supportGraceFrames);

            if (isMinimized || _supportFailFrames > graceFrames)
            {
                DetachWindowTop();
            }

            return;
        }

        _supportFailFrames = 0;

        if (IsSupportOccluded(_followWindow, rect))
        {
            DetachWindowTop();
            return;
        }
        
        //追従しているウィンドウの移動にあわせてキャラクターウィンドウを移動させる
        if (WindowsAPI.TryGetCurrentWindowRect(out var currentAppRect))
        {
            float appCenterX = (currentAppRect.left + currentAppRect.right) * 0.5f;

            if (appCenterX < rect.left || appCenterX > rect.right)
            {
                DetachWindowTop();
                return;
            }

            float desiredLeft = rect.left + _windowOffsetX;
            float deltaXToTarget = desiredLeft - currentAppRect.left;
            float deltaY = rect.top - _lastFollowWindowRect.top;
            bool supportWindowMoved = false;
            
            if (Mathf.Abs(deltaXToTarget) > 0.001f || Mathf.Abs(deltaY) > 0.001f)
            {
                supportWindowMoved = true;
                MoveWindow(deltaXToTarget, -deltaY);
            }

            float sitYOffsetPixels = 0f;
            float supportTop = rect.top + sitYOffsetPixels;
            float appCenterY = (currentAppRect.top + currentAppRect.bottom) * 0.5f;
            float gapToSupportTop = supportTop - appCenterY;
            
            if (Mathf.Abs(gapToSupportTop) > Mathf.Max(0.001f, groundedPixels))
            {
                supportWindowMoved = true;
                MoveWindow(0f, -gapToSupportTop);
            }

            if (supportWindowMoved)
            {
                // 支持ウィンドウの移動中はキャラクターを常にその前面へ置く。
                WindowsAPI.PlaceCurrentWindowAbove(_followWindow);
            }
        }

        _lastFollowWindowRect = rect;
#endif
    }

    /// <summary>
    /// キャラクターをウィンドウ上から切り離す
    /// </summary>
    void DetachWindowTop()
    {
        ClearFollowTarget();
        IsGrounded = false;
        _lastGroundSurfaceType = GroundSurfaceType.None;
        SyncOnWindowState(false);
        SetFallingState(true);
    }

    /// <summary>
    /// タスクバーの高さを取得する。タスクバーがない場合は0を返す
    /// </summary>
    /// <returns>タスクバーの高さ</returns>
    float GetGroundY()
    {
#if UNITY_EDITOR
        var pseudoBar = FindFirstObjectByType<PseudoTaskbar>();

        if (pseudoBar != null)
        {
            float bestPseudo = 0f;
            if (pseudoBar.IsEnabled)
            {
                if (pseudoBar.TryGetScreenRect(out var rect))
                {
                    bestPseudo = Mathf.Max(bestPseudo, rect.yMax);
                }
            }
        }
#endif

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        float best = 0f;

        //API基準のタスクバーの高さを基準にする
        if (WindowsAPI.TryGetTaskbarRect(out var taskbarRect))
        {
            float nativeRectY = Mathf.Ceil(taskbarRect.yMax);

            if (nativeRectY > 0f)
            {
                best = nativeRectY;
            }
        }

        //画面サイズから作業エリアの高さを引いて比較対象にし、より高い方を基準にする
        if (WindowsAPI.TryGetWorkArea(out var workArea))
        {
            float dpiScale = Mathf.Max(0.01f, WindowsAPI.GetDPIScale());

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

    /// <summary>
    /// 着地対象を探索しながら着地点までの距離を取得する。着地対象が見つかった場合はその情報も返す。
    /// </summary>
    /// <param name="res">着地点の情報</param>
    /// <returns>着地対象が見つかったかどうか</returns>
    bool TryProbeSupportDistance(out SupportDistProbeResult res)
    {
        res = default;
        res.SurfaceType = GroundSurfaceType.None;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!WindowsAPI.TryGetCurrentWindowRect(out var windowRect)) return false;

        if (!WindowsAPI.TryGetWorkArea(out var workArea)) return false;

        float centerY = (windowRect.top + windowRect.bottom) * 0.5f;
        res.Bottom = centerY;

        float centerX = (windowRect.left + windowRect.right) * 0.5f;

        float fallbackY = workArea.bottom;
        float fallbackGap = fallbackY - centerY;

        if (_followWindow != IntPtr.Zero && _fallRuntime.HasFollowingWindow)
        {
            if (TryGetWindowTrackable(_followWindow, out var trackedRect, out bool isMinimized))
            {
                _supportFailFrames = 0;
                res.SignedDistance = trackedRect.top - centerY;
                res.SurfaceType = GroundSurfaceType.WindowTop;
                res.SupportY = trackedRect.top;
                res.SupportHandle = _followWindow;
                res.SupportRect = trackedRect;

                return true;
            }

            _supportFailFrames++;

            //安定化のため、追従対象のウィンドウが条件を満たさないときにすぐに追従を解除せず、一定フレーム数までは追従状態を維持する
            if (!isMinimized && _supportFailFrames <= supportGraceFrames)
            {
                res.SignedDistance = _lastFollowWindowRect.top - centerY;
                res.SurfaceType = GroundSurfaceType.WindowTop;
                res.SupportY = _lastFollowWindowRect.top;
                res.SupportHandle = _followWindow;
                res.SupportRect = _lastFollowWindowRect;
                return true;
            }
        }

        //キャラクターの下にある着地対象を探索する。ウィンドウの上端が着地対象となりうる条件を満たしている場合はそのウィンドウの情報を返す。
        if (TryFindSupportWindow(
                centerX,
                centerY,
                fallbackY,
                out float supportGap,
                out IntPtr candidateHandle,
                out WindowsAPI.RECT candidateRect))
        {
            res.SignedDistance = supportGap;
            res.SurfaceType = GroundSurfaceType.WindowTop;
            res.SupportY = candidateRect.top;
            res.SupportHandle = candidateHandle;
            res.SupportRect = candidateRect;
            return true;
        }

        res.SignedDistance = fallbackGap;
        res.SurfaceType = GroundSurfaceType.Taskbar;
        res.SupportY = fallbackY;

        return true;
#else
        return false;
#endif
    }

    /// <summary>
    /// 着地対象となりうるウィンドウを探索する。条件を満たすウィンドウが見つかった場合はそのウィンドウのハンドルと矩形情報を返す。
    /// </summary>
    /// <param name="centerX">キャラクターウィンドウの中心X座標</param>
    /// <param name="centerY">キャラクターウィンドウの中心Y座標</param>
    /// <param name="fallbackY">フォールバックY座標</param>
    /// <param name="dist">見つかったウィンドウまでの距離</param>
    /// <param name="supportHandle">見つかったウィンドウのハンドル</param>
    /// <param name="supportRect">見つかったウィンドウの矩形情報</param>
    /// <returns>条件を満たすウィンドウが見つかったかどうか</returns>
    bool TryFindSupportWindow(float centerX, float centerY, float fallbackY, out float dist, out IntPtr supportHandle,  out WindowsAPI.RECT supportRect)
    {
        dist = 0f;
        supportHandle = IntPtr.Zero;
        supportRect = default;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        IntPtr currentHwnd = WindowsAPI.GetCurrentWindowHandle();
        IntPtr foregroundHwnd = WindowsAPI.GetForegroundWindowHandle();

        bool isDominant = foregroundHwnd != IntPtr.Zero
                    && foregroundHwnd != currentHwnd
                    && WindowsAPI.IsWindowVisible(foregroundHwnd)
                    && !WindowsAPI.IsWindowMinimized(foregroundHwnd)
                    && (WindowsAPI.IsWindowMaximized(foregroundHwnd) || WindowsAPI.IsWindowFullscreen(foregroundHwnd));

        WindowsAPI.RECT foregroundRect = default;

        if (isDominant)
        {
            isDominant = WindowsAPI.GetWindowRect(foregroundHwnd, out foregroundRect);
        }

        float bestGap = float.MaxValue;
        bool isFound = false;
        var windows = WindowsAPI.GetTopLevelWindows();
        
        for (int i = 0; i < windows.Count; i++)
        {
            var info = windows[i];

            if (info == null) continue;

            IntPtr hWnd = info.hWnd;

            if (hWnd == IntPtr.Zero || hWnd == currentHwnd || !WindowsAPI.IsWindowVisible(hWnd) || WindowsAPI.IsWindowMinimized(hWnd))
                continue;

            WindowsAPI.RECT rect = info.rect;
            float width = rect.right - rect.left;
            float height = rect.bottom - rect.top;

            if (width < 50f || height < 50f || centerX < rect.left || centerX > rect.right)
                continue;

            if (isDominant
                && !WindowsAPI.AreSameRootOwnerWindow(foregroundHwnd, hWnd)
                && centerX >= foregroundRect.left
                && centerX <= foregroundRect.right
                && foregroundRect.bottom > rect.top)
            {
                continue;
            }

            int probeX = Mathf.Clamp(Mathf.RoundToInt(centerX), rect.left + 1, rect.right - 1);
            int probeY = Mathf.Clamp(rect.top + 1, rect.top, rect.bottom - 1);
            IntPtr topWindow = WindowsAPI.GetWindowFromScreenPoint(probeX, probeY);

            if (topWindow == currentHwnd)
            {
                IntPtr underlying = FindUnderlyingWindow(currentHwnd, probeX, probeY);

                if (underlying != IntPtr.Zero)
                {
                    topWindow = underlying;
                }
            }

            if (topWindow != IntPtr.Zero
                && topWindow != currentHwnd
                && !WindowsAPI.AreSameRootOwnerWindow(topWindow, hWnd))
            {
                continue;
            }

            if (IsSupportOccluded(hWnd, rect)) continue;

            float supportTopY = rect.top;

            if (supportTopY > fallbackY + Mathf.Max(1f, groundedPixels)) continue;

            float gap = supportTopY - centerY;

            if (gap < -Mathf.Max(1f, groundedPixels)) continue;

            if (gap < bestGap)
            {
                bestGap = gap;
                supportHandle = hWnd;
                supportRect = rect;
                isFound = true;
            }
        }

        if (!isFound) return false;

        dist = bestGap;

        return true;
#else
        return false;
#endif
    }

    ///<summary>
    /// 指定したウィンドウより下にあるウィンドウを探す。条件を満たすウィンドウが見つかった場合はそのウィンドウのハンドルを返す。
    /// </summary>
    /// <param name="currentHwnd">現在のウィンドウハンドル</param>
    /// <param name="x">探す位置のX座標</param>
    /// <param name="y">探す位置のY座標</param>
    /// <returns>条件を満たすウィンドウのハンドル、見つからなければIntPtr.Zero</returns>
    IntPtr FindUnderlyingWindow(IntPtr currentHwnd, int x, int y)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (currentHwnd == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr walker = currentHwnd;

        for (int i = 0; i < 64; i++)
        {
            walker = WindowsAPI.GetNextWindow(walker);

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

    /// <summary>
    /// 着地時のキャラクターの状態確定処理。着地対象の情報を受け取り、キャラクターの位置補正や状態の更新を行う。
    /// </summary>
    /// <param name="deltaY">着地対象とのY方向の距離</param>
    /// <param name="surfaceType">着地対象の種類</param>
    /// <param name="supportHandle">着地対象のウィンドウハンドル</param>
    /// <param name="supportRect">着地対象の矩形情報</param>
    /// <param name="onWindow">着地対象がウィンドウの上端であるかどうか</param>
    /// <param name="wasGrounded">着地前に地面についていたかどうか</param>
    void CommitGroundedTransition(float deltaY, GroundSurfaceType surfaceType, IntPtr supportHandle, WindowsAPI.RECT supportRect, bool onWindow, bool wasGrounded)
    {
        if (Mathf.Abs(deltaY) > 0.001f)
        {
            MoveWindow(0f, deltaY);
        }

        SetDraggingState(false);
        _fallSpeed = 0f;
        IsGrounded = true;
        _lastGroundSurfaceType = surfaceType;
        UpdateFollowTarget(surfaceType, supportHandle, supportRect);
        SyncOnWindowState(onWindow);
        SetFallingState(false);
        SetTopmostForTaskbar();

        if (!wasGrounded)
        {
            ApplyZOrder(surfaceType, supportHandle);
        }
    }
    
    ///<summary>
    /// 対象が既存の追従ウィンドウでなければ新しく追従ウィンドウとして設定する
    /// </summary>
    /// <param name="surfaceType">着地対象の種類</param>
    /// <param name="supportHandle">着地対象のウィンドウハンドル</param>
    /// <param name="supportRect">着地対象の矩形情報</param>
    void UpdateFollowTarget(GroundSurfaceType surfaceType, IntPtr supportHandle, WindowsAPI.RECT supportRect)
    {
        if (surfaceType != GroundSurfaceType.WindowTop || supportHandle == IntPtr.Zero)
        {
            ClearFollowTarget();

            return;
        }

        bool isNewTarget = _followWindow != supportHandle || !_fallRuntime.HasFollowingWindow;

        _followWindow = supportHandle;
        _lastFollowWindowRect = supportRect;
        _fallRuntime.HasFollowingWindow = true;
        _supportFailFrames = 0;

        if (isNewTarget && WindowsAPI.TryGetCurrentWindowRect(out var appRect))
        {
            _windowOffsetX = appRect.left - supportRect.left;
        }
    }

    /// <summary>
    /// タスクバーに着地している場合に前面固定を設定する。
    /// </summary>
    void SetTopmostForTaskbar()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!IsGrounded || _lastGroundSurfaceType != GroundSurfaceType.Taskbar || _zOrderRuntime.IsTopmostSupport) return;

        _zOrderRuntime.IsTopmostSupport = WindowsAPI.SetCurrentWindowTopmost(true);
#endif
    }

    ///<summary>
    /// キャラクターが空中にいる状態に定義する
    /// </summary>
    void CommitAirTransition()
    {
        IsGrounded = false;
        _lastGroundSurfaceType = GroundSurfaceType.None;
        ClearFollowTarget();
        SyncOnWindowState(false);
        SetFallingState(true);
    }

    ///<summary>
    /// キャラクターが地面に着地している状態に定義する
    /// </summary>
    void CommitGroundedNoSupport()
    {
        _fallSpeed = 0f;
        IsGrounded = true;
        _lastGroundSurfaceType = GroundSurfaceType.None;
        ClearFollowTarget();
        SyncOnWindowState(false);
        SetFallingState(false);
    }

    /// <summary>
    /// スクロール入力から十分なスクロール量があった場合にキャラクターウィンドウの拡大率を変更する
    /// </summary>
    void ScrollUpdate()
    {
        if (!_isInputRegistered)
        {
            RegisterInputEvents();
        }

        if (Time.frameCount == _lastScrollFrame || !TryResolveReferences() || IsDragging) return;

        float scroll;
        Vector2 screenPosition;

        //スクロールの入力が保留されている場合は保留されている値を使用する。そうでない場合は現在のマウスのスクロール値と位置を使用する。
        if (_hasScrollPending)
        {
            scroll = _pendingScroll;
            screenPosition = _pendingScreenPosition;
            _hasScrollPending = false;
            _pendingScroll = 0f;
            _pendingScreenPosition = Vector2.zero;

            if (Mathf.Approximately(scroll, 0f))
            {
                var mouse = Mouse.current;

                if (mouse == null) return;

                scroll = mouse.scroll.ReadValue().y;

                if (Mathf.Approximately(scroll, 0f)) return;

                screenPosition = mouse.position.ReadValue();
            }
        } 
        else
        {
            var mouse = Mouse.current;
            
            if (mouse == null) return;

            scroll = mouse.scroll.ReadValue().y;

            if (Mathf.Approximately(scroll, 0f)) return;

            screenPosition = mouse.position.ReadValue();
        }

        if (!IsPointerOnCharacter(screenPosition)) return;

        float notch = scroll / 120f * scrollMultiplier;

        if (Mathf.Approximately(notch, 0f))
        {
            notch = Mathf.Sign(scroll);
        }

        Transform modelContainer = ModelContainer.transform;

        float currentScale = modelContainer.localScale.x;
        float nextScale = currentScale + (notch * scrollStep);
        nextScale = Mathf.Clamp(nextScale, minScale, maxScale);

        modelContainer.localScale = Vector3.one * nextScale;

        var appSettings = ApplicationSettings.Instance;

        nextScale *= 2f;

        if (appSettings != null && appSettings.Character != null)
        {
            appSettings.Character.Scale = nextScale;
        }

        TryResizeWindow(nextScale);

        _lastScrollFrame = Time.frameCount;
    }

    ///<summary>
    /// 右クリック入力があった場合にメインプロセスへ設定ダイアログ表示をリクエストする
    /// </summary>
    void RightClickMenuUpdate()
    {
        if (!_hasRightClickPending || !IsPointerOnCharacter(_pendingRightClickPosition)) return;

        _hasRightClickPending = false;

        try
        {
            using var webSocket = new WebSocket(CharacterStateWebSocketUrl);
            webSocket.Connect();

            if (!webSocket.IsAlive)
            {
                UnityEngine.Debug.LogWarning($"{LogPrefix} Failed to request settings dialog via WebSocket. endpoint={CharacterStateWebSocketUrl}");
                return;
            }

            webSocket.Send(Constant.UIOpenSettingsDialogMessage);
            webSocket.Close();
        }
        catch
        {
        }
    }


    /// <summary>
    /// ウィンドウが追従可能かを判定する。追従可能なウィンドウであれば、その矩形と最小化されているかの状態を返す。
    /// </summary>
    /// <param name="hWnd">対象ウィンドウのハンドル</param>
    /// <param name="rect">ウィンドウの矩形</param>
    /// <param name="minimized">ウィンドウが最小化されているかどうか</param>
    /// <returns>追従可能なウィンドウであればtrue、それ以外はfalse</returns>
    bool TryGetWindowTrackable(IntPtr hWnd, out WindowsAPI.RECT rect, out bool minimized)
    {
        rect = default;
        minimized = false;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (hWnd == IntPtr.Zero) return false;

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

        if (TryGetTopLevelWindow(hWnd, out rect))
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

    ///<summary>
    /// ウィンドウのリストから指定したハンドルのウィンドウを探し、その情報を返す。見つからなかった場合はfalseを返す。
    /// </summary>
    /// <param name="hWnd">探すウィンドウのハンドル</param>
    /// <param name="rect">見つかったウィンドウの矩形情報</param>
    /// <returns>見つかった場合はtrue、それ以外はfalse</returns>
    bool TryGetTopLevelWindow(IntPtr hWnd, out WindowsAPI.RECT rect)
    {
        rect = default;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        var candidateWindows = WindowsAPI.GetTopLevelWindows();

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

    public void OnApplicationQuit()
    {
        UnityEngine.Debug.Log($"{LogPrefix} OnApplicationQuit");
        _cancellationTokenSource?.Cancel();
        UnregisterInputEvents();
    }

    void OnDestroy()
    {
        UnityEngine.Debug.Log($"{LogPrefix} OnDestroy");
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        _loadedBundle?.Unload(false);
        _loadedBundle = null;
        CleanupNetMQ();
    }

    /// <summary>
    /// ドラッグ状態を設定する
    /// </summary>
    /// <param name="dragging">ドラッグ中かどうか</param>
    void SetDraggingState(bool dragging)
    {
        if (_animatorSync.GetBool(Constant.AnimatorIsDragging) == dragging) return;

        _animatorSync.SetDragging(dragging);

        //ドラッグ中は次の行動遷移を無効にして最前面にする
        if (dragging)
        {
            _animatorSync.SetShouldGoNext(false);
            _isTimerActive = false;
            SetCharacterFront();
        }
        else
        {
            ReleaseCharacterFront();
        }
    }

    /// <summary>
    /// 落下状態を設定する
    /// </summary>
    /// <param name="falling">落下中かどうか</param>
    void SetFallingState(bool falling)
    {
        if (_animatorSync.GetBool(Constant.AnimatorIsFalling) == falling) return;

        _animatorSync.SetFalling(falling);

        //落下中は次の行動遷移を無効にする
        if (falling)
        {
            _animatorSync.SetShouldGoNext(false);
            _isTimerActive = false;
            _isWalkRequested = false;
        }
    }

    ///<summary>
    /// アニメーターから実行する
    /// 地面にいるときの行動遷移の処理。現在の行動の種類に応じて次の行動への遷移を許可するタイミングを制御する。
    /// </summary>
    public void OnEnterGroundAction()
    {
        if (IsOnWindow)
        {
            _currentAction = GroundAction.Sit;
            _pendingAction = GroundAction.Sit;
            _hasPendingAction = false;
            _isTimerActive = false;
            _currentActionLimit = 0f;
            _animatorSync.SetShouldGoNext(false);
            return;
        }

        _currentAction = GetCurrentGroundAction();

        _isTimerActive = true;
        _currentActionLimit = PickActionDuration(_currentAction);
        SetPendingAction();
        _animatorSync.SetNextGroundAction(_pendingAction);

        _animatorSync.SetShouldGoNext(false);

        if (_animatorSync.GetBool(Constant.AnimatorIsWalking))
        {
            _isWalkRequested = true;
        }
    }

    /// <summary>
    /// アニメーターパラメータから現在のタスクバーでの行動の種類を取得する
    /// </summary>
    /// <returns>現在のタスクバーでの行動の種類</returns>
    GroundAction GetCurrentGroundAction()
    {
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

    /// <summary>
    /// アニメーターから実行する
    /// 着地アニメーションの開始時の処理。着地の状態に応じて次の行動を保留し、行動遷移のタイミングを制御する。
    /// </summary>
    public void OnEnterLandingAnimation()
    {

        _isWaitingLanding = true;
        _isTimerActive = false;
        _hasPendingAction = true;
        _currentActionLimit = 0f;

        if (!IsOnWindow)
        {
            _pendingAction = GroundAction.Idle;
            _animatorSync.SetNextGroundAction(_pendingAction);
        }
        else
        {
            _pendingAction = GroundAction.Sit;
        }

        _animatorSync.SetShouldGoNext(false);
    }

    /// <summary>
    /// アニメーターから実行する
    /// 着地アニメーションの終了時の処理。着地の状態に応じて行動を確定し、次の行動への遷移を許可する。
    /// </summary>
    public void OnExitLandingAnimation()
    {
        _isWaitingLanding = false;
        bool isOnWindow = false;

        if (_animatorSync != null)
        {
            bool isCurrentOnWindow = _animatorSync.GetBool(Constant.AnimatorIsOnWindow);
            isOnWindow = isCurrentOnWindow;

            bool isSitRequested = _animatorSync.GetBool(Constant.AnimatorIsSittingWindow);
            _animatorSync.SetIsSittingWindow(isCurrentOnWindow && isSitRequested);
        }

        _isTimerActive = false;

        if (isOnWindow)
        {
            _currentAction = GroundAction.Sit;
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

    /// <summary>
    /// アニメーターから実行する
    /// 座る状態のYオフセットを設定する。
    /// </summary>
    /// <param name="yOffsetRatio"></param>
    public void SetSitYOffset(float yOffsetRatio)
    {
        _sitYOffset = yOffsetRatio;
        _offsetApplyMode = OffsetMode.Sit;
    }

    /// <summary>
    /// アニメーターから実行する
    /// 座る状態のYオフセットをリセットする。リセット後は座る状態以外のときはオフセットが適用されなくなる。
    /// </summary>
    public void ResetSitYOffset()
    {
        _sitYOffset = 0f;
        if (_offsetApplyMode == OffsetMode.Sit)
        {
            _offsetApplyMode = OffsetMode.None;
        }
    }

    /// <summary>
    /// 次の行動を選択して保留する。
    /// </summary>
    void SetPendingAction()
    {
        //次の行動が保留されていて、かつそれが現在の行動と異なる場合はすでに次の行動が選ばれているので離脱
        if (_hasPendingAction && _pendingAction != _currentAction)
        {
            return;
        }

        //ウィンドウの上にいるときは座る以外の行動を選ばない
        if (IsOnWindow)
        {
            _pendingAction = GroundAction.Sit;
            _hasPendingAction = true;
            return;
        }

        float idle = _currentAction == GroundAction.Idle ? 0f : idleAction.bias;
        float sit = _currentAction == GroundAction.Sit ? 0f : sitAction.bias;
        float walk = _currentAction == GroundAction.Walk ? 0f : walkAction.bias;
        float total = idle + sit + walk;

        float roll = UnityEngine.Random.Range(0f, total);

        if (roll < idle)
        {
            _pendingAction = GroundAction.Idle;
            _hasPendingAction = true;
            return;
        }

        roll -= idle;

        if (roll < sit)
        {
            _pendingAction = GroundAction.Sit;
            _hasPendingAction = true;
            return;
        }

        _pendingAction = GroundAction.Walk;
        _hasPendingAction = true;
    }

    /// <summary>
    /// 行動の種類に応じて次の行動までの時間をランダムに選択する
    /// </summary>
    /// <param name="action">次の行動の種類</param>
    /// <returns>次の行動までの時間</returns>
    float PickActionDuration(GroundAction action)
    {
        switch (action)
        {
            case GroundAction.Sit:
                return UnityEngine.Random.Range(sitAction.minRange, sitAction.maxRange);
            case GroundAction.Idle:
                return UnityEngine.Random.Range(idleAction.minRange, idleAction.maxRange);
            default:
                return 0f;
        }
    }

    public void SetCharacterScale(float value, bool saveSettings = true)
    {
        float nextScale = Mathf.Clamp(value, Mathf.Min(minScale, maxScale), Mathf.Max(minScale, maxScale));

        if (_modelContainer != null)
        {
            _modelContainer.transform.localScale = Vector3.one * nextScale;
            TryResizeWindow(nextScale * 2f);
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

    /// <summary>
    /// InputSystemの入力イベントを登録する。
    /// </summary>
    void RegisterInputEvents()
    {
        if (_isInputRegistered) return;

        var controller = InputController.Instance;

        if (controller == null) return;

        controller.UI.ScrollWheel.performed += OnScrollWheel;
        controller.UI.RightClick.performed += OnRightClick;
        _isInputRegistered = true;
    }

    /// <summary>
    /// InputSystemの入力イベントを解除する。
    /// </summary>
    void UnregisterInputEvents()
    {
        if (!_isInputRegistered) return;

        var controller = InputController.Instance;

        if (controller != null)
        {
            controller.UI.ScrollWheel.performed -= OnScrollWheel;
            controller.UI.RightClick.performed -= OnRightClick;
        }

        _isInputRegistered = false;
    }

    /// <summary>
    /// InputSystem用のスクロールホイール入力イベントハンドラー
    /// スクロールホイールの入力を処理する。スクロールの値とマウスの位置を保留し、次のUpdateで処理する。
    /// </summary>
    void OnScrollWheel(InputAction.CallbackContext context)
    {
        var mouse = Mouse.current;

        if (mouse == null) return;

        _pendingScroll = mouse.scroll.ReadValue().y;
        _pendingScreenPosition = mouse.position.ReadValue();
        _hasScrollPending = true;
    }

    /// <summary>
    /// InputSystem用の右クリック入力イベントハンドラー
    /// 右クリックの入力を処理する。マウスの位置を保留し、次のUpdateで処理する。
    /// </summary>
    /// <param name="context"></param>
    void OnRightClick(InputAction.CallbackContext context)
    {
        if (!context.performed) return;

        var mouse = Mouse.current;

        if (mouse == null) return;

        _pendingRightClickPosition = mouse.position.ReadValue();
        _hasRightClickPending = true;
    }

    /// <summary>
    /// キャラクターウィンドウのサイズをスケールに合わせて調整する
    /// </summary>
    /// <param name="currentScale">キャラクタースケール</param>
    void TryResizeWindow(float currentScale)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (Application.isEditor) return;

        if (!TryInitResize(currentScale)) return;

        if (!WindowsAPI.TryGetCurrentWindowRect(out var currentRect)) return;

        int targetHeight = Mathf.RoundToInt(_windowResizeHeight * currentScale);
        int currentHeight = currentRect.bottom - currentRect.top;

        // サイズに変更がない場合はリサイズしない
        if (targetHeight == currentHeight) return;

        float aspect = Mathf.Max(0.01f, _windowResizeAspect);
        int targetWidth = Mathf.RoundToInt(targetHeight * aspect);

        int centerX = (currentRect.left + currentRect.right) / 2;
        int centerY = (currentRect.top + currentRect.bottom) / 2;
        int targetX = centerX - (targetWidth / 2);
        int targetY = centerY - (targetHeight / 2);

        bool isWindowResized = WindowsAPI.SetCurrentWindowRect(targetX, targetY, targetWidth, targetHeight);
#endif
    }

    /// <summary>
    /// ウィンドウサイズのリサイズに必要な情報を初期化する
    /// </summary>
    /// <param name="currentScale">キャラクタースケール</param>
    /// <returns></returns>
    bool TryInitResize(float currentScale)
    {
        if (_isWindowResizeReady) return true;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!WindowsAPI.TryGetCurrentWindowRect(out var rect)) return false;

        int width = rect.right - rect.left;
        int height = rect.bottom - rect.top;
        
        if (width <= 0 || height <= 0) return false;

        _windowResizeHeight = height;
        _windowResizeAspect = height > 0 ? (float)width / height : 1f;
        _isWindowResizeReady = true;

        return true;
#else
        return false;
#endif
    }

    /// <summary>
    /// マウスポインターがキャラクターの描画されている範囲内にあるかどうかを判定する
    /// </summary>
    /// <param name="screenPosition">マウスポインターのスクリーン座標</param>
    bool IsPointerOnCharacter(Vector2 screenPosition)
    {
        var ray = _targetCamera.ScreenPointToRay(screenPosition);
        var renderers = ModelContainer.GetComponentsInChildren<Renderer>(true);

        float nearest = float.PositiveInfinity;
        bool isHit = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];

            if (!renderer.bounds.IntersectRay(ray, out var distance) || distance < 0f || distance >= nearest)
                continue;

            nearest = distance;
            isHit = true;
        }

        return isHit;
    }

    /// <summary>
    /// キャラクターが追従するウィンドウをクリアする。タスクバーに着地している場合は、前面固定を維持するためにトップモスト設定は解除しない。
    /// </summary>
    void ClearFollowTarget()
    {
        bool isKeepTopmost = _alwaysTopmostLocked || (IsGrounded && _lastGroundSurfaceType == GroundSurfaceType.Taskbar);

        bool hasFollow = _followWindow != IntPtr.Zero || _fallRuntime.HasFollowingWindow;

        if (!hasFollow)
        {
            //キャラクターが最前面になっていて対象がタスクバーでない場合は、前面固定を解除する
            if (_zOrderRuntime.IsTopmostSupport && !isKeepTopmost)
            {
                WindowsAPI.SetCurrentWindowTopmost(false);
                _zOrderRuntime.IsTopmostSupport = false;
            }

            return;
        }

        _followWindow = IntPtr.Zero;
        _lastFollowWindowRect = default;
        _fallRuntime.HasFollowingWindow = false;
        _windowOffsetX = 0f;
        _wasSupportFocused = false;
        _lastFocusedHwnd = IntPtr.Zero;

        if (_zOrderRuntime.IsTopmostSupport && !isKeepTopmost)
        {
            WindowsAPI.SetCurrentWindowTopmost(false);
            _zOrderRuntime.IsTopmostSupport = false;
        }

        if (_zOrderRuntime.IsTopmostSupport && isKeepTopmost)
        {
            bool ensuredTopmost = WindowsAPI.SetCurrentWindowTopmost(true);
            _zOrderRuntime.IsTopmostSupport = ensuredTopmost;
        }

        _supportFailFrames = 0;
    }

    void SetCharacterFront()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        IntPtr currentHwnd = WindowsAPI.GetCurrentWindowHandle();
        IntPtr foregroundHwnd = WindowsAPI.GetForegroundWindowHandle();

        if (foregroundHwnd != IntPtr.Zero && foregroundHwnd != currentHwnd)
        {
                bool placedAboveForeground = WindowsAPI.PlaceCurrentWindowAbove(foregroundHwnd);
        }

        bool topmostEnabled = WindowsAPI.SetCurrentWindowTopmost(true);
        _zOrderRuntime.IsTopmostDrag = topmostEnabled;
        _zOrderRuntime.IsTopmostSupport = _zOrderRuntime.IsTopmostSupport || topmostEnabled;

        bool broughtFront = WindowsAPI.BringCurrentWindowToFront();
#endif
    }

    void ReleaseCharacterFront()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!_zOrderRuntime.IsTopmostDrag)
        {
            return;
        }

        bool isKeepTopmost = _alwaysTopmostLocked || (IsGrounded && _lastGroundSurfaceType == GroundSurfaceType.Taskbar);
        if (isKeepTopmost)
        {
            _zOrderRuntime.IsTopmostSupport = true;
            _zOrderRuntime.IsTopmostDrag = false;
            
            return;
        }

        bool released = WindowsAPI.SetCurrentWindowTopmost(false);
        _zOrderRuntime.IsTopmostDrag = false;
        _zOrderRuntime.IsTopmostSupport = false;
#endif
    }

    void ApplyZOrder(GroundSurfaceType SurfaceType, IntPtr supportHandle)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        IntPtr currentHwnd = WindowsAPI.GetCurrentWindowHandle();
        IntPtr foregroundHwnd = WindowsAPI.GetForegroundWindowHandle();
        IntPtr targetHwnd = IntPtr.Zero;
        
        if (SurfaceType == GroundSurfaceType.WindowTop)
        {
            targetHwnd = supportHandle;
        }
        else if (SurfaceType == GroundSurfaceType.Taskbar)
        {
             bool canUseForeground = foregroundHwnd != IntPtr.Zero
                                    && foregroundHwnd != currentHwnd
                                    && WindowsAPI.IsWindowVisible(foregroundHwnd)
                                    && !WindowsAPI.IsWindowMinimized(foregroundHwnd);

            targetHwnd = canUseForeground ? foregroundHwnd : WindowsAPI.GetTaskbarHandle();
        }

        if (targetHwnd == IntPtr.Zero)
        {
            bool broughtNoTarget = WindowsAPI.BringCurrentWindowToFront();
            return;
        }

        if (SurfaceType == GroundSurfaceType.Taskbar)
        {
            bool topmostEnabled = WindowsAPI.SetCurrentWindowTopmost(true);
            _zOrderRuntime.IsTopmostSupport = topmostEnabled;
        }

        WindowsAPI.PlaceCurrentWindowAbove(targetHwnd);
        WindowsAPI.BringCurrentWindowToFront();
#endif
    }

    ///<summary>
    /// ウィンドウ上にいるときのパラメータを同期する
    /// </summary>
    /// <param name="onWindow">ウィンドウの上にいるかどうか</param>
    void SyncOnWindowState(bool onWindow)
    {
        SetOnWindow(onWindow);
        SetSittingWindow(onWindow && _animatorSync.GetBool(Constant.AnimatorIsSittingWindow));
    }

    /// <summary>
    /// ウィンドウの上にいるかの状態を設定する
    /// </summary>
    /// <param name="onWindow">ウィンドウの上にいるかどうか</param>
    public void SetOnWindow(bool onWindow)
    {
        bool currentOnWindow = _animatorSync.GetBool(Constant.AnimatorIsOnWindow);

        //ドラッグ中はウィンドウの上にいることにしない
        if (IsDragging)
        {
            if (!onWindow && currentOnWindow)
            {
                _animatorSync.SetIsOnWindow(false);
                _animatorSync.SetIsSittingWindow(false);
                return;
            }

            return;
        }

        bool currentSittingWindow = _animatorSync.GetBool(Constant.AnimatorIsSittingWindow);

        //状態に変更がなく、かつ、ウィンドウの上にいる状態でない場合は離脱
        if (currentOnWindow == onWindow && (onWindow || !currentSittingWindow)) return;

        _animatorSync.SetIsOnWindow(onWindow);
        
        if (!onWindow)
        {
            _animatorSync.SetIsSittingWindow(false);
        }
    }

    /// <summary>
    /// ウィンドウの上に座っているかを設定する
    /// </summary>
    /// <param name="sittingWindow">ウィンドウの上に座っているかどうか</param>
    void SetSittingWindow(bool sittingWindow)
    {
        if (IsDragging) return;

        bool isOnWindow = _animatorSync.GetBool(Constant.AnimatorIsOnWindow);
        bool isApplied = sittingWindow && isOnWindow;
        bool currentSittingWindow = _animatorSync.GetBool(Constant.AnimatorIsSittingWindow);
    
        if (currentSittingWindow == isApplied) return;

        _animatorSync.SetIsSittingWindow(isApplied);
    }

    /// <summary>
    /// 支持ウィンドウが他のウィンドウに隠されているかを判定する。隠されていると判断された場合は、その支持ウィンドウは着地候補から除外する。
    /// </summary>
    /// <param name="hwnd">支持ウィンドウのハンドル</param>
    /// <param name="supportRect">支持ウィンドウの矩形情報</param>
    /// <returns>隠されている場合はtrue、それ以外はfalse</returns>
    bool IsSupportOccluded(IntPtr hwnd, WindowsAPI.RECT supportRect)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        IntPtr currentHwnd = WindowsAPI.GetCurrentWindowHandle();
        IntPtr foregroundHwnd = WindowsAPI.GetForegroundWindowHandle();

        // 前面のウィンドウが存在しない、または支持ウィンドウ自身、もしくはキャラクターモデルのウィンドウである場合は、支持ウィンドウは隠されていないと判断する。
        if (foregroundHwnd == IntPtr.Zero || foregroundHwnd == currentHwnd || foregroundHwnd == hwnd)
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

        bool dominantWindow = WindowsAPI.IsWindowMaximized(foregroundHwnd)
                      || WindowsAPI.IsWindowFullscreen(foregroundHwnd)
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

    /// <summary>
    /// キャラクターウィンドウを指定した量だけ移動する
    /// </summary>
    /// <param name="deltaX">X方向の移動量</param>
    /// <param name="deltaY">Y方向の移動量</param>
    bool MoveWindow(float deltaX, float deltaY)
    {
        if (Application.isEditor)
        {
            bool movedInEditor = MoveTransform(deltaX, deltaY);

            if (movedInEditor)
            {
                UpdateFallingStateFromWindowMove(deltaY);
            }

            return movedInEditor;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        int dx = Mathf.RoundToInt(deltaX);
        int dy = Mathf.RoundToInt(-deltaY);

        if (dx == 0 && dy == 0)
        {
            UpdateFallingStateFromWindowMove(0f);
            return true;
        }

        bool moved = WindowsAPI.MoveCurrentWindowBy(dx, dy);

        if (moved)
        {
            float appliedDeltaY = -dy;
            UpdateFallingStateFromWindowMove(appliedDeltaY);
        }

        return moved;

#else
        return false;
#endif
    }

    /// <summary>
    /// 実際のウィンドウ移動量に応じて落下状態を同期する。
    /// 下方向への移動が発生したときのみ落下状態をtrueにする。
    /// </summary>
    /// <param name="appliedDeltaY">実際に適用されたY方向移動量</param>
    void UpdateFallingStateFromWindowMove(float appliedDeltaY)
    {
        if (_animatorSync == null || IsDragging)
        {
            return;
        }

        bool isMovingDown = appliedDeltaY < -0.001f;
        SetFallingState(isMovingDown);
    }

    /// <summary>
    /// キャラクターのTransformを指定した量だけ移動する
    /// </summary>
    /// <param name="deltaX">X方向の移動量</param>
    /// <param name="deltaY">Y方向の移動量</param>
    bool MoveTransform(float deltaX, float deltaY)
    {

        if (!ScreenSpaceTransformUtility.TryGetScreenPosition(_targetCamera, ModelContainer.transform, out var screen)) return false;

        var moved = new Vector3(screen.x + deltaX, screen.y + deltaY, screen.z);
        return ScreenSpaceTransformUtility.TrySetScreenPosition(_targetCamera, ModelContainer.transform, moved, screen.z);
    }

    /// <summary>
    /// WebSocket送信先の初期化ログを出す。
    /// </summary>
    private void InitializeCharacterStatePublisher()
    {
        UnityEngine.Debug.Log($"{LogPrefix} Character state WebSocket publisher ready. endpoint={CharacterStateWebSocketUrl}");
    }

    /// <summary>
    /// Publish character spawn completed message
    /// </summary>
    private void PublishCharacterSpawnCompleted()
    {
        try
        {
            var message = new CharacterStateMessage
            {
                schemaVersion = "1.0",
                messageType = "CharacterSpawnCompleted",
                characterId = _characterId,
                timestampUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                status = "spawned"
            };

            string messageJson = UnityEngine.JsonUtility.ToJson(message);
            using var webSocket = new WebSocket(CharacterStateWebSocketUrl);
            webSocket.Connect();

            if (!webSocket.IsAlive)
            {
                UnityEngine.Debug.LogWarning($"{LogPrefix} Failed to publish CharacterSpawnCompleted. WebSocket is not connected. endpoint={CharacterStateWebSocketUrl}");
                return;
            }

            webSocket.Send(messageJson);
            webSocket.Close();
            UnityEngine.Debug.Log($"{LogPrefix} Published CharacterSpawnCompleted: {messageJson}");
        } catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"{LogPrefix} Failed to publish message: {ex}");
        }
    }

    private IEnumerator PublishCharacterSpawnCompletedRepeatedly()
    {
        int retryCount = Mathf.Max(1, CHARACTER_SPAWN_NOTIFICATION_RETRY_COUNT);
        float retryInterval = Mathf.Max(0.01f, CHARACTER_SPAWN_NOTIFICATION_RETRY_INTERVAL_SECONDS);

        for (int i = 0; i < retryCount; i++)
        {
            PublishCharacterSpawnCompleted();

            if (i < retryCount - 1)
            {
                yield return new WaitForSecondsRealtime(retryInterval);
            }
        }
    }

    /// <summary>
    /// Cleanup WebSocket resources
    /// </summary>
    private void CleanupNetMQ()
    {
        UnityEngine.Debug.Log($"{LogPrefix} Character state WebSocket publisher cleanup completed.");
    }
}
