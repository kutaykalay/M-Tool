using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Tests.Fakes;

namespace MTool.Tests.Ec;

/// <summary>
/// The CLI blocks the WPF UI thread while it waits for EC work. Core must not post continuations
/// back to the caller's context, or that wait deadlocks (seen on hardware, 2026-09-30).
/// </summary>
public class SynchronizationContextTests
{
    [Fact]
    public void Gateway_completes_while_the_calling_context_is_blocked()
    {
        using var worker = new EcWorker(P65Memory.Faz0Snapshot(), new FakeEcLock(), TimeSpan.FromMilliseconds(50));
        var gateway = new EcGateway(worker, new WritePolicy(true, true, DryRun: true), new ListLog());
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new NeverRunningContext());
        try
        {
            var task = gateway.ApplyAsync(WritePlans.ChargeLimit(79));

#pragma warning disable xUnit1031 // Blocking is the scenario under test.
            task.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("the gateway must not need the blocked context");
#pragma warning restore xUnit1031
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>Like a UI thread stuck in a blocking wait: posted work never runs.</summary>
    private sealed class NeverRunningContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
        }
    }
}
