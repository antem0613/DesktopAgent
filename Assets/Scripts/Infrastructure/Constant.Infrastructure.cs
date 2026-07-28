public partial class Constant
{
    public const string BackendHost = "127.0.0.1";
    public const string UIProcessArgument = "--ui";
    /// <summary>キャラクタープロセス起動時の引数プレフィックス（例: Chara=0）。</summary>
    public const string CharacterProcessArgumentPrefix = "Chara=";
    public const string BackendExecutable = "python";
    public const string BackendArguments = "-m app.server";

    public const string BackendWorkingDirectoryRelativePath = "F:/DesktopAgent/BackendServer";

    public const string OllamaExecutable = "ollama.exe";
    public const string OllamaArguments = "serve";
    public const int OllamaHealthPort = 11434;

    public const string CoeiroinkExecutable = "engine.exe";
    public const int CoeiroinkHealthPort = 50032;

    public const string UIProcessExecutablePath = "UI/DesktopAgentUI.exe";
    public const string UIProcessName = "DesktopAgentUI";
    public const string CharacterStartupSceneName = "Character";
    public const string UIStartupSceneName = "UI";
    public const int CharacterStateWebSocketPort = 5700;
    public const string CharacterStateWebSocketPath = "/character-state";
    public const int CharacterLlmBridgeWebSocketPort = 5800;
    public const string CharacterLlmBridgeWebSocketPath = "/llm-bridge";
    public const int UIHealthCheckUdpPort = 38181;
    public const string UIHealthCheckPingMessage = "desktopagent-ui-ping";
    public const string UIHealthCheckPongMessage = "desktopagent-ui-pong";
    public const string UIForceTopmostMessage = "desktopagent-ui-force-topmost";
    public const string UIForceTopmostAckMessage = "desktopagent-ui-force-topmost-ack";
    public const string UIOpenMenuMessage = "desktopagent-ui-open-menu";
    public const string UIOpenMenuAckMessage = "desktopagent-ui-open-menu-ack";
    public const string UIOpenSettingsDialogMessage = "desktopagent-ui-open-settings-dialog";
    public const string UILoadingShowMessage = "desktopagent-ui-loading-show";
    public const string UILoadingHideMessage = "desktopagent-ui-loading-hide";
    public const string UILoadingAckMessage = "desktopagent-ui-loading-ack";

    public const int BackendPort = 8000;
    public const string BackendHealthCheckPath = "/health";
    public const int BackendHealthCheckHttpTimeoutMs = 200;

}
