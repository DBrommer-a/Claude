using System;
using System.Numerics;

namespace Frostline.Flight.Core
{
    /// <summary>
    /// Flugphysik-Kern von Frostline. Reines C# ohne Unity-Abhängigkeit, damit er sich
    /// außerhalb des Editors testen lässt.
    ///
    /// Aufbau nach KestrelFPV (Edouard Leurent, MIT-Lizenz, siehe ThirdParty/KestrelFPV):
    /// Schub und Drehmoment jedes Propellers quadratisch aus der Drehzahl, Summe der Kräfte
    /// über die Motorpositionen, Mixer normiert auf die Stellwirkung im Schwebeflug,
    /// Ratenregler innen und Lageregler außen. Neu dazugekommen sind Betaflight- und
    /// Actual-Rates, I-, D- und Vorsteuer-Anteil, Airmode, Motorträgheit, Akku-Einbruch,
    /// Schubverlust bei Fahrt, richtungsabhängiger Luftwiderstand, Horizon- und Turtle-Modus.
    ///
    /// Achsen wie in Unity: x rechts, y oben, z vorn. Motorreihenfolge wie Betaflight Quad-X:
    /// 0 hinten rechts, 1 vorn rechts, 2 hinten links, 3 vorn links.
    /// </summary>
    public sealed class QuadSim : IFlightModel
    {
        public const int MotorCount = 4;

        // Vorzeichen, mit denen Roll, Pitch und Yaw auf die Motoren gehen.
        static readonly float[] MixRoll = { -1f, -1f, 1f, 1f };   // rechts rollen = linke Motoren schneller
        static readonly float[] MixPitch = { 1f, -1f, 1f, -1f };  // Nase runter = hintere Motoren schneller
        static readonly float[] MixYaw = { -1f, 1f, 1f, -1f };    // +1: Propeller dreht von oben gesehen links herum
        static readonly float[] SideX = { 1f, 1f, -1f, -1f };
        static readonly float[] SideZ = { -1f, 1f, -1f, 1f };

        const float RpmToRadPerSec = (float)(2.0 * Math.PI / 60.0);
        const float MaxAxisMix = 0.5f;
        const float MaxYawMix = 0.35f;
        const float MinAuthorityCommand = 0.20f;
        const float AirmodeStartThrottle = 0.25f;
        const float GroundIdleThrottle = 0.05f;
        const float ArmingThrottleLimit = 0.10f;
        const float MaxLevelRate = 400f * MathUtil.Deg2Rad;
        const float TurtleDeadband = 0.15f;
        const float ReverseThrustFactor = 0.5f;
        const float AvionicsCurrentAmps = 0.6f;
        const float BrakeTorqueFactor = 0.8f;

        readonly Battery battery = new Battery();
        readonly PidAxis pidRoll = new PidAxis();
        readonly PidAxis pidPitch = new PidAxis();
        readonly PidAxis pidYaw = new PidAxis();
        readonly Telemetry telemetry = new Telemetry();
        readonly float[] rpm = new float[MotorCount];
        readonly float[] rotorAccel = new float[MotorCount];
        readonly float[] command = new float[MotorCount];
        readonly float[] mix = new float[MotorCount];

        QuadConfig cfg;
        RateProfile rates;
        Pt2Filter smoothRoll, smoothPitch, smoothYaw, smoothThrottle;
        bool armed;
        bool airmodeActive;
        bool saturated;
        float thrustCoefficient;   // N pro (U/min)²
        float torqueCoefficient;   // Nm pro (U/min)²
        float torquePerThrust;     // m
        float pitchSpeedPerRpm;    // m/s pro U/min

        public QuadSim(QuadConfig config, RateProfile rateProfile)
        {
            rates = rateProfile ?? new RateProfile();
            SetConfig(config ?? QuadConfig.Freestyle5());
            Reset();
        }

        public string Name { get { return cfg.Name; } }
        public float MassKg { get { return cfg.MassKg; } }
        public Vector3 InertiaDiagonal { get { return new Vector3(cfg.InertiaPitch, cfg.InertiaYaw, cfg.InertiaRoll); } }
        public Telemetry Telemetry { get { return telemetry; } }
        public QuadConfig Config { get { return cfg; } }
        public bool Armed { get { return armed; } }

        public RateProfile Rates
        {
            get { return rates; }
            set { rates = value ?? new RateProfile(); }
        }

        /// <summary>Übernimmt geänderte Einstellungen, auch mitten im Flug.</summary>
        public void SetConfig(QuadConfig config)
        {
            cfg = config;
            thrustCoefficient = cfg.ThrustCoefficient;
            torquePerThrust = cfg.PropTorqueRatio * cfg.PropDiameterMeters;
            torqueCoefficient = thrustCoefficient * torquePerThrust;
            pitchSpeedPerRpm = cfg.PropPitchMeters * cfg.PitchSpeedFactor / 60f;
            battery.Configure(cfg);
        }

        public void Reset()
        {
            for (int i = 0; i < MotorCount; i++)
            {
                rpm[i] = 0f;
                rotorAccel[i] = 0f;
                command[i] = 0f;
                mix[i] = 0f;
            }
            pidRoll.Reset();
            pidPitch.Reset();
            pidYaw.Reset();
            smoothRoll.Clear();
            smoothPitch.Clear();
            smoothYaw.Clear();
            smoothThrottle.Clear();
            battery.Configure(cfg);
            battery.Reset();
            armed = false;
            airmodeActive = false;
            saturated = false;
            telemetry.ArmingBlockedByThrottle = false;
            telemetry.Failsafe = false;
            telemetry.TurtleActive = false;
            FillTelemetry(default(Vector3), 0f, FlightMode.Acro);
        }

        public FlightOutput Step(in ControlInput input, in BodyState state, float dt)
        {
            if (dt <= 0f) return default(FlightOutput);

            UpdateArming(input);

            float roll = smoothRoll.Apply(MathUtil.Clamp(input.Roll, -1f, 1f), cfg.RcSmoothingHz, dt);
            float pitch = smoothPitch.Apply(MathUtil.Clamp(input.Pitch, -1f, 1f), cfg.RcSmoothingHz, dt);
            float yaw = smoothYaw.Apply(MathUtil.Clamp(input.Yaw, -1f, 1f), cfg.RcSmoothingHz, dt);
            float throttle = smoothThrottle.Apply(MathUtil.Clamp01(input.Throttle), cfg.RcSmoothingHz, dt);

            float maxRpm = cfg.MaxRpmAt(battery.Voltage);
            Quaternion worldToBody = Quaternion.Conjugate(state.Rotation);

            // Kreisel in Pilot-Achsen: Roll rechts, Nase runter, Gier rechts jeweils positiv.
            Vector3 omega = state.AngularVelocityBody;
            float gyroRoll = -omega.Z;
            float gyroPitch = omega.X;
            float gyroYaw = omega.Y;

            Vector3 setpoint = default(Vector3);
            bool turtle = false;

            if (!armed)
            {
                for (int i = 0; i < MotorCount; i++) command[i] = 0f;
                pidRoll.Reset();
                pidPitch.Reset();
                pidYaw.Reset();
                airmodeActive = false;
                saturated = false;
            }
            else if (input.TurtleSwitch)
            {
                turtle = true;
                ComputeTurtle(roll, pitch);
                pidRoll.Reset();
                pidPitch.Reset();
                pidYaw.Reset();
            }
            else
            {
                setpoint = ComputeSetpoints(input.Mode, roll, pitch, yaw, worldToBody);

                if (!airmodeActive && throttle >= AirmodeStartThrottle) airmodeActive = true;

                if (!airmodeActive && throttle < GroundIdleThrottle)
                {
                    // Steht scharf am Boden mit Gas unten: nur Leerlauf, nichts aufintegrieren.
                    pidRoll.Reset();
                    pidPitch.Reset();
                    pidYaw.Reset();
                    saturated = false;
                    for (int i = 0; i < MotorCount; i++) command[i] = cfg.IdleThrottle;
                }
                else
                {
                    float accelRoll = pidRoll.Update(setpoint.X, gyroRoll, dt, cfg.PidRoll, saturated);
                    float accelPitch = pidPitch.Update(setpoint.Y, gyroPitch, dt, cfg.PidPitch, saturated);
                    float accelYaw = pidYaw.Update(setpoint.Z, gyroYaw, dt, cfg.PidYaw, saturated);
                    Mix(throttle, accelRoll, accelPitch, accelYaw, maxRpm);
                }
            }

            // --- Motoren: Drehzahl folgt dem Sollwert mit Trägheit ---
            // Kleine Änderungen folgen einer Zeitkonstante. Große Sprünge begrenzt das
            // Drehmoment, das der Motor bei dieser Drehzahl noch aufbringen kann.
            float noLoadRpm = Math.Max(cfg.MotorKv * battery.Voltage, 1f);
            float staticTorque = torqueCoefficient * maxRpm * maxRpm;
            float torqueCap = staticTorque * cfg.MotorTorqueLimit;
            float headroom = Math.Max(1f - cfg.LoadedRpmFactor, 0.05f);
            float rotorInertia = Math.Max(cfg.RotorInertia, 1e-7f);
            for (int i = 0; i < MotorCount; i++)
            {
                float target = command[i] * maxRpm;
                float tau = target > rpm[i] ? cfg.MotorTauUp : cfg.MotorTauDown;
                float blend = tau > 1e-5f ? 1f - (float)Math.Exp(-dt / tau) : 1f;
                float delta = (target - rpm[i]) * blend;

                float aeroTorque = torqueCoefficient * rpm[i] * rpm[i];
                float available = Math.Min(torqueCap, staticTorque * (1f - rpm[i] / noLoadRpm) / headroom);
                float maxUp = Math.Max(available - aeroTorque, 0f) / rotorInertia * dt / RpmToRadPerSec;
                float maxDown = (torqueCap * BrakeTorqueFactor + aeroTorque) / rotorInertia * dt / RpmToRadPerSec;
                delta = MathUtil.Clamp(delta, -maxDown, maxUp);

                rotorAccel[i] = delta / dt * RpmToRadPerSec;
                rpm[i] += delta;
            }

            // --- Kräfte der Propeller ---
            Vector3 airVelocityBody = Vector3.Transform(state.Velocity - state.Wind, worldToBody);
            float axialSpeed = airVelocityBody.Y;

            float thrust = 0f;
            float torqueX = 0f;
            float torqueY = 0f;
            float torqueZ = 0f;
            float mechanicalPower = 0f;

            for (int i = 0; i < MotorCount; i++)
            {
                float n = rpm[i];
                float staticThrust = thrustCoefficient * n * n;
                float dragTorque = torqueCoefficient * n * n;
                float spin = MixYaw[i];
                float f;

                if (turtle)
                {
                    // Propeller laufen rückwärts: weniger Schub, nach unten gerichtet.
                    f = -ReverseThrustFactor * staticThrust;
                    spin = -spin;
                }
                else
                {
                    // Schubverlust, wenn die Luft schon von selbst durch den Propeller strömt.
                    float pitchSpeed = Math.Max(n * pitchSpeedPerRpm, 0.5f);
                    float inflow = MathUtil.Clamp(1f - axialSpeed / pitchSpeed, 0f, 1.15f);
                    f = staticThrust * inflow;
                }

                thrust += f;
                torqueX += -(SideZ[i] * cfg.ArmZ) * f;
                torqueZ += (SideX[i] * cfg.ArmX) * f;
                torqueY += spin * (dragTorque + cfg.RotorInertia * rotorAccel[i]);

                float rotorSpeed = n * RpmToRadPerSec;
                mechanicalPower += dragTorque * rotorSpeed;
                float spinUpPower = cfg.RotorInertia * rotorSpeed * rotorAccel[i];
                if (spinUpPower > 0f) mechanicalPower += spinUpPower;
            }

            // --- Luftwiderstand, je nach Anströmrichtung unterschiedlich groß ---
            float airSpeed = airVelocityBody.Length();
            float q = 0.5f * cfg.AirDensity * airSpeed;
            Vector3 drag = new Vector3(
                -(q * cfg.DragAreaSide + cfg.LinearDrag) * airVelocityBody.X,
                -(q * cfg.DragAreaTop + cfg.LinearDrag) * airVelocityBody.Y,
                -(q * cfg.DragAreaFront + cfg.LinearDrag) * airVelocityBody.Z);
            Vector3 angularDrag = -cfg.AngularDrag * omega.Length() * omega;

            // --- Akku ---
            float volts = Math.Max(battery.Voltage, 1f);
            float current = mechanicalPower / (Math.Max(cfg.MotorEfficiency, 0.1f) * volts) + AvionicsCurrentAmps;
            battery.Step(current, dt);

            telemetry.TurtleActive = turtle;
            FillTelemetry(setpoint, thrust, input.Mode);
            telemetry.MaxRpm = maxRpm;

            FlightOutput output;
            output.ForceBody = new Vector3(drag.X, thrust + drag.Y, drag.Z);
            output.TorqueBody = new Vector3(torqueX, torqueY, torqueZ) + angularDrag;
            return output;
        }

        void UpdateArming(in ControlInput input)
        {
            telemetry.Failsafe = !input.SignalValid;
            bool wanted = input.ArmSwitch && input.SignalValid;

            if (!wanted)
            {
                armed = false;
                telemetry.ArmingBlockedByThrottle = false;
                return;
            }

            if (!armed)
            {
                if (input.Throttle < ArmingThrottleLimit)
                {
                    armed = true;
                    airmodeActive = false;
                    telemetry.ArmingBlockedByThrottle = false;
                    pidRoll.Reset();
                    pidPitch.Reset();
                    pidYaw.Reset();
                }
                else
                {
                    telemetry.ArmingBlockedByThrottle = true;
                }
            }
        }

        /// <summary>Soll-Drehraten in rad/s als (Roll, Pitch, Yaw).</summary>
        Vector3 ComputeSetpoints(FlightMode mode, float roll, float pitch, float yaw, Quaternion worldToBody)
        {
            float acroRoll = rates.Evaluate(0, roll) * MathUtil.Deg2Rad;
            float acroPitch = rates.Evaluate(1, pitch) * MathUtil.Deg2Rad;
            float acroYaw = rates.Evaluate(2, yaw) * MathUtil.Deg2Rad;

            if (mode == FlightMode.Acro)
            {
                return new Vector3(acroRoll, acroPitch, acroYaw);
            }

            // Lage aus der Richtung von "oben" im Körpersystem.
            Vector3 up = Vector3.Transform(Vector3.UnitY, worldToBody);
            float rollAngle = (float)Math.Atan2(-up.X, up.Y);
            float pitchAngle = (float)Math.Atan2(-up.Z, up.Y);
            float limit = cfg.AngleLimitDeg * MathUtil.Deg2Rad;

            float levelRoll = (roll * limit - rollAngle) * cfg.LevelGain;
            float levelPitch = (pitch * limit - pitchAngle) * cfg.LevelGain;

            if (mode == FlightMode.Angle)
            {
                return new Vector3(
                    MathUtil.Clamp(levelRoll, -MaxLevelRate, MaxLevelRate),
                    MathUtil.Clamp(levelPitch, -MaxLevelRate, MaxLevelRate),
                    acroYaw);
            }

            // Horizon: Selbststabilisierung blendet mit dem Stickausschlag aus.
            float deflection = Math.Max(Math.Abs(roll), Math.Abs(pitch));
            float transition = Math.Max(cfg.HorizonTransition, 0.05f);
            float strength = MathUtil.Clamp01(1f - deflection / transition);
            return new Vector3(
                acroRoll + MathUtil.Clamp(levelRoll, -MaxLevelRate, MaxLevelRate) * strength,
                acroPitch + MathUtil.Clamp(levelPitch, -MaxLevelRate, MaxLevelRate) * strength,
                acroYaw);
        }

        /// <summary>
        /// Verteilt Gas und gewünschte Winkelbeschleunigungen auf die vier Motoren.
        /// Wie im Original-KestrelFPV wird durch die Stellwirkung geteilt, hier aber beim
        /// aktuellen Gas statt fest im Schwebeflug.
        /// </summary>
        void Mix(float throttle, float accelRoll, float accelPitch, float accelYaw, float maxRpm)
        {
            float idle = cfg.IdleThrottle;
            float span = 1f - idle;
            float maxThrustPerMotor = thrustCoefficient * maxRpm * maxRpm;
            float center = Math.Max(idle + span * throttle, MinAuthorityCommand);
            float gain = 8f * maxThrustPerMotor * center * span;
            if (gain < 1e-6f) gain = 1e-6f;

            float authorityRoll = gain * cfg.ArmX / cfg.InertiaRoll;
            float authorityPitch = gain * cfg.ArmZ / cfg.InertiaPitch;
            float authorityYaw = gain * torquePerThrust / cfg.InertiaYaw;

            float mr = MathUtil.Clamp(accelRoll / authorityRoll, -MaxAxisMix, MaxAxisMix);
            float mp = MathUtil.Clamp(accelPitch / authorityPitch, -MaxAxisMix, MaxAxisMix);
            float my = MathUtil.Clamp(accelYaw / authorityYaw, -MaxYawMix, MaxYawMix);

            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = 0; i < MotorCount; i++)
            {
                mix[i] = mr * MixRoll[i] + mp * MixPitch[i] + my * MixYaw[i];
                if (mix[i] < min) min = mix[i];
                if (mix[i] > max) max = mix[i];
            }

            // Airmode: Lageregelung hat Vorrang vor dem Gas. Reicht der Bereich nicht,
            // wird die Korrektur gestaucht statt abgeschnitten.
            float range = max - min;
            saturated = range > 1f;
            if (saturated)
            {
                float scale = 1f / range;
                for (int i = 0; i < MotorCount; i++) mix[i] *= scale;
                min *= scale;
                max *= scale;
            }

            float t = throttle;
            if (t < -min) t = -min;
            if (t > 1f - max) t = 1f - max;

            for (int i = 0; i < MotorCount; i++)
            {
                command[i] = idle + span * MathUtil.Clamp01(t + mix[i]);
            }
        }

        /// <summary>
        /// Turtle-Modus: Die Motoren auf der Seite, in die der Stick zeigt, laufen rückwärts
        /// und drücken die auf dem Rücken liegende Drohne wieder herum.
        /// </summary>
        void ComputeTurtle(float roll, float pitch)
        {
            float deflection = Math.Max(Math.Abs(roll), Math.Abs(pitch));
            for (int i = 0; i < MotorCount; i++)
            {
                float value = 0f;
                if (deflection > TurtleDeadband)
                {
                    value = MathUtil.Clamp01(-roll * MixRoll[i] - pitch * MixPitch[i]);
                }
                command[i] = value * cfg.TurtlePower;
            }
        }

        void FillTelemetry(Vector3 setpoint, float thrust, FlightMode mode)
        {
            for (int i = 0; i < MotorCount; i++)
            {
                telemetry.MotorRpm[i] = rpm[i];
                telemetry.MotorCommand[i] = command[i];
            }
            telemetry.BatteryVoltage = battery.Voltage;
            telemetry.CellVoltage = battery.CellVoltage;
            telemetry.CurrentAmps = battery.CurrentAmps;
            telemetry.UsedMah = battery.UsedMah;
            telemetry.BatteryPercent = battery.StateOfCharge * 100f;
            telemetry.RateSetpointDeg = setpoint * MathUtil.Rad2Deg;
            telemetry.TotalThrustNewton = thrust;
            telemetry.Armed = armed;
            telemetry.Mode = mode;
        }
    }
}
