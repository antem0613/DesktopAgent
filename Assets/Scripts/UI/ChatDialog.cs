using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using Unity.Logging;
using UnityEngine.Localization.Components;
using UnityEngine.UI;
using Button = UnityEngine.UI.Button;
using System.Linq;
using UnityEngine.Networking;

/// <summary>
/// チャットダイアログ
/// </summary>
public class ChatDialog : DialogBase
{
    /// <summary>
    /// チャットダイアログの入力フィールド
    /// </summary>
    [SerializeField] private TMP_InputField inputField;

    /// <summary>
    /// チャットダイアログの送信ボタン
    /// </summary>
    [SerializeField] private Button sendButton;

    /// <summary>
    /// チャットダイアログのスクロールビュー
    /// </summary>
    [SerializeField] private ScrollRect scrollRect;

    /// <summary>
    /// チャットダイアログのテキスト表示
    /// </summary>
    [SerializeField] private TextMeshProUGUI chatText;

    /// <summary>
    /// チャット履歴テキストビルダー
    /// </summary>
    private readonly StringBuilder _chatTextBuilder = new StringBuilder();

    /// <summary>
    /// TTSのAudioSource
    /// </summary>
    [SerializeField] private AudioSource ttsAudioSource;

    [SerializeField] private Button microphoneButton;

    [SerializeField] private SwitchMicrophoneIcon microphoneIcon;

    /// <summary>
    /// AIの返信を蓄積するビルダー
    /// </summary>
    private StringBuilder _replyTextBuilder;

    /// <summary>
    /// チャットダイアログのオーバーレイ通知イメージ
    /// </summary>
    [SerializeField] private Image overNoticeImage;

    /// <summary>
    /// チャットダイアログのオーバーレイ通知ローカライズされた文字列イベント
    /// </summary>
    [SerializeField] private LocalizeStringEvent overNoticeLocalizedStringEvent;

    /// <summary>
    /// チャットダイアログのオーバーレイ通知テキスト
    /// </summary>
    [SerializeField] private TextMeshProUGUI overNoticeText;

    /// <summary>
    /// 入力をブロックするフラグ
    /// </summary>
    private bool _inputBlocked = false;

    /// <summary>
    /// 前回の返信の長さを記録する変数
    /// </summary>
    private int _lastReplyLength = 0;

    /// <summary>
    /// マイクがオンかどうかのフラグ
    /// </summary>
    private bool _isMiscrophoneOn = false;

    /// <summary>
    /// マイクデバイス選択用ドロップダウン
    /// </summary>
    [SerializeField] private TMP_Dropdown micDeviceDropdown;

    [SerializeField] private VoskSpeechToText speechToText;

    [Header("COEIROINK TTS")]
    [SerializeField] private string coeiroinkBaseUrl = "http://127.0.0.1:50031/v1";
    [SerializeField] private string coeiroinkSpeakerId = "";
    [SerializeField] private int coeiroinkStyleId = 0;
    [SerializeField] private bool autoStartCoeiroinkBeforeTts = true;
    [SerializeField] private float coeiroinkStartupWaitSeconds = 2.0f;
    [SerializeField] private int coeiroinkHealthCheckTimeoutMs = 150;
    [SerializeField] private int[] coeiroinkFallbackPorts = { 50031, 50032, 50021 };

    private string _lastVoiceMessage = string.Empty;

    // 音声合成用のフィールド
    private bool _isTTSInitialized = false;

    private void Start()
    {

        SetEvents();
        // マイク音声認識イベント購読
        if (speechToText != null)
        {
            speechToText.OnSpeechRecognized += OnSpeechRecognized;
        }
        // アイコン初期化
        microphoneIcon.SwitchIcon(_isMiscrophoneOn);

        // AudioSourceが設定されていない場合、自動作成
        if (ttsAudioSource == null)
        {
            ttsAudioSource = gameObject.AddComponent<AudioSource>();
            ttsAudioSource.playOnAwake = false;
            ttsAudioSource.volume = 1.0f;
        }

        // 音声合成の初期化
        InitializeTTS();
    }

    [SerializeField] private TextMeshProUGUI micLevelText;
    private void Update()
    {
    }

    /// <summary>
    /// ダイアログを表示する
    /// </summary>
    public override void Show()
    {
        base.Show();
        SwitchModelDownloadState();
    }

    /// <summary>
    /// モデルのダウンロード状況によって表示を切り替える
    /// </summary>
    private void SwitchModelDownloadState()
    {
        switch (ModelDownloader.ModelDownloadProgressEnum)
        {
            case ModelDownloadProgressEnum.ProgressChanged:
                ShowOverNotice("モデルのダウンロード中...");
                break;
            case ModelDownloadProgressEnum.DownloadCompleted:
                HideOverNotice();
                break;
            case ModelDownloadProgressEnum.DownloadFailed:
                ShowOverNotice("モデルのダウンロードに失敗しました。");
                break;
            default: throw new ArgumentOutOfRangeException();
        }
    }

    /// <summary>
    /// Submitアクションが実行されたときの処理（Enterキー）
    /// </summary>
    private void OnSubmit(InputAction.CallbackContext context)
    {
        // 入力フィールドが選択されている場合のみ処理
        if (inputField.isFocused)
        {
            SendMessages();

            // InputFieldが改行を追加しないようにする
            inputField.DeactivateInputField();
            inputField.ActivateInputField();
        }
    }

    /// <summary>
    /// チャットダイアログを表示する
    /// </summary>
    private void ScrollToBottom()
    {
        // レイアウトを強制的に更新
        Canvas.ForceUpdateCanvases();
        // ScrollRectのverticalNormalizedPositionを0に設定（0が一番下、1が一番上）
        scrollRect.verticalNormalizedPosition = 0f;
        // レイアウトを再度更新
        Canvas.ForceUpdateCanvases();
    }

    /// <summary>
    /// メッセージを送信する
    /// </summary>
    private void SendMessages()
    {
        if (_inputBlocked || string.IsNullOrWhiteSpace(inputField.text))
        {
            return;
        }

        // 入力をブロック
        _inputBlocked = true;
        sendButton.interactable = false;
        inputField.interactable = false;

        // ユーザーのメッセージをチャット履歴に追加
        string userMessage = inputField.text;
        _chatTextBuilder.AppendLine($"あなた: {userMessage}");
        chatText.text = _chatTextBuilder.ToString();

        // ScrollToBottomを呼び出して最新のメッセージを表示
        ScrollToBottom();

        // 入力フィールドをクリア
        inputField.text = string.Empty;

        // AIの返信用のStringBuilderを初期化
        _replyTextBuilder = new StringBuilder();

        // 前回の返信の長さをリセット
        _lastReplyLength = 0;

        // LLMにユーザーのメッセージを送信し、返信を処理
        _ = ReceiveAIResponse(userMessage);
    }

    /// <summary>
    /// 非同期でAIの返信を受信
    /// </summary>
    private async UniTask ReceiveAIResponse(string userMessage)
    {
        try
        {
            var provider = ResolveChatProvider();
            if (provider == null || !provider.IsAvailable)
            {
                Log.Error("[ChatDialog] Chat provider is not assigned.");
                _inputBlocked = false;
                sendButton.interactable = true;
                inputField.interactable = true;
                return;
            }

            // プロバイダー経由で返信を受信
            await provider.ChatAsync(
                userMessage,
                HandleReply,
                ReplyCompleted
            );
        } catch (Exception ex)
        {
            Log.Error($"AIの返信の受信中にエラーが発生しました。{ex.Message}");
            // エラーが発生した場合、入力をアンブロック
            _inputBlocked = false;
            sendButton.interactable = true;
            inputField.interactable = true;
        }
    }

    private IChatProvider ResolveChatProvider()
    {

        return null;
    }

    /// <summary>
    /// AIの返信を処理する（ストリーミング対応）
    /// </summary>
    /// <param name="reply">累積されたAIからの返信</param>
    private void HandleReply(string reply)
    {
        // 新しく追加された部分のみを取得
        string newText = reply.Substring(_lastReplyLength);
        _lastReplyLength = reply.Length;

        // AIの返信をビルダーに追加
        _replyTextBuilder.Append(newText);

        // 現在のチャット履歴と進行中のAI返信を表示
        using (var sb = ZString.CreateStringBuilder())
        {
            sb.Append(_chatTextBuilder.ToString());
            sb.Append($"AI: {_replyTextBuilder}");
            chatText.text = sb.ToString();
        }

        // ScrollToBottomを呼び出して最新のメッセージを表示
        ScrollToBottom();
    }

    /// <summary>
    /// AIの返信が完了したときの処理
    /// </summary>
    private void ReplyCompleted()
    {
        // 最終的なAIの返信をチャット履歴に追加
        var aiReplyText = _replyTextBuilder?.ToString() ?? "";
        _chatTextBuilder.AppendLine($"AI: {aiReplyText}");
        chatText.text = _chatTextBuilder.ToString();

        // ScrollToBottomを呼び出して最新のメッセージを表示
        ScrollToBottom();

        // 音声合成を実行
        if (_isTTSInitialized && !string.IsNullOrWhiteSpace(aiReplyText))
        {
            _ = SynthesizeAndPlayTTS(aiReplyText);
        }

        // AIの返信用ビルダーをクリア
        _replyTextBuilder = null;

        // 入力をアンブロック
        _inputBlocked = false;
        sendButton.interactable = true;
        inputField.interactable = true;

        // 入力フィールドにフォーカスをセット
        inputField.ActivateInputField();

        // ユーザがマイク ON であれば録音再開
        if (_isMiscrophoneOn)
        {
            speechToText?.ResumeRecording();
        }
    }

    /// <summary>
    /// イベントを設定する
    /// </summary>
    private void SetEvents()
    {
        sendButton.onClick.AddListener(SendMessages);
        microphoneButton.onClick.AddListener(() =>
        {
            _isMiscrophoneOn = !_isMiscrophoneOn;
            microphoneIcon.SwitchIcon(_isMiscrophoneOn);

            if (speechToText == null) return;

            // 録音状態をトグル
            speechToText.ToggleRecording();

            // マイクONの間はテキスト入力を無効化する
            inputField.interactable = !_isMiscrophoneOn;
        });
    }

    /// <summary>
    /// オーバーレイ通知を表示する
    /// </summary>
    /// <param name="notice"></param>
    public void ShowOverNotice(string notice)
    {
        overNoticeLocalizedStringEvent.StringReference.Arguments = new object[] { notice };
        overNoticeLocalizedStringEvent.StringReference.RefreshString();
        overNoticeImage.enabled = true;
        overNoticeText.enabled = true;
    }

    /// <summary>
    /// オーバーレイ通知を非表示にする
    /// </summary>
    public void HideOverNotice()
    {
        overNoticeText.enabled = false;
        overNoticeImage.enabled = false;
    }

    /// <summary>
    /// 音声合成を初期化する
    /// </summary>
    private async void InitializeTTS()
    {
        await UniTask.Yield();
        _isTTSInitialized = !string.IsNullOrWhiteSpace(coeiroinkBaseUrl);
    }

    /// <summary>
    /// テキストを音声合成して再生する
    /// </summary>
    private async UniTask SynthesizeAndPlayTTS(string text)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            if (autoStartCoeiroinkBeforeTts)
            {
                await WaitForCoeiroinkReady();
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
                return;
            }

            var synthUrl = $"{baseUrl}/synthesis";
            using var synthReq = new UnityWebRequest(synthUrl, UnityWebRequest.kHttpVerbPOST);
            var payload = BuildCoeiroinkProcessPayload(text, coeiroinkSpeakerId, coeiroinkStyleId);
            synthReq.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
            synthReq.downloadHandler = new DownloadHandlerBuffer();
            synthReq.SetRequestHeader("Content-Type", "application/json");

            await synthReq.SendWebRequest().ToUniTask();

            if (synthReq.result != UnityWebRequest.Result.Success)
            {
                return;
            }

            var wavBytes = synthReq.downloadHandler.data;
            if (!WavUtility.TryCreateAudioClipFromWav(wavBytes, $"TTS_{DateTime.Now:HHmmss}", out var audioClip, out var error))
            {
                return;
            }

            if (ttsAudioSource != null && audioClip != null)
            {
                ttsAudioSource.clip = audioClip;
                ttsAudioSource.volume = 1.0f;
                ttsAudioSource.Play();
            }
        } catch
        {
         
        }
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    private static string BuildCoeiroinkProcessPayload(string text, string speakerId, int styleId)
    {
        var safeText = EscapeJson(text);
        var safeSpeaker = EscapeJson(speakerId);

        return "{" +
               $"\"text\":\"{safeText}\"," +
               $"\"speaker\":\"{safeSpeaker}\"," +
               $"\"styleId\":{styleId}," +
               "\"volumeScale\":1.0," +
               "\"pitchScale\":0.0," +
               "\"intonationScale\":1.0," +
               "\"prePhonemeLength\":0.1," +
               "\"postPhonemeLength\":0.1," +
               "\"outputSamplingRate\":0," +
               "\"sampledIntervalValue\":0," +
               "\"adjustedF0\":[]," +
               "\"processingAlgorithm\":\"\"," +
               "\"startTrimBuffer\":0," +
               "\"endTrimBuffer\":0," +
               "\"pauseLength\":0," +
               "\"pauseStartTrimBuffer\":0," +
               "\"pauseEndTrimBuffer\":0," +
               "\"wavBase64\":\"\"," +
               "\"moraDurations\":[]" +
               "}";
    }

    private async UniTask WaitForCoeiroinkReady()
    {
        var delayMs = Mathf.Max(0, Mathf.RoundToInt(coeiroinkStartupWaitSeconds * 1000f));
        if (delayMs == 0) return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < delayMs)
        {
            if (IsCoeiroinkReachable()) return;
            await UniTask.Delay(100);
        }
    }

    private bool IsCoeiroinkReachable()
    {
        try
        {
            if (!TryGetCoeiroinkEndpoint(out var host, out var port))
            {
                return false;
            }

            using var client = new System.Net.Sockets.TcpClient();
            var result = client.BeginConnect(host, port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(coeiroinkHealthCheckTimeoutMs));
            if (!success) return false;
            client.EndConnect(result);
            return true;
        } catch
        {
            return false;
        }
    }

    private bool TryGetCoeiroinkEndpoint(out string host, out int port)
    {
        host = "127.0.0.1";
        port = 50031;

        var baseUrl = NormalizeCoeiroinkBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl)) return false;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return false;

        host = uri.Host;
        port = uri.Port > 0 ? uri.Port : 50031;
        return true;
    }

    private bool TryResolveCoeiroinkBaseUrl(out string resolvedBaseUrl)
    {
        resolvedBaseUrl = string.Empty;

        var baseUrl = NormalizeCoeiroinkBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl)) return false;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return false;

        var basePath = uri.AbsolutePath?.TrimEnd('/') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(basePath) || basePath == "/")
        {
            basePath = string.Empty;
        }

        foreach (var port in coeiroinkFallbackPorts)
        {
            if (port <= 0) continue;
            if (IsCoeiroinkReachable(uri.Host, port))
            {
                resolvedBaseUrl = $"{uri.Scheme}://{uri.Host}:{port}{basePath}";
                return true;
            }
        }

        return false;
    }

    private bool IsCoeiroinkReachable(string host, int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var result = client.BeginConnect(host, port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(coeiroinkHealthCheckTimeoutMs));
            if (!success) return false;
            client.EndConnect(result);
            return true;
        } catch
        {
            return false;
        }
    }

    private string NormalizeCoeiroinkBaseUrl()
    {
        if (string.IsNullOrWhiteSpace(coeiroinkBaseUrl)) return string.Empty;
        var trimmed = coeiroinkBaseUrl.Trim().TrimEnd('/');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString().TrimEnd('/');
        }

        var withScheme = $"http://{trimmed}";
        return Uri.TryCreate(withScheme, UriKind.Absolute, out var fallback)
            ? fallback.ToString().TrimEnd('/')
            : string.Empty;
    }

    private void OnDestroy()
    {
        sendButton.onClick.RemoveAllListeners();

        // リスナーの登録解除
        if (InputController.Instance != null)
        {
            InputController.Instance.UI.Submit.performed -= OnSubmit;
        }

        if (speechToText != null)
        {
            speechToText.OnSpeechRecognized -= OnSpeechRecognized;
        }

        // 音声合成関連のリソースを解放（COEIROINK使用時は不要）
    }

    /// <summary>
    /// 音声認識でユーザーの発話を受け取った際に呼び出される
    /// </summary>
    /// <param name="recognizedText">確定したテキスト</param>
    private void OnSpeechRecognized(string recognizedText)
    {
        if (!_isMiscrophoneOn) return;
        if (string.IsNullOrWhiteSpace(recognizedText)) return;
        if (_inputBlocked) return; // AI返信中は無視

        // 同一メッセージが連続で来ないように判定
        if (recognizedText == _lastVoiceMessage) return;
        _lastVoiceMessage = recognizedText;

        SendVoiceMessage(recognizedText);
    }

    /// <summary>
    /// 音声入力由来のメッセージを送信する
    /// </summary>
    private void SendVoiceMessage(string userMessage)
    {
        if (_inputBlocked || string.IsNullOrWhiteSpace(userMessage))
        {
            return;
        }

        // 入力をブロック
        _inputBlocked = true;
        sendButton.interactable = false;
        inputField.interactable = false;

        // ユーザーのメッセージをチャット履歴に追加
        _chatTextBuilder.AppendLine($"あなた: {userMessage}");
        chatText.text = _chatTextBuilder.ToString();

        // ScrollToBottomを呼び出して最新のメッセージを表示
        ScrollToBottom();

        // AIの返信用のStringBuilderを初期化
        _replyTextBuilder = new StringBuilder();

        // 前回の返信の長さをリセット
        _lastReplyLength = 0;

        // LLMにユーザーのメッセージを送信し、返信を処理
        _ = ReceiveAIResponse(userMessage);

        // LLM 処理中はマイクを停止
        speechToText?.PauseRecording();
    }
}