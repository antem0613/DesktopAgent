public partial class Constant
{
    public const string BackendHost = "127.0.0.1";
    public const string UIProcessArgument = "--ui";
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
    public const string CharacterStartupSceneName = "Test";
    public const string UIStartupSceneName = "Test UI";
    public const int UIHealthCheckUdpPort = 38181;
    public const string UIHealthCheckPingMessage = "desktopagent-ui-ping";
    public const string UIHealthCheckPongMessage = "desktopagent-ui-pong";
    public const string UIForceTopmostMessage = "desktopagent-ui-force-topmost";
    public const string UIForceTopmostAckMessage = "desktopagent-ui-force-topmost-ack";
    public const string UIOpenMenuMessage = "desktopagent-ui-open-menu";
    public const string UIOpenMenuAckMessage = "desktopagent-ui-open-menu-ack";
    public const string UILoadingShowMessage = "desktopagent-ui-loading-show";
    public const string UILoadingHideMessage = "desktopagent-ui-loading-hide";
    public const string UILoadingAckMessage = "desktopagent-ui-loading-ack";

    public const int BackendPort = 8000;
    public const string BackendHealthCheckPath = "/health";
    public const int BackendHealthCheckHttpTimeoutMs = 200;

}
