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
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using UnityEngine.SceneManagement;
using WebSocketSharp;
using WebSocketSharp.Server;

public partial class BackendManager : SingletonMonoBehaviour<BackendManager>
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [Header("Run")]
    [SerializeField] private string appUserModelId = Constant.DefaultAppUserModelId;
#endif

    private float startupTimeout = 4f;
    private float pollInterval = 0.25f;
    private int tcpTimeoutMs = 200;

    [SerializeField] private MenuDialog menuDialog;
    [SerializeField] private GameObject loadingIndicator;
    [SerializeField] private float topmostEnforceInterval = 1.0f;

    private sealed class BackendState
    {
        public bool IsShutdownProcessed;
        public bool IsBackendStartupInProgress;
        public bool IsBackendStartupIssued;
    }

    private sealed class UiProcessState
    {
        public bool IsStartupLaunchIssued;
        public int LaunchAttemptCount;
        public Coroutine TopmostEnforceCoroutine;
    }

    private Process _process;
    private Process _ollamaProcess;
    private Process _coeiroinkProcess;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private IntPtr _managedProcessJob = IntPtr.Zero;
#endif
    private static readonly object s_backendStartSync = new object();
    private volatile bool _menuOpenRequested;
    private readonly BackendState _backendState = new();
    private readonly UiProcessState _uiProcessState = new();

    // WebSocket IPC subscription
    private WebSocketServer _characterStateServer;
    private readonly ConcurrentQueue<string> _pendingCharacterStateMessages = new();
    private bool _hasLoggedSubscriberUpdateActive;

    // --- Initialization and runtime mode setup ---
    /// <summary>
    /// 実行モード判定・入力設定・AppUserModelID適用・UI監視開始などの初期化を順に実行します。
    /// </summary>
    private protected override void Awake()
    {
        base.Awake();

        Application.runInBackground = true;

        try
        {
            var inputSystemType = Type.GetType("UnityEngine.InputSystem.InputSystem, Unity.InputSystem");

            PropertyInfo settingsProperty = inputSystemType.GetProperty("settings", BindingFlags.Public | BindingFlags.Static);
            object settings = settingsProperty?.GetValue(null);

            PropertyInfo backgroundBehaviorProperty = settings.GetType().GetProperty("backgroundBehavior", BindingFlags.Public | BindingFlags.Instance);

            object ignoreFocusValue = Enum.Parse(backgroundBehaviorProperty.PropertyType, "IgnoreFocus", ignoreCase: true);
            backgroundBehaviorProperty.SetValue(settings, ignoreFocusValue);
        } catch
        {
        }

        if (_uiProcessState.TopmostEnforceCoroutine != null)
        {
            return;
        }

        _uiProcessState.TopmostEnforceCoroutine = StartCoroutine(TopmostEnforcementLoop());

        UnityEngine.Debug.Log("[BackendManager] Initializing WebSocket character state server.");
        InitializeCharacterStateServer();

        ApplySettings();
    }

    /// <summary>
    /// Character側プロセスでのみバックエンド起動を開始し、UIプロセスではスキップします。
    /// </summary>
    private void Start()
    {
        StartBackend();
        SubscribeToCompanionController();
    }

    private void SubscribeToCompanionController()
    {
        var controller = CompanionController.Instance;
        if (controller == null)
        {
            UnityEngine.Debug.LogWarning("[BackendManager] CompanionController is not available. Loading indicator will not be updated on character spawn.");
            return;
        }

        controller.OnCharacterSpawnCompleted += OnCharacterSpawnCompleted;
        UnityEngine.Debug.Log("[BackendManager] Subscribed to CompanionController.OnCharacterSpawnCompleted.");
    }

    private void OnCharacterSpawnCompleted(int characterId)
    {
        UnityEngine.Debug.Log($"[BackendManager] OnCharacterSpawnCompleted: characterId={characterId}. Hiding loading indicator.");
        VisualizeLoadingIndicator(false);
    }

    /// <summary>
    /// 重複起動をロックで抑止しつつ、設定反映・ジョブ準備・監視開始後に起動コルーチンを開始します。
    /// </summary>
    public void StartBackend()
    {
        lock (s_backendStartSync)
        {
            if (_backendState.IsBackendStartupInProgress)
            {
                return;
            }

            _backendState.IsBackendStartupInProgress = true;
        }

        ApplySettings();
        EnsureProcessJob();
        StartCoroutine(StartBackendRoutine());
    }

    /// <summary>
    /// UI/依存プロセス起動とバックエンド起動後、タイムアウトまで疎通確認をポーリングします。
    /// </summary>
    private IEnumerator StartBackendRoutine()
    {
        StartCoeiroink();

        if (!IsProcessAlive(_process) && !StartProcess())
        {
            lock (s_backendStartSync)
            {
                _backendState.IsBackendStartupInProgress = false;
            }
            yield break;
        }

        // Allow realistic server boot time; 2s cap was too aggressive and caused false startup failures.
        float timeout = Mathf.Clamp(startupTimeout, 1.0f, 120.0f);
        float poll = Mathf.Max(0.05f, pollInterval);
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            if (IsBackendAlive())
            {
                lock (s_backendStartSync)
                {
                    _backendState.IsBackendStartupInProgress = false;
                }
                yield break;
            }

            yield return new WaitForSeconds(poll);
            elapsed += poll;
        }

        lock (s_backendStartSync)
        {
            _backendState.IsBackendStartupInProgress = false;
            // Startup failed: allow a future explicit retry (e.g. from LLMProvider) instead of deadlocking the session.
            _backendState.IsBackendStartupIssued = false;
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
            return false;
        }

        if (!TryBuildStartInfo(workingDirectory, out var startInfo))
        {
            return false;
        }

        try
        {
            _process = Process.Start(startInfo);
            if (_process == null)
            {
                if (TryLaunchBackend(startInfo, out _process))
                {
                    return true;
                }

                if (TryLaunchPythonBackend(workingDirectory, out _process))
                {
                    return true;
                }

                return false;
            }

            TryAssignProcessToJob(_process);

            return true;
        } catch
        {
            if (TryLaunchBackend(startInfo, out _process))
            {
                return true;
            }

            if (TryLaunchPythonBackend(workingDirectory, out _process))
            {
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// バックエンド用の STARTUPINFO を構成し、ネイティブ CreateProcess で起動を試行します。
    /// </summary>
    private bool TryLaunchBackend(ProcessStartInfo startInfo, out Process process)
    {
        process = null;

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
                return false;
            }

            return true;
        } catch
        {
            return false;
        }
#else
        return false;
#endif
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
            process = Process.Start(fallbackInfo);

            if (process == null)
            {
                return false;
            }

            TryAssignProcessToJob(process);

            return true;
        } catch
        {
            return false;
        }
    }

    void Update()
    {
        if (_menuOpenRequested)
        {
            _menuOpenRequested = false;
            OpenMenuDialog();
        }

        TryProcessCharacterStateMessages();
    }

    private void TryProcessCharacterStateMessages()
    {
        if (_characterStateServer == null)
        {
            return;
        }

        if (!_hasLoggedSubscriberUpdateActive)
        {
            _hasLoggedSubscriberUpdateActive = true;
            UnityEngine.Debug.Log("[BackendManager] Character state WebSocket polling is active.");
        }

        while (_pendingCharacterStateMessages.TryDequeue(out string messageJson))
        {
            UnityEngine.Debug.Log($"[BackendManager] Received raw character state message: {messageJson}");
            ProcessCharacterStateMessage(messageJson);
        }
    }

    private void ProcessCharacterStateMessage(string messageJson)
    {
        if (string.Equals(messageJson?.Trim(), Constant.UIOpenMenuMessage, StringComparison.Ordinal)
            || string.Equals(messageJson?.Trim(), Constant.UIOpenSettingsDialogMessage, StringComparison.Ordinal))
        {
            UnityEngine.Debug.Log("[BackendManager] Received settings/menu open request via WebSocket.");
            _menuOpenRequested = true;
            return;
        }

        try
        {
            var message = UnityEngine.JsonUtility.FromJson<CharacterStateMessage>(messageJson);

            if (message == null)
            {
                return;
            }

            if (string.Equals(message.messageType, Constant.UIOpenMenuMessage, StringComparison.Ordinal)
                || string.Equals(message.messageType, Constant.UIOpenSettingsDialogMessage, StringComparison.Ordinal))
            {
                UnityEngine.Debug.Log("[BackendManager] Received settings/menu open request via WebSocket message envelope.");
                _menuOpenRequested = true;
                return;
            }

            CompanionController.Instance?.HandleCharacterStateMessage(message);
        } catch
        {
        }
    }

    private void OpenMenuDialog()
    {
        if (menuDialog == null)
        {
            return;
        }

        menuDialog.Show();
    }

    private void InitializeCharacterStateServer()
    {
        try
        {
            _characterStateServer = new WebSocketServer(IPAddress.Loopback, Constant.CharacterStateWebSocketPort);
            _characterStateServer.AddWebSocketService<CharacterStateWebSocketBehavior>(
                Constant.CharacterStateWebSocketPath,
                behavior => behavior.Initialize(EnqueueCharacterStateMessage));
            _characterStateServer.Start();
            UnityEngine.Debug.Log($"[BackendManager] WebSocket IPC server listening at ws://127.0.0.1:{Constant.CharacterStateWebSocketPort}{Constant.CharacterStateWebSocketPath}");
        } catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[BackendManager] Failed to initialize character state WebSocket server: {ex.Message}");
        }
    }

    private void EnqueueCharacterStateMessage(string messageJson)
    {
        if (string.IsNullOrWhiteSpace(messageJson))
        {
            return;
        }

        _pendingCharacterStateMessages.Enqueue(messageJson);
    }

    private void CleanupNetMQ()
    {
        try
        {
            if (_characterStateServer != null)
            {
                _characterStateServer.Stop();
                _characterStateServer = null;
                UnityEngine.Debug.Log("[BackendManager] Character state WebSocket server stopped.");
            }
        } catch
        {
        }
    }

    private sealed class CharacterStateWebSocketBehavior : WebSocketBehavior
    {
        private Action<string> _onMessageReceived;

        public void Initialize(Action<string> onMessageReceived)
        {
            _onMessageReceived = onMessageReceived;
        }

        protected override void OnOpen()
        {
            UnityEngine.Debug.Log("[BackendManager] Character state WebSocket client connected.");
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            if (!e.IsText || string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            _onMessageReceived?.Invoke(e.Data);
        }

        protected override void OnError(WebSocketSharp.ErrorEventArgs e)
        {
            UnityEngine.Debug.LogWarning($"[BackendManager] Character state WebSocket error: {e.Message}");
        }

        protected override void OnClose(CloseEventArgs e)
        {
            UnityEngine.Debug.Log($"[BackendManager] Character state WebSocket client closed. code={e.Code}, reason={e.Reason}");
        }
    }

    private void VisualizeLoadingIndicator(bool visible)
    {
        if (loadingIndicator == null || loadingIndicator.activeSelf == visible)
        {
            return;
        }

        loadingIndicator.SetActive(visible);
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
                return false;
            }

            resolvedFileName = fileName;
        }

        fileName = resolvedFileName;

        if (IsPythonShim(fileName))
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
    /// 指定パスが WindowsApps の python shim かを判定します。
    /// </summary>
    private static bool IsPythonShim(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        string normalized = fileName.Replace('/', '\\');
        return normalized.EndsWith("\\Microsoft\\WindowsApps\\python.exe", StringComparison.OrdinalIgnoreCase);
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
                startupTimeout = provider.Backend.StartupTimeoutSeconds;
                pollInterval = provider.Backend.PollIntervalSeconds;
                tcpTimeoutMs = provider.Backend.TcpTimeoutMs;
                applied = true;
            }
        } catch
        {
        }

        if (!applied)
        {
            return;
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

    // --- Shutdown and process cleanup ---
    /// <summary>
    /// アプリ終了時にバックエンド停止処理を実行します。
    /// </summary>
    private void OnApplicationQuit()
    {
        TryStopBackendServerOnExit();
    }

    /// <summary>
    /// 破棄時にバックエンド停止処理を実行します。
    /// </summary>
    private void OnDestroy()
    {
        var controller = CompanionController.Instance;
        if (controller != null)
        {
            controller.OnCharacterSpawnCompleted -= OnCharacterSpawnCompleted;
        }

        CleanupNetMQ();
        TryStopBackendServerOnExit();
    }

    /// <summary>
    /// 無効化時にバックエンド停止処理を実行します。
    /// </summary>
    private void OnDisable()
    {
        TryStopBackendServerOnExit();
    }

    /// <summary>
    /// 終了系イベントからの停止処理を一度だけ実行し、関連監視も停止します。
    /// </summary>
    private void TryStopBackendServerOnExit()
    {
        StopTopmostEnforcement();

        if (_backendState.IsShutdownProcessed)
        {
            return;
        }

        _backendState.IsShutdownProcessed = true;

        StopBackendInternal();
    }

    /// <summary>
    /// トップモスト維持コルーチンを停止します。
    /// </summary>
    private void StopTopmostEnforcement()
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
    private IEnumerator TopmostEnforcementLoop()
    {
        // Give the standalone player window a short moment to initialize its main handle.
        yield return new WaitForSeconds(0.25f);

        while (!_backendState.IsShutdownProcessed)
        {
            bool applied = false;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            applied = WindowsAPI.SetCurrentWindowTopmost(true);
#endif

            float interval = Mathf.Max(0.2f, topmostEnforceInterval);
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
        }

        DisposeProcessJob();
        _uiProcessState.IsStartupLaunchIssued = false;
        _uiProcessState.LaunchAttemptCount = 0;

        StopTrackedProcess(ref _process, "LangGraph");
        StopTrackedProcess(ref _ollamaProcess, "Ollama");
        StopTrackedProcess(ref _coeiroinkProcess, "COEIROINK");
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
                if (TryCreateDependencyProcess(name, startInfo, out process))
                {
                    return true;
                }

                return false;
            }

            TryAssignProcessToJob(process);

            return true;
        } catch
        {
            return false;
        }
    }

    /// <summary>
    /// 依存プロセスをネイティブ CreateProcess で起動します。
    /// </summary>
    private bool TryCreateDependencyProcess(string name, ProcessStartInfo startInfo, out Process process)
    {
        process = null;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        try
        {
            var startupInfo = new STARTUPINFO
            {
                cb = (uint)Marshal.SizeOf<STARTUPINFO>()
            };

            if (!TryCreateNativeProcess(startInfo, startupInfo, out process, out int lastError))
            {
                return false;
            }

            return true;
        } catch
        {
            return false;
        }
#else
        return false;
#endif
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

            TryKillProcessTree(process);

        } catch
        {
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

        try
        {
            if (IsProcessAlive(process))
            {
                process.Kill();
            }
        } catch
        {
        }

        return !IsProcessAlive(process);
    }

    /// <summary>
    /// 未作成なら JobObject を作成し、終了時一括終了ポリシーを設定します。
    /// </summary>
    void EnsureProcessJob()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (_managedProcessJob != IntPtr.Zero) return;

        try
        {
            _managedProcessJob = CreateJobObject(IntPtr.Zero, null);

            if (_managedProcessJob == IntPtr.Zero) return;

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
        if (process == null) return;

        EnsureProcessJob();

        if (_managedProcessJob == IntPtr.Zero) return;

        try
        {
            AssignProcessToJobObject(_managedProcessJob, process.Handle);
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

/// <summary>
/// Character state message format for NetMQ communication
/// </summary>
[System.Serializable]
public class CharacterStateMessage
{
    public string schemaVersion = "1.0";
    public string messageType;
    public string characterId;
    public long timestampUnixMs;
    public string status;
}

