# msiec-import

Writes `src/MTool.Core/Devices/msi-wmi2-firmware.json`, the list of WMI2 firmware M-Tool knows,
from the Linux [msi-ec](https://github.com/BeardOverflow/msi-ec) driver's `msi-ec.c`. It is a .NET 10
file-based app. It is not part of the solution, the build or CI.

## Run

Keep the msi-ec checkout outside this repository, then run this from the repository root:

```
dotnet run tools/msiec-import/msiec-import.cs -- <path to msi-ec.c> <40-character commit sha> src/MTool.Core/Devices/msi-wmi2-firmware.json
```

The commit is written into the file's `sources`. After a run, review the diff, then update the
counts and the firmware hash in `tests/MTool.Tests/Device/Config/Wmi2RecordTests.cs`, and the msi-ec commit in
THIRD-PARTY-NOTICES.md ("Data sources") if it changed.

## What it takes

- The firmware strings in the `ALLOWED_FW_G2_*` lists of the `CONF_G2_*` configs, as `exact`.
  `families` is the first 10 characters of each.

That is all. Comments, model names, code and the configs' other fields are not carried over.

## What it checks, and where it stops

The import stops without writing anything when:

- A G2 config states the charge limit address, the Cooler Boost address and bit, the shift mode or
  fan mode address and values, or the Super Battery address and mask differently from M-Tool's
  WMI2 map (`Wmi2WriteCeiling`). Configs that disagree are never merged; splitting them is a
  separate decision.
- A `CONF_G2_*` name appears but its declaration is not recognised, a referenced
  `ALLOWED_FW_G2_*` list is missing or empty, or a list contains an escape sequence.
- A firmware is listed twice.
- M-Tool's own rules reject the result (`Wmi2FirmwareList.Load`). For example, a string that is not
  12 characters, or a firmware or family that a device record already claims, such as the P65.
- A family in `familyOverrides` is no longer in the list.

## What it keeps

`familyOverrides` and every source line that does not start with `msi-ec ` are written by hand.
They come from other references, such as GhostDeck's records of what MSI Center writes. The import
keeps them as they are. The code allows one exception byte per mode (`Wmi2WriteCeiling.PerformanceExceptions`),
and `Wmi2RecordTests` pins the overrides, so adding one is a reviewed change.

## Note

- M-Tool.Core embeds the output file, so the first run needs an existing, valid file to build
  against.
- The parser does not evaluate the preprocessor: a config inside `#if 0` counts as live. msi-ec has
  none today.
- The script does not check that the file matches the commit you pass. Run it on a checkout of
  that commit, and review the JSON diff; `Wmi2RecordTests` pins a hash of the firmware strings.
