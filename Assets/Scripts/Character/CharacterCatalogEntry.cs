/// <summary>
/// CharacterCatalog がランタイムで保持するキャラクターエントリ。
/// </summary>
public class CharacterCatalogEntry
{
    public int CharacterId { get; }
    public string DisplayName { get; }
    /// <summary>AssetBundle ファイルの絶対パス。</summary>
    public string BundlePath { get; }
    /// <summary>AssetBundle 内のプレハブ名。</summary>
    public string PrefabName { get; }
    /// <summary>StreamingAssets 同梱キャラクターの場合 true。persistentDataPath 追加キャラクターは false。</summary>
    public bool IsBuiltIn { get; }

    public CharacterCatalogEntry(CharacterBundleMeta meta, string bundlePath, bool isBuiltIn)
    {
        CharacterId = meta.characterId;
        DisplayName = string.IsNullOrWhiteSpace(meta.displayName) ? meta.prefabName : meta.displayName;
        BundlePath = bundlePath;
        PrefabName = meta.prefabName;
        IsBuiltIn = isBuiltIn;
    }
}
