# coen-calibrate-valve

Calibrates a reward valve on a Coen Lab rig. It fires known pulse widths on one behavior board line, weighs what each delivers, and writes the curve (pulse width in ms against volume per pulse in µl) where the rig's experiment workflows read it. It is built from the open-rigs dispense calibration (`OpenRigs.Core:DispenseCalibration.bonsai`, see open-rigs' `docs/articles/calibration.md`) and the lab's open-rigs packages, with no C# of its own.

Run it from rig-control's Calibrations page. It is an open-rigs project under `ExperimentControl`, so rig-control launches, watches and stops it like any experiment.

## What It Takes

- **Launch properties, from rig-control.** The Calibrations page reads the rig's own rig.yml (`<ConfigRoot>\<MACHINE-NAME>\rig.yml`, the file av-dome reads) and launches with:
  - `PortName`: `behaviorBoard: main: portName`.
  - `OutputPulseEnable`: its `pulseController: outputPulseEnable`, written with commas (`DO1, DO2`).
  - `Channel`: the line's number.
  - `FileName`: the line's artefact name. That is its own `artefact:` when rig.yml already calibrates it, else `valve-DO<n>`.
  - `Notes`.
  
  The page refuses a line that `outputPulseEnable` does not list.
- **`BalancePort`**, asked for on the page: the balance's COM port, since the balance is plugged in only to calibrate; or `manual` for a balance with no data port, read by eye (below).
- **`config\valve-calibration.yml`**: the procedure (pulse widths, repeats, priming, settling, fit) and the balance's serial protocol. Committed, the same for every rig.

## A Balance Read by Eye

With `BalancePort` `manual`, the workflow runs `OpenRigs.Devices:ManualBalance.bonsai` in place of the serial balance. From Go it follows the procedure's bursts by the pulses fired. Once each burst has landed, it asks for a weight in the `weigh` metric: the baseline after priming, then each sample's. Read the balance and give the number in grams with **Enter Weight** (the `Weight` command). The next weight given is that burst's reading. A weight given when none is due, or text that is not a number, is refused in `weigh`, and the run waits. Each weighing waits up to an hour. Leave the container on the balance throughout: each sample is the difference from the one before.

## A Run

1. Put a container on the balance under the spout, plug the balance in, choose the line on the Calibrations page and press Calibrate.
2. **Ready** once the board has answered and, for a serial balance, the balance has given a reading. `target` names the line and file; `weight_g` shows the balance every second.
3. Press **Go**. The procedure primes the line, weighs a baseline, fires each sample's pulses and weighs each gain.
4. **Finished** once `calibration\<FileName>.json` is written beside rig.yml, with a dated copy in `calibration\history\`. `points` lists the volume per pulse at each width. A curve that is not monotone is refused and nothing is written.

A line still set to `FixedPulse` keeps its fixed width until rig.yml points it at the artefact:

```yaml
DO1: { discriminatorType: CalibratedPulse, artefact: calibration/valve-DO1.json }
```

`calibratedBy` is whoever pressed Go, as rig-control knows them from the Lab PIN (OpenRigs.Control `GoByName`).

## Setup

`scripts\deploy.ps1` (or `.bonsai\Setup.ps1`) installs Bonsai and the packages. The lab's packages come from lib-open-rigs (`workflows.txt` there); never bump them here by hand.
