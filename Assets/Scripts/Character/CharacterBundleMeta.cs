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
}
