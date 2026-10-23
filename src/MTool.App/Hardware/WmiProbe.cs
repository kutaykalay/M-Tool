using System.Diagnostics;
using System.Management;
using MTool.App.Resources;
using MTool.Core;
using MTool.Core.Diagnostics;
using MTool.Core.Device.Config;
using MTool.Core.Ec;
using MTool.Core.Localization;

namespace MTool.App.Hardware;

/// <summary>A model M-Tool cannot read yet; the message is meant for the user.</summary>
/// <param name="message">In the language of the window.</param>
/// <param name="logMessage">The same sentence in English, for the log.</param>
/// <param name="detected">The interface the probe found (never WMI1 here): WMI2, or null for none.</param>
internal sealed class UnsupportedDeviceException(string message, string logMessage, WmiInterface? detected) : Exception(message)
{
    public string LogMessage { get; } = logMessage;

    public WmiInterface? Detected { get; } = detected;
}

/// <summary>
/// Finds which MSI WMI interface this laptop has before a session opens, so a model M-Tool cannot
/// read gets a clear message instead of a WMI error. Only class definitions are queried: no
/// instance is read, no method is called, the EC is never touched.
/// </summary>
internal static class WmiProbe
{
    private const string Wmi1Class = "MSI_Software";
    private const string Wmi2Class = "MSI_ACPI";
    private static readonly TimeSpan ProbeBudget = TimeSpan.FromSeconds(2);

    /// <exception cref="EcAccessException">WMI does not answer.</exception>
    public static WmiInterface? Detect() => InterfaceOrThrow(DetectDetailed());

    /// <summary>
    /// The probe with its reason when WMI does not answer: which step failed and its code. Connect
    /// and the class queries run one after the other within one <see cref="ProbeBudget"/>, each on
    /// its own. <c>MSI_ACPI</c> is asked only when <c>MSI_Software</c> answered "absent": WMI1 wins,
    /// and a failed <c>MSI_Software</c> query is never read as WMI2. Never throws.
    /// </summary>
    public static WmiProbeOutcome DetectDetailed() => DetectDetailed(ProbeBudget);

    /// <inheritdoc cref="DetectDetailed()"/>
    /// <param name="budget">For all steps together, instead of the default 2 s.</param>
    public static WmiProbeOutcome DetectDetailed(TimeSpan budget) => DetectDetailed(MsiWmiFields.Connect, HasClass, budget);

    /// <param name="budget">For all steps together; a step gets what the earlier ones left.</param>
    internal static WmiProbeOutcome DetectDetailed<TScope>(Func<TScope> connect, Func<TScope, string, bool> hasClass, TimeSpan budget)
    {
        var watch = Stopwatch.StartNew();
        var connected = Step(WmiProbeStage.Connect, connect, budget - watch.Elapsed);
        if (connected.Failure is { } failure)
        {
            return WmiProbeOutcome.Failed(failure);
        }

        var scope = connected.Value!;
        var software = AnswerOf(Step(WmiProbeStage.QuerySoftware, () => hasClass(scope, Wmi1Class), budget - watch.Elapsed));
        if (software.Failure is not null || software.Found)
        {
            return WmiProbeOutcome.Decide(software, WmiClassAnswer.Of(false));
        }

        var acpi = AnswerOf(Step(WmiProbeStage.QueryAcpi, () => hasClass(scope, Wmi2Class), budget - watch.Elapsed));
        return WmiProbeOutcome.Decide(software, acpi);
    }

    /// <summary>What <see cref="Detect"/> returns: the interface, or the failure as a WMI error naming stage and code.</summary>
    /// <exception cref="EcAccessException">The probe failed.</exception>
    internal static WmiInterface? InterfaceOrThrow(WmiProbeOutcome outcome)
    {
        if (outcome.Failure is not { } failure)
        {
            return outcome.Interface;
        }

        var what = $"interface detection, {failure.Stage}";
        var (text, log) = failure.TimedOut
            ? AppText.Say(nameof(Strings.Wmi_NoAnswer), what, ProbeBudget.TotalSeconds)
            : AppText.Say(nameof(Strings.Wmi_CallFailed), what, failure.CodeText);
        throw new EcAccessException(text).WithLog(log);
    }

    /// <summary>
    /// The number WMI gave: the WMI status for <see cref="ManagementException"/> (<c>0x8004xxxx</c>),
    /// else the HRESULT, which for an error that is not WMI's own is a CLR code such as <c>0x80131509</c>.
    /// </summary>
    internal static uint CodeOf(Exception error) => error is ManagementException wmi
        ? unchecked((uint)wmi.ErrorCode)
        : unchecked((uint)error.HResult);

    /// <summary>
    /// Every MSI class in <c>root\WMI</c> with its property names, from the class definitions only:
    /// listing instances would run the firmware's ACPI methods, which a report must not do.
    /// </summary>
    /// <exception cref="EcAccessException">WMI does not answer.</exception>
    public static IReadOnlyList<WmiClassInfo> MsiClasses() => MsiWmiFields.Bounded("class list", ProbeBudget, () =>
    {
        using var searcher = new ManagementObjectSearcher(
            MsiWmiFields.Connect(), new ObjectQuery("SELECT * FROM meta_class WHERE __CLASS LIKE 'MSI[_]%'"),
            new EnumerationOptions { Timeout = ProbeBudget });
        using var classes = searcher.Get();
        var found = new List<WmiClassInfo>();
        foreach (var item in classes.Cast<ManagementClass>())
        {
            using (item)
            {
                found.Add(new WmiClassInfo(
                    item.ClassPath.ClassName,
                    Array.AsReadOnly([.. item.Properties.Cast<PropertyData>().Select(p => p.Name).Order(StringComparer.Ordinal)])));
            }
        }

        return (IReadOnlyList<WmiClassInfo>)found.OrderBy(c => c.Name, StringComparer.Ordinal).ToList().AsReadOnly();
    });

    /// <inheritdoc cref="WmiProbeOutcome.Classify"/>
    internal static WmiInterface? Classify(bool hasSoftware, bool hasAcpi) => WmiProbeOutcome.Classify(hasSoftware, hasAcpi);

    internal static string UnsupportedMessage(WmiInterface? detected) => detected == WmiInterface.Wmi2
        ? Strings.Probe_UnsupportedWmi2
        : string.Format(Strings.Probe_UnsupportedNoWmi, Wmi1Class, Wmi2Class);

    /// <summary>What <see cref="UnsupportedMessage"/> says, always in English: the log is read by people in any language.</summary>
    internal static string UnsupportedLogMessage(WmiInterface? detected) => detected == WmiInterface.Wmi2
        ? "This MSI model is not supported yet: it reaches the EC through MSI's WMI2 interface. " +
          "M-Tool can only read WMI1 models for now."
        : $"This computer is not supported yet: the MSI WMI interface ({Wmi1Class} or {Wmi2Class} in root\\WMI) was not found.";

    /// <summary>
    /// Runs one step on a thread-pool (MTA) thread, like <see cref="MsiWmiFields.Bounded"/>, but keeps
    /// the code instead of Windows' message. A hung step is abandoned; its late failure is observed and dropped.
    /// </summary>
    private static StepResult<T> Step<T>(WmiProbeStage stage, Func<T> access, TimeSpan left)
    {
        if (left <= TimeSpan.Zero)
        {
            return new(default, new WmiFailure(stage, null));
        }

        var call = Task.Run(access);
        try
        {
            if (call.Wait(left))
            {
                return new(call.Result, null);
            }
        }
        catch (AggregateException ex) when (ex.InnerException is { } inner)
        {
            return new(default, new WmiFailure(stage, CodeOf(inner)));
        }

        _ = call.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        return new(default, new WmiFailure(stage, null));
    }

    private static bool HasClass(ManagementScope scope, string className)
    {
        using var searcher = new ManagementObjectSearcher(
            scope, new ObjectQuery($"SELECT * FROM meta_class WHERE __CLASS = '{className}'"), new EnumerationOptions { Timeout = ProbeBudget });
        using var classes = searcher.Get();
        return classes.Count > 0;
    }

    private static WmiClassAnswer AnswerOf(StepResult<bool> step) =>
        step.Failure is { } failure ? WmiClassAnswer.Failed(failure) : WmiClassAnswer.Of(step.Value);

    private readonly record struct StepResult<T>(T? Value, WmiFailure? Failure);
}

/// <summary>
/// The check before a session opens. Only a definite answer stops start-up: a probe that times out
/// or fails (WMI still starting at log-on) is logged and the session opens as it did before the
/// probe existed, failing there if WMI1 really is missing. A confirmed WMI1 is not probed again.
/// </summary>
/// <param name="detect">The probe with its time budget.</param>
internal sealed class WmiInterfaceCheck(Func<TimeSpan?, WmiProbeOutcome> detect)
{
    private volatile bool _wmi1Confirmed;

    public static WmiInterfaceCheck Default { get; } =
        new(budget => budget is { } b ? WmiProbe.DetectDetailed(b) : WmiProbe.DetectDetailed());

    /// <summary>The probe's answer; a WMI1 answer is remembered, so the session that follows does not probe again.</summary>
    /// <param name="budget">Null for the probe's default.</param>
    public WmiProbeOutcome Probe(TimeSpan? budget = null)
    {
        if (_wmi1Confirmed)
        {
            return WmiProbeOutcome.Answered(WmiInterface.Wmi1);
        }

        var outcome = detect(budget);
        if (outcome is { Failure: null, Interface: WmiInterface.Wmi1 })
        {
            _wmi1Confirmed = true;
        }

        return outcome;
    }

    /// <exception cref="UnsupportedDeviceException">The probe answered: WMI2 only, or no MSI WMI at all.</exception>
    public void EnsureWmi1(IAppLog log)
    {
        var outcome = Probe();
        if (outcome.Failure is { } failure)
        {
            log.Warn($"MSI WMI interface not detected ({failure}), trying to open the session anyway.");
            return;
        }

        if (outcome.Interface is not WmiInterface.Wmi1 and var detected)
        {
            throw new UnsupportedDeviceException(WmiProbe.UnsupportedMessage(detected), WmiProbe.UnsupportedLogMessage(detected), detected);
        }
    }
}
