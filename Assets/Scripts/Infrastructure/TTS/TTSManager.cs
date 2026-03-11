using System;
using System.Collections;
using System.Net.Sockets;
using System.Text;
using Unity.Logging;
using UnityEngine;
using UnityEngine.Networking;

public class TTSManager : SingletonMonoBehaviour<TTSManager>
{
    [Header("Dependencies")]
    [SerializeField] private AudioSource ttsAudioSource;
    [SerializeField] private CharacterSpeechBubbleUI characterSpeechBubbleUI;

    [Header("Run")]
    [SerializeField] private string testText = "こんにちは。";
    [SerializeField] private bool autoPlayOnStart = true;
    [SerializeField] private bool showSpeechBubble = false;
    [SerializeField] private float spawnWaitSec = 12f;
    [SerializeField] private float spawnPollSec = 0.1f;
    [SerializeField] private float bubbleHideDelaySec = 3f;

    private bool _isUiProcess;
    private bool _loggedSettings;
    private int _lastHealthTimeoutMs;
    private float _lastStartupWaitSec;

    /// <summary>
    /// シングルトン初期化後に実行環境判定と設定反映を行い、起動ログを出力します。
    /// </summary>
    private protected override void Awake()
    {
        base.Awake();
        _isUiProcess = IsUiProcess();
        Log.Info($"[TtsCoeiroinkTest] Awake completed. IsUiProcess={_isUiProcess}");
        ApplySettings();
    }

    private string baseUrl = $"http://{Constant.BackendHost}:{Constant.CoeiroinkHealthPort}/v1";
    private string speakerId = "";
    private int styleId;
    private float startupWaitSec = 2.0f;
    private int healthCheckTimeoutMs = 150;
    private int[] fallbackPorts = { Constant.CoeiroinkHealthPort, 50031, 50021 };

    [ContextMenu("Play Test TTS")]
    /// <summary>
    /// インスペクタで指定したテスト文を音声合成して再生します。
    /// </summary>
    public void PlayTestTts()
    {
        PlayTts(testText);
    }

    [ContextMenu("Stop Test TTS")]
    /// <summary>
    /// 再生中の音声を停止し、必要に応じて吹き出し表示を閉じます。
    /// </summary>
    public void StopTestTts()
    {
        if (ttsAudioSource != null)
        {
            ttsAudioSource.Stop();
            ttsAudioSource.clip = null;
        }

        if (!_isUiProcess && showSpeechBubble)
        {
            characterSpeechBubbleUI?.HideThinkingBubble();
            characterSpeechBubbleUI?.HideTtsBubble();
        }

        Log.Info("[TtsCoeiroinkTest] TTS playback stopped.");
    }

    /// <summary>
    /// 依存コンポーネントを解決したうえで、指定テキストの音声合成コルーチンを開始します。
    /// </summary>
    public void PlayTts(string text)
    {
        ApplySettings();

        if (string.IsNullOrWhiteSpace(text))
        {
            Log.Warning("[TtsCoeiroinkTest] text is empty.");
            return;
        }

        Log.Info($"[TtsCoeiroinkTest] PlayTts requested. textLength={text.Length}");

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

        StopAllCoroutines();
        StartCoroutine(Synthesize(text));
    }

    /// <summary>
    /// 起動時の自動再生設定に従って、キャラクター出現待ち後のテスト再生を開始します。
    /// </summary>
    private void Start()
    {
        if (_isUiProcess)
        {
            Log.Info("[TtsCoeiroinkTest] Start skipped in external-ui process.");
            return;
        }

        Log.Info("[TtsCoeiroinkTest] Start completed.");

        if (!autoPlayOnStart)
        {
            Log.Info("[TtsCoeiroinkTest] Auto test playback is disabled.");
            return;
        }

        Log.Info("[TtsCoeiroinkTest] Auto test playback started.");
        StartCoroutine(WaitCharacterSpawn());
    }

    /// <summary>
    /// キャラクター生成完了をタイムアウト付きで待機し、完了またはタイムアウト時にテスト再生します。
    /// </summary>
    private IEnumerator WaitCharacterSpawn()
    {
        float timeout = Mathf.Max(0.1f, spawnWaitSec);
        float poll = Mathf.Max(0.02f, spawnPollSec);
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            if (CharacterManager.Instance.ModelContainer != null)
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

    /// <summary>
    /// TTSエンジン起動確認、音声合成API呼び出し、WAVデコード、AudioSource再生までを順に実行します。
    /// </summary>
    private IEnumerator Synthesize(string text)
    {
        TryStartEngine();
        yield return WaitEngineReady();

        if (!IsEngineAlive())
        {
            if (TryResolveBaseUrl(out var resolvedBaseUrl))
            {
                baseUrl = resolvedBaseUrl;
            }
        }

        var normalizedUrl = NormalizeBaseUrl();
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            if (!_isUiProcess && showSpeechBubble)
            {
                characterSpeechBubbleUI?.HideThinkingBubble();
            }
            Log.Error("[TtsCoeiroinkTest] COEIROINK base URL is invalid.");
            yield break;
        }

        var payload = BuildPayload(text, speakerId, styleId);

        byte[] wavBytes = null;
        string usedSynthesisUrl = string.Empty;
        long lastResponseCode = 0;
        string lastError = string.Empty;
        string lastErrorBody = string.Empty;

        var synthesisUrlCandidates = BuildUrlCandidates(normalizedUrl);
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
            if (!_isUiProcess && showSpeechBubble)
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
            if (!_isUiProcess && showSpeechBubble)
            {
                characterSpeechBubbleUI?.HideThinkingBubble();
            }
            Log.Error($"[TtsCoeiroinkTest] WAV decode failed: {error}");
            yield break;
        }

        if (audioClip == null)
        {
            if (!_isUiProcess)
            {
                characterSpeechBubbleUI?.HideThinkingBubble();
            }
            Log.Error("[TtsCoeiroinkTest] Audio clip is null.");
            yield break;
        }

        ttsAudioSource.clip = audioClip;
        ttsAudioSource.volume = 1.0f;
        ttsAudioSource.Play();
        if (!_isUiProcess && showSpeechBubble)
        {
            characterSpeechBubbleUI?.HideThinkingBubble();
            characterSpeechBubbleUI?.ShowTtsBubble(text, bubbleHideDelaySec);
        }
        Log.Info($"[TtsCoeiroinkTest] playback started. endpoint={usedSynthesisUrl}");
    }

    /// <summary>
    /// ベースURLから合成エンドポイント候補を生成します。
    /// </summary>
    private string[] BuildUrlCandidates(string baseUrl)
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

    /// <summary>
    /// コマンドライン引数を確認し、このプロセスがUIプロセス実行かを判定します。
    /// </summary>
    private static bool IsUiProcess()
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
                if (string.Equals(args[i], Constant.UIProcessArgument, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        } catch
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// エンジン未起動時のみ BackendManager 経由でバックエンド起動を要求します。
    /// </summary>
    private void TryStartEngine()
    {
        if (IsEngineAlive())
        {
            return;
        }

        if (BackendManager.Instance == null)
        {
            Log.Warning("[TtsCoeiroinkTest] BackendManager is not found. Skip backend startup request.");
            return;
        }

        BackendManager.Instance.StartBackend();
    }

    /// <summary>
    /// 設定秒数の間、短い間隔でエンジン到達性を監視し、到達可能になれば待機を終了します。
    /// </summary>
    private IEnumerator WaitEngineReady()
    {
        var delay = Mathf.Max(0f, startupWaitSec);
        if (delay <= 0f)
        {
            yield break;
        }

        float elapsed = 0f;
        const float poll = 0.1f;
        while (elapsed < delay)
        {
            if (IsEngineAlive())
            {
                yield break;
            }

            yield return new WaitForSeconds(poll);
            elapsed += poll;
        }

        Log.Warning($"[TtsCoeiroinkTest] COEIROINK not reachable after wait. BaseUrl: {baseUrl}");
    }

    /// <summary>
    /// 現在設定されているエンドポイントへTCP接続を試行し、エンジンの生存を判定します。
    /// </summary>
    private bool IsEngineAlive()
    {
        try
        {
            if (!TryGetEndpoint(out var host, out var port))
            {
                return false;
            }

            using var client = new TcpClient();
            var result = client.BeginConnect(host, port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(healthCheckTimeoutMs));
            if (!success)
            {
                return false;
            }

            client.EndConnect(result);
            return true;
        } catch
        {
            return false;
        }
    }

    /// <summary>
    /// 正規化済みベースURLから接続先ホストとポートを取得します。
    /// </summary>
    private bool TryGetEndpoint(out string host, out int port)
    {
        host = Constant.BackendHost;
        port = Constant.CoeiroinkHealthPort;

        var baseUrl = NormalizeBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        host = uri.Host;
        port = uri.Port > 0 ? uri.Port : Constant.CoeiroinkHealthPort;
        return true;
    }

    /// <summary>
    /// フォールバックポートを順に疎通確認し、到達可能なベースURLを解決します。
    /// </summary>
    private bool TryResolveBaseUrl(out string resolvedBaseUrl)
    {
        resolvedBaseUrl = string.Empty;

        var baseUrl = NormalizeBaseUrl();
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

        if (fallbackPorts == null)
        {
            return false;
        }

        for (int i = 0; i < fallbackPorts.Length; i++)
        {
            int fallbackPort = fallbackPorts[i];
            if (fallbackPort <= 0)
            {
                continue;
            }

            if (IsEngineAlive(uri.Host, fallbackPort))
            {
                resolvedBaseUrl = $"{uri.Scheme}://{uri.Host}:{fallbackPort}{basePath}";
                Log.Warning($"[TtsCoeiroinkTest] COEIROINK base URL updated to reachable endpoint: {resolvedBaseUrl}");
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 指定ホストとポートへTCP接続を試行し、エンジン到達性を判定します。
    /// </summary>
    private bool IsEngineAlive(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var result = client.BeginConnect(host, port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(healthCheckTimeoutMs));
            if (!success)
            {
                return false;
            }

            client.EndConnect(result);
            return true;
        } catch
        {
            return false;
        }
    }

    /// <summary>
    /// SettingsProvider からTTS関連設定を読み込み、接続先と話者情報を反映します。
    /// </summary>
    private void ApplySettings()
    {
        try
        {
            var provider = SettingsProvider.Instance;
            if (provider?.Backend == null)
            {
                return;
            }

            var backend = provider.Backend;
            healthCheckTimeoutMs = backend.TcpTimeoutMs;
            startupWaitSec = backend.StartupTimeoutSeconds;

            string coeiroinkHost = string.IsNullOrWhiteSpace(backend.CoeiroinkHost)
                ? Constant.BackendHost
                : backend.CoeiroinkHost.Trim();
            int coeiroinkPort = backend.CoeiroinkPort > 0 ? backend.CoeiroinkPort : Constant.CoeiroinkHealthPort;
            baseUrl = EnsureV1BasePath($"http://{coeiroinkHost}:{coeiroinkPort}");

            speakerId = backend.CoeiroinkSpeakerUuid ?? string.Empty;
            styleId = backend.CoeiroinkStyleId;

            bool changed = !_loggedSettings
                || _lastHealthTimeoutMs != healthCheckTimeoutMs
                || !Mathf.Approximately(_lastStartupWaitSec, startupWaitSec);

            if (changed)
            {
                _loggedSettings = true;
                _lastHealthTimeoutMs = healthCheckTimeoutMs;
                _lastStartupWaitSec = startupWaitSec;
                Log.Info($"[TtsCoeiroinkTest] Settings applied. healthCheckTimeoutMs={healthCheckTimeoutMs}, startupWaitSeconds={startupWaitSec:0.###}");
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[TtsCoeiroinkTest] Failed to load settings from SettingsProvider: {ex.Message}");
        }
    }

    /// <summary>
    /// ベースURLを絶対URLへ正規化し、末尾を /v1 に揃えます。
    /// </summary>
    private string NormalizeBaseUrl()
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return string.Empty;
        }

        var trimmed = baseUrl.Trim().TrimEnd('/');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
        {
            return EnsureV1BasePath(absolute.ToString().TrimEnd('/'));
        }

        var withScheme = $"http://{trimmed}";
        return Uri.TryCreate(withScheme, UriKind.Absolute, out var fallback)
            ? EnsureV1BasePath(fallback.ToString().TrimEnd('/'))
            : string.Empty;
    }

    /// <summary>
    /// URLのスキーム・ホスト・ポートを維持したままAPIベースパスを /v1 に固定します。
    /// </summary>
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

    /// <summary>
    /// JSON文字列に埋め込むため、制御文字と引用符をエスケープします。
    /// </summary>
    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    /// <summary>
    /// 合成APIに送信するJSONペイロード文字列を構築します。
    /// </summary>
    private static string BuildPayload(string text, string speakerId, int styleId)
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
