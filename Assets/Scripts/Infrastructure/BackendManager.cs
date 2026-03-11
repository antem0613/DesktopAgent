using UnityEngine;
using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Unity.Logging;
using UnityEngine.SceneManagement;

public partial class BackendManager : SingletonMonoBehaviour<BackendManager>
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [Header("Run")]
    [SerializeField] private string appUserModelId = Constant.DefaultAppUserModelId;
#endif

    private float startupTimeoutSeconds = 4f;
    private float pollIntervalSeconds = 0.25f;
    private int tcpTimeoutMs = 200;

    [Header("UI Health Check")]
    [SerializeField] private float uiHealthCheckIntervalSeconds = 2f;
    [SerializeField] private float uiHealthCheckStartupGraceSeconds = 3f;
    [SerializeField] private int uiHealthCheckSuccessThreshold = 1;
    [SerializeField] private int uiHealthCheckMissThreshold = 2;
    [SerializeField] private float uiHealthCheckInitialValidationTimeoutSeconds = 12f;
    [SerializeField] private bool autoRestartUiProcessOnHealthCheckFailure = true;
    [SerializeField] private float uiHealthCheckRestartCooldownSeconds = 8f;
    [SerializeField] private string uiHealthCheckUdpHost = "127.0.0.1";
    [SerializeField] private int uiHealthCheckUdpPort = Constant.UIHealthCheckUdpPort;
    [SerializeField] private int uiHealthCheckUdpTimeoutMs = 180;
    [SerializeField] private string uiHealthCheckPingMessage = Constant.UIHealthCheckPingMessage;
    [SerializeField] private string uiHealthCheckPongMessage = Constant.UIHealthCheckPongMessage;
    [SerializeField] private string uiForceTopmostMessage = Constant.UIForceTopmostMessage;
    [SerializeField] private string uiForceTopmostAckMessage = Constant.UIForceTopmostAckMessage;
    [SerializeField] private string uiOpenMenuMessage = Constant.UIOpenMenuMessage;
    [SerializeField] private string uiOpenMenuAckMessage = Constant.UIOpenMenuAckMessage;
    [SerializeField] private MenuDialog uiMenuDialog;
    [Header("UI Topmost")]
    [SerializeField] private bool enforceUiTopmost = false;
    [SerializeField] private float uiTopmostEnforceIntervalSeconds = 1.0f;

    private sealed class BackendState
    {
        public bool IsShutdownProcessed;
        public bool IsBackendStartupInProgress;
        public bool IsBackendStartupIssued;
        public bool HasLoggedSettings;
        public float LastLoggedStartupTimeoutSeconds;
        public float LastLoggedPollIntervalSeconds;
        public int LastLoggedTcpTimeoutMs;
    }

    private sealed class UiHealthState
    {
        public Coroutine MonitoringCoroutine;
        public int ConsecutiveMisses;
        public int ConsecutiveSuccesses;
        public float StartedAt = -1f;
        public bool IsPendingLaunchValidation;
        public float LastHeartbeatNoResponseLogAt = -1f;
        public float LastValidationWaitLogAt = -1f;
        public float LastRestartSkipAliveLogAt = -1f;
        public float LastHealthDetailLogAt = -1f;
    }

    private sealed class UiProcessState
    {
        public bool IsStartupLaunchIssued;
        public int LaunchAttemptCount;
        public bool IsSceneChangeSubscribed;
        public Coroutine TopmostEnforceCoroutine;
        public float LastTopmostEnforceLogAt = -1f;
    }

    private Process _process;
    private Process _ollamaProcess;
    private Process _coeiroinkProcess;
    private Process _uiProcess;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private IntPtr _managedProcessJob = IntPtr.Zero;
#endif
    private bool _isUiProcess;
    private static readonly object s_uiLaunchSync = new object();
    private static readonly object s_backendStartSync = new object();
    private static bool s_backendStartupIssued;
    private static bool s_uiStartInProgress;
    private static float s_lastUiLaunchRequestedAt = -999f;
    private static float s_lastUiHealthRestartAt = -999f;
    private UdpClient _uiHealthCheckResponder;
    private Thread _uiHealthCheckResponderThread;
    private volatile bool _uiHealthCheckResponderRunning;
    private volatile bool _uiMenuOpenRequested;
    private readonly BackendState _backendState = new();
    private readonly UiHealthState _uiHealthState = new();
    private readonly UiProcessState _uiProcessState = new();

    // --- Initialization and runtime mode setup ---
    /// <summary>
    /// 実行モード判定・入力設定・AppUserModelID適用・UI監視開始などの初期化を順に実行します。
    /// </summary>
    private protected override void Awake()
    {
        base.Awake();
        _isUiProcess = IsUiProcess();
        LogInfo($"[BackendManager] Awake completed. isUiProcess={_isUiProcess}");
        if (_isUiProcess)
        {
            string sceneName = SceneManager.GetActiveScene().name;
            LogInfo($"[BackendManager] UI process bootstrap begin. activeScene={sceneName}, openMenuMessage={uiOpenMenuMessage}, openMenuAck={uiOpenMenuAckMessage}");
        }
        ConfigureUiInput();
        ApplyAppUserModelId();
        StartUiHealthCheckResponderIfNeeded();
        StartUiTopmostEnforcementIfNeeded();
        SubscribeUiSceneChangeFrontingIfNeeded();

        ApplySettings();
    }

    /// <summary>
    /// UIプロセス時のみシーン変更イベントを購読し、前面化制御を有効化します。
    /// </summary>
    private void SubscribeUiSceneChangeFrontingIfNeeded()
    {
        if (!_isUiProcess || _uiProcessState.IsSceneChangeSubscribed)
        {
            return;
        }

        SceneManager.activeSceneChanged += OnUiActiveSceneChanged;
        _uiProcessState.IsSceneChangeSubscribed = true;
    }

    /// <summary>
    /// 購読済みのシーン変更イベントを解除し、重複購読を防ぎます。
    /// </summary>
    private void UnsubscribeUiSceneChangeFronting()
    {
        if (!_uiProcessState.IsSceneChangeSubscribed)
        {
            return;
        }

        SceneManager.activeSceneChanged -= OnUiActiveSceneChanged;
        _uiProcessState.IsSceneChangeSubscribed = false;
    }

    /// <summary>
    /// 対象シーン遷移時にUIウィンドウ前面化を試み、失敗時は間引きログを出力します。
    /// </summary>
    private void OnUiActiveSceneChanged(Scene previous, Scene current)
    {
        if (!_isUiProcess)
        {
            return;
        }

        string target = (Constant.UIStartupSceneName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(target)
            && !string.Equals(current.name, target, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        bool fronted = ForceUiWindowFront();
        if (!fronted && ShouldLogUiHealthRepeated(ref _uiProcessState.LastTopmostEnforceLogAt, 2f))
        {
            LogWarning($"[BackendManager] UI scene switch fronting failed. scene={current.name}");
        }
#endif
    }

    /// <summary>
    /// UIプロセスでバックグラウンド入力を有効化し、InputSystem設定を IgnoreFocus に変更します。
    /// </summary>
    private void ConfigureUiInput()
    {
        if (!_isUiProcess)
        {
            return;
        }

        Application.runInBackground = true;

        try
        {
            var inputSystemType = Type.GetType("UnityEngine.InputSystem.InputSystem, Unity.InputSystem");
            if (inputSystemType == null)
            {
                LogInfo("InputSystem not found. runInBackground only.");
                return;
            }

            PropertyInfo settingsProperty = inputSystemType.GetProperty("settings", BindingFlags.Public | BindingFlags.Static);
            object settings = settingsProperty?.GetValue(null);
            if (settings == null)
            {
                LogInfo("InputSystem settings unavailable. runInBackground only.");
                return;
            }

            PropertyInfo backgroundBehaviorProperty = settings.GetType().GetProperty("backgroundBehavior", BindingFlags.Public | BindingFlags.Instance);
            if (backgroundBehaviorProperty?.PropertyType == null)
            {
                LogInfo("backgroundBehavior property unavailable.");
                return;
            }

            object ignoreFocusValue = Enum.Parse(backgroundBehaviorProperty.PropertyType, "IgnoreFocus", ignoreCase: true);
            backgroundBehaviorProperty.SetValue(settings, ignoreFocusValue);
            LogInfo("External UI background input enabled.");
        } catch (Exception ex)
        {
            LogWarning($"External UI background input setup failed: {ex.GetType().Name}:{ex.Message}");
        }
    }

    /// <summary>
    /// 現在プロセスに AppUserModelID を設定し、タスクバー識別を安定化します。
    /// </summary>
    private void ApplyAppUserModelId()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        string appId = (appUserModelId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(appId))
        {
            appId = Constant.DefaultAppUserModelId;
        }

        try
        {
            int hr = SetCurrentProcessExplicitAppUserModelID(appId);
            if (hr != 0)
            {
                LogWarning($"SetCurrentProcessExplicitAppUserModelID failed. hr=0x{hr:X8}, appId={appId}");
            } else
            {
                LogInfo($"AppUserModelID applied: {appId}");
            }
        } catch (Exception ex)
        {
            LogWarning($"AppUserModelID apply failed: {ex.Message}");
        }
#endif
    }

    // --- Public backend controls ---
    [ContextMenu("Start Backend")]
    /// <summary>
    /// 重複起動をロックで抑止しつつ、設定反映・ジョブ準備・監視開始後に起動コルーチンを開始します。
    /// </summary>
    public void StartBackend()
    {
        lock (s_backendStartSync)
        {
            if (_backendState.IsBackendStartupInProgress)
            {
                LogInfo("StartBackend skipped: startup is already in progress.");
                return;
            }

            if (_backendState.IsBackendStartupIssued || s_backendStartupIssued)
            {
                LogInfo("StartBackend skipped: startup has already been issued for this session.");
                return;
            }

            _backendState.IsBackendStartupInProgress = true;
            _backendState.IsBackendStartupIssued = true;
            s_backendStartupIssued = true;
        }

        LogInfo("StartBackend requested.");
        ApplySettings();
        EnsureProcessJob();
        EnsureUiHealthCheckMonitoring();
        StartCoroutine(StartBackendRoutine());
    }

    [ContextMenu("Stop Backend")]
    /// <summary>
    /// バックエンド停止要求を受け取り、内部停止処理を実行します。
    /// </summary>
    public void StopBackend()
    {
        LogInfo("StopBackend requested.");
        StopBackendInternal();
    }

    [ContextMenu("Run Health Check")]
    /// <summary>
    /// バックエンド到達性を即時確認し、結果をログ出力します。
    /// </summary>
    public void RunHealthCheck()
    {
        bool reachable = IsBackendAlive();
        int backendPort = ResolveBackendPort();
        LogInfo(reachable
            ? $"backend reachable at {Constant.BackendHost}:{backendPort}"
            : $"backend not reachable at {Constant.BackendHost}:{backendPort}");
    }

    [ContextMenu("Run UI Health Check")]
    /// <summary>
    /// UIヘルス状態と検出プロセス数を即時確認し、結果をログ出力します。
    /// </summary>
    public void RunUiHealthCheck()
    {
        int detectedUiProcesses = GetAliveUiProcessCount();
        bool healthy = IsUiHealthy();
        LogInfo(healthy
            ? $"UI health check passed: UDP heartbeat responded. uiLaunchAttempts={_uiProcessState.LaunchAttemptCount}, detectedUiProcesses={detectedUiProcesses}"
            : $"UI health check failed: UDP heartbeat did not respond. uiLaunchAttempts={_uiProcessState.LaunchAttemptCount}, detectedUiProcesses={detectedUiProcesses}");
    }

    // --- Lifecycle startup hook ---
    /// <summary>
    /// Character側プロセスでのみバックエンド起動を開始し、UIプロセスではスキップします。
    /// </summary>
    private void Start()
    {
        LogInfo("[BackendManager] Start called.");
        if (_isUiProcess)
        {
            LogInfo("Start skipped in external-ui process. Waiting for UDP bridge commands.");
            return;
        }

        StartBackend();
    }

    private void Update()
    {
        if (!_isUiProcess || !_uiMenuOpenRequested)
        {
            return;
        }

        LogInfo("UI menu open flag consumed on main thread.");
        _uiMenuOpenRequested = false;
        OpenMenuDialogOnUiProcess();
    }

    // --- Backend server startup pipeline ---
    /// <summary>
    /// UI/依存プロセス起動とバックエンド起動後、タイムアウトまで疎通確認をポーリングします。
    /// </summary>
    private IEnumerator StartBackendRoutine()
    {
        int backendPort = ResolveBackendPort();
        LogInfo($"begin host={Constant.BackendHost} port={backendPort} timeout={startupTimeoutSeconds:0.##}s");

        if (!_uiProcessState.IsStartupLaunchIssued)
        {
            _uiProcessState.IsStartupLaunchIssued = true;
            bool startedByStartup = StartUiProcess("startup");
            if (!startedByStartup)
            {
                LogWarning("UI startup launch attempt did not start a new process. Startup path will not retry automatically.");
            }
        }
        else
        {
            LogInfo("UI startup launch skipped: already issued in this session.");
        }

        StartOllama();
        StartCoeiroink();

        if (IsProcessAlive(_process))
        {
            LogInfo("process already running.");
        } else if (!StartProcess())
        {
            lock (s_backendStartSync)
            {
                _backendState.IsBackendStartupInProgress = false;
            }
            yield break;
        }

        // Allow realistic server boot time; 2s cap was too aggressive and caused false startup failures.
        float timeout = Mathf.Clamp(startupTimeoutSeconds, 1.0f, 120.0f);
        float poll = Mathf.Max(0.05f, pollIntervalSeconds);
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            if (IsBackendAlive())
            {
                LogInfo($"backend reachable at {Constant.BackendHost}:{backendPort} elapsed={elapsed:0.##}s");

                lock (s_backendStartSync)
                {
                    _backendState.IsBackendStartupInProgress = false;
                }
                yield break;
            }

            yield return new WaitForSeconds(poll);
            elapsed += poll;
        }

        LogError($"timeout: backend not reachable at {Constant.BackendHost}:{backendPort}. startupTimeoutSeconds={timeout:0.##}");
        lock (s_backendStartSync)
        {
            _backendState.IsBackendStartupInProgress = false;
            // Startup failed: allow a future explicit retry (e.g. from LLMProvider) instead of deadlocking the session.
            _backendState.IsBackendStartupIssued = false;
            s_backendStartupIssued = false;
        }

        if (IsProcessAlive(_process))
        {
            LogWarning("backend process is alive but health check failed. Keeping process for next retry.");
        }
    }

    /// <summary>
    /// 作業ディレクトリと起動情報を解決し、通常起動→ネイティブ起動→pyフォールバックの順で起動を試行します。
    /// </summary>
    private bool StartProcess()
    {
        string workingDirectory = ResolveWorkingDirectory();
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            LogError($"WorkingDirectory not found: {workingDirectory}");
            return false;
        }

        if (!TryBuildStartInfo(workingDirectory, out var startInfo))
        {
            return false;
        }

        LogInfo($"starting process: {startInfo.FileName} {startInfo.Arguments}");
        LogInfo($"WorkingDirectory: {startInfo.WorkingDirectory}");

        try
        {
            _process = Process.Start(startInfo);
            if (_process == null)
            {
                if (TryLaunchBackend(startInfo, out _process, out string nativeSuccessMessage))
                {
                    LogInfo(nativeSuccessMessage);
                    return true;
                }

                LogError("Process.Start returned null.");

                if (TryLaunchPythonBackend(workingDirectory, out _process))
                {
                    return true;
                }

                return false;
            }

            WireProcessOutput(_process);

            TryAssignProcessToJob(_process);

            return true;
        } catch (Win32Exception ex)
        {
            if (TryLaunchBackend(startInfo, out _process, out string nativeSuccessMessage))
            {
                LogInfo(nativeSuccessMessage);
                return true;
            }

            if (TryLaunchPythonBackend(workingDirectory, out _process))
            {
                return true;
            }

            LogError($"process start failed (Win32): {ex.Message}");
            return false;
        } catch (Exception ex)
        {
            if (TryLaunchPythonBackend(workingDirectory, out _process))
            {
                return true;
            }

            LogError($"process start failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// py ランチャー経由でバックエンド起動を再試行し、成功時は出力購読とJob割当を行います。
    /// </summary>
    private bool TryLaunchPythonBackend(string workingDirectory, out Process process)
    {
        process = null;

        string pyPath = ResolveExecutablePath("py.exe", workingDirectory);
        string fileName = string.IsNullOrWhiteSpace(pyPath) ? "py" : NormalizePathForProcessStart(pyPath);
        string args = Constant.BackendArguments.Trim();
        if (!args.StartsWith("-3 ", StringComparison.OrdinalIgnoreCase))
        {
            args = "-3 " + args;
        }

        var fallbackInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            LogWarning($"Retrying backend start via py launcher: {fallbackInfo.FileName} {fallbackInfo.Arguments}");
            process = Process.Start(fallbackInfo);
            if (process == null)
            {
                LogWarning("py launcher fallback returned null process.");
                return false;
            }

            WireProcessOutput(process);
            TryAssignProcessToJob(process);
            LogInfo("backend process started via py launcher fallback.");
            return true;
        }
        catch (Exception ex)
        {
            LogWarning($"py launcher fallback failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// CreateProcess を直接呼び出して Process を取得し、必要なハンドル後始末を行います。
    /// </summary>
    private bool TryCreateNativeProcess(ProcessStartInfo startInfo, STARTUPINFO startupInfo, out Process process, out int lastError)
    {
        process = null;
        lastError = 0;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        string commandLine = BuildCommandLine(startInfo.FileName, startInfo.Arguments);
        bool created = CreateProcess(
            startInfo.FileName,
            commandLine,
            IntPtr.Zero,
            IntPtr.Zero,
            false,
            Constant.CREATE_NO_WINDOW,
            IntPtr.Zero,
            startInfo.WorkingDirectory,
            ref startupInfo,
            out PROCESS_INFORMATION pi);

        if (!created)
        {
            lastError = Marshal.GetLastWin32Error();
            return false;
        }

        try
        {
            process = Process.GetProcessById((int)pi.dwProcessId);
            TryAssignProcessHandleToJob(pi.hProcess);
        }
        catch
        {
            process = null;
        }
        finally
        {
            if (pi.hThread != IntPtr.Zero)
            {
                CloseHandle(pi.hThread);
            }

            if (pi.hProcess != IntPtr.Zero)
            {
                CloseHandle(pi.hProcess);
            }
        }

        return process != null;
#else
        return false;
#endif
    }

    /// <summary>
    /// バックエンド用の STARTUPINFO を構成し、ネイティブ CreateProcess で起動を試行します。
    /// </summary>
    private bool TryLaunchBackend(ProcessStartInfo startInfo, out Process process, out string successMessage)
    {
        process = null;
        successMessage = string.Empty;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        try
        {
            var startupInfo = new STARTUPINFO
            {
                cb = (uint)Marshal.SizeOf<STARTUPINFO>(),
                dwFlags = Constant.STARTF_USESHOWWINDOW,
                wShowWindow = Constant.SW_HIDE
            };

            if (!TryCreateNativeProcess(startInfo, startupInfo, out process, out int lastError))
            {
                LogWarning($"Backend CreateProcess failed. error={lastError}, file={startInfo.FileName}, args={startInfo.Arguments}, cwd={startInfo.WorkingDirectory}");
                return false;
            }

            successMessage = $"process started: {startInfo.FileName} {startInfo.Arguments}";
            return true;
        } catch (Exception ex)
        {
            LogWarning($"Backend CreateProcess exception: {ex.GetType().Name}:{ex.Message}");
            return false;
        }
#else
        return false;
#endif
    }

    /// <summary>
    /// 実行ファイル解決・WindowsApps判定・引数補正を行い ProcessStartInfo を構築します。
    /// </summary>
    private bool TryBuildStartInfo(string workingDirectory, out ProcessStartInfo startInfo)
    {
        startInfo = null;

        string fileName = Constant.BackendExecutable.Trim();
        string args = Constant.BackendArguments.Trim();


        string resolvedFileName = ResolveBackendExecutablePath(fileName, workingDirectory);
        if (string.IsNullOrWhiteSpace(resolvedFileName))
        {
            if (!IsPythonCommandName(fileName))
            {
                LogError($"launch executable could not be resolved. file={fileName}, workingDir={workingDirectory}");
                return false;
            }

            resolvedFileName = fileName;
            LogWarning($"Python executable path could not be resolved. Falling back to command-name launch: {resolvedFileName}");
        }

        fileName = resolvedFileName;

        if (IsWindowsAppsPythonShim(fileName))
        {
            string pyPath = ResolveExecutablePath("py.exe", workingDirectory);
            if (!string.IsNullOrWhiteSpace(pyPath))
            {
                fileName = NormalizePathForProcessStart(pyPath);
            }
            else
            {
                fileName = "py";
            }

            if (!args.StartsWith("-3 ", StringComparison.OrdinalIgnoreCase))
            {
                args = "-3 " + args;
            }

            LogWarning("Detected WindowsApps python shim. Switching backend launch to py -3.");
        }

        startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        return true;
    }

    /// <summary>
    /// バックエンド実行ファイルを候補順に探索し、見つかったパスを正規化して返します。
    /// </summary>
    private static string ResolveBackendExecutablePath(string fileName, string workingDirectory)
    {
        string resolved = ResolveExecutablePath(fileName, workingDirectory);
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return NormalizePathForProcessStart(resolved);
        }

        if (!IsPythonCommandName(fileName))
        {
            return string.Empty;
        }

        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        string[] pythonCandidates =
        {
            Path.Combine(workingDirectory ?? string.Empty, ".venv", "Scripts", "python.exe"),
            Path.Combine(workingDirectory ?? string.Empty, "venv", "Scripts", "python.exe"),
            Path.Combine(projectRoot, ".venv", "Scripts", "python.exe"),
            Path.Combine(projectRoot, "venv", "Scripts", "python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "py.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "python.exe")
        };

        for (int i = 0; i < pythonCandidates.Length; i++)
        {
            string candidate = pythonCandidates[i];
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                if (File.Exists(candidate))
                {
                    return NormalizePathForProcessStart(candidate);
                }
            } catch
            {
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// 指定名が Python 実行コマンド名かを判定します。
    /// </summary>
    private static bool IsPythonCommandName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        string normalizedName = Path.GetFileName(fileName).Trim();
        return string.Equals(normalizedName, "python", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedName, "python.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedName, "python3", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedName, "python3.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 指定パスが WindowsApps の python shim かを判定します。
    /// </summary>
    private static bool IsWindowsAppsPythonShim(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        string normalized = fileName.Replace('/', '\\');
        return normalized.EndsWith("\\Microsoft\\WindowsApps\\python.exe", StringComparison.OrdinalIgnoreCase);
    }

    // --- Settings and health check ---
    /// <summary>
    /// SettingsProvider から起動タイミング設定を反映し、変更時のみ設定値をログ出力します。
    /// </summary>
    private void ApplySettings()
    {
        bool applied = false;

        try
        {
            var provider = SettingsProvider.Instance;
            if (provider?.Backend != null)
            {
                startupTimeoutSeconds = provider.Backend.StartupTimeoutSeconds;
                pollIntervalSeconds = provider.Backend.PollIntervalSeconds;
                tcpTimeoutMs = provider.Backend.TcpTimeoutMs;
                applied = true;
            }
        } catch (Exception ex)
        {
            LogWarning($"Failed to load startup timings from SettingsProvider: {ex.Message}");
        }

        if (!applied)
        {
            LogInfo("Using built-in default startup timings.");
            return;
        }

        bool changed = !_backendState.HasLoggedSettings
            || !Mathf.Approximately(_backendState.LastLoggedStartupTimeoutSeconds, startupTimeoutSeconds)
            || !Mathf.Approximately(_backendState.LastLoggedPollIntervalSeconds, pollIntervalSeconds)
            || _backendState.LastLoggedTcpTimeoutMs != tcpTimeoutMs;

        if (changed)
        {
            _backendState.HasLoggedSettings = true;
            _backendState.LastLoggedStartupTimeoutSeconds = startupTimeoutSeconds;
            _backendState.LastLoggedPollIntervalSeconds = pollIntervalSeconds;
            _backendState.LastLoggedTcpTimeoutMs = tcpTimeoutMs;
            LogInfo($"Settings applied. startupTimeoutSeconds={startupTimeoutSeconds:0.###}, pollIntervalSeconds={pollIntervalSeconds:0.###}, tcpTimeoutMs={tcpTimeoutMs}");
        }
    }

    /// <summary>
    /// 設定からバックエンドポートを取得し、無効時は既定値を返します。
    /// </summary>
    private int ResolveBackendPort()
    {
        try
        {
            var provider = SettingsProvider.Instance;
            if (provider?.Backend != null && provider.Backend.Port > 0)
            {
                return provider.Backend.Port;
            }
        } catch
        {
        }

        return Constant.BackendPort;
    }

    /// <summary>
    /// 標準出力・標準エラー・終了イベントを購読し、プロセスログを集約します。
    /// </summary>
    private void WireProcessOutput(Process process)
    {
        try
        {
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    LogInfo($"[stdout] {e.Data}");
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    LogWarning($"[stderr] {e.Data}");
                }
            };
            process.Exited += (_, __) =>
            {
                try
                {
                    LogWarning($"process exited. ExitCode={process.ExitCode}");
                } catch
                {
                    LogWarning("process exited.");
                }
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        } catch (Exception ex)
        {
            LogWarning($"Failed to wire process output: {ex.Message}");
        }
    }

    /// <summary>
    /// バックエンド作業ディレクトリをプロジェクト基準で絶対パス化して返します。
    /// </summary>
    private string ResolveWorkingDirectory()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        string relative = Constant.BackendWorkingDirectoryRelativePath?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(relative))
        {
            return projectRoot;
        }

        if (Path.IsPathRooted(relative))
        {
            return Path.GetFullPath(relative);
        }

        return Path.GetFullPath(Path.Combine(projectRoot, relative));
    }

    /// <summary>
    /// HTTPヘルスチェックを優先し、必要時はTCP到達性で代替判定します。
    /// </summary>
    public bool IsBackendAlive()
    {
        int backendPort = ResolveBackendPort();
        string path = NormalizeHttpPath(Constant.BackendHealthCheckPath);
        if (!string.IsNullOrWhiteSpace(path))
        {
            int timeoutMs = Mathf.Max(100, Constant.BackendHealthCheckHttpTimeoutMs);
            if (TryGetHttpStatusCode(Constant.BackendHost, backendPort, path, timeoutMs, out int statusCode))
            {
                return statusCode >= 200 && statusCode < 300;
            }

            if (statusCode == 404)
            {
                return IsTcpReachable(Constant.BackendHost, backendPort);
            }

            return false;
        }

        return IsTcpReachable(Constant.BackendHost, backendPort);
    }

    // --- Shutdown and process cleanup ---
    /// <summary>
    /// アプリ終了時にバックエンド停止処理を実行します。
    /// </summary>
    private void OnApplicationQuit()
    {
        LogInfo("OnApplicationQuit received.");
        TryStopBackendServerOnExit();
    }

    /// <summary>
    /// 破棄時にバックエンド停止処理を実行します。
    /// </summary>
    private void OnDestroy()
    {
        LogInfo("OnDestroy received.");
        TryStopBackendServerOnExit();
    }

    /// <summary>
    /// 無効化時にバックエンド停止処理を実行します。
    /// </summary>
    private void OnDisable()
    {
        LogInfo("OnDisable received.");
        TryStopBackendServerOnExit();
    }

    /// <summary>
    /// 終了系イベントからの停止処理を一度だけ実行し、関連監視も停止します。
    /// </summary>
    private void TryStopBackendServerOnExit()
    {
        StopUiTopmostEnforcement();
        StopUiHealthCheckResponder();
        UnsubscribeUiSceneChangeFronting();

        if (_isUiProcess || _backendState.IsShutdownProcessed)
        {
            return;
        }

        _backendState.IsShutdownProcessed = true;

        StopBackendInternal();
        LogInfo("backend stop requested on exit.");
    }

    /// <summary>
    /// UIプロセスかつ有効設定時のみ、トップモスト維持コルーチンを開始します。
    /// </summary>
    private void StartUiTopmostEnforcementIfNeeded()
    {
        if (!_isUiProcess || !enforceUiTopmost)
        {
            return;
        }

        if (_uiProcessState.TopmostEnforceCoroutine != null)
        {
            return;
        }

        _uiProcessState.LastTopmostEnforceLogAt = -1f;
        _uiProcessState.TopmostEnforceCoroutine = StartCoroutine(UiTopmostEnforcementLoop());
        LogInfo("UI topmost enforcement started.");
    }

    /// <summary>
    /// トップモスト維持コルーチンを停止します。
    /// </summary>
    private void StopUiTopmostEnforcement()
    {
        if (_uiProcessState.TopmostEnforceCoroutine != null)
        {
            StopCoroutine(_uiProcessState.TopmostEnforceCoroutine);
            _uiProcessState.TopmostEnforceCoroutine = null;
        }
    }

    /// <summary>
    /// 一定間隔でトップモスト適用を試行し、失敗は間引きログで通知します。
    /// </summary>
    private IEnumerator UiTopmostEnforcementLoop()
    {
        // Give the standalone player window a short moment to initialize its main handle.
        yield return new WaitForSeconds(0.25f);

        while (_isUiProcess && !_backendState.IsShutdownProcessed)
        {
            bool applied = false;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            applied = WindowsAPI.SetCurrentWindowTopmost(true);
#endif
            if (!applied && ShouldLogUiHealthRepeated(ref _uiProcessState.LastTopmostEnforceLogAt, 5f))
            {
                LogWarning("UI topmost enforcement failed. Window handle may not be ready yet.");
            }

            float interval = Mathf.Max(0.2f, uiTopmostEnforceIntervalSeconds);
            yield return new WaitForSeconds(interval);
        }
    }

    /// <summary>
    /// 起動状態・監視・Job・関連プロセスをまとめて停止/リセットします。
    /// </summary>
    private void StopBackendInternal()
    {
        lock (s_backendStartSync)
        {
            _backendState.IsBackendStartupInProgress = false;
            _backendState.IsBackendStartupIssued = false;
            s_backendStartupIssued = false;
        }

        StopUiHealthCheckMonitoring();
        DisposeProcessJob();
        _uiProcessState.IsStartupLaunchIssued = false;
        _uiProcessState.LaunchAttemptCount = 0;

        StopTrackedProcess(ref _process, "LangGraph");
        StopTrackedProcess(ref _ollamaProcess, "Ollama");
        StopTrackedProcess(ref _coeiroinkProcess, "COEIROINK");
        StopTrackedProcess(ref _uiProcess, "UI Process");
    }

    /// <summary>
    /// Character側でUIヘルス監視状態を初期化し、監視コルーチンを開始します。
    /// </summary>
    private void EnsureUiHealthCheckMonitoring()
    {
        if (_isUiProcess)
        {
            return;
        }

        if (_uiHealthState.MonitoringCoroutine != null)
        {
            return;
        }

        _uiHealthState.ConsecutiveMisses = 0;
        _uiHealthState.ConsecutiveSuccesses = 0;
        _uiHealthState.StartedAt = Time.realtimeSinceStartup;
        _uiHealthState.IsPendingLaunchValidation = false;
        _uiHealthState.LastHeartbeatNoResponseLogAt = -1f;
        _uiHealthState.LastValidationWaitLogAt = -1f;
        _uiHealthState.LastRestartSkipAliveLogAt = -1f;
        _uiHealthState.LastHealthDetailLogAt = -1f;
        _uiHealthState.MonitoringCoroutine = StartCoroutine(UiHealthCheckLoop());
        LogInfo("UI health check monitoring started.");
    }

    /// <summary>
    /// UIヘルス監視コルーチンを停止し、監視状態を初期化します。
    /// </summary>
    private void StopUiHealthCheckMonitoring()
    {
        if (_uiHealthState.MonitoringCoroutine != null)
        {
            StopCoroutine(_uiHealthState.MonitoringCoroutine);
            _uiHealthState.MonitoringCoroutine = null;
        }

        _uiHealthState.ConsecutiveMisses = 0;
        _uiHealthState.ConsecutiveSuccesses = 0;
        _uiHealthState.StartedAt = -1f;
        _uiHealthState.IsPendingLaunchValidation = false;
        _uiHealthState.LastHeartbeatNoResponseLogAt = -1f;
        _uiHealthState.LastValidationWaitLogAt = -1f;
        _uiHealthState.LastRestartSkipAliveLogAt = -1f;
        _uiHealthState.LastHealthDetailLogAt = -1f;
    }

    /// <summary>
    /// 終了済みまたはUIプロセス実行時かどうかを判定し、監視ループ終了条件を返します。
    /// </summary>
    private bool ShouldExitUiHealthCheckLoop()
    {
        return _backendState.IsShutdownProcessed || _isUiProcess;
    }

    /// <summary>
    /// UI起動直後の猶予期間内かどうかを判定します。
    /// </summary>
    private bool IsUiHealthCheckWithinStartupGrace()
    {
        float grace = Mathf.Max(0f, uiHealthCheckStartupGraceSeconds);
        return _uiHealthState.StartedAt >= 0f
            && Time.realtimeSinceStartup - _uiHealthState.StartedAt < grace;
    }

    /// <summary>
    /// UIヘルス連続成功/失敗カウンタと開始時刻をリセットします。
    /// </summary>
    private void ResetUiHealthProgress()
    {
        _uiHealthState.ConsecutiveMisses = 0;
        _uiHealthState.ConsecutiveSuccesses = 0;
        _uiHealthState.StartedAt = Time.realtimeSinceStartup;
    }

    /// <summary>
    /// UI重複起動を検知した場合に統合を試み、必要ならヘルス進捗をリセットします。
    /// </summary>
    private bool HandleDuplicateUiProcessHealthState(int aliveUiProcesses)
    {
        if (aliveUiProcesses <= 1)
        {
            return false;
        }

        LogWarning($"UI health check detected multiple UI processes ({aliveUiProcesses}). Attempting to collapse duplicates.");
        if (TryCollapseDuplicateUiProcesses())
        {
            ResetUiHealthProgress();
        }

        return true;
    }

    /// <summary>
    /// UIヘルス成功時のカウンタ更新と初回起動検証完了処理を行います。
    /// </summary>
    private void HandleHealthyUiHeartbeat()
    {
        _uiHealthState.ConsecutiveSuccesses++;
        if (_uiHealthState.ConsecutiveMisses > 0)
        {
            LogInfo("UI health check recovered.");
        }

        _uiHealthState.ConsecutiveMisses = 0;

        if (_uiHealthState.IsPendingLaunchValidation
            && _uiHealthState.ConsecutiveSuccesses >= Mathf.Max(1, uiHealthCheckSuccessThreshold))
        {
            _uiHealthState.IsPendingLaunchValidation = false;
            LogInfo("UI launch health-check validation completed. Relaunch is enabled.");
        }
    }

    /// <summary>
    /// 起動直後の検証待ち状態を監視し、タイムアウトと進捗ログを管理します。
    /// </summary>
    private bool HandlePendingUiLaunchValidation()
    {
        if (!_uiHealthState.IsPendingLaunchValidation)
        {
            return false;
        }

        float pendingElapsedSeconds = _uiHealthState.StartedAt > 0f
            ? Time.realtimeSinceStartup - _uiHealthState.StartedAt
            : 0f;
        float pendingTimeoutSeconds = Mathf.Max(2f, uiHealthCheckInitialValidationTimeoutSeconds);
        if (pendingElapsedSeconds >= pendingTimeoutSeconds)
        {
            _uiHealthState.IsPendingLaunchValidation = false;
            LogWarning($"UI launch health-check validation timed out after {pendingElapsedSeconds:0.0}s. Continuing with normal health-check flow. uiLaunchAttempts={_uiProcessState.LaunchAttemptCount}");
        }

        if (ShouldLogUiHealthRepeated(ref _uiHealthState.LastValidationWaitLogAt, 5f))
        {
            LogInfo($"UI health check waiting for first launch validation. uiLaunchAttempts={_uiProcessState.LaunchAttemptCount}, elapsed={pendingElapsedSeconds:0.0}s, timeout={pendingTimeoutSeconds:0.0}s");
        }

        return _uiHealthState.IsPendingLaunchValidation;
    }

    /// <summary>
    /// UIプロセスが存在するのにUDP応答がない場合のみ、間引き警告を出力します。
    /// </summary>
    private void LogUiHeartbeatNoResponseIfNeeded(int aliveUiProcesses)
    {
        if (aliveUiProcesses <= 0)
        {
            return;
        }

        if (ShouldLogUiHealthRepeated(ref _uiHealthState.LastHeartbeatNoResponseLogAt, 3f))
        {
            LogWarning("UI health check: UI process exists but UDP heartbeat did not respond.");
        }
    }

    /// <summary>
    /// 再起動設定・クールダウン・起動中状態を確認し、UI再起動可否を返します。
    /// </summary>
    private bool ShouldRestartUiAfterHealthFailure()
    {
        if (!autoRestartUiProcessOnHealthCheckFailure)
        {
            return false;
        }

        if (_backendState.IsBackendStartupInProgress)
        {
            LogInfo("UI health check restart skipped: backend startup is in progress.");
            return false;
        }

        float restartCooldown = Mathf.Max(1f, uiHealthCheckRestartCooldownSeconds);
        if (Time.realtimeSinceStartup - s_lastUiHealthRestartAt < restartCooldown)
        {
            LogInfo("UI health check restart skipped: restart cooldown is active.");
            return false;
        }

        if (IsUiHealthy())
        {
            LogInfo("UI health check restart skipped: UDP heartbeat responded during restart decision.");
            _uiHealthState.ConsecutiveSuccesses = Mathf.Max(1, _uiHealthState.ConsecutiveSuccesses);
            _uiHealthState.ConsecutiveMisses = 0;
            return false;
        }

        return true;
    }

    /// <summary>
    /// UIヘルス失敗時のカウンタ・詳細ログ・閾値到達時の再起動処理を行います。
    /// </summary>
    private void HandleFailedUiHeartbeat(int aliveUiProcesses)
    {
        _uiHealthState.ConsecutiveSuccesses = 0;
        LogUiHeartbeatNoResponseIfNeeded(aliveUiProcesses);

        if (HandlePendingUiLaunchValidation())
        {
            return;
        }

        _uiHealthState.ConsecutiveMisses++;
        LogWarning($"UI health check miss {_uiHealthState.ConsecutiveMisses}/{Mathf.Max(1, uiHealthCheckMissThreshold)}.");

        if (ShouldLogUiHealthRepeated(ref _uiHealthState.LastHealthDetailLogAt, 5f))
        {
            LogInfo($"UI health detail. uiLaunchAttempts={_uiProcessState.LaunchAttemptCount}, detectedUiProcesses={aliveUiProcesses}, pendingValidation={_uiHealthState.IsPendingLaunchValidation}, consecutiveMisses={_uiHealthState.ConsecutiveMisses}");
        }

        if (_uiHealthState.ConsecutiveMisses < Mathf.Max(1, uiHealthCheckMissThreshold))
        {
            return;
        }

        _uiHealthState.ConsecutiveMisses = 0;
        if (!ShouldRestartUiAfterHealthFailure())
        {
            return;
        }

        LogWarning("UI health check failed threshold. Restarting UI process.");
        if (StartUiProcess("health-check"))
        {
            s_lastUiHealthRestartAt = Time.realtimeSinceStartup;
            _uiHealthState.StartedAt = Time.realtimeSinceStartup;
        }
    }

    /// <summary>
    /// 一定間隔でUIヘルスを監視し、成功処理または失敗処理へ分岐します。
    /// </summary>
    private IEnumerator UiHealthCheckLoop()
    {
        while (!ShouldExitUiHealthCheckLoop())
        {
            float interval = Mathf.Max(0.2f, uiHealthCheckIntervalSeconds);
            yield return new WaitForSeconds(interval);

            if (ShouldExitUiHealthCheckLoop())
            {
                yield break;
            }

            if (IsUiHealthCheckWithinStartupGrace())
            {
                continue;
            }

            int aliveUiProcesses = GetAliveUiProcessCount();
            if (HandleDuplicateUiProcessHealthState(aliveUiProcesses))
            {
                continue;
            }

            if (IsUiHealthy())
            {
                HandleHealthyUiHeartbeat();
                continue;
            }

            HandleFailedUiHeartbeat(aliveUiProcesses);
        }
    }

    /// <summary>
    /// UI起動ロックとクールダウンを確認し、起動試行カウンタを進めます。
    /// </summary>
    private bool TryBeginUiLaunchAttempt(float now, out int currentAttempt)
    {
        currentAttempt = 0;

        lock (s_uiLaunchSync)
        {
            if (s_uiStartInProgress)
            {
                LogInfo("UI start skipped: launch is already in progress (global lock).");
                return false;
            }

            if (now - s_lastUiLaunchRequestedAt < 2.0f)
            {
                LogInfo("UI start skipped: launch cooldown is active (global lock).");
                return false;
            }

            if (IsUiHealthy())
            {
                LogInfo("UI start skipped: UDP heartbeat already responds.");
                return false;
            }

            s_uiStartInProgress = true;
            s_lastUiLaunchRequestedAt = now;
            _uiProcessState.LaunchAttemptCount++;
            currentAttempt = _uiProcessState.LaunchAttemptCount;
            return true;
        }
    }

    /// <summary>
    /// UI起動直後の検証待ち状態をセットし、ヘルスカウンタを初期化します。
    /// </summary>
    private void MarkUiLaunchValidationPending()
    {
        _uiHealthState.IsPendingLaunchValidation = true;
        _uiHealthState.ConsecutiveSuccesses = 0;
        _uiHealthState.ConsecutiveMisses = 0;
        _uiHealthState.StartedAt = Time.realtimeSinceStartup;
    }

    /// <summary>
    /// 同名UIプロセスを列挙し、残すPID以外を終了して重複を解消します。
    /// </summary>
    private bool TryCollapseDuplicateUiProcesses()
    {
        if (IsSingleBinaryUiLaunchMode())
        {
            LogInfo("[BackendManager] UI duplicate collapse skipped in single-binary mode.");
            return true;
        }

        string processName = (Constant.UIProcessName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(processName))
        {
            processName = Path.GetFileNameWithoutExtension(Constant.UIProcessExecutablePath ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        try
        {
            var processes = Process.GetProcessesByName(processName);
            var alive = new List<Process>();
            for (int i = 0; i < processes.Length; i++)
            {
                var process = processes[i];
                if (IsProcessAlive(process))
                {
                    alive.Add(process);
                }
            }

            if (alive.Count <= 1)
            {
                return true;
            }

            int keepPid = -1;
            if (IsProcessAlive(_uiProcess))
            {
                try
                {
                    keepPid = _uiProcess.Id;
                } catch
                {
                    keepPid = -1;
                }
            }

            if (keepPid <= 0)
            {
                _uiProcess = alive[0];
                keepPid = alive[0].Id;
            }

            int killed = 0;
            for (int i = 0; i < alive.Count; i++)
            {
                Process candidate = alive[i];
                int pid;
                try
                {
                    pid = candidate.Id;
                } catch
                {
                    continue;
                }

                if (pid == keepPid)
                {
                    continue;
                }

                if (TryKillProcessTree(candidate))
                {
                    killed++;
                }
            }

            int remaining = GetAliveUiProcessCount();
            if (remaining <= 1)
            {
                LogWarning($"UI duplicate collapse completed. keptPid={keepPid}, killed={killed}, remaining={remaining}.");
                return true;
            }

            LogWarning($"UI duplicate collapse incomplete. keptPid={keepPid}, killed={killed}, remaining={remaining}.");
            return false;
        } catch (Exception ex)
        {
            LogWarning($"UI duplicate collapse failed: {ex.GetType().Name}:{ex.Message}");
            return false;
        }
    }

    // --- External UI process management ---
    /// <summary>
    /// UI実行ファイルを解決して起動し、成功時は起動直後検証待ち状態へ遷移させます。
    /// </summary>
    private bool StartUiProcess(string reason)
    {
        float now = Time.realtimeSinceStartup;
        if (!TryBeginUiLaunchAttempt(now, out int currentAttempt))
        {
            return false;
        }

        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        string resolvedExecutable = ResolveUiLaunchExecutablePath(projectRoot);
        if (string.IsNullOrWhiteSpace(resolvedExecutable))
        {
            LogWarning($"UI executable not found: {Constant.UIProcessExecutablePath}");
            lock (s_uiLaunchSync)
            {
                s_uiStartInProgress = false;
            }
            return false;
        }

        resolvedExecutable = NormalizePathForProcessStart(resolvedExecutable);

        string workingDirectory = NormalizePathForProcessStart(Path.GetDirectoryName(resolvedExecutable) ?? projectRoot);
        var startInfo = new ProcessStartInfo
        {
            FileName = resolvedExecutable,
            WorkingDirectory = workingDirectory,
            Arguments = BuildUiProcessArguments(),
            UseShellExecute = false,
            CreateNoWindow = false
        };

        LogInfo($"UI start requested. reason={reason}, attempt={currentAttempt}, file={startInfo.FileName}, args={startInfo.Arguments}, cwd={startInfo.WorkingDirectory}");

        try
        {
            if (TryCreateUiProcess(startInfo, out string successMessage))
            {
                MarkUiLaunchValidationPending();
                LogInfo(successMessage);
                return true;
            }

            LogWarning(
                $"UI start failed. file={startInfo.FileName}, args={startInfo.Arguments}, cwd={startInfo.WorkingDirectory}, exists={File.Exists(startInfo.FileName)}");
            return false;
        }
        finally
        {
            lock (s_uiLaunchSync)
            {
                s_uiStartInProgress = false;
            }
        }
    }

    /// <summary>
    /// UIプロセスをネイティブ CreateProcess で起動し、追跡対象に登録します。
    /// </summary>
    private bool TryCreateUiProcess(ProcessStartInfo startInfo, out string successMessage)
    {
        successMessage = string.Empty;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        try
        {
            var startupInfo = new STARTUPINFO
            {
                cb = (uint)Marshal.SizeOf<STARTUPINFO>(),
                dwFlags = Constant.STARTF_USESHOWWINDOW,
                wShowWindow = Constant.SW_HIDE
            };

            if (!TryCreateNativeProcess(startInfo, startupInfo, out var process, out int lastError))
            {
                LogWarning($"CreateProcess failed. error={lastError}, file={startInfo.FileName}, args={startInfo.Arguments}, cwd={startInfo.WorkingDirectory}");
                return false;
            }

            _uiProcess = process;
            successMessage = $"UI process started (native CreateProcess): {startInfo.FileName} {startInfo.Arguments}";
            TryConfigureExternalUiTaskbarVisibility(_uiProcess);
            return true;
        } catch (Exception ex)
        {
            LogWarning($"CreateProcess exception: {ex.GetType().Name}:{ex.Message}");
            return false;
        }
#else
        return false;
#endif
    }

    /// <summary>
    /// 外部UIウィンドウにAppUserModelID設定とタスクバー非表示設定を適用します。
    /// </summary>
    private void TryConfigureExternalUiTaskbarVisibility(Process process)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (process == null)
        {
            LogWarning("UI taskbar config skipped: process handle is null.");
            return;
        }

        string appId = (appUserModelId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(appId))
        {
            appId = Constant.DefaultAppUserModelId;
        }

        try
        {
            const int maxAttempts = 20;
            LogInfo($"UI taskbar config requested. pid={process.Id}, appId={appId}");
            for (int i = 0; i < maxAttempts; i++)
            {
                IntPtr hwnd = IntPtr.Zero;
                try
                {
                    process.Refresh();
                    hwnd = process.MainWindowHandle;
                } catch
                {
                    return;
                }

                if (hwnd != IntPtr.Zero)
                {
                    bool appIdApplied = TrySetWindowAppUserModelId(hwnd, appId);
                    bool hideApplied = TryHideWindowTaskbarIcon(hwnd);

                    if (appIdApplied && hideApplied)
                    {
                        LogInfo($"UI taskbar config applied. appId={appIdApplied}, hidden={hideApplied}");
                    } else
                    {
                        LogWarning($"UI taskbar config failed. appId={appIdApplied}, hidden={hideApplied}");
                    }
                    return;
                }

                Thread.Sleep(100);
            }

            LogWarning($"UI taskbar config skipped: main window handle not found within {maxAttempts * 100}ms.");
        } catch (Exception ex)
        {
            LogWarning($"UI taskbar config exception: {ex.GetType().Name}:{ex.Message}");
        }
#endif
    }

    /// <summary>
    /// 拡張ウィンドウスタイルを更新してタスクバーアイコンを非表示化します。
    /// </summary>
    private static bool TryHideWindowTaskbarIcon(IntPtr windowHandle)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        IntPtr stylePtr = GetWindowLongPtrCompat(windowHandle, Constant.GWL_EXSTYLE);
        long style = stylePtr.ToInt64();
        long targetStyle = (style | Constant.WS_EX_TOOLWINDOW) & ~Constant.WS_EX_APPWINDOW;

        if (targetStyle != style)
        {
            Marshal.GetLastWin32Error();
            IntPtr previous = SetWindowLongPtrCompat(windowHandle, Constant.GWL_EXSTYLE, new IntPtr(targetStyle));
            int err = Marshal.GetLastWin32Error();
            if (previous == IntPtr.Zero && err != 0)
            {
                return false;
            }
        }

        SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, 0, 0,
            Constant.SWP_NOMOVE | Constant.SWP_NOSIZE | Constant.SWP_NOZORDER | Constant.SWP_NOACTIVATE | Constant.SWP_FRAMECHANGED);

        long verify = GetWindowLongPtrCompat(windowHandle, Constant.GWL_EXSTYLE).ToInt64();
        bool hasToolWindow = (verify & Constant.WS_EX_TOOLWINDOW) != 0;
        bool hasAppWindow = (verify & Constant.WS_EX_APPWINDOW) != 0;
        return hasToolWindow && !hasAppWindow;
#else
        return false;
#endif
    }

    /// <summary>
    /// 32/64bit差異を吸収して GetWindowLongPtr を呼び出します。
    /// </summary>
    private static IntPtr GetWindowLongPtrCompat(IntPtr hWnd, int nIndex)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLongPtr32(hWnd, nIndex);
#else
        return IntPtr.Zero;
#endif
    }

    /// <summary>
    /// 32/64bit差異を吸収して SetWindowLongPtr を呼び出します。
    /// </summary>
    private static IntPtr SetWindowLongPtrCompat(IntPtr hWnd, int nIndex, IntPtr newValue)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, newValue) : SetWindowLongPtr32(hWnd, nIndex, newValue);
#else
        return IntPtr.Zero;
#endif
    }

    /// <summary>
    /// 指定ウィンドウに AppUserModelID プロパティを書き込みます。
    /// </summary>
    private static bool TrySetWindowAppUserModelId(IntPtr windowHandle, string appId)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (windowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(appId))
        {
            return false;
        }

        Guid iid = typeof(IPropertyStore).GUID;
        if (SHGetPropertyStoreForWindow(windowHandle, ref iid, out IPropertyStore store) != 0 || store == null)
        {
            return false;
        }

        var pv = new PROPVARIANT
        {
            vt = Constant.VT_LPWSTR,
            p = Marshal.StringToCoTaskMemUni(appId),
            p2 = 0
        };

        try
        {
            PROPERTYKEY appUserModelKey = PKEY_AppUserModel_ID;
            int hrSet = store.SetValue(ref appUserModelKey, ref pv);
            if (hrSet != 0)
            {
                return false;
            }

            return store.Commit() == 0;
        } finally
        {
            PropVariantClear(ref pv);
            Marshal.ReleaseComObject(store);
        }
#else
        return false;
#endif
    }

    // --- Dependency process management (Ollama / COEIROINK) ---
    /// <summary>
    /// 到達性と既存プロセスを確認し、必要時のみOllamaを起動します。
    /// </summary>
    private void StartOllama()
    {
        if (IsOllamaServerReachable())
        {
            LogInfo($"Ollama start skipped: already reachable at {Constant.BackendHost}:{Constant.OllamaHealthPort}");
            return;
        }

        if (IsProcessAlive(_ollamaProcess))
        {
            LogInfo($"Ollama start skipped: existing launched process is alive (pid={_ollamaProcess.Id})");
            return;
        }

        if (!TryStartDependencyProcess("Ollama", Constant.OllamaExecutable, ResolveWorkingDirectory(), out var process, args: Constant.OllamaArguments))
        {
            return;
        }

        _ollamaProcess = process;
    }

    /// <summary>
    /// Ollama のヘルスエンドポイントへHTTP接続し到達性を判定します。
    /// </summary>
    private bool IsOllamaServerReachable()
    {
        const string versionPath = "/api/version";
        const string tagsPath = "/api/tags";
        int timeoutMs = Mathf.Max(100, Constant.BackendHealthCheckHttpTimeoutMs);

        if (TryGetHttpStatusCode(Constant.BackendHost, Constant.OllamaHealthPort, versionPath, timeoutMs, out int statusCode) && statusCode >= 200 && statusCode < 300)
        {
            return true;
        }

        if (statusCode == (int)HttpStatusCode.NotFound && TryGetHttpStatusCode(Constant.BackendHost, Constant.OllamaHealthPort, tagsPath, timeoutMs, out int tagsStatusCode) && tagsStatusCode >= 200 && tagsStatusCode < 300)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 既存到達性と既存プロセスを確認し、必要時のみCOEIROINKを起動します。
    /// </summary>
    private void StartCoeiroink()
    {
        if (IsTcpReachable(Constant.BackendHost, Constant.CoeiroinkHealthPort))
        {
            return;
        }

        if (IsProcessAlive(_coeiroinkProcess))
        {
            return;
        }

        if (!TryStartDependencyProcess("COEIROINK", Constant.CoeiroinkExecutable, ResolveWorkingDirectory(), out var process))
        {
            return;
        }

        _coeiroinkProcess = process;
    }

    /// <summary>
    /// 依存プロセスを通常起動し、失敗時はネイティブ起動へフォールバックします。
    /// </summary>
    private bool TryStartDependencyProcess(string name, string executableNameOrPath, string workingDirectory, out Process process, string args = "")
    {
        process = null;

        string resolvedExecutable = ResolveExecutablePath(executableNameOrPath, workingDirectory);
        if (string.IsNullOrWhiteSpace(resolvedExecutable))
        {
            LogWarning($"{name} executable not found: {executableNameOrPath}. workingDir={workingDirectory}");
            return false;
        }

        resolvedExecutable = NormalizePathForProcessStart(resolvedExecutable);
        string executableDirectory = Path.GetDirectoryName(resolvedExecutable);
        string effectiveWorkingDirectory = !string.IsNullOrWhiteSpace(executableDirectory) && Directory.Exists(executableDirectory)
            ? executableDirectory
            : (string.IsNullOrWhiteSpace(workingDirectory) ? Application.dataPath : workingDirectory);
        effectiveWorkingDirectory = NormalizePathForProcessStart(effectiveWorkingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = resolvedExecutable,
            Arguments = args?.Trim() ?? string.Empty,
            WorkingDirectory = effectiveWorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            process = Process.Start(startInfo);
            if (process == null)
            {
                if (TryCreateDependencyProcess(name, startInfo, out process, out string successMessage))
                {
                    LogInfo(successMessage);
                    return true;
                }

                LogWarning($"{name} Process.Start returned null.");
                return false;
            }

            WireProcessOutput(process);

            TryAssignProcessToJob(process);

            LogInfo($"{name} process started: {startInfo.FileName} {startInfo.Arguments}");
            return true;
        } catch (Win32Exception ex)
        {
            if (TryCreateDependencyProcess(name, startInfo, out process, out string nativeSuccessMessage))
            {
                LogInfo(nativeSuccessMessage);
                return true;
            }

            LogWarning($"{name} start failed (Win32): {ex.Message}. file={startInfo.FileName}, args={startInfo.Arguments}, cwd={startInfo.WorkingDirectory}");
            return false;
        } catch (Exception ex)
        {
            LogWarning($"{name} start failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 依存プロセスをネイティブ CreateProcess で起動します。
    /// </summary>
    private bool TryCreateDependencyProcess(string name, ProcessStartInfo startInfo, out Process process, out string successMessage)
    {
        process = null;
        successMessage = string.Empty;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        try
        {
            var startupInfo = new STARTUPINFO
            {
                cb = (uint)Marshal.SizeOf<STARTUPINFO>()
            };

            if (!TryCreateNativeProcess(startInfo, startupInfo, out process, out int lastError))
            {
                LogWarning($"{name} native CreateProcess failed. error={lastError}, file={startInfo.FileName}, args={startInfo.Arguments}, cwd={startInfo.WorkingDirectory}");
                return false;
            }

            successMessage = $"{name} process started (native CreateProcess): {startInfo.FileName} {startInfo.Arguments}";
            return true;
        } catch (Exception ex)
        {
            LogWarning($"{name} native CreateProcess exception: {ex.GetType().Name}:{ex.Message}");
            return false;
        }
#else
        return false;
#endif
    }

    /// <summary>
    /// ファイル名と引数を CreateProcess 用コマンドライン文字列へ整形します。
    /// </summary>
    private static string BuildCommandLine(string fileName, string arguments)
    {
        string quotedFileName = string.IsNullOrWhiteSpace(fileName)
            ? string.Empty
            : $"\"{fileName}\"";

        string trimmedArgs = arguments?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedArgs))
        {
            return quotedFileName;
        }

        if (string.IsNullOrWhiteSpace(quotedFileName))
        {
            return trimmedArgs;
        }

        return quotedFileName + " " + trimmedArgs;
    }

    /// <summary>
    /// Process 参照が有効かつ未終了かを安全に判定します。
    /// </summary>
    private static bool IsProcessAlive(Process process)
    {
        if (process == null)
        {
            return false;
        }

        try
        {
            return !process.HasExited;
        } catch
        {
            return false;
        }
    }

    /// <summary>
    /// 実行ファイルを作業ディレクトリ・プロジェクト・PATH順で探索します。
    /// </summary>
    private static string ResolveExecutablePath(string executableNameOrPath, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(executableNameOrPath))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(executableNameOrPath))
        {
            return File.Exists(executableNameOrPath) ? executableNameOrPath : string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            string workingCandidate = Path.Combine(workingDirectory, executableNameOrPath);
            if (File.Exists(workingCandidate))
            {
                return workingCandidate;
            }
        }

        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        string projectCandidate = Path.Combine(projectRoot, executableNameOrPath);
        if (File.Exists(projectCandidate))
        {
            return projectCandidate;
        }

        string assetsCandidate = Path.Combine(Application.dataPath, executableNameOrPath);
        if (File.Exists(assetsCandidate))
        {
            return assetsCandidate;
        }

        string[] pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator);
        for (int i = 0; i < pathEntries.Length; i++)
        {
            string entry = pathEntries[i]?.Trim();
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            string candidate = Path.Combine(entry, executableNameOrPath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        if (string.Equals(Path.GetFileName(executableNameOrPath), "engine.exe", StringComparison.OrdinalIgnoreCase) &&
            TryResolveCoeiroinkEnginePath(out string coeiroinkEnginePath))
        {
            return coeiroinkEnginePath;
        }

        return string.Empty;
    }

    /// <summary>
    /// COEIROINKエンジン実行ファイルを環境変数と既知配置から探索します。
    /// </summary>
    private static bool TryResolveCoeiroinkEnginePath(out string resolvedPath)
    {
        resolvedPath = string.Empty;

        string envPath = (Environment.GetEnvironmentVariable("COEIROINK_ENGINE_PATH") ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            resolvedPath = envPath;
            return true;
        }

        string[] roots =
        {
            @"C:\",
            @"D:\",
            @"E:\"
        };

        for (int i = 0; i < roots.Length; i++)
        {
            string root = roots[i];
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                string[] topLevelCandidates = Directory.GetDirectories(root, "COEIROINK*", SearchOption.TopDirectoryOnly);
                for (int j = 0; j < topLevelCandidates.Length; j++)
                {
                    string baseDir = topLevelCandidates[j];

                    string directEngine = Path.Combine(baseDir, "engine", "engine.exe");
                    if (File.Exists(directEngine))
                    {
                        resolvedPath = directEngine;
                        return true;
                    }

                    string[] childDirs;
                    try
                    {
                        childDirs = Directory.GetDirectories(baseDir, "*", SearchOption.TopDirectoryOnly);
                    } catch
                    {
                        childDirs = Array.Empty<string>();
                    }

                    for (int k = 0; k < childDirs.Length; k++)
                    {
                        string nestedEngine = Path.Combine(childDirs[k], "engine", "engine.exe");
                        if (File.Exists(nestedEngine))
                        {
                            resolvedPath = nestedEngine;
                            return true;
                        }
                    }
                }
            } catch
            {
            }
        }

        return false;
    }

    /// <summary>
    /// 起動前パスを絶対化し、区切り文字を Windows 形式へ正規化します。
    /// </summary>
    private static string NormalizePathForProcessStart(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path).Replace('/', '\\');
        } catch
        {
            return path.Replace('/', '\\');
        }
    }

    // --- Network and process utility helpers ---
    private static string ProcessTypeTag => IsUiProcess() ? "UI" : "Character";

    /// <summary>
    /// ログメッセージの前置詞を整理し、出力用に正規化します。
    /// </summary>
    private static string NormalizeBackendLogMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        string normalized = message.Trim();
        const string managerPrefix = "[BackendManager]";
        if (normalized.StartsWith(managerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(managerPrefix.Length).TrimStart();
        }

        return normalized;
    }

    /// <summary>
    /// 情報ログをプロセスタグ付きで出力します。
    /// </summary>
    private static void LogInfo(string message)
    {
        string normalized = NormalizeBackendLogMessage(message);
        Log.Info($"[BackendManager] [{ProcessTypeTag}] {normalized}");
    }

    /// <summary>
    /// 警告ログをプロセスタグ付きで出力します。
    /// </summary>
    private static void LogWarning(string message)
    {
        string normalized = NormalizeBackendLogMessage(message);
        Log.Warning($"[BackendManager] [{ProcessTypeTag}] {normalized}");
    }

    /// <summary>
    /// エラーログをプロセスタグ付きで出力します。
    /// </summary>
    private static void LogError(string message)
    {
        string normalized = NormalizeBackendLogMessage(message);
        Log.Error($"[BackendManager] [{ProcessTypeTag}] {normalized}");
    }

    /// <summary>
    /// 追跡中または探索したUIプロセスからメインウィンドウハンドル取得を試行します。
    /// </summary>
    public bool TryGetUiWindowHandle(out IntPtr uiWindowHandle)
    {
        uiWindowHandle = IntPtr.Zero;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (_isUiProcess)
        {
            LogInfo("ZOrder direct UI handle lookup skipped in UI process.");
            return false;
        }

        if (TryGetWindowHandle(_uiProcess, out uiWindowHandle))
        {
            LogInfo($"ZOrder direct UI handle resolved from tracked process. hwnd=0x{uiWindowHandle.ToInt64():X}");
            return true;
        }

        if (TryResolveTrackedUiProcess(out var resolved) && TryGetWindowHandle(resolved, out uiWindowHandle))
        {
            _uiProcess = resolved;
            int pid = -1;
            try
            {
                pid = resolved.Id;
            }
            catch
            {
                pid = -1;
            }
            LogInfo($"ZOrder direct UI handle resolved by process scan. pid={pid}, hwnd=0x{uiWindowHandle.ToInt64():X}");
            return true;
        }
#endif

        LogWarning("ZOrder direct UI handle resolution failed.");

        return false;
    }

    /// <summary>
    /// UIウィンドウを現在ウィンドウより前面に再配置します。
    /// </summary>
    public bool TryRaiseUiWindow()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!TryGetUiWindowHandle(out var uiWindowHandle))
        {
            LogWarning("ZOrder direct UI re-front skipped: UI window handle unavailable.");
            return false;
        }

        IntPtr currentWindowHandle = WindowsAPI.GetCurrentWindowHandle();
        if (currentWindowHandle == IntPtr.Zero || currentWindowHandle == uiWindowHandle)
        {
            LogWarning($"ZOrder direct UI re-front skipped. current=0x{currentWindowHandle.ToInt64():X}, ui=0x{uiWindowHandle.ToInt64():X}");
            return false;
        }

        bool placed = NativeWindowApi.PlaceAboveWindow(uiWindowHandle, currentWindowHandle);
        LogInfo($"ZOrder direct UI re-front place-above. ui=0x{uiWindowHandle.ToInt64():X}, current=0x{currentWindowHandle.ToInt64():X}, success={placed}");
        return placed;
#else
        return false;
#endif
    }

    /// <summary>
    /// 設定候補名を使って生存UIプロセスを列挙し、重複を除いた件数を返します。
    /// </summary>
    private int GetAliveUiProcessCount()
    {
        if (IsSingleBinaryUiLaunchMode())
        {
            return IsProcessAlive(_uiProcess) ? 1 : 0;
        }

        var alivePids = new HashSet<int>();

        if (IsProcessAlive(_uiProcess))
        {
            try
            {
                alivePids.Add(_uiProcess.Id);
            }
            catch
            {
            }
        }

        var processNameCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string configuredName = (Constant.UIProcessName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(configuredName))
        {
            processNameCandidates.Add(Path.GetFileNameWithoutExtension(configuredName) ?? string.Empty);
        }

        string executableBaseName = Path.GetFileNameWithoutExtension(Constant.UIProcessExecutablePath ?? string.Empty) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(executableBaseName))
        {
            processNameCandidates.Add(executableBaseName);
        }

        if (IsProcessAlive(_uiProcess))
        {
            try
            {
                string trackedName = _uiProcess.ProcessName ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(trackedName))
                {
                    processNameCandidates.Add(trackedName);
                }
            }
            catch
            {
            }
        }

        try
        {
            foreach (string candidate in processNameCandidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                var processes = Process.GetProcessesByName(candidate);
                for (int i = 0; i < processes.Length; i++)
                {
                    var process = processes[i];
                    if (IsProcessAlive(process))
                    {
                        try
                        {
                            alivePids.Add(process.Id);
                        }
                        catch
                        {
                        }

                        if (_uiProcess == null)
                        {
                            _uiProcess = process;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogWarning($"[BackendManager] Failed to enumerate UI processes: {ex.GetType().Name}:{ex.Message}");
        }

        return alivePids.Count;
    }

    /// <summary>
    /// UI起動引数を返します。
    /// </summary>
    private static string BuildUiProcessArguments()
    {
        return Constant.UIProcessArgument;
    }

    /// <summary>
    /// プロセスのメインウィンドウを取得し、必要時はPID探索へフォールバックします。
    /// </summary>
    private static bool TryGetWindowHandle(Process process, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        if (!IsProcessAlive(process))
        {
            return false;
        }

        try
        {
            process.Refresh();
            handle = process.MainWindowHandle;
            if (handle != IntPtr.Zero)
            {
                return true;
            }

            int pid;
            try
            {
                pid = process.Id;
            }
            catch
            {
                return false;
            }

            // Unity player windows can temporarily report MainWindowHandle=0; resolve by PID as a fallback.
            return TryGetWindowByPid(pid, out handle);
        }
        catch
        {
            handle = IntPtr.Zero;
            return false;
        }
    }

    /// <summary>
    /// 列挙したトップレベルウィンドウからPID一致のハンドルを取得します。
    /// </summary>
    private static bool TryGetWindowByPid(int pid, out IntPtr handle)
    {
        handle = IntPtr.Zero;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (pid <= 0)
        {
            return false;
        }

        IntPtr foundVisible = IntPtr.Zero;
        IntPtr foundAny = IntPtr.Zero;
        WindowsAPI.EnumWindows((hWnd, _) =>
        {
            if (hWnd == IntPtr.Zero)
            {
                return true;
            }

            GetWindowThreadProcessId(hWnd, out uint windowPid);
            if (windowPid == (uint)pid)
            {
                if (foundAny == IntPtr.Zero)
                {
                    foundAny = hWnd;
                }

                if (WindowsAPI.IsWindowVisible(hWnd))
                {
                    foundVisible = hWnd;
                    return false;
                }
            }

            return true;
        }, IntPtr.Zero);

        IntPtr found = foundVisible != IntPtr.Zero ? foundVisible : foundAny;
        if (found == IntPtr.Zero)
        {
            return false;
        }

        handle = found;
        return true;
#else
        return false;
#endif
    }

    /// <summary>
    /// UI候補プロセスを走査し、ウィンドウを持つ追跡対象を解決します。
    /// </summary>
    private bool TryResolveTrackedUiProcess(out Process process)
    {
        process = null;

        var processNameCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string configuredName = (Constant.UIProcessName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(configuredName))
        {
            processNameCandidates.Add(Path.GetFileNameWithoutExtension(configuredName) ?? string.Empty);
        }

        string executableBaseName = Path.GetFileNameWithoutExtension(Constant.UIProcessExecutablePath ?? string.Empty) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(executableBaseName))
        {
            processNameCandidates.Add(executableBaseName);
        }

        int currentPid = -1;
        try
        {
            currentPid = Process.GetCurrentProcess().Id;
        }
        catch
        {
            currentPid = -1;
        }

        foreach (string candidate in processNameCandidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(candidate);
            }
            catch
            {
                continue;
            }

            for (int i = 0; i < processes.Length; i++)
            {
                var found = processes[i];
                if (!IsProcessAlive(found))
                {
                    continue;
                }

                int pid;
                try
                {
                    pid = found.Id;
                }
                catch
                {
                    continue;
                }

                if (pid == currentPid)
                {
                    continue;
                }

                if (TryGetWindowHandle(found, out _))
                {
                    process = found;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// UI起動実行ファイルパスを現在プロセス情報と設定値から解決します。
    /// </summary>
    private static string ResolveUiLaunchExecutablePath(string projectRoot)
    {
        if (TryGetCurrentProcessExecutablePath(out string currentFromArgs))
        {
            return currentFromArgs;
        }

        try
        {
            using var current = Process.GetCurrentProcess();
            string currentExecutable = current.MainModule?.FileName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(currentExecutable))
            {
                return NormalizePathForProcessStart(currentExecutable);
            }
        }
        catch
        {
        }

        string configured = ResolveExecutablePath(Constant.UIProcessExecutablePath, projectRoot);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return NormalizePathForProcessStart(configured);
        }

        return string.Empty;
    }

    /// <summary>
    /// コマンドライン引数から現在実行中の実行ファイルパス取得を試行します。
    /// </summary>
    private static bool TryGetCurrentProcessExecutablePath(out string executablePath)
    {
        executablePath = string.Empty;

        try
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args == null || args.Length == 0)
            {
                return false;
            }

            string candidate = args[0] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            executablePath = NormalizePathForProcessStart(candidate);
            return !string.IsNullOrWhiteSpace(executablePath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 現在実行バイナリとUI起動先の一致を判定し、単一バイナリ運用か返します。
    /// </summary>
    private static bool IsSingleBinaryUiLaunchMode()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            string currentExecutable = current.MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(currentExecutable))
            {
                return false;
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string selectedLaunchExecutable = ResolveUiLaunchExecutablePath(projectRoot);
            if (string.IsNullOrWhiteSpace(selectedLaunchExecutable))
            {
                return false;
            }

            string configuredPath = NormalizePathForProcessStart(selectedLaunchExecutable);
            string currentPath = NormalizePathForProcessStart(currentExecutable);
            return string.Equals(configuredPath, currentPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 指定ホスト/ポートへのTCP接続可否をタイムアウト付きで判定します。
    /// </summary>
    private bool IsTcpReachable(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host) || port <= 0)
        {
            return false;
        }

        try
        {
            using var client = new TcpClient();
            var result = client.BeginConnect(host, port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(Mathf.Max(50, tcpTimeoutMs)));
            if (!success)
            {
                return false;
            }

            client.EndConnect(result);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 一定間隔内の重複ログ出力を抑制する判定を行います。
    /// </summary>
    private static bool ShouldLogUiHealthRepeated(ref float lastLoggedAt, float minIntervalSeconds)
    {
        float now = Time.realtimeSinceStartup;
        float interval = Mathf.Max(0.1f, minIntervalSeconds);
        if (lastLoggedAt > 0f && now - lastLoggedAt < interval)
        {
            return false;
        }

        lastLoggedAt = now;
        return true;
    }

    /// <summary>
    /// UDP ping/pong によるUI応答性ヘルスチェックを実行します。
    /// </summary>
    private bool IsUiHealthy()
    {
        string host = string.IsNullOrWhiteSpace(uiHealthCheckUdpHost) ? "127.0.0.1" : uiHealthCheckUdpHost.Trim();
        int port = Mathf.Max(1, uiHealthCheckUdpPort);
        int timeout = Mathf.Max(50, uiHealthCheckUdpTimeoutMs);
        string ping = string.IsNullOrWhiteSpace(uiHealthCheckPingMessage) ? "desktopagent-ui-ping" : uiHealthCheckPingMessage;
        string expectedPong = string.IsNullOrWhiteSpace(uiHealthCheckPongMessage) ? "desktopagent-ui-pong" : uiHealthCheckPongMessage;

        try
        {
            using var client = new UdpClient();
            client.Client.ReceiveTimeout = timeout;
            client.Client.SendTimeout = timeout;

            byte[] requestBytes = Encoding.UTF8.GetBytes(ping);
            client.Send(requestBytes, requestBytes.Length, host, port);

            IPEndPoint remote = null;
            byte[] responseBytes = client.Receive(ref remote);
            if (responseBytes == null || responseBytes.Length == 0)
            {
                return false;
            }

            string response = Encoding.UTF8.GetString(responseBytes).Trim();
            return string.Equals(response, expectedPong, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// UI側でヘルスチェックUDP応答スレッドを開始します。
    /// </summary>
    private void StartUiHealthCheckResponderIfNeeded()
    {
        if (!_isUiProcess || _uiHealthCheckResponderRunning)
        {
            return;
        }

        string host = string.IsNullOrWhiteSpace(uiHealthCheckUdpHost) ? "127.0.0.1" : uiHealthCheckUdpHost.Trim();
        int port = Mathf.Max(1, uiHealthCheckUdpPort);
        int timeout = Mathf.Max(50, uiHealthCheckUdpTimeoutMs);
        LogInfo($"Starting UI UDP responder. host={host}, port={port}, timeoutMs={timeout}, openMenuMessage={uiOpenMenuMessage}");

        try
        {
            IPAddress address = IPAddress.TryParse(host, out var parsed) ? parsed : IPAddress.Loopback;
            _uiHealthCheckResponder = new UdpClient(new IPEndPoint(address, port));
            _uiHealthCheckResponder.Client.ReceiveTimeout = timeout;
            _uiHealthCheckResponderRunning = true;
            _uiHealthCheckResponderThread = new Thread(UiHealthCheckResponderLoop)
            {
                IsBackground = true,
                Name = "UIHealthCheckResponder"
            };
            _uiHealthCheckResponderThread.Start();
            LogInfo($"UI health-check UDP responder started. endpoint={host}:{port}");
        }
        catch (Exception ex)
        {
            _uiHealthCheckResponderRunning = false;
            _uiHealthCheckResponder = null;
            LogWarning($"[BackendManager] UI health-check UDP responder failed to start: {ex.Message}");
        }
    }

    /// <summary>
    /// ヘルスチェックUDP応答スレッドとソケットを停止・破棄します。
    /// </summary>
    private void StopUiHealthCheckResponder()
    {
        _uiHealthCheckResponderRunning = false;

        try
        {
            _uiHealthCheckResponder?.Close();
        }
        catch
        {
        }

        try
        {
            _uiHealthCheckResponder?.Dispose();
        }
        catch
        {
        }

        _uiHealthCheckResponder = null;

        if (_uiHealthCheckResponderThread != null)
        {
            try
            {
                _uiHealthCheckResponderThread.Join(200);
            }
            catch
            {
            }
            _uiHealthCheckResponderThread = null;
        }
    }

    /// <summary>
    /// UDP受信ループでヘルスチェック応答と前面化要求を処理します。
    /// </summary>
    private void UiHealthCheckResponderLoop()
    {
        string ping = string.IsNullOrWhiteSpace(uiHealthCheckPingMessage) ? Constant.UIHealthCheckPingMessage : uiHealthCheckPingMessage;
        string pong = string.IsNullOrWhiteSpace(uiHealthCheckPongMessage) ? Constant.UIHealthCheckPongMessage : uiHealthCheckPongMessage;
        string forceTopmost = string.IsNullOrWhiteSpace(uiForceTopmostMessage) ? Constant.UIForceTopmostMessage : uiForceTopmostMessage;
        string forceTopmostAck = string.IsNullOrWhiteSpace(uiForceTopmostAckMessage) ? Constant.UIForceTopmostAckMessage : uiForceTopmostAckMessage;
        string openMenu = string.IsNullOrWhiteSpace(uiOpenMenuMessage) ? Constant.UIOpenMenuMessage : uiOpenMenuMessage;
        string openMenuAck = string.IsNullOrWhiteSpace(uiOpenMenuAckMessage) ? Constant.UIOpenMenuAckMessage : uiOpenMenuAckMessage;
        byte[] pongBytes = Encoding.UTF8.GetBytes(pong);
        byte[] forceTopmostAckBytes = Encoding.UTF8.GetBytes(forceTopmostAck);
        byte[] openMenuAckBytes = Encoding.UTF8.GetBytes(openMenuAck);
        LogInfo($"UI UDP responder loop active. ping={ping}, forceTopmost={forceTopmost}, openMenu={openMenu}");

        while (_uiHealthCheckResponderRunning)
        {
            try
            {
                IPEndPoint remote = null;
                byte[] requestBytes = _uiHealthCheckResponder?.Receive(ref remote);
                if (requestBytes == null || requestBytes.Length == 0 || remote == null)
                {
                    continue;
                }

                string request = Encoding.UTF8.GetString(requestBytes).Trim();

                if (string.Equals(request, forceTopmost, StringComparison.Ordinal))
                {
                    LogInfo($"[BackendManager] UI force-topmost UDP received. from={remote.Address}:{remote.Port}, message={request}");

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                    bool topmostApplied = ForceUiWindowFront();
                    LogInfo($"[BackendManager] UI force-topmost UDP processed. success={topmostApplied}");

                    if (!topmostApplied && enforceUiTopmost && ShouldLogUiHealthRepeated(ref _uiProcessState.LastTopmostEnforceLogAt, 2f))
                    {
                        LogWarning("[BackendManager] UI force-topmost request received but topmost apply failed.");
                    }
#endif
                    _uiHealthCheckResponder?.Send(forceTopmostAckBytes, forceTopmostAckBytes.Length, remote);
                    LogInfo($"[BackendManager] UI force-topmost UDP ack sent. to={remote.Address}:{remote.Port}, message={forceTopmostAck}");
                    continue;
                }

                if (string.Equals(request, openMenu, StringComparison.Ordinal))
                {
                    LogInfo($"UI open-menu request accepted. Scheduling main-thread dialog open. from={remote.Address}:{remote.Port}");
                    _uiMenuOpenRequested = true;
                    _uiHealthCheckResponder?.Send(openMenuAckBytes, openMenuAckBytes.Length, remote);
                    LogInfo($"UI open-menu UDP ack sent. to={remote.Address}:{remote.Port}, message={openMenuAck}");
                    continue;
                }

                if (!string.Equals(request, ping, StringComparison.Ordinal))
                {
                    continue;
                }

                _uiHealthCheckResponder?.Send(pongBytes, pongBytes.Length, remote);
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    continue;
                }

                if (_uiHealthCheckResponderRunning)
                {
                    LogWarning($"[BackendManager] UI health-check responder socket error: {ex.SocketErrorCode}");
                }
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (_uiHealthCheckResponderRunning)
                {
                    LogWarning($"[BackendManager] UI health-check responder exception: {ex.GetType().Name}:{ex.Message}");
                }
            }
        }
    }

    private void OpenMenuDialogOnUiProcess()
    {
        if (!_isUiProcess)
        {
            LogWarning("OpenMenuDialogOnUiProcess called in non-UI process.");
            return;
        }

        if (uiMenuDialog == null)
        {
            LogInfo("Resolving MenuDialog by scene search.");
            uiMenuDialog = FindFirstObjectByType<MenuDialog>();
        }

        if (uiMenuDialog == null)
        {
            string sceneName = SceneManager.GetActiveScene().name;
            LogWarning($"UI menu open skipped: MenuDialog is not found. activeScene={sceneName}");
            return;
        }

        LogInfo($"Opening MenuDialog. object={uiMenuDialog.name}, activeInHierarchy={uiMenuDialog.gameObject.activeInHierarchy}");
        uiMenuDialog.Show();
        LogInfo("UI menu opened by bridge request.");
    }

    /// <summary>
    /// HTTPパスの先頭スラッシュを補正して正規化します。
    /// </summary>
    private static string NormalizeHttpPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string trimmed = path.Trim();
        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            trimmed = "/" + trimmed;
        }

        return trimmed;
    }

    /// <summary>
    /// 現在ウィンドウを前面化し、必要に応じてトップモスト化します。
    /// </summary>
    private bool ForceUiWindowFront()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        IntPtr currentWindowHandle = WindowsAPI.GetCurrentWindowHandle();
        if (currentWindowHandle == IntPtr.Zero)
        {
            return false;
        }

        if (enforceUiTopmost)
        {
            bool topmost = WindowsAPI.SetCurrentWindowTopmost(true);
            bool brought = WindowsAPI.BringCurrentWindowToFront();
            IntPtr foreground = NativeWindowApi.GetForegroundWindowHandle();
            bool foregroundMatched = NativeWindowApi.AreSameRootOwnerWindow(currentWindowHandle, foreground);
            return topmost || brought || foregroundMatched;
        }

        // Keep topmost after force-front requests; releasing immediately often cancels the intended z-order.
        bool raised = WindowsAPI.SetCurrentWindowTopmost(true);
        bool broughtOnce = WindowsAPI.BringCurrentWindowToFront();
        IntPtr currentForeground = NativeWindowApi.GetForegroundWindowHandle();
        bool foregroundMatchedNow = NativeWindowApi.AreSameRootOwnerWindow(currentWindowHandle, currentForeground);
        return raised || broughtOnce || foregroundMatchedNow;
#else
        return false;
#endif
    }

    /// <summary>
    /// HTTP GET を実行し、ステータスコード取得を試行します。
    /// </summary>
    private static bool TryGetHttpStatusCode(string host, int port, string path, int timeoutMs, out int statusCode)
    {
        statusCode = 0;

        if (string.IsNullOrWhiteSpace(host) || port <= 0 || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string url = $"http://{host}:{port}{path}";
        try
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;

            using var response = (HttpWebResponse)request.GetResponse();
            statusCode = (int)response.StatusCode;
            return true;
        } catch (WebException ex) when (ex.Response is HttpWebResponse httpResponse)
        {
            statusCode = (int)httpResponse.StatusCode;
            return false;
        } catch
        {
            return false;
        }
    }

    /// <summary>
    /// 追跡中プロセスを安全に停止し、参照を解放します。
    /// </summary>
    private static void StopTrackedProcess(ref Process process, string label)
    {
        try
        {
            if (process == null)
            {
                return;
            }

            if (TryKillProcessTree(process))
            {
                LogInfo($"[BackendManager] {label} process stopped.");
            }
        } catch (Exception ex)
        {
            LogWarning($"[BackendManager] {label} stop failed: {ex.Message}");
        } finally
        {
            process = null;
        }
    }

    /// <summary>
    /// プロセス終了を試み、必要時は taskkill で子プロセスを含めて強制終了します。
    /// </summary>
    private static bool TryKillProcessTree(Process process)
    {
        if (process == null)
        {
            return false;
        }

        int pid = -1;
        try
        {
            pid = process.Id;
        } catch
        {
        }

        try
        {
            if (IsProcessAlive(process))
            {
                process.Kill();
            }
        } catch
        {
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (pid > 0)
        {
            try
            {
                var taskkillInfo = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/PID {pid} /T /F",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var taskkill = Process.Start(taskkillInfo);
                taskkill?.WaitForExit(2000);
            } catch
            {
            }
        }
#endif

        return !IsProcessAlive(process);
    }

    /// <summary>
    /// COEIROINK接続先の到達可否を確認し、利用可能なエンドポイントを返します。
    /// </summary>
    private bool TryResolveCoeiroinkEndpoint(out string host, out int port)
    {
        host = Constant.BackendHost;
        port = Constant.CoeiroinkHealthPort;

        if (!Uri.TryCreate(Constant.BackendHost, UriKind.Absolute, out _))
        {
            return false;
        }

        if (IsTcpReachable(host, port))
        {
            return true;
        }

        return false;
    }

    // --- Windows JobObject integration ---
    /// <summary>
    /// 起動引数と実行プロセス名から現在プロセスがUIかを判定します。
    /// </summary>
    private static bool IsUiProcess()
    {
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args == null)
            {
                return false;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], Constant.UIProcessArgument, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            string uiProcessName = (Constant.UIProcessName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(uiProcessName))
            {
                uiProcessName = Path.GetFileNameWithoutExtension(Constant.UIProcessExecutablePath ?? string.Empty);
            }

            if (!string.IsNullOrWhiteSpace(uiProcessName))
            {
                string normalizedUiProcessName = Path.GetFileNameWithoutExtension(uiProcessName) ?? string.Empty;
                using var current = Process.GetCurrentProcess();
                string currentName = current.ProcessName ?? string.Empty;
                if (string.Equals(currentName, normalizedUiProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                try
                {
                    string mainModulePath = current.MainModule?.FileName ?? string.Empty;
                    string moduleName = Path.GetFileNameWithoutExtension(mainModulePath) ?? string.Empty;
                    if (string.Equals(moduleName, normalizedUiProcessName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                } catch
                {
                }
            }
        } catch
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// 未作成なら JobObject を作成し、終了時一括終了ポリシーを設定します。
    /// </summary>
    private void EnsureProcessJob()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (_managedProcessJob != IntPtr.Zero)
        {
            return;
        }

        try
        {
            _managedProcessJob = CreateJobObject(IntPtr.Zero, null);
            if (_managedProcessJob == IntPtr.Zero)
            {
                return;
            }

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = Constant.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

            int length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            IntPtr infoPtr = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(info, infoPtr, false);
                if (!SetInformationJobObject(_managedProcessJob, Constant.JobObjectExtendedLimitInformation, infoPtr, (uint)length))
                {
                    CloseHandle(_managedProcessJob);
                    _managedProcessJob = IntPtr.Zero;
                }
            } finally
            {
                Marshal.FreeHGlobal(infoPtr);
            }
        } catch
        {
            if (_managedProcessJob != IntPtr.Zero)
            {
                CloseHandle(_managedProcessJob);
                _managedProcessJob = IntPtr.Zero;
            }
        }
#endif
    }

    /// <summary>
    /// Process ハンドルを JobObject に割り当てます。
    /// </summary>
    private void TryAssignProcessToJob(Process process)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (process == null)
        {
            return;
        }

        EnsureProcessJob();
        if (_managedProcessJob == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AssignProcessToJobObject(_managedProcessJob, process.Handle);
        } catch
        {
        }
#endif
    }

    /// <summary>
    /// ネイティブハンドルを JobObject に割り当てます。
    /// </summary>
    private void TryAssignProcessHandleToJob(IntPtr processHandle)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (processHandle == IntPtr.Zero)
        {
            return;
        }

        EnsureProcessJob();
        if (_managedProcessJob == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AssignProcessToJobObject(_managedProcessJob, processHandle);
        } catch
        {
        }
#endif
    }

    /// <summary>
    /// JobObject を閉じて管理ハンドルを解放します。
    /// </summary>
    private void DisposeProcessJob()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (_managedProcessJob == IntPtr.Zero)
        {
            return;
        }

        try
        {
            CloseHandle(_managedProcessJob);
        } catch
        {
        } finally
        {
            _managedProcessJob = IntPtr.Zero;
        }
#endif
    }
}

