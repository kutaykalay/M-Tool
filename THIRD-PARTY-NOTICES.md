# Third-party notices

## PawnIO LpcACPIEC module

`src/MTool.App/Hardware/Resources/LpcACPIEC.bin` is an unmodified, signed module from
PawnIO.Modules release 0.2.11 (<https://github.com/namazso/PawnIO.Modules/releases/tag/0.2.11>).

- SHA-256: `c38fd116e7aff4d1fdb0a494e296be0a6708e5a22fc72f14587442fb7f8f7906`
- Copyright (C) 2023 namazso
- Licence: GNU Lesser General Public License v2.1 or later
- Source: <https://github.com/namazso/PawnIO.Modules/blob/0.2.11/LpcACPIEC.p>

M-Tool loads this module into the PawnIO driver and communicates with the driver only through
the device I/O control interface. The PawnIO driver itself (GPL-2.0-or-later with an exception
for programs that use only that interface) is not distributed with M-Tool; users install it from
<https://pawnio.eu>.

## NuGet packages

These packages are compiled into the single-file `M-Tool.exe`. All three are licensed under the
MIT License; the copyright lines below are the ones given in each package's metadata.

- **CommunityToolkit.Mvvm** 8.4.2, <https://github.com/CommunityToolkit/dotnet>.
  Copyright (c) .NET Foundation and Contributors.
- **System.Management** 10.0.0, <https://dot.net/>. Copyright (c) Microsoft Corporation.
- **TaskScheduler** 2.12.2, <https://github.com/dahall/taskscheduler>. Copyright (c) 2002-2025
  David Hall.

The MIT License applies to each of the three packages, with its own copyright line above:

```
Permission is hereby granted, free of charge, to any person obtaining a copy of this software
and associated documentation files (the "Software"), to deal in the Software without
restriction, including without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING
BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## Data sources

The device records in `src/MTool.Core/Devices/`, the WMI2 report and the live WMI2 values are
M-Tool's own files in M-Tool's own format. Facts about MSI embedded controllers (register
addresses, bit meanings, mode values, firmware name prefixes, WMI method names and packet
positions) were taken by hand from the projects below. No code, files, file structure, comments
or descriptions were copied.

- **YAMDCC** by Sparronator9999, <https://codeberg.org/Sparronator9999/YAMDCC>, commit
  `da7d9d8068625217605f2dd1f5057b7b55974700`. Licence: GNU General Public License v3.0 or later.
  Used for: fan table addresses (cross-check), the WMI2 method names, packet positions and the RPM
  constant used to read the opt-in WMI2 report.
- **msi-ec** (Linux driver), <https://github.com/BeardOverflow/msi-ec>, commit
  `d7fbbd88e6831e56801b860e46475cbf8ddbc7c1`. Licence: GNU General Public License v2.0 or later.
  Used for: the firmware families and features of the older WMI1 record (`CONF_G1_10`), and a
  cross-check of the Cooler Boost, charge limit and mode registers.
- **GhostDeck** by wygodad, <https://github.com/wygodad/ghostdeck>, commit
  `7f3a67040da2626a60231f2b31a8f531ecf1112a` (v1.38.0). Licence: GNU General Public License v3.0.
  Used for: the `Get_Data` packet layout and the two tach formats, from its tested entries only.

The P65 Creator 9SE record was measured on that laptop; the references only confirmed it. Each
record lists its own sources in its `sources` field.
