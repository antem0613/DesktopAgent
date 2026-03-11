using System;
using System.IO;
using Unity.Logging;
using UnityEngine;

public class JsonSettingsFileTestComponent : MonoBehaviour
{
    private const string LogTag = "JsonSettingsFileTest";

    private enum StartAction
    {
        None = 0,
        Save = 1,
        Load = 2,
        SaveThenLoad = 3,
    }

    private enum BaseFolderType
    {
        PersistentDataPath = 0,
        StreamingAssetsPath = 1,
        DataPath = 2,
        AbsolutePath = 3,
    }

    [Serializable]
    public class BackendStartupSettings
    {
        public bool startBackendServer = true;
        public int launchMethod = 1;
        public string executable = "python";
        public string arguments = "-m app.server";
        public string pythonExecutable = "python";
        public string pythonModuleName = "app.server";
        public string pythonModuleArguments = "";
        public string pyLauncherScriptPath = "app/server.py";
        public string pyLauncherExtraArguments = "";
        [NonSerialized] public string workingDirectoryRelativePath = "BackendServer";
        [NonSerialized] public bool useProjectRootAsWorkingDirectoryWhenEmpty = true;
        [NonSerialized] public bool logProcessOutput = true;
        public bool startOllamaServer = true;
        public string ollamaExecutable = "ollama.exe";
        public string ollamaArguments = "serve";
        [NonSerialized] public string ollamaHealthHost = "127.0.0.1";
        [NonSerialized] public int ollamaHealthPort = 11434;
        public bool startCoeiroinkServer = true;
        public string coeiroinkExecutable = "D:/COEIROINK_WIN_CPU_v.2.12.3/COEIROINK_WIN_CPU_v.2.12.3/engine/engine.exe";
        public string coeiroinkArguments = "";
        [NonSerialized] public string coeiroinkHealthHost = "127.0.0.1";
        [NonSerialized] public int coeiroinkHealthPort = 50032;
        public bool startExternalUiProcess = true;
        public string externalUiExecutablePath = "UI/DesktopAgentUI.exe";
        public string externalUiArguments = "";
        [NonSerialized] public string externalUiProcessName = "DesktopAgentUI";
        [NonSerialized] public bool stopExternalUiProcessOnExit = true;
        public string backendHost = "127.0.0.1";
        public int backendPort = 8000;
        [NonSerialized] public string backendHealthCheckPath = "";
        [NonSerialized] public int backendHealthCheckHttpTimeoutMs = 200;
        [NonSerialized] public float startupTimeoutSeconds = 4f;
        [NonSerialized] public float pollIntervalSeconds = 0.25f;
        [NonSerialized] public int tcpTimeoutMs = 200;
    }

    [Serializable]
    public class LlmProviderSettings
    {
        public string endpointUrl = "http://127.0.0.1:8000/chat";
        public int timeoutSeconds = 60;
        public bool autoStartBackendBeforeRequest = true;
        public float backendStartupWaitSeconds = 2.0f;
        [NonSerialized] public int backendHealthPollIntervalMs = 100;
        public string apiKey = "";
        public string apiKeyHeaderName = "Authorization";
        public bool useBearer = true;
    }

    [Serializable]
    public class CoeiroinkSettings
    {
        public string baseUrl = "http://127.0.0.1:50032/v1";
        [NonSerialized] public int[] fallbackPorts = { 50032, 50031, 50021 };
        [NonSerialized] public int healthCheckTimeoutMs = 150;
        [NonSerialized] public float startupWaitSeconds = 2.0f;
        public string speakerUuid = "";
        public int styleId = 0;
    }

    [Serializable]
    public class UdpBridgeSettings
    {
        public string listenHost = "127.0.0.1";
        public int listenPort = 27651;
        [NonSerialized] public int maxQueuedMessages = 32;
    }

    [Serializable]
    private class AudioSettings
    {
        public float masterVolume = 1.0f;
        public bool mute = false;
    }

    [Serializable]
    private class WindowSettings
    {
        public int width = 1000;
        public int height = 800;
        public int posX = 100;
        public int posY = 100;
    }

    [Serializable]
    private class JsonSettingsData
    {
        public int version = 1;
        public string profileName = "default";
        public string language = "ja-JP";
        public bool useDarkTheme = false;
        public AudioSettings audio = new AudioSettings();
        public WindowSettings window = new WindowSettings();
        public BackendStartupSettings backend = new BackendStartupSettings();
        public LlmProviderSettings llm = new LlmProviderSettings();
        public CoeiroinkSettings coeiroink = new CoeiroinkSettings();
        public UdpBridgeSettings udpBridge = new UdpBridgeSettings();
        public string updatedAtIso8601 = "";
    }

    [Header("Run")]
    [SerializeField] private bool runOnStart;
    [SerializeField] private StartAction startAction = StartAction.None;

    [Header("Path")]
    [SerializeField] private BaseFolderType baseFolderType = BaseFolderType.StreamingAssetsPath;
    [SerializeField] private string relativeFolderPath = "TestSettings";
    [SerializeField] private string jsonFileName = "test_settings.json";
    [SerializeField] private string absoluteFolderPath = "";
    [SerializeField] private bool autoCreateJsonIfMissing = true;

    [Header("Test Values")]
    [SerializeField] private int version = 1;
    [SerializeField] private string profileName = "default";
    [SerializeField] private string language = "ja-JP";
    [SerializeField] private bool useDarkTheme;
    [SerializeField] private float masterVolume = 1.0f;
    [SerializeField] private bool mute;
    [SerializeField] private int windowWidth = 1000;
    [SerializeField] private int windowHeight = 800;
    [SerializeField] private int windowPosX = 100;
    [SerializeField] private int windowPosY = 100;

    [Header("Backend Test Settings")]
    [SerializeField] private bool backendStartBackendServer = true;
    [SerializeField] private int backendLaunchMethod = 1;
    [SerializeField] private string backendExecutable = "python";
    [SerializeField] private string backendArguments = "-m app.server";
    [SerializeField] private string backendPythonExecutable = "python";
    [SerializeField] private string backendPythonModuleName = "app.server";
    [SerializeField] private string backendPythonModuleArguments = "";
    [SerializeField] private string backendPyLauncherScriptPath = "app/server.py";
    [SerializeField] private string backendPyLauncherExtraArguments = "";
    [SerializeField] private string backendWorkingDirectoryRelativePath = "BackendServer";
    [SerializeField] private bool backendUseProjectRootAsWorkingDirectoryWhenEmpty = true;
    [SerializeField] private bool backendLogProcessOutput = true;
    [SerializeField] private bool backendStartOllamaServer = true;
    [SerializeField] private string backendOllamaExecutable = "ollama.exe";
    [SerializeField] private string backendOllamaArguments = "serve";
    [SerializeField] private string backendOllamaHealthHost = "127.0.0.1";
    [SerializeField] private int backendOllamaHealthPort = 11434;
    [SerializeField] private bool backendStartCoeiroinkServer = true;
    [SerializeField] private string backendCoeiroinkExecutable = "D:/COEIROINK_WIN_CPU_v.2.12.3/COEIROINK_WIN_CPU_v.2.12.3/engine/engine.exe";
    [SerializeField] private string backendCoeiroinkArguments = "";
    [SerializeField] private string backendCoeiroinkHealthHost = "127.0.0.1";
    [SerializeField] private int backendCoeiroinkHealthPort = 50032;
    [SerializeField] private bool backendStartExternalUiProcess = true;
    [SerializeField] private string backendExternalUiExecutablePath = "UI/DesktopAgentUI.exe";
    [SerializeField] private string backendExternalUiArguments = "";
    [SerializeField] private string backendExternalUiProcessName = "DesktopAgentUI";
    [SerializeField] private bool backendStopExternalUiProcessOnExit = true;
    [SerializeField] private string backendHealthHost = "127.0.0.1";
    [SerializeField] private int backendHealthPort = 8000;
    [SerializeField] private string backendHealthPath = "";
    [SerializeField] private int backendHealthHttpTimeoutMs = 200;
    [SerializeField] private float backendStartupTimeoutSeconds = 4f;
    [SerializeField] private float backendPollIntervalSeconds = 0.25f;
    [SerializeField] private int backendTcpTimeoutMs = 200;

    [Header("LLM Test Settings")]
    [SerializeField] private string llmEndpointUrl = "http://127.0.0.1:8000/chat";
    [SerializeField] private int llmTimeoutSeconds = 60;
    [SerializeField] private bool llmAutoStartBackendBeforeRequest = true;
    [SerializeField] private float llmBackendStartupWaitSeconds = 2.0f;
    [SerializeField] private int llmBackendHealthPollIntervalMs = 100;
    [SerializeField] private string llmApiKey = "";
    [SerializeField] private string llmApiKeyHeaderName = "Authorization";
    [SerializeField] private bool llmUseBearer = true;

    [Header("COEIROINK Test Settings")]
    [SerializeField] private string coeiroinkBaseUrl = "http://127.0.0.1:50032/v1";
    [SerializeField] private int[] coeiroinkFallbackPorts = { 50032, 50031, 50021 };
    [SerializeField] private int coeiroinkHealthCheckTimeoutMs = 150;
    [SerializeField] private float coeiroinkStartupWaitSeconds = 2.0f;
    [SerializeField] private string coeiroinkSpeakerUuid = "";
    [SerializeField] private int coeiroinkStyleId = 0;

    [Header("UDP Bridge Test Settings")]
    [SerializeField] private string udpBridgeListenHost = "127.0.0.1";
    [SerializeField] private int udpBridgeListenPort = 27651;
    [SerializeField] private int udpBridgeMaxQueuedMessages = 32;

    private void Start()
    {
        if (!runOnStart)
        {
            return;
        }

        switch (startAction)
        {
            case StartAction.Save:
                SaveToJson();
                break;
            case StartAction.Load:
                LoadFromJson();
                break;
            case StartAction.SaveThenLoad:
                SaveToJson();
                LoadFromJson();
                break;
        }
    }

    [ContextMenu("Save JSON Settings")]
    public void SaveToJson()
    {
        try
        {
            string filePath = GetJsonFilePath();
            EnsureParentDirectory(filePath);

            JsonSettingsData data = BuildDataFromInspector();
            data.updatedAtIso8601 = DateTime.UtcNow.ToString("o");

            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(filePath, json);

            Log.Info($"[{LogTag}] Saved JSON settings: {filePath}");
            Log.Info($"[{LogTag}] Saved payload: {json}");
        }
        catch (Exception ex)
        {
            Log.Error($"[{LogTag}] Save failed: {ex.Message}");
        }
    }

    [ContextMenu("Load JSON Settings")]
    public void LoadFromJson()
    {
        try
        {
            string filePath = GetJsonFilePath();
            EnsureJsonFileExistsIfNeeded(filePath);
            if (!File.Exists(filePath))
            {
                Log.Warning($"[{LogTag}] JSON file not found: {filePath}");
                return;
            }

            string json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                Log.Warning($"[{LogTag}] JSON file is empty: {filePath}");
                return;
            }

            JsonSettingsData data = JsonUtility.FromJson<JsonSettingsData>(json);
            if (data == null)
            {
                Log.Error($"[{LogTag}] Load failed: JsonUtility returned null. path={filePath}");
                return;
            }

            ApplyDataToInspector(data);
            Log.Info($"[{LogTag}] Loaded JSON settings: {filePath}");
            Log.Info($"[{LogTag}] Loaded payload: {json}");
        }
        catch (Exception ex)
        {
            Log.Error($"[{LogTag}] Load failed: {ex.Message}");
        }
    }

    [ContextMenu("Delete JSON Settings")]
    public void DeleteJsonFile()
    {
        try
        {
            string filePath = GetJsonFilePath();
            if (!File.Exists(filePath))
            {
                Log.Warning($"[{LogTag}] Delete skipped: file not found. {filePath}");
                return;
            }

            File.Delete(filePath);
            Log.Info($"[{LogTag}] Deleted JSON settings: {filePath}");
        }
        catch (Exception ex)
        {
            Log.Error($"[{LogTag}] Delete failed: {ex.Message}");
        }
    }

    [ContextMenu("Roundtrip Test (Save->Load)")]
    public void RoundtripTest()
    {
        SaveToJson();
        LoadFromJson();
        Log.Info($"[{LogTag}] Roundtrip test finished.");
    }

    [ContextMenu("Log JSON File Path")]
    public void LogJsonFilePath()
    {
        try
        {
            Log.Info($"[{LogTag}] JSON file path: {GetJsonFilePath()}");
        }
        catch (Exception ex)
        {
            Log.Error($"[{LogTag}] Path resolve failed: {ex.Message}");
        }
    }

    public bool TryGetBackendStartupSettings(out BackendStartupSettings settings)
    {
        settings = BuildBackendSettingsFromInspector();
        if (TryReadJson(out var data) && data.backend != null)
        {
            ApplyBackendUserSettings(settings, data.backend);
        }

        return settings != null;
    }

    public bool TryGetLlmProviderSettings(out LlmProviderSettings settings)
    {
        settings = BuildLlmSettingsFromInspector();
        if (TryReadJson(out var data) && data.llm != null)
        {
            ApplyLlmUserSettings(settings, data.llm);
        }

        return settings != null;
    }

    public bool TryGetCoeiroinkSettings(out CoeiroinkSettings settings)
    {
        settings = BuildCoeiroinkSettingsFromInspector();
        if (TryReadJson(out var data) && data.coeiroink != null)
        {
            ApplyCoeiroinkUserSettings(settings, data.coeiroink);
        }

        if ((string.IsNullOrWhiteSpace(settings.speakerUuid) || settings.styleId <= 0) &&
            TryReadCoeiroinkSettingsFromDefaultStreamingAssets(out var fallback))
        {
            if (!string.IsNullOrWhiteSpace(fallback.speakerUuid))
            {
                settings.speakerUuid = fallback.speakerUuid;
            }

            if (fallback.styleId > 0)
            {
                settings.styleId = fallback.styleId;
            }

            if (!string.IsNullOrWhiteSpace(fallback.baseUrl))
            {
                settings.baseUrl = fallback.baseUrl;
            }

            Log.Warning($"[{LogTag}] COEIROINK settings were supplemented from default StreamingAssets test_settings.json.");
        }

        return settings != null;
    }

    private bool TryReadCoeiroinkSettingsFromDefaultStreamingAssets(out CoeiroinkSettings settings)
    {
        settings = null;
        try
        {
            string filePath = Path.Combine(Application.streamingAssetsPath, "TestSettings", "test_settings.json");
            if (!File.Exists(filePath))
            {
                return false;
            }

            string json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            JsonSettingsData data = JsonUtility.FromJson<JsonSettingsData>(json);
            if (data?.coeiroink == null)
            {
                return false;
            }

            settings = data.coeiroink;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryGetUdpBridgeSettings(out UdpBridgeSettings settings)
    {
        settings = BuildUdpBridgeSettingsFromInspector();
        if (TryReadJson(out var data) && data.udpBridge != null)
        {
            ApplyUdpBridgeUserSettings(settings, data.udpBridge);
        }

        return settings != null;
    }

    private JsonSettingsData BuildDataFromInspector()
    {
        return new JsonSettingsData
        {
            version = version,
            profileName = profileName ?? string.Empty,
            language = language ?? string.Empty,
            useDarkTheme = useDarkTheme,
            audio = new AudioSettings
            {
                masterVolume = masterVolume,
                mute = mute,
            },
            window = new WindowSettings
            {
                width = windowWidth,
                height = windowHeight,
                posX = windowPosX,
                posY = windowPosY,
            },
            backend = BuildBackendSettingsFromInspector(),
            llm = BuildLlmSettingsFromInspector(),
            coeiroink = BuildCoeiroinkSettingsFromInspector(),
            udpBridge = BuildUdpBridgeSettingsFromInspector(),
        };
    }

    private void ApplyDataToInspector(JsonSettingsData data)
    {
        version = data.version;
        profileName = data.profileName ?? string.Empty;
        language = data.language ?? string.Empty;
        useDarkTheme = data.useDarkTheme;

        if (data.audio != null)
        {
            masterVolume = data.audio.masterVolume;
            mute = data.audio.mute;
        }

        if (data.window != null)
        {
            windowWidth = data.window.width;
            windowHeight = data.window.height;
            windowPosX = data.window.posX;
            windowPosY = data.window.posY;
        }

        if (data.backend != null)
        {
            backendStartBackendServer = data.backend.startBackendServer;
            backendLaunchMethod = data.backend.launchMethod;
            backendExecutable = data.backend.executable;
            backendArguments = data.backend.arguments;
            backendPythonExecutable = data.backend.pythonExecutable;
            backendPythonModuleName = data.backend.pythonModuleName;
            backendPythonModuleArguments = data.backend.pythonModuleArguments;
            backendPyLauncherScriptPath = data.backend.pyLauncherScriptPath;
            backendPyLauncherExtraArguments = data.backend.pyLauncherExtraArguments;
            backendStartOllamaServer = data.backend.startOllamaServer;
            backendOllamaExecutable = data.backend.ollamaExecutable;
            backendOllamaArguments = data.backend.ollamaArguments;
            backendStartCoeiroinkServer = data.backend.startCoeiroinkServer;
            backendCoeiroinkExecutable = data.backend.coeiroinkExecutable;
            backendCoeiroinkArguments = data.backend.coeiroinkArguments;
            backendStartExternalUiProcess = data.backend.startExternalUiProcess;
            backendExternalUiExecutablePath = data.backend.externalUiExecutablePath;
            backendExternalUiArguments = data.backend.externalUiArguments;
            backendHealthHost = data.backend.backendHost;
            backendHealthPort = data.backend.backendPort;
        }

        if (data.llm != null)
        {
            llmEndpointUrl = data.llm.endpointUrl;
            llmTimeoutSeconds = data.llm.timeoutSeconds;
            llmAutoStartBackendBeforeRequest = data.llm.autoStartBackendBeforeRequest;
            llmBackendStartupWaitSeconds = data.llm.backendStartupWaitSeconds;
            llmApiKey = data.llm.apiKey;
            llmApiKeyHeaderName = data.llm.apiKeyHeaderName;
            llmUseBearer = data.llm.useBearer;
        }

        if (data.coeiroink != null)
        {
            coeiroinkBaseUrl = data.coeiroink.baseUrl;
            coeiroinkSpeakerUuid = data.coeiroink.speakerUuid;
            coeiroinkStyleId = data.coeiroink.styleId;
        }

        if (data.udpBridge != null)
        {
            udpBridgeListenHost = data.udpBridge.listenHost;
            udpBridgeListenPort = data.udpBridge.listenPort;
        }
    }

    private static void ApplyBackendUserSettings(BackendStartupSettings target, BackendStartupSettings source)
    {
        target.startBackendServer = source.startBackendServer;
        target.launchMethod = source.launchMethod;
        target.executable = source.executable ?? target.executable;
        target.arguments = source.arguments ?? target.arguments;
        target.pythonExecutable = source.pythonExecutable ?? target.pythonExecutable;
        target.pythonModuleName = source.pythonModuleName ?? target.pythonModuleName;
        target.pythonModuleArguments = source.pythonModuleArguments ?? target.pythonModuleArguments;
        target.pyLauncherScriptPath = source.pyLauncherScriptPath ?? target.pyLauncherScriptPath;
        target.pyLauncherExtraArguments = source.pyLauncherExtraArguments ?? target.pyLauncherExtraArguments;
        target.startOllamaServer = source.startOllamaServer;
        target.ollamaExecutable = source.ollamaExecutable ?? target.ollamaExecutable;
        target.ollamaArguments = source.ollamaArguments ?? target.ollamaArguments;
        target.startCoeiroinkServer = source.startCoeiroinkServer;
        target.coeiroinkExecutable = source.coeiroinkExecutable ?? target.coeiroinkExecutable;
        target.coeiroinkArguments = source.coeiroinkArguments ?? target.coeiroinkArguments;
        target.startExternalUiProcess = source.startExternalUiProcess;
        target.externalUiExecutablePath = source.externalUiExecutablePath ?? target.externalUiExecutablePath;
        target.externalUiArguments = source.externalUiArguments ?? target.externalUiArguments;
        target.backendHost = source.backendHost ?? target.backendHost;
        target.backendPort = source.backendPort;
    }

    private static void ApplyLlmUserSettings(LlmProviderSettings target, LlmProviderSettings source)
    {
        target.endpointUrl = source.endpointUrl ?? target.endpointUrl;
        target.timeoutSeconds = source.timeoutSeconds;
        target.autoStartBackendBeforeRequest = source.autoStartBackendBeforeRequest;
        target.backendStartupWaitSeconds = source.backendStartupWaitSeconds;
        target.apiKey = source.apiKey ?? target.apiKey;
        target.apiKeyHeaderName = source.apiKeyHeaderName ?? target.apiKeyHeaderName;
        target.useBearer = source.useBearer;
    }

    private static void ApplyCoeiroinkUserSettings(CoeiroinkSettings target, CoeiroinkSettings source)
    {
        target.baseUrl = source.baseUrl ?? target.baseUrl;
        target.speakerUuid = source.speakerUuid ?? target.speakerUuid;
        target.styleId = source.styleId;
    }

    private static void ApplyUdpBridgeUserSettings(UdpBridgeSettings target, UdpBridgeSettings source)
    {
        target.listenHost = source.listenHost ?? target.listenHost;
        target.listenPort = source.listenPort;
    }

    private BackendStartupSettings BuildBackendSettingsFromInspector()
    {
        return new BackendStartupSettings
        {
            startBackendServer = backendStartBackendServer,
            launchMethod = backendLaunchMethod,
            executable = backendExecutable,
            arguments = backendArguments,
            pythonExecutable = backendPythonExecutable,
            pythonModuleName = backendPythonModuleName,
            pythonModuleArguments = backendPythonModuleArguments,
            pyLauncherScriptPath = backendPyLauncherScriptPath,
            pyLauncherExtraArguments = backendPyLauncherExtraArguments,
            workingDirectoryRelativePath = backendWorkingDirectoryRelativePath,
            useProjectRootAsWorkingDirectoryWhenEmpty = backendUseProjectRootAsWorkingDirectoryWhenEmpty,
            logProcessOutput = backendLogProcessOutput,
            startOllamaServer = backendStartOllamaServer,
            ollamaExecutable = backendOllamaExecutable,
            ollamaArguments = backendOllamaArguments,
            ollamaHealthHost = backendOllamaHealthHost,
            ollamaHealthPort = backendOllamaHealthPort,
            startCoeiroinkServer = backendStartCoeiroinkServer,
            coeiroinkExecutable = backendCoeiroinkExecutable,
            coeiroinkArguments = backendCoeiroinkArguments,
            coeiroinkHealthHost = backendCoeiroinkHealthHost,
            coeiroinkHealthPort = backendCoeiroinkHealthPort,
            startExternalUiProcess = backendStartExternalUiProcess,
            externalUiExecutablePath = backendExternalUiExecutablePath,
            externalUiArguments = backendExternalUiArguments,
            externalUiProcessName = backendExternalUiProcessName,
            stopExternalUiProcessOnExit = backendStopExternalUiProcessOnExit,
            backendHost = backendHealthHost,
            backendPort = backendHealthPort,
            backendHealthCheckPath = backendHealthPath,
            backendHealthCheckHttpTimeoutMs = backendHealthHttpTimeoutMs,
            startupTimeoutSeconds = backendStartupTimeoutSeconds,
            pollIntervalSeconds = backendPollIntervalSeconds,
            tcpTimeoutMs = backendTcpTimeoutMs,
        };
    }

    private LlmProviderSettings BuildLlmSettingsFromInspector()
    {
        return new LlmProviderSettings
        {
            endpointUrl = llmEndpointUrl,
            timeoutSeconds = llmTimeoutSeconds,
            autoStartBackendBeforeRequest = llmAutoStartBackendBeforeRequest,
            backendStartupWaitSeconds = llmBackendStartupWaitSeconds,
            backendHealthPollIntervalMs = llmBackendHealthPollIntervalMs,
            apiKey = llmApiKey,
            apiKeyHeaderName = llmApiKeyHeaderName,
            useBearer = llmUseBearer,
        };
    }

    private CoeiroinkSettings BuildCoeiroinkSettingsFromInspector()
    {
        return new CoeiroinkSettings
        {
            baseUrl = coeiroinkBaseUrl,
            fallbackPorts = coeiroinkFallbackPorts,
            healthCheckTimeoutMs = coeiroinkHealthCheckTimeoutMs,
            startupWaitSeconds = coeiroinkStartupWaitSeconds,
            speakerUuid = coeiroinkSpeakerUuid,
            styleId = coeiroinkStyleId,
        };
    }

    private UdpBridgeSettings BuildUdpBridgeSettingsFromInspector()
    {
        return new UdpBridgeSettings
        {
            listenHost = udpBridgeListenHost,
            listenPort = udpBridgeListenPort,
            maxQueuedMessages = udpBridgeMaxQueuedMessages,
        };
    }

    private bool TryReadJson(out JsonSettingsData data)
    {
        data = null;
        try
        {
            string filePath = GetJsonFilePath();
            EnsureJsonFileExistsIfNeeded(filePath);
            if (!File.Exists(filePath))
            {
                return false;
            }

            string json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            data = JsonUtility.FromJson<JsonSettingsData>(json);
            return data != null;
        }
        catch
        {
            return false;
        }
    }

    private void EnsureJsonFileExistsIfNeeded(string filePath)
    {
        if (!autoCreateJsonIfMissing)
        {
            return;
        }

        if (File.Exists(filePath))
        {
            return;
        }

        EnsureParentDirectory(filePath);
        var defaultData = BuildDataFromInspector();
        defaultData.updatedAtIso8601 = DateTime.UtcNow.ToString("o");
        string json = JsonUtility.ToJson(defaultData, true);
        File.WriteAllText(filePath, json);
        Log.Info($"[{LogTag}] JSON settings file auto-created: {filePath}");
    }

    private string GetJsonFilePath()
    {
        string basePath = GetBaseFolderPath();
        string safeFileName = string.IsNullOrWhiteSpace(jsonFileName) ? "test_settings.json" : jsonFileName.Trim();
        if (!safeFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            safeFileName += ".json";
        }

        string folder = string.IsNullOrWhiteSpace(relativeFolderPath)
            ? basePath
            : Path.Combine(basePath, relativeFolderPath.Trim());

        return Path.GetFullPath(Path.Combine(folder, safeFileName));
    }

    private string GetBaseFolderPath()
    {
        switch (baseFolderType)
        {
            case BaseFolderType.PersistentDataPath:
                return Application.persistentDataPath;
            case BaseFolderType.StreamingAssetsPath:
                return Application.streamingAssetsPath;
            case BaseFolderType.DataPath:
                return Application.dataPath;
            case BaseFolderType.AbsolutePath:
                if (string.IsNullOrWhiteSpace(absoluteFolderPath))
                {
                    throw new InvalidOperationException("absoluteFolderPath is empty while BaseFolderType.AbsolutePath is selected.");
                }

                return Path.GetFullPath(absoluteFolderPath.Trim());
            default:
                return Application.persistentDataPath;
        }
    }

    private static void EnsureParentDirectory(string filePath)
    {
        string directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
