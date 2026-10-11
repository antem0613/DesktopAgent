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
    /// <summary>キャラクターの呼び名。未設定の場合は DisplayName を返す。</summary>
    public string CallName { get; }
    /// <summary>キャラクターが使用する TTS エンジン種別。</summary>
    public CompanionDescripter.TtsEngine TtsEngine { get; }
    /// <summary>TTS の話者ID。</summary>
    public string TtsSpeakerId { get; }
    /// <summary>TTS のスタイルID。</summary>
    public int TtsStyleId { get; }

    public CharacterCatalogEntry(CharacterBundleMeta meta, string bundlePath, bool isBuiltIn)
    {
        CharacterId = meta.characterId;
        DisplayName = string.IsNullOrWhiteSpace(meta.displayName) ? meta.prefabName : meta.displayName;
        BundlePath = bundlePath;
        PrefabName = meta.prefabName;
        IsBuiltIn = isBuiltIn;
        CallName = string.IsNullOrWhiteSpace(meta.callName) ? DisplayName : meta.callName;
        TtsEngine = meta.ttsEngine;
        TtsSpeakerId = meta.ttsSpeakerId ?? string.Empty;
        TtsStyleId = meta.ttsStyleId;
    }
}
