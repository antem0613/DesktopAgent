using UnityEditor;
using UnityEngine;

namespace uDesktopMascot.Editor
{
    /// <summary>
    ///    メニューアイコンのインポート時に設定を変更するためのクラス
    /// </summary>
    public class MenuIconPostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            // 対象のパスを指定
            if (assetPath.StartsWith("Assets/uDesktopMascot/Images/UI/MenuIcon"))
            {
                TextureImporter importer = (TextureImporter)assetImporter;

                // スプライトモードをSingleに設定
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;

                // 色空間を設定（プロジェクトに合わせて変更してください）
                importer.sRGBTexture = true;

                // アルファチャンネルを透明度として扱う
                importer.alphaIsTransparency = true;

                // MipMapを無効化（アイコンやUIの場合は不要）
                importer.mipmapEnabled = false;

                // フィルターモードを設定（必要に応じて変更）
                importer.filterMode = FilterMode.Bilinear;

                // テクスチャ圧縮を無圧縮に
                importer.textureCompression = TextureImporterCompression.Uncompressed;

                // ピクセルパーエニットを設定（画像の解像度に合わせて）
                importer.spritePixelsPerUnit = 100f;
                
                // Wrap Modeを設定（アイコンの場合はClampが一般的）
                importer.wrapMode = TextureWrapMode.Clamp;
                
                // テクスチャの解像度を取得
                importer.GetSourceTextureWidthAndHeight(out var width, out var height);

                // 最大の辺の長さを取得
                int maxDimension = Mathf.Max(width, height);

                // Max Size を設定
                importer.maxTextureSize = Mathf.NextPowerOfTwo(maxDimension);

                // プラットフォームごとの設定を変更（必要に応じて）
                // 例：iOSとAndroidでの圧縮形式を設定
                SetPlatformTextureSettings(importer, "iPhone");
                SetPlatformTextureSettings(importer, "Android");
            }
        }

        /// <summary>
        ///    プラットフォームごとの設定を変更
        /// </summary>
        /// <param name="importer"></param>
        /// <param name="platform"></param>
        private void SetPlatformTextureSettings(TextureImporter importer, string platform)
        {
            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
            settings.overridden = true;

            // 圧縮形式を設定（必要に応じて変更）
            settings.format = TextureImporterFormat.RGBA32;
            settings.textureCompression = TextureImporterCompression.Uncompressed;
            settings.resizeAlgorithm = TextureResizeAlgorithm.Bilinear;

            importer.SetPlatformTextureSettings(settings);
        }
    }
}