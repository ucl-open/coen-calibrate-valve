# coen-calibrate-valve

Calibrates a reward valve on a Coen Lab rig: fires known pulse widths on one behavior board line, weighs what each delivers on a serial balance, and writes the curve (pulse width in ms against volume per pulse in µl) where the rig's experiment workflows read it. Built on the open-rigs dispense calibration (`OpenRigs.Core:DispenseCalibration.bonsai`, see open-rigs' `docs/articles/calibration.md`).

Run it from rig-control's Calibrations page. It is an open-rigs project under `ExperimentControl`, so rig-control launches, watches and stops it like any experiment.

## What It Reads

- **The rig's own rig.yml**, `<ConfigRoot>\<MACHINE-NAME>\rig.yml`, the file av-dome reads. Only `behaviorBoard: main:` is read: its `portName`, and `pulseController:` `outputPulseEnable` and `outputs:`. Every other entry is ignored, so one rig.yml serves the experiment and the calibration.
- **`config\valve-calibration.yml`**: the procedure (pulse widths, repeats, priming, settling, fit) and the balance's serial protocol. Committed, the same for every rig.
- **`BalancePort`**, a launch property: the balance's COM port, since the balance is plugged in only to calibrate.

## A Run

1. Put a container on the balance under the spout, plug the balance in, launch with `BalancePort`.
2. **Ready** once the board has answered and the balance has given a reading. `weight_g` shows the balance every second.
3. Set **Line** (1 to 3; it starts at rig.yml's first `CalibratedPulse` line) and **Notes**, press **Go**. The line must be one of rig.yml's `outputs` and listed in `outputPulseEnable`, or the run stops there, before anything fires.
4. The procedure primes the line, weighs a baseline, fires each sample's pulses and weighs each gain (`target` names the line and file).
5. **Finished** once `calibration\<name>.json` is written beside rig.yml, with a dated copy in `calibration\history\`. `points` lists the volume per pulse at each width. A curve that is not monotone is refused and nothing is written.

The artefact's name is the line's own `artefact:` when rig.yml already has it as a `CalibratedPulse`, else `valve-DO<n>.json`. A line still set to `FixedPulse` keeps its fixed width until rig.yml points it at the artefact:

```yaml
DO1: { discriminatorType: CalibratedPulse, artefact: calibration/valve-DO1.json }
```

`calibratedBy` is whoever pressed Go, as rig-control knows them from the Lab PIN.

## Setup

`scripts\deploy.ps1` (or `.bonsai\Setup.ps1`) installs Bonsai and the packages. The lab's packages come from lib-open-rigs (`workflows.txt` there); never bump them here by hand.
