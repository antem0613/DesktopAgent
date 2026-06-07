using UnityEngine;
using System.Threading;
using UnityEngine.SceneManagement;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System;

public enum ProcessType
{
    Main,
    Character,
    Wallpaper,
    Other
}

public class ProcessIdentifier : SingletonMonoBehaviour<ProcessIdentifier>
{
    static Mutex _mutex ;

    ProcessType _processType;
    public ProcessType ProcessType { get => _processType; private set => _processType = value; }

    int _optionVal;

    public int OptionVal { get => _optionVal; private set => _optionVal = value; }

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
        
        string[] args = Environment.GetCommandLineArgs();

        if(args == null ||args.Length < 1)
        {
            if (!TryAcquireMutex("DesktopAgent_Main"))
            {
                UnityEngine.Debug.LogError("Failed to acquire mutex for main process. Another instance might be running.");
                Application.Quit();
            }

            _processType = ProcessType.Main;
            SceneManager.LoadScene(MainSceneName);
            return;
        }

        string typeStr = args[0].Split('=')[0];
        int optionVal = int.Parse(args[0].Split('=')[1]);

        switch (typeStr)
        {
            case "Main":
                if (!TryAcquireMutex("DesktopAgent_Main"))
                {
                    UnityEngine.Debug.LogError("Failed to acquire mutex for main process. Another instance might be running.");
                    Application.Quit();
                }
                _processType = ProcessType.Main;
                SceneManager.LoadScene(MainSceneName);

                break;
            case "Chara":
                if (TryAcquireMutex("DesktopAgent_Main"))
                {
                    _processType = ProcessType.Main;

                    using var current = Process.GetCurrentProcess();
                    string currentExecutable = current.MainModule?.FileName ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(currentExecutable))
                    {
                        NormalizePath(currentExecutable);
                    }

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = currentExecutable,
                        WorkingDirectory = currentExecutable,
                        Arguments = $"Chara={optionVal}",
                        UseShellExecute = false,
                        CreateNoWindow = false
                    };

                    TryCreateProcess(startInfo);

                    SceneManager.LoadScene(MainSceneName);
                    return;
                }
                
                if(TryAcquireMutex("DesktopAgent_Chara_" + optionVal))
                {
                    _processType = ProcessType.Character;
                    OptionVal = optionVal;
                    SceneManager.LoadScene(CharacterSceneName);
                    return;
                } else
                {
                    Application.Quit();
                }
                
                break;
            case "Wall":
                //later
                break;
            default:
                Application.Quit();
                break;
        }
    }

    bool TryAcquireMutex(string mutexName)
    {
        Mutex mutex = new Mutex(true, mutexName, out bool createdNew);

        if (createdNew)
        {
            _mutex = mutex;
            return true;
        }
        else
        {
            mutex.Close();
            return false;
        }
    }

    void OnApplicationQuit()
    {
        if(_mutex != null)
        {
            _mutex.ReleaseMutex();
            _mutex.Close();
            _mutex = null;
        }
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

            return true;
        } catch (Exception ex)
        {
            return false;
        }
#else
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
