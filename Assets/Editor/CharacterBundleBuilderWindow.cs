using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// キャラクター AssetBundle を一括ビルドして StreamingAssets/Characters/ に配置する Editor ツール。
/// メニュー: Tools/DesktopAgent/Build Character Bundles
/// </summary>
public class CharacterBundleBuilderWindow : EditorWindow
{
    [MenuItem("Tools/DesktopAgent/Build Character Bundles")]
    public static void BuildCharacterBundles()
    {
        // Assets/Resources 以下の CompanionDescripter 付きプレハブを収集する
        var prefabInfos = CollectCharacterPrefabs();
        if (prefabInfos.Count == 0)
        {
            Debug.LogWarning("[CharacterBundleBuilder] No prefabs with CompanionDescripter found in Assets/Resources.");
            EditorUtility.DisplayDialog(
                "Build Character Bundles",
                "Assets/Resources 以下に CompanionDescripter を持つプレハブが見つかりませんでした。",
                "OK");
            return;
        }

        Debug.Log($"[CharacterBundleBuilder] Building {prefabInfos.Count} character bundle(s)...");

        // ビルド出力先 (プロジェクトルート/Temp/CharacterBundles)
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string tempDir = Path.Combine(projectRoot, "Temp", "CharacterBundles");
        Directory.CreateDirectory(tempDir);

        // AssetBundleBuild 配列を構築する（各キャラクターに固有のバンドル名を使用）
        var builds = new AssetBundleBuild[prefabInfos.Count];
        for (int i = 0; i < prefabInfos.Count; i++)
        {
            var info = prefabInfos[i];
            // ファイル名衝突を避けるため prefab 名を含める（最終的には character.bundle にリネームする）
            builds[i] = new AssetBundleBuild
            {
                assetBundleName = $"character_{info.Prefab.name.ToLowerInvariant()}.bundle",
                assetNames = new[] { info.AssetPath },
            };
        }

        // 一括ビルド（複数回に分けるより 1 回の方が大幅に高速）
        var manifest = BuildPipeline.BuildAssetBundles(
            tempDir,
            builds,
            BuildAssetBundleOptions.None,
            BuildTarget.StandaloneWindows64);

        if (manifest == null)
        {
            Debug.LogError("[CharacterBundleBuilder] BuildPipeline.BuildAssetBundles returned null. Build failed.");
            EditorUtility.DisplayDialog("Build Character Bundles", "ビルドに失敗しました。Console を確認してください。", "OK");
            return;
        }

        // StreamingAssets/Characters/ に各バンドルをコピーする
        string streamingCharsDir = Path.Combine(Application.dataPath, "StreamingAssets", Constant.CharactersBundleDirectoryName);
        Directory.CreateDirectory(streamingCharsDir);

        int successCount = 0;
        var indexEntries = new List<CharacterBundleMeta>();

        foreach (var info in prefabInfos)
        {
            string bundleSrcName = $"character_{info.Prefab.name.ToLowerInvariant()}.bundle";
            string bundleSrcPath = Path.Combine(tempDir, bundleSrcName);

            if (!File.Exists(bundleSrcPath))
            {
                Debug.LogWarning($"[CharacterBundleBuilder] Bundle not found after build: {bundleSrcPath}");
                continue;
            }

            // 出力ディレクトリ: StreamingAssets/Characters/{prefabName}/
            string charOutputDir = Path.Combine(streamingCharsDir, info.Prefab.name);
            Directory.CreateDirectory(charOutputDir);

            // bundle コピー（character.bundle に統一）
            File.Copy(bundleSrcPath, Path.Combine(charOutputDir, "character.bundle"), overwrite: true);

            // index.json に追加するエントリを蓄積する
            indexEntries.Add(new CharacterBundleMeta
            {
                schemaVersion = "1.0",
                characterId = info.Descriptor.CharacterId,
                displayName = info.Descriptor.CharacterName,
                folder = info.Prefab.name,
                bundleFileName = "character.bundle",
                prefabName = info.Prefab.name,
                callName = info.Descriptor.CallName,
                ttsEngine = info.Descriptor.Voice.engine,
                ttsSpeakerId = info.Descriptor.Voice.speakerId,
                ttsStyleId = info.Descriptor.Voice.styleId,
            });

            Debug.Log($"[CharacterBundleBuilder] Built: {info.Prefab.name} (id={info.Descriptor.CharacterId})");
            successCount++;
        }

        // 全キャラクターをまとめた index.json を Characters/ 直下に生成する
        if (indexEntries.Count > 0)
        {
            var indexFile = new CharacterIndexFile
            {
                schemaVersion = "1.0",
                characters = indexEntries.ToArray(),
            };
            string indexJson = JsonUtility.ToJson(indexFile, prettyPrint: true);
            File.WriteAllText(Path.Combine(streamingCharsDir, "index.json"), indexJson);
            Debug.Log($"[CharacterBundleBuilder] index.json generated with {indexEntries.Count} entry(ies).");
        }

        AssetDatabase.Refresh();
        Debug.Log($"[CharacterBundleBuilder] Done. {successCount}/{prefabInfos.Count} character(s) built.");
        EditorUtility.DisplayDialog(
            "Build Character Bundles",
            $"{successCount} 個のキャラクターバンドルを StreamingAssets/Characters/ に出力しました。\nindex.json を更新しました。",
            "OK");
    }

    // ─────────────────────────────────────────────────────

    private static List<CharacterPrefabInfo> CollectCharacterPrefabs()
    {
        var result = new List<CharacterPrefabInfo>();
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources" });

        foreach (var guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null) continue;

            var desc = prefab.GetComponent<CompanionDescripter>();
            if (desc == null) continue;

            result.Add(new CharacterPrefabInfo(prefab, assetPath, desc));
        }

        return result;
    }

    private readonly struct CharacterPrefabInfo
    {
        public readonly GameObject Prefab;
        public readonly string AssetPath;
        public readonly CompanionDescripter Descriptor;

        public CharacterPrefabInfo(GameObject prefab, string assetPath, CompanionDescripter descriptor)
        {
            Prefab = prefab;
            AssetPath = assetPath;
            Descriptor = descriptor;
        }
    }
}
