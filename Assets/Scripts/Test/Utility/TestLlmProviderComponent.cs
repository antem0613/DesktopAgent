using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Logging;
using UnityEngine;
using UnityEngine.Networking;

[RequireComponent(typeof(JsonSettingsFileTestComponent), typeof(BackendServerStartupTestComponent))]
public class TestLlmProviderComponent : MonoBehaviour
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

    private string endpointUrl = "http://127.0.0.1:8000/chat";
    private int timeoutSeconds = 60;

    private JsonSettingsFileTestComponent jsonSettingsSource;

    private bool autoStartBackendBeforeRequest = true;
    private BackendServerStartupTestComponent backendStartupTest;
    private float backendStartupWaitSeconds = 2.0f;
    private int backendHealthPollIntervalMs = 100;

    private string apiKey = "";
    private string apiKeyHeaderName = "Authorization";
    private bool useBearer = true;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(endpointUrl);
    public string ProviderName => "TestLlmProvider";

    public void PrewarmBackendIfConfigured()
    {
        if (!autoStartBackendBeforeRequest)
        {
            return;
        }

        ApplySettingsFromJsonSource();
        TryStartBackendIfNeeded();
    }

    private void Awake()
    {
        if (jsonSettingsSource == null)
        {
            jsonSettingsSource = GetComponent<JsonSettingsFileTestComponent>();
        }

        if (jsonSettingsSource == null)
        {
            Log.Error("[TestLlmProvider] JsonSettingsFileTestComponent is required on the same GameObject.");
        }

        if (backendStartupTest == null)
        {
            backendStartupTest = GetComponent<BackendServerStartupTestComponent>();
        }

        if (backendStartupTest == null)
        {
            Log.Error("[TestLlmProvider] BackendServerStartupTestComponent is required on the same GameObject.");
        }

        ApplySettingsFromJsonSource();
    }

    public async UniTask ChatAsync(
        string userMessage,
        Action<string> onStream,
        Action onComplete,
        CancellationToken cancellationToken = default)
    {
        double chatStart = Time.realtimeSinceStartupAsDouble;
        ApplySettingsFromJsonSource();

        if (!IsAvailable)
        {
            Log.Error("[TestLlmProvider] Endpoint URL is not configured.");
            onComplete?.Invoke();
            return;
        }

        if (autoStartBackendBeforeRequest)
        {
            TryStartBackendIfNeeded();
            await WaitForBackendReady(cancellationToken);
        }

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
        request.timeout = Mathf.Max(1, timeoutSeconds);

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            string value = useBearer ? $"Bearer {apiKey}" : apiKey;
            request.SetRequestHeader(apiKeyHeaderName, value);
        }

        try
        {
            await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            onComplete?.Invoke();
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"[TestLlmProvider] Request failed: {ex.Message} (Endpoint: {endpointUrl})");
            onComplete?.Invoke();
            return;
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            string responseBody = request.downloadHandler?.text ?? string.Empty;
            Log.Error($"[TestLlmProvider] HTTP error: {request.responseCode} {request.error} (Endpoint: {endpointUrl}) Response: {responseBody}");
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
        Log.Info($"[TestLlmProvider] Chat completed. totalMs={totalMs:0}, httpMs={httpMs:0}, endpoint={endpointUrl}");
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
        }
        catch (Exception ex)
        {
            Log.Warning($"[TestLlmProvider] Response parse failed: {ex.Message}");
        }

        return string.Empty;
    }

    private void TryStartBackendIfNeeded()
    {
        if (backendStartupTest == null)
        {
            backendStartupTest = GetComponent<BackendServerStartupTestComponent>();
        }

        if (backendStartupTest == null)
        {
            return;
        }

        if (backendStartupTest.IsBackendAlive())
        {
            return;
        }

        backendStartupTest.StartManagedBackend();
    }

    private async UniTask WaitForBackendReady(CancellationToken cancellationToken)
    {
        if (backendStartupTest == null)
        {
            backendStartupTest = GetComponent<BackendServerStartupTestComponent>();
        }

        if (backendStartupTest == null)
        {
            return;
        }

        if (backendStartupTest.IsBackendAlive())
        {
            return;
        }

        int maxWaitMs = Mathf.Max(0, Mathf.RoundToInt(backendStartupWaitSeconds * 1000f));
        if (maxWaitMs <= 0)
        {
            return;
        }

        int pollMs = Mathf.Max(50, backendHealthPollIntervalMs);
        int elapsed = 0;
        while (elapsed < maxWaitMs)
        {
            if (backendStartupTest.IsBackendAlive())
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await UniTask.Delay(pollMs, cancellationToken: cancellationToken);
            elapsed += pollMs;
        }

        if (!backendStartupTest.IsBackendAlive())
        {
            Log.Warning($"[TestLlmProvider] Backend readiness wait timed out. waitedMs={maxWaitMs}");
        }
    }

    private void ApplySettingsFromJsonSource()
    {
        if (jsonSettingsSource == null)
        {
            return;
        }

        if (!jsonSettingsSource.TryGetLlmProviderSettings(out var settings) || settings == null)
        {
            return;
        }

        endpointUrl = settings.endpointUrl ?? endpointUrl;
        timeoutSeconds = settings.timeoutSeconds;
        autoStartBackendBeforeRequest = settings.autoStartBackendBeforeRequest;
        backendStartupWaitSeconds = settings.backendStartupWaitSeconds;
        backendHealthPollIntervalMs = settings.backendHealthPollIntervalMs;
        apiKey = settings.apiKey ?? apiKey;
        apiKeyHeaderName = settings.apiKeyHeaderName ?? apiKeyHeaderName;
        useBearer = settings.useBearer;
    }
}
