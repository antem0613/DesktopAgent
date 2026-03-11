using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class TestCharacterSpawnComponent : MonoBehaviour, ITestFeatureComponent
{
    private const string LogTag = nameof(TestCharacterSpawnComponent);

    [SerializeField] private bool runTest = true;
    [SerializeField] private bool spawnOnStart = true;

    [Header("Model Source")]
    [SerializeField] private string overrideStreamingAssetsModelPath;

    [Header("Placement")]
    [SerializeField] private bool applyCharacterSettingsTransform = true;
    [SerializeField] private Vector3 additionalPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 additionalRotationOffsetEuler = Vector3.zero;

    [Header("Animator")]
    [SerializeField] private bool createAnimatorIfMissing = true;
    [SerializeField] private bool createGenericAvatarIfMissing = true;

    [Header("Startup Order")]
    [SerializeField] private bool waitForBackendBeforeSpawn = true;
    [SerializeField] private bool requestBackendStartupBeforeSpawn = true;
    [SerializeField] private bool requireBackendRunning = true;
    [SerializeField] private float backendWaitTimeoutSeconds = 6f;
    [SerializeField] private float backendPollIntervalSeconds = 0.1f;
    [SerializeField] private BackendServerStartupTestComponent backendStartupTestComponent;

    [Header("Startup Order / External UI")]
    [SerializeField] private bool waitForExternalUiBeforeSpawn = true;
    [SerializeField] private bool requireExternalUiRunning = true;
    [SerializeField] private string externalUiProcessName = "DesktopAgentUI";
    [SerializeField] private string externalUiExecutablePath;
    [SerializeField] private float externalUiWaitTimeoutSeconds = 1.5f;
    [SerializeField] private float externalUiPollIntervalSeconds = 0.1f;
    [SerializeField] private float externalUiReadyExtraDelaySeconds = 0.15f;

    private GameObject _modelContainer;
    private GameObject _spawnedModel;
    private Animator _modelAnimator;
    private Camera _targetCamera;
    private CancellationTokenSource _cancellationTokenSource;
    private bool _isSpawning;
    private LoadedVRMInfo _loadedVrmInfo;
    private bool _modelLoadFailedLogged;

    public bool IsTestEnabled => runTest;
    public GameObject ModelContainer => _modelContainer;
    public GameObject SpawnedModel => _spawnedModel;
    public Animator ModelAnimator => _modelAnimator;
    public LoadedVRMInfo LoadedVrmInfo => _loadedVrmInfo;

    private void Awake()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _modelLoadFailedLogged = false;
    }

    public void OnTestStart()
    {
        TestLog.Info(LogTag, "Started.");
        if (spawnOnStart)
        {
            SpawnCharacter();
        }
    }

    public void OnTestTick(float deltaTime)
    {
    }

    public void OnTestStop()
    {
        _cancellationTokenSource?.Cancel();
        TestLog.Info(LogTag, "Stopped.");
    }

    public bool SpawnCharacter()
    {
        if (_isSpawning)
        {
            TestLog.Warning(LogTag, "Spawn request ignored because spawn is already running.");
            return false;
        }

        TestLog.Info(LogTag, "Spawn requested.");
        SpawnCharacterAsync().Forget();
        return true;
    }

    public void ClearSpawnedCharacter()
    {
        if (_modelContainer != null)
        {
            Destroy(_modelContainer);
            TestLog.Info(LogTag, "Previous spawned model cleared.");
        }

        _modelContainer = null;
        _spawnedModel = null;
        _modelAnimator = null;
        _loadedVrmInfo = null;
    }

    private bool TryResolveCamera()
    {
        if (_targetCamera != null)
        {
            return true;
        }

        _targetCamera = Camera.main;
        if (_targetCamera != null)
        {
            return true;
        }

        var cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null && cameras[i].CompareTag("MainCamera"))
            {
                _targetCamera = cameras[i];
                return true;
            }
        }

        if (cameras.Length > 0)
        {
            _targetCamera = cameras[0];
        }

        return _targetCamera != null;
    }

    private async UniTaskVoid SpawnCharacterAsync()
    {
        _isSpawning = true;
        try
        {
            StartBackendWaitInBackground(_cancellationTokenSource.Token);
            await WaitForExternalUiIfNeeded(_cancellationTokenSource.Token);

            if (!TryResolveCamera())
            {
                return;
            }

            ClearSpawnedCharacter();

            var loadedModelInfo = await LoadModelForTest(_cancellationTokenSource.Token);
            if (loadedModelInfo == null || loadedModelInfo.Model == null)
            {
                if (!_modelLoadFailedLogged)
                {
                    _modelLoadFailedLogged = true;
                    TestLog.Error(LogTag, "Model load failed.");
                }
                return;
            }

            _modelLoadFailedLogged = false;

            _loadedVrmInfo = loadedModelInfo;
            _spawnedModel = loadedModelInfo.Model;
            _spawnedModel.name = _spawnedModel.name + "_TestModel";

            _modelContainer = new GameObject("ModelContainer");
            _modelContainer.transform.SetParent(null, false);

            _spawnedModel.transform.SetParent(_modelContainer.transform, false);

            var cameraTransform = _targetCamera.transform;
            var cameraPosition = new Vector3(cameraTransform.position.x, 0, cameraTransform.position.z);
            _modelContainer.transform.LookAt(cameraPosition, Vector3.up);

            ApplyCharacterSettingsLikeProduction();

            _modelContainer.transform.position += additionalPositionOffset;
            _modelContainer.transform.rotation = Quaternion.Euler(_modelContainer.transform.rotation.eulerAngles + additionalRotationOffsetEuler);

            _modelAnimator = _modelContainer.GetComponentInChildren<Animator>();
            if (_modelAnimator == null && createAnimatorIfMissing)
            {
                _modelAnimator = _spawnedModel.AddComponent<Animator>();
                TestLog.Info(LogTag, "Animator was missing and has been added.");
            }

            if (_modelAnimator != null && createGenericAvatarIfMissing && _modelAnimator.avatar == null)
            {
                var generatedAvatar = CreateAvatarFromModel(_spawnedModel);
                if (generatedAvatar != null)
                {
                    _modelAnimator.avatar = generatedAvatar;
                    TestLog.Info(LogTag, "Generic avatar generated and assigned.");
                }
            }

            TestLog.Info(LogTag, $"Spawn succeeded. model={_spawnedModel.name}");
        }
        catch (System.OperationCanceledException)
        {
            TestLog.Warning(LogTag, "Spawn cancelled.");
        }
        finally
        {
            _isSpawning = false;
        }
    }

    private void StartBackendWaitInBackground(CancellationToken cancellationToken)
    {
        WaitForBackendIfNeededBackground(cancellationToken).Forget();
    }

    private async UniTaskVoid WaitForBackendIfNeededBackground(CancellationToken cancellationToken)
    {
        try
        {
            await WaitForBackendIfNeeded(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            TestLog.Warning(LogTag, $"Backend wait (background) failed: {ex.Message}");
        }
    }

    private async UniTask WaitForBackendIfNeeded(CancellationToken cancellationToken)
    {
        if (!waitForBackendBeforeSpawn)
        {
            return;
        }

        if (backendStartupTestComponent == null)
        {
            backendStartupTestComponent = GetComponent<BackendServerStartupTestComponent>();
            if (backendStartupTestComponent == null)
            {
                backendStartupTestComponent = FindFirstObjectByType<BackendServerStartupTestComponent>();
            }
        }

        if (backendStartupTestComponent == null)
        {
            TestLog.Warning(LogTag, "Backend startup test component not found. Skip backend wait.");
            return;
        }

        if (requestBackendStartupBeforeSpawn)
        {
            backendStartupTestComponent.StartManagedBackend();
        }

        if (!requireBackendRunning)
        {
            return;
        }

        float timeout = Mathf.Max(0.1f, backendWaitTimeoutSeconds);
        float poll = Mathf.Max(0.02f, backendPollIntervalSeconds);
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (backendStartupTestComponent.IsBackendAlive())
            {
                TestLog.Info(LogTag, $"Backend is ready. elapsed={elapsed:0.##}s");
                return;
            }

            int delayMs = Mathf.RoundToInt(poll * 1000f);
            await UniTask.Delay(delayMs, cancellationToken: cancellationToken);
            elapsed += poll;
        }

        TestLog.Warning(LogTag, $"Backend wait timed out ({timeout:0.##}s). Continue spawning character.");
    }

    private async UniTask WaitForExternalUiIfNeeded(CancellationToken cancellationToken)
    {
        if (!waitForExternalUiBeforeSpawn)
        {
            return;
        }

        if (!requireExternalUiRunning)
        {
            return;
        }

        float timeout = Mathf.Max(0.1f, externalUiWaitTimeoutSeconds);
        float poll = Mathf.Max(0.02f, externalUiPollIntervalSeconds);
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsExternalUiRunning())
            {
                float extraDelay = Mathf.Max(0f, externalUiReadyExtraDelaySeconds);
                if (extraDelay > 0f)
                {
                    int extraDelayMs = Mathf.RoundToInt(extraDelay * 1000f);
                    await UniTask.Delay(extraDelayMs, cancellationToken: cancellationToken);
                }

                TestLog.Info(LogTag, $"External UI is ready. elapsed={elapsed:0.##}s");
                return;
            }

            int delayMs = Mathf.RoundToInt(poll * 1000f);
            await UniTask.Delay(delayMs, cancellationToken: cancellationToken);
            elapsed += poll;
        }

        TestLog.Warning(LogTag, $"External UI wait timed out ({timeout:0.##}s). Continue spawning character.");
    }

    private bool IsExternalUiRunning()
    {
        string processName = (externalUiProcessName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        string normalizedTargetPath = string.Empty;
        if (!string.IsNullOrWhiteSpace(externalUiExecutablePath))
        {
            try
            {
                normalizedTargetPath = NormalizePath(externalUiExecutablePath);
            }
            catch
            {
                normalizedTargetPath = string.Empty;
            }
        }

        try
        {
            var processes = Process.GetProcessesByName(processName);
            for (int i = 0; i < processes.Length; i++)
            {
                var process = processes[i];
                if (process == null || process.HasExited)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(normalizedTargetPath))
                {
                    return true;
                }

                try
                {
                    string processPath = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(processPath))
                    {
                        continue;
                    }

                    processPath = NormalizePath(processPath);
                    if (string.Equals(processPath, normalizedTargetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    continue;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).Replace('/', '\\');
    }

    private async UniTask<LoadedVRMInfo> LoadModelForTest(CancellationToken cancellationToken)
    {
        var characterSettings = ApplicationSettings.Instance.Character;
        string originalModelPath = characterSettings.ModelPath;
        bool hasOverride = !string.IsNullOrWhiteSpace(overrideStreamingAssetsModelPath);

        try
        {
            if (hasOverride)
            {
                characterSettings.ModelPath = overrideStreamingAssetsModelPath;
                TestLog.Info(LogTag, $"Model path overridden for test. path={overrideStreamingAssetsModelPath}");
            }

            return await LoadCharacterModel.LoadModel(cancellationToken);
        }
        finally
        {
            if (hasOverride)
            {
                characterSettings.ModelPath = originalModelPath;
            }
        }
    }

    private void ApplyCharacterSettingsLikeProduction()
    {
        if (_modelContainer == null || !applyCharacterSettingsTransform)
        {
            return;
        }

        var characterSettings = ApplicationSettings.Instance.Character;

        _modelContainer.transform.localScale = Vector3.one * characterSettings.Scale;
        _modelContainer.transform.position += new Vector3(
            characterSettings.PositionX,
            characterSettings.PositionY,
            0);

        Vector3 currentRotation = _modelContainer.transform.rotation.eulerAngles;
        _modelContainer.transform.rotation = Quaternion.Euler(
            currentRotation.x + characterSettings.RotationX,
            currentRotation.y + characterSettings.RotationY,
            currentRotation.z + characterSettings.RotationZ);
    }

    private void OnDestroy()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        TestLog.Info(LogTag, "Destroyed.");
    }

    private static Avatar CreateAvatarFromModel(GameObject model)
    {
        if (model == null)
        {
            return null;
        }

        var animator = model.GetComponent<Animator>();
        if (animator != null && animator.avatar != null)
        {
            return animator.avatar;
        }

        var skinnedMeshRenderer = model.GetComponentInChildren<SkinnedMeshRenderer>();
        if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null)
        {
            var avatar = AvatarBuilder.BuildGenericAvatar(model, "");
            if (avatar != null)
            {
                avatar.name = model.name + "_Avatar";
            }

            return avatar;
        }

        return null;
    }
}