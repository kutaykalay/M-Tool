<p align="center">
  <img src="assets/banner.svg" alt="M-Tool" width="100%">
</p>

A small Windows tray app for the **MSI P65 Creator 9SE**: fan curves, Cooler Boost, performance
mode and battery charge limit. One exe, no background service.

> **Status:** in development. It runs on one laptop and firmware only (see below).
> The interface is in Turkish.

## Features

- **Fan profiles:** Default (factory), Cool and Silent, for the CPU and GPU fans.
- **Cooler Boost** on and off.
- **Performance mode:** high, balanced or eco.
- **Charge limit** between 50 and 100 %.
- Live temperatures and fan speeds, and a tray menu for quick switching.
- A drift band when the laptop no longer holds what you chose, with one-click reapply.
- A command line for dumps and scripted changes.

## Supported device

| Laptop | EC firmware | Status |
|---|---|---|
| MSI P65 Creator 9SE | `16Q4EMS2.107` | Supported |

M-Tool checks the firmware at start-up. With any other firmware it only **reads** and never writes.
Other MSI models use different register layouts, so a wrong write could set a fan to the wrong
speed. Each new model needs its own map, tested on that laptop.

## Requirements

- Windows 10 or 11, x64
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- [PawnIO](https://pawnio.eu) (`winget install namazso.PawnIO`)
- Administrator rights (the app asks through UAC)

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

The result is a single `M-Tool.exe`, about 1 MB.

## Command line

```
M-Tool.exe --dump                              EC state, written to %AppData%\M-Tool\dumps
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
