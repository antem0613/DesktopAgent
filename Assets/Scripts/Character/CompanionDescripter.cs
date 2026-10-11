using UnityEngine;

/// <summary>
/// キャラクタープレハブのルートにアタッチするメタデータコンポーネント。
/// 情報が不足・未設定でもエラーによって実行が停止しないよう、
/// 各プロパティは必ずデフォルト値を返す。
///
/// 重要: このファイルは MatieCompanionSDK
/// (Packages/com.antem0613.matie-companionsdk/Runtime/CompanionDescripter.cs) と
/// DesktopAgent (Assets/Scripts/Character/CompanionDescripter.cs) の双方に
/// 同一内容で存在する。AssetBundle はスクリプト参照を GUID で解決するため、
/// 両ファイルの .meta に記録された guid (4b6655add0c17eb45895280eb64fe6b0) を
/// 必ず一致させ、フィールドを追加・変更する際は双方に同じ変更を適用すること。
/// </summary>
[RequireComponent(typeof(Animator))]
public class CompanionDescripter : MonoBehaviour
{
    /// <summary>キャラクターが使用する TTS エンジン種別。将来的に他エンジンを追加する想定。</summary>
    public enum TtsEngine
    {
        Coeiroink = 0,
    }

    /// <summary>キャラクターの TTS プロファイル（使用エンジンと話者設定）。</summary>
    [System.Serializable]
    public class TtsProfile
    {
        public TtsEngine engine = TtsEngine.Coeiroink;
        public string speakerId = string.Empty;
        public int styleId = 0;
    }

    [SerializeField] private string characterName = "";

    /// <summary>
    /// optionVal に直接対応するキャラクター識別子。
    /// SDK 作成時に自動割り当てされることを想定。現在は手動設定。
    /// </summary>
    [SerializeField] private int characterId = 0;

    /// <summary>キャラクターの呼び名（ユーザーから呼びかけられる際の名前）。</summary>
    [SerializeField] private string callName = "";

    /// <summary>キャラクターの性格設定プロンプト（LLM へのシステムプロンプト等に使用予定）。</summary>
    [SerializeField, TextArea(3, 10)] private string personalityPrompt = "";

    /// <summary>
    /// キャラクターのポートレート画像。
    /// エディタカメラからのキャプチャ、またはファイル読み込みで設定される。
    /// </summary>
    [SerializeField] private Texture2D portrait;

    /// <summary>キャラクターが使用する TTS プロファイル。</summary>
    [SerializeField] private TtsProfile ttsProfile = new TtsProfile();

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

    /// <summary>
    /// キャラクターの呼び名。未設定の場合は <see cref="CharacterName"/> を返す。
    /// </summary>
    public string CallName => string.IsNullOrWhiteSpace(callName) ? CharacterName : callName;

    /// <summary>
    /// キャラクターの性格設定プロンプト。未設定の場合は空文字を返す。
    /// </summary>
    public string PersonalityPrompt => personalityPrompt ?? string.Empty;

    /// <summary>
    /// キャラクターのポートレート画像。未設定の場合は null を返す。
    /// </summary>
    public Texture2D Portrait => portrait;

    /// <summary>
    /// キャラクターの TTS プロファイル。未設定の場合もデフォルトインスタンスを返す。
    /// </summary>
    public TtsProfile Voice => ttsProfile ?? new TtsProfile();
}
