using UnityEngine;
using UniVRM10;
using uLipSync;

/// <summary>
///   キャラクターモデル
/// </summary>
public class CharacterModel
{
    /// <summary>
    ///   現在のmodelContainer
    /// </summary>
    public GameObject CurrentModelContainer { get; set; }

    /// <summary>
    ///  現在のVRM情報
    /// </summary>
    public LoadedVRMInfo CurrentVrmInfo { get; set; }

    public Vrm10Instance vrmInstance { get; set; }

    public uLipSyncBlendShape lipSync { get; set; }

    /// <summary>
    /// モデルのアニメーター
    /// </summary>
    public Animator ModelAnimator { get; set; }

    /// <summary>
    /// 初期化済みかどうか
    /// </summary>
    public bool IsInitialized { get; set; } = false;

    /// <summary>
    /// モデルがロード済みかどうか
    /// </summary>
    /// <returns></returns>
    public bool IsModelLoaded { get; set; } = false;

}