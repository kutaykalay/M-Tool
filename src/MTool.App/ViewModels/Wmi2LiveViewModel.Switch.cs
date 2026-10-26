using MTool.App.Resources;
using MTool.Core.Localization;

namespace MTool.App.ViewModels;

internal sealed partial class Wmi2LiveViewModel
{
    /// <summary>
    /// Reads through another source from now on: experimental control, which takes the place of the read-only
    /// source (only one <c>MSI_ACPI</c> session is open at a time). The 2 s rule passes first, the timer waits,
    /// the current source is closed, then the other one opened; when it does not open (null or an error), the
    /// read-only source opens again. Opening reads the EC, so it counts as a round for the 2 s rule, and the
    /// next round reads the status. Nothing happens before reading has started or while a round runs.
    /// </summary>
    /// <param name="openOther">Opens the other source (it may block on the EC); null when it does not apply.</param>
    /// <returns>Whether the switch ran (whichever source it ended on); false when it could not start or the window closed.</returns>
    internal async Task<bool> SwitchSourceAsync(Func<CancellationToken, Task<IWmi2LiveSource?>> openOther)
    {
        if (!CanSwitch())
        {
            return false;
        }

        await WaitForMinIntervalAsync();
        if (!CanSwitch())
        {
            return false; // the window closed, or a round started while waiting
        }

        IsBusy = true;
        StopPolling();
        IWmi2LiveSource? next;
        try
        {
            var current = _source;
            _source = null;
            await Task.Run(() => Close(current)); // waits for a running call, at most the call timeout
            next = await OpenOtherAsync(openOther);
            if (next is null && !_closing.IsCancellationRequested)
            {
                next = await Task.Run(Open);
            }

            _lastRound = time.GetTimestamp();
        }
        finally
        {
            IsBusy = false;
        }

        if (_closing.IsCancellationRequested)
        {
            next?.Dispose(); // the window closed while the source was opening
            return false;
        }

        Switched(next);
        return true;
    }

    private bool CanSwitch() => IsStarted && !IsBusy && !IsSourceHeld && !_closing.IsCancellationRequested;

    /// <summary>A source that fails to close is logged: the switch goes on, its session is gone either way.</summary>
    private void Close(IWmi2LiveSource? source)
    {
        try
        {
            source?.Dispose();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Warn($"WMI2 live read: the source did not close cleanly: {ex.ForLog()}");
        }
    }

    private async Task<IWmi2LiveSource?> OpenOtherAsync(Func<CancellationToken, Task<IWmi2LiveSource?>> openOther)
    {
        try
        {
            return await openOther(_closing.Token);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            return null; // the window closed while it was opening
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Warn($"WMI2 live read: the other source did not open, reading only again: {ex.ForLog()}");
            return null;
        }
    }

    private void Switched(IWmi2LiveSource? next)
    {
        _source = next;
        if (next is null)
        {
            IsStarted = false;
            IsUnavailable = true;
            Message = Strings.Wmi2_Unavailable;
        }
        else
        {
            _statusWanted = true;
            StartPolling();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
