using System;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Threading;
using System.Net.Sockets;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Logging;
using UnityEngine;
using UnityEngine.Networking;

public class LLMProvider : MonoBehaviour, IChatProvider
{
    [Serializable]
    private class ChatRequest
    {
        public string message;
    }

    [Serializable]
    private class ChatResponse
    {
        public string message;
    }

    [Serializable]
    private struct SttForwardMessage
    {
        public string text;
        public string source;
        public long timestampUnixMs;
    }

    private string endpointUrl = $"http://{Constant.BackendHost}:{Constant.BackendPort}/chat";
    private int timeoutSec = 60;

    private float startupWaitSec = 2.0f;
    private int pollIntervalMs = 100;

    [Header("Bridge Dependencies")]
    [SerializeField] private CharacterSpeechBubbleUI bubbleUI;
    [SerializeField] private float bubbleHideDelaySec = 8f;
    [SerializeField] private int maxQueuedMessages = 32;

    private readonly ConcurrentQueue<string> _recognizedQueue = new();
    private UdpClient _udpClient;
    private CancellationTokenSource _listenCts;
    private Task _listenTask;
    private CancellationTokenSource _requestCts;
    private bool _isRunning;
    private bool _isProcessing;
    private bool _isUiProcess;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(endpointUrl);
    public string ProviderName => "LlmProvider";

    public void PrewarmBackend()
    {
        ApplySettings();
        TryStartBackend();
    }

    private void Awake()
    {
        _isUiProcess = IsUiProcess();

        ResolveBridge();
        ApplySettings();
    }

    private void Start()
    {
        StartBridge();
    }

    private void Update()
    {
        if (!_isRunning || _isProcessing)
        {
            return;
        }

        if (!_recognizedQueue.TryDequeue(out string recognizedText))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(recognizedText))
        {
            return;
        }

        Log.Info($"[LLMProvider][TextFlow] Character process dequeued STT text: '{recognizedText}'");

        ProcessRecognizedAsync(recognizedText).Forget();
    }

    private void OnDestroy()
    {
        StopBridge();
    }

    [ContextMenu("Start Character LLM Bridge")]
    public void StartBridge()
    {
        if (_isRunning)
        {
            return;
        }

        if (_isUiProcess)
        {
            Log.Info("[LLMProvider] Bridge start skipped in external-ui process.");
            return;
        }

        ResolveBridge();
        PrewarmBackend();

        try
        {
            IPAddress address = ParseListenAddress(Constant.BackendHost);
            var endpoint = new IPEndPoint(address, ResolveBackendPort());
            _udpClient = new UdpClient(endpoint);
            _udpClient.Client.ReceiveTimeout = 200;

            _listenCts = new CancellationTokenSource();
            _listenTask = Task.Run(() => ReceiveLoop(_listenCts.Token));
            _isRunning = true;
        }
        catch (Exception ex)
        {
            Log.Error($"[LLMProvider] Failed to start bridge receiver: {ex.Message}");
            StopBridge();
        }
    }

    [ContextMenu("Stop Character LLM Bridge")]
    public void StopBridge()
    {
        _isRunning = false;

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
        CancelBridgeRequest();

        while (_recognizedQueue.TryDequeue(out _))
        {
        }
    }

    public async UniTask ChatAsync(
        string userMessage,
        Action<string> onStream,
        Action onComplete,
        CancellationToken cancellationToken = default)
    {
        double chatStart = Time.realtimeSinceStartupAsDouble;
        ApplySettings();

        if (!IsAvailable)
        {
            Log.Error("[LLMProvider] Endpoint URL is not configured.");
            onComplete?.Invoke();
            return;
        }

        TryStartBackend();
        await WaitForReady(cancellationToken);

        double requestStart = Time.realtimeSinceStartupAsDouble;

        var payload = new ChatRequest
        {
            message = userMessage ?? string.Empty,
        };

        string json = JsonUtility.ToJson(payload);
        using var request = new UnityWebRequest(endpointUrl, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = Mathf.Max(1, timeoutSec);

        try
        {
            await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);
        } catch (OperationCanceledException)
        {
            onComplete?.Invoke();
            throw;
        } catch (Exception ex)
        {
            Log.Error($"[LLMProvider] Request failed: {ex.Message} (Endpoint: {endpointUrl})");
            onComplete?.Invoke();
            return;
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            string responseBody = request.downloadHandler?.text ?? string.Empty;
            Log.Error($"[LLMProvider] HTTP error: {request.responseCode} {request.error} (Endpoint: {endpointUrl}) Response: {responseBody}");
            onComplete?.Invoke();
            return;
        }

        string responseText = request.downloadHandler?.text ?? string.Empty;
        string reply = TryExtractReply(responseText);
        if (string.IsNullOrWhiteSpace(reply))
        {
            reply = responseText;
        }

        onStream?.Invoke(reply);
        onComplete?.Invoke();

        double totalMs = (Time.realtimeSinceStartupAsDouble - chatStart) * 1000.0;
        double httpMs = (Time.realtimeSinceStartupAsDouble - requestStart) * 1000.0;
        Log.Info($"[LLMProvider] Chat completed. totalMs={totalMs:0}, httpMs={httpMs:0}, endpoint={endpointUrl}");
    }

    private string TryExtractReply(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return string.Empty;
        }

        try
        {
            var parsed = JsonUtility.FromJson<ChatResponse>(responseText);
            if (parsed != null && !string.IsNullOrWhiteSpace(parsed.message))
            {
                return parsed.message;
            }
        } catch (Exception ex)
        {
            Log.Warning($"[LLMProvider] Response parse failed: {ex.Message}");
        }

        return string.Empty;
    }

    private void TryStartBackend()
    {
        var backendManager = BackendManager.Instance;
        if (backendManager == null)
        {
            Log.Warning("[LLMProvider] BackendManager is not available. Skipped backend startup request.");
            return;
        }

        if (backendManager.IsBackendAlive())
        {
            return;
        }

        backendManager.StartBackend();
    }

    private async UniTask WaitForReady(CancellationToken cancellationToken)
    {
        var backendManager = BackendManager.Instance;
        if (backendManager == null)
        {
            Log.Warning("[LLMProvider] BackendManager is not available. Skip readiness wait.");
            return;
        }

        if (backendManager.IsBackendAlive())
        {
            return;
        }

        int maxWaitMs = Mathf.Max(0, Mathf.RoundToInt(startupWaitSec * 1000f));
        if (maxWaitMs <= 0)
        {
            return;
        }

        int pollMs = Mathf.Max(50, pollIntervalMs);
        int elapsed = 0;
        while (elapsed < maxWaitMs)
        {
            if (backendManager.IsBackendAlive())
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await UniTask.Delay(pollMs, cancellationToken: cancellationToken);
            elapsed += pollMs;
        }

        if (!backendManager.IsBackendAlive())
        {
            Log.Warning($"[LLMProvider] Backend readiness wait timed out. waitedMs={maxWaitMs}");
        }
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

                string recognizedText = TryExtractRecognized(payload);
                if (string.IsNullOrWhiteSpace(recognizedText))
                {
                    continue;
                }

                Log.Info($"[LLMProvider][TextFlow] Character process received STT text. remote={remote?.Address}:{remote?.Port}, text='{recognizedText}'");

                if (_recognizedQueue.Count >= Mathf.Max(1, maxQueuedMessages))
                {
                    Log.Warning($"[LLMProvider] Bridge queue full. Dropped recognized text: '{recognizedText}'");
                    continue;
                }

                _recognizedQueue.Enqueue(recognizedText);
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    continue;
                }

                if (!token.IsCancellationRequested)
                {
                    Log.Warning($"[LLMProvider] Bridge receive loop socket warning: {ex.Message}");
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
                    Log.Error($"[LLMProvider] Bridge receive loop error: {ex.Message}");
                }
            }
        }
    }

    private async UniTaskVoid ProcessRecognizedAsync(string recognizedText)
    {
        _isProcessing = true;

        if (!IsAvailable)
        {
            Log.Warning("[LLMProvider] Bridge skipped: provider unavailable.");
            _isProcessing = false;
            return;
        }

        CancelBridgeRequest();
        _requestCts = new CancellationTokenSource();
        var requestCts = _requestCts;

        var replyBuilder = new StringBuilder();
        int lastReplyLength = 0;

        try
        {
            await ChatAsync(
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
            Log.Info($"[LLMProvider] Bridge LLM reply: {aiReply}");

            if (string.IsNullOrWhiteSpace(aiReply))
            {
                Log.Warning("[LLMProvider] Bridge LLM reply was empty.");
            }
            else if (!_isUiProcess)
            {
                if (bubbleUI == null)
                {
                    bubbleUI = FindFirstObjectByType<CharacterSpeechBubbleUI>();
                }

                if (bubbleUI != null)
                {
                    bubbleUI.HideThinkingBubble();
                    bubbleUI.ShowTtsBubble(aiReply, bubbleHideDelaySec);
                }
                else
                {
                    Log.Warning("[LLMProvider] Bridge reply bubble display skipped: CharacterSpeechBubbleUI is not found.");
                }
            }

            if (!string.IsNullOrWhiteSpace(aiReply))
            {
                var ttsManager = TTSManager.Instance;
                if (ttsManager == null)
                {
                    Log.Warning("[LLMProvider] Bridge LLM->TTS forwarding skipped: TTSManager is not found.");
                }
                else
                {
                    ttsManager.PlayTts(aiReply);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log.Debug("[LLMProvider] Bridge LLM request canceled.");
        }
        catch (Exception ex)
        {
            Log.Error($"[LLMProvider] Bridge failed to process text via LLM: {ex.Message}");
        }
        finally
        {
            if (_requestCts == requestCts)
            {
                requestCts.Dispose();
                _requestCts = null;
            }

            _isProcessing = false;
        }
    }

    private static string TryExtractRecognized(string payload)
    {
        try
        {
            var message = JsonUtility.FromJson<SttForwardMessage>(payload);
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

    private void ResolveBridge()
    {
        if (bubbleUI == null)
        {
            bubbleUI = GetComponent<CharacterSpeechBubbleUI>();
            if (bubbleUI == null)
            {
                bubbleUI = FindFirstObjectByType<CharacterSpeechBubbleUI>();
            }
        }
    }

    private int ResolveBackendPort()
    {
        try
        {
            var provider = SettingsProvider.Instance;
            if (provider?.Backend != null && provider.Backend.Port > 0)
            {
                return provider.Backend.Port;
            }
        }
        catch
        {
        }

        return Constant.BackendPort;
    }

    private void ApplySettings()
    {
        endpointUrl = $"http://{Constant.BackendHost}:{ResolveBackendPort()}/chat";

        try
        {
            var provider = SettingsProvider.Instance;
            if (provider?.Backend == null)
            {
                return;
            }

            var backend = provider.Backend;
            startupWaitSec = backend.StartupTimeoutSeconds;
            pollIntervalMs = Mathf.Max(50, Mathf.RoundToInt(backend.PollIntervalSeconds * 1000f));
        }
        catch (Exception ex)
        {
            Log.Warning($"[LLMProvider] Failed to load settings from SettingsProvider: {ex.Message}");
        }
    }

    private void CancelBridgeRequest()
    {
        if (_requestCts == null)
        {
            return;
        }

        _requestCts.Cancel();
        _requestCts.Dispose();
        _requestCts = null;
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

}
