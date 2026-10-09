using System.Globalization;

namespace MTool.Tests.Fakes;

/// <summary>
/// Switches the interface language for one test. The test run is Turkish (xunit.runner.json); the English
/// tests wrap their body in <c>using var _ = TestCulture.Use("en")</c>. The culture follows the async flow
/// and goes back when the scope ends, so parallel tests do not see it.
/// </summary>
internal static class TestCulture
{
    public static IDisposable Use(string name)
    {
        var before = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        return new Restore(before);
    }

    private sealed class Restore(CultureInfo before) : IDisposable
    {
        public void Dispose() => CultureInfo.CurrentUICulture = before;
    }
}
