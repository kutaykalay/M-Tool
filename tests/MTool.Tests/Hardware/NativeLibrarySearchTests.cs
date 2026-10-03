using System.Reflection;
using System.Runtime.InteropServices;
using MTool.App.Hardware;

namespace MTool.Tests.Hardware;

public class NativeLibrarySearchTests
{
    [Fact]
    public void The_app_loads_native_libraries_from_system32_only()
    {
        var attribute = typeof(PawnIoPortIo).Assembly.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Paths.Should().Be(DllImportSearchPath.System32);
    }
}
