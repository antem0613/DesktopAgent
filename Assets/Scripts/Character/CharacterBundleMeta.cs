using System;

/// <summary>
/// Characters/index.json 内の各キャラクターエントリを表す。
/// JSON シリアライズ可能な単純データクラス。
/// </summary>
[Serializable]
public class CharacterBundleMeta
{
    public string schemaVersion = "1.0";
    public int characterId;
    public string displayName;
    /// <summary>Characters/ ルート以下のサブフォルダ名。バンドルファイルの場所の解決に使用する。</summary>
    public string folder;
    public string bundleFileName = "character.bundle";
    /// <summary>AssetBundle 内のプレハブ名（拡張子なし）。</summary>
    public string prefabName;

    /// <summary>キャラクターの呼び名。未設定の場合は displayName を使用する。</summary>
    public string callName = "";
    /// <summary>キャラクターが使用する TTS エンジン種別。</summary>
    public CompanionDescripter.TtsEngine ttsEngine = CompanionDescripter.TtsEngine.Coeiroink;
    /// <summary>TTS の話者ID。</summary>
    public string ttsSpeakerId = "";
    /// <summary>TTS のスタイルID。</summary>
    public int ttsStyleId = 0;
}
