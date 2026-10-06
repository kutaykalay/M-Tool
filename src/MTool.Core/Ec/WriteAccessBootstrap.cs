using MTool.Core.Device;
using MTool.Core.Settings;

namespace MTool.Core.Ec;

/// <param name="Firmware">Null when the firmware could not be read; writes are then locked.</param>
public sealed record WriteAccessSetup(FirmwareInfo? Firmware, EcGateway Gateway);

/// <summary>
/// Opens the write path for a session: reads the firmware, takes the pre-M-Tool backup once on
/// supported firmware and builds the gateway. Never throws for EC or file trouble: anything that
/// goes wrong leaves the gateway locked (read-only mode) and is logged.
/// </summary>
public static class WriteAccessBootstrap
{
    public static async Task<WriteAccessSetup> CreateAsync(
        EcWorker worker,
        string dataDirectory,
        bool dryRun,
        bool portAvailable,
        IAppLog log,
        EcAccessRetry? retry = null,
        Func<DateTimeOffset>? now = null)
    {
        retry ??= EcAccessRetry.Default;
        var firmware = await ReadFirmwareAsync(worker, retry, log).ConfigureAwait(false);
        var store = new PreStateStore(dataDirectory);
        if (firmware is { IsSupported: true })
        {
            await BackUpOnceAsync(worker, retry, log, store, firmware, now ?? (() => DateTimeOffset.Now)).ConfigureAwait(false);
        }

        var policy = new WritePolicy(
            FirmwareSupported: firmware?.IsSupported ?? false,
            PreStateSaved: firmware is not null && HasValidSnapshot(store, firmware, log),
            DryRun: dryRun,
            PortAvailable: portAvailable,
            PersistedLockReason: ReadPersistedLock(dataDirectory, log, out var writeLock));
        return new WriteAccessSetup(firmware, new EcGateway(worker, policy, log, writeLock.Lock, retry));
    }

    private static async Task<FirmwareInfo?> ReadFirmwareAsync(EcWorker worker, EcAccessRetry retry, IAppLog log)
    {
        try
        {
            return await worker.RunRetryingAsync(ec => new P65Device(ec).ReadFirmware(), retry, log.Warn).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Error("Firmware okunamadı; EC yazma kapalı (salt okunur)", ex);
            return null;
        }
    }

    /// <summary>Only when no file exists: a corrupt snapshot is left for a human to look at.</summary>
    private static async Task BackUpOnceAsync(
        EcWorker worker, EcAccessRetry retry, IAppLog log, PreStateStore store, FirmwareInfo firmware, Func<DateTimeOffset> now)
    {
        if (store.Exists)
        {
            return;
        }

        try
        {
            var state = await worker.RunRetryingAsync(ec => PreStateCapture.Read(ec, firmware, now()), retry, log.Warn)
                .ConfigureAwait(false);
            store.SaveIfMissing(state);
            log.Info($"M-Tool öncesi durum yedeklendi ({state.Registers.Count} register).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Error("M-Tool öncesi durum yedeği alınamadı; EC yazma kapalı (salt okunur)", ex);
        }
    }

    /// <summary>Any trouble reading the snapshot (locked file, odd JSON) counts as "no valid backup".</summary>
    private static bool HasValidSnapshot(PreStateStore store, FirmwareInfo firmware, IAppLog log)
    {
        try
        {
            return store.HasValidSnapshot(firmware.Version);
        }
        catch (Exception ex)
        {
            log.Error("M-Tool öncesi durum yedeği okunamadı; EC yazma kapalı (salt okunur)", ex);
            return false;
        }
    }

    private static string? ReadPersistedLock(string dataDirectory, IAppLog log, out WriteLockStore writeLock)
    {
        writeLock = new WriteLockStore(dataDirectory);
        try
        {
            return writeLock.Reason;
        }
        catch (Exception ex)
        {
            // Fail closed: an unreadable lock file is treated as a lock.
            log.Error("Yazma kilidi dosyası okunamadı; EC yazma kapalı", ex);
            return $"kilit dosyası (write-lock.txt) okunamadı ({ex.Message}).";
        }
    }
}
