using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using Unity.Logging;
using UnityEngine;
using UnityEngine.InputSystem;
using Whisper;
using Whisper.Utils;

public class STTManager : MonoBehaviour
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private const int VK_SPACE = 0x20;

    [DllImport("user32.dll")]
    /// <summary>
    /// GetAsyncKeyStateの処理を実行します。
    /// </summary>
    private static extern short GetAsyncKeyState(int vKey);
#endif

    public enum RecognitionInputMode
    {
        AlwaysListening = 0,
        PushToTalk = 1,
    }

    [Header("Dependencies")]
    [SerializeField] private WhisperManager whisperManager;
    [SerializeField] private MicrophoneRecord microphoneRecord;
    [SerializeField] private SwitchMicrophoneIcon micIcon;
    [SerializeField] private GameObject micIconObject;
    [SerializeField] private TMP_Text recognizedText;
    [SerializeField] private GameObject recognizedVisibilityObject;

    [Header("Run")]
    [SerializeField] private RecognitionInputMode recognitionInputMode = RecognitionInputMode.AlwaysListening;
    [SerializeField] private InputActionProperty pushToTalkInput;
    [SerializeField] private string fallbackPushToTalkBinding = "<Keyboard>/space";
    [SerializeField] private float pttReleaseGraceSec = 1.0f;
    [SerializeField] private float pttFinalAcceptGraceSec = 3.0f;
    [SerializeField] private int pttFlushDelayMs = 120;
    [SerializeField] private float pttFlushCooldownSec = 0.35f;
    [SerializeField] private float pttFlushRestartSec = 1.5f;
    [SerializeField] private float pttFinalizeLockSec = 6.0f;
    [SerializeField] private float micToggleMinIntervalSec = 0.08f;
    [SerializeField] private int pttGlobalKey = VK_SPACE;

    [Header("Display")]
    [SerializeField] private float resultHideDelaySec = 4f;
    [SerializeField] private bool showIntermediateResult;
    [SerializeField] private float uiFrontCooldownSec = 0.2f;

    [Header("Diagnostics")]
    [SerializeField] private float noResultWarnSec = 20f;
    [SerializeField] private float noResultWarnRepeatSec = 10f;
    [SerializeField] private float modelLoadTimeoutSec = 60f;
    [SerializeField] private float streamTimeoutSec = 12f;
    [SerializeField] private bool isLoggedStream;
    [SerializeField] private float dropLogIntervalSec = 15f;

    [Header("Stability")]
    [SerializeField] private float maxStreamSec = 30f;
    [SerializeField] private int maxSegmentsPerStream = 8;
    [SerializeField] private int maxQueuedResults = 64;
    [SerializeField] private bool isDisabledAutoRotation = true;
    [SerializeField] private bool isDisablePromptUpdate = true;
    [SerializeField] private float minStepSec = 4f;
    [SerializeField] private float minLengthSec = 12f;

    [Header("Noise Filter")]
    [SerializeField] private int minFinalTextLength = 2;
    [SerializeField] private int minIntermediateTextLength = 4;
    [SerializeField] private float minResultIntervalSec = 0.35f;
    [SerializeField] private float duplicateSuppressSec = 2.0f;
    [SerializeField] private float extendedDuplicateSuppressSec = 8.0f;
    [SerializeField] private int repeatedFinalMinRepeats = 2;
    [SerializeField] private float finalConcatWindowSec = 1.0f;
    [SerializeField] private string[] ignoredExactTexts = { ".", "..", "...", "えー", "あー", "んー" };
    [SerializeField] private bool restartStreamOnIgnored = false;

    [Header("Input Sensitivity")]
    [SerializeField] private bool isDisabledVad = true;
    [SerializeField] private float vadThreshold = 1.0f;
    [SerializeField] private float vadFrequencyThreshold = 100.0f;
    [SerializeField] private float vadLastSeconds = 1.25f;
    [SerializeField] private float vadContextSeconds = 30.0f;
    [SerializeField] private float vadUpdateRateSeconds = 0.1f;

    [Header("Character Process Bridge (Test)")]
    [SerializeField] private string mainProcessUdpHost = Constant.BackendHost;
    [SerializeField] private int mainProcessUdpPort = Constant.BackendPort;
    [SerializeField] private bool logForwarding;

    private readonly struct RecognizedQueueItem
    {
        public readonly string Text;
        public readonly bool IsIntermediate;

        /// <summary>
        /// 認識テキストと中間結果フラグを保持するキュー要素を生成します。
        /// </summary>
        public RecognizedQueueItem(string text, bool isIntermediate)
        {
            Text = text;
            IsIntermediate = isIntermediate;
        }
    }

    private WhisperStream _stream;
    private bool _running;
    private bool _isStarting;
    private bool _isStopping;
    private bool _isPttHeld;
    private bool _isPttFinalizing;
    private bool _isPendingRestart;
    private bool _isSwitchingStream;
    private int _recognizedCount;
    private int _segmentCount;
    private int _queuedResultCount;
    private float _elapsedSinceStart;
    private float _noResultWarnElapsed;
    private float _lastRecognizedAt = -1f;
    private float _lastAcceptedAt = -1f;
    private float _lastPttReleased = -1f;
    private float _pttFinalizeStarted = -1f;
    private float _lastPttFlushAt = -1f;
    private float _restartRequestedAt = -1f;
    private float _streamStartedAt = -1f;
    private float _lastMicToggleAt = -1f;
    private string _lastAcceptedResult = string.Empty;
    private string _lastIntermediateResult = string.Empty;
    private string _lastFinalResult = string.Empty;
    private float _lastFinalAt = -1f;
    private bool _isStreamStarted;
    private string _lastDropLogText = string.Empty;
    private string _lastDropLogReason = string.Empty;
    private bool _lastDropLogIntermediate;
    private float _lastDropLogAt = -1f;
    private int _suppressedDropCount;

    private Coroutine _recognizedTextHideCoroutine;
    private InputAction _pushToTalkAction;
    private bool _ownsPushToTalkAction;
    private bool _isPttShortcutOverrideActive;
    private bool _isUiProcess;
    private bool _lastShouldRecordState;
    private bool _hasLastShouldTracked;
    private bool _lastMicIconVisible;
    private bool _lastMicCanRecognize;
    private bool _hasMicUiTracked;
    private bool _isPttFlushInFlight;
    private int _pttFlushRequestVersion;
    private float _lastUiFrontAt = -1f;
    private readonly ConcurrentQueue<RecognizedQueueItem> _recognizedQueue = new();

    private const string PttShortcutActionMap = "Shortcut";
    private const string PttShortcutActionName = "PushToTalk";

    /// <summary>
    /// 実行環境判定、設定反映、PushToTalk入力初期化を行います。
    /// </summary>
    private void Awake()
    {
        _isUiProcess = IsUiProcess();
        ApplySettings();
        SetupPushToTalkAction();
    }

    /// <summary>
    /// PushToTalk入力アクションを有効化します。
    /// </summary>
    private void OnEnable()
    {
        EnablePushToTalkAction();
    }

    /// <summary>
    /// PushToTalk入力アクションを無効化します。
    /// </summary>
    private void OnDisable()
    {
        DisposePushToTalkAction();
        _isPttHeld = false;
    }

    /// <summary>
    /// STT開始処理を起動時に呼び出します。
    /// </summary>
    private void Start()
    {
        StartSTT();
    }

    /// <summary>
    /// 入力状態更新、認識結果処理、マイクUI更新、監視系タイマー処理を実行します。
    /// </summary>
    private void Update()
    {
        try
        {
            UpdatePttState();
            UpdateRecordingState();
            DrainRecognizedQueue();
            UpdateMicUi();
        }
        catch (Exception ex)
        {
            LogException("UpdateLoop", ex);
        }

        if (!_running)
        {
            return;
        }

        RecoverFlushRestart();

        if (ShouldRotateStream())
        {
            RestartStreamAsync($"rotation(time={maxStreamSec:0.0}s,segments={_segmentCount})").Forget();
        }

        bool isInitialized = whisperManager != null && whisperManager.IsLoaded;
        bool isRecording = microphoneRecord != null && microphoneRecord.IsRecording;
        if (!isInitialized || !isRecording)
        {
            _elapsedSinceStart = 0f;
            _noResultWarnElapsed = 0f;
            return;
        }

        _elapsedSinceStart += Time.deltaTime;
        _noResultWarnElapsed += Time.deltaTime;

        bool reachedNoResultThreshold = noResultWarnSec > 0f && _elapsedSinceStart >= noResultWarnSec;
        bool reachedWarningRepeat = noResultWarnRepeatSec <= 0f || _noResultWarnElapsed >= noResultWarnRepeatSec;
        if (reachedNoResultThreshold && reachedWarningRepeat)
        {
            _noResultWarnElapsed = 0f;
            float sinceLast = _lastRecognizedAt < 0f ? -1f : Time.unscaledTime - _lastRecognizedAt;
            Log.Warning($"[STT] No recognition result yet. elapsed={_elapsedSinceStart:0.0}s, initialized={isInitialized}, recording={isRecording}, mode={recognitionInputMode}, pttHeld={_isPttHeld}, recognizedCount={_recognizedCount}, sinceLast={(sinceLast < 0f ? "N/A" : sinceLast.ToString("0.0") + "s")}");
        }
    }

    /// <summary>
    /// アプリ終了時にSTTを停止します。
    /// </summary>
    private void OnApplicationQuit()
    {
        StopSTT();
    }

    /// <summary>
    /// 破棄時に入力アクション解放とSTT停止を行います。
    /// </summary>
    private void OnDestroy()
    {
        DisablePushToTalkAction();
        DisposePushToTalkAction();
        StopSTT();
    }

    [ContextMenu("Start STT")]
    /// <summary>
    /// STT開始要求を受け付け、重複起動を防ぎつつ非同期開始処理を実行します。
    /// </summary>
    public void StartSTT()
    {
        if (_isStarting)
        {
            Log.Warning("[STT] Start request ignored: start is already in progress.");
            return;
        }

        if (_isStopping)
        {
            Log.Warning("[STT] Start request ignored: stop is in progress.");
            return;
        }

        StartSTTAsync().Forget();
    }

    [ContextMenu("Stop STT")]
    /// <summary>
    /// STTの実行状態を初期化し、ストリーム・録音・UIを停止します。
    /// </summary>
    public void StopSTT()
    {
        if (_isStopping)
        {
            return;
        }

        _isStopping = true;
        _isStarting = false;

        _running = false;
        _isPttHeld = false;
        _isPendingRestart = false;
        _isSwitchingStream = false;
        _recognizedCount = 0;
        _segmentCount = 0;
        _queuedResultCount = 0;
        _elapsedSinceStart = 0f;
        _noResultWarnElapsed = 0f;
        _lastRecognizedAt = -1f;
        _lastAcceptedAt = -1f;
        _lastPttReleased = -1f;
        _pttFinalizeStarted = -1f;
        _lastPttFlushAt = -1f;
        _restartRequestedAt = -1f;
        _streamStartedAt = -1f;
        _lastMicToggleAt = -1f;
        _lastAcceptedResult = string.Empty;
        _lastIntermediateResult = string.Empty;
        _lastFinalResult = string.Empty;
        _lastFinalAt = -1f;
        _isStreamStarted = false;
        _isPttFlushInFlight = false;
        _pttFlushRequestVersion = 0;
        ResetDropLog();

        if (_stream != null)
        {
            _stream.OnResultUpdated -= OnStreamResultUpdated;
            _stream.OnSegmentFinished -= OnSegmentFinished;
            _stream.OnStreamFinished -= OnStreamFinished;
            TryStopStream(_stream, "stop-test");
            _stream = null;
        }

        if (microphoneRecord != null && microphoneRecord.IsRecording)
        {
            microphoneRecord.StopRecord();
        }

        while (_recognizedQueue.TryDequeue(out _)) { }

        UpdateMicUi();
        ClearRecognized();

        _isStopping = false;

        Log.Info("[STT] Whisper STT stopped.");
    }

    /// <summary>
    /// 依存解決、モデル準備、ストリーム作成、録音開始までの起動シーケンスを実行します。
    /// </summary>
    private async UniTaskVoid StartSTTAsync()
    {
        _isStarting = true;

        try
        {
            if (!TryResolveDependencies())
            {
                return;
            }

            ApplyMicSensitivity();
            ApplyStreamingStability();
            LogMicDevice();

            bool modelReady = await EnsureModelReady();
            if (!modelReady)
            {
                Log.Error("[STT] Whisper model is not loaded.");
                return;
            }

            if (_isStopping)
            {
                return;
            }

            if (_stream != null)
            {
                _stream.OnResultUpdated -= OnStreamResultUpdated;
                _stream.OnSegmentFinished -= OnSegmentFinished;
                _stream.OnStreamFinished -= OnStreamFinished;
                TryStopStream(_stream, "restart-start-test");
                _stream = null;
            }

            var createdStream = await CreateStreamAsync("start");
            if (createdStream == null)
            {
                Log.Error("[STT] Failed to create whisper stream.");
                return;
            }

            AttachStream(createdStream);

            _running = true;
            _isPendingRestart = false;
            _recognizedCount = 0;
            _segmentCount = 0;
            _queuedResultCount = 0;
            _elapsedSinceStart = 0f;
            _noResultWarnElapsed = 0f;
            _lastRecognizedAt = -1f;
            _lastAcceptedAt = -1f;
            _lastPttReleased = -1f;
            _lastPttFlushAt = -1f;
            _restartRequestedAt = -1f;
            _lastMicToggleAt = -1f;
            _lastAcceptedResult = string.Empty;
            _lastIntermediateResult = string.Empty;
            _lastFinalResult = string.Empty;
            _lastFinalAt = -1f;
            _isPttFlushInFlight = false;
            _pttFlushRequestVersion = 0;
            ResetDropLog();
            ClearRecognized();

            if (recognitionInputMode == RecognitionInputMode.AlwaysListening)
            {
                EnsureMicRecording(true);
            }
            else
            {
                EnsureMicRecording(false);
            }

            Log.Info($"[STT] Whisper STT test started. mode={recognitionInputMode}, micRecording={microphoneRecord.IsRecording}");
        }
        finally
        {
            _isStarting = false;
        }
    }

    /// <summary>
    /// WhisperManagerとMicrophoneRecord参照を解決し、利用可否を検証します。
    /// </summary>
    private bool TryResolveDependencies()
    {
        if (whisperManager == null)
        {
            whisperManager = GetComponent<WhisperManager>() ?? FindFirstObjectByType<WhisperManager>();
        }

        if (microphoneRecord == null)
        {
            microphoneRecord = GetComponent<MicrophoneRecord>() ?? FindFirstObjectByType<MicrophoneRecord>();
        }

        if (whisperManager == null)
        {
            Log.Error("[STT] WhisperManager is missing.");
            return false;
        }

        if (microphoneRecord == null)
        {
            Log.Error("[STT] MicrophoneRecord is missing.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// PushToTalk入力アクションを生成または取得し、イベント購読を設定します。
    /// </summary>
    private void SetupPushToTalkAction()
    {
        DisposePushToTalkAction();

        bool hasShortcutOverride = HasPushToTalkShortcutOverride();
        _isPttShortcutOverrideActive = hasShortcutOverride;

        // ショートカット設定がある場合は、PTT入力をショートカット専用に固定する。
        if (hasShortcutOverride)
        {
            _pushToTalkAction = new InputAction("WhisperPushToTalk", InputActionType.Button);
            ShortcutBindingService.ApplyToAction(_pushToTalkAction, PttShortcutActionMap, PttShortcutActionName, Key.Space);
            _ownsPushToTalkAction = true;

            _pushToTalkAction.started += OnPushToTalkStarted;
            _pushToTalkAction.canceled += OnPushToTalkCanceled;
            return;
        }

        _pushToTalkAction = pushToTalkInput.action;
        _ownsPushToTalkAction = false;

        if (_pushToTalkAction == null)
        {
            _pushToTalkAction = InputController.Instance?.Asset?.FindAction($"{PttShortcutActionMap}/{PttShortcutActionName}", false);
            _ownsPushToTalkAction = false;
        }

        if (_pushToTalkAction == null)
        {
            string binding = string.IsNullOrWhiteSpace(fallbackPushToTalkBinding) ? "<Keyboard>/space" : fallbackPushToTalkBinding;
            _pushToTalkAction = new InputAction("WhisperPushToTalk", InputActionType.Button, binding);
            _ownsPushToTalkAction = true;
        }

        _pushToTalkAction.started += OnPushToTalkStarted;
        _pushToTalkAction.canceled += OnPushToTalkCanceled;
    }

    /// <summary>
    /// 保存済みショートカット設定を再読み込みして、PTT入力へ再バインドします。
    /// </summary>
    public void RefreshPushToTalkBinding()
    {
        bool shouldEnable = _pushToTalkAction != null && _pushToTalkAction.enabled;
        SetupPushToTalkAction();
        if (shouldEnable)
        {
            EnablePushToTalkAction();
        }
    }

    private static bool HasPushToTalkShortcutOverride()
    {
        var shortcuts = ShortcutBindingService.GetShortcuts();
        if (shortcuts == null)
        {
            return false;
        }

        for (int i = 0; i < shortcuts.Count; i++)
        {
            var entry = shortcuts[i];
            if (entry == null)
            {
                continue;
            }

            if (!string.Equals(entry.ActionMap, PttShortcutActionMap, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(entry.ActionName, PttShortcutActionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return !string.IsNullOrWhiteSpace(entry.PrimaryKey);
        }

        return false;
    }

    /// <summary>
    /// PushToTalk入力アクションを有効化します。
    /// </summary>
    private void EnablePushToTalkAction()
    {
        if (_pushToTalkAction == null)
        {
            SetupPushToTalkAction();
        }

        if (_pushToTalkAction != null && !_pushToTalkAction.enabled)
        {
            _pushToTalkAction.Enable();
        }
    }

    /// <summary>
    /// PushToTalk入力アクションを無効化し、押下状態を解除します。
    /// </summary>
    private void DisablePushToTalkAction()
    {
        if (_ownsPushToTalkAction && _pushToTalkAction != null && _pushToTalkAction.enabled)
        {
            _pushToTalkAction.Disable();
        }

        _isPttHeld = false;
    }

    /// <summary>
    /// PushToTalk入力アクションの購読解除と破棄を行います。
    /// </summary>
    private void DisposePushToTalkAction()
    {
        if (_pushToTalkAction == null)
        {
            return;
        }

        _pushToTalkAction.started -= OnPushToTalkStarted;
        _pushToTalkAction.canceled -= OnPushToTalkCanceled;
        if (_ownsPushToTalkAction)
        {
            _pushToTalkAction.Dispose();
        }

        _pushToTalkAction = null;
        _ownsPushToTalkAction = false;
    }

    /// <summary>
    /// PushToTalk押下開始時に入力ロックを確認して押下状態を有効化します。
    /// </summary>
    private void OnPushToTalkStarted(InputAction.CallbackContext _)
    {
        if (recognitionInputMode != RecognitionInputMode.PushToTalk)
        {
            return;
        }

        if (ShouldBlockSttInput())
        {
            return;
        }

        if (IsPttLocked())
        {
            return;
        }

        _isPttHeld = true;
        _lastPttReleased = -1f;
    }

    /// <summary>
    /// PushToTalk押下終了時に確定フェーズへ移行し、フラッシュ再起動を開始します。
    /// </summary>
    private void OnPushToTalkCanceled(InputAction.CallbackContext _)
    {
        if (recognitionInputMode != RecognitionInputMode.PushToTalk)
        {
            return;
        }

        // Ignore spurious cancel callbacks that did not follow an active hold.
        if (!_isPttHeld)
        {
            return;
        }

        BeginPttFinalize();
        _isPttHeld = false;
        _lastPttReleased = Time.unscaledTime;

        RequestPttFlush();
    }

    /// <summary>
    /// 非フォーカス時のグローバルキー状態を監視し、PushToTalk状態を更新します。
    /// </summary>
    private void UpdatePttState()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!_isUiProcess
            || recognitionInputMode != RecognitionInputMode.PushToTalk
            || Application.isFocused
            || _isPttShortcutOverrideActive)
        {
            return;
        }

        int virtualKey = Mathf.Clamp(pttGlobalKey, 1, 255);
        bool isPressed = (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

        if (isPressed)
        {
            if (IsPttLocked())
            {
                return;
            }

            if (!_isPttHeld)
            {
                _isPttHeld = true;
                _lastPttReleased = -1f;
            }

            return;
        }

        if (_isPttHeld)
        {
            BeginPttFinalize();
            _isPttHeld = false;
            _lastPttReleased = Time.unscaledTime;
            RequestPttFlush();
        }
#endif
    }

    /// <summary>
    /// PushToTalkフラッシュ要求を世代管理し、多重起動を防止します。
    /// </summary>
    private void RequestPttFlush()
    {
        _pttFlushRequestVersion++;
        int requestVersion = _pttFlushRequestVersion;

        if (_isPttFlushInFlight)
        {
            return;
        }

        FlushStreamPttAsync(requestVersion).Forget();
    }

    /// <summary>
    /// PushToTalk解放後にストリーム停止を要求して再作成準備を行います。
    /// </summary>
    private async UniTaskVoid FlushStreamPttAsync(int requestVersion)
    {
        _isPttFlushInFlight = true;
        try
        {
            if (!_running || recognitionInputMode != RecognitionInputMode.PushToTalk)
            {
                EndPttFinalize();
                return;
            }

            if (requestVersion != _pttFlushRequestVersion)
            {
                EndPttFinalize();
                return;
            }

            if (_stream == null || _isSwitchingStream || _isPendingRestart)
            {
                EndPttFinalize();
                return;
            }

            if (_lastPttFlushAt > 0f && Time.unscaledTime - _lastPttFlushAt < pttFlushCooldownSec)
            {
                EndPttFinalize();
                return;
            }

            int delayMs = Mathf.Max(0, pttFlushDelayMs);
            if (delayMs > 0)
            {
                await UniTask.Delay(delayMs);
            }

            if (!_running || recognitionInputMode != RecognitionInputMode.PushToTalk || _isPttHeld)
            {
                EndPttFinalize();
                return;
            }

            if (requestVersion != _pttFlushRequestVersion)
            {
                EndPttFinalize();
                return;
            }

            if (_stream == null || _isSwitchingStream || _isPendingRestart)
            {
                EndPttFinalize();
                return;
            }

            _lastPttFlushAt = Time.unscaledTime;
            _isPendingRestart = true;
            _restartRequestedAt = Time.unscaledTime;
            bool requestedStop = TryStopStream(_stream, "ptt-flush");
            if (!requestedStop)
            {
                _isPendingRestart = false;
                _restartRequestedAt = -1f;
                StartStreamAfterFlushAsync().Forget();
            }
        }
        finally
        {
            _isPttFlushInFlight = false;
        }
    }

    /// <summary>
    /// フラッシュ後に音声認識ストリームを再作成して再開します。
    /// </summary>
    private async UniTaskVoid StartStreamAfterFlushAsync()
    {
        if (!_running || _isSwitchingStream || _isStopping || _isStarting)
        {
            return;
        }

        _isSwitchingStream = true;
        try
        {
            if (_stream != null)
            {
                _stream.OnResultUpdated -= OnStreamResultUpdated;
                _stream.OnSegmentFinished -= OnSegmentFinished;
                _stream.OnStreamFinished -= OnStreamFinished;
                _stream = null;
            }

            var recreatedStream = await CreateStreamAsync("ptt-flush");
            if (recreatedStream == null)
            {
                Log.Error("[STT] Failed to recreate whisper stream after flush.");
                return;
            }

            AttachStream(recreatedStream);
            Log.Info("[STT] Whisper stream restarted after push-to-talk flush.");
        }
        catch (Exception ex)
        {
            Log.Error($"[STT] Failed to restart stream after push-to-talk flush: {ex.Message}");
        }
        finally
        {
            _isSwitchingStream = false;
            EndPttFinalize();
        }
    }

    /// <summary>
    /// 認識モードと入力状態に応じて録音継続可否を更新します。
    /// </summary>
    private void UpdateRecordingState()
    {
        if (!_running || whisperManager == null || microphoneRecord == null)
        {
            _isPttHeld = false;
            EndPttFinalize();
            return;
        }

        RecoverPttFinalizeTimeout();

        bool shouldBlockInput = ShouldBlockSttInput();

        if (recognitionInputMode != RecognitionInputMode.PushToTalk)
        {
            _isPttHeld = false;
        }

        if (shouldBlockInput)
        {
            _isPttHeld = false;
        }

        bool withinReleaseGrace = recognitionInputMode == RecognitionInputMode.PushToTalk
                                  && !_isPttHeld
                                  && _lastPttReleased > 0f
                                  && Time.unscaledTime - _lastPttReleased <= Mathf.Max(0f, pttReleaseGraceSec);

        bool shouldRecord = recognitionInputMode == RecognitionInputMode.AlwaysListening
                            || _isPttHeld
                            || withinReleaseGrace;
        if (shouldBlockInput)
        {
            shouldRecord = false;
        }

        EnsureMicRecording(shouldRecord);
    }

    /// <summary>
    /// 目標状態に合わせてマイク録音を開始または停止します。
    /// </summary>
    private void EnsureMicRecording(bool shouldRecord)
    {
        if (microphoneRecord == null)
        {
            return;
        }

        if (isLoggedStream)
        {
            if (!_hasLastShouldTracked || _lastShouldRecordState != shouldRecord)
            {
                _lastShouldRecordState = shouldRecord;
                _hasLastShouldTracked = true;
                Log.Info($"[STT][trace] mic-state-transition shouldRecord={shouldRecord}, isRecording={microphoneRecord.IsRecording}, mode={recognitionInputMode}, pttHeld={_isPttHeld}");
            }
        }

        bool isRecording = microphoneRecord.IsRecording;
        if (shouldRecord == isRecording)
        {
            return;
        }

        float minToggleInterval = Mathf.Max(0f, micToggleMinIntervalSec);
        float now = Time.unscaledTime;
        if (_lastMicToggleAt > 0f && now - _lastMicToggleAt < minToggleInterval)
        {
            return;
        }

        try
        {
            if (shouldRecord)
            {
                microphoneRecord.StartRecord();
            }
            else
            {
                microphoneRecord.StopRecord();
            }

            _lastMicToggleAt = now;
        }
        catch (Exception ex)
        {
            _lastMicToggleAt = now;
            LogException($"EnsureMicRecording(shouldRecord={shouldRecord})", ex);
        }
    }

    /// <summary>
    /// 中間認識結果を受け取り、キュー投入処理へ渡します。
    /// </summary>
    private void OnStreamResultUpdated(string updatedResult)
    {
        try
        {
            if (!showIntermediateResult || string.IsNullOrWhiteSpace(updatedResult))
            {
                return;
            }

            TryEnqueue(updatedResult, true);
        }
        catch (Exception ex)
        {
            LogException("OnStreamResultUpdated", ex);
        }
    }

    /// <summary>
    /// セグメント確定結果を受け取り、確定テキストとしてキュー投入します。
    /// </summary>
    private void OnSegmentFinished(WhisperResult segment)
    {
        try
        {
            if (segment == null || string.IsNullOrWhiteSpace(segment.Result))
            {
                return;
            }

            _segmentCount++;
            TryEnqueue(segment.Result, false);
        }
        catch (Exception ex)
        {
            LogException("OnSegmentFinished", ex);
        }
    }

    /// <summary>
    /// ストリーム終了時に最終結果を処理し、必要なら再起動を実行します。
    /// </summary>
    private void OnStreamFinished(string finalResult)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(finalResult))
            {
                TryEnqueue(finalResult, false);
            }

            _isStreamStarted = false;

            if (_isPendingRestart)
            {
                _isPendingRestart = false;
                _restartRequestedAt = -1f;
                StartStreamAfterFlushAsync().Forget();
            }
        }
        catch (Exception ex)
        {
            LogException("OnStreamFinished", ex);
        }
    }

    /// <summary>
    /// 保留中のフラッシュ再起動が停滞した場合にタイムアウト復旧します。
    /// </summary>
    private void RecoverFlushRestart()
    {
        if (!_isPendingRestart || _isSwitchingStream || _isStarting || _isStopping)
        {
            return;
        }

        if (_restartRequestedAt < 0f)
        {
            _restartRequestedAt = Time.unscaledTime;
            return;
        }

        float timeout = Mathf.Max(0.2f, pttFlushRestartSec);
        float elapsed = Time.unscaledTime - _restartRequestedAt;
        if (elapsed < timeout)
        {
            return;
        }

        _isPendingRestart = false;
        _restartRequestedAt = -1f;
        Log.Warning($"[STT] Pending flush restart timed out ({elapsed:0.000}s). Restarting stream forcefully.");
        StartStreamAfterFlushAsync().Forget();
    }

    /// <summary>
    /// 認識結果キューを順に取り出し、メインスレッドで処理します。
    /// </summary>
    private void DrainRecognizedQueue()
    {
        while (_recognizedQueue.TryDequeue(out var item))
        {
            _queuedResultCount = Mathf.Max(0, _queuedResultCount - 1);
            try
            {
                OnRecognizedText(item.Text, item.IsIntermediate);
            }
            catch (Exception ex)
            {
                LogException($"DrainRecognizedQueue(intermediate={item.IsIntermediate})", ex);
            }
        }
    }

    /// <summary>
    /// 認識テキストの正規化・重複抑制・受理判定を行ってキューへ追加します。
    /// </summary>
    private void TryEnqueue(string text, bool isIntermediate)
    {
        try
        {
            if (ShouldBlockSttInput())
            {
                LogDropped(text, isIntermediate, "llm-response-in-progress");
                return;
            }

            string normalized = text?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (!isIntermediate && !TryMergeWithLastFinal(ref normalized))
            {
                return;
            }

            if (!isIntermediate)
            {
                normalized = CollapseConsecutiveDuplicate(normalized);
            }

            if (!isIntermediate
                && TryCollapseRepeated(normalized, out string collapsedText, out int repeats)
                && repeats >= Mathf.Max(2, repeatedFinalMinRepeats))
            {
                normalized = collapsedText;
            }

            if (isIntermediate && string.Equals(_lastIntermediateResult, normalized, StringComparison.Ordinal))
            {
                LogDropped(normalized, isIntermediate, "duplicate-intermediate");
                return;
            }

            if (!ShouldAccept(normalized, isIntermediate, out string rejectReason))
            {
                LogDropped(normalized, isIntermediate, rejectReason);
                if (!isIntermediate && IsForbiddenReason(rejectReason))
                {
                    OnForbiddenFinal(normalized, rejectReason);
                }
                return;
            }

            int maxQueue = Mathf.Max(1, maxQueuedResults);
            if (_queuedResultCount >= maxQueue)
            {
                LogDropped(normalized, isIntermediate, $"queue-overflow({maxQueue})");
                return;
            }

            _recognizedQueue.Enqueue(new RecognizedQueueItem(normalized, isIntermediate));
            _queuedResultCount++;
            _lastAcceptedResult = normalized;
            _lastAcceptedAt = Time.unscaledTime;
            if (isIntermediate)
            {
                _lastIntermediateResult = normalized;
            }
            else
            {
                _lastFinalResult = normalized;
                _lastFinalAt = Time.unscaledTime;
            }
        }
        catch (Exception ex)
        {
            LogException($"TryEnqueue(intermediate={isIntermediate})", ex);
        }
    }

    /// <summary>
    /// 直近の確定文との結合可否を判定し、必要なら連結テキストへ更新します。
    /// </summary>
    private bool TryMergeWithLastFinal(ref string normalized)
    {
        if (string.IsNullOrWhiteSpace(_lastFinalResult))
        {
            return true;
        }

        if (_lastFinalAt > 0f && finalConcatWindowSec > 0f)
        {
            float elapsedFromLastFinal = Time.unscaledTime - _lastFinalAt;
            if (elapsedFromLastFinal > finalConcatWindowSec)
            {
                return true;
            }
        }

        bool previousIsForbidden = IsIgnored(_lastFinalResult);
        bool incomingIsForbidden = IsIgnored(normalized);
        bool incomingContainsForbidden = ContainsIgnored(normalized);
        if (incomingIsForbidden || incomingContainsForbidden)
        {
            LogDropped(normalized, false, "ignored-exact-text");
            return false;
        }

        if (previousIsForbidden)
        {
            return true;
        }

        string merged = ConcatenateFinal(_lastFinalResult, normalized);
        if (ContainsIgnored(merged))
        {
            LogDropped(merged, false, "ignored-exact-text-contained");
            return false;
        }

        normalized = merged;
        return true;
    }

    /// <summary>
    /// 同一パターンの繰り返し文字列を検出して圧縮します。
    /// </summary>
    private bool TryCollapseRepeated(string text, out string collapsedText, out int repeats)
    {
        collapsedText = text;
        repeats = 1;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        int length = text.Length;
        if (length < 2)
        {
            return false;
        }

        for (int repeat = length / 2; repeat >= 2; repeat--)
        {
            if (length % repeat != 0)
            {
                continue;
            }

            int unitLength = length / repeat;
            string unit = text.Substring(0, unitLength);
            bool allSame = true;
            for (int i = 1; i < repeat; i++)
            {
                int index = i * unitLength;
                if (!string.Equals(text.Substring(index, unitLength), unit, StringComparison.Ordinal))
                {
                    allSame = false;
                    break;
                }
            }

            if (allSame)
            {
                collapsedText = unit.Trim();
                repeats = repeat;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 連続する重複文を終端記号単位で除去します。
    /// </summary>
    private string CollapseConsecutiveDuplicate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var sentenceBuilder = new StringBuilder();
        var outputBuilder = new StringBuilder();
        string lastNormalized = string.Empty;
        bool hasAnySentence = false;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            sentenceBuilder.Append(ch);

            if (!IsSentenceTerminator(ch))
            {
                continue;
            }

            string sentence = sentenceBuilder.ToString();
            sentenceBuilder.Clear();

            string normalized = NormalizeSentence(sentence);
            if (string.IsNullOrEmpty(normalized))
            {
                continue;
            }

            if (!string.Equals(lastNormalized, normalized, StringComparison.Ordinal))
            {
                outputBuilder.Append(sentence.Trim());
                lastNormalized = normalized;
                hasAnySentence = true;
            }
        }

        if (sentenceBuilder.Length > 0)
        {
            string tailSentence = sentenceBuilder.ToString();
            string normalizedTail = NormalizeSentence(tailSentence);
            if (!string.IsNullOrEmpty(normalizedTail)
                && !string.Equals(lastNormalized, normalizedTail, StringComparison.Ordinal))
            {
                outputBuilder.Append(tailSentence.Trim());
                hasAnySentence = true;
            }
        }

        if (!hasAnySentence)
        {
            return text;
        }

        string collapsed = outputBuilder.ToString().Trim();
        return string.IsNullOrEmpty(collapsed) ? text : collapsed;
    }

    /// <summary>
    /// 文比較用に末尾記号を除去して正規化します。
    /// </summary>
    private string NormalizeSentence(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence))
        {
            return string.Empty;
        }

        string normalized = sentence.Trim();
        while (normalized.Length > 0)
        {
            char tail = normalized[normalized.Length - 1];
            if (tail == '。' || tail == '.' || tail == '．' || tail == '!' || tail == '！' || tail == '?' || tail == '？' || tail == '…' || tail == '、' || tail == '，' || tail == ',')
            {
                normalized = normalized.Substring(0, normalized.Length - 1).TrimEnd();
                continue;
            }

            break;
        }

        return normalized;
    }

    /// <summary>
    /// 文字が文末終端記号かどうかを判定します。
    /// </summary>
    private bool IsSentenceTerminator(char ch)
    {
        return ch == '。' || ch == '.' || ch == '．' || ch == '!' || ch == '！' || ch == '?' || ch == '？' || ch == '…';
    }

    /// <summary>
    /// 認識テキストを採用するかを各種フィルタ条件で判定します。
    /// </summary>
    private bool ShouldAccept(string text, bool isIntermediate, out string rejectReason)
    {
        rejectReason = string.Empty;

        if (recognitionInputMode == RecognitionInputMode.PushToTalk && !IsPttAcceptWindow(isIntermediate))
        {
            rejectReason = "ptt-not-held";
            return false;
        }

        if (!isIntermediate
            && duplicateSuppressSec > 0f
            && !string.IsNullOrEmpty(_lastFinalResult)
            && string.Equals(_lastFinalResult, text, StringComparison.Ordinal)
            && _lastFinalAt > 0f)
        {
            float elapsedFromLastFinal = Time.unscaledTime - _lastFinalAt;
            if (elapsedFromLastFinal < duplicateSuppressSec)
            {
                rejectReason = $"duplicate-final({elapsedFromLastFinal:0.000}s<{duplicateSuppressSec:0.000}s)";
                return false;
            }
        }

        if (!isIntermediate
            && extendedDuplicateSuppressSec > 0f
            && !string.IsNullOrEmpty(_lastFinalResult)
            && string.Equals(_lastFinalResult, text, StringComparison.Ordinal)
            && _lastFinalAt > 0f)
        {
            float elapsedFromLastFinal = Time.unscaledTime - _lastFinalAt;
            if (elapsedFromLastFinal < extendedDuplicateSuppressSec)
            {
                rejectReason = $"duplicate-final-extended({elapsedFromLastFinal:0.000}s<{extendedDuplicateSuppressSec:0.000}s)";
                return false;
            }
        }

        int minLen = isIntermediate ? Mathf.Max(1, minIntermediateTextLength) : Mathf.Max(1, minFinalTextLength);
        if (text.Length < minLen)
        {
            rejectReason = $"too-short(len={text.Length},min={minLen})";
            return false;
        }

        if (IsIgnored(text))
        {
            rejectReason = "ignored-exact-text";
            return false;
        }

        if (ContainsIgnored(text))
        {
            rejectReason = "ignored-exact-text-contained";
            return false;
        }

        if (minResultIntervalSec > 0f && _lastAcceptedAt > 0f)
        {
            float elapsed = Time.unscaledTime - _lastAcceptedAt;
            if (elapsed < minResultIntervalSec && string.Equals(_lastAcceptedResult, text, StringComparison.Ordinal))
            {
                rejectReason = $"duplicate-within-interval({elapsed:0.000}s<{minResultIntervalSec:0.000}s)";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// テキストが除外対象フレーズと完全一致するかを判定します。
    /// </summary>
    private bool IsIgnored(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || ignoredExactTexts == null)
        {
            return false;
        }

        string normalized = NormalizeIgnored(text);
        for (int i = 0; i < ignoredExactTexts.Length; i++)
        {
            string ignored = ignoredExactTexts[i];
            if (!string.IsNullOrWhiteSpace(ignored)
                && string.Equals(normalized, NormalizeIgnored(ignored), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// テキストに除外対象フレーズが含まれるかを判定します。
    /// </summary>
    private bool ContainsIgnored(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || ignoredExactTexts == null)
        {
            return false;
        }

        string normalizedText = NormalizeIgnored(text);
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return false;
        }

        for (int i = 0; i < ignoredExactTexts.Length; i++)
        {
            string ignored = ignoredExactTexts[i];
            if (string.IsNullOrWhiteSpace(ignored))
            {
                continue;
            }

            string normalizedIgnored = NormalizeIgnored(ignored);
            if (string.IsNullOrWhiteSpace(normalizedIgnored))
            {
                continue;
            }

            if (normalizedText.IndexOf(normalizedIgnored, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 除外判定用に文字列を正規化します。
    /// </summary>
    private static string NormalizeIgnored(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string normalized = text.Trim();
        while (normalized.Length > 0)
        {
            char tail = normalized[normalized.Length - 1];
            if (tail == '。' || tail == '.' || tail == '．' || tail == '!' || tail == '！' || tail == '?' || tail == '？' || tail == '…' || tail == '、' || tail == '，' || tail == ',')
            {
                normalized = normalized.Substring(0, normalized.Length - 1).TrimEnd();
                continue;
            }

            break;
        }

        return normalized;
    }

    /// <summary>
    /// 確定文同士を空白調整しながら連結します。
    /// </summary>
    private static string ConcatenateFinal(string previous, string current)
    {
        if (string.IsNullOrWhiteSpace(previous))
        {
            return current?.Trim() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(current))
        {
            return previous.Trim();
        }

        string left = previous.TrimEnd();
        string right = current.TrimStart();

        if (left.Length == 0)
        {
            return right;
        }

        if (right.Length == 0)
        {
            return left;
        }

        bool needsSpace = char.IsLetterOrDigit(left[left.Length - 1]) && char.IsLetterOrDigit(right[0]);
        return needsSpace ? $"{left} {right}" : $"{left}{right}";
    }

    /// <summary>
    /// PushToTalk解放猶予を含めて受理ウィンドウ内かを判定します。
    /// </summary>
    private bool IsPttAcceptWindow(bool isIntermediate)
    {
        if (_isPttHeld)
        {
            return true;
        }

        if (!isIntermediate && _isPttFinalizing)
        {
            return true;
        }

        if (_lastPttReleased < 0f)
        {
            return false;
        }

        float graceSeconds = isIntermediate
            ? Mathf.Max(0f, pttReleaseGraceSec)
            : Mathf.Max(pttReleaseGraceSec, pttFinalAcceptGraceSec);

        if (graceSeconds <= 0f)
        {
            return false;
        }

        return Time.unscaledTime - _lastPttReleased <= graceSeconds;
    }

    /// <summary>
    /// 棄却した認識結果を間引き付きでログ出力します。
    /// </summary>
    private void LogDropped(string text, bool isIntermediate, string reason)
    {
        float now = Time.unscaledTime;
        bool isSameAsLast = string.Equals(_lastDropLogReason, reason, StringComparison.Ordinal)
                            && string.Equals(_lastDropLogText, text, StringComparison.Ordinal)
                            && _lastDropLogIntermediate == isIntermediate;

        if (isSameAsLast && _lastDropLogAt > 0f)
        {
            float interval = Mathf.Max(0.5f, dropLogIntervalSec);
            if (now - _lastDropLogAt < interval)
            {
                _suppressedDropCount++;
                return;
            }
        }

        if (_suppressedDropCount > 0)
        {
            Log.Debug($"[STT][drop] suppressed={_suppressedDropCount}, reason={_lastDropLogReason}, intermediate={_lastDropLogIntermediate}, text='{_lastDropLogText}'");
            _suppressedDropCount = 0;
        }

        Log.Debug($"[STT][drop] reason={reason}, intermediate={isIntermediate}, text='{text}'");
        _lastDropLogReason = reason ?? string.Empty;
        _lastDropLogText = text ?? string.Empty;
        _lastDropLogIntermediate = isIntermediate;
        _lastDropLogAt = now;
    }

    /// <summary>
    /// 棄却ログの集計状態を初期化します。
    /// </summary>
    private void ResetDropLog()
    {
        _lastDropLogText = string.Empty;
        _lastDropLogReason = string.Empty;
        _lastDropLogIntermediate = false;
        _lastDropLogAt = -1f;
        _suppressedDropCount = 0;
    }

    /// <summary>
    /// ストリームの時間・セグメント閾値に基づきローテーション要否を判定します。
    /// </summary>
    private bool ShouldRotateStream()
    {
        if (!_running || _isSwitchingStream || _isPendingRestart)
        {
            return false;
        }

        if (isDisabledAutoRotation && recognitionInputMode == RecognitionInputMode.AlwaysListening)
        {
            return false;
        }

        if (_stream == null || _streamStartedAt < 0f)
        {
            return false;
        }

        bool overTime = maxStreamSec > 0f && Time.unscaledTime - _streamStartedAt >= maxStreamSec;
        bool overSegments = maxSegmentsPerStream > 0 && _segmentCount >= maxSegmentsPerStream;
        return overTime || overSegments;
    }

    /// <summary>
    /// 現在のストリームを停止し、新しいストリームへ再接続します。
    /// </summary>
    private async UniTaskVoid RestartStreamAsync(string reason)
    {
        if (_isSwitchingStream || _isStopping || _isStarting)
        {
            return;
        }

        _isSwitchingStream = true;
        try
        {
            if (_stream != null)
            {
                _stream.OnResultUpdated -= OnStreamResultUpdated;
                _stream.OnSegmentFinished -= OnSegmentFinished;
                _stream.OnStreamFinished -= OnStreamFinished;
                TryStopStream(_stream, "rotation");
                _stream = null;
            }

            var recreatedStream = await CreateStreamAsync($"restart:{reason}");
            if (recreatedStream == null)
            {
                Log.Error("[STT] Failed to recreate whisper stream.");
                return;
            }

            AttachStream(recreatedStream);
            Log.Warning($"[STT] Whisper stream restarted. reason={reason}, maxStreamSec={maxStreamSec:0.0}");
        }
        catch (Exception ex)
        {
            Log.Error($"[STT] Failed to restart whisper stream: {ex.Message}");
        }
        finally
        {
            _isSwitchingStream = false;
        }
    }

    /// <summary>
    /// ストリームイベントを購読して認識ストリームを開始します。
    /// </summary>
    private void AttachStream(WhisperStream stream)
    {
        try
        {
            _stream = stream;
            _stream.OnResultUpdated += OnStreamResultUpdated;
            _stream.OnSegmentFinished += OnSegmentFinished;
            _stream.OnStreamFinished += OnStreamFinished;
            _stream.StartStream();
            _isStreamStarted = true;
            _streamStartedAt = Time.unscaledTime;
            _segmentCount = 0;

            if (isLoggedStream)
            {
                Log.Info($"[STT][trace] stream-started mode={recognitionInputMode}, pttHeld={_isPttHeld}, queue={_queuedResultCount}");
            }
        }
        catch (Exception ex)
        {
            LogException("AttachStream", ex);
            throw;
        }
    }

    /// <summary>
    /// タイムアウト付きでWhisperストリームを作成します。
    /// </summary>
    private async UniTask<WhisperStream> CreateStreamAsync(string reason)
    {
        if (whisperManager == null || microphoneRecord == null)
        {
            return null;
        }

        int timeoutMs = Mathf.Max(1000, Mathf.RoundToInt(streamTimeoutSec * 1000f));
        var createTask = whisperManager.CreateStream(microphoneRecord);
        var createUniTask = createTask.AsUniTask();
        var (hasResultLeft, createdStream) = await UniTask.WhenAny(createUniTask, UniTask.Delay(timeoutMs));
        if (!hasResultLeft)
        {
            Log.Error($"[STT] CreateStream timeout. reason={reason}, timeoutMs={timeoutMs}");
            return null;
        }

        return createdStream;
    }

    /// <summary>
    /// 安全にストリーム停止を試行し、結果を返します。
    /// </summary>
    private bool TryStopStream(WhisperStream stream, string reason)
    {
        if (stream == null)
        {
            return false;
        }

        if (!_isStreamStarted)
        {
            return false;
        }

        try
        {
            stream.StopStream();
            _isStreamStarted = false;

            if (isLoggedStream)
            {
                Log.Info($"[STT][trace] stream-stopped reason={reason}, queue={_queuedResultCount}, pendingRestart={_isPendingRestart}");
            }

            return true;
        }
        catch (Exception ex)
        {
            _isStreamStarted = false;
            LogException($"TryStopStream(reason={reason})", ex);
            return false;
        }
    }

    /// <summary>
    /// 例外情報に実行時スナップショットを添えてログ出力します。
    /// </summary>
    private void LogException(string scope, Exception ex)
    {
        if (ex == null)
        {
            return;
        }

        string snapshot = BuildSnapshot(scope);
        Log.Error($"[STT][crash-probe] scope={scope}, exception={ex}\n{snapshot}");
    }

    /// <summary>
    /// 診断用の実行時状態スナップショット文字列を構築します。
    /// </summary>
    private string BuildSnapshot(string scope)
    {
        bool micRecording = microphoneRecord != null && microphoneRecord.IsRecording;
        bool micVoiceDetected = microphoneRecord != null && microphoneRecord.IsVoiceDetected;
        bool whisperLoaded = whisperManager != null && whisperManager.IsLoaded;
        bool whisperLoading = whisperManager != null && whisperManager.IsLoading;

        return "[STT][snapshot]"
               + $" scope={scope}"
               + $", running={_running}"
               + $", isStarting={_isStarting}"
               + $", isStopping={_isStopping}"
               + $", isSwitchingStream={_isSwitchingStream}"
               + $", isStreamStarted={_isStreamStarted}"
               + $", streamNull={_stream == null}"
               + $", pendingRestart={_isPendingRestart}"
               + $", recognitionMode={recognitionInputMode}"
               + $", pttHeld={_isPttHeld}"
               + $", micRecording={micRecording}"
               + $", micVoiceDetected={micVoiceDetected}"
               + $", whisperLoaded={whisperLoaded}"
               + $", whisperLoading={whisperLoading}"
               + $", queueCount={_queuedResultCount}"
               + $", recognizedCount={_recognizedCount}"
               + $", segmentCount={_segmentCount}"
               + $", lastRecognizedAt={_lastRecognizedAt:0.000}"
               + $", lastAcceptedAt={_lastAcceptedAt:0.000}"
               + $", lastFinalAcceptedAt={_lastFinalAt:0.000}";
    }

    /// <summary>
    /// 禁止語扱いで棄却した確定文に対する後処理と再起動判定を行います。
    /// </summary>
    private void OnForbiddenFinal(string text, string reason)
    {
        _lastIntermediateResult = string.Empty;
        _lastAcceptedResult = string.Empty;
        _lastFinalResult = string.Empty;
        _lastFinalAt = -1f;

        if (!restartStreamOnIgnored)
        {
            return;
        }

        if (!_running || _isSwitchingStream || _isPendingRestart || _stream == null)
        {
            return;
        }

        RestartStreamAsync($"{reason}:{text}").Forget();
    }

    /// <summary>
    /// 棄却理由が禁止語由来かを判定します。
    /// </summary>
    private bool IsForbiddenReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        return reason.StartsWith("ignored-exact-text", StringComparison.Ordinal);
    }

    /// <summary>
    /// 認識済みテキストを表示・記録し、確定文を外部転送します。
    /// </summary>
    private void OnRecognizedText(string text, bool isIntermediate)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _recognizedCount++;
        _lastRecognizedAt = Time.unscaledTime;
        _elapsedSinceStart = 0f;

        ShowRecognized(text);

        Log.Info($"[STT] Recognized({_recognizedCount}, intermediate={isIntermediate}): {text}");

        if (!isIntermediate)
        {
            ForwardFinalText(text);
        }
    }

    /// <summary>
    /// 確定テキストをUDPでキャラクタープロセスへ転送します。
    /// </summary>
    private void ForwardFinalText(string recognizedText)
    {
        if (!_isUiProcess)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(recognizedText))
        {
            return;
        }

        if (mainProcessUdpPort <= 0)
        {
            Log.Warning($"[STT] Character process forwarding skipped: invalid UDP port ({mainProcessUdpPort}).");
            return;
        }

        try
        {
            var payload = new SttForwardMessage
            {
                text = recognizedText,
                source = nameof(STTManager),
                timestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };

            string json = JsonUtility.ToJson(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using var client = new UdpClient();
            client.Send(bytes, bytes.Length, mainProcessUdpHost, mainProcessUdpPort);

            if (logForwarding)
            {
                Log.Info($"[STT] Forwarded final recognized text to character process. host={mainProcessUdpHost}, port={mainProcessUdpPort}, text='{recognizedText}'");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[STT] Failed to forward recognized text to character process: {ex.Message}");
        }
    }

    [Serializable]
    private struct SttForwardMessage
    {
        public string text;
        public string source;
        public long timestampUnixMs;
    }

    /// <summary>
    /// 認識可否と入力状態に応じてマイクUI表示を更新します。
    /// </summary>
    private void UpdateMicUi()
    {
        if (micIcon == null && micIconObject != null)
        {
            micIconObject.SetActive(false);
            if (!_hasMicUiTracked || _lastMicIconVisible || _lastMicCanRecognize)
            {
                _lastMicIconVisible = false;
                _lastMicCanRecognize = false;
                _hasMicUiTracked = true;
                RequestUiFront("mic-icon-hidden");
            }
            return;
        }

        bool isInitialized = whisperManager != null && whisperManager.IsLoaded;
        bool isRecording = microphoneRecord != null && microphoneRecord.IsRecording;
        bool isBlockedByLlm = ShouldBlockSttInput();
        bool isFinalizing = recognitionInputMode == RecognitionInputMode.PushToTalk && _isPttFinalizing;
        bool isInputActive = recognitionInputMode == RecognitionInputMode.AlwaysListening || _isPttHeld || isFinalizing;
        bool shouldShowIconObject = isInitialized && _running && (isInputActive || isBlockedByLlm);
        bool canRecognize = isInitialized
                            && _running
                    && isInputActive
                    && !isFinalizing
                            && !isBlockedByLlm
                            && isRecording;

        bool iconStateChanged = !_hasMicUiTracked
                    || _lastMicIconVisible != shouldShowIconObject
                    || _lastMicCanRecognize != canRecognize;

        if (micIconObject != null)
        {
            micIconObject.SetActive(shouldShowIconObject);
        }
        else if (micIcon != null)
        {
            micIcon.gameObject.SetActive(shouldShowIconObject);
        }

        if (micIcon == null)
        {
            _lastMicIconVisible = shouldShowIconObject;
            _lastMicCanRecognize = canRecognize;
            _hasMicUiTracked = true;
            if (iconStateChanged)
            {
                RequestUiFront("mic-icon-switch(no-icon)");
            }
            return;
        }

        micIcon.SwitchIcon(canRecognize);
        _lastMicIconVisible = shouldShowIconObject;
        _lastMicCanRecognize = canRecognize;
        _hasMicUiTracked = true;
        if (iconStateChanged)
        {
            RequestUiFront("mic-icon-switch");
        }
    }

    /// <summary>
    /// 認識テキストをUIへ表示し、自動非表示タイマーを開始します。
    /// </summary>
    private void ShowRecognized(string text)
    {
        if (recognizedText == null)
        {
            return;
        }

        recognizedText.text = text ?? string.Empty;
        GameObject visibilityObject = ResolveResultVisibility();
        if (visibilityObject != null && !visibilityObject.activeSelf)
        {
            visibilityObject.SetActive(true);
        }

        ScheduleResultHide();
        RequestUiFront("recognized-text");
    }

    /// <summary>
    /// 認識テキスト表示を消去し、表示オブジェクトを非表示にします。
    /// </summary>
    private void ClearRecognized()
    {
        CancelResultHide();

        if (recognizedText == null)
        {
            return;
        }

        recognizedText.text = string.Empty;
        GameObject visibilityObject = ResolveResultVisibility();
        if (visibilityObject != null && visibilityObject.activeSelf)
        {
            visibilityObject.SetActive(false);
        }

        RequestUiFront("recognized-text-hidden");
    }

    /// <summary>
    /// 認識表示に使う可視制御オブジェクトを解決します。
    /// </summary>
    private GameObject ResolveResultVisibility()
    {
        if (recognizedVisibilityObject != null)
        {
            return recognizedVisibilityObject;
        }

        return recognizedText != null ? recognizedText.gameObject : null;
    }

    /// <summary>
    /// 認識表示の遅延非表示コルーチンを開始します。
    /// </summary>
    private void ScheduleResultHide()
    {
        CancelResultHide();

        if (resultHideDelaySec <= 0f)
        {
            return;
        }

        _recognizedTextHideCoroutine = StartCoroutine(DelayedResultHide(resultHideDelaySec));
    }

    /// <summary>
    /// 認識表示の遅延非表示コルーチンを停止します。
    /// </summary>
    private void CancelResultHide()
    {
        if (_recognizedTextHideCoroutine == null)
        {
            return;
        }

        StopCoroutine(_recognizedTextHideCoroutine);
        _recognizedTextHideCoroutine = null;
    }

    /// <summary>
    /// 指定秒待機して認識表示をクリアします。
    /// </summary>
    private System.Collections.IEnumerator DelayedResultHide(float delaySeconds)
    {
        yield return new WaitForSeconds(delaySeconds);
        _recognizedTextHideCoroutine = null;
        ClearRecognized();
    }

    /// <summary>
    /// STT入力をブロックすべきかを返します。
    /// </summary>
    private bool ShouldBlockSttInput()
    {
        return false;
    }

    /// <summary>
    /// UIプロセス時にウィンドウを前面化します。
    /// </summary>
    private void RequestUiFront(string reason)
    {
        if (!_isUiProcess)
        {
            return;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        float cooldown = Mathf.Max(0.05f, uiFrontCooldownSec);
        float now = Time.unscaledTime;
        if (_lastUiFrontAt > 0f && now - _lastUiFrontAt < cooldown)
        {
            return;
        }

        _lastUiFrontAt = now;
        WindowsAPI.SetCurrentWindowTopmost(true);
        WindowsAPI.BringCurrentWindowToFront();
        WindowsAPI.SetCurrentWindowTopmost(false);
#endif
    }

    /// <summary>
    /// PushToTalk解放後の確定フェーズを開始します。
    /// </summary>
    private void BeginPttFinalize()
    {
        if (recognitionInputMode != RecognitionInputMode.PushToTalk)
        {
            return;
        }

        _isPttFinalizing = true;
        _pttFinalizeStarted = Time.unscaledTime;
    }

    /// <summary>
    /// PushToTalk確定フェーズを終了します。
    /// </summary>
    private void EndPttFinalize()
    {
        _isPttFinalizing = false;
        _pttFinalizeStarted = -1f;
    }

    /// <summary>
    /// PushToTalk入力が確定フェーズでロック中かを判定します。
    /// </summary>
    private bool IsPttLocked()
    {
        return recognitionInputMode == RecognitionInputMode.PushToTalk && _isPttFinalizing;
    }

    /// <summary>
    /// PushToTalk確定ロックのタイムアウト復旧を行います。
    /// </summary>
    private void RecoverPttFinalizeTimeout()
    {
        if (!_isPttFinalizing)
        {
            return;
        }

        float timeout = Mathf.Max(0.5f, pttFinalizeLockSec);
        if (_pttFinalizeStarted < 0f)
        {
            _pttFinalizeStarted = Time.unscaledTime;
            return;
        }

        if (Time.unscaledTime - _pttFinalizeStarted < timeout)
        {
            return;
        }

        Log.Warning($"[STT] PushToTalk finalize lock timed out ({timeout:0.0}s). Releasing lock.");
        EndPttFinalize();
    }

    /// <summary>
    /// 実行引数からUIプロセス起動かどうかを判定します。
    /// </summary>
    private static bool IsUiProcess()
    {
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args == null)
            {
                return false;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], Constant.UIProcessArgument, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// 起動時に選択中マイク情報をログ出力します。
    /// </summary>
    private void LogMicDevice()
    {
        if (microphoneRecord == null)
        {
            return;
        }

        string selected = microphoneRecord.SelectedMicDevice;
        string display = string.IsNullOrWhiteSpace(selected) ? "default" : selected;
        Log.Info($"[STT] Startup microphone device: {display}, frequency={microphoneRecord.frequency}");
    }

    /// <summary>
    /// VAD関連の感度設定をマイクとWhisperへ反映します。
    /// </summary>
    private void ApplyMicSensitivity()
    {
        bool shouldDisableVadForAlwaysListening = isDisabledVad && recognitionInputMode == RecognitionInputMode.AlwaysListening;
        microphoneRecord.useVad = !shouldDisableVadForAlwaysListening;
        microphoneRecord.vadThd = Mathf.Max(0.01f, vadThreshold);
        microphoneRecord.vadFreqThd = Mathf.Max(1f, vadFrequencyThreshold);
        microphoneRecord.vadLastSec = Mathf.Max(0.05f, vadLastSeconds);
        microphoneRecord.vadContextSec = Mathf.Max(microphoneRecord.vadLastSec, vadContextSeconds);
        microphoneRecord.vadUpdateRateSec = Mathf.Max(0.01f, vadUpdateRateSeconds);

        if (whisperManager != null)
        {
            whisperManager.useVad = microphoneRecord.useVad;
        }

        if (shouldDisableVadForAlwaysListening)
        {
            Log.Warning("[STT] VAD disabled automatically for AlwaysListening mode (stability preference).");
        }

        Log.Info($"[STT] Applied VAD settings. mic.useVad={microphoneRecord.useVad}, whisper.useVad={whisperManager?.useVad}, vadThd={microphoneRecord.vadThd:0.###}, vadFreqThd={microphoneRecord.vadFreqThd:0.###}, vadLastSec={microphoneRecord.vadLastSec:0.###}, vadContextSec={microphoneRecord.vadContextSec:0.###}, vadUpdateRateSec={microphoneRecord.vadUpdateRateSec:0.###}");
    }

    /// <summary>
    /// AlwaysListening向けのストリーム安定化設定を適用します。
    /// </summary>
    private void ApplyStreamingStability()
    {
        if (whisperManager == null || recognitionInputMode != RecognitionInputMode.AlwaysListening)
        {
            return;
        }

        bool changed = false;

        if (isDisablePromptUpdate && whisperManager.updatePrompt)
        {
            whisperManager.updatePrompt = false;
            changed = true;
        }

        float minStep = Mathf.Max(0.5f, minStepSec);
        if (whisperManager.stepSec < minStep)
        {
            whisperManager.stepSec = minStep;
            changed = true;
        }

        float minLength = Mathf.Max(whisperManager.stepSec + 1f, minLengthSec);
        if (whisperManager.lengthSec < minLength)
        {
            whisperManager.lengthSec = minLength;
            changed = true;
        }

        if (changed)
        {
            Log.Warning($"[STT] Applied AlwaysListening stability settings. updatePrompt={whisperManager.updatePrompt}, stepSec={whisperManager.stepSec:0.###}, lengthSec={whisperManager.lengthSec:0.###}");
        }
    }

    /// <summary>
    /// Whisperモデルの初期化完了をタイムアウト付きで待機します。
    /// </summary>
    private async UniTask<bool> EnsureModelReady()
    {
        if (whisperManager == null)
        {
            return false;
        }

        if (!whisperManager.IsLoaded && !whisperManager.IsLoading)
        {
            await whisperManager.InitModel();
        }

        float timeout = Mathf.Max(1f, modelLoadTimeoutSec);
        float elapsed = 0f;
        while (whisperManager.IsLoading && elapsed < timeout)
        {
            await UniTask.Delay(100);
            elapsed += 0.1f;
        }

        if (whisperManager.IsLoading)
        {
            Log.Error($"[STT] Whisper model load timeout. timeout={timeout:0.0}s");
            return false;
        }

        return whisperManager.IsLoaded;
    }

    /// <summary>
    /// SettingsProviderからブリッジ設定を読み込みます。
    /// </summary>
    private void ApplySettings()
    {
        mainProcessUdpHost = Constant.BackendHost;
        mainProcessUdpPort = Constant.BackendPort;

        try
        {
            var provider = SettingsProvider.Instance;
            if (provider?.Backend != null && provider.Backend.Port > 0)
            {
                mainProcessUdpPort = provider.Backend.Port;
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[STT] Failed to load STT bridge settings from SettingsProvider: {ex.Message}");
        }
    }
}
