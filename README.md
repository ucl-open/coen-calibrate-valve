# coen-calibrate-valve

Calibrates a reward valve on a Coen Lab rig. It fires known pulse widths on one behavior board line, weighs what each delivers, and writes the curve (pulse width in ms against volume per pulse in µl) where the rig's experiment workflows read it. It is built from the open-rigs dispense calibration (`OpenRigs.Core:DispenseCalibration.bonsai`, see open-rigs' `docs/articles/calibration.md`) and the lab's open-rigs packages, with no C# of its own.

Run it from rig-control's Calibrations page, the only place it is offered: it is not in the Setup page's Workflow list. It is an open-rigs project under `ExperimentControl`, which the page launches, watches and stops.

## What It Takes

- **Launch properties, from rig-control.** The Calibrations page reads the rig's own rig.yml (`<ConfigRoot>\<MACHINE-NAME>\rig.yml`, the file av-dome reads) and launches with:
  - `PortName`: `behaviorBoard: main: portName`.
  - `OutputPulseEnable`: its `pulseController: outputPulseEnable`, written with commas (`DO1, DO2`).
  - `Channel`: the line's number.
  - `FileName`: the line's artefact name. That is its own `artefact:` when rig.yml already calibrates it, else `valve-DO<n>`.
  - `CameraSerialNumber`, `CameraExposureTime`: `camera: body:`'s, and `CameraFrequency`: `cameraTriggerController: trigger0Frequency`. The body camera runs on camera trigger 0 (DO0) and is streamed by `OpenRigs.LiveView` (5558) for the Calibrations page to show live; nothing is recorded. Trigger 1 stays off, so DO1 stays a valve line.
  
  `Notes` (free text kept in the curve) is taken only from a run in the Bonsai editor; the page sends none. The page refuses a line that `outputPulseEnable` does not list, and does not show one rig.yml gives another job (the domes' DO3, the Zapit TTL).
- **`config\valve-calibration.yml`**: the procedure (pulse widths, repeats, priming, fit, and how long each weighing waits). Committed, the same for every rig. Five widths, each measured 3 times in rounds of all five in a shuffled order (fixed in the file), each burst weighed once: 15 weighings. Per burst 20 ms x 167, 30 ms x 111, 40 ms x 83, 60 ms x 56 and 80 ms x 42 pulses, so each width delivers in all about what 500 pulses at 20 ms do (volume taken as proportional to width). Pulses are 63 ms apart, 1.25 times the 50 ms av-dome leaves between a reward's pulses (rig.yml `interPulseInterval`).

## The Balance, Read by Eye

The balance is read by eye: the workflow runs `OpenRigs.Devices:ManualBalance.bonsai`, and nothing is plugged into the computer. From Start it follows the procedure's bursts by the pulses fired. Once each burst has landed, it asks for a weight in the `weigh` metric: the baseline after priming, then each sample's. The Calibrations page shows that prompt in a band, which turns amber with its box highlighted while a weight is due: read the balance, type the number in grams and press Enter (the `Weight` command). The next weight given is that burst's reading. A weight given when none is due, or text that is not a number, is refused in `weigh`, and the run waits. Each weighing waits up to an hour. Leave the container on the balance throughout: each sample is the difference from the one before.

## A Run

1. On the Calibrations page, press the line's **Go** and give your Lab PIN.
2. **Ready** once the board has answered. Put a container on the balance under the spout.
3. Press **Start**. The procedure primes the line, asks for the baseline weight, fires each sample's pulses and asks for each weight.
4. **Finished** once `calibration\<FileName>.json` is written beside rig.yml, with a dated copy in `calibration\history\`. The page lists the volume per pulse at each width (`points`) and shows the new curve. A curve that is not monotone is refused and nothing is written.

A line still set to `FixedPulse` keeps its fixed width until rig.yml points it at the artefact. The Calibrations page says so, and its Use Calibration button makes that change, once asked and with a Lab PIN, to that line alone (the rest of rig.yml, comments included, kept as it was, the old file in `_backups`):

```yaml
DO1: { discriminatorType: CalibratedPulse, artefact: calibration/valve-DO1.json }
```

`calibratedBy` is whoever pressed Start (the workflow's Go), as rig-control knows them from the Lab PIN (OpenRigs.Control `GoByName`).

## Setup

`scripts\deploy.ps1` (or `.bonsai\Setup.ps1`) installs Bonsai and the packages. The lab's packages come from lib-open-rigs (`workflows.txt` there); never bump them here by hand.
