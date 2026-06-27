namespace IctTrader.E2E.Fixtures;

/// <summary>
/// Probes whether a Docker daemon is reachable so the E2E gate can fail FAST with a clear message when it is not,
/// rather than timing out deep inside Testcontainers. Mirrors the IntegrationTests <c>DockerRequiredFact</c> probe:
/// an explicit <c>tcp://</c> <c>DOCKER_HOST</c> is assumed reachable (typical in CI); otherwise the actual daemon
/// endpoint is probed — the named pipe on Windows, the unix socket elsewhere.
/// </summary>
internal static class DockerProbe
{
    public static bool IsAvailable()
    {
        if (Environment.GetEnvironmentVariable("SKIP_DOCKER_TESTS") == "true")
        {
            return false;
        }

        try
        {
            var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");

            if (dockerHost?.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            return OperatingSystem.IsWindows()
                ? WindowsDockerPipeExists()
                : File.Exists(UnixSocketPath(dockerHost));
        }
        catch
        {
            return false;
        }
    }

    private static string UnixSocketPath(string? dockerHost) =>
        dockerHost?.StartsWith("unix://", StringComparison.OrdinalIgnoreCase) == true
            ? dockerHost["unix://".Length..]
            : "/var/run/docker.sock";

    private static bool WindowsDockerPipeExists()
    {
        try
        {
            return Directory.EnumerateFiles(@"\\.\pipe\")
                .Any(name => name.EndsWith("docker_engine", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
