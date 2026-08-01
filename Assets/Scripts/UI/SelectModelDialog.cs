using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using TMPro;
using Unity.Logging;
using UnityEngine.UI;

// サムネイル取得用 VRM メタロード: 後から実装するためコメントアウト保持
// using System.IO;
// using SFB;

/// <summary>
/// キャラクター選択ダイアログ。
/// Resources/Characters/ 以下の CompanionDescripter 付きプレハブを列挙して表示する。
/// </summary>
public class SelectModelDialog : DialogBase
{
    private const string LogPrefix = "[SelectModelDialog]";

    [SerializeField] private ModelInfo modelInfoPrefab;
    [SerializeField] private Transform contentTransform;

    // SDK インポート UI: 後から実装するためコメントアウト保持
    [SerializeField] private TMP_InputField addModelPathInputField;
    [SerializeField] private Button addModelButton;
    [SerializeField] private Button openModelPathButton;

    private ModelInfo _currentModel;
    private CancellationTokenSource _cancellationTokenSource;

    private protected override void Awake()
    {
        base.Awake();
        _cancellationTokenSource = new CancellationTokenSource();
    }

    private async void Start()
    {
        AddDefaultModelEntry();
        await LoadCharacterListAsync();

        // SDK インポートボタン: 後から実装するためスタブを登録
        if (addModelButton != null) addModelButton.onClick.AddListener(OnAddModelButtonClicked);
        if (openModelPathButton != null) openModelPathButton.onClick.AddListener(OnOpenPathButtonClicked);
    }

    /// <summary>
    /// CharacterCatalog から id=0 のデフォルトキャラクターをリスト先頭に表示する。
    /// </summary>
    private void AddDefaultModelEntry()
    {
        var entry = CharacterCatalog.FindById(0);
        if (entry == null)
        {
            Log.Warning($"{LogPrefix} Default character (id=0) not found in catalog. Skipping default entry.");
            return;
        }

        // サムネイル: 後から実装
        // Texture2D thumbnail = null;

        var item = Instantiate(modelInfoPrefab, contentTransform);
        int capturedId = entry.CharacterId;
        item.Initialize(entry.DisplayName, null, () => OnModelSelected(item, capturedId));
        _currentModel = item;
        _currentModel.SetSelected(true);
    }

    /// <summary>
    /// CharacterCatalog の全エントリを列挙してリストに追加する。id=0 は AddDefaultModelEntry で追加済みのためスキップする。
    /// ユーザー追加バンドルの非同期スキャンもここで行う（メインプロセスのみ）。
    /// </summary>
    private async UniTask LoadCharacterListAsync()
    {
        // ユーザー追加バンドルを非同期で検出・初期化する（meta.json がなければ bundle から自動生成）
        await CharacterCatalog.InitializeUserBundlesAsync(_cancellationTokenSource.Token);

        var entries = CharacterCatalog.Entries;

        if (entries == null || entries.Count == 0) return;

        foreach (var entry in entries)
        {
            // characterId=0 は AddDefaultModelEntry で追加済み
            if (entry.CharacterId == 0) continue;

            // サムネイル: 後から実装
            // Texture2D thumbnail = null;

            var item = Instantiate(modelInfoPrefab, contentTransform);
            int capturedId = entry.CharacterId;
            item.Initialize(entry.DisplayName, null, () => OnModelSelected(item, capturedId));

            // サムネイル更新: 後から実装
            // item.UpdateModelInfo(entry.DisplayName, thumbnail);

            await UniTask.Yield();
        }
    }

    /// <summary>
    /// モデルが選択されたときの処理。キャラクタープロセスを新規起動する。
    /// </summary>
    private void OnModelSelected(ModelInfo modelInfo, int characterId)
    {
        if (modelInfo == _currentModel) return;

        modelInfo.SetSelected(true);
        _currentModel?.SetSelected(false);
        _currentModel = modelInfo;

        var controller = CompanionController.Instance;
        if (controller == null)
        {
            Log.Warning($"{LogPrefix} CompanionController not available. Cannot spawn character.");
            return;
        }

        bool success = controller.SpawnCharacter(characterId);
        if (!success)
        {
            Log.Warning($"{LogPrefix} Failed to spawn character: id={characterId}");
        }
    }

    /// <summary>
    /// モデル追加ボタン。SDK 未実装のためスタブ。
    /// </summary>
    private void OnAddModelButtonClicked()
    {
        // TODO: SDK による外部キャラクターインポート実装時に対応
        Log.Info($"{LogPrefix} Add model: not yet implemented (pending SDK).");
    }

    /// <summary>
    /// ファイルブラウザを開くボタン。SDK 未実装のためスタブ。
    /// </summary>
    private void OnOpenPathButtonClicked()
    {
        // TODO: SDK による外部キャラクターインポート実装時に対応
        Log.Info($"{LogPrefix} Open file browser: not yet implemented (pending SDK).");
    }

    // --- 以下: VRM ファイルベースの旧実装（サムネイル等の参照のためコメントアウト保持） ---

    // private async UniTask AddModel(string filePath)
    // {
    //     var loadMetaTask = LoadVRM.LoadVrmMetaAsync(filePath);
    //     await UniTask.SwitchToMainThread();
    //     var item = Instantiate(modelInfoPrefab, contentTransform);
    //     item.Initialize("モデル情報を取得中...", null, () => OnModelSelectedLegacy(item, filePath).Forget());
    //     var (modelName, thumbnail) = await loadMetaTask;
    //     await UniTask.SwitchToMainThread();
    //     item.UpdateModelInfo(modelName, thumbnail);
    //     await UniTask.Yield();
    // }

    // private async UniTaskVoid OnModelSelectedLegacy(ModelInfo modelInfo, string path = null)
    // {
    //     if (modelInfo == _currentModel) return;
    //     modelInfo.SetSelected(true);
    //     _currentModel?.SetSelected(false);
    //     _currentModel = modelInfo;
    //     LoadedVRMInfo model;
    //     if (path == null)
    //     {
    //         var defaultModel = await LoadVRM.LoadDefaultModel();
    //         model = new LoadedVRMInfo(defaultModel.gameObject, defaultModel.Vrm.Meta.Name, defaultModel.Vrm.Meta.Thumbnail);
    //     }
    //     else
    //     {
    //         model = await LoadVRM.LoadModelAsync(path, _cancellationTokenSource.Token);
    //     }
    //     if (model != null)
    //     {
    //         Log.Debug("Model loaded: " + model.Model.name);
    //         //CharacterManager.Instance.OnModelLoaded(model.Model, true);
    //     }
    //     else
    //     {
    //         Log.Error($"Failed to load Model: {path}");
    //     }
    // }

    // private void OpenFileBrowserLegacy()
    // {
    //     var extensions = new[]
    //     {
    //         new ExtensionFilter("VRM Files", "vrm"),
    //         new ExtensionFilter("All Files", "*"),
    //     };
    //     StandaloneFileBrowser.OpenFilePanelAsync("Open VRM File", "", extensions, false, (string[] paths) =>
    //     {
    //         if (paths.Length > 0) addModelPathInputField.text = paths[0];
    //     });
    // }

    private void OnDestroy()
    {
        if (openModelPathButton != null) openModelPathButton.onClick.RemoveAllListeners();
        if (addModelButton != null) addModelButton.onClick.RemoveAllListeners();
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }
}