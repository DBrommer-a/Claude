using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.Controls;

namespace Frostline.Controls
{
    /// <summary>
    /// Führt Schritt für Schritt durch die Sender-Kalibrierung. Welche Achse welcher Stick ist
    /// und in welche Richtung sie läuft, erkennt der Assistent daran, was sich bewegt. Damit
    /// ist die Kanalreihenfolge (AETR, TAER, ...) egal und nichts muss von Hand invertiert werden.
    /// </summary>
    public sealed class CalibrationWizard
    {
        public enum Step
        {
            Inactive,
            Rest,
            ThrottleUp,
            YawRight, YawLeft,
            PitchForward, PitchBack,
            RollRight, RollLeft,
            ArmOff, ArmOn,
            ModeAcro, ModeAngle, ModeHorizon,
            Done,
        }

        const float MoveThreshold = 0.25f;
        const float StableBand = 0.03f;
        const float HoldSeconds = 0.6f;

        readonly StickInput stick;
        readonly HashSet<int> used = new HashSet<int>();
        InputProfile draft;
        float[] baseline;
        int candidate = -1;
        int activeIndex = -1;
        float holdValue;
        float holdTimer;

        public Step Current { get; private set; }
        public bool Active { get { return Current != Step.Inactive; } }
        /// <summary>0..1: wie lange der aktuelle Ausschlag schon ruhig gehalten wird.</summary>
        public float HoldProgress { get { return Mathf.Clamp01(holdTimer / HoldSeconds); } }

        public CalibrationWizard(StickInput stickInput)
        {
            stick = stickInput;
        }

        public void Begin()
        {
            if (stick.ActiveSource != StickInput.Source.Transmitter) return;
            draft = new InputProfile();
            if (stick.Profile != null) draft.deadband = stick.Profile.deadband;
            used.Clear();
            stick.Suspended = true;
            Go(Step.Rest);
        }

        public void Cancel()
        {
            stick.Suspended = false;
            Current = Step.Inactive;
        }

        /// <summary>Text für den aktuellen Schritt.</summary>
        public string Prompt
        {
            get
            {
                switch (Current)
                {
                    case Step.Rest: return "Beide Sticks loslassen (Mitte), Gas ganz nach unten, Arm-Schalter aus. Dann „Weiter“.";
                    case Step.ThrottleUp: return "Gas ganz nach OBEN schieben und halten.";
                    case Step.YawRight: return "Gier-Stick (Drehen) ganz nach RECHTS und halten.";
                    case Step.YawLeft: return "Gier-Stick ganz nach LINKS und halten.";
                    case Step.PitchForward: return "Nick-Stick ganz nach VORN (von dir weg) und halten.";
                    case Step.PitchBack: return "Nick-Stick ganz nach HINTEN (zu dir) und halten.";
                    case Step.RollRight: return "Roll-Stick ganz nach RECHTS und halten.";
                    case Step.RollLeft: return "Roll-Stick ganz nach LINKS und halten.";
                    case Step.ArmOff: return "Sticks loslassen. Arm-Schalter auf AUS. Dann „Weiter“ – oder „Überspringen“ (dann schärft die Leertaste).";
                    case Step.ArmOn: return "Arm-Schalter jetzt auf AN stellen.";
                    case Step.ModeAcro: return "Modus-Schalter auf die Stellung für ACRO. Dann „Weiter“ – oder „Überspringen“ (dann wechselt Taste M).";
                    case Step.ModeAngle: return "Modus-Schalter auf die Stellung für ANGLE stellen.";
                    case Step.ModeHorizon: return "Dritte Stellung für HORIZON einstellen – oder „Überspringen“ bei einem Schalter mit zwei Stellungen.";
                    case Step.Done: return "Fertig. Die Kalibrierung ist gespeichert.";
                    default: return "";
                }
            }
        }

        public bool NeedsConfirm
        {
            get { return Current == Step.Rest || Current == Step.ArmOff || Current == Step.ModeAcro || Current == Step.Done; }
        }

        public bool CanSkip
        {
            get
            {
                return Current == Step.ArmOff || Current == Step.ArmOn || Current == Step.ModeAcro
                    || Current == Step.ModeAngle || Current == Step.ModeHorizon;
            }
        }

        public void Confirm()
        {
            switch (Current)
            {
                case Step.Rest:
                    baseline = stick.CaptureRaw();
                    Go(Step.ThrottleUp);
                    break;
                case Step.ArmOff:
                    baseline = stick.CaptureRaw();
                    Go(Step.ArmOn);
                    break;
                case Step.ModeAcro:
                    baseline = stick.CaptureRaw();
                    Go(Step.ModeAngle);
                    break;
                case Step.Done:
                    stick.Suspended = false;
                    Current = Step.Inactive;
                    break;
            }
        }

        public void Skip()
        {
            switch (Current)
            {
                case Step.ArmOff:
                case Step.ArmOn:
                    draft.arm = new SwitchBinding();
                    Go(Step.ModeAcro);
                    break;
                case Step.ModeAcro:
                case Step.ModeAngle:
                    draft.mode = new ModeBinding();
                    Finish();
                    break;
                case Step.ModeHorizon:
                    draft.mode.hasHorizon = false;
                    Finish();
                    break;
            }
        }

        public void Update(float deltaTime)
        {
            if (!Active || NeedsConfirm) return;
            IReadOnlyList<AxisControl> controls = stick.LearnableControls;
            int index;
            float value;

            switch (Current)
            {
                case Step.ThrottleUp:
                    if (CaptureNew(controls, true, deltaTime, out index, out value))
                    {
                        draft.throttle = new AxisBinding
                        {
                            path = stick.RelativePath(controls[index]),
                            low = baseline[index],
                            center = (baseline[index] + value) * 0.5f,
                            high = value,
                        };
                        used.Add(index);
                        Go(Step.YawRight);
                    }
                    break;

                case Step.YawRight:
                    if (CaptureNew(controls, true, deltaTime, out index, out value))
                    {
                        draft.yaw = HighSide(controls, index, value);
                        Go(Step.YawLeft, index);
                    }
                    break;
                case Step.YawLeft:
                    if (CaptureOpposite(controls, draft.yaw, deltaTime, out value))
                    {
                        draft.yaw.low = value;
                        used.Add(activeIndex);
                        Go(Step.PitchForward);
                    }
                    break;

                case Step.PitchForward:
                    if (CaptureNew(controls, true, deltaTime, out index, out value))
                    {
                        draft.pitch = HighSide(controls, index, value);
                        Go(Step.PitchBack, index);
                    }
                    break;
                case Step.PitchBack:
                    if (CaptureOpposite(controls, draft.pitch, deltaTime, out value))
                    {
                        draft.pitch.low = value;
                        used.Add(activeIndex);
                        Go(Step.RollRight);
                    }
                    break;

                case Step.RollRight:
                    if (CaptureNew(controls, true, deltaTime, out index, out value))
                    {
                        draft.roll = HighSide(controls, index, value);
                        Go(Step.RollLeft, index);
                    }
                    break;
                case Step.RollLeft:
                    if (CaptureOpposite(controls, draft.roll, deltaTime, out value))
                    {
                        draft.roll.low = value;
                        used.Add(activeIndex);
                        Go(Step.ArmOff);
                    }
                    break;

                case Step.ArmOn:
                    if (CaptureNew(controls, false, deltaTime, out index, out value))
                    {
                        draft.arm = new SwitchBinding
                        {
                            path = stick.RelativePath(controls[index]),
                            offValue = baseline[index],
                            onValue = value,
                        };
                        used.Add(index);
                        Go(Step.ModeAcro);
                    }
                    break;

                case Step.ModeAngle:
                    if (CaptureNew(controls, false, deltaTime, out index, out value))
                    {
                        draft.mode = new ModeBinding
                        {
                            path = stick.RelativePath(controls[index]),
                            acroValue = baseline[index],
                            angleValue = value,
                            hasHorizon = false,
                        };
                        Go(Step.ModeHorizon, index);
                    }
                    break;

                case Step.ModeHorizon:
                    if (activeIndex >= 0 && activeIndex < controls.Count)
                    {
                        float current = StickInput.ReadRaw(controls[activeIndex]);
                        bool distinct = Mathf.Abs(current - draft.mode.acroValue) > 0.2f
                            && Mathf.Abs(current - draft.mode.angleValue) > 0.2f;
                        if (distinct && HeldStill(current, deltaTime))
                        {
                            draft.mode.horizonValue = current;
                            draft.mode.hasHorizon = true;
                            Finish();
                        }
                        else if (!distinct)
                        {
                            holdTimer = 0f;
                        }
                    }
                    break;
            }
        }

        AxisBinding HighSide(IReadOnlyList<AxisControl> controls, int index, float value)
        {
            return new AxisBinding
            {
                path = stick.RelativePath(controls[index]),
                center = baseline[index],
                high = value,
                // Vorläufig gespiegelt; der nächste Schritt misst die Gegenseite.
                low = baseline[index] - (value - baseline[index]),
            };
        }

        /// <summary>Wartet, bis ein noch nicht vergebenes Bedienelement deutlich bewegt und ruhig gehalten wird.</summary>
        bool CaptureNew(IReadOnlyList<AxisControl> controls, bool axesOnly, float deltaTime, out int index, out float value)
        {
            index = -1;
            value = 0f;
            float bestChange = MoveThreshold;
            for (int i = 0; i < controls.Count && i < baseline.Length; i++)
            {
                if (used.Contains(i)) continue;
                if (axesOnly && controls[i] is ButtonControl) continue;
                float change = Mathf.Abs(StickInput.ReadRaw(controls[i]) - baseline[i]);
                if (change > bestChange)
                {
                    bestChange = change;
                    index = i;
                }
            }

            if (index < 0)
            {
                candidate = -1;
                holdTimer = 0f;
                return false;
            }

            value = StickInput.ReadRaw(controls[index]);
            if (index != candidate)
            {
                candidate = index;
                holdValue = value;
                holdTimer = 0f;
                return false;
            }
            return HeldStill(value, deltaTime);
        }

        /// <summary>Wartet auf den Vollausschlag derselben Achse in die Gegenrichtung.</summary>
        bool CaptureOpposite(IReadOnlyList<AxisControl> controls, AxisBinding binding, float deltaTime, out float value)
        {
            value = 0f;
            if (activeIndex < 0 || activeIndex >= controls.Count) return false;
            value = StickInput.ReadRaw(controls[activeIndex]);
            float offset = value - binding.center;
            float highSpan = binding.high - binding.center;
            bool opposite = Mathf.Sign(offset) != Mathf.Sign(highSpan) && Mathf.Abs(offset) > MoveThreshold;
            if (!opposite)
            {
                holdTimer = 0f;
                return false;
            }
            return HeldStill(value, deltaTime);
        }

        bool HeldStill(float value, float deltaTime)
        {
            if (Mathf.Abs(value - holdValue) > StableBand)
            {
                holdValue = value;
                holdTimer = 0f;
                return false;
            }
            holdTimer += deltaTime;
            return holdTimer >= HoldSeconds;
        }

        void Go(Step next, int index = -1)
        {
            Current = next;
            activeIndex = index;
            candidate = -1;
            holdTimer = 0f;
            holdValue = float.MaxValue;
        }

        void Finish()
        {
            stick.ApplyProfile(draft);
            Go(Step.Done);
        }
    }
}
