using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Logging;
using UnityEngine;

[RequireComponent(typeof(JsonSettingsFileTestComponent))]
public class BackendServerStartupTestComponent : MonoBehaviour
{
    private const string ExternalUiProcessRoleArgument = "--external-ui-process";

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint STARTF_USESHOWWINDOW = 0x00000001;
    private const short SW_HIDE = 0;
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    private const string DefaultAppUserModelId = "DesktopAgent.App";
    private const ushort VT_LPWSTR = 31;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_APPWINDOW = 0x00040000L;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private static readonly PROPERTYKEY PKEY_AppUserModel_ID = new PROPERTYKEY(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public uint cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public IntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;

        public PROPERTYKEY(Guid formatId, uint propertyId)
        {
            fmtid = formatId;
            pid = propertyId;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr p;
        public int p2;
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint cProps);

        [PreserveSig]
        int GetAt(uint iProp, out PROPERTYKEY pkey);

        [PreserveSig]
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);

        [PreserveSig]
        int SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);

        [PreserveSig]
        int Commit();
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        int infoType,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SHGetPropertyStoreForWindow(
        IntPtr hwnd,
        ref Guid iid,
        [Out, MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

#endif

    private enum BackendLaunchMethod
    {
        CustomExecutable = 0,
        PythonModule = 1,
        PyLauncherScript = 2,
    }

    [Header("Run")]
    [SerializeField] private bool runOnStart = true;
    [SerializeField] private bool runInExternalUiProcess = false;
    [SerializeField] private bool allowInputWhenUnfocusedInExternalUiProcess = true;
    [SerializeField] private bool stopBackendServerOnExit = true;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [SerializeField] private string appUserModelId = DefaultAppUserModelId;
    [SerializeField] private bool hideExternalUiTaskbarButton = true;
#endif

    private JsonSettingsFileTestComponent jsonSettingsSource;

    private bool startBackendServer = true;
    private BackendLaunchMethod launchMethod = BackendLaunchMethod.PythonModule;

    private string executable = "python";
    private string arguments = "-m app.server";

    private string pythonExecutable = "python";
    private string pythonModuleName = "app.server";
    private string pythonModuleArguments = "";

    private string pyLauncherScriptPath = "app/server.py";
    private string pyLauncherExtraArguments = "";

    private string workingDirectoryRelativePath = "BackendServer";
    private bool useProjectRootAsWorkingDirectoryWhenEmpty = true;
    private bool logProcessOutput = true;

    private bool startOllamaServer = true;
    private string ollamaExecutable = "ollama.exe";
    private string ollamaArguments = "serve";
    private string ollamaHealthHost = "127.0.0.1";
    private int ollamaHealthPort = 11434;

    private bool startCoeiroinkServer = true;
    private string coeiroinkExecutable = "engine.exe";
    private string coeiroinkArguments = "";
    private string coeiroinkHealthHost = "127.0.0.1";
    private int coeiroinkHealthPort = 50032;

    private bool startExternalUiProcess = true;
    private string externalUiExecutablePath = "UI/DesktopAgentUI.exe";
    private string externalUiArguments = "";
    private string externalUiProcessName = "DesktopAgentUI";
    private bool stopExternalUiProcessOnExit = true;

    [Header("Optional Reachability")]
    [SerializeField] private bool checkCoeiroinkReachability;

    private string backendHost = "127.0.0.1";
    private int backendPort = 8000;
    private string backendHealthCheckPath = "/health";
    private int backendHealthCheckHttpTimeoutMs = 200;

    private string coeiroinkBaseUrl = "http://127.0.0.1:50032";
    private int[] coeiroinkFallbackPorts = { 50032, 50031, 50021 };

    private float startupTimeoutSeconds = 4f;
    private float pollIntervalSeconds = 0.25f;
    private int tcpTimeoutMs = 200;

    private Process _process;
    private Process _ollamaProcess;
    private Process _coeiroinkProcess;
    private Process _externalUiProcess;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private IntPtr _managedProcessJob = IntPtr.Zero;
#endif
    private bool _shutdownProcessed;
    private bool _isExternalUiProcess;
    private bool _healthPathNotFoundLogged;

    private void Awake()
    {
        _isExternalUiProcess = IsExternalUiProcess();
        ConfigureExternalUiBackgroundInput();
        ApplyAppUserModelIdIfNeeded();

        if (jsonSettingsSource == null)
        {
            jsonSettingsSource = GetComponent<JsonSettingsFileTestComponent>();
        }

        if (jsonSettingsSource == null)
        {
            Log.Error("[BackendServerStartupTest] JsonSettingsFileTestComponent is required on the same GameObject.");
        }

        ApplySettingsFromJsonSource();
    }

    private void ConfigureExternalUiBackgroundInput()
    {
        if (!_isExternalUiProcess || !allowInputWhenUnfocusedInExternalUiProcess)
        {
            return;
        }

        Application.runInBackground = true;

        try
        {
            var inputSystemType = Type.GetType("UnityEngine.InputSystem.InputSystem, Unity.InputSystem");
            if (inputSystemType == null)
            {
                Log.Info("[BackendServerStartupTest] External UI background input: InputSystem not found. runInBackground only.");
                return;
            }

            PropertyInfo settingsProperty = inputSystemType.GetProperty("settings", BindingFlags.Public | BindingFlags.Static);
            object settings = settingsProperty?.GetValue(null);
            if (settings == null)
            {
                Log.Info("[BackendServerStartupTest] External UI background input: InputSystem settings unavailable. runInBackground only.");
                return;
            }

            PropertyInfo backgroundBehaviorProperty = settings.GetType().GetProperty("backgroundBehavior", BindingFlags.Public | BindingFlags.Instance);
            if (backgroundBehaviorProperty?.PropertyType == null)
            {
                Log.Info("[BackendServerStartupTest] External UI background input: backgroundBehavior property unavailable.");
                return;
            }

            object ignoreFocusValue = Enum.Parse(backgroundBehaviorProperty.PropertyType, "IgnoreFocus", ignoreCase: true);
            backgroundBehaviorProperty.SetValue(settings, ignoreFocusValue);
            Log.Info("[BackendServerStartupTest] External UI background input enabled (runInBackground + InputSystem.IgnoreFocus).");
        }
        catch (Exception ex)
        {
            Log.Warning($"[BackendServerStartupTest] External UI background input setup failed: {ex.GetType().Name}:{ex.Message}");
        }
    }

    private void ApplyAppUserModelIdIfNeeded()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        string appId = (appUserModelId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(appId))
        {
            appId = DefaultAppUserModelId;
        }

        try
        {
            int hr = SetCurrentProcessExplicitAppUserModelID(appId);
            if (hr != 0)
            {
                Log.Warning($"[BackendServerStartupTest] SetCurrentProcessExplicitAppUserModelID failed. hr=0x{hr:X8}, appId={appId}");
            }
            else
            {
                Log.Info($"[BackendServerStartupTest] AppUserModelID applied: {appId}");
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[BackendServerStartupTest] AppUserModelID apply failed: {ex.Message}");
        }
#endif
    }

    [ContextMenu("Start Managed Backend")]
    public void StartManagedBackend()
    {
        ApplySettingsFromJsonSource();
        EnsureManagedProcessJob();
        StopAllCoroutines();
        StartCoroutine(StartManagedBackendRoutine());
    }

    [ContextMenu("Stop Managed Backend")]
    public void StopManagedBackend()
    {
        StopManagedBackendInternal();
    }

    [ContextMenu("Run Health Check")]
    public void RunHealthCheck()
    {
        bool reachable = IsBackendReachable();
        Log.Info(reachable
            ? $"[BackendServerStartupTest] backend reachable at {backendHost}:{backendPort}"
            : $"[BackendServerStartupTest] backend not reachable at {backendHost}:{backendPort}");
    }

    public bool IsBackendAlive()
    {
        return IsBackendReachable();
    }

    private void Start()
    {
        if (_isExternalUiProcess && !runInExternalUiProcess)
        {
            Log.Info("[BackendServerStartupTest] Start skipped in external-ui process.");
            return;
        }

        if (!runOnStart)
        {
            return;
        }
        StartManagedBackend();
    }

    private IEnumerator StartManagedBackendRoutine()
    {
        Log.Info($"[BackendServerStartupTest] begin host={backendHost} port={backendPort} timeout={startupTimeoutSeconds:0.##}s");

        StartExternalUiIfNeeded();
        StartOllamaIfNeeded();
        StartCoeiroinkIfNeeded();

        if (startBackendServer)
        {
            if (_process != null && !_process.HasExited)
            {
                Log.Info("[BackendServerStartupTest] process already running.");
            }
            else if (!StartProcess())
            {
                yield break;
            }
        }

        float timeout = Mathf.Clamp(startupTimeoutSeconds, 0.1f, 2.0f);
        float poll = Mathf.Max(0.05f, pollIntervalSeconds);
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            if (IsBackendReachable())
            {
                Log.Info($"[BackendServerStartupTest] backend reachable at {backendHost}:{backendPort} elapsed={elapsed:0.##}s");

                if (checkCoeiroinkReachability)
                {
                    if (TryResolveCoeiroinkEndpoint(out var coeiroinkHost, out var coeiroinkPort) && IsTcpReachable(coeiroinkHost, coeiroinkPort))
                    {
                        Log.Info($"[BackendServerStartupTest] coeiroink reachable at {coeiroinkHost}:{coeiroinkPort}");
                    }
                    else
                    {
                        Log.Warning("[BackendServerStartupTest] coeiroink is not reachable.");
                    }
                }

                yield break;
            }

            yield return new WaitForSeconds(poll);
            elapsed += poll;
        }

        Log.Error($"[BackendServerStartupTest] timeout: backend not reachable at {backendHost}:{backendPort}");
    }

    private bool StartProcess()
    {
        string workingDirectory = ResolveWorkingDirectory();
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            Log.Error($"[BackendServerStartupTest] WorkingDirectory not found: {workingDirectory}");
            return false;
        }

        if (!TryBuildStartInfo(workingDirectory, out var startInfo))
        {
            return false;
        }

        Log.Info($"[BackendServerStartupTest] starting process: {startInfo.FileName} {startInfo.Arguments}");
        Log.Info($"[BackendServerStartupTest] WorkingDirectory: {startInfo.WorkingDirectory}");

        try
        {
            _process = Process.Start(startInfo);
            if (_process == null)
            {
                if (TryStartBackendViaNativeCreateProcess(startInfo, out _process, out string nativeSuccessMessage))
                {
                    Log.Info(nativeSuccessMessage);
                    return true;
                }

                Log.Error("[BackendServerStartupTest] Process.Start returned null.");
                return false;
            }

            if (logProcessOutput)
            {
                WireProcessOutput(_process);
            }

            TryAssignProcessToManagedJob(_process);

            return true;
        }
        catch (Win32Exception ex)
        {
            if (TryStartBackendViaNativeCreateProcess(startInfo, out _process, out string nativeSuccessMessage))
            {
                Log.Info(nativeSuccessMessage);
                return true;
            }

            Log.Error($"[BackendServerStartupTest] process start failed (Win32): {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"[BackendServerStartupTest] process start failed: {ex.Message}");
            return false;
        }
    }

    private bool TryStartBackendViaNativeCreateProcess(ProcessStartInfo startInfo, out Process process, out string successMessage)
    {
        process = null;
        successMessage = string.Empty;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        try
        {
            var startupInfo = new STARTUPINFO
            {
                cb = (uint)Marshal.SizeOf<STARTUPINFO>(),
                dwFlags = STARTF_USESHOWWINDOW,
                wShowWindow = SW_HIDE
            };

            string commandLine = BuildNativeCommandLine(startInfo.FileName, startInfo.Arguments);

            bool created = CreateProcess(
                startInfo.FileName,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CREATE_NO_WINDOW,
                IntPtr.Zero,
                startInfo.WorkingDirectory,
                ref startupInfo,
                out PROCESS_INFORMATION pi);

            if (!created)
            {
                int lastError = Marshal.GetLastWin32Error();
                Log.Warning($"[BackendServerStartupTest] Backend native CreateProcess failed. error={lastError}, file={startInfo.FileName}, args={startInfo.Arguments}, cwd={startInfo.WorkingDirectory}");
                return false;
            }

            try
            {
                process = Process.GetProcessById((int)pi.dwProcessId);
                TryAssignProcessHandleToManagedJob(pi.hProcess);
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

            successMessage = $"[BackendServerStartupTest] process started (native CreateProcess): {startInfo.FileName} {startInfo.Arguments}";
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"[BackendServerStartupTest] Backend native CreateProcess exception: {ex.GetType().Name}:{ex.Message}");
            return false;
        }
#else
        return false;
#endif
    }

    private bool TryBuildStartInfo(string workingDirectory, out ProcessStartInfo startInfo)
    {
        startInfo = null;

        string fileName;
        string args;

        switch (launchMethod)
        {
            case BackendLaunchMethod.CustomExecutable:
                fileName = executable?.Trim() ?? string.Empty;
                args = arguments?.Trim() ?? string.Empty;
                break;

            case BackendLaunchMethod.PythonModule:
                string moduleName = pythonModuleName?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(moduleName))
                {
                    Log.Error("[BackendServerStartupTest] PythonModule launch selected but module name is empty.");
                    return false;
                }

                fileName = string.IsNullOrWhiteSpace(pythonExecutable) ? "python" : pythonExecutable.Trim();
                string moduleArgs = pythonModuleArguments?.Trim() ?? string.Empty;
                args = string.IsNullOrWhiteSpace(moduleArgs)
                    ? $"-m {moduleName}"
                    : $"-m {moduleName} {moduleArgs}";
                break;

            case BackendLaunchMethod.PyLauncherScript:
                string scriptPath = pyLauncherScriptPath?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(scriptPath))
                {
                    Log.Error("[BackendServerStartupTest] PyLauncherScript launch selected but script path is empty.");
                    return false;
                }

                fileName = "py";
                string extraArgs = pyLauncherExtraArguments?.Trim() ?? string.Empty;
                args = string.IsNullOrWhiteSpace(extraArgs)
                    ? scriptPath
                    : $"{scriptPath} {extraArgs}";
                break;

            default:
                Log.Error($"[BackendServerStartupTest] Unsupported launch method: {launchMethod}");
                return false;
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            Log.Error($"[BackendServerStartupTest] launch executable is empty. method={launchMethod}");
            return false;
        }

        string resolvedFileName = ResolveBackendExecutablePath(fileName, workingDirectory, launchMethod);
        if (string.IsNullOrWhiteSpace(resolvedFileName))
        {
            Log.Error($"[BackendServerStartupTest] launch executable could not be resolved. file={fileName}, workingDir={workingDirectory}, method={launchMethod}");
            return false;
        }

        fileName = resolvedFileName;

        startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = logProcessOutput,
            RedirectStandardError = logProcessOutput
        };

        return true;
    }

    private static string ResolveBackendExecutablePath(string fileName, string workingDirectory, BackendLaunchMethod method)
    {
        string resolved = ResolveExecutablePath(fileName, workingDirectory);
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return NormalizePathForProcessStart(resolved);
        }

        if (method != BackendLaunchMethod.PythonModule || !IsPythonCommandName(fileName))
        {
            return string.Empty;
        }

        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        string[] pythonCandidates =
        {
            Path.Combine(workingDirectory ?? string.Empty, ".venv", "Scripts", "python.exe"),
            Path.Combine(workingDirectory ?? string.Empty, "venv", "Scripts", "python.exe"),
            Path.Combine(projectRoot, ".venv", "Scripts", "python.exe"),
            Path.Combine(projectRoot, "venv", "Scripts", "python.exe")
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
            }
            catch
            {
            }
        }

        string pyLauncher = ResolveExecutablePath("py.exe", workingDirectory);
        if (!string.IsNullOrWhiteSpace(pyLauncher))
        {
            string resolvedPython = TryResolvePythonExecutableViaPyLauncher(pyLauncher, workingDirectory);
            if (!string.IsNullOrWhiteSpace(resolvedPython))
            {
                return NormalizePathForProcessStart(resolvedPython);
            }

            return NormalizePathForProcessStart(pyLauncher);
        }

        return string.Empty;
    }

    private static string TryResolvePythonExecutableViaPyLauncher(string pyLauncherPath, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(pyLauncherPath) || !File.Exists(pyLauncherPath))
        {
            return string.Empty;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = pyLauncherPath,
                Arguments = "-c \"import sys;print(sys.executable)\"",
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Application.dataPath : workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var probe = Process.Start(startInfo);
            if (probe == null)
            {
                return string.Empty;
            }

            string output = probe.StandardOutput.ReadToEnd()?.Trim() ?? string.Empty;
            probe.WaitForExit(3000);

            if (!string.IsNullOrWhiteSpace(output) && File.Exists(output))
            {
                return output;
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static bool IsPythonCommandName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        string normalizedName = Path.GetFileName(fileName).Trim();
        return string.Equals(normalizedName, "python", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedName, "python.exe", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplySettingsFromJsonSource()
    {
        if (jsonSettingsSource == null)
        {
            return;
        }

        if (!jsonSettingsSource.TryGetBackendStartupSettings(out var settings) || settings == null)
        {
            return;
        }

        startBackendServer = settings.startBackendServer;

        int launch = Mathf.Clamp(settings.launchMethod, 0, 2);
        launchMethod = (BackendLaunchMethod)launch;

        executable = settings.executable ?? executable;
        arguments = settings.arguments ?? arguments;

        pythonExecutable = settings.pythonExecutable ?? pythonExecutable;
        pythonModuleName = settings.pythonModuleName ?? pythonModuleName;
        pythonModuleArguments = settings.pythonModuleArguments ?? pythonModuleArguments;

        pyLauncherScriptPath = settings.pyLauncherScriptPath ?? pyLauncherScriptPath;
        pyLauncherExtraArguments = settings.pyLauncherExtraArguments ?? pyLauncherExtraArguments;

        workingDirectoryRelativePath = settings.workingDirectoryRelativePath ?? workingDirectoryRelativePath;
        useProjectRootAsWorkingDirectoryWhenEmpty = settings.useProjectRootAsWorkingDirectoryWhenEmpty;
        logProcessOutput = settings.logProcessOutput;
        startOllamaServer = settings.startOllamaServer;
        ollamaExecutable = settings.ollamaExecutable ?? ollamaExecutable;
        ollamaArguments = settings.ollamaArguments ?? ollamaArguments;
        ollamaHealthHost = settings.ollamaHealthHost ?? ollamaHealthHost;
        ollamaHealthPort = settings.ollamaHealthPort;
        startCoeiroinkServer = settings.startCoeiroinkServer;
        coeiroinkExecutable = settings.coeiroinkExecutable ?? coeiroinkExecutable;
        coeiroinkArguments = settings.coeiroinkArguments ?? coeiroinkArguments;
        coeiroinkHealthHost = settings.coeiroinkHealthHost ?? coeiroinkHealthHost;
        coeiroinkHealthPort = settings.coeiroinkHealthPort;
        startExternalUiProcess = settings.startExternalUiProcess;
        externalUiExecutablePath = settings.externalUiExecutablePath ?? externalUiExecutablePath;
        externalUiArguments = settings.externalUiArguments ?? externalUiArguments;
        externalUiProcessName = settings.externalUiProcessName ?? externalUiProcessName;
        stopExternalUiProcessOnExit = settings.stopExternalUiProcessOnExit;

        backendHost = settings.backendHost ?? backendHost;
        backendPort = settings.backendPort;
        backendHealthCheckPath = settings.backendHealthCheckPath ?? backendHealthCheckPath;
        backendHealthCheckHttpTimeoutMs = settings.backendHealthCheckHttpTimeoutMs;

        startupTimeoutSeconds = settings.startupTimeoutSeconds;
        pollIntervalSeconds = settings.pollIntervalSeconds;
        tcpTimeoutMs = settings.tcpTimeoutMs;

        if (jsonSettingsSource.TryGetCoeiroinkSettings(out var coeiroinkSettings) && coeiroinkSettings != null)
        {
            coeiroinkBaseUrl = coeiroinkSettings.baseUrl ?? coeiroinkBaseUrl;
            coeiroinkFallbackPorts = coeiroinkSettings.fallbackPorts ?? coeiroinkFallbackPorts;
        }
    }

    private void WireProcessOutput(Process process)
    {
        try
        {
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    Log.Info($"[BackendServerStartupTest][stdout] {e.Data}");
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    Log.Warning($"[BackendServerStartupTest][stderr] {e.Data}");
                }
            };
            process.Exited += (_, __) =>
            {
                try
                {
                    Log.Warning($"[BackendServerStartupTest] process exited. ExitCode={process.ExitCode}");
                }
                catch
                {
                    Log.Warning("[BackendServerStartupTest] process exited.");
                }
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            Log.Warning($"[BackendServerStartupTest] Failed to wire process output: {ex.Message}");
        }
    }

    private string ResolveWorkingDirectory()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        string relative = workingDirectoryRelativePath?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(relative))
        {
            return useProjectRootAsWorkingDirectoryWhenEmpty ? projectRoot : string.Empty;
        }

        if (Path.IsPathRooted(relative))
        {
            return Path.GetFullPath(relative);
        }

        return Path.GetFullPath(Path.Combine(projectRoot, relative));
    }

    private bool IsBackendReachable()
    {
        string path = NormalizeHttpPath(backendHealthCheckPath);
        if (!string.IsNullOrWhiteSpace(path))
        {
            int timeoutMs = Mathf.Max(100, backendHealthCheckHttpTimeoutMs);
            if (TryGetHttpStatusCode(backendHost, backendPort, path, timeoutMs, out int statusCode))
            {
                return statusCode >= 200 && statusCode < 300;
            }

            if (statusCode == 404)
            {
                if (!_healthPathNotFoundLogged)
                {
                    _healthPathNotFoundLogged = true;
                    Log.Warning($"[BackendServerStartupTest] Health path '{path}' returned 404. Fallback to TCP health check at {backendHost}:{backendPort}.");
                }

                return IsTcpReachable(backendHost, backendPort);
            }

            return false;
        }

        return IsTcpReachable(backendHost, backendPort);
    }

    private void OnApplicationQuit()
    {
        TryStopBackendServerOnExit();
    }

    private void OnDestroy()
    {
        TryStopBackendServerOnExit();
    }

    private void OnDisable()
    {
        TryStopBackendServerOnExit();
    }

    private void TryStopBackendServerOnExit()
    {
        if (_isExternalUiProcess && !runInExternalUiProcess)
        {
            return;
        }

        if (_shutdownProcessed || !stopBackendServerOnExit)
        {
            return;
        }

        _shutdownProcessed = true;

        StopManagedBackendInternal();
        Log.Info("[BackendServerStartupTest] backend stop requested on exit.");
    }

    private void StopManagedBackendInternal()
    {
        DisposeManagedProcessJob();

        StopTrackedProcess(ref _process, "managed backend");
        StopTrackedProcess(ref _ollamaProcess, "managed ollama");
        StopTrackedProcess(ref _coeiroinkProcess, "managed COEIROINK");

        if (stopExternalUiProcessOnExit)
        {
            StopTrackedProcess(ref _externalUiProcess, "managed external UI");
        }
    }

    private void StartExternalUiIfNeeded()
    {
        if (!startExternalUiProcess)
        {
            Log.Info("[BackendServerStartupTest] External UI start skipped: startExternalUiProcess=false");
            return;
        }

        if (IsExternalUiRunning())
        {
            Log.Info("[BackendServerStartupTest] External UI start skipped: process already running.");
            return;
        }

        if (_externalUiProcess != null && !_externalUiProcess.HasExited)
        {
            Log.Info($"[BackendServerStartupTest] External UI start skipped: tracked process alive (pid={_externalUiProcess.Id}).");
            return;
        }

        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        string resolvedExecutable = ResolveExecutablePath(externalUiExecutablePath, projectRoot);
        if (string.IsNullOrWhiteSpace(resolvedExecutable))
        {
            Log.Warning($"[BackendServerStartupTest] External UI executable not found: {externalUiExecutablePath}");
            return;
        }

        resolvedExecutable = NormalizePathForProcessStart(resolvedExecutable);

        string uiArgs = (externalUiArguments ?? string.Empty).Trim();
        if (!uiArgs.Contains(ExternalUiProcessRoleArgument, StringComparison.OrdinalIgnoreCase))
        {
            uiArgs = string.IsNullOrWhiteSpace(uiArgs)
                ? ExternalUiProcessRoleArgument
                : uiArgs + " " + ExternalUiProcessRoleArgument;
        }

        string workingDirectory = NormalizePathForProcessStart(Path.GetDirectoryName(resolvedExecutable) ?? projectRoot);
        var startInfo = new ProcessStartInfo
        {
            FileName = resolvedExecutable,
            Arguments = uiArgs,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        Log.Info($"[BackendServerStartupTest] External UI start requested. file={startInfo.FileName}, args={startInfo.Arguments}, cwd={startInfo.WorkingDirectory}");

        if (TryStartExternalUiViaNativeCreateProcess(startInfo, out string successMessage))
        {
            Log.Info(successMessage);
            return;
        }

        Log.Warning(
            $"[BackendServerStartupTest] External UI start failed. file={startInfo.FileName}, args={startInfo.Arguments}, cwd={startInfo.WorkingDirectory}, exists={File.Exists(startInfo.FileName)}");
    }

    private bool TryStartExternalUiViaNativeCreateProcess(ProcessStartInfo startInfo, out string successMessage)
    {
        successMessage = string.Empty;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        try
        {
            var startupInfo = new STARTUPINFO
            {
                cb = (uint)Marshal.SizeOf<STARTUPINFO>(),
                dwFlags = STARTF_USESHOWWINDOW,
                wShowWindow = SW_HIDE
            };

            string commandLine = string.IsNullOrWhiteSpace(startInfo.Arguments)
                ? string.Empty
                : startInfo.Arguments;

            bool created = CreateProcess(
                startInfo.FileName,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CREATE_NO_WINDOW,
                IntPtr.Zero,
                startInfo.WorkingDirectory,
                ref startupInfo,
                out PROCESS_INFORMATION pi);

            if (!created)
            {
                int lastError = Marshal.GetLastWin32Error();
                Log.Warning($"[BackendServerStartupTest] Native CreateProcess failed. error={lastError}, file={startInfo.FileName}, args={startInfo.Arguments}, cwd={startInfo.WorkingDirectory}");
                return false;
            }

            try
            {
                _externalUiProcess = Process.GetProcessById((int)pi.dwProcessId);
                TryAssignProcessHandleToManagedJob(pi.hProcess);
            }
            catch
            {
                _externalUiProcess = null;
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

            successMessage = $"[BackendServerStartupTest] External UI process started (native CreateProcess): {startInfo.FileName} {startInfo.Arguments}";
            TryConfigureExternalUiTaskbarVisibility(_externalUiProcess);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"[BackendServerStartupTest] Native CreateProcess exception: {ex.GetType().Name}:{ex.Message}");
            return false;
        }
#else
        return false;
#endif
    }

    private void TryConfigureExternalUiTaskbarVisibility(Process process)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (process == null)
        {
            Log.Warning("[BackendServerStartupTest] External UI taskbar config skipped: process handle is null.");
            return;
        }

        string appId = (appUserModelId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(appId))
        {
            appId = DefaultAppUserModelId;
        }

        try
        {
            const int maxAttempts = 20;
            Log.Info($"[BackendServerStartupTest] External UI taskbar config requested. pid={process.Id}, hideButton={hideExternalUiTaskbarButton}, appId={appId}");
            for (int i = 0; i < maxAttempts; i++)
            {
                IntPtr hwnd = IntPtr.Zero;
                try
                {
                    process.Refresh();
                    hwnd = process.MainWindowHandle;
                }
                catch
                {
                    return;
                }

                if (hwnd != IntPtr.Zero)
                {
                    bool appIdApplied = TrySetWindowAppUserModelId(hwnd, appId);
                    bool hideApplied = !hideExternalUiTaskbarButton || TryHideWindowTaskbarButton(hwnd);

                    if (appIdApplied && hideApplied)
                    {
                        Log.Info($"[BackendServerStartupTest] External UI taskbar config applied. appId={appIdApplied}, hidden={hideApplied}");
                    }
                    else
                    {
                        Log.Warning($"[BackendServerStartupTest] External UI taskbar config failed. appId={appIdApplied}, hidden={hideApplied}");
                    }
                    return;
                }

                Thread.Sleep(100);
            }

            Log.Warning($"[BackendServerStartupTest] External UI taskbar config skipped: main window handle not found within {maxAttempts * 100}ms.");
        }
        catch (Exception ex)
        {
            Log.Warning($"[BackendServerStartupTest] External UI taskbar config exception: {ex.GetType().Name}:{ex.Message}");
        }
#endif
    }

    private static bool TryHideWindowTaskbarButton(IntPtr windowHandle)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        IntPtr stylePtr = GetWindowLongPtrCompat(windowHandle, GWL_EXSTYLE);
        long style = stylePtr.ToInt64();
        long targetStyle = (style | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;

        if (targetStyle != style)
        {
            Marshal.GetLastWin32Error();
            IntPtr previous = SetWindowLongPtrCompat(windowHandle, GWL_EXSTYLE, new IntPtr(targetStyle));
            int err = Marshal.GetLastWin32Error();
            if (previous == IntPtr.Zero && err != 0)
            {
                return false;
            }
        }

        SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

        long verify = GetWindowLongPtrCompat(windowHandle, GWL_EXSTYLE).ToInt64();
        bool hasToolWindow = (verify & WS_EX_TOOLWINDOW) != 0;
        bool hasAppWindow = (verify & WS_EX_APPWINDOW) != 0;
        return hasToolWindow && !hasAppWindow;
#else
        return false;
#endif
    }

    private static IntPtr GetWindowLongPtrCompat(IntPtr hWnd, int nIndex)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLongPtr32(hWnd, nIndex);
#else
        return IntPtr.Zero;
#endif
    }

    private static IntPtr SetWindowLongPtrCompat(IntPtr hWnd, int nIndex, IntPtr newValue)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, newValue) : SetWindowLongPtr32(hWnd, nIndex, newValue);
#else
        return IntPtr.Zero;
#endif
    }

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
            vt = VT_LPWSTR,
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
        }
        finally
        {
            PropVariantClear(ref pv);
            Marshal.ReleaseComObject(store);
        }
#else
        return false;
#endif
    }

    private void StartOllamaIfNeeded()
    {
        if (!startOllamaServer)
        {
            Log.Info("[BackendServerStartupTest] Ollama start skipped: startOllamaServer=false");
            return;
        }

        if (IsOllamaServerReachable())
        {
            Log.Info($"[BackendServerStartupTest] Ollama start skipped: already reachable at {ollamaHealthHost}:{ollamaHealthPort}");
            return;
        }

        if (_ollamaProcess != null && !_ollamaProcess.HasExited)
        {
            Log.Info($"[BackendServerStartupTest] Ollama start skipped: existing launched process is alive (pid={_ollamaProcess.Id})");
            return;
        }

        if (!TryStartDependencyProcess("Ollama", ollamaExecutable, ollamaArguments, ResolveWorkingDirectory(), out var process))
        {
            return;
        }

        _ollamaProcess = process;
    }

    private bool IsOllamaServerReachable()
    {
        const string versionPath = "/api/version";
        const string tagsPath = "/api/tags";
        int timeoutMs = Mathf.Max(100, backendHealthCheckHttpTimeoutMs);

        if (TryGetHttpStatusCode(ollamaHealthHost, ollamaHealthPort, versionPath, timeoutMs, out int statusCode) && statusCode >= 200 && statusCode < 300)
        {
            return true;
        }

        if (statusCode == (int)HttpStatusCode.NotFound && TryGetHttpStatusCode(ollamaHealthHost, ollamaHealthPort, tagsPath, timeoutMs, out int tagsStatusCode) && tagsStatusCode >= 200 && tagsStatusCode < 300)
        {
            return true;
        }

        return false;
    }

    private void StartCoeiroinkIfNeeded()
    {
        if (!startCoeiroinkServer)
        {
            return;
        }

        if (IsTcpReachable(coeiroinkHealthHost, coeiroinkHealthPort))
        {
            return;
        }

        if (_coeiroinkProcess != null && !_coeiroinkProcess.HasExited)
        {
            return;
        }

        if (!TryStartDependencyProcess("COEIROINK", coeiroinkExecutable, coeiroinkArguments, ResolveWorkingDirectory(), out var process))
        {
            return;
        }

        _coeiroinkProcess = process;
    }

    private bool TryStartDependencyProcess(string name, string executableNameOrPath, string args, string workingDirectory, out Process process)
    {
        process = null;

        string resolvedExecutable = ResolveExecutablePath(executableNameOrPath, workingDirectory);
        if (string.IsNullOrWhiteSpace(resolvedExecutable))
        {
            Log.Warning($"[BackendServerStartupTest] {name} executable not found: {executableNameOrPath}. workingDir={workingDirectory}");
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
            CreateNoWindow = true,
            RedirectStandardOutput = logProcessOutput,
            RedirectStandardError = logProcessOutput
        };

        try
        {
            process = Process.Start(startInfo);
            if (process == null)
            {
                if (TryStartDependencyViaNativeCreateProcess(name, startInfo, out process, out string nativeSuccessMessage))
                {
                    Log.Info(nativeSuccessMessage);
                    return true;
                }

                Log.Warning($"[BackendServerStartupTest] {name} Process.Start returned null.");
                return false;
            }

            if (logProcessOutput)
            {
                WireProcessOutput(process);
            }

            TryAssignProcessToManagedJob(process);

            Log.Info($"[BackendServerStartupTest] {name} process started: {startInfo.FileName} {startInfo.Arguments}");
            return true;
        }
        catch (Win32Exception ex)
        {
            if (TryStartDependencyViaNativeCreateProcess(name, startInfo, out process, out string nativeSuccessMessage))
            {
                Log.Info(nativeSuccessMessage);
                return true;
            }

            Log.Warning($"[BackendServerStartupTest] {name} start failed (Win32): {ex.Message}. file={startInfo.FileName}, args={startInfo.Arguments}, cwd={startInfo.WorkingDirectory}");
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning($"[BackendServerStartupTest] {name} start failed: {ex.Message}");
            return false;
        }
    }

    private bool TryStartDependencyViaNativeCreateProcess(string name, ProcessStartInfo startInfo, out Process process, out string successMessage)
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

            string commandLine = BuildNativeCommandLine(startInfo.FileName, startInfo.Arguments);

            bool created = CreateProcess(
                startInfo.FileName,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CREATE_NO_WINDOW,
                IntPtr.Zero,
                startInfo.WorkingDirectory,
                ref startupInfo,
                out PROCESS_INFORMATION pi);

            if (!created)
            {
                int lastError = Marshal.GetLastWin32Error();
                Log.Warning($"[BackendServerStartupTest] {name} native CreateProcess failed. error={lastError}, file={startInfo.FileName}, args={startInfo.Arguments}, cwd={startInfo.WorkingDirectory}");
                return false;
            }

            try
            {
                process = Process.GetProcessById((int)pi.dwProcessId);
                TryAssignProcessHandleToManagedJob(pi.hProcess);
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

            successMessage = $"[BackendServerStartupTest] {name} process started (native CreateProcess): {startInfo.FileName} {startInfo.Arguments}";
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"[BackendServerStartupTest] {name} native CreateProcess exception: {ex.GetType().Name}:{ex.Message}");
            return false;
        }
#else
        return false;
#endif
    }

    private static string BuildNativeCommandLine(string fileName, string arguments)
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

    private static bool IsProcessAlive(Process process)
    {
        if (process == null)
        {
            return false;
        }

        try
        {
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

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
            TryResolveCoeiroinkEnginePathFromCommonLocations(out string coeiroinkEnginePath))
        {
            return coeiroinkEnginePath;
        }

        return string.Empty;
    }

    private static bool TryResolveCoeiroinkEnginePathFromCommonLocations(out string resolvedPath)
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
                    }
                    catch
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
            }
            catch
            {
            }
        }

        return false;
    }

    private static string NormalizePathForProcessStart(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path).Replace('/', '\\');
        }
        catch
        {
            return path.Replace('/', '\\');
        }
    }

    private bool IsExternalUiRunning()
    {
        if (_externalUiProcess != null && !_externalUiProcess.HasExited)
        {
            return true;
        }

        string processName = (externalUiProcessName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(processName))
        {
            processName = Path.GetFileNameWithoutExtension(externalUiExecutablePath ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        try
        {
            var processes = Process.GetProcessesByName(processName);
            for (int i = 0; i < processes.Length; i++)
            {
                var process = processes[i];
                if (process != null && !process.HasExited)
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

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
        }
        catch (WebException ex) when (ex.Response is HttpWebResponse httpResponse)
        {
            statusCode = (int)httpResponse.StatusCode;
            return false;
        }
        catch
        {
            return false;
        }
    }

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
                Log.Info($"[BackendServerStartupTest] {label} process stopped.");
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[BackendServerStartupTest] {label} stop failed: {ex.Message}");
        }
        finally
        {
            process = null;
        }
    }

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
        }
        catch
        {
        }

        try
        {
            if (IsProcessAlive(process))
            {
                process.Kill();
            }
        }
        catch
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
            }
            catch
            {
            }
        }
#endif

        return !IsProcessAlive(process);
    }

    private bool TryResolveCoeiroinkEndpoint(out string host, out int port)
    {
        host = "127.0.0.1";
        port = 50032;

        if (!Uri.TryCreate(coeiroinkBaseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        host = uri.Host;
        int initialPort = uri.Port > 0 ? uri.Port : 50032;
        if (IsTcpReachable(host, initialPort))
        {
            port = initialPort;
            return true;
        }

        if (coeiroinkFallbackPorts == null || coeiroinkFallbackPorts.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < coeiroinkFallbackPorts.Length; i++)
        {
            int fallbackPort = coeiroinkFallbackPorts[i];
            if (fallbackPort <= 0)
            {
                continue;
            }

            if (IsTcpReachable(host, fallbackPort))
            {
                port = fallbackPort;
                return true;
            }
        }

        return false;
    }

    private static bool IsExternalUiProcess()
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
                if (string.Equals(args[i], ExternalUiProcessRoleArgument, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private void EnsureManagedProcessJob()
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
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

            int length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            IntPtr infoPtr = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(info, infoPtr, false);
                if (!SetInformationJobObject(_managedProcessJob, JobObjectExtendedLimitInformation, infoPtr, (uint)length))
                {
                    CloseHandle(_managedProcessJob);
                    _managedProcessJob = IntPtr.Zero;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(infoPtr);
            }
        }
        catch
        {
            if (_managedProcessJob != IntPtr.Zero)
            {
                CloseHandle(_managedProcessJob);
                _managedProcessJob = IntPtr.Zero;
            }
        }
#endif
    }

    private void TryAssignProcessToManagedJob(Process process)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (process == null)
        {
            return;
        }

        EnsureManagedProcessJob();
        if (_managedProcessJob == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AssignProcessToJobObject(_managedProcessJob, process.Handle);
        }
        catch
        {
        }
#endif
    }

    private void TryAssignProcessHandleToManagedJob(IntPtr processHandle)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (processHandle == IntPtr.Zero)
        {
            return;
        }

        EnsureManagedProcessJob();
        if (_managedProcessJob == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AssignProcessToJobObject(_managedProcessJob, processHandle);
        }
        catch
        {
        }
#endif
    }

    private void DisposeManagedProcessJob()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (_managedProcessJob == IntPtr.Zero)
        {
            return;
        }

        try
        {
            CloseHandle(_managedProcessJob);
        }
        catch
        {
        }
        finally
        {
            _managedProcessJob = IntPtr.Zero;
        }
#endif
    }
}
