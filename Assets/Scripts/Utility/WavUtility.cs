using System;
using System.Text;
using UnityEngine;

public static class WavUtility
{
    public static bool TryCreateAudioClipFromWav(byte[] wavData, string clipName, out AudioClip clip, out string error)
    {
        clip = null;
        error = null;

        if (wavData == null || wavData.Length < 44)
        {
            error = "WAV data is null or too small.";
            return false;
        }

        if (!HasTag(wavData, 0, "RIFF") || !HasTag(wavData, 8, "WAVE"))
        {
            error = "Invalid RIFF/WAVE header.";
            return false;
        }

        int fmtChunkOffset = -1;
        int dataChunkOffset = -1;
        int dataChunkSize = 0;

        int offset = 12;
        while (offset + 8 <= wavData.Length)
        {
            string chunkId = Encoding.ASCII.GetString(wavData, offset, 4);
            int chunkSize = BitConverter.ToInt32(wavData, offset + 4);

            if (chunkId == "fmt ")
            {
                fmtChunkOffset = offset + 8;
            }
            else if (chunkId == "data")
            {
                dataChunkOffset = offset + 8;
                dataChunkSize = chunkSize;
                break;
            }

            offset += 8 + chunkSize;
            if (chunkSize % 2 == 1) offset += 1; // padding
        }

        if (fmtChunkOffset < 0 || dataChunkOffset < 0)
        {
            error = "fmt or data chunk not found.";
            return false;
        }

        short audioFormat = BitConverter.ToInt16(wavData, fmtChunkOffset + 0);
        short channels = BitConverter.ToInt16(wavData, fmtChunkOffset + 2);
        int sampleRate = BitConverter.ToInt32(wavData, fmtChunkOffset + 4);
        short bitsPerSample = BitConverter.ToInt16(wavData, fmtChunkOffset + 14);

        if (audioFormat != 1)
        {
            error = $"Unsupported WAV format: {audioFormat} (only PCM supported).";
            return false;
        }

        if (bitsPerSample != 16)
        {
            error = $"Unsupported bits per sample: {bitsPerSample} (only 16-bit supported).";
            return false;
        }

        if (dataChunkOffset + dataChunkSize > wavData.Length)
        {
            error = "WAV data chunk exceeds buffer length.";
            return false;
        }

        int sampleCount = dataChunkSize / 2;
        float[] samples = new float[sampleCount];
        int sampleIndex = 0;
        for (int i = 0; i < dataChunkSize; i += 2)
        {
            short sample = BitConverter.ToInt16(wavData, dataChunkOffset + i);
            samples[sampleIndex++] = sample / 32768f;
        }

        int lengthSamples = sampleCount / Mathf.Max(1, channels);
        clip = AudioClip.Create(clipName, lengthSamples, channels, sampleRate, false);
        clip.SetData(samples, 0);
        return true;
    }

    private static bool HasTag(byte[] data, int offset, string tag)
    {
        if (offset + 4 > data.Length) return false;
        return Encoding.ASCII.GetString(data, offset, 4) == tag;
    }
}
