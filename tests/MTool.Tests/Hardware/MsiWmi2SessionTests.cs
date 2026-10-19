using MTool.App.Hardware;
using MTool.Core.Ec;
using MTool.Core.Localization;
using MTool.Tests.Fakes;

namespace MTool.Tests.Hardware;

/// <summary>
/// The WMI2 session refuses anything but a call on M-Tool's list before it touches WMI (7g; Aşama 10).
/// Outside Core and its tests a <see cref="Wmi2Request"/> off the lists cannot be built, but the check stays.
/// </summary>
public class MsiWmi2SessionTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(100);

    public static TheoryData<Wmi2Request> AllowedCalls() => [.. Wmi2Calls.Allowed];

    [Theory]
    [InlineData("Set_AP", 0)]
    [InlineData("Set_Fan", 0)]
    [InlineData("Set_Data", 0x98)]
    [InlineData("get_EC", 0)]
    [InlineData("Get", 0)]
    [InlineData("", 0)]
    public void A_method_that_is_not_a_get_is_refused_without_any_wmi_access(string method, byte sub)
    {
        var (session, instance, calls) = Session(_ => FakeWmi2Methods.Packet(0));

        var act = () => session.Call(new Wmi2Request(method, sub));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Only read methods*");
        calls.Should().BeEmpty();
        instance.Disposed.Should().BeFalse();
    }

    [Theory]
    [InlineData("Get_X", 0)]
    [InlineData("Get_EC", 1)]
    [InlineData("Get_Data", 0x00)]
    [InlineData("Get_Data", 0xEF)]
    [InlineData("Get_Data", 0xF2)]
    public void A_read_that_is_not_on_the_list_is_refused_without_any_wmi_access(string method, byte sub)
    {
        var (session, _, calls) = Session(_ => FakeWmi2Methods.Packet(0));

        var act = () => session.Call(new Wmi2Request(method, sub));

        act.Should().Throw<InvalidOperationException>().WithMessage("*not on M-Tool's WMI2 list*");
        calls.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(AllowedCalls))]
    public void Every_listed_call_passes_the_check(Wmi2Request request)
    {
        var act = () => MsiWmi2Session.EnsureAllowed(request);

        act.Should().NotThrow();
    }

    [Fact]
    public void A_listed_call_reaches_wmi_once_and_returns_its_answer()
    {
        var (session, _, calls) = Session(_ => FakeWmi2Methods.Packet(61));

        var answer = session.Call(Wmi2Calls.Live[0]);

        answer[1].Should().Be(61);
        calls.Should().Equal(Wmi2Calls.Live[0]);
    }

    [Fact]
    public void A_failed_call_is_an_ec_access_error_and_the_next_call_still_runs()
    {
        var fail = true;
        var (session, _, calls) = Session(_ => fail ? throw new InvalidOperationException("no result") : FakeWmi2Methods.Packet(1));

        var first = () => session.Call(Wmi2Calls.Live[0]);
        first.Should().Throw<EcAccessException>().WithMessage("*MSI_ACPI Get_Data(0x68)*no result*");
        fail = false;

        session.Call(Wmi2Calls.Live[1])[1].Should().Be(1);
        calls.Should().HaveCount(2);
    }

    [Fact]
    public void After_a_call_that_never_answers_no_other_call_is_made()
    {
        using var release = new ManualResetEventSlim();
        var (session, _, calls) = Session(_ =>
        {
            release.Wait();
            return FakeWmi2Methods.Packet(0);
        });

        var hung = () => session.Call(Wmi2Calls.Live[0]);
        hung.Should().Throw<EcAccessException>().Which.ForLog().Should().Contain("timeout");
        var next = () => session.Call(Wmi2Calls.Live[1]);

        using (TestCulture.Use("tr"))
        {
            // Shown in the report and live-read windows: in the user's language, English in the log.
            var refused = next.Should().Throw<EcAccessException>().Which;
            refused.Message.Should().Be("MSI_ACPI: önceki bir çağrı hâlâ sürüyor (hiç cevap vermedi); çağrı yapılmadı.");
            refused.ForLog().Should().Be("MSI_ACPI: an earlier call is still running (it never answered); no call made.");
        }

        calls.Should().ContainSingle();
        release.Set();
    }

    [Fact]
    public void Disposing_while_a_call_hangs_leaves_the_instance_to_the_call()
    {
        using var release = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        var (session, instance, _) = Session(_ =>
        {
            release.Wait();
            return FakeWmi2Methods.Packet(0);
        }, onDispose: finished.Set);
        var hung = () => session.Call(Wmi2Calls.Live[0]);
        hung.Should().Throw<EcAccessException>();

        session.Dispose();

        instance.Disposed.Should().BeFalse("the abandoned call is still using it");
        release.Set();
        finished.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("the last call to finish disposes the instance");
        instance.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void An_idle_session_disposes_its_instance_once()
    {
        var (session, instance, _) = Session(_ => FakeWmi2Methods.Packet(0));

        session.Dispose();
        session.Dispose();

        instance.DisposeCount.Should().Be(1);
        var act = () => session.Call(Wmi2Calls.Live[0]);
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void The_report_probe_makes_exactly_the_report_calls()
    {
        var wmi = new FakeWmi2Methods();

        var readout = MsiWmi2Session.Probe(wmi);

        wmi.Calls.Should().Equal(Wmi2Calls.Report);
        readout.Calls.Should().HaveCount(Wmi2Calls.Report.Count);
    }

    private static (MsiWmi2Session Session, FakeInstance Instance, List<Wmi2Request> Calls) Session(
        Func<Wmi2Request, byte[]> answer, Action? onDispose = null)
    {
        var instance = new FakeInstance(onDispose);
        var calls = new List<Wmi2Request>();
        var session = new MsiWmi2Session(instance, request =>
        {
            lock (calls)
            {
                calls.Add(request);
            }

            return answer(request);
        }, ShortTimeout);
        return (session, instance, calls);
    }

    private sealed class FakeInstance(Action? onDispose) : IDisposable
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public bool Disposed => DisposeCount > 0;

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
            onDispose?.Invoke();
        }
    }
}
