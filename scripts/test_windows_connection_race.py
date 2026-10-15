"""Compile the actual connection-winner branch with disposable session stubs."""

from pathlib import Path
import subprocess
import sys


root = Path(__file__).resolve().parent.parent
source = (root / "windows/Hinge.App/MainWindow.xaml.cs").read_text(encoding="utf-8-sig")
start = source.index("if (tcs.TrySetResult(direct))")
end = source.index("catch (Exception exception)", start)
# Remove the surrounding try's closing brace, keeping the actual if/else.
branch = source[start:end].rstrip().rsplit("}", 1)[0].rstrip()
if "--baseline" in sys.argv:
    guard = "if (!ReferenceEquals(direct, tcs.Task.Result) &&"
    assert guard in branch
    guard_end = branch.index("}", branch.index(guard)) + 1
    branch = branch[:branch.index(guard)] + "direct.Dispose();" + branch[guard_end:]

directory = root / "tmp/windows-connection-race-selfcheck"
directory.mkdir(parents=True, exist_ok=True)
(directory / "SelfCheck.csproj").write_text(
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
    '<OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>'
    '<ImplicitUsings>enable</ImplicitUsings>'
    '</PropertyGroup></Project>', encoding="utf-8"
)
(directory / "Program.cs").write_text(
    """
static class Program
{
    static readonly Manager _sessionManager = new();
    static readonly Device device = new();
    static void Complete(Session direct, TaskCompletionSource<Session> tcs,
        CancellationTokenSource cts)
    {
""" + branch + """
    }

    static void Main()
    {
        for (var scenario = 0; scenario < 4; scenario++)
        {
            using var cts = new CancellationTokenSource();
            var direct = new Session();
            var other = new Session();
            var tcs = new TaskCompletionSource<Session>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (scenario == 1) tcs.SetResult(direct);
            if (scenario >= 2) tcs.SetResult(other);
            _sessionManager.Current = scenario == 3 ? direct : null;
            Complete(direct, tcs, cts);
            if (tcs.Task.Result.Disposed)
                throw new Exception($"Winner disposed in scenario {scenario}");
            if (direct.Disposed != (scenario == 2))
                throw new Exception($"Wrong loser cleanup in scenario {scenario}");
            if (scenario == 0 && !cts.IsCancellationRequested)
                throw new Exception("Winner did not cancel the race");
        }
        Console.WriteLine("PASS: direct winner, same winner, distinct loser, deduplicated replacement");
    }
}
sealed class Manager
{
    public Session Current;
    public Session ConnectionForDevice(string id) => Current;
}
sealed class Device { public string DeviceId = "test"; }
sealed class Session : IDisposable
{
    public bool Disposed;
    public void Dispose() => Disposed = true;
}
""", encoding="utf-8"
)
result = subprocess.run(["dotnet", "run", "--project", str(directory / "SelfCheck.csproj")])
sys.exit(result.returncode)
