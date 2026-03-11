using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Logging;
using UnityEngine;
using TMPro;

public class SttVoskTestComponent : MonoBehaviour
{
    public enum RecognitionInputMode
    {
        AlwaysListening = 0,
        PushToTalk = 1,
    }

    [Header("Dependencies")]
    [SerializeField] private VoskSpeechToText speechToText;
    [SerializeField] private VoiceProcessor voiceProcessor;
    [SerializeField] private TestLlmProviderComponent chatProvider;
    [SerializeField] private SwitchMicrophoneIcon sttMicrophoneIcon;
    [SerializeField] private GameObject sttMicrophoneIconObject;
    [SerializeField] private TMP_Text sttRecognizedText;
    [SerializeField] private GameObject sttRecognizedVisibilityObject;

    [Header("Run")]
    [SerializeField] private bool runOnStart = true;
    [SerializeField] private bool startMicOnStart = true;
    [SerializeField] private bool stopRecordingOnExit = true;
    [SerializeField] private RecognitionInputMode recognitionInputMode = RecognitionInputMode.AlwaysListening;
    [SerializeField] private KeyCode pushToTalkKey = KeyCode.Space;
    [SerializeField] private float pushToTalkReleaseGraceSeconds = 1.0f;

    [Header("Settings")]
    [SerializeField] private string modelPathOverride = "";
    [SerializeField] private float noResultWarningSeconds = 20f;
    [SerializeField] private float noResultWarningRepeatSeconds = 10f;
    [SerializeField] private float recognizedTextHideDelaySeconds = 4f;
    [SerializeField] private bool enableDiagnostics = true;

    [Header("LLM Forwarding")]
    [SerializeField] private bool forwardRecognizedTextToLlmBackend;
    [SerializeField] private bool cancelPreviousLlmRequest = true;
    [SerializeField] private bool logLlmReply = true;

    private float _elapsedSinceStart;
    private bool _running;
    private float _micInputLevel;
    private bool _pushToTalkHeld;
    private float _lastPushToTalkReleasedAt = -1f;
    private float _elapsedSinceLastNoResultWarning;
    private int _recognizedCount;
    private float _lastRecognizedAt = -1f;
    private bool? _lastShouldRecord;
    private bool? _lastActualRecording;
    private Coroutine _recognizedTextHideCoroutine;
    private CancellationTokenSource _llmRequestCts;

    [ContextMenu("Start STT Test")]
    public void StartSttTest()
    {
        if (!TryResolveDependencies())
        {
            return;
        }

        LogStartupMicrophoneDevice();

        speechToText.OnSpeechRecognized -= HandleSpeechRecognized;
        speechToText.OnSpeechRecognized += HandleSpeechRecognized;
        speechToText.OnStatusUpdated -= HandleStatusUpdated;
        speechToText.OnStatusUpdated += HandleStatusUpdated;
        voiceProcessor.OnFrameCaptured -= OnMicFrameCaptured;
        voiceProcessor.OnFrameCaptured += OnMicFrameCaptured;

        if (speechToText.VoiceProcessor == null)
        {
            speechToText.VoiceProcessor = voiceProcessor;
        }

        _elapsedSinceStart = 0f;
        _elapsedSinceLastNoResultWarning = 0f;
        _recognizedCount = 0;
        _lastRecognizedAt = -1f;
        _lastPushToTalkReleasedAt = -1f;
        _lastShouldRecord = null;
        _lastActualRecording = null;
        _running = true;
        ClearRecognizedText();

        bool startMic = startMicOnStart && recognitionInputMode == RecognitionInputMode.AlwaysListening;
        if (!speechToText.IsInitialized && !speechToText.IsInitializing)
        {
            if (string.IsNullOrWhiteSpace(modelPathOverride))
            {
                speechToText.StartVoskStt(startMic: startMic);
            }
            else
            {
                speechToText.StartVoskStt(modelPath: modelPathOverride, startMic: startMic);
            }

            Log.Info($"[SttVoskTest] STT initialize requested. modelOverride='{modelPathOverride}', startMic={startMic}");
            return;
        }

        if (startMic)
        {
            speechToText.ResumeRecording();
        }
        else if (speechToText.IsInitialized)
        {
            speechToText.PauseRecording();
        }

        Log.Info($"[SttVoskTest] STT test started. initialized={speechToText.IsInitialized}, startMic={startMic}");
    }

    [ContextMenu("Stop STT Test")]
    public void StopSttTest()
    {
        _running = false;
        _micInputLevel = 0f;
        _elapsedSinceStart = 0f;
        _elapsedSinceLastNoResultWarning = 0f;
        _recognizedCount = 0;
        _lastRecognizedAt = -1f;
        _lastPushToTalkReleasedAt = -1f;
        _lastShouldRecord = null;
        _lastActualRecording = null;

        if (speechToText != null)
        {
            speechToText.OnSpeechRecognized -= HandleSpeechRecognized;
            speechToText.OnStatusUpdated -= HandleStatusUpdated;

            if (speechToText.VoiceProcessor != null && speechToText.VoiceProcessor.IsRecording)
            {
                speechToText.PauseRecording();
            }
        }

        if (voiceProcessor != null)
        {
            voiceProcessor.OnFrameCaptured -= OnMicFrameCaptured;
        }

        UpdateMicrophoneUi();
        ClearRecognizedText();
        CancelPendingLlmRequest();

        Log.Info("[SttVoskTest] STT test stopped.");
    }

    private void Start()
    {
        if (!runOnStart)
        {
            return;
        }

        StartSttTest();
    }

    private void Update()
    {
        UpdateRecognitionRecordingState();
        UpdateMicrophoneUi();

        if (!_running)
        {
            return;
        }

        bool isInitialized = speechToText != null && speechToText.IsInitialized;
        bool isRecording = voiceProcessor != null && voiceProcessor.IsRecording;
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
            Log.Warning($"[SttVoskTest] No recognition result yet. elapsed={_elapsedSinceStart:0.0}s, micLevel={_micInputLevel:0.000}, initialized={isInitialized}, recording={isRecording}, mode={recognitionInputMode}, pttHeld={_pushToTalkHeld}, recognizedCount={_recognizedCount}, sinceLast={(sinceLast < 0f ? "N/A" : sinceLast.ToString("0.0") + "s")}. Check microphone permission/device and model path.");
        }
    }

    private void OnApplicationQuit()
    {
        if (stopRecordingOnExit)
        {
            StopSttTest();
        }
    }

    private void OnDestroy()
    {
        if (stopRecordingOnExit)
        {
            StopSttTest();
        }
    }

    private bool TryResolveDependencies()
    {
        if (speechToText == null)
        {
            speechToText = GetComponent<VoskSpeechToText>();
        }

        if (voiceProcessor == null)
        {
            voiceProcessor = GetComponent<VoiceProcessor>();
        }

        if (speechToText == null)
        {
            Log.Error("[SttVoskTest] VoskSpeechToText is missing.");
            return false;
        }

        if (voiceProcessor == null)
        {
            Log.Error("[SttVoskTest] VoiceProcessor is missing.");
            return false;
        }

        if (chatProvider == null)
        {
            chatProvider = GetComponent<TestLlmProviderComponent>();
            if (chatProvider == null)
            {
                chatProvider = FindFirstObjectByType<TestLlmProviderComponent>();
            }
        }

        return true;
    }

    private void OnMicFrameCaptured(short[] samples)
    {
        if (samples == null || samples.Length == 0)
        {
            _micInputLevel = 0f;
            return;
        }

        double sum = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float norm = samples[i] / (float)short.MaxValue;
            sum += norm * norm;
        }

        _micInputLevel = Mathf.Sqrt((float)(sum / samples.Length));
    }

    private void UpdateMicrophoneUi()
    {
        if (sttMicrophoneIcon == null && sttMicrophoneIconObject != null)
        {
            sttMicrophoneIconObject.SetActive(false);
            return;
        }

        bool isInitialized = speechToText != null && speechToText.IsInitialized;
        bool isRecording = voiceProcessor != null && voiceProcessor.IsRecording;
        bool isPushToTalkActive = IsPushToTalkActiveWindow();
        bool canRecognize = isInitialized
                            && _running
                    && (recognitionInputMode == RecognitionInputMode.AlwaysListening || isPushToTalkActive)
                            && isRecording;

        if (sttMicrophoneIconObject != null)
        {
            sttMicrophoneIconObject.SetActive(canRecognize);
        }
        else if (sttMicrophoneIcon != null)
        {
            sttMicrophoneIcon.gameObject.SetActive(canRecognize);
        }

        if (!canRecognize || sttMicrophoneIcon == null)
        {
            return;
        }

        sttMicrophoneIcon.SwitchIcon(true);
    }

    private void UpdateRecognitionRecordingState()
    {
        if (!_running || speechToText == null)
        {
            _pushToTalkHeld = false;
            _lastPushToTalkReleasedAt = -1f;
            return;
        }

        bool wasPushToTalkHeld = _pushToTalkHeld;
        _pushToTalkHeld = recognitionInputMode == RecognitionInputMode.PushToTalk && Input.GetKey(pushToTalkKey);

        if (recognitionInputMode != RecognitionInputMode.PushToTalk)
        {
            _lastPushToTalkReleasedAt = -1f;
        }
        else if (wasPushToTalkHeld && !_pushToTalkHeld)
        {
            _lastPushToTalkReleasedAt = Time.unscaledTime;
        }
        else if (_pushToTalkHeld)
        {
            _lastPushToTalkReleasedAt = -1f;
        }

        if (!speechToText.IsInitialized)
        {
            return;
        }

        bool shouldRecord = recognitionInputMode == RecognitionInputMode.AlwaysListening || IsPushToTalkActiveWindow();
        bool isRecording = voiceProcessor != null && voiceProcessor.IsRecording;

        if (enableDiagnostics)
        {
            if (_lastShouldRecord != shouldRecord || _lastActualRecording != isRecording)
            {
                Log.Info($"[SttVoskTest][diag] shouldRecord={shouldRecord}, isRecording={isRecording}, mode={recognitionInputMode}, pttHeld={_pushToTalkHeld}, initialized={speechToText.IsInitialized}");
                _lastShouldRecord = shouldRecord;
                _lastActualRecording = isRecording;
            }
        }

        if (shouldRecord && !isRecording)
        {
            speechToText.ResumeRecording();
        }
        else if (!shouldRecord && isRecording)
        {
            speechToText.PauseRecording();
        }
    }

    private bool IsPushToTalkActiveWindow()
    {
        if (recognitionInputMode != RecognitionInputMode.PushToTalk)
        {
            return false;
        }

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

    private void HandleSpeechRecognized(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _elapsedSinceStart = 0f;
        _recognizedCount++;
        _lastRecognizedAt = Time.unscaledTime;
        ShowRecognizedText(text);

        if (forwardRecognizedTextToLlmBackend)
        {
            ForwardRecognizedTextToLlmAsync(text).Forget();
        }

        Log.Info($"[SttVoskTest] Recognized({_recognizedCount}): {text}");
    }

    private void HandleStatusUpdated(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return;
        }

        Log.Info($"[SttVoskTest] Status: {status}");
    }

    private void ShowRecognizedText(string text)
    {
        if (sttRecognizedText == null)
        {
            return;
        }

        sttRecognizedText.text = text ?? string.Empty;
        var visibilityObject = ResolveRecognizedVisibilityObject();
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
        var visibilityObject = ResolveRecognizedVisibilityObject();
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

    private async UniTaskVoid ForwardRecognizedTextToLlmAsync(string recognizedText)
    {
        var provider = ResolveChatProvider();
        if (provider == null || !provider.IsAvailable)
        {
            Log.Warning("[SttVoskTest] LLM forwarding skipped: chat provider is not available.");
            return;
        }

        if (cancelPreviousLlmRequest)
        {
            CancelPendingLlmRequest();
        }
        else if (_llmRequestCts != null)
        {
            Log.Info("[SttVoskTest] LLM request is already running. New recognized text was not forwarded.");
            return;
        }

        _llmRequestCts = new CancellationTokenSource();
        var requestCts = _llmRequestCts;

        var replyBuilder = new StringBuilder();
        int lastReplyLength = 0;

        try
        {
            await provider.ChatAsync(
                recognizedText,
                cumulativeReply =>
                {
                    if (string.IsNullOrEmpty(cumulativeReply))
                    {
                        return;
                    }

                    if (cumulativeReply.Length < lastReplyLength)
                    {
                        lastReplyLength = 0;
                    }

                    var newText = cumulativeReply.Substring(lastReplyLength);
                    lastReplyLength = cumulativeReply.Length;
                    replyBuilder.Append(newText);
                },
                () => { },
                requestCts.Token
            );

            if (logLlmReply)
            {
                var aiReply = replyBuilder.ToString();
                Log.Info($"[SttVoskTest] LLM reply: {aiReply}");
            }
        }
        catch (OperationCanceledException)
        {
            Log.Debug("[SttVoskTest] LLM request canceled.");
        }
        catch (Exception ex)
        {
            Log.Error($"[SttVoskTest] Failed to forward recognized text to LLM: {ex.Message}");
        }
        finally
        {
            if (_llmRequestCts == requestCts)
            {
                requestCts.Dispose();
                _llmRequestCts = null;
            }
        }
    }

    private TestLlmProviderComponent ResolveChatProvider()
    {
        if (chatProvider != null && chatProvider.IsAvailable)
        {
            return chatProvider;
        }

        return null;
    }

    private void CancelPendingLlmRequest()
    {
        if (_llmRequestCts == null)
        {
            return;
        }

        _llmRequestCts.Cancel();
        _llmRequestCts.Dispose();
        _llmRequestCts = null;
    }

    private void LogStartupMicrophoneDevice()
    {
        var devices = Microphone.devices;
        if (devices == null || devices.Length == 0)
        {
            Log.Warning("[SttVoskTest] No microphone devices found at startup.");
            return;
        }

        int selectedIndex = voiceProcessor != null ? voiceProcessor.SelectedDeviceIndex : 0;
        int resolvedIndex = (selectedIndex >= 0 && selectedIndex < devices.Length) ? selectedIndex : 0;
        var deviceName = devices[resolvedIndex];

        Log.Info($"[SttVoskTest] Startup microphone device: index={resolvedIndex}, name='{deviceName}' (selectedIndex={selectedIndex}, total={devices.Length})");
    }
}
