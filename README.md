<p align="center">
  <img src="assets/banner.svg" alt="M-Tool" width="100%">
</p>

A small Windows tray app for the **MSI P65 Creator 9SE**: fan curves, Cooler Boost, performance
mode and battery charge limit. One exe, no background service.

> **Status:** in development. It runs on one laptop and firmware only (see below).
> The interface is in Turkish.

## Features

- **Fan profiles:** Default (factory), Cool and Silent, for the CPU and GPU fans.
- **Custom fan profiles:** copy a profile and edit its CPU and GPU curves in a curve editor, up to
  10 profiles. Every point is kept within safe limits while you edit, and a curve that would cool
  too little cannot be saved.
- **Cooler Boost** on and off.
- **Performance mode:** high, balanced or eco.
- **Charge limit** between 50 and 100 %.
- **Separate AC and battery settings** (off by default): the fan profile and performance mode you
  pick on AC and on battery are remembered apart, and applied a few seconds after the cable is
  plugged in or pulled out. The charge limit and Cooler Boost stay as they are.
- Live temperatures and fan speeds, and a tray menu for quick switching.
- A drift band when the laptop no longer holds what you chose, with one-click reapply.
- A command line for dumps and scripted changes.

## Supported devices

| Laptop | EC firmware | MSI WMI | Read | Write |
|---|---|---|---|---|
| MSI P65 Creator 9SE | `16Q4EMS2.107` | WMI1 | yes | yes |
| MSI P65 Creator 9SE, other firmware (`16Q4EMS2.1xx`) | | WMI1 | yes, unverified | no |
| Other MSI laptops with WMI1 (mostly Intel 10th gen and older) | | WMI1 | yes, unverified | no |
| MSI laptops with WMI2 (mostly Intel 11th gen and newer) | | WMI2 | not yet (`--report --wmi2` collects data) | no |

M-Tool checks the firmware at start-up. With any other firmware it only **reads** and never writes.
Other MSI models use different register layouts, so a wrong write could set a fan to the wrong
speed. Each new model needs its own map, tested on that laptop. On an unverified model the values
M-Tool shows come from the P65's map and may be wrong.

## How to help

Own another MSI laptop? A device report lets M-Tool learn its layout. It only reads: no EC write,
and the raw port is never opened.

1. Run `M-Tool.exe --report` from an administrator terminal. On a newer MSI with WMI2 (mostly
   Intel 11th gen and later), run `M-Tool.exe --report --wmi2` instead: it also records the raw
   answers of MSI's WMI2 read methods (`Get_*` only; nothing is set). These methods run firmware
   code, and that they have no side effects is an assumption, so this part is opt-in.
2. Open a [device report issue](https://github.com/kutaykalay/M-Tool/issues/new?template=device-report.yml) and attach the zip
   from `%AppData%\M-Tool\reports`.

The zip holds `report.txt` (the model, BIOS and EC firmware versions, the MSI WMI classes and the
values M-Tool could read) and `dsdt.aml`, a copy of the laptop's ACPI DSDT table. M-Tool does not
query the serial number or UUID, and it hides your user and computer names in `report.txt`. The
DSDT is firmware code written by MSI, not personal data, but it is a binary file M-Tool does not
filter. With `--wmi2`, `report.txt` also holds MSI's raw WMI2 answers as hex bytes; M-Tool does
not know what every byte means, so it cannot hide anything in them either. Read `report.txt`
before you post it.

## Requirements

- Windows 10 or 11, x64
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- [PawnIO](https://pawnio.eu) (`winget install namazso.PawnIO`)
- Administrator rights (the app asks through UAC)

## Install

1. Put `M-Tool.exe` in any folder and run it. There is no installer; the exe keeps nothing next to
   itself.
2. The first start is a **dry run**: M-Tool checks and logs your choices but writes nothing. When
   the values look right, close M-Tool, set `"dryRun": false` in
   `%AppData%\M-Tool\settings.json` and start it again.
3. Optional: tray menu → **Oturum açılışında başlat** (start at sign-in). M-Tool then starts
   hidden in the tray when you sign in, without a UAC prompt, and applies your fan profile and
   performance mode.

Start at sign-in uses a Task Scheduler task that runs the exe as administrator, from the folder it
was in when you turned the option on. If you move the exe or start another copy, M-Tool shows a
warning; turn the option off and on again from the exe you want to keep. Because the task runs
the exe as administrator, keep it in a folder only administrators can change, such as
`C:\Program Files\M-Tool`: in Downloads or on the desktop, any program you run could swap it.

M-Tool does not touch the EC while the laptop sleeps. A few seconds after waking it applies your
fan profile and performance mode again. The charge limit is not rewritten automatically; the
laptop keeps it across sleep and restarts.

## Uninstall

1. Tray menu → turn off **Oturum açılışında başlat** (this removes the task).
2. Tray menu → **Çıkış** (exit) and delete `M-Tool.exe`.
3. Optional: delete `%AppData%\M-Tool` (settings, logs, dumps). M-Tool writes nothing to the
   registry.

## How it works

- Almost everything goes through MSI's own WMI interface, which uses Windows' EC driver.
- Only Cooler Boost and the charge limit have no WMI field. Those two registers are read and written
  through PawnIO, and only when you change them.
- **Every write passes one gate:**
  - firmware check
  - register whitelist and value rules
  - a fan curve check with a safety floor
  - read-back verification
- If a write fails, M-Tool restores the factory fan table and stops writing until you clear the
  lock with `M-Tool.exe --unlock --confirm`.
- The EC runs the fan curve itself, so the fans keep the last good profile even if M-Tool closes.

## Build

```
dotnet build
dotnet test
dotnet publish src/MTool.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=embedded
```

The result is a single `M-Tool.exe`, about 1.5 MB.

## Command line

```
M-Tool.exe --dump                              EC state, written to %AppData%\M-Tool\dumps
M-Tool.exe --report [--wmi2]                   device report for another MSI model, read-only
M-Tool.exe --watch [seconds]                   log changes, read-only
M-Tool.exe --apply fan default|cool|silent     also: perf, boost, charge, fanmode
M-Tool.exe --restore                           factory fan table
```

Without `--confirm`, `--apply` and `--restore` only show what they would write.

## Credits

[YAMDCC](https://github.com/Sparronator9999/YAMDCC) and the Linux
[msi-ec](https://github.com/BeardOverflow/msi-ec) driver were used as references for the register
map. No code was copied. EC access goes through [PawnIO](https://github.com/namazso/PawnIO).

## License

Copyright (c) 2026 Kutay Kalay. M-Tool is free software: you can redistribute it and/or modify it
under the terms of the [GNU General Public License](LICENSE), version 3 or (at your option) any
later version (GPL-3.0-or-later). The bundled PawnIO module is LGPL-2.1-or-later, see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Not affiliated with or endorsed by MSI. You use M-Tool at your own risk: it writes to your laptop's
embedded controller.
