using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Logging;
using UnityEngine;

/// <summary>
/// キャラクター AssetBundle の非同期ロードを担当する。
/// </summary>
public static class CharacterBundleLoader
{
    private const string LogPrefix = "[CharacterBundleLoader]";

    /// <summary>
    /// カタログエントリからバンドルをロードしてプレハブを返す。
    /// </summary>
    public static UniTask<(GameObject prefab, AssetBundle bundle)> LoadPrefabAsync(
        CharacterCatalogEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (entry == null)
        {
            Log.Warning($"{LogPrefix} entry is null.");
            return new UniTask<(GameObject, AssetBundle)>((null, null));
        }
        return LoadPrefabAsync(entry.BundlePath, entry.PrefabName, cancellationToken);
    }

    /// <summary>
    /// バンドルパスとプレハブ名を直接指定してロードする。
    /// キャラクタープロセスが ProcessIdentifier から受け取った値をそのまま渡すことができる。
    /// 呼び出し元はバンドルをアンロードする責任を持つ。失敗時は (null, null) を返す。
    /// </summary>
    public static async UniTask<(GameObject prefab, AssetBundle bundle)> LoadPrefabAsync(
        string bundlePath,
        string prefabName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bundlePath) || string.IsNullOrWhiteSpace(prefabName))
        {
            Log.Warning($"{LogPrefix} bundlePath or prefabName is empty.");
            return (null, null);
        }

        Log.Info($"{LogPrefix} Loading bundle: {bundlePath}");

        AssetBundle bundle = null;
        try
        {
            var bundleRequest = AssetBundle.LoadFromFileAsync(bundlePath);
            bundle = await bundleRequest.ToUniTask(cancellationToken: cancellationToken);

            if (bundle == null)
            {
                Log.Warning($"{LogPrefix} AssetBundle is null after load: {bundlePath}");
                return (null, null);
            }

            Log.Info($"{LogPrefix} Bundle loaded. Loading prefab: {prefabName}");

            var assetRequest = bundle.LoadAssetAsync<GameObject>(prefabName);
            var prefab = (GameObject)await assetRequest.ToUniTask(cancellationToken: cancellationToken);

            if (prefab == null)
            {
                Log.Warning($"{LogPrefix} Prefab '{prefabName}' not found in bundle: {bundlePath}");
                bundle.Unload(false);
                return (null, null);
            }

            Log.Info($"{LogPrefix} Prefab loaded: {prefabName}");
            return (prefab, bundle);
        }
        catch (System.OperationCanceledException)
        {
            bundle?.Unload(false);
            Log.Info($"{LogPrefix} Load cancelled: {prefabName}");
            return (null, null);
        }
        catch (System.Exception ex)
        {
            bundle?.Unload(false);
            Log.Warning($"{LogPrefix} Load failed: {bundlePath}. {ex.Message}");
            return (null, null);
        }
    }
}

