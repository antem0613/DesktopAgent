using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using Newtonsoft.Json;
using Vosk;

/// <summary>
///   Vosk を利用したオフライン音声認識。確定テキストを OnSpeechRecognized で通知します。
/// </summary>
public class VoskSpeechToText : MonoBehaviour
{
    [Tooltip("StreamingAssets からの相対パス、または展開済みモデルフォルダ名")]
    public string ModelPath = "vosk-model-small-ja-0.22";

    [Tooltip("マイク入力用コンポーネント")]
    public VoiceProcessor VoiceProcessor;

    [Tooltip("候補として取得する最大アルタナティブ数")]
    public int MaxAlternatives = 1;

    public bool AutoStart = true;

    public List<string> KeyPhrases = new();

    public Action<string> OnSpeechRecognized;
    public Action<string> OnStatusUpdated;

    [Tooltip("無音と判定する秒数（VAD）。この時間だけ音声が途切れたら確定文を送信")]
    public float VadSilenceSeconds = 1.0f;

    [Tooltip("1フレームで処理する認識結果の最大件数。長文時の詰まりを防ぐ")]
    public int MaxResultsPerFrame = 64;

    [Tooltip("この文字数を超えたら無音待ちせずに自動送信する。0以下で無効")]
    public int AutoSendCharThreshold = 80;

    [Header("Diagnostics")]
    [SerializeField] private bool enableDiagnostics = true;
    [SerializeField] private float diagnosticsLogIntervalSeconds = 5f;

    private VoskRecognizer _recognizer;
    private Model _model;
    private bool _recognizerReady;

    private string _modelAbsolutePath;
    private string _grammar;

    private bool _isInitializing;
    private bool _didInit;
    private bool _running;
    private bool _startMicOnInit;

    /// <summary>
    /// 初期化完了済みか
    /// </summary>
    public bool IsInitialized => _didInit;

    /// <summary>
    /// 初期化中か
    /// </summary>
    public bool IsInitializing => _isInitializing;

    private readonly ConcurrentQueue<short[]> _micQueue = new();
    private readonly ConcurrentQueue<string> _resultQueue = new();

    [SerializeField] private TMP_Text recognizerStatusText;

    private static readonly ProfilerMarker markerCreate = new("VoskRecognizer.Create");
    private static readonly ProfilerMarker markerAccept = new("VoskRecognizer.AcceptWaveform");

    private float _silenceTimer = 0f;
    private string _accumulatedText = string.Empty;
    private float _nextDiagnosticsLogTime;
    private int _diagDequeuedFrames;
    private int _diagFinalJsonCount;
    private int _diagPartialJsonCount;
    private int _diagSentCount;

    private void Start()
    {
        if (AutoStart) StartVoskStt();
    }

    public void StartVoskStt(List<string> keyPhrases = null, string modelPath = null, bool startMic = false)
    {
        if (startMic) _startMicOnInit = true;
        if (_isInitializing) { return; }
        if (_didInit) { Debug.Log("Vosk already initialized"); return; }

        if (!string.IsNullOrEmpty(modelPath)) ModelPath = modelPath;
        if (keyPhrases != null) KeyPhrases = keyPhrases;

        Debug.Log($"[STT] StartVoskStt: modelPath='{ModelPath}', startMic={startMic}");

        StartCoroutine(InitializeCoroutine(startMic));
    }

    private IEnumerator InitializeCoroutine(bool startMic)
    {
        _isInitializing = true;
        Debug.Log("[STT] Initialize: waiting for mic device...");
        yield return WaitMicReady();
        Debug.Log("[STT] Initialize: locating model...");
        yield return LocateModelCoroutine();

        if (string.IsNullOrEmpty(_modelAbsolutePath) || !Directory.Exists(_modelAbsolutePath))
        {
            Debug.LogError("[VoskSpeechToText] Model path is invalid. Initialization aborted.");
            _isInitializing = false;
            yield break;
        }

        OnStatusUpdated?.Invoke($"Loading model: {_modelAbsolutePath}");
        Debug.Log($"[STT] Initialize: loading model at '{_modelAbsolutePath}'");
        _model = new Model(_modelAbsolutePath);

        _isInitializing = false;
        _didInit = true;

        SetupEvents();

        Debug.Log("[STT] Initialize: completed");

        if (startMic || _startMicOnInit)
        {
            _startMicOnInit = false;
            ResumeRecording();
        }
    }

    private void SetupEvents()
    {
        if (VoiceProcessor == null)
        {
            Debug.LogError("VoiceProcessor is null");
            return;
        }
        VoiceProcessor.OnFrameCaptured += samples => _micQueue.Enqueue(samples);
    }

    private IEnumerator WaitMicReady()
    {
        while (Microphone.devices.Length == 0) yield return null;
    }

    private IEnumerator LocateModelCoroutine()
    {
        string streaming = Path.Combine(Application.streamingAssetsPath, ModelPath);
        if (Directory.Exists(streaming)) { _modelAbsolutePath = streaming; yield break; }
        string persistent = Path.Combine(Application.persistentDataPath, ModelPath);
        if (Directory.Exists(persistent)) { _modelAbsolutePath = persistent; yield break; }
        Debug.LogError($"Model not found: {ModelPath}");
    }

    public void ToggleRecording()
    {
        if (VoiceProcessor == null) return;
        if (!VoiceProcessor.IsRecording)
        {
            if (!_didInit)
            {
                if (!_isInitializing)
                {
                    StartVoskStt(startMic: true);
                }
                return;
            }
            _running = true;
            VoiceProcessor.StartRecording();
            Debug.Log("[STT] Recording started (toggle)");
            Task.Run(ProcessAudioLoop);
        } else
        {
            _running = false;
            VoiceProcessor.StopRecording();
            Debug.Log("[STT] Recording stopped (toggle)");
        }
    }

    public void PauseRecording()
    {
        if (!VoiceProcessor || !VoiceProcessor.IsRecording) return;
        _running = false;
        VoiceProcessor.StopRecording();
        _accumulatedText = string.Empty;
        _silenceTimer = 0f;
        if (enableDiagnostics)
        {
            Debug.Log($"[STT][diag] PauseRecording queue={_micQueue.Count} running={_running}");
        }
    }

    public void ResumeRecording()
    {
        if (!VoiceProcessor || VoiceProcessor.IsRecording) return;
        if (!_didInit)
        {
            if (!_isInitializing)
            {
                StartVoskStt(startMic: true);
            }
            return;
        }
        _running = true;
        VoiceProcessor.StartRecording();
        Debug.Log("[STT] Recording resumed");
        if (enableDiagnostics)
        {
            Debug.Log($"[STT][diag] ResumeRecording queue={_micQueue.Count} sampleRate={VoiceProcessor.SampleRate}");
        }
        Task.Run(ProcessAudioLoop);
    }

    private async Task ProcessAudioLoop()
    {
        markerCreate.Begin();
        if (!_recognizerReady)
        {
            if (_model == null)
            {
                Debug.LogError("[VoskSpeechToText] Model is not loaded. Abort STT loop.");
                _running = false;
                markerCreate.End();
                return;
            }
            UpdateGrammar();
            float sr = VoiceProcessor != null ? VoiceProcessor.SampleRate : 16000f;
            _recognizer = string.IsNullOrEmpty(_grammar)
                ? new VoskRecognizer(_model, sr)
                : new VoskRecognizer(_model, sr, _grammar);
            _recognizer.SetMaxAlternatives(MaxAlternatives);
            _recognizerReady = true;
            Debug.Log($"[STT] Recognizer ready (sr={sr}, grammar={(string.IsNullOrEmpty(_grammar) ? "none" : "custom")})");
        }
        markerCreate.End();

        markerAccept.Begin();

        while (_running)
        {
            if (_micQueue.TryDequeue(out var data))
            {
                Interlocked.Increment(ref _diagDequeuedFrames);
                if (_recognizer.AcceptWaveform(data, data.Length))
                {
                    _resultQueue.Enqueue(_recognizer.Result());
                    Interlocked.Increment(ref _diagFinalJsonCount);
                }
                else
                {
                    var partial = _recognizer.PartialResult();
                    if (!string.IsNullOrEmpty(partial))
                    {
                        _resultQueue.Enqueue(partial);
                        Interlocked.Increment(ref _diagPartialJsonCount);
                    }
                }
            } else
            {
                // パーシャル結果を確認
                var partial = _recognizer.PartialResult();
                if (!string.IsNullOrEmpty(partial))
                {
                    _resultQueue.Enqueue(partial);
                    Interlocked.Increment(ref _diagPartialJsonCount);
                }
                await Task.Delay(20);
            }
        }

        markerAccept.End();
        if (enableDiagnostics)
        {
            Debug.Log($"[STT][diag] ProcessAudioLoop end running={_running} queue={_micQueue.Count}");
        }
    }

    private void UpdateGrammar()
    {
        if (KeyPhrases.Count == 0) { _grammar = string.Empty; return; }
        var list = new List<string>(KeyPhrases.Count + 1);
        foreach (var p in KeyPhrases) list.Add(p.ToLower());
        list.Add("[unk]");
        _grammar = JsonConvert.SerializeObject(list);
    }

    private void Update()
    {
        EmitDiagnosticsIfNeeded();

        int processed = 0;
        int maxPerFrame = Mathf.Max(1, MaxResultsPerFrame);
        while (processed < maxPerFrame && _resultQueue.TryDequeue(out var json))
        {
            var res = new RecognitionResult(json);
            processed++;
            if (string.IsNullOrEmpty(res.BestText))
            {
                continue;
            }

            if (res.Partial)
            {
                // partialログは抑制
            } else
            {
                _accumulatedText = string.IsNullOrEmpty(_accumulatedText)
                    ? res.BestText
                    : ($"{_accumulatedText} {res.BestText}").Trim();
                _silenceTimer = 0f; // 話し続けているのでリセット

                if (AutoSendCharThreshold > 0 && _accumulatedText.Length >= AutoSendCharThreshold)
                {
                    EmitAccumulatedText("length");
                }
            }
        }

        // VAD: 無音が続いたら送信
        if (!string.IsNullOrEmpty(_accumulatedText))
        {
            _silenceTimer += Time.deltaTime;
            if (_silenceTimer >= VadSilenceSeconds)
            {
                EmitAccumulatedText("vad");
            }
        }
    }

    private void EmitAccumulatedText(string reason)
    {
        if (string.IsNullOrEmpty(_accumulatedText))
        {
            return;
        }

        Debug.Log($"[STT][send:{reason}] len={_accumulatedText.Length} text={_accumulatedText}");
        OnSpeechRecognized?.Invoke(_accumulatedText);
        Interlocked.Increment(ref _diagSentCount);
        _accumulatedText = string.Empty;
        _silenceTimer = 0f;
    }

    private void EmitDiagnosticsIfNeeded()
    {
        if (!enableDiagnostics)
        {
            return;
        }

        if (Time.unscaledTime < _nextDiagnosticsLogTime)
        {
            return;
        }

        _nextDiagnosticsLogTime = Time.unscaledTime + Mathf.Max(1f, diagnosticsLogIntervalSeconds);

        bool recording = VoiceProcessor != null && VoiceProcessor.IsRecording;
        int frames = Interlocked.Exchange(ref _diagDequeuedFrames, 0);
        int finals = Interlocked.Exchange(ref _diagFinalJsonCount, 0);
        int partials = Interlocked.Exchange(ref _diagPartialJsonCount, 0);
        int sent = Interlocked.Exchange(ref _diagSentCount, 0);

        Debug.Log($"[STT][diag] rec={recording} run={_running} init={_didInit} micQ={_micQueue.Count} resultQ={_resultQueue.Count} frames={frames} finals={finals} partials={partials} sent={sent} vad={VadSilenceSeconds:0.00} silence={_silenceTimer:0.00}");
    }
}