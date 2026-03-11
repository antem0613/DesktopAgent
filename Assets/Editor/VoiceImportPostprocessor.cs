using UnityEditor;
using UnityEngine;

namespace uDesktopMascot.Editor
{
    /// <summary>
    ///     ボイスファイルのインポート時に設定を変更するためのクラス
    /// </summary>
    public class VoiceImportPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessAudio()
        {
            // Voiceのインポート時に設定を変更
            OnPreprocessVoice();

            // BGMのインポート時に設定を変更
            OnPreprocessBGM();
        }

        /// <summary>
        ///     BGMファイルのインポート時に設定を変更する
        /// </summary>
        private void OnPreprocessBGM()
        {
            // ターゲットフォルダのパス
            var targetFolder = "Assets/uDesktopMascot/Resources/DefaultBGM/";

            // アセットが指定フォルダ内にある場合
            if (assetPath.Contains(targetFolder))
            {
                var audioImporter = (AudioImporter)assetImporter;

                // デフォルトのサンプル設定を取得
                var sampleSettings = audioImporter.defaultSampleSettings;

                // Load TypeをStreamingに設定
                sampleSettings.loadType = AudioClipLoadType.Streaming;
                // Compression FormatをVorbisに設定
                sampleSettings.compressionFormat = AudioCompressionFormat.Vorbis;
                // Qualityを70%に設定（0.0f - 1.0f）
                sampleSettings.quality = 0.7f;
                // Sample Rate SettingをPreserve Sample Rateに設定
                sampleSettings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                // Preload Audio Dataを有効に設定（Streamingの場合は無効でも可）
                sampleSettings.preloadAudioData = true;

                // 変更した設定を適用
                audioImporter.defaultSampleSettings = sampleSettings;

                // 必要に応じてプラットフォームごとの設定も変更可能
                // 以下の例では、すべてのプラットフォームで同じ設定を適用しています
                var platforms = new[] { "Standalone", "Android", "iOS" };
                foreach (var platform in platforms)
                {
                    audioImporter.SetOverrideSampleSettings(platform, sampleSettings);
                }
            }
        }

        /// <summary>
        ///     Voiceファイルのインポート時に設定を変更する
        /// </summary>
        private void OnPreprocessVoice()
        {
            // ターゲットフォルダのパス
            var targetFolder = "Assets/uDesktopMascot/Resources/DefaultVoice/";

            // アセットが指定フォルダ内にある場合
            if (assetPath.Contains(targetFolder))
            {
                var audioImporter = (AudioImporter)assetImporter;

                // デフォルトのサンプル設定を取得
                var sampleSettings = audioImporter.defaultSampleSettings;

                // Load TypeをCompressed In Memoryに設定
                sampleSettings.loadType = AudioClipLoadType.CompressedInMemory;
                // Compression FormatをADPCMに設定
                sampleSettings.compressionFormat = AudioCompressionFormat.ADPCM;
                // Preload Audio Dataを有効に設定
                sampleSettings.preloadAudioData = true;

                // 変更した設定を適用
                audioImporter.defaultSampleSettings = sampleSettings;

                // 必要に応じてプラットフォームごとの設定も変更可能
                // 以下の例では、すべてのプラットフォームで同じ設定を適用しています
                var platforms = new[] { "Standalone", "Android", "iOS" };
                foreach (var platform in platforms)
                {
                    audioImporter.SetOverrideSampleSettings(platform, sampleSettings);
                }
            }
        }
    }
}