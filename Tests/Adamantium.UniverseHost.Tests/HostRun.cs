namespace Adamantium.UniverseHost.Tests;

/// <summary>How a run of the host process ended: its exit code and what it wrote to its error output.</summary>
internal sealed record HostRun(int ExitCode, string Errors);
