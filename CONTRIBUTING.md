# Contributing

## Device reports

The most useful help is a device report from an MSI laptop M-Tool does not support yet. See
"How to help" in the [README](README.md#how-to-help). A report only reads; it never writes to the
embedded controller (EC).

## Write verification with a volunteer

M-Tool writes to the EC only on a laptop whose record is `writeVerified`. Today that is one laptop
and one firmware: the MSI P65 Creator 9SE with `16Q4EMS2.107`. A new model gets there in four
stages, and only with an owner who agrees to run the tests on their own laptop.

A wrong EC write can set a fan to the wrong speed or stop it. Every stage below is done step by
step: the owner runs one step, sends the result, and the next step starts only after it was
checked. The owner runs the tests at their own risk and can stop at any step.

### 1. Draft record

1. The owner sends a device report (`M-Tool.exe --report`).
2. A `draft` record for the model is added under `src/MTool.Core/Devices`. Its `sources` field
   says where each fact came from.
3. A draft record is read-only. M-Tool shows its values with an "experimental" band, never opens
   the raw port and never writes.

### 2. Read verification (`readVerified`)

The record becomes `readVerified` when all of these hold:

- The MSI WMI class and index of every field were checked against the laptop's DSDT.
- The values M-Tool reads are plausible: temperatures, fan speeds, the fan table and the modes.
- The owner compared them with MSI Center or HWiNFO and confirmed them, including which
  performance modes MSI Center offers and whether the laptop has a GPU fan.

### 3. Write verification

The release exe cannot write on any model but the verified ones, and it has no switch for it.
Write tests run on a **separate verification build** made for one laptop:

- It can write only on that one record and firmware; everything else works as in the release.
- It is built from a branch, given only to that owner with its SHA-256, and never published as a
  release.
- It starts in dry-run mode, like every M-Tool install. The owner turns dry run off only right
  before step 1.
- M-Tool's write rules are written for the P65 today. The first other model also needs code
  changes in the write path; they are reviewed and tested before this stage starts.

Before the first step:

- Close MSI Center and any other fan or EC tool, and stop their services.
- Keep the laptop on AC power.
- Run `M-Tool.exe --dump` and send the file from `%AppData%\M-Tool\dumps`.
- Some values, such as the fan curve's down offsets on WMI1 laptops, cannot be read through WMI.
  If the record needs them, the verification build reads them once through the raw port. This
  happens only after the owner agrees to that one read.

The steps, in this order. After each one the owner sends the log from `%AppData%\M-Tool\logs` and
a new dump:

1. **Unchanged write:** write the value a register already holds (for example the current charge
   limit) and read it back. This tests the write protocol without changing anything.
2. **Single changes,** one at a time, each followed by a return to the old value:
   - charge limit one step down and back;
   - Cooler Boost on and off, checking that the other bits of its register stay as they were;
   - each performance mode, then back to the starting one.
3. **Unchanged fan table:** write the factory fan table the laptop already has.
4. **Changed fan table:** apply the Cool profile, put load on the CPU and check that the fans
   follow the curve. Then return to Default.
5. **Fan mode:** with a custom table written, watch how the EC behaves in automatic and in
   advanced fan mode, then return to automatic.
6. **Persistence:** with Cool applied, restart, sleep and fully shut down the laptop. Read the fan
   table after each one.
7. **Restore:** run `M-Tool.exe --restore --confirm` and check that the factory table is written
   and verified.

Stop at once and report if a read-back does not match, if M-Tool locks writing, or if a fan
behaves in a way the step did not ask for. If a fan stops under load or temperatures keep
climbing, shut the laptop down.

### 4. Write verified (`writeVerified`)

When every step passed:

- The record becomes `writeVerified` and lists the exact firmware versions that were tested.
- The firmware is also added to the allow list in the code. Writing needs both keys, so a change
  to the JSON alone can never turn writing on; the code change goes through review.
- The logs and dumps stay attached to the issue as the record's evidence.
- The model ships as writable in a normal release. Any other firmware of the same model stays
  read-only until it is verified too.

## Code

Open an issue before a larger change. YAMDCC and msi-ec are used as references for facts only;
do not copy code, files or comments from them (see "Data sources" in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)). Automated tests never touch a real EC; they use
the simulated EC in the test project.
