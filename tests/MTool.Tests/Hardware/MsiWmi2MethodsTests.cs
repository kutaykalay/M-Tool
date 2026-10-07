using MTool.App.Hardware;

namespace MTool.Tests.Hardware;

/// <summary>The WMI2 caller refuses anything but a read method before it touches WMI (7g).</summary>
public class MsiWmi2MethodsTests
{
    [Theory]
    [InlineData("Set_AP")]
    [InlineData("Set_Fan")]
    [InlineData("get_EC")]
    [InlineData("Get")]
    [InlineData("")]
    public void A_method_that_is_not_a_get_is_refused_without_any_wmi_access(string method)
    {
        // No instance at all: the refusal must come before the first WMI call.
        var act = () => MsiWmi2Methods.Call(instance: null!, method, 0);

        act.Should().Throw<InvalidOperationException>().WithMessage("*okuma*");
    }
}
