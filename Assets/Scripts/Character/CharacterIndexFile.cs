using System;

/// <summary>
/// StreamingAssets/Characters/index.json および persistentDataPath/Characters/index.json の
/// トップレベル構造を表す。全キャラクターエントリを一括管理する。
/// </summary>
[Serializable]
public class CharacterIndexFile
{
    public string schemaVersion = "1.0";
    public CharacterBundleMeta[] characters;
}
