<p align="center">
  <img src="assets/banner.svg" alt="M-Tool" width="100%">
</p>

[![Build](https://github.com/kutaykalay/M-Tool/actions/workflows/build.yml/badge.svg)](https://github.com/kutaykalay/M-Tool/actions/workflows/build.yml)

A small Windows tray app for the **MSI P65 Creator 9SE**: fan curves, Cooler Boost, performance
mode and battery charge limit. One exe, no background service.

> **Status:** beta. M-Tool writes settings on one laptop and firmware only. Other MSI laptops with
> WMI1 are read-only and experimental, and on MSI laptops with WMI2 it shows a few values, read-only
> and experimental; see [Supported devices](#supported-devices).

## Features

- **Fan profiles:** Default (factory), Cool and Silent, for the CPU and GPU fans.
- **Custom fan profiles:** copy a profile and edit its CPU and GPU curves in a curve editor, up to
  10 profiles. Every point is kept within safe limits while you edit, and a curve that would cool
  too little cannot be saved. Points can be dragged on a chart beside the number table, or moved with
  the arrow keys.
- **Cooler Boost** on and off.
- **Performance mode:** high, balanced or eco.
- **Charge limit** between 50 and 100 %.
- **Keyboard backlight:** off and three steps on a bar in the main window, P65 only. The Fn key keeps
  working, and the bar follows it while the window is open. M-Tool does not set the backlight again
  at start-up or after sleep.
- **Separate AC and battery settings** (off by default): the fan profile and performance mode you
  pick on AC and on battery are remembered apart, and applied a few seconds after the cable is
  plugged in or pulled out. The charge limit and Cooler Boost stay as they are.
- Live temperatures and fan speeds, and a tray menu for quick switching.
- **Settings window** (tray "Settings…" or the small button in the main window): a General tab
  (start at sign-in, language, AC/battery switching) and an About tab (version, device info with
  copy, licenses, GitHub, log folder, update check).
- A drift band when the laptop no longer holds what you chose, with one-click reapply.
- A command line for dumps and scripted changes.

## Languages

| Language | Status |
|---|---|
| English | reviewed |
| Türkçe (Turkish) | reviewed |
| 中文（简体）(Chinese, Simplified) | machine-translated, review welcome |
| Русский (Russian) | machine-translated, review welcome |
| Español (Spanish) | machine-translated, review welcome |
| Português (Brasil) (Portuguese, Brazil) | machine-translated, review welcome |
| Deutsch (German) | machine-translated, review welcome |
| Français (French) | machine-translated, review welcome |

M-Tool follows the Windows display language; any other language gets English. Tray menu →
**Language** changes it from the next start. The log is always in English, so a log attached to an
issue reads the same for everyone. Corrections are welcome: see "Translations" in
[CONTRIBUTING.md](CONTRIBUTING.md).

## Supported devices

| Laptop | EC firmware | MSI WMI | Read | Write |
|---|---|---|---|---|
| MSI P65 Creator 9SE | `16Q4EMS2.107` | WMI1 | yes | yes |
| MSI P65 Creator 9SE, other firmware (`16Q4EMS2.1xx`) | | WMI1 | yes, unverified | no |
| Other MSI laptops with WMI1 (mostly Intel 10th gen and older) | | WMI1 | yes, unverified | no |
| MSI laptops with WMI2 (mostly Intel 11th gen and newer) | | WMI2 | experimental, a few values; [help check them](#wmi2-testers-wanted) | no |

M-Tool checks the firmware at start-up and, with any other firmware, only **reads**. Other MSI
models use different register layouts, so a wrong write could set a fan to the wrong speed. Each new
model needs its own map, tested on that laptop. Until then, the values M-Tool shows on it come from
the P65's map and may be wrong.

On a WMI2 laptop M-Tool opens its reports window instead of the app. Its **Live values
(experimental)** section shows the temperatures, fan speeds and a few settings, read through MSI's
WMI2 `Get_*` methods: only after you say Yes, and about every 2 seconds while the window is on screen.
Nothing is written. The EC addresses come from other projects and have not been checked with
M-Tool on any WMI2 laptop yet, so every value is shown with its raw byte and marked *not
verified*. You can help check them: see [WMI2 testers wanted](#wmi2-testers-wanted).

## How to help

Own another MSI laptop? M-Tool can learn its layout from a read-only report: nothing is written to
the EC, and the raw port is never opened. On a laptop M-Tool cannot control yet, it opens a small
window with the same report actions instead of the app.

### Easiest: Send EC data

1. Right-click the M-Tool tray icon and choose **Send EC data (for a config)**.
2. M-Tool reads the data, writes nothing to disk, and shows the whole text it would send. The
   default button is **Don't send**.
3. Read the text and choose **Send** if you agree. M-Tool makes one HTTPS request to the M-Tool
   server, which checks the data and stores it as an issue in a private GitHub repository.

On a laptop with WMI2, M-Tool first asks whether it may read the WMI2 data; the default is **No**,
and with No nothing is read or sent. With Yes, a fixed list of `Get_*` methods runs once (the raw
packets, and `Get_Data` at twelve fixed EC addresses) and `Set_*` is never called. These methods
run firmware code that is assumed to have no side effects, which is why this part is opt-in.

### WMI2 testers wanted

M-Tool's WMI2 reading has not run on a real WMI2 laptop yet. If you have one (most MSI laptops
from Intel 11th gen on), a ten-minute check helps a lot. Nothing in it writes to the laptop.

1. Start M-Tool. In the reports window, under **Live values (experimental)**, choose **Show live
   values** and answer **Yes**.
2. Open HWiNFO or MSI Center next to it. Do the CPU and GPU temperatures match within about 3 °C,
   and the fan speeds within about 10 %?
3. Switch the performance or fan mode (with MSI Center or the laptop's hotkey) and choose **Refresh**.
   Does the mode's raw value change?
4. Leave the window open for ten minutes, once with MSI Center running too. Does reading stop
   ("Reading stopped")?
5. Put the laptop to sleep and wake it: the values should pause and then go on.
6. Choose **Device report (with WMI2)**, then **Send report (GitHub)**. Attach the zip and write
   under "What does M-Tool show?" which tool you compared with and whether each value matched.
   If you would rather not post publicly, **Send EC data** sends the raw values privately instead.

### More detail: zip and GitHub issue

Use this path to include the ACPI DSDT, or to read the report before you post it.

1. Right-click the M-Tool tray icon and choose **Create device report**. On a laptop with WMI2,
   **Device report (with WMI2)** also records the raw answers of MSI's WMI2 read methods (`Get_*`
   only), after M-Tool explains this and asks. From a terminal: `M-Tool.exe --report` (or
   `--report --wmi2`) as administrator.
2. Choose **Send report (GitHub)** in the same menu. It opens a
   [device report issue](https://github.com/kutaykalay/M-Tool/issues/new?template=device-report.yml)
   with the model and firmware filled in. Attach the zip from `%AppData%\M-Tool\reports`;
   **Open the last report's folder** shows it.

The zip holds `report.txt` (the model, BIOS and EC firmware versions, the MSI WMI classes and the
values M-Tool could read) and `dsdt.aml`, a copy of the laptop's ACPI DSDT table. M-Tool does not
query the serial number or UUID, and it hides your user and computer names in `report.txt`. The
DSDT is firmware code written by MSI, not personal data, but it is a binary file M-Tool does not
filter. With `--wmi2`, `report.txt` also holds MSI's raw WMI2 answers as hex bytes; M-Tool does
not know what every byte means, so it cannot hide anything in them either. Read `report.txt`
before you post it.

### What "Send EC data" sends

Exactly the text shown in the preview is sent, nothing else.

Sent:

- the M-Tool version;
- from SMBIOS: manufacturer, model, board, BIOS version and date, and the CPU;
- the Windows version;
- the MSI WMI interface and the MSI class and property names;
- the EC firmware version and date, the matching record, and the register numbers and values of
  the WMI1 fields M-Tool could read (live sensor values included);
- on a WMI2 laptop, only if you said Yes: the raw `Get_*` answers.

Not sent:

- serial number, UUID and SKU;
- your user name, computer name, domain and profile path: where one appears in a value it becomes
  `<hidden>`, and a look-alike spelling (full-width or accented letters) hides the whole value;
- time stamp and time zone, and the interface language;
- your settings, profile names and log contents;
- the DSDT, error messages and file paths;
- MAC address, disk and network information.

Your IP address reaches Cloudflare, which hosts the server. The server does not store it; it keeps
a hash of it for one minute, only to limit how often data is sent: 3 times a minute per address and
100 records a day in total. Data already received for the same model, firmware and BIOS is not
stored again.

Received data is a hint, not proof. Nothing becomes a model record automatically: every submission
is labelled `unverified`, and writing on a new model needs its owner to test each write step by step
on their laptop. How that works is in [CONTRIBUTING.md](CONTRIBUTING.md).

## Requirements

- Windows 10 or 11, x64
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- Administrator rights (the app asks through UAC)
- [PawnIO](https://pawnio.eu) (`winget install namazso.PawnIO`), only for Cooler Boost and the
  charge limit on the supported P65. Without it M-Tool still opens, with those two hidden; elsewhere
  it reads through MSI's WMI alone

## Download

Get `M-Tool.exe` from the latest release on the
[Releases page](https://github.com/kutaykalay/M-Tool/releases) and compare its SHA-256 with the
one in the release notes:

```
Get-FileHash .\M-Tool.exe -Algorithm SHA256
```

From 0.15.1-beta.1 on, the release exe is built by GitHub Actions from the tagged source. With the
[GitHub CLI](https://cli.github.com) you can check that it came from that build:

```
gh attestation verify .\M-Tool.exe --repo kutaykalay/M-Tool
```

The exe is not signed, so SmartScreen may warn on the first start. Choose **More info → Run
anyway** only if the hash matches.

## Install

1. Put `M-Tool.exe` in a folder and run it. There is no installer, and the exe keeps nothing next to
   itself.
2. The first start is a **dry run**: M-Tool checks and logs your choices but writes nothing. When
   the values look right, close M-Tool, set `"dryRun": false` in
   `%AppData%\M-Tool\settings.json` and start it again.
3. Optional: tray menu → **Start at sign-in**. M-Tool then starts hidden in the tray when you sign
   in, without a UAC prompt, and applies your fan profile and performance mode.

Start at sign-in uses a Task Scheduler task that runs the exe as administrator, from the folder it
was in when you turned the option on. If you move the exe or start another copy, M-Tool shows a
warning; turn the option off and on again from the exe you want to keep. Because the task runs
the exe as administrator, keep it in a folder only administrators can change, such as
`C:\Program Files\M-Tool`: in Downloads or on the desktop, any program you run could swap it.

M-Tool does not touch the EC while the laptop sleeps. A few seconds after waking it applies your
fan profile and performance mode again. The charge limit is not rewritten automatically; the
laptop keeps it across sleep and restarts.

## Uninstall

1. Tray menu → turn off **Start at sign-in**; this removes the task.
2. Tray menu → **Exit**, then delete `M-Tool.exe`.
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
- **Network:** M-Tool goes online in only two places, and only when you ask:
  - **Send EC data** (tray menu), after you have seen exactly what is sent.
  - **Check for updates** (Settings › About): one request for GitHub's list of releases. Nothing about
    you or the laptop is sent. "Check once when M-Tool starts" is off unless you turn it on. M-Tool
    never downloads or replaces itself; a newer version is only named, with a link to its release page.

## Build

```
dotnet build
dotnet test
dotnet publish src/MTool.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=embedded
```

The result is a single `M-Tool.exe` of about 2 MB.

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

[YAMDCC](https://codeberg.org/Sparronator9999/YAMDCC), the Linux
[msi-ec](https://github.com/BeardOverflow/msi-ec) driver and
[GhostDeck](https://github.com/wygodad/ghostdeck) were used as references for the register map,
the other models' records and the WMI2 reading. Only facts were taken, by hand; no code was
copied. See "Data sources" in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). EC access goes
through [PawnIO](https://github.com/namazso/PawnIO).

## License

Copyright (c) 2026 Kutay Kalay. M-Tool is free software: you can redistribute it and/or modify it
under the terms of the [GNU General Public License](LICENSE), version 3 or (at your option) any
later version (GPL-3.0-or-later). The bundled PawnIO module is LGPL-2.1-or-later, see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Not affiliated with or endorsed by MSI. You use M-Tool at your own risk: it writes to your laptop's
embedded controller.
