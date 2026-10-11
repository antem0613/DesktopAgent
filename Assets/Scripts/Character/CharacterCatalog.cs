using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Logging;
using UnityEngine;

/// <summary>
/// キャラクターバンドルカタログ。
/// ・同期初期化: StreamingAssets/Characters/index.json を読む（メイン・キャラクター両プロセスで安全）
/// ・非同期初期化: persistentDataPath/Characters/ をスキャンしユーザー追加キャラクターを検出する
///   （メインプロセスのみ呼び出すこと。meta.json がない場合はバンドルからメタ情報を読み取り自動生成する）
/// </summary>
public static class CharacterCatalog
{
    private const string LogPrefix = "[CharacterCatalog]";
    private const string IndexFileName = "index.json";
    private const string DefaultBundleFileName = "character.bundle";

    private static List<CharacterCatalogEntry> _entries;
    private static bool _userBundlesInitialized;

    // ─── Public API ─────────────────────────────────────────────────────────────────

    /// <summary>カタログ全エントリ（characterId 昇順）。未初期化の場合は同期初期化してから返す。</summary>
    public static IReadOnlyList<CharacterCatalogEntry> Entries
    {
        get
        {
            EnsureInitialized();
            return _entries;
        }
    }

    /// <summary>characterId で検索する。見つからない場合は null。</summary>
    public static CharacterCatalogEntry FindById(int characterId)
    {
        EnsureInitialized();
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].CharacterId == characterId) return _entries[i];
        }
        return null;
    }

    /// <summary>
    /// persistentDataPath/Characters/ をスキャンしてユーザー追加キャラクターをカタログに追加する。
    /// 未登録のバンドルを検出した場合は一度ロードして CompanionDescripter からメタを取得し index.json を更新する。
    /// メインプロセスのみから呼び出すこと。
    /// </summary>
    public static async UniTask InitializeUserBundlesAsync(CancellationToken cancellationToken = default)
    {
        if (_userBundlesInitialized) return;
        _userBundlesInitialized = true;

        EnsureInitialized();

        string persistentPath = Path.Combine(Application.persistentDataPath, Constant.CharactersBundleDirectoryName);
        if (!Directory.Exists(persistentPath))
        {
            Log.Info($"{LogPrefix} ユーザーキャラクターディレクトリが存在しません: {persistentPath}");
            return;
        }

        // persistentDataPath/Characters/index.json を読み込む
        string indexPath = Path.Combine(persistentPath, IndexFileName);
        var existingIndex = TryReadUserIndexFile(indexPath);
        var indexedEntries = existingIndex?.characters != null
            ? new System.Collections.Generic.List<CharacterBundleMeta>(existingIndex.characters)
            : new System.Collections.Generic.List<CharacterBundleMeta>();

        // index 登録済みのフォルダ名セット
        var indexedFolders = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in indexedEntries)
            if (!string.IsNullOrWhiteSpace(m.folder)) indexedFolders.Add(m.folder);

        string[] subdirs;
        try { subdirs = Directory.GetDirectories(persistentPath); }
        catch (Exception ex)
        {
            Log.Warning($"{LogPrefix} ディレクトリの列挙に失敗: {ex.Message}");
            subdirs = Array.Empty<string>();
        }

        // 未登録の bundle を検出して index を更新する
        bool indexUpdated = false;
        foreach (var dir in subdirs)
        {
            if (cancellationToken.IsCancellationRequested) break;

            string folderName = Path.GetFileName(dir);
            if (indexedFolders.Contains(folderName)) continue;  // 登録済み

            string bundlePath = FindBundleFileInDirectory(dir);
            if (bundlePath == null) continue;

            Log.Info($"{LogPrefix} 未登録のバンドルを検出: {bundlePath}");
            var meta = await ReadMetaFromBundleAsync(bundlePath, dir, cancellationToken);
            if (meta == null) continue;

            // 実際に見つかったファイル名をそのまま記録する（character.bundle へのリネームを不要にする）
            meta.bundleFileName = Path.GetFileName(bundlePath);

            indexedEntries.Add(meta);
            indexedFolders.Add(folderName);
            indexUpdated = true;
        }

        // 更新があれば index.json に書き込む
        if (indexUpdated)
        {
            TryWriteUserIndexFile(indexPath, new CharacterIndexFile
            {
                schemaVersion = "1.0",
                characters = indexedEntries.ToArray()
            });
        }

        // index 全エントリをカタログに登録する
        foreach (var meta in indexedEntries)
        {
            if (string.IsNullOrWhiteSpace(meta.folder) || string.IsNullOrWhiteSpace(meta.prefabName)) continue;

            string bundleFileName = string.IsNullOrWhiteSpace(meta.bundleFileName) ? DefaultBundleFileName : meta.bundleFileName;
            string bundlePath = Path.Combine(persistentPath, meta.folder, bundleFileName);
            if (!File.Exists(bundlePath)) continue;  // 削除済みバンドルはスキップ

            RegisterEntry(new CharacterCatalogEntry(meta, bundlePath, isBuiltIn: false));
        }

        _entries.Sort((a, b) => a.CharacterId.CompareTo(b.CharacterId));
        Log.Info($"{LogPrefix} ユーザーバンドル初期化完了。総エントリ数: {_entries.Count}");
    }

    /// <summary>キャッシュを破棄して次回アクセス時に再スキャンする。</summary>
    public static void Invalidate()
    {
        _entries = null;
        _userBundlesInitialized = false;
    }

    // ─── Sync init (StreamingAssets index.json) ────────────────────────────────────────────

    private static void EnsureInitialized()
    {
        if (_entries != null) return;

        _entries = new List<CharacterCatalogEntry>();

        string streamingPath = Path.Combine(Application.streamingAssetsPath, Constant.CharactersBundleDirectoryName);
        ScanIndexFile(streamingPath, isBuiltIn: true);

        _entries.Sort((a, b) => a.CharacterId.CompareTo(b.CharacterId));
        Log.Info($"{LogPrefix} 同期初期化完了。組み込みキャラクター数: {_entries.Count}");
    }

    private static void ScanIndexFile(string rootPath, bool isBuiltIn)
    {
        if (!Directory.Exists(rootPath)) return;

        string indexPath = Path.Combine(rootPath, IndexFileName);
        if (!File.Exists(indexPath))
        {
            Log.Info($"{LogPrefix} index.json が見つかりません: {indexPath}");
            return;
        }

        try
        {
            string json = File.ReadAllText(indexPath);
            var index = JsonUtility.FromJson<CharacterIndexFile>(json);

            if (index?.characters == null || index.characters.Length == 0)
            {
                Log.Warning($"{LogPrefix} index.json が空またはフォーマット不正: {indexPath}");
                return;
            }

            foreach (var meta in index.characters)
                TryRegisterEntry(meta, rootPath, isBuiltIn);
        }
        catch (Exception ex)
        {
            Log.Warning($"{LogPrefix} index.json の読み込みに失敗: {indexPath}. {ex.Message}");
        }
    }

    private static void TryRegisterEntry(CharacterBundleMeta meta, string rootPath, bool isBuiltIn)
    {
        if (meta == null
            || string.IsNullOrWhiteSpace(meta.folder)
            || string.IsNullOrWhiteSpace(meta.prefabName))
        {
            Log.Warning($"{LogPrefix} folder または prefabName が欠落しているエントリをスキップ。");
            return;
        }

        string bundleFileName = string.IsNullOrWhiteSpace(meta.bundleFileName)
            ? DefaultBundleFileName : meta.bundleFileName;

        string bundlePath = Path.Combine(rootPath, meta.folder, bundleFileName);
        if (!File.Exists(bundlePath))
        {
            Log.Warning($"{LogPrefix} バンドルファイルが見つかりません: {bundlePath}");
            return;
        }

        RegisterEntry(new CharacterCatalogEntry(meta, bundlePath, isBuiltIn));
    }

    private static void RegisterEntry(CharacterCatalogEntry entry)
    {
        int existingIndex = _entries.FindIndex(e => e.CharacterId == entry.CharacterId);
        if (existingIndex >= 0)
        {
            if (!entry.IsBuiltIn)
            {
                _entries[existingIndex] = entry;
                Log.Info($"{LogPrefix} ユーザーキャラクターで同一 id を上書き: id={entry.CharacterId}");
            }
        }
        else
        {
            _entries.Add(entry);
        }
        Log.Info($"{LogPrefix} 登録: id={entry.CharacterId}, name={entry.DisplayName}, builtIn={entry.IsBuiltIn}");
    }

    // ─── Async user bundle scan (persistentDataPath) ──────────────────────────────────────────

    /// <summary>
    /// キャラクターフォルダ内のバンドルファイルを検出する。
    /// 既定のファイル名（character.bundle）を優先しつつ、任意のファイル名の .bundle ファイルも
    /// そのまま検出することで、エクスポート後のリネームを不要にする。
    /// </summary>
    private static string FindBundleFileInDirectory(string dir)
    {
        string defaultPath = Path.Combine(dir, DefaultBundleFileName);
        if (File.Exists(defaultPath)) return defaultPath;

        try
        {
            var candidates = Directory.GetFiles(dir, "*.bundle");
            if (candidates.Length > 0) return candidates[0];
        }
        catch (Exception ex)
        {
            Log.Warning($"{LogPrefix} .bundle ファイルの検索に失敗: {dir}. {ex.Message}");
        }

        return null;
    }

    private static CharacterIndexFile TryReadUserIndexFile(string indexPath)
    {
        if (!File.Exists(indexPath)) return null;
        try
        {
            return JsonUtility.FromJson<CharacterIndexFile>(File.ReadAllText(indexPath));
        }
        catch (Exception ex)
        {
            Log.Warning($"{LogPrefix} index.json の読み込みに失敗: {indexPath}. {ex.Message}");
            return null;
        }
    }

    private static void TryWriteUserIndexFile(string indexPath, CharacterIndexFile index)
    {
        try
        {
            File.WriteAllText(indexPath, JsonUtility.ToJson(index, prettyPrint: true));
            Log.Info($"{LogPrefix} index.json を更新: {indexPath}");
        }
        catch (Exception ex)
        {
            Log.Warning($"{LogPrefix} index.json の書き込みに失敗: {indexPath}. {ex.Message}");
        }
    }

    /// <summary>
    /// バンドルを一時ロードして CompanionDescripter からキャラクターメタ情報を取得する。
    /// 取得後はバンドルを完全にアンロードする。
    /// </summary>
    private static async UniTask<CharacterBundleMeta> ReadMetaFromBundleAsync(
        string bundlePath, string dirPath, CancellationToken ct)
    {
        AssetBundle bundle = null;
        try
        {
            bundle = await AssetBundle.LoadFromFileAsync(bundlePath).ToUniTask(cancellationToken: ct);
            if (bundle == null)
            {
                Log.Warning($"{LogPrefix} バンドルのロードに失敗: {bundlePath}");
                return null;
            }

            foreach (var assetName in bundle.GetAllAssetNames())
            {
                if (!assetName.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;

                string nameWithoutExt = Path.GetFileNameWithoutExtension(assetName);
                var prefab = (GameObject)await bundle.LoadAssetAsync<GameObject>(nameWithoutExt)
                    .ToUniTask(cancellationToken: ct);

                if (prefab == null) continue;

                var desc = prefab.GetComponent<CompanionDescripter>();
                if (desc == null) continue;

                return new CharacterBundleMeta
                {
                    schemaVersion = "1.0",
                    characterId = desc.CharacterId,
                    displayName = desc.CharacterName,
                    folder = Path.GetFileName(dirPath),
                    bundleFileName = DefaultBundleFileName,
                    prefabName = nameWithoutExt,
                    callName = desc.CallName,
                    ttsEngine = desc.Voice.engine,
                    ttsSpeakerId = desc.Voice.speakerId,
                    ttsStyleId = desc.Voice.styleId,
                };
            }

            Log.Warning($"{LogPrefix} バンドル内に CompanionDescripter 付きプレハブが見つかりません: {bundlePath}");
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Warning($"{LogPrefix} バンドルからのメタ読み取りに失敗: {bundlePath}. {ex.Message}");
            return null;
        }
        finally
        {
            bundle?.Unload(true);
        }
    }
}

/// <summary>
