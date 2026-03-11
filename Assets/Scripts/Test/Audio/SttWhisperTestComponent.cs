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

public class SttWhisperTestComponent : MonoBehaviour
{
    private const string ExternalUiProcessRoleArgument = "--external-ui-process";
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private const int VK_SPACE = 0x20;

    [DllImport("user32.dll")]
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
    [SerializeField] private SwitchMicrophoneIcon sttMicrophoneIcon;
    [SerializeField] private GameObject sttMicrophoneIconObject;
    [SerializeField] private TMP_Text sttRecognizedText;
    [SerializeField] private GameObject sttRecognizedVisibilityObject;

    [Header("Run")]
    [SerializeField] private bool runOnStart = true;
    [SerializeField] private bool stopRecordingOnExit = true;
    [SerializeField] private RecognitionInputMode recognitionInputMode = RecognitionInputMode.AlwaysListening;
    [SerializeField] private InputActionProperty pushToTalkInput;
    [SerializeField] private string fallbackPushToTalkBinding = "<Keyboard>/space";
    [SerializeField] private bool keepMicrophoneOpenInPushToTalk = true;
    [SerializeField] private float pushToTalkReleaseGraceSeconds = 1.0f;
    [SerializeField] private bool flushStreamOnPushToTalkRelease = true;
    [SerializeField] private int pushToTalkFlushDelayMs = 120;
    [SerializeField] private float pushToTalkFlushCooldownSeconds = 0.35f;
    [SerializeField] private float pushToTalkFlushRestartTimeoutSeconds = 1.5f;
    [SerializeField] private bool allowGlobalPushToTalkWhenUnfocused = true;
    [SerializeField] private int globalPushToTalkVirtualKey = VK_SPACE;

    [Header("Display")]
    [SerializeField] private float recognizedTextHideDelaySeconds = 4f;
    [SerializeField] private bool showIntermediateResult;

    [Header("Diagnostics")]
    [SerializeField] private float noResultWarningSeconds = 20f;
    [SerializeField] private float noResultWarningRepeatSeconds = 10f;
    [SerializeField] private float whisperModelLoadTimeoutSeconds = 60f;
    [SerializeField] private float createStreamTimeoutSeconds = 12f;
    [SerializeField] private bool enableCrashInvestigationLogs = true;
    [SerializeField] private bool logStreamLifecycleTransitions;
    [SerializeField] private bool throttleRepeatedDropLogs = true;
    [SerializeField] private float repeatedDropLogIntervalSeconds = 15f;

    [Header("Stability")]
    [SerializeField] private float maxContinuousStreamSeconds = 30f;
    [SerializeField] private int maxSegmentsPerStream = 8;
    [SerializeField] private int maxQueuedRecognitionResults = 64;
    [SerializeField] private bool dropDuplicateIntermediateResults = true;
    [SerializeField] private bool disableAutoRotationInAlwaysListening = true;
    [SerializeField] private bool disablePromptUpdateInAlwaysListening = true;
    [SerializeField] private float minStepSecInAlwaysListening = 4f;
    [SerializeField] private float minLengthSecInAlwaysListening = 12f;

    [Header("Noise Filter")]
    [SerializeField] private bool enableNoiseFilter = true;
    [SerializeField] private bool logDroppedRecognitionReasons = false;
    [SerializeField] private bool ignoreWhenVadSaysNoVoice = true;
    [SerializeField] private int minFinalTextLength = 2;
    [SerializeField] private int minIntermediateTextLength = 4;
    [SerializeField] private float minSecondsBetweenAcceptedResults = 0.35f;
    [SerializeField] private float duplicateFinalSuppressSeconds = 2.0f;
    [SerializeField] private float extendedDuplicateFinalSuppressSeconds = 8.0f;
    [SerializeField] private bool collapseRepeatedFinalText = true;
    [SerializeField] private int repeatedFinalMinRepeats = 2;
    [SerializeField] private bool concatenateConsecutiveFinalResults = true;
    [SerializeField] private float finalConcatenateWindowSeconds = 1.0f;
    [SerializeField] private string[] ignoredExactTexts = { ".", "..", "...", "えー", "あー", "んー" };
    [SerializeField] private bool restartStreamOnIgnoredExactText = false;

    [Header("Input Sensitivity")]
    [SerializeField] private bool overrideMicrophoneVadSettings = true;
    [SerializeField] private bool useVadForMicrophone = true;
    [SerializeField] private bool disableVadInAlwaysListening = true;
    [SerializeField] private float vadThreshold = 1.0f;
    [SerializeField] private float vadFrequencyThreshold = 100.0f;
    [SerializeField] private float vadLastSeconds = 1.25f;
    [SerializeField] private float vadContextSeconds = 30.0f;
    [SerializeField] private float vadUpdateRateSeconds = 0.1f;

    [Header("Character Process Bridge (Test)")]
    [SerializeField] private bool forwardFinalRecognizedTextToCharacterProcess = true;
    [SerializeField] private bool forwardOnlyFromExternalUiProcess = true;
    [SerializeField] private string characterProcessUdpHost = "127.0.0.1";
    [SerializeField] private int characterProcessUdpPort = 27651;
    [SerializeField] private bool logCharacterProcessForwarding;

    private readonly struct RecognizedQueueItem
    {
        public readonly string Text;
        public readonly bool IsIntermediate;

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
    private bool _pushToTalkHeld;
    private bool _pendingRestartAfterStreamFinish;
    private bool _isSwitchingStream;
    private int _recognizedCount;
    private int _segmentFinishedCountInStream;
    private int _queuedResultCount;
    private float _elapsedSinceStart;
    private float _elapsedSinceLastNoResultWarning;
    private float _lastRecognizedAt = -1f;
    private float _lastAcceptedAt = -1f;
    private float _lastPushToTalkReleasedAt = -1f;
    private float _lastPushToTalkFlushAt = -1f;
    private float _pendingRestartRequestedAt = -1f;
    private float _streamStartedAt = -1f;
    private string _lastAcceptedResult = string.Empty;
    private string _lastIntermediateResult = string.Empty;
    private string _lastAcceptedFinalResult = string.Empty;
    private float _lastAcceptedFinalAt = -1f;
    private bool _isStreamStarted;
    private string _lastDropLogText = string.Empty;
    private string _lastDropLogReason = string.Empty;
    private bool _lastDropLogIntermediate;
    private float _lastDropLogAt = -1f;
    private int _suppressedDropLogCount;

    private Coroutine _recognizedTextHideCoroutine;
    private InputAction _pushToTalkAction;
    private bool _ownsPushToTalkAction;
    private bool _isExternalUiProcess;
    private bool _lastShouldRecordState;
    private bool _hasLastShouldRecordState;
    private readonly ConcurrentQueue<RecognizedQueueItem> _recognizedQueue = new();

    private void Awake()
    {
        _isExternalUiProcess = IsExternalUiProcess();
        SetupPushToTalkAction();
    }

    private void OnEnable()
    {
        EnablePushToTalkAction();
    }

    private void OnDisable()
    {
        DisablePushToTalkAction();
    }

    private void Start()
    {
        if (runOnStart)
        {
            StartWhisperTest();
        }
    }

    private void Update()
    {
        try
        {
            UpdateBackgroundPushToTalkState();
            UpdateRecognitionRecordingState();
            DrainRecognizedQueue();
            UpdateMicrophoneUi();
        }
        catch (Exception ex)
        {
            LogExceptionWithSnapshot("UpdateLoop", ex);
        }

        if (!_running)
        {
            return;
        }

        RecoverPendingFlushRestartIfStuck();

        if (ShouldRotateStream())
        {
            RestartStreamAsync($"rotation(time={maxContinuousStreamSeconds:0.0}s,segments={_segmentFinishedCountInStream})").Forget();
        }

        bool isInitialized = whisperManager != null && whisperManager.IsLoaded;
        bool isRecording = microphoneRecord != null && microphoneRecord.IsRecording;
        if (!isInitialized || !isRecording)
        {
            _elapsedSinceStart = 0f;
            _elapsedSinceLastNoResultWarning = 0f;
            return;
        }

        _elapsedSinceStart += Time.deltaTime;
        _elapsedSinceLastNoResultWarning += Time.deltaTime;

        bool reachedNoResultThreshold = noResultWarningSeconds > 0f && _elapsedSinceStart >= noResultWarningSeconds;
        bool reachedWarningRepeat = noResultWarningRepeatSeconds <= 0f || _elapsedSinceLastNoResultWarning >= noResultWarningRepeatSeconds;
        if (reachedNoResultThreshold && reachedWarningRepeat)
        {
            _elapsedSinceLastNoResultWarning = 0f;
            float sinceLast = _lastRecognizedAt < 0f ? -1f : Time.unscaledTime - _lastRecognizedAt;
            Log.Warning($"[SttWhisperTest] No recognition result yet. elapsed={_elapsedSinceStart:0.0}s, initialized={isInitialized}, recording={isRecording}, mode={recognitionInputMode}, pttHeld={_pushToTalkHeld}, recognizedCount={_recognizedCount}, sinceLast={(sinceLast < 0f ? "N/A" : sinceLast.ToString("0.0") + "s")}");
        }
    }

    private void OnApplicationQuit()
    {
        if (stopRecordingOnExit)
        {
            StopWhisperTest();
        }
    }

    private void OnDestroy()
    {
        DisablePushToTalkAction();
        DisposePushToTalkAction();

        if (stopRecordingOnExit)
        {
            StopWhisperTest();
        }
    }

    [ContextMenu("Start Whisper STT Test")]
    public void StartWhisperTest()
    {
        if (_isStarting)
        {
            Log.Warning("[SttWhisperTest] Start request ignored: start is already in progress.");
            return;
        }

        if (_isStopping)
        {
            Log.Warning("[SttWhisperTest] Start request ignored: stop is in progress.");
            return;
        }

        StartWhisperTestAsync().Forget();
    }

    [ContextMenu("Stop Whisper STT Test")]
    public void StopWhisperTest()
    {
        if (_isStopping)
        {
            return;
        }

        _isStopping = true;
        _isStarting = false;

        _running = false;
        _pushToTalkHeld = false;
        _pendingRestartAfterStreamFinish = false;
        _isSwitchingStream = false;
        _recognizedCount = 0;
        _segmentFinishedCountInStream = 0;
        _queuedResultCount = 0;
        _elapsedSinceStart = 0f;
        _elapsedSinceLastNoResultWarning = 0f;
        _lastRecognizedAt = -1f;
        _lastAcceptedAt = -1f;
        _lastPushToTalkReleasedAt = -1f;
        _lastPushToTalkFlushAt = -1f;
        _pendingRestartRequestedAt = -1f;
        _streamStartedAt = -1f;
        _lastAcceptedResult = string.Empty;
        _lastIntermediateResult = string.Empty;
        _lastAcceptedFinalResult = string.Empty;
        _lastAcceptedFinalAt = -1f;
        _isStreamStarted = false;
        ResetDropLogState();

        if (_stream != null)
        {
            _stream.OnResultUpdated -= HandleStreamResultUpdated;
            _stream.OnSegmentFinished -= HandleSegmentFinished;
            _stream.OnStreamFinished -= HandleStreamFinished;
            StopStreamSafely(_stream, "stop-test");
            _stream = null;
        }

        if (microphoneRecord != null && microphoneRecord.IsRecording)
        {
            microphoneRecord.StopRecord();
        }

        while (_recognizedQueue.TryDequeue(out _)) { }

        UpdateMicrophoneUi();
        ClearRecognizedText();

        _isStopping = false;

        Log.Info("[SttWhisperTest] Whisper STT test stopped.");
    }

    private async UniTaskVoid StartWhisperTestAsync()
    {
        _isStarting = true;

        try
        {
        if (!TryResolveDependencies())
        {
            return;
        }

        ApplyMicrophoneSensitivitySettings();
        ApplyWhisperStreamingStabilitySettings();
        LogStartupMicrophoneDevice();

        bool modelReady = await EnsureWhisperModelReady();
        if (!modelReady)
        {
            Log.Error("[SttWhisperTest] Whisper model is not loaded.");
            return;
        }

        if (_isStopping)
        {
            return;
        }

        if (_stream != null)
        {
            _stream.OnResultUpdated -= HandleStreamResultUpdated;
            _stream.OnSegmentFinished -= HandleSegmentFinished;
            _stream.OnStreamFinished -= HandleStreamFinished;
            StopStreamSafely(_stream, "restart-start-test");
            _stream = null;
        }

        var createdStream = await CreateStreamWithTimeoutAsync("start");
        if (createdStream == null)
        {
            Log.Error("[SttWhisperTest] Failed to create whisper stream.");
            return;
        }

        AttachAndStartStream(createdStream);

        _running = true;
        _pendingRestartAfterStreamFinish = false;
        _recognizedCount = 0;
        _segmentFinishedCountInStream = 0;
        _queuedResultCount = 0;
        _elapsedSinceStart = 0f;
        _elapsedSinceLastNoResultWarning = 0f;
        _lastRecognizedAt = -1f;
        _lastAcceptedAt = -1f;
        _lastPushToTalkReleasedAt = -1f;
        _lastPushToTalkFlushAt = -1f;
        _pendingRestartRequestedAt = -1f;
        _lastAcceptedResult = string.Empty;
        _lastIntermediateResult = string.Empty;
        _lastAcceptedFinalResult = string.Empty;
        _lastAcceptedFinalAt = -1f;
        ResetDropLogState();
        ClearRecognizedText();

        if (recognitionInputMode == RecognitionInputMode.AlwaysListening)
        {
            EnsureMicRecording(true);
        }
        else
        {
            EnsureMicRecording(false);
        }

        Log.Info($"[SttWhisperTest] Whisper STT test started. mode={recognitionInputMode}, micRecording={microphoneRecord.IsRecording}");
        }
        finally
        {
            _isStarting = false;
        }
    }

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
            Log.Error("[SttWhisperTest] WhisperManager is missing.");
            return false;
        }

        if (microphoneRecord == null)
        {
            Log.Error("[SttWhisperTest] MicrophoneRecord is missing.");
            return false;
        }

        return true;
    }

    private void SetupPushToTalkAction()
    {
        DisposePushToTalkAction();

        _pushToTalkAction = pushToTalkInput.action;
        _ownsPushToTalkAction = false;

        if (_pushToTalkAction == null)
        {
            string binding = string.IsNullOrWhiteSpace(fallbackPushToTalkBinding) ? "<Keyboard>/space" : fallbackPushToTalkBinding;
            _pushToTalkAction = new InputAction("WhisperPushToTalk", InputActionType.Button, binding);
            _ownsPushToTalkAction = true;
        }

        _pushToTalkAction.started += OnPushToTalkStarted;
        _pushToTalkAction.canceled += OnPushToTalkCanceled;
    }

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

    private void DisablePushToTalkAction()
    {
        if (_pushToTalkAction != null && _pushToTalkAction.enabled)
        {
            _pushToTalkAction.Disable();
        }

        _pushToTalkHeld = false;
    }

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

        _pushToTalkHeld = true;
        _lastPushToTalkReleasedAt = -1f;
    }

    private void OnPushToTalkCanceled(InputAction.CallbackContext _)
    {
        _pushToTalkHeld = false;
        _lastPushToTalkReleasedAt = Time.unscaledTime;

        if (flushStreamOnPushToTalkRelease)
        {
            FlushStreamOnPushToTalkReleaseAsync().Forget();
        }
    }

    private void UpdateBackgroundPushToTalkState()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!_isExternalUiProcess
            || !allowGlobalPushToTalkWhenUnfocused
            || recognitionInputMode != RecognitionInputMode.PushToTalk
            || Application.isFocused)
        {
            return;
        }

        int virtualKey = Mathf.Clamp(globalPushToTalkVirtualKey, 1, 255);
        bool isPressed = (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

        if (isPressed)
        {
            if (!_pushToTalkHeld)
            {
                _pushToTalkHeld = true;
                _lastPushToTalkReleasedAt = -1f;
            }

            return;
        }

        if (_pushToTalkHeld)
        {
            _pushToTalkHeld = false;
            _lastPushToTalkReleasedAt = Time.unscaledTime;

            if (flushStreamOnPushToTalkRelease)
            {
                FlushStreamOnPushToTalkReleaseAsync().Forget();
            }
        }
#endif
    }

    private async UniTaskVoid FlushStreamOnPushToTalkReleaseAsync()
    {
        if (!_running || recognitionInputMode != RecognitionInputMode.PushToTalk)
        {
            return;
        }

        if (_stream == null || _isSwitchingStream || _pendingRestartAfterStreamFinish)
        {
            return;
        }

        if (_lastPushToTalkFlushAt > 0f && Time.unscaledTime - _lastPushToTalkFlushAt < pushToTalkFlushCooldownSeconds)
        {
            return;
        }

        int delayMs = Mathf.Max(0, pushToTalkFlushDelayMs);
        if (delayMs > 0)
        {
            await UniTask.Delay(delayMs);
        }

        if (!_running || recognitionInputMode != RecognitionInputMode.PushToTalk || _pushToTalkHeld)
        {
            return;
        }

        if (_stream == null || _isSwitchingStream || _pendingRestartAfterStreamFinish)
        {
            return;
        }

        _lastPushToTalkFlushAt = Time.unscaledTime;
        _pendingRestartAfterStreamFinish = true;
        _pendingRestartRequestedAt = Time.unscaledTime;
        bool requestedStop = StopStreamSafely(_stream, "ptt-flush");
        if (!requestedStop)
        {
            _pendingRestartAfterStreamFinish = false;
            _pendingRestartRequestedAt = -1f;
            StartStreamAfterFlushAsync().Forget();
        }
    }

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
                _stream.OnResultUpdated -= HandleStreamResultUpdated;
                _stream.OnSegmentFinished -= HandleSegmentFinished;
                _stream.OnStreamFinished -= HandleStreamFinished;
                _stream = null;
            }

            var recreatedStream = await CreateStreamWithTimeoutAsync("ptt-flush");
            if (recreatedStream == null)
            {
                Log.Error("[SttWhisperTest] Failed to recreate whisper stream after flush.");
                return;
            }

            AttachAndStartStream(recreatedStream);
            Log.Info("[SttWhisperTest] Whisper stream restarted after push-to-talk flush.");
        }
        catch (Exception ex)
        {
            Log.Error($"[SttWhisperTest] Failed to restart stream after push-to-talk flush: {ex.Message}");
        }
        finally
        {
            _isSwitchingStream = false;
        }
    }

    private void UpdateRecognitionRecordingState()
    {
        if (!_running || whisperManager == null || microphoneRecord == null)
        {
            _pushToTalkHeld = false;
            return;
        }

        bool shouldBlockInput = ShouldBlockSttInput();

        if (recognitionInputMode != RecognitionInputMode.PushToTalk)
        {
            _pushToTalkHeld = false;
        }

        if (shouldBlockInput)
        {
            _pushToTalkHeld = false;
        }

        bool shouldRecord = recognitionInputMode == RecognitionInputMode.AlwaysListening
                            || _pushToTalkHeld
                            || (recognitionInputMode == RecognitionInputMode.PushToTalk && keepMicrophoneOpenInPushToTalk);
        if (shouldBlockInput)
        {
            shouldRecord = false;
        }

        EnsureMicRecording(shouldRecord);
    }

    private void EnsureMicRecording(bool shouldRecord)
    {
        if (microphoneRecord == null)
        {
            return;
        }

        if (logStreamLifecycleTransitions)
        {
            if (!_hasLastShouldRecordState || _lastShouldRecordState != shouldRecord)
            {
                _lastShouldRecordState = shouldRecord;
                _hasLastShouldRecordState = true;
                Log.Info($"[SttWhisperTest][trace] mic-state-transition shouldRecord={shouldRecord}, isRecording={microphoneRecord.IsRecording}, mode={recognitionInputMode}, pttHeld={_pushToTalkHeld}");
            }
        }

        if (shouldRecord && !microphoneRecord.IsRecording)
        {
            microphoneRecord.StartRecord();
        }
        else if (!shouldRecord && microphoneRecord.IsRecording)
        {
            microphoneRecord.StopRecord();
        }
    }

    private void HandleStreamResultUpdated(string updatedResult)
    {
        try
        {
            if (!showIntermediateResult || string.IsNullOrWhiteSpace(updatedResult))
            {
                return;
            }

            TryEnqueueRecognizedText(updatedResult, true);
        }
        catch (Exception ex)
        {
            LogExceptionWithSnapshot("HandleStreamResultUpdated", ex);
        }
    }

    private void HandleSegmentFinished(WhisperResult segment)
    {
        try
        {
            if (segment == null || string.IsNullOrWhiteSpace(segment.Result))
            {
                return;
            }

            _segmentFinishedCountInStream++;
            TryEnqueueRecognizedText(segment.Result, false);
        }
        catch (Exception ex)
        {
            LogExceptionWithSnapshot("HandleSegmentFinished", ex);
        }
    }

    private void HandleStreamFinished(string finalResult)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(finalResult))
            {
                TryEnqueueRecognizedText(finalResult, false);
            }

            _isStreamStarted = false;

            if (_pendingRestartAfterStreamFinish)
            {
                _pendingRestartAfterStreamFinish = false;
                _pendingRestartRequestedAt = -1f;
                StartStreamAfterFlushAsync().Forget();
            }
        }
        catch (Exception ex)
        {
            LogExceptionWithSnapshot("HandleStreamFinished", ex);
        }
    }

    private void RecoverPendingFlushRestartIfStuck()
    {
        if (!_pendingRestartAfterStreamFinish || _isSwitchingStream || _isStarting || _isStopping)
        {
            return;
        }

        if (_pendingRestartRequestedAt < 0f)
        {
            _pendingRestartRequestedAt = Time.unscaledTime;
            return;
        }

        float timeout = Mathf.Max(0.2f, pushToTalkFlushRestartTimeoutSeconds);
        float elapsed = Time.unscaledTime - _pendingRestartRequestedAt;
        if (elapsed < timeout)
        {
            return;
        }

        _pendingRestartAfterStreamFinish = false;
        _pendingRestartRequestedAt = -1f;
        Log.Warning($"[SttWhisperTest] Pending flush restart timed out ({elapsed:0.000}s). Restarting stream forcefully.");
        StartStreamAfterFlushAsync().Forget();
    }

    private void DrainRecognizedQueue()
    {
        while (_recognizedQueue.TryDequeue(out var item))
        {
            _queuedResultCount = Mathf.Max(0, _queuedResultCount - 1);
            try
            {
                HandleRecognizedText(item.Text, item.IsIntermediate);
            }
            catch (Exception ex)
            {
                LogExceptionWithSnapshot($"DrainRecognizedQueue(intermediate={item.IsIntermediate})", ex);
            }
        }
    }

    private void TryEnqueueRecognizedText(string text, bool isIntermediate)
    {
        try
        {
            if (ShouldBlockSttInput())
            {
                LogDroppedRecognition(text, isIntermediate, "llm-response-in-progress");
                return;
            }

            string normalized = text?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

        if (!isIntermediate
            && concatenateConsecutiveFinalResults
            && !string.IsNullOrWhiteSpace(_lastAcceptedFinalResult))
        {
            if (_lastAcceptedFinalAt > 0f && finalConcatenateWindowSeconds > 0f)
            {
                float elapsedFromLastFinal = Time.unscaledTime - _lastAcceptedFinalAt;
                if (elapsedFromLastFinal > finalConcatenateWindowSeconds)
                {
                    goto SkipFinalConcatenate;
                }
            }

            bool previousIsForbidden = IsIgnoredExactText(_lastAcceptedFinalResult);
            bool incomingIsForbidden = IsIgnoredExactText(normalized);
            bool incomingContainsForbidden = ContainsIgnoredPhrase(normalized);

            if (incomingIsForbidden || incomingContainsForbidden)
            {
                LogDroppedRecognition(normalized, isIntermediate, "ignored-exact-text");
                return;
            }

            if (!previousIsForbidden)
            {
                normalized = ConcatenateFinalText(_lastAcceptedFinalResult, normalized);
                if (ContainsIgnoredPhrase(normalized))
                {
                    LogDroppedRecognition(normalized, isIntermediate, "ignored-exact-text-contained");
                    return;
                }
            }
        }
SkipFinalConcatenate:

        if (!isIntermediate
            && collapseRepeatedFinalText)
        {
            normalized = CollapseConsecutiveDuplicateSentences(normalized);
        }

        if (!isIntermediate
            && collapseRepeatedFinalText
            && TryCollapseExactRepeatedText(normalized, out string collapsedText, out int repeats)
            && repeats >= Mathf.Max(2, repeatedFinalMinRepeats))
        {
            normalized = collapsedText;
        }

        if (isIntermediate && dropDuplicateIntermediateResults && string.Equals(_lastIntermediateResult, normalized, StringComparison.Ordinal))
        {
            LogDroppedRecognition(normalized, isIntermediate, "duplicate-intermediate");
            return;
        }

        if (!ShouldAcceptRecognizedText(normalized, isIntermediate, out string rejectReason))
        {
            LogDroppedRecognition(normalized, isIntermediate, rejectReason);
            if (!isIntermediate && IsForbiddenRejectReason(rejectReason))
            {
                HandleForbiddenFinalRejected(normalized, rejectReason);
            }
            return;
        }

        int maxQueue = Mathf.Max(1, maxQueuedRecognitionResults);
        if (_queuedResultCount >= maxQueue)
        {
            LogDroppedRecognition(normalized, isIntermediate, $"queue-overflow({maxQueue})");
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
                _lastAcceptedFinalResult = normalized;
                _lastAcceptedFinalAt = Time.unscaledTime;
            }
        }
        catch (Exception ex)
        {
            LogExceptionWithSnapshot($"TryEnqueueRecognizedText(intermediate={isIntermediate})", ex);
        }
    }

    private bool TryCollapseExactRepeatedText(string text, out string collapsedText, out int repeats)
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

    private string CollapseConsecutiveDuplicateSentences(string text)
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

            string normalized = NormalizeSentenceForComparison(sentence);
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
            string normalizedTail = NormalizeSentenceForComparison(tailSentence);
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

    private string NormalizeSentenceForComparison(string sentence)
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

    private bool IsSentenceTerminator(char ch)
    {
        return ch == '。' || ch == '.' || ch == '．' || ch == '!' || ch == '！' || ch == '?' || ch == '？' || ch == '…';
    }

    private bool ShouldAcceptRecognizedText(string text, bool isIntermediate, out string rejectReason)
    {
        rejectReason = string.Empty;

        if (!enableNoiseFilter)
        {
            return true;
        }

        if (recognitionInputMode == RecognitionInputMode.PushToTalk && isIntermediate && !IsPushToTalkAcceptWindow())
        {
            rejectReason = "ptt-not-held";
            return false;
        }

        if (!isIntermediate
            && duplicateFinalSuppressSeconds > 0f
            && !string.IsNullOrEmpty(_lastAcceptedFinalResult)
            && string.Equals(_lastAcceptedFinalResult, text, StringComparison.Ordinal)
            && _lastAcceptedFinalAt > 0f)
        {
            float elapsedFromLastFinal = Time.unscaledTime - _lastAcceptedFinalAt;
            if (elapsedFromLastFinal < duplicateFinalSuppressSeconds)
            {
                rejectReason = $"duplicate-final({elapsedFromLastFinal:0.000}s<{duplicateFinalSuppressSeconds:0.000}s)";
                return false;
            }
        }

        if (!isIntermediate
            && extendedDuplicateFinalSuppressSeconds > 0f
            && !string.IsNullOrEmpty(_lastAcceptedFinalResult)
            && string.Equals(_lastAcceptedFinalResult, text, StringComparison.Ordinal)
            && _lastAcceptedFinalAt > 0f)
        {
            float elapsedFromLastFinal = Time.unscaledTime - _lastAcceptedFinalAt;
            if (elapsedFromLastFinal < extendedDuplicateFinalSuppressSeconds)
            {
                rejectReason = $"duplicate-final-extended({elapsedFromLastFinal:0.000}s<{extendedDuplicateFinalSuppressSeconds:0.000}s)";
                return false;
            }
        }

        if (isIntermediate && ignoreWhenVadSaysNoVoice && microphoneRecord != null && microphoneRecord.useVad && !microphoneRecord.IsVoiceDetected)
        {
            rejectReason = "vad-no-voice";
            return false;
        }

        int minLen = isIntermediate ? Mathf.Max(1, minIntermediateTextLength) : Mathf.Max(1, minFinalTextLength);
        if (text.Length < minLen)
        {
            rejectReason = $"too-short(len={text.Length},min={minLen})";
            return false;
        }

        if (IsIgnoredExactText(text))
        {
            rejectReason = "ignored-exact-text";
            return false;
        }

        if (ContainsIgnoredPhrase(text))
        {
            rejectReason = "ignored-exact-text-contained";
            return false;
        }

        if (minSecondsBetweenAcceptedResults > 0f && _lastAcceptedAt > 0f)
        {
            float elapsed = Time.unscaledTime - _lastAcceptedAt;
            if (elapsed < minSecondsBetweenAcceptedResults && string.Equals(_lastAcceptedResult, text, StringComparison.Ordinal))
            {
                rejectReason = $"duplicate-within-interval({elapsed:0.000}s<{minSecondsBetweenAcceptedResults:0.000}s)";
                return false;
            }
        }

        return true;
    }

    private bool IsIgnoredExactText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || ignoredExactTexts == null)
        {
            return false;
        }

        string normalized = NormalizeIgnoredCandidate(text);
        for (int i = 0; i < ignoredExactTexts.Length; i++)
        {
            string ignored = ignoredExactTexts[i];
            if (!string.IsNullOrWhiteSpace(ignored)
                && string.Equals(normalized, NormalizeIgnoredCandidate(ignored), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private bool ContainsIgnoredPhrase(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || ignoredExactTexts == null)
        {
            return false;
        }

        string normalizedText = NormalizeIgnoredCandidate(text);
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

            string normalizedIgnored = NormalizeIgnoredCandidate(ignored);
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

    private static string NormalizeIgnoredCandidate(string text)
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

    private static string ConcatenateFinalText(string previous, string current)
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

    private bool IsPushToTalkAcceptWindow()
    {
        if (_pushToTalkHeld)
        {
            return true;
        }

        if (pushToTalkReleaseGraceSeconds <= 0f || _lastPushToTalkReleasedAt < 0f)
        {
            return false;
        }

        return Time.unscaledTime - _lastPushToTalkReleasedAt <= pushToTalkReleaseGraceSeconds;
    }

    private void LogDroppedRecognition(string text, bool isIntermediate, string reason)
    {
        if (!logDroppedRecognitionReasons)
        {
            return;
        }

        float now = Time.unscaledTime;
        bool isSameAsLast = string.Equals(_lastDropLogReason, reason, StringComparison.Ordinal)
                            && string.Equals(_lastDropLogText, text, StringComparison.Ordinal)
                            && _lastDropLogIntermediate == isIntermediate;

        if (throttleRepeatedDropLogs && isSameAsLast && _lastDropLogAt > 0f)
        {
            float interval = Mathf.Max(0.5f, repeatedDropLogIntervalSeconds);
            if (now - _lastDropLogAt < interval)
            {
                _suppressedDropLogCount++;
                return;
            }
        }

        if (_suppressedDropLogCount > 0)
        {
            Log.Debug($"[SttWhisperTest][drop] suppressed={_suppressedDropLogCount}, reason={_lastDropLogReason}, intermediate={_lastDropLogIntermediate}, text='{_lastDropLogText}'");
            _suppressedDropLogCount = 0;
        }

        Log.Debug($"[SttWhisperTest][drop] reason={reason}, intermediate={isIntermediate}, text='{text}'");
        _lastDropLogReason = reason ?? string.Empty;
        _lastDropLogText = text ?? string.Empty;
        _lastDropLogIntermediate = isIntermediate;
        _lastDropLogAt = now;
    }

    private void ResetDropLogState()
    {
        _lastDropLogText = string.Empty;
        _lastDropLogReason = string.Empty;
        _lastDropLogIntermediate = false;
        _lastDropLogAt = -1f;
        _suppressedDropLogCount = 0;
    }

    private bool ShouldRotateStream()
    {
        if (!_running || _isSwitchingStream || _pendingRestartAfterStreamFinish)
        {
            return false;
        }

        if (disableAutoRotationInAlwaysListening && recognitionInputMode == RecognitionInputMode.AlwaysListening)
        {
            return false;
        }

        if (_stream == null || _streamStartedAt < 0f)
        {
            return false;
        }

        bool overTime = maxContinuousStreamSeconds > 0f && Time.unscaledTime - _streamStartedAt >= maxContinuousStreamSeconds;
        bool overSegments = maxSegmentsPerStream > 0 && _segmentFinishedCountInStream >= maxSegmentsPerStream;
        return overTime || overSegments;
    }

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
                _stream.OnResultUpdated -= HandleStreamResultUpdated;
                _stream.OnSegmentFinished -= HandleSegmentFinished;
                _stream.OnStreamFinished -= HandleStreamFinished;
                StopStreamSafely(_stream, "rotation");
                _stream = null;
            }

            var recreatedStream = await CreateStreamWithTimeoutAsync($"restart:{reason}");
            if (recreatedStream == null)
            {
                Log.Error("[SttWhisperTest] Failed to recreate whisper stream.");
                return;
            }

            AttachAndStartStream(recreatedStream);
            Log.Warning($"[SttWhisperTest] Whisper stream restarted. reason={reason}, maxContinuousStreamSeconds={maxContinuousStreamSeconds:0.0}");
        }
        catch (Exception ex)
        {
            Log.Error($"[SttWhisperTest] Failed to restart whisper stream: {ex.Message}");
        }
        finally
        {
            _isSwitchingStream = false;
        }
    }

    private void AttachAndStartStream(WhisperStream stream)
    {
        try
        {
            _stream = stream;
            _stream.OnResultUpdated += HandleStreamResultUpdated;
            _stream.OnSegmentFinished += HandleSegmentFinished;
            _stream.OnStreamFinished += HandleStreamFinished;
            _stream.StartStream();
            _isStreamStarted = true;
            _streamStartedAt = Time.unscaledTime;
            _segmentFinishedCountInStream = 0;

            if (logStreamLifecycleTransitions)
            {
                Log.Info($"[SttWhisperTest][trace] stream-started mode={recognitionInputMode}, pttHeld={_pushToTalkHeld}, queue={_queuedResultCount}");
            }
        }
        catch (Exception ex)
        {
            LogExceptionWithSnapshot("AttachAndStartStream", ex);
            throw;
        }
    }

    private async UniTask<WhisperStream> CreateStreamWithTimeoutAsync(string reason)
    {
        if (whisperManager == null || microphoneRecord == null)
        {
            return null;
        }

        int timeoutMs = Mathf.Max(1000, Mathf.RoundToInt(createStreamTimeoutSeconds * 1000f));
        var createTask = whisperManager.CreateStream(microphoneRecord);
        var createUniTask = createTask.AsUniTask();
        var (hasResultLeft, createdStream) = await UniTask.WhenAny(createUniTask, UniTask.Delay(timeoutMs));
        if (!hasResultLeft)
        {
            Log.Error($"[SttWhisperTest] CreateStream timeout. reason={reason}, timeoutMs={timeoutMs}");
            return null;
        }

        return createdStream;
    }

    private bool StopStreamSafely(WhisperStream stream, string reason)
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

            if (logStreamLifecycleTransitions)
            {
                Log.Info($"[SttWhisperTest][trace] stream-stopped reason={reason}, queue={_queuedResultCount}, pendingRestart={_pendingRestartAfterStreamFinish}");
            }

            return true;
        }
        catch (Exception ex)
        {
            _isStreamStarted = false;
            LogExceptionWithSnapshot($"StopStreamSafely(reason={reason})", ex);
            return false;
        }
    }

    private void LogExceptionWithSnapshot(string scope, Exception ex)
    {
        if (ex == null)
        {
            return;
        }

        if (!enableCrashInvestigationLogs)
        {
            Log.Error($"[SttWhisperTest] {scope} exception: {ex.Message}");
            return;
        }

        string snapshot = BuildRuntimeSnapshot(scope);
        Log.Error($"[SttWhisperTest][crash-probe] scope={scope}, exception={ex}\n{snapshot}");
    }

    private string BuildRuntimeSnapshot(string scope)
    {
        bool micRecording = microphoneRecord != null && microphoneRecord.IsRecording;
        bool micVoiceDetected = microphoneRecord != null && microphoneRecord.IsVoiceDetected;
        bool whisperLoaded = whisperManager != null && whisperManager.IsLoaded;
        bool whisperLoading = whisperManager != null && whisperManager.IsLoading;

        return "[SttWhisperTest][snapshot]"
               + $" scope={scope}"
               + $", running={_running}"
               + $", isStarting={_isStarting}"
               + $", isStopping={_isStopping}"
               + $", isSwitchingStream={_isSwitchingStream}"
               + $", isStreamStarted={_isStreamStarted}"
               + $", streamNull={_stream == null}"
               + $", pendingRestart={_pendingRestartAfterStreamFinish}"
               + $", recognitionMode={recognitionInputMode}"
               + $", pttHeld={_pushToTalkHeld}"
               + $", micRecording={micRecording}"
               + $", micVoiceDetected={micVoiceDetected}"
               + $", whisperLoaded={whisperLoaded}"
               + $", whisperLoading={whisperLoading}"
               + $", queueCount={_queuedResultCount}"
               + $", recognizedCount={_recognizedCount}"
               + $", segmentCount={_segmentFinishedCountInStream}"
               + $", lastRecognizedAt={_lastRecognizedAt:0.000}"
               + $", lastAcceptedAt={_lastAcceptedAt:0.000}"
               + $", lastFinalAcceptedAt={_lastAcceptedFinalAt:0.000}";
    }

    private void HandleForbiddenFinalRejected(string text, string reason)
    {
        _lastIntermediateResult = string.Empty;
        _lastAcceptedResult = string.Empty;
        _lastAcceptedFinalResult = string.Empty;
        _lastAcceptedFinalAt = -1f;

        if (!restartStreamOnIgnoredExactText)
        {
            return;
        }

        if (!_running || _isSwitchingStream || _pendingRestartAfterStreamFinish || _stream == null)
        {
            return;
        }

        RestartStreamAsync($"{reason}:{text}").Forget();
    }

    private bool IsForbiddenRejectReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        return reason.StartsWith("ignored-exact-text", StringComparison.Ordinal);
    }

    private void HandleRecognizedText(string text, bool isIntermediate)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _recognizedCount++;
        _lastRecognizedAt = Time.unscaledTime;
        _elapsedSinceStart = 0f;

        ShowRecognizedText(text);

        Log.Info($"[SttWhisperTest] Recognized({_recognizedCount}, intermediate={isIntermediate}): {text}");

        if (!isIntermediate)
        {
            ForwardFinalRecognizedTextToCharacterProcess(text);
        }
    }

    private void ForwardFinalRecognizedTextToCharacterProcess(string recognizedText)
    {
        if (!forwardFinalRecognizedTextToCharacterProcess)
        {
            return;
        }

        if (forwardOnlyFromExternalUiProcess && !_isExternalUiProcess)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(recognizedText))
        {
            return;
        }

        if (characterProcessUdpPort <= 0)
        {
            Log.Warning($"[SttWhisperTest] Character process forwarding skipped: invalid UDP port ({characterProcessUdpPort}).");
            return;
        }

        try
        {
            var payload = new CharacterProcessSttForwardMessage
            {
                text = recognizedText,
                source = nameof(SttWhisperTestComponent),
                timestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };

            string json = JsonUtility.ToJson(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using var client = new UdpClient();
            client.Send(bytes, bytes.Length, characterProcessUdpHost, characterProcessUdpPort);

            if (logCharacterProcessForwarding)
            {
                Log.Info($"[SttWhisperTest] Forwarded final recognized text to character process. host={characterProcessUdpHost}, port={characterProcessUdpPort}, text='{recognizedText}'");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[SttWhisperTest] Failed to forward recognized text to character process: {ex.Message}");
        }
    }

    [Serializable]
    private struct CharacterProcessSttForwardMessage
    {
        public string text;
        public string source;
        public long timestampUnixMs;
    }

    private void UpdateMicrophoneUi()
    {
        if (sttMicrophoneIcon == null && sttMicrophoneIconObject != null)
        {
            sttMicrophoneIconObject.SetActive(false);
            return;
        }

        bool isInitialized = whisperManager != null && whisperManager.IsLoaded;
        bool isRecording = microphoneRecord != null && microphoneRecord.IsRecording;
        bool isBlockedByLlm = ShouldBlockSttInput();
        bool isInputActive = recognitionInputMode == RecognitionInputMode.AlwaysListening || _pushToTalkHeld;
        bool shouldShowIconObject = isInitialized && _running && (isInputActive || isBlockedByLlm);
        bool canRecognize = isInitialized
                            && _running
                    && isInputActive
                            && !isBlockedByLlm
                            && isRecording;

        if (sttMicrophoneIconObject != null)
        {
            sttMicrophoneIconObject.SetActive(shouldShowIconObject);
        }
        else if (sttMicrophoneIcon != null)
        {
            sttMicrophoneIcon.gameObject.SetActive(shouldShowIconObject);
        }

        if (sttMicrophoneIcon == null)
        {
            return;
        }

        sttMicrophoneIcon.SwitchIcon(canRecognize);
    }

    private void ShowRecognizedText(string text)
    {
        if (sttRecognizedText == null)
        {
            return;
        }

        sttRecognizedText.text = text ?? string.Empty;
        GameObject visibilityObject = ResolveRecognizedVisibilityObject();
        if (visibilityObject != null && !visibilityObject.activeSelf)
        {
            visibilityObject.SetActive(true);
        }

        ScheduleRecognizedTextHide();
    }

    private void ClearRecognizedText()
    {
        CancelRecognizedTextHide();

        if (sttRecognizedText == null)
        {
            return;
        }

        sttRecognizedText.text = string.Empty;
        GameObject visibilityObject = ResolveRecognizedVisibilityObject();
        if (visibilityObject != null && visibilityObject.activeSelf)
        {
            visibilityObject.SetActive(false);
        }
    }

    private GameObject ResolveRecognizedVisibilityObject()
    {
        if (sttRecognizedVisibilityObject != null)
        {
            return sttRecognizedVisibilityObject;
        }

        return sttRecognizedText != null ? sttRecognizedText.gameObject : null;
    }

    private void ScheduleRecognizedTextHide()
    {
        CancelRecognizedTextHide();

        if (recognizedTextHideDelaySeconds <= 0f)
        {
            return;
        }

        _recognizedTextHideCoroutine = StartCoroutine(HideRecognizedTextAfterDelay(recognizedTextHideDelaySeconds));
    }

    private void CancelRecognizedTextHide()
    {
        if (_recognizedTextHideCoroutine == null)
        {
            return;
        }

        StopCoroutine(_recognizedTextHideCoroutine);
        _recognizedTextHideCoroutine = null;
    }

    private System.Collections.IEnumerator HideRecognizedTextAfterDelay(float delaySeconds)
    {
        yield return new WaitForSeconds(delaySeconds);
        _recognizedTextHideCoroutine = null;
        ClearRecognizedText();
    }

    private bool ShouldBlockSttInput()
    {
        return false;
    }

    private static bool IsExternalUiProcess()
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
                if (string.Equals(args[i], ExternalUiProcessRoleArgument, StringComparison.OrdinalIgnoreCase))
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

    private void LogStartupMicrophoneDevice()
    {
        if (microphoneRecord == null)
        {
            return;
        }

        string selected = microphoneRecord.SelectedMicDevice;
        string display = string.IsNullOrWhiteSpace(selected) ? "default" : selected;
        Log.Info($"[SttWhisperTest] Startup microphone device: {display}, frequency={microphoneRecord.frequency}");
    }

    private void ApplyMicrophoneSensitivitySettings()
    {
        if (!overrideMicrophoneVadSettings || microphoneRecord == null)
        {
            return;
        }

        bool shouldDisableVadForAlwaysListening = disableVadInAlwaysListening && recognitionInputMode == RecognitionInputMode.AlwaysListening;
        microphoneRecord.useVad = useVadForMicrophone && !shouldDisableVadForAlwaysListening;
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
            Log.Warning("[SttWhisperTest] VAD disabled automatically for AlwaysListening mode (stability preference).");
        }

        Log.Info($"[SttWhisperTest] Applied VAD settings. mic.useVad={microphoneRecord.useVad}, whisper.useVad={whisperManager?.useVad}, vadThd={microphoneRecord.vadThd:0.###}, vadFreqThd={microphoneRecord.vadFreqThd:0.###}, vadLastSec={microphoneRecord.vadLastSec:0.###}, vadContextSec={microphoneRecord.vadContextSec:0.###}, vadUpdateRateSec={microphoneRecord.vadUpdateRateSec:0.###}");
    }

    private void ApplyWhisperStreamingStabilitySettings()
    {
        if (whisperManager == null || recognitionInputMode != RecognitionInputMode.AlwaysListening)
        {
            return;
        }

        bool changed = false;

        if (disablePromptUpdateInAlwaysListening && whisperManager.updatePrompt)
        {
            whisperManager.updatePrompt = false;
            changed = true;
        }

        float minStep = Mathf.Max(0.5f, minStepSecInAlwaysListening);
        if (whisperManager.stepSec < minStep)
        {
            whisperManager.stepSec = minStep;
            changed = true;
        }

        float minLength = Mathf.Max(whisperManager.stepSec + 1f, minLengthSecInAlwaysListening);
        if (whisperManager.lengthSec < minLength)
        {
            whisperManager.lengthSec = minLength;
            changed = true;
        }

        if (changed)
        {
            Log.Warning($"[SttWhisperTest] Applied AlwaysListening stability settings. updatePrompt={whisperManager.updatePrompt}, stepSec={whisperManager.stepSec:0.###}, lengthSec={whisperManager.lengthSec:0.###}");
        }
    }

    private async UniTask<bool> EnsureWhisperModelReady()
    {
        if (whisperManager == null)
        {
            return false;
        }

        if (!whisperManager.IsLoaded && !whisperManager.IsLoading)
        {
            await whisperManager.InitModel();
        }

        float timeout = Mathf.Max(1f, whisperModelLoadTimeoutSeconds);
        float elapsed = 0f;
        while (whisperManager.IsLoading && elapsed < timeout)
        {
            await UniTask.Delay(100);
            elapsed += 0.1f;
        }

        if (whisperManager.IsLoading)
        {
            Log.Error($"[SttWhisperTest] Whisper model load timeout. timeout={timeout:0.0}s");
            return false;
        }

        return whisperManager.IsLoaded;
    }
}
