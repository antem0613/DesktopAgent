using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System;
using System.Text;

public enum ProcessType
{
    Main,
    Character,
    Wallpaper,
    Other
}

public class ProcessIdentifier : SingletonMonoBehaviour<ProcessIdentifier>
{
    static FileStream _lockStream;
    static string _lockFilePath;
    const string CharacterArgumentPrefix = "Chara=";
    const string UnityMonitorArgument = "-monitor";
    const int PrimaryMonitorIndex = 1;
    const string UnityLogFileArgument = "-logFile";
    const string ProcessLogDirectoryName = "ProcessLogs";
    const string WindowPlacementLogPrefix = "[ProcessIdentifier][WindowPlacement]";
    const string LogPrefix = "[ProcessIdentifier]";

    ProcessType _processType;
    public ProcessType ProcessType { get => _processType; private set => _processType = value; }

    public int _optionVal;

    public int OptionVal { get => _optionVal; private set => _optionVal = value; }

    private int _defaultCharacterOptionVal = -1;

    /// <summary>
    /// メインプロセス起動時に CompanionController が生成すべきデフォルトキャラクターの optionVal。
    /// -1 はデフォルトキャラクター生成不要を示す。
    /// </summary>
    public int DefaultCharacterOptionVal => _defaultCharacterOptionVal;

    /// <summary>キャラクタープロセスがロードするバンドルファイルの絶対パス。メインプロセスから引数に渡される。</summary>
    public string BundlePath { get; private set; }

    /// <summary>キャラクタープロセスがロードする AssetBundle 内のプレハブ名。メインプロセスから引数に渡される。</summary>
    public string PrefabName { get; private set; }

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

    [SerializeField] string MainSceneName = "Main";
    [SerializeField] string CharacterSceneName = "Character";
    //[SerializeField] string WallpaperSceneName = "Wallpaper";

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

    private protected override void Awake()
    {
        base.Awake();

        DontDestroyOnLoad(this);

        string[] args = Environment.GetCommandLineArgs();
        UnityEngine.Debug.Log($"{LogPrefix} Awake. args={string.Join(", ", args ?? Array.Empty<string>())}");

        string startupArg = GetStartupArgument(args);

        if (string.IsNullOrWhiteSpace(startupArg))
        {
            UnityEngine.Debug.Log($"{LogPrefix} No startup argument found. Attempting main process startup.");
            if (!TryAcquireLockFile("DesktopAgent_Main"))
            {
                UnityEngine.Debug.LogError("Failed to acquire lock file for main process. Another instance might be running.");
                Application.Quit();
            }

            _processType = ProcessType.Main;
            _defaultCharacterOptionVal = 0;
            UnityEngine.Debug.Log($"{LogPrefix} Main lock file acquired in no-arg startup. Default character optionVal set to 0. CompanionController will start the process.");
            ScheduleMainWindowPlacement();
            UnityEngine.Debug.Log($"{LogPrefix} ProcessType=Main. Loading scene={MainSceneName}");
            SceneManager.LoadScene(MainSceneName);
            return;
        }

        UnityEngine.Debug.Log($"{LogPrefix} Parsing startup argument: {startupArg}");

        string typeStr;
        int optionVal = 0;
        int separatorIndex = startupArg.IndexOf('=');
        if (separatorIndex >= 0)
        {
            typeStr = startupArg.Substring(0, separatorIndex).Trim();
            string optionRaw = startupArg.Substring(separatorIndex + 1).Trim();

            if (!string.IsNullOrWhiteSpace(optionRaw) && !int.TryParse(optionRaw, out optionVal))
            {
                UnityEngine.Debug.LogWarning($"{LogPrefix} Invalid option value: {optionRaw}. Defaulting to 0.");
                optionVal = 0;
            }
        } else
        {
            typeStr = startupArg.Trim();
        }

        switch (typeStr)
        {
            case "Main":
                UnityEngine.Debug.Log($"{LogPrefix} Startup type=Main");
                if (!TryAcquireLockFile("DesktopAgent_Main"))
                {
                    UnityEngine.Debug.LogWarning($"{LogPrefix} Main lock file already acquired. Quitting duplicate main process.");
                    Application.Quit();
                }
                _processType = ProcessType.Main;
                ScheduleMainWindowPlacement();
                UnityEngine.Debug.Log($"{LogPrefix} ProcessType=Main. Loading scene={MainSceneName}");
                SceneManager.LoadScene(MainSceneName);

                break;
            case "Chara":
                UnityEngine.Debug.Log($"{LogPrefix} Startup type=Chara optionVal={optionVal}");
                if (TryAcquireLockFile("DesktopAgent_Main"))
                {
                    _processType = ProcessType.Main;
                    _defaultCharacterOptionVal = optionVal;
                    UnityEngine.Debug.Log($"{LogPrefix} Main lock file acquired in Chara mode. Default character optionVal set to {optionVal}. CompanionController will start the process.");
                    ScheduleMainWindowPlacement();

                    UnityEngine.Debug.Log($"{LogPrefix} Loading main scene={MainSceneName}");
                    SceneManager.LoadScene(MainSceneName);
                    return;
                }

                if (TryAcquireLockFile("DesktopAgent_Chara_" + optionVal))
                {
                    _processType = ProcessType.Character;
                    OptionVal = optionVal;
                    BundlePath = GetArgValue(args, Constant.CharacterBundlePathArgPrefix);
                    PrefabName = GetArgValue(args, Constant.CharacterPrefabNameArgPrefix);
                    UnityEngine.Debug.Log($"{LogPrefix} ProcessType=Character optionVal={OptionVal}, bundlePath={BundlePath}, prefabName={PrefabName}. Loading scene={CharacterSceneName}");
                    SceneManager.LoadScene(CharacterSceneName);
                    return;
                } else
                {
                    UnityEngine.Debug.LogWarning($"{LogPrefix} Character lock file already acquired for optionVal={optionVal}. Quitting duplicate character process.");
                    Application.Quit();
                }
                
                break;
            case "Wall":
                UnityEngine.Debug.Log($"{LogPrefix} Startup type=Wall");
                //later
                break;
            default:
                UnityEngine.Debug.LogWarning($"{LogPrefix} Unknown startup type={typeStr}. Quitting process.");
                Application.Quit();
                break;
        }
    }

    bool TryAcquireLockFile(string lockName)
    {
        UnityEngine.Debug.Log($"{LogPrefix} TryAcquireLockFile: {lockName}");
        try
        {
            string lockDirectory = Path.Combine(Application.persistentDataPath, "ProcessLocks");
            Directory.CreateDirectory(lockDirectory);

            string lockFilePath = Path.Combine(lockDirectory, SanitizeLockName(lockName) + ".lock");
            var stream = new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            _lockStream = stream;
            _lockFilePath = lockFilePath;

            UnityEngine.Debug.Log($"{LogPrefix} File lock acquired: {lockFilePath}");
            return true;
        } catch (IOException ioEx)
        {
            UnityEngine.Debug.LogWarning($"{LogPrefix} File lock already held: {lockName}. {ioEx.Message}");
            return false;
        } catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"{LogPrefix} Failed to acquire file lock for {lockName}. {ex}");
            return false;
        }
    }

    void OnApplicationQuit()
    {
        if (_lockStream != null)
        {
            UnityEngine.Debug.Log($"{LogPrefix} Releasing file lock and quitting. path={_lockFilePath}");
            _lockStream.Close();
            _lockStream = null;

            if (!string.IsNullOrWhiteSpace(_lockFilePath))
            {
                try
                {
                    File.Delete(_lockFilePath);
                } catch
                {
                }

                _lockFilePath = null;
            }
        }
    }

    string SanitizeLockName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "default";
        }

        char[] invalidChars = Path.GetInvalidFileNameChars();
        char[] chars = name.ToCharArray();

        for (int i = 0; i < chars.Length; i++)
        {
            for (int j = 0; j < invalidChars.Length; j++)
            {
                if (chars[i] == invalidChars[j])
                {
                    chars[i] = '_';
                    break;
                }
            }
        }

        return new string(chars);
    }

    public bool TryStartCharacterProcess(int optionVal, string bundlePath, string prefabName)
    {
        UnityEngine.Debug.Log($"{LogPrefix} TryStartCharacterProcess called optionVal={optionVal}, currentType={ProcessType}");
        if (ProcessType != ProcessType.Main)
        {
            UnityEngine.Debug.LogWarning($"{LogPrefix} Character process launch rejected. Current process type is not Main.");
            return false;
        }

        if (!TryResolveCurrentExecutable(out string currentExecutable, out string workingDirectory))
        {
            UnityEngine.Debug.LogError($"{LogPrefix} Failed to resolve current executable path or working directory.");
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = currentExecutable,
            WorkingDirectory = workingDirectory,
            Arguments = BuildLaunchArguments(
                GetProcessLogFilePath(ProcessType.Character, optionVal),
                $"{CharacterArgumentPrefix}{Mathf.Max(0, optionVal)}",
                QuoteArgument($"{Constant.CharacterBundlePathArgPrefix}{bundlePath ?? string.Empty}"),
                $"{Constant.CharacterPrefabNameArgPrefix}{prefabName ?? string.Empty}",
                BuildMonitorLaunchArgument(PrimaryMonitorIndex)),
            UseShellExecute = false,
            CreateNoWindow = false
        };

        UnityEngine.Debug.Log($"{LogPrefix} Launching character process. file={startInfo.FileName}, cwd={startInfo.WorkingDirectory}, args={startInfo.Arguments}");
        return TryCreateProcess(startInfo);
    }

    void ScheduleMainWindowPlacement()
    {
        ScheduleWindowPlacement();
    }

    void ScheduleWindowPlacement()
    {
        StartCoroutine(PlaceMainWindowOnPrimaryDisplayRoutine());
    }

    IEnumerator PlaceMainWindowOnPrimaryDisplayRoutine()
    {
        const int maxAttempts = 50;
        const float retryDelaySeconds = 0.1f;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            yield return null;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (WindowsAPI.TryMoveCurrentWindowToPrimaryDisplay())
            {
                UnityEngine.Debug.Log($"{WindowPlacementLogPrefix} Moved window to primary display on attempt={attempt}. processType={_processType}");
                yield break;
            }
#endif

            yield return new WaitForSeconds(retryDelaySeconds);
        }

        UnityEngine.Debug.LogWarning($"{WindowPlacementLogPrefix} Failed to move window to primary display after {maxAttempts} attempts. processType={_processType}");
    }

    string GetStartupArgument(string[] args)
    {
        if (args == null || args.Length <= 1)
        {
            return null;
        }

        for (int i = 1; i < args.Length; i++)
        {
            string current = args[i]?.Trim();

            if (string.IsNullOrWhiteSpace(current))
            {
                continue;
            }

            if (string.Equals(current, UnityLogFileArgument, StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            if (current.StartsWith("-"))
            {
                continue;
            }

            return current;
        }

        return null;
    }

    /// <summary>
    /// 全引数から prefix で始まる引数の値部分を返す。見つからなければ null。
    /// </summary>
    private string GetArgValue(string[] args, string prefix)
    {
        if (args == null || string.IsNullOrWhiteSpace(prefix)) return null;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i]?.Trim();
            if (string.IsNullOrEmpty(arg)) continue;
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return arg.Substring(prefix.Length);
        }
        return null;
    }

    string BuildLaunchArguments(string logFilePath, string startupArgument, params string[] extraArguments)
    {
        string quotedLogFilePath = QuoteArgument(logFilePath);
        string quotedStartupArgument = string.IsNullOrWhiteSpace(startupArgument) ? string.Empty : startupArgument.Trim();
        string joinedExtraArguments = JoinNonEmptyArguments(extraArguments);

        if (string.IsNullOrWhiteSpace(quotedLogFilePath))
        {
            return JoinNonEmptyArguments(quotedStartupArgument, joinedExtraArguments);
        }

        if (string.IsNullOrWhiteSpace(quotedStartupArgument))
        {
            return JoinNonEmptyArguments($"{UnityLogFileArgument} {quotedLogFilePath}", joinedExtraArguments);
        }

        return JoinNonEmptyArguments($"{UnityLogFileArgument} {quotedLogFilePath}", quotedStartupArgument, joinedExtraArguments);
    }

    string BuildMonitorLaunchArgument(int monitorIndex)
    {
        int resolvedIndex = Mathf.Max(1, monitorIndex);
        return $"{UnityMonitorArgument} {resolvedIndex}";
    }

    string JoinNonEmptyArguments(params string[] values)
    {
        if (values == null || values.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        for (int i = 0; i < values.Length; i++)
        {
            string value = values[i]?.Trim();

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(value);
        }

        return builder.ToString();
    }

    string GetProcessLogFilePath(ProcessType processType, int optionVal = 0)
    {
        string logDirectory = Path.Combine(Application.persistentDataPath, ProcessLogDirectoryName);
        Directory.CreateDirectory(logDirectory);

        string fileName = processType switch
        {
            ProcessType.Main => "Main.log",
            ProcessType.Character => $"Character_{Mathf.Max(0, optionVal)}.log",
            ProcessType.Wallpaper => "Wallpaper.log",
            _ => "Other.log"
        };

        return Path.Combine(logDirectory, fileName);
    }

    string QuoteArgument(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return $"\"{value}\"";
    }

    bool TryResolveCurrentExecutable(out string executablePath, out string workingDirectory)
    {
        executablePath = string.Empty;
        workingDirectory = string.Empty;

        string[] commandLineArgs = Environment.GetCommandLineArgs();
        string commandPath = (commandLineArgs != null && commandLineArgs.Length > 0) ? commandLineArgs[0] : string.Empty;

        if (!string.IsNullOrWhiteSpace(commandPath))
        {
            string normalizedCommandPath = NormalizePath(commandPath);

            if (File.Exists(normalizedCommandPath))
            {
                executablePath = normalizedCommandPath;
            } else
            {
                string commandFileName = Path.GetFileName(commandPath);
                string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(commandFileName) && !string.IsNullOrWhiteSpace(projectRoot))
                {
                    string candidate = NormalizePath(Path.Combine(projectRoot, commandFileName));

                    if (File.Exists(candidate))
                    {
                        executablePath = candidate;
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            try
            {
                using var current = Process.GetCurrentProcess();
                executablePath = NormalizePath(current.MainModule?.FileName ?? string.Empty);
            } catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"{LogPrefix} Process.MainModule could not be read. fallback will be used. reason={ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return false;
        }

        workingDirectory = Path.GetDirectoryName(executablePath);

        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            return false;
        }

        return true;
    }

    string NormalizePath(string path)
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

    private bool TryCreateProcess(ProcessStartInfo startInfo)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        try
        {
            var startupInfo = new STARTUPINFO
            {
                cb = (uint)Marshal.SizeOf<STARTUPINFO>(),
                dwFlags = Constant.STARTF_USESHOWWINDOW,
                wShowWindow = Constant.SW_HIDE
            };

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

            UnityEngine.Debug.Log($"{LogPrefix} CreateProcess result={created}, commandLine={commandLine}, pid={(created ? pi.dwProcessId : 0)}");
            return created;
        } catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"{LogPrefix} Exception while creating process: {ex}");
            return false;
        }
#else
        UnityEngine.Debug.LogWarning($"{LogPrefix} TryCreateProcess is not supported on this platform.");
        return false;
#endif
    }

    string BuildCommandLine(string fileName, string arguments)
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
}
