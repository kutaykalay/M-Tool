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
