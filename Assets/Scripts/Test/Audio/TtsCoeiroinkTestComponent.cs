using System;
using System.Collections;
using System.Net.Sockets;
using System.Text;
using Unity.Logging;
using UnityEngine;
using UnityEngine.Networking;

[RequireComponent(typeof(JsonSettingsFileTestComponent))]
public class TtsCoeiroinkTestComponent : MonoBehaviour
{
    private const string ExternalUiProcessRoleArgument = "--external-ui-process";

    [Header("Dependencies")]
    [SerializeField] private BackendServerStartupTestComponent backendServerStartupTest;
    [SerializeField] private AudioSource ttsAudioSource;
    [SerializeField] private CharacterSpeechBubbleUI characterSpeechBubbleUI;
    [SerializeField] private TestCharacterSpawnComponent characterSpawnComponent;
    private JsonSettingsFileTestComponent jsonSettingsSource;

    [Header("Run")]
    [SerializeField] private bool runOnStart;
    [SerializeField] private bool runInExternalUiProcess;
    [SerializeField] private string testText = "こんにちは。TTSテストです。";
    [SerializeField] private bool waitForCharacterSpawnBeforeRunOnStart = true;
    [SerializeField] private float characterSpawnWaitTimeoutSeconds = 12f;
    [SerializeField] private float characterSpawnPollIntervalSeconds = 0.1f;
    [SerializeField] private bool showBubbleInTest = true;
    [SerializeField] private float ttsBubbleHideDelaySeconds = 8f;

    private bool _isExternalUiProcess;

    private void Awake()
    {
        _isExternalUiProcess = IsExternalUiProcess();

        if (jsonSettingsSource == null)
        {
            jsonSettingsSource = GetComponent<JsonSettingsFileTestComponent>();
        }

        if (jsonSettingsSource == null)
        {
            Log.Error("[TtsCoeiroinkTest] JsonSettingsFileTestComponent is required on the same GameObject.");
        }

        ApplySettingsFromJsonSource();
    }

    private string coeiroinkBaseUrl = "http://127.0.0.1:50032/v1";
    private string coeiroinkSpeakerId = "";
    private int coeiroinkStyleId;
    private bool autoStartCoeiroinkBeforeTts = true;
    private bool requestBackendStartupWhenCoeiroinkUnreachable = true;
    private float coeiroinkStartupWaitSeconds = 2.0f;
    private int coeiroinkHealthCheckTimeoutMs = 150;
    private int[] coeiroinkFallbackPorts = { 50032, 50031, 50021 };

    [ContextMenu("Play Test TTS")]
    public void PlayTestTts()
    {
        PlayTts(testText);
    }

    [ContextMenu("Stop Test TTS")]
    public void StopTestTts()
    {
        if (ttsAudioSource != null)
        {
            ttsAudioSource.Stop();
            ttsAudioSource.clip = null;
        }

        if (ShouldShowBubbleInThisProcess())
        {
            characterSpeechBubbleUI?.HideThinkingBubble();
            characterSpeechBubbleUI?.HideTtsBubble();
        }

        Log.Info("[TtsCoeiroinkTest] TTS playback stopped.");
    }

    public void PlayTts(string text)
    {
        ApplySettingsFromJsonSource();

        if (string.IsNullOrWhiteSpace(text))
        {
            Log.Warning("[TtsCoeiroinkTest] text is empty.");
            return;
        }

        if (characterSpeechBubbleUI == null)
        {
            characterSpeechBubbleUI = FindFirstObjectByType<CharacterSpeechBubbleUI>();
        }

        if (ttsAudioSource == null)
        {
            ttsAudioSource = GetComponent<AudioSource>();
            if (ttsAudioSource == null)
            {
                ttsAudioSource = gameObject.AddComponent<AudioSource>();
                ttsAudioSource.playOnAwake = false;
                ttsAudioSource.volume = 1.0f;
            }
        }

        if (ShouldShowBubbleInThisProcess())
        {
            characterSpeechBubbleUI?.HideTtsBubble();
            characterSpeechBubbleUI?.ShowThinkingBubble("音声を生成中...");
        }

        StopAllCoroutines();
        StartCoroutine(SynthesizeAndPlayRoutine(text));
    }

    private void Start()
    {
        if (_isExternalUiProcess && !runInExternalUiProcess)
        {
            Log.Info("[TtsCoeiroinkTest] Start skipped in external-ui process.");
            return;
        }

        if (!runOnStart)
        {
            return;
        }

        if (waitForCharacterSpawnBeforeRunOnStart)
        {
            StartCoroutine(PlayTestTtsAfterCharacterSpawnReadyRoutine());
            return;
        }

        PlayTestTts();
    }

    private IEnumerator PlayTestTtsAfterCharacterSpawnReadyRoutine()
    {
        if (characterSpawnComponent == null)
        {
            characterSpawnComponent = FindFirstObjectByType<TestCharacterSpawnComponent>();
        }

        if (characterSpawnComponent == null)
        {
            Log.Warning("[TtsCoeiroinkTest] Character spawn component not found. Play test TTS immediately.");
            PlayTestTts();
            yield break;
        }

        float timeout = Mathf.Max(0.1f, characterSpawnWaitTimeoutSeconds);
        float poll = Mathf.Max(0.02f, characterSpawnPollIntervalSeconds);
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            if (characterSpawnComponent.SpawnedModel != null)
            {
                Log.Info($"[TtsCoeiroinkTest] Character spawn confirmed. Start test TTS. elapsed={elapsed:0.##}s");
                PlayTestTts();
                yield break;
            }

            yield return new WaitForSeconds(poll);
            elapsed += poll;
        }

        Log.Warning($"[TtsCoeiroinkTest] Character spawn wait timed out ({timeout:0.##}s). Play test TTS anyway.");
        PlayTestTts();
    }

    private IEnumerator SynthesizeAndPlayRoutine(string text)
    {
        if (autoStartCoeiroinkBeforeTts)
        {
            TryStartCoeiroinkIfNeeded();
            yield return WaitForCoeiroinkReady();
        }

        if (!IsCoeiroinkReachable())
        {
            if (TryResolveCoeiroinkBaseUrl(out var resolvedBaseUrl))
            {
                coeiroinkBaseUrl = resolvedBaseUrl;
            }
        }

        var baseUrl = NormalizeCoeiroinkBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            if (ShouldShowBubbleInThisProcess())
            {
                characterSpeechBubbleUI?.HideThinkingBubble();
            }
            Log.Error("[TtsCoeiroinkTest] COEIROINK base URL is invalid.");
            yield break;
        }

        var payload = BuildCoeiroinkProcessPayload(text, coeiroinkSpeakerId, coeiroinkStyleId);

        byte[] wavBytes = null;
        string usedSynthesisUrl = string.Empty;
        long lastResponseCode = 0;
        string lastError = string.Empty;
        string lastErrorBody = string.Empty;

        var synthesisUrlCandidates = BuildSynthesisUrlCandidates(baseUrl);
        for (int i = 0; i < synthesisUrlCandidates.Length; i++)
        {
            string synthUrl = synthesisUrlCandidates[i];
            using var synthReq = new UnityWebRequest(synthUrl, UnityWebRequest.kHttpVerbPOST);
            synthReq.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
            synthReq.downloadHandler = new DownloadHandlerBuffer();
            synthReq.SetRequestHeader("Content-Type", "application/json");

            Log.Info($"[TtsCoeiroinkTest] request endpoint: {synthUrl}");
            Log.Info($"[TtsCoeiroinkTest] request payload: {payload}");

            yield return synthReq.SendWebRequest();

            if (synthReq.result == UnityWebRequest.Result.Success)
            {
                wavBytes = synthReq.downloadHandler.data;
                usedSynthesisUrl = synthUrl;
                break;
            }

            lastResponseCode = synthReq.responseCode;
            lastError = synthReq.error;
            lastErrorBody = synthReq.downloadHandler?.text ?? string.Empty;

            bool canRetryWithNextRoute = synthReq.responseCode == 404 && i < synthesisUrlCandidates.Length - 1;
            if (canRetryWithNextRoute)
            {
                Log.Warning($"[TtsCoeiroinkTest] synthesis endpoint not found at {synthUrl}. Retrying next route.");
                continue;
            }

            break;
        }

        if (wavBytes == null || wavBytes.Length == 0)
        {
            if (ShouldShowBubbleInThisProcess())
            {
                characterSpeechBubbleUI?.HideThinkingBubble();
            }

            Log.Error($"[TtsCoeiroinkTest] synthesis failed: {lastResponseCode} {lastError}");
            if (!string.IsNullOrWhiteSpace(lastErrorBody))
            {
                Log.Error($"[TtsCoeiroinkTest] error body: {lastErrorBody}");
            }

            yield break;
        }
        if (!WavUtility.TryCreateAudioClipFromWav(wavBytes, $"TTS_Test_{DateTime.Now:HHmmss}", out var audioClip, out var error))
        {
            if (ShouldShowBubbleInThisProcess())
            {
                characterSpeechBubbleUI?.HideThinkingBubble();
            }
            Log.Error($"[TtsCoeiroinkTest] WAV decode failed: {error}");
            yield break;
        }

        if (audioClip == null)
        {
            if (ShouldShowBubbleInThisProcess())
            {
                characterSpeechBubbleUI?.HideThinkingBubble();
            }
            Log.Error("[TtsCoeiroinkTest] Audio clip is null.");
            yield break;
        }

        ttsAudioSource.clip = audioClip;
        ttsAudioSource.volume = 1.0f;
        ttsAudioSource.Play();
        if (ShouldShowBubbleInThisProcess())
        {
            characterSpeechBubbleUI?.HideThinkingBubble();
            characterSpeechBubbleUI?.ShowTtsBubble(text, ttsBubbleHideDelaySeconds);
        }
        Log.Info($"[TtsCoeiroinkTest] playback started. endpoint={usedSynthesisUrl}");
    }

    private string[] BuildSynthesisUrlCandidates(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return Array.Empty<string>();
        }

        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return new[] { $"{uri.Scheme}://{uri.Host}:{uri.Port}/v1/synthesis" };
        }

        return new[] { $"{baseUrl.TrimEnd('/')}/v1/synthesis" };
    }

    private bool ShouldShowBubbleInThisProcess()
    {
        return showBubbleInTest && !_isExternalUiProcess;
    }

    private static bool IsExternalUiProcess()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
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

    private void TryStartCoeiroinkIfNeeded()
    {
        if (IsCoeiroinkReachable())
        {
            return;
        }

        if (!requestBackendStartupWhenCoeiroinkUnreachable)
        {
            return;
        }

        if (backendServerStartupTest == null)
        {
            backendServerStartupTest = FindFirstObjectByType<BackendServerStartupTestComponent>();
        }

        if (backendServerStartupTest == null)
        {
            Log.Warning("[TtsCoeiroinkTest] BackendServerStartupTestComponent is not found. Skip backend startup request.");
            return;
        }

        backendServerStartupTest.StartManagedBackend();
    }

    private IEnumerator WaitForCoeiroinkReady()
    {
        var delay = Mathf.Max(0f, coeiroinkStartupWaitSeconds);
        if (delay <= 0f)
        {
            yield break;
        }

        float elapsed = 0f;
        const float poll = 0.1f;
        while (elapsed < delay)
        {
            if (IsCoeiroinkReachable())
            {
                yield break;
            }

            yield return new WaitForSeconds(poll);
            elapsed += poll;
        }

        Log.Warning($"[TtsCoeiroinkTest] COEIROINK not reachable after wait. BaseUrl: {coeiroinkBaseUrl}");
    }

    private bool IsCoeiroinkReachable()
    {
        try
        {
            if (!TryGetCoeiroinkEndpoint(out var host, out var port))
            {
                return false;
            }

            using var client = new TcpClient();
            var result = client.BeginConnect(host, port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(coeiroinkHealthCheckTimeoutMs));
            if (!success)
            {
                return false;
            }

            client.EndConnect(result);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetCoeiroinkEndpoint(out string host, out int port)
    {
        host = "127.0.0.1";
        port = 50032;

        var baseUrl = NormalizeCoeiroinkBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        host = uri.Host;
        port = uri.Port > 0 ? uri.Port : 50032;
        return true;
    }

    private bool TryResolveCoeiroinkBaseUrl(out string resolvedBaseUrl)
    {
        resolvedBaseUrl = string.Empty;

        var baseUrl = NormalizeCoeiroinkBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var basePath = uri.AbsolutePath?.TrimEnd('/') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(basePath) || basePath == "/")
        {
            basePath = string.Empty;
        }

        if (coeiroinkFallbackPorts == null)
        {
            return false;
        }

        for (int i = 0; i < coeiroinkFallbackPorts.Length; i++)
        {
            int fallbackPort = coeiroinkFallbackPorts[i];
            if (fallbackPort <= 0)
            {
                continue;
            }

            if (IsCoeiroinkReachable(uri.Host, fallbackPort))
            {
                resolvedBaseUrl = $"{uri.Scheme}://{uri.Host}:{fallbackPort}{basePath}";
                Log.Warning($"[TtsCoeiroinkTest] COEIROINK base URL updated to reachable endpoint: {resolvedBaseUrl}");
                return true;
            }
        }

        return false;
    }

    private bool IsCoeiroinkReachable(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var result = client.BeginConnect(host, port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(coeiroinkHealthCheckTimeoutMs));
            if (!success)
            {
                return false;
            }

            client.EndConnect(result);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ApplySettingsFromJsonSource()
    {
        if (jsonSettingsSource == null)
        {
            return;
        }

        if (!jsonSettingsSource.TryGetCoeiroinkSettings(out var settings) || settings == null)
        {
            return;
        }

        coeiroinkBaseUrl = settings.baseUrl ?? coeiroinkBaseUrl;
        coeiroinkFallbackPorts = settings.fallbackPorts ?? coeiroinkFallbackPorts;
        coeiroinkHealthCheckTimeoutMs = settings.healthCheckTimeoutMs;
        coeiroinkStartupWaitSeconds = settings.startupWaitSeconds;
        coeiroinkSpeakerId = settings.speakerUuid ?? coeiroinkSpeakerId;
        coeiroinkStyleId = settings.styleId;
    }

    private string NormalizeCoeiroinkBaseUrl()
    {
        if (string.IsNullOrWhiteSpace(coeiroinkBaseUrl))
        {
            return string.Empty;
        }

        var trimmed = coeiroinkBaseUrl.Trim().TrimEnd('/');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
        {
            return EnsureV1BasePath(absolute.ToString().TrimEnd('/'));
        }

        var withScheme = $"http://{trimmed}";
        return Uri.TryCreate(withScheme, UriKind.Absolute, out var fallback)
            ? EnsureV1BasePath(fallback.ToString().TrimEnd('/'))
            : string.Empty;
    }

    private static string EnsureV1BasePath(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url.TrimEnd('/');
        }

        return $"{uri.Scheme}://{uri.Host}:{uri.Port}/v1";
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    private static string BuildCoeiroinkProcessPayload(string text, string speakerId, int styleId)
    {
        var safeText = EscapeJson(text);
        var safeSpeaker = EscapeJson(speakerId);

        return "{" +
               $"\"text\":\"{safeText}\"," +
               $"\"speakerUuid\":\"{safeSpeaker}\"," +
               $"\"styleId\":{styleId}," +
               "\"speedScale\":1.0," +
               "\"volumeScale\":1.0," +
               "\"prosodyDetail\":[]," +
               "\"pitchScale\":0.0," +
               "\"intonationScale\":1.0," +
               "\"prePhonemeLength\":0.1," +
               "\"postPhonemeLength\":0.5," +
               "\"outputSamplingRate\":24000" +
               "}";
    }
}
