using System;
using UnityEngine;

/// <summary>
///   16kHz モノラルでマイクをキャプチャし、short[] のバッファをイベントで通知するユーティリティ。
/// </summary>
public class VoiceProcessor : MonoBehaviour
{
    public bool IsRecording { get; private set; }

    /// <summary>
    ///   キャプチャした PCM データ (16kHz, short) が 1 フレーム分用意されるたびに呼び出されます。
    /// </summary>
    public event Action<short[]> OnFrameCaptured;
    public event Action OnRecordingStop;

    private const int DesiredSampleRate = 16000;
    public int SampleRate { get; private set; } = DesiredSampleRate;
    private const int FrameSize = 1024; // サンプル数 (約64ms)

    private AudioClip _clip;
    private string _microphoneDevice;
    private int _lastReadPos;

    // 現在の録音デバイス名
    private int _selectedDeviceIndex = 0;
    /// <summary>
    /// 録音に使うマイクデバイス名を取得・設定
    /// </summary>
    public int SelectedDeviceIndex
    {
        get => _selectedDeviceIndex;
        set => _selectedDeviceIndex = value;
    }

    /// <summary>
    /// 録音デバイス名を明示的に指定して切り替える
    /// </summary>
    public void SetMicrophoneDevice(int index)
    {
        _selectedDeviceIndex = index;
        if (IsRecording)
        {
            StopRecording();
            StartRecording();
        }
    }

    public void StartRecording()
    {
        if (IsRecording) return;
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("[VoiceProcessor] No microphone devices found.");
            return;
        }

        string deviceToUse;

        if (_selectedDeviceIndex >= 0 && _selectedDeviceIndex < Microphone.devices.Length)
        {
            deviceToUse = Microphone.devices[_selectedDeviceIndex];
        } else
        {
            deviceToUse = Microphone.devices[0];
        }

        _microphoneDevice = deviceToUse;
        Debug.Log($"[VoiceProcessor] Try start recording: device='{_microphoneDevice}'");
        _clip = Microphone.Start(_microphoneDevice, true, 10, DesiredSampleRate);
        if (_clip != null)
        {
            SampleRate = _clip.frequency; // 実際に取得されたサンプルレート
            Debug.Log($"[VoiceProcessor] Microphone started. SampleRate={SampleRate}");
        } else
        {
            Debug.LogError($"[VoiceProcessor] Failed to start Microphone. device='{_microphoneDevice}' devices=({string.Join(", ", Microphone.devices)})");
            return;
        }

        _lastReadPos = 0;
        IsRecording = true;
    }

    public void StopRecording()
    {
        if (!IsRecording) return;

        Microphone.End(_microphoneDevice);
        IsRecording = false;
        _clip = null;
        OnRecordingStop?.Invoke();
    }

    private void Update()
    {
        if (!IsRecording || _clip == null) return;

        int currentPos = Microphone.GetPosition(_microphoneDevice);
        if (currentPos < _lastReadPos) currentPos += _clip.samples; // wrap

        int samplesAvailable = currentPos - _lastReadPos;
        while (samplesAvailable >= FrameSize)
        {
            float[] floatBuffer = new float[FrameSize];
            int clipPos = _lastReadPos % _clip.samples;
            _clip.GetData(floatBuffer, clipPos);

            short[] shortBuffer = new short[FrameSize];
            for (int i = 0; i < FrameSize; i++)
            {
                float sample = Mathf.Clamp(floatBuffer[i], -1f, 1f);
                shortBuffer[i] = (short)(sample * short.MaxValue);
            }

            _lastReadPos += FrameSize;
            samplesAvailable -= FrameSize;

            OnFrameCaptured?.Invoke(shortBuffer);
        }
    }
}