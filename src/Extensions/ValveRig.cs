using Bonsai;
using Harp.Behavior;
using OpenRigs.Control;
using OpenRigs.Core.Calibration;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using YamlDotNet.RepresentationModel;

namespace UclOpenCoenCalibrateValve
{
    /// <summary>
    /// One pulsed output line of the behavior board, as the rig's rig.yml declares it.
    /// </summary>
    public class ValveLine
    {
        /// <summary>The line's name in rig.yml, such as DO1.</summary>
        public string Name { get; set; }

        /// <summary>The line's number, 1 to 3, as PulseController's TriggerCalibratedPulse takes it.</summary>
        public int Channel { get; set; }

        /// <summary>FixedPulse or CalibratedPulse, the line's discriminatorType.</summary>
        public string Mode { get; set; }

        /// <summary>A CalibratedPulse line's artefact path relative to the rig configuration directory; empty otherwise.</summary>
        public string Artefact { get; set; }

        /// <summary>Whether outputPulseEnable lists the line, so the board pulses it.</summary>
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// What the valve calibration needs from rig.yml: the behavior board's port and pulsed output lines.
    /// </summary>
    public class ValveRig
    {
        /// <summary>The behavior board's serial port.</summary>
        public string PortName { get; set; }

        /// <summary>The board's USB serial, as rig.yml records it.</summary>
        public string UsbSerial { get; set; }

        /// <summary>The board's outputPulseEnable, as rig.yml gives it.</summary>
        public string OutputPulseEnable { get; set; }

        /// <summary>The lines outputPulseEnable lists, as the PulseController's OutputPulseEnable takes them.</summary>
        public DigitalOutputs PulseEnable { get; set; }

        /// <summary>Every pulsed output line rig.yml declares, in order.</summary>
        public List<ValveLine> Lines { get; set; }
    }

    /// <summary>
    /// Where one run of the calibration fires and what it writes.
    /// </summary>
    public class CalibrationTarget
    {
        /// <summary>The line's name, such as DO1.</summary>
        public string Line { get; set; }

        /// <summary>The line's number, 1 to 3.</summary>
        public int Channel { get; set; }

        /// <summary>The artefact's file name without extension, written to calibration\&lt;FileName&gt;.json.</summary>
        public string FileName { get; set; }

        /// <summary>Who pressed Go, written into the artefact's provenance.</summary>
        public string CalibratedBy { get; set; }

        /// <summary>The line's mode in rig.yml when the run started.</summary>
        public string Mode { get; set; }

        /// <summary>Free text written into the artefact's provenance.</summary>
        public string Notes { get; set; }
    }

    /// <summary>
    /// Reads the behavior board's port and pulsed output lines from rig.yml's text, ignoring every
    /// other entry, so the calibration reads the same rig.yml as the rig's experiment workflows
    /// whatever else that file holds.
    /// </summary>
    [Combinator]
    [Description("Reads the behavior board's port and pulsed output lines (pulseController: outputs:) from rig.yml's text, ignoring every other entry. Fails if rig.yml has no behaviorBoard entry by BoardName, or that entry has no portName.")]
    [WorkflowElementCategory(ElementCategory.Transform)]
    public class ReadValveRig
    {
        private string boardName = "main";

        /// <summary>Gets or sets the behavior board's entry name under behaviorBoard: in rig.yml.</summary>
        [Description("The behavior board's entry name under behaviorBoard: in rig.yml.")]
        public string BoardName
        {
            get { return boardName; }
            set { boardName = value; }
        }

        /// <summary>Parses each rig.yml text.</summary>
        public IObservable<ValveRig> Process(IObservable<string> source)
        {
            return source.Select(text => Parse(text, boardName));
        }

        internal static ValveRig Parse(string text, string boardName)
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            var root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
            var boards = Child(root, "behaviorBoard");
            var board = Child(boards, boardName);
            if (board == null)
            {
                throw new InvalidOperationException(string.Format(
                    "rig.yml has no behaviorBoard: {0}: entry, so there is no board to fire the valve. Its behaviorBoard entries: {1}.",
                    boardName, Names(boards)));
            }

            var rig = new ValveRig();
            rig.PortName = Scalar(board, "portName");
            rig.UsbSerial = Scalar(board, "usbSerial");
            if (string.IsNullOrEmpty(rig.PortName))
            {
                throw new InvalidOperationException(string.Format("rig.yml's behaviorBoard: {0}: has no portName.", boardName));
            }

            var pulseController = Child(board, "pulseController");
            rig.OutputPulseEnable = Scalar(pulseController, "outputPulseEnable");
            var enabled = new HashSet<string>(
                (rig.OutputPulseEnable ?? "").Split(new[] { ',', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);
            foreach (var name in enabled)
            {
                DigitalOutputs output;
                if (!Enum.TryParse(name, true, out output))
                {
                    throw new InvalidOperationException(string.Format(
                        "rig.yml's outputPulseEnable ('{0}') names {1}, which is not a behavior board output.",
                        rig.OutputPulseEnable, name));
                }

                rig.PulseEnable |= output;
            }

            rig.Lines = new List<ValveLine>();
            var outputs = Child(pulseController, "outputs");
            if (outputs != null)
            {
                foreach (var entry in outputs.Children)
                {
                    var name = ((YamlScalarNode)entry.Key).Value;
                    int channel;
                    if (!name.StartsWith("DO", StringComparison.OrdinalIgnoreCase) ||
                        !int.TryParse(name.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out channel))
                    {
                        continue;
                    }

                    var line = entry.Value as YamlMappingNode;
                    rig.Lines.Add(new ValveLine
                    {
                        Name = name,
                        Channel = channel,
                        Mode = Scalar(line, "discriminatorType") ?? "",
                        Artefact = Scalar(line, "artefact") ?? "",
                        Enabled = enabled.Contains(name)
                    });
                }
            }

            return rig;
        }

        static YamlMappingNode Child(YamlMappingNode node, string key)
        {
            if (node == null) return null;
            YamlNode child;
            return node.Children.TryGetValue(new YamlScalarNode(key), out child) ? child as YamlMappingNode : null;
        }

        // An unset value is empty, ~ or null in lower case exactly, lib-open-rigs' RigDevices.Value rule.
        static string Scalar(YamlMappingNode node, string key)
        {
            if (node == null) return null;
            YamlNode child;
            if (!node.Children.TryGetValue(new YamlScalarNode(key), out child)) return null;
            var scalar = child as YamlScalarNode;
            if (scalar == null || scalar.Value == null) return null;
            var value = scalar.Value.Trim();
            return value.Length == 0 || value == "~" || value == "null" ? null : value;
        }

        static string Names(YamlMappingNode node)
        {
            if (node == null || node.Children.Count == 0) return "none";
            return string.Join(", ", node.Children.Keys.Select(k => ((YamlScalarNode)k).Value).ToArray());
        }
    }

    /// <summary>
    /// The line a run starts on when nobody chooses one: the first CalibratedPulse line rig.yml
    /// enables, else its first enabled line.
    /// </summary>
    [Combinator]
    [Description("The line to offer first: the first CalibratedPulse line rig.yml enables, else its first enabled line, else 1.")]
    [WorkflowElementCategory(ElementCategory.Transform)]
    public class DefaultValveLine
    {
        /// <summary>Picks the default line of each rig.</summary>
        public IObservable<int> Process(IObservable<ValveRig> source)
        {
            return source.Select(rig =>
            {
                var line = rig.Lines.FirstOrDefault(l => l.Enabled && l.Mode == "CalibratedPulse") ??
                           rig.Lines.FirstOrDefault(l => l.Enabled);
                return line != null ? line.Channel : 1;
            });
        }
    }

    /// <summary>
    /// Turns the Line parameter, as it stood at Go, into the run's target, checked against rig.yml.
    /// </summary>
    [Combinator]
    [Description("Turns the Line parameter (1 to 3, or DO1 to DO3) into the run's target, checked against rig.yml: the line must be one of its pulseController outputs and listed in outputPulseEnable. A CalibratedPulse line writes the artefact rig.yml names, which must be in calibration\\; any other line writes calibration\\valve-DO<n>.json. Input is a Tuple of the ValveRig, the Line parameter, who pressed Go and the Notes parameter.")]
    [WorkflowElementCategory(ElementCategory.Transform)]
    public class SelectValveLine
    {
        /// <summary>Checks each choice against its rig.</summary>
        public IObservable<CalibrationTarget> Process(IObservable<Tuple<ValveRig, string, string, string>> source)
        {
            return source.Select(input => Select(input.Item1, input.Item2, input.Item3, input.Item4));
        }

        internal static CalibrationTarget Select(ValveRig rig, string parameter, string calibratedBy, string notes)
        {
            var text = (parameter ?? "").Trim();
            if (text.StartsWith("DO", StringComparison.OrdinalIgnoreCase)) text = text.Substring(2);
            int channel;
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out channel))
            {
                throw new InvalidOperationException(string.Format("Line '{0}' is not a behavior board line: give 1 to 3.", parameter));
            }

            var line = rig.Lines.FirstOrDefault(l => l.Channel == channel);
            var declared = string.Join(", ", rig.Lines.Select(l => l.Name + " " + l.Mode).ToArray());
            if (line == null)
            {
                throw new InvalidOperationException(string.Format(
                    "rig.yml declares no DO{0} under behaviorBoard: pulseController: outputs:. It declares: {1}.",
                    channel, declared.Length > 0 ? declared : "none"));
            }

            if (!line.Enabled)
            {
                throw new InvalidOperationException(string.Format(
                    "rig.yml's outputPulseEnable ('{0}') does not list {1}, so the board would not pulse it.",
                    rig.OutputPulseEnable, line.Name));
            }

            string fileName;
            if (line.Mode == "CalibratedPulse" && line.Artefact.Length > 0)
            {
                var artefact = line.Artefact.Replace('\\', '/');
                if (!artefact.StartsWith("calibration/", StringComparison.OrdinalIgnoreCase) ||
                    !artefact.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                    artefact.IndexOf('/', "calibration/".Length) >= 0)
                {
                    throw new InvalidOperationException(string.Format(
                        "{0}'s artefact is '{1}'; the calibration writes only calibration/<name>.json.",
                        line.Name, line.Artefact));
                }

                fileName = artefact.Substring("calibration/".Length, artefact.Length - "calibration/".Length - ".json".Length);
            }
            else
            {
                fileName = "valve-" + line.Name.ToUpperInvariant();
            }

            return new CalibrationTarget
            {
                Line = line.Name,
                Channel = channel,
                FileName = fileName,
                CalibratedBy = string.IsNullOrEmpty(calibratedBy) ? "unknown" : calibratedBy,
                Mode = line.Mode,
                Notes = notes ?? ""
            };
        }
    }

    /// <summary>
    /// Who pressed Go, as the controller vouched for them (rig-control: from the Lab PIN or the
    /// launch's control token), else who launched the run.
    /// </summary>
    [Combinator]
    [Description("Who pressed Go, from the controller (rig-control fills it in from the Lab PIN), else who launched the run, else empty. Input is the Go event.")]
    [WorkflowElementCategory(ElementCategory.Transform)]
    public class GoByName
    {
        /// <summary>Names the person behind each Go event.</summary>
        public IObservable<string> Process(IObservable<ControlEvent> source)
        {
            return source.Select(go =>
            {
                var state = ControlState.Current;
                if (state == null) return "";
                var person = state.GoBy ?? state.LaunchedBy;
                if (person == null) return "";
                return !string.IsNullOrEmpty(person.Name) ? person.Name : (person.Id ?? "");
            });
        }
    }

    /// <summary>
    /// One line summing up a calibration curve for the controller, such as
    /// "10 ms 0.62 ul, 20 ms 1.19 ul".
    /// </summary>
    [Combinator]
    [Description("One line summing up a calibration curve's points for the controller, such as '10 ms 0.62 ul, 20 ms 1.19 ul'.")]
    [WorkflowElementCategory(ElementCategory.Transform)]
    public class DescribeCurve
    {
        /// <summary>Sums up each curve.</summary>
        public IObservable<string> Process(IObservable<CalibrationCurve> source)
        {
            return source.Select(curve => string.Join(", ", curve.Points.Select(p => string.Format(
                CultureInfo.InvariantCulture, "{0:0.##} {1} {2:0.000} {3}", p.X, curve.XUnit, p.Y, curve.YUnit)).ToArray()));
        }
    }
}
