using Bonsai;
using OpenRigs.Core;
using OpenRigs.Core.Calibration;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace UclOpenCoenCalibrateValve
{
    /// <summary>
    /// Balance readings typed by a person, for a balance the computer cannot read: BalancePort manual.
    /// </summary>
    public static class ManualBalanceMode
    {
        /// <summary>What BalancePort holds for a balance read by eye.</summary>
        public const string Word = "manual";

        /// <summary>A person has the hour to read the balance and type each weight.</summary>
        public const double WeighTimeoutS = 3600;

        /// <summary>Whether the BalancePort value means a balance read by eye.</summary>
        public static bool IsManual(string port)
        {
            return string.Equals((port ?? "").Trim(), Word, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The calibration settings for the balance BalancePort names: as committed for a serial balance;
    /// for a manual one, each weighing takes the one weight typed and waits up to an hour for it.
    /// </summary>
    [Combinator]
    [Description("The calibration settings for the balance BalancePort names. A COM port keeps them as committed. 'manual' makes each weighing take the one weight a person types (SettleWindow 1) and wait up to an hour for it. Empty stops the workflow, naming both choices. Input is a Tuple of the settings and BalancePort.")]
    [WorkflowElementCategory(ElementCategory.Transform)]
    public class BalanceSettings
    {
        /// <summary>Adjusts each settings for its balance.</summary>
        public IObservable<DispenseCalibration> Process(IObservable<Tuple<DispenseCalibration, string>> source)
        {
            return source.Select(input =>
            {
                var port = (input.Item2 ?? "").Trim();
                if (port.Length == 0)
                {
                    throw new InvalidOperationException(
                        "BalancePort is empty: give the balance's COM port, such as COM5, or manual to type each weight.");
                }

                var settings = input.Item1;
                if (ManualBalanceMode.IsManual(port))
                {
                    settings.Procedure.SettleWindow = 1;
                    settings.Procedure.WeighTimeoutS = ManualBalanceMode.WeighTimeoutS;
                }

                return settings;
            });
        }
    }

    /// <summary>
    /// Passes BalancePort through only for one kind of balance, so the serial and the manual balance
    /// each start only when chosen.
    /// </summary>
    [Combinator]
    [Description("Passes BalancePort through only when it names the kind of balance Manual says: a COM port when Manual is false, 'manual' when true.")]
    [WorkflowElementCategory(ElementCategory.Condition)]
    public class BalanceKind
    {
        /// <summary>Gets or sets whether to pass a manual balance rather than a serial one.</summary>
        [Description("True to pass 'manual'; false to pass a COM port.")]
        public bool Manual { get; set; }

        /// <summary>Filters each BalancePort.</summary>
        public IObservable<string> Process(IObservable<string> source)
        {
            return source.Where(port => ManualBalanceMode.IsManual(port) == Manual);
        }
    }

    /// <summary>
    /// What the manual balance says and takes: the line telling the person what to do, and the
    /// weight they typed when one was due.
    /// </summary>
    public class ManualWeighingEvent
    {
        /// <summary>What to do now, and what became of the last weight typed.</summary>
        public string Prompt { get; set; }

        /// <summary>The weight taken, or null.</summary>
        public WeightMeasurement? Reading { get; set; }
    }

    /// <summary>
    /// Follows the dispense procedure's bursts by the pulses it fires and asks for a weight after each:
    /// the baseline after priming, then one per sample. A weight typed while one is due is taken as
    /// that burst's reading; one typed at any other time is refused, saying why.
    /// </summary>
    [Combinator]
    [Description("A balance read by eye. Follows the procedure's bursts by the pulses fired (first input: the settings at Go; second: TriggerCalibratedPulse) and, once each burst has landed, asks for a weight in its Prompt: the baseline after priming, then each sample's. The next weight typed (third input: the Weight command's text, in grams) is that burst's reading. A weight typed when none is due, or text that is not a weight, is refused in the Prompt.")]
    [WorkflowElementCategory(ElementCategory.Combinator)]
    public class ManualWeighing
    {
        struct Burst
        {
            public string Name;
            public int Count;
            public int Size;
            public int Interval;
        }

        /// <summary>Asks for and takes each weight of the runs the settings start.</summary>
        public IObservable<ManualWeighingEvent> Process(
            IObservable<DispenseCalibration> start,
            IObservable<Tuple<int, int>> pulses,
            IObservable<string> typed)
        {
            return Observable.Create<ManualWeighingEvent>(observer =>
            {
                var gate = new object();
                List<Burst> bursts = null;
                int index = 0, fired = 0;
                bool due = false;
                string note = "";
                var timer = new SerialDisposable();
                Action<string> say = prompt =>
                {
                    observer.OnNext(new ManualWeighingEvent { Prompt = prompt + (note.Length > 0 ? " " + note : "") });
                };
                Func<string> dueText = () => index == 0
                    ? string.Format("Weigh Now: the baseline, the container's weight {0}.",
                        bursts[0].Count > 0 ? "after " + bursts[0].Count + " priming pulses" : "before any pulse")
                    : string.Format("Weigh Now: {0}, after {1} pulses of {2} ms.", bursts[index].Name, bursts[index].Count, bursts[index].Size);
                Action land = () =>
                {
                    // PulseBurst completes one period after its last pulse, so the drop has landed
                    // before the weighing starts; a little after that, the person is asked.
                    var burst = bursts[index];
                    var wait = TimeSpan.FromMilliseconds(burst.Count > 0 ? burst.Size + burst.Interval + 250 : 250);
                    timer.Disposable = Observable.Timer(wait).Subscribe(_ =>
                    {
                        lock (gate)
                        {
                            due = true;
                            say(dueText());
                        }
                    });
                };

                say("Manual balance: after Go, each weight is asked for here; give it with Enter Weight, in grams.");
                var subscriptions = new CompositeDisposable(timer);
                subscriptions.Add(start.Subscribe(settings =>
                {
                    lock (gate)
                    {
                        var p = settings.Procedure;
                        int interval = p.Samples.Max(s => s.InterPulseIntervalMs);
                        bursts = new List<Burst>();
                        bursts.Add(new Burst
                        {
                            Name = "priming",
                            Count = p.PrimingPulses,
                            Size = p.PrimingSize ?? p.Samples.Max(s => s.Size),
                            Interval = interval
                        });
                        for (int i = 0; i < p.Samples.Count; i++)
                        {
                            bursts.Add(new Burst
                            {
                                Name = string.Format("sample {0} of {1}", i + 1, p.Samples.Count),
                                Count = p.Samples[i].Repeats,
                                Size = p.Samples[i].Size,
                                Interval = p.Samples[i].InterPulseIntervalMs
                            });
                        }

                        index = fired = 0;
                        due = false;
                        note = "";
                        if (bursts[0].Count > 0) say("Priming the line: wait.");
                        else land();
                    }
                }, observer.OnError));
                subscriptions.Add(pulses.Subscribe(pulse =>
                {
                    lock (gate)
                    {
                        if (pulse == null || bursts == null || due || index >= bursts.Count) return;
                        fired++;
                        var burst = bursts[index];
                        say(string.Format("Firing {0}: pulse {1} of {2}, {3} ms. Wait.", burst.Name, fired, burst.Count, burst.Size));
                        if (fired >= burst.Count) land();
                    }
                }, observer.OnError));
                subscriptions.Add(typed.Subscribe(text =>
                {
                    lock (gate)
                    {
                        var entry = (text ?? "").Trim();
                        var number = entry.EndsWith("g", StringComparison.OrdinalIgnoreCase) ? entry.Substring(0, entry.Length - 1).Trim() : entry;
                        double grams;
                        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out grams) || grams < 0)
                        {
                            note = string.Format("('{0}' is not a weight in grams: type it again.)", entry);
                            say(due ? dueText() : "No weight is due yet: wait.");
                            return;
                        }

                        if (!due)
                        {
                            note = string.Format("({0} g refused: no weight was due. Wait to be asked.)", number);
                            say(bursts == null ? "No weight is due before Go." : "No weight is due yet: wait.");
                            return;
                        }

                        var name = index == 0 ? "the baseline" : bursts[index].Name;
                        var reading = new WeightMeasurement(0, (float)grams, 1);
                        due = false;
                        index++;
                        fired = 0;
                        note = string.Format("({0} g taken for {1}.)", number, name);
                        observer.OnNext(new ManualWeighingEvent
                        {
                            Prompt = (index < bursts.Count ? string.Format("Firing {0}: wait.", bursts[index].Name) : "Every weight taken: writing the calibration.") + " " + note,
                            Reading = reading
                        });
                    }
                }, observer.OnError));
                return subscriptions;
            });
        }
    }

    /// <summary>
    /// The weights a manual balance took, as the readings a serial balance would publish.
    /// </summary>
    [Combinator]
    [Description("The weights a manual balance took, as WeightMeasurement readings for BalanceReading.")]
    [WorkflowElementCategory(ElementCategory.Transform)]
    public class ManualReadings
    {
        /// <summary>Passes each event's reading, when it has one.</summary>
        public IObservable<WeightMeasurement> Process(IObservable<ManualWeighingEvent> source)
        {
            return source.Where(e => e.Reading.HasValue).Select(e => e.Reading.Value);
        }
    }
}
