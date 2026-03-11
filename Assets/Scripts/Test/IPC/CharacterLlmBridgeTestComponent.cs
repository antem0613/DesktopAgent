using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Unity.Logging;
using UnityEngine;

[RequireComponent(typeof(TestLlmProviderComponent), typeof(JsonSettingsFileTestComponent))]
public class CharacterLlmBridgeTestComponent : MonoBehaviour
{
    private const string ExternalUiProcessRoleArgument = "--external-ui-process";

    [Serializable]
    private struct CharacterProcessSttForwardMessage
    {
        public string text;
        public string source;
        public long timestampUnixMs;
    }

    [Header("Dependencies")]
    [SerializeField] private TestLlmProviderComponent chatProvider;
    [SerializeField] private TtsCoeiroinkTestComponent ttsTestComponent;
    [SerializeField] private CharacterSpeechBubbleUI characterSpeechBubbleUI;
    private JsonSettingsFileTestComponent jsonSettingsSource;

    [Header("Run")]
    [SerializeField] private bool runOnStart = true;
    [SerializeField] private bool runOnlyInCharacterProcess = true;

    private string listenHost = "127.0.0.1";
    private int listenPort = 27651;
    private int maxQueuedMessages = 32;

    [Header("LLM")]
    [SerializeField] private bool logReceivedText = true;
    [SerializeField] private bool speakReplyWithTts = true;
    [SerializeField] private bool showLlmReplyInBubble = true;
    [SerializeField] private float llmReplyBubbleHideDelaySeconds = 8f;

    private readonly ConcurrentQueue<string> _recognizedTextQueue = new();
    private UdpClient _udpClient;
    private CancellationTokenSource _listenCts;
    private Task _listenTask;
    private CancellationTokenSource _llmRequestCts;
    private bool _bridgeRunning;
    private bool _isProcessing;
    private bool _isExternalUiProcess;

    [ContextMenu("Start Character LLM Bridge Test")]
    public void StartBridge()
    {
        if (_bridgeRunning)
        {
            return;
        }

        if (runOnlyInCharacterProcess && _isExternalUiProcess)
        {
            Log.Info("[CharacterLlmBridgeTest] Start skipped in external-ui process.");
            return;
        }

        if (listenPort <= 0)
        {
            Log.Error($"[CharacterLlmBridgeTest] Invalid listen port: {listenPort}");
            return;
        }

        ResolveDependencies();
        chatProvider?.PrewarmBackendIfConfigured();

        try
        {
            IPAddress address = ParseListenAddress(listenHost);
            var endpoint = new IPEndPoint(address, listenPort);
            _udpClient = new UdpClient(endpoint);
            _udpClient.Client.ReceiveTimeout = 200;

            _listenCts = new CancellationTokenSource();
            _listenTask = Task.Run(() => ReceiveLoop(_listenCts.Token));
            _bridgeRunning = true;

            Log.Info($"[CharacterLlmBridgeTest] Started. listen={address}:{listenPort}, runOnlyInCharacterProcess={runOnlyInCharacterProcess}, isExternalUiProcess={_isExternalUiProcess}");
            Log.Info("[CharacterLlmBridgeTest] Character-process LLM reply logging is enabled.");
            Log.Info($"[CharacterLlmBridgeTest] LLM->TTS forwarding enabled={speakReplyWithTts}");
        }
        catch (Exception ex)
        {
            Log.Error($"[CharacterLlmBridgeTest] Failed to start receiver: {ex.Message}");
            StopBridge();
        }
    }

    [ContextMenu("Stop Character LLM Bridge Test")]
    public void StopBridge()
    {
        _bridgeRunning = false;

        if (_listenCts != null)
        {
            _listenCts.Cancel();
            _listenCts.Dispose();
            _listenCts = null;
        }

        if (_udpClient != null)
        {
            _udpClient.Close();
            _udpClient.Dispose();
            _udpClient = null;
        }

        _listenTask = null;
        CancelPendingLlmRequest();

        while (_recognizedTextQueue.TryDequeue(out _)) { }

        Log.Info("[CharacterLlmBridgeTest] Stopped.");
    }

    private void Awake()
    {
        _isExternalUiProcess = IsExternalUiProcess();
        ResolveDependencies();
        ApplyUdpSettingsFromJsonSource();
    }

    private void Start()
    {
        if (!runOnStart)
        {
            return;
        }

        StartBridge();
    }

    private void Update()
    {
        if (!_bridgeRunning || _isProcessing)
        {
            return;
        }

        if (!_recognizedTextQueue.TryDequeue(out string recognizedText))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(recognizedText))
        {
            return;
        }

        ProcessRecognizedTextAsync(recognizedText).Forget();
    }

    private void OnDestroy()
    {
        StopBridge();
    }

    private void ReceiveLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                IPEndPoint remote = null;
                byte[] bytes = _udpClient.Receive(ref remote);
                if (bytes == null || bytes.Length == 0)
                {
                    continue;
                }

                string payload = Encoding.UTF8.GetString(bytes);
                if (string.IsNullOrWhiteSpace(payload))
                {
                    continue;
                }

                string recognizedText = TryExtractRecognizedText(payload);
                if (string.IsNullOrWhiteSpace(recognizedText))
                {
                    continue;
                }

                if (_recognizedTextQueue.Count >= Mathf.Max(1, maxQueuedMessages))
                {
                    Log.Warning($"[CharacterLlmBridgeTest] Queue full. Dropped recognized text: '{recognizedText}'");
                    continue;
                }

                _recognizedTextQueue.Enqueue(recognizedText);
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    continue;
                }

                if (!token.IsCancellationRequested)
                {
                    Log.Warning($"[CharacterLlmBridgeTest] Receive loop socket warning: {ex.Message}");
                }
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    Log.Error($"[CharacterLlmBridgeTest] Receive loop error: {ex.Message}");
                }
            }
        }
    }

    private async UniTaskVoid ProcessRecognizedTextAsync(string recognizedText)
    {
        _isProcessing = true;

        var provider = ResolveChatProvider();
        if (provider == null || !provider.IsAvailable)
        {
            Log.Warning("[CharacterLlmBridgeTest] LLM provider unavailable. Incoming text was not processed.");
            _isProcessing = false;
            return;
        }

        if (logReceivedText)
        {
            Log.Info($"[CharacterLlmBridgeTest] STT final received: {recognizedText}");
        }

        CancelPendingLlmRequest();
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

                    string newText = cumulativeReply.Substring(lastReplyLength);
                    lastReplyLength = cumulativeReply.Length;
                    replyBuilder.Append(newText);
                },
                () => { },
                requestCts.Token
            );

            string aiReply = replyBuilder.ToString();
            Log.Info($"[CharacterLlmBridgeTest] LLM reply: {aiReply}");

            if (string.IsNullOrWhiteSpace(aiReply))
            {
                Log.Warning("[CharacterLlmBridgeTest] LLM reply was empty.");
            }
            else if (showLlmReplyInBubble && !_isExternalUiProcess)
            {
                if (characterSpeechBubbleUI == null)
                {
                    characterSpeechBubbleUI = FindFirstObjectByType<CharacterSpeechBubbleUI>();
                }

                if (characterSpeechBubbleUI != null)
                {
                    characterSpeechBubbleUI.HideThinkingBubble();
                    characterSpeechBubbleUI.ShowTtsBubble(aiReply, llmReplyBubbleHideDelaySeconds);
                    Log.Info("[CharacterLlmBridgeTest] LLM reply displayed in speech bubble.");
                }
                else
                {
                    Log.Warning("[CharacterLlmBridgeTest] LLM reply bubble display skipped: CharacterSpeechBubbleUI is not found.");
                }
            }

            if (speakReplyWithTts && !string.IsNullOrWhiteSpace(aiReply))
            {
                if (ttsTestComponent == null)
                {
                    ttsTestComponent = FindFirstObjectByType<TtsCoeiroinkTestComponent>();
                }

                if (ttsTestComponent == null)
                {
                    Log.Warning("[CharacterLlmBridgeTest] LLM->TTS forwarding skipped: TtsCoeiroinkTestComponent is not found.");
                }
                else
                {
                    ttsTestComponent.PlayTts(aiReply);
                    Log.Info("[CharacterLlmBridgeTest] LLM reply forwarded to TTS component.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log.Debug("[CharacterLlmBridgeTest] LLM request canceled.");
        }
        catch (Exception ex)
        {
            Log.Error($"[CharacterLlmBridgeTest] Failed to process incoming text via LLM: {ex.Message}");
        }
        finally
        {
            if (_llmRequestCts == requestCts)
            {
                requestCts.Dispose();
                _llmRequestCts = null;
            }

            _isProcessing = false;
        }
    }

    private static string TryExtractRecognizedText(string payload)
    {
        try
        {
            var message = JsonUtility.FromJson<CharacterProcessSttForwardMessage>(payload);
            if (!string.IsNullOrWhiteSpace(message.text))
            {
                return message.text.Trim();
            }
        }
        catch
        {
        }

        return payload.Trim();
    }

    private void ResolveDependencies()
    {
        if (chatProvider == null)
        {
            chatProvider = GetComponent<TestLlmProviderComponent>();
        }

        if (chatProvider == null)
        {
            Log.Error("[CharacterLlmBridgeTest] TestLlmProviderComponent is required on the same GameObject.");
        }

        if (ttsTestComponent == null)
        {
            ttsTestComponent = GetComponent<TtsCoeiroinkTestComponent>();
            if (ttsTestComponent == null)
            {
                ttsTestComponent = FindFirstObjectByType<TtsCoeiroinkTestComponent>();
            }
        }

        if (characterSpeechBubbleUI == null)
        {
            characterSpeechBubbleUI = GetComponent<CharacterSpeechBubbleUI>();
            if (characterSpeechBubbleUI == null)
            {
                characterSpeechBubbleUI = FindFirstObjectByType<CharacterSpeechBubbleUI>();
            }
        }

        if (jsonSettingsSource == null)
        {
            jsonSettingsSource = GetComponent<JsonSettingsFileTestComponent>();
        }

        if (jsonSettingsSource == null)
        {
            Log.Error("[CharacterLlmBridgeTest] JsonSettingsFileTestComponent is required on the same GameObject.");
        }
    }

    private void ApplyUdpSettingsFromJsonSource()
    {
        if (jsonSettingsSource == null)
        {
            return;
        }

        if (!jsonSettingsSource.TryGetUdpBridgeSettings(out var settings) || settings == null)
        {
            return;
        }

        listenHost = string.IsNullOrWhiteSpace(settings.listenHost) ? listenHost : settings.listenHost;
        listenPort = settings.listenPort > 0 ? settings.listenPort : listenPort;
        maxQueuedMessages = settings.maxQueuedMessages > 0 ? settings.maxQueuedMessages : maxQueuedMessages;
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

    private static IPAddress ParseListenAddress(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return IPAddress.Loopback;
        }

        if (IPAddress.TryParse(host, out IPAddress address))
        {
            return address;
        }

        return IPAddress.Loopback;
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
}
