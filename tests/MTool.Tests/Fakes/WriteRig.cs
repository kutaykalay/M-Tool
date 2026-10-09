using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Settings;

namespace MTool.Tests.Fakes;

/// <summary>What one run of a write scenario left behind: the text for the user and everything that must not depend on it.</summary>
/// <param name="Facts">Status, planned writes, every EC access, the lock file and the log; equal in every language.</param>
internal sealed record WriteRun(
    IReadOnlyList<string> Statuses,
    string Facts,
    IReadOnlyList<string> Log,
    string Texts,
    string? LockReason,
    string LockFileText);

/// <summary>
/// A fake EC that records every access, with a gateway and a lock folder of its own. The culture tests run
/// a scenario in Turkish, English and German over a fresh rig each and compare the runs.
/// </summary>
internal sealed class WriteRig : IDisposable
{
    private static readonly WritePolicy Live = new(FirmwareSupported: true, PreStateSaved: true, DryRun: false, PortAvailable: true);
    private static readonly FirmwareInfo Firmware = new("16Q4EMS2.107", "05132019");

    private readonly List<byte> _reads = [];
    private readonly EcAccessRetry _retry = EcAccessRetry.Default with { Sleep = _ => { } };
    private EcGateway? _gateway;

    public WriteRig()
    {
        Worker = new EcWorker(Ec, EcLock, TimeSpan.FromMilliseconds(50));
        Ec.ReadHook = register =>
        {
            lock (_reads)
            {
                _reads.Add(register);
            }

            return ReadHook?.Invoke(register);
        };
    }

    public FakeEcRegisters Ec { get; } = P65Memory.FactorySnapshot();

    public FakeEcLock EcLock { get; } = new();

    public ListLog Log { get; } = new();

    public EcWorker Worker { get; }

    public string Folder { get; } = Directory.CreateTempSubdirectory("mtool-culture-").FullName;

    public string LockFilePath => Path.Combine(Folder, "write-lock.txt");

    /// <summary>Overrides what an EC read returns (as <see cref="FakeEcRegisters.ReadHook"/>); the rig still records the read.</summary>
    public Func<byte, byte?>? ReadHook { get; set; }

    public EcGateway Gateway(WritePolicy? policy = null) =>
        _gateway = new EcGateway(Worker, policy ?? Live, Log, new WriteLockStore(Folder).Lock, _retry);

    public P65Control Control(WritePolicy? policy = null) =>
        new(Worker, new WriteAccessSetup(Firmware, Gateway(policy)), TestLayouts.P65, Log, _retry);

    /// <summary>Runs <paramref name="act"/> with the UI language set to <paramref name="culture"/>.</summary>
    /// <param name="exceptionInMessage">
    /// The first message ends with the text of an exception from the EC worker, which also reaches the log
    /// through the exception; that text is masked in the log (it follows the language, see the plan).
    /// </param>
    public static async Task<WriteRun> RunAsync(string culture, Func<WriteRig, Task<WriteOutcome[]>> act, bool exceptionInMessage = false)
    {
        using var rig = new WriteRig(); // its worker thread starts in the test's own culture, not the one under test
        using var _ = TestCulture.Use(culture);
        var outcomes = await act(rig);
        return rig.Finish(outcomes, exceptionInMessage);
    }

    public WriteRun Finish(WriteOutcome[] outcomes, bool exceptionInMessage)
    {
        var message = outcomes[0].Message;
        var exception = exceptionInMessage ? message[(message.IndexOf(" (", StringComparison.Ordinal) + 2)..^1] : null;
        var log = Log.Lines.Select(l => exception is null ? l : l.Replace(exception, "<exception>", StringComparison.Ordinal)).ToArray();
        string[] reads;
        lock (_reads)
        {
            reads = [.. _reads.Select(r => $"{r:X2}")];
        }

        var facts = string.Join(
            "\n",
            $"status: {string.Join(' ', outcomes.Select(o => o.Status))}",
            $"planned: {string.Join(" | ", outcomes.Select(o => string.Join(' ', o.Planned)))}",
            $"writes: {string.Join(' ', Ec.Writes.Select(w => $"{w.Register:X2}={w.Value:X2}"))}",
            $"reads: {string.Join(' ', reads)}",
            $"accesses: {Ec.Accesses}",
            $"lock file: {File.Exists(LockFilePath)}",
            $"write enabled: {_gateway?.IsWriteEnabled}",
            $"log:\n{string.Join('\n', log)}");
        return new WriteRun(
            [.. outcomes.Select(o => o.Status.ToString())],
            facts,
            log,
            string.Join(" | ", outcomes.Select(o => o.Message)),
            _gateway?.LockReason,
            File.Exists(LockFilePath) ? File.ReadAllText(LockFilePath) : "");
    }

    public void Dispose()
    {
        Worker.Dispose();
        Directory.Delete(Folder, recursive: true);
    }
}
