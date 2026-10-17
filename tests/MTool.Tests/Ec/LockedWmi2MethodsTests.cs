using MTool.Core.Ec;
using MTool.Tests.Fakes;

namespace MTool.Tests.Ec;

/// <summary>The WMI2 report's calls take the shared EC lock, so they never run alongside a live round.</summary>
public class LockedWmi2MethodsTests
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMilliseconds(500);

    [Fact]
    public void Each_call_runs_inside_the_lock()
    {
        var ecLock = new FakeEcLock();
        var heldDuringCall = false;
        var wmi = new FakeWmi2Methods();
        wmi.AnswerHook = _ =>
        {
            heldDuringCall = ecLock.Held;
            return null;
        };

        new LockedWmi2Methods(wmi, ecLock, LockTimeout).Call(Wmi2Calls.Live[0]);

        heldDuringCall.Should().BeTrue();
        ecLock.Held.Should().BeFalse();
        ecLock.Events.Select(e => e.Action).Should().Equal("acquire", "release");
    }

    [Fact]
    public void A_lock_that_cannot_be_taken_is_an_ec_access_error_and_no_call()
    {
        var ecLock = new FakeEcLock { Available = false };
        var wmi = new FakeWmi2Methods();

        var call = () => new LockedWmi2Methods(wmi, ecLock, LockTimeout).Call(Wmi2Calls.Live[0]);

        call.Should().Throw<EcAccessException>().WithMessage("*Get_Data(0x68)*");
        wmi.Calls.Should().BeEmpty();
    }

    [Fact]
    public void A_failed_call_still_releases_the_lock()
    {
        var ecLock = new FakeEcLock();
        var wmi = new FakeWmi2Methods { Error = new EcAccessException("no answer") };

        var call = () => new LockedWmi2Methods(wmi, ecLock, LockTimeout).Call(Wmi2Calls.Live[0]);

        call.Should().Throw<EcAccessException>().WithMessage("no answer");
        ecLock.Held.Should().BeFalse();
    }

    [Fact]
    public void The_answer_is_passed_through_unchanged()
    {
        var wmi = new FakeWmi2Methods { [0x68] = 61 };

        var answer = new LockedWmi2Methods(wmi, new FakeEcLock(), LockTimeout).Call(Wmi2Calls.Live[0]);

        answer.Should().Equal(FakeWmi2Methods.Packet(61));
    }
}
