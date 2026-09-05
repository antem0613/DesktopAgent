public enum HealthCheckKind
{
    None,
    Tcp,
    Http,
}

public sealed class ManagedServiceDescriptor
{
    public string Name;
    public string Executable;
    public string Arguments;
    public string WorkingDirectory;
    public HealthCheckKind HealthCheck;
    public string HealthHost;
    public int HealthPort;
    public string HealthPath;
    public int HealthTimeoutMs;
    public bool IsDependency;
}
