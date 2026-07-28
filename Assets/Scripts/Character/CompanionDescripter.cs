using UnityEngine;

/// <summary>
/// キャラクタープレハブのルートにアタッチするメタデータコンポーネント。
/// 情報が不足・未設定でもエラーによって実行が停止しないよう、
/// 各プロパティは必ずデフォルト値を返す。
/// </summary>
public class CompanionDescripter : MonoBehaviour
{
    [SerializeField] private string characterName = "";

    /// <summary>
    /// optionVal に直接対応するキャラクター識別子。
    /// SDK 作成時に自動割り当てされることを想定。現在は手動設定。
    /// </summary>
    [SerializeField] private int characterId = 0;

    // サムネイル: 後から実装するためコメントアウト保持
    // [SerializeField] private Texture2D thumbnail;

    /// <summary>
    /// キャラクターの表示名。未設定の場合は GameObject 名を返す。
    /// </summary>
    public string CharacterName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(characterName)) return characterName;
            // GameObject 名を安全に取得（破棄済みオブジェクトでも例外にしない）
            try { return gameObject != null ? gameObject.name : string.Empty; }
            catch { return string.Empty; }
        }
    }

    /// <summary>
    /// optionVal として使用するキャラクター識別子。
    /// </summary>
    public int CharacterId => characterId;

    // サムネイル: 後から実装
    // public Texture2D Thumbnail => thumbnail;
}
