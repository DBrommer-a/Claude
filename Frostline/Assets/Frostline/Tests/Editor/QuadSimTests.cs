using System;
using System.Numerics;
using Frostline.Flight.Core;
using NUnit.Framework;

namespace Frostline.Tests
{
    /// <summary>
    /// Fliegt die Physik ohne Unity: Schweben, Schub, Sprungantworten, Modi, Akku.
    /// Läuft im Unity Test Runner und per "dotnet test" (siehe tools/flightcore-tests).
    /// </summary>
    public class QuadSimTests
    {
        static QuadConfig[] Presets()
        {
            return QuadConfig.AllPresets();
        }

        static SimRig AirborneRig(QuadConfig config, float settleSeconds = 0.5f)
        {
            var rig = new SimRig(config);
            rig.Position = new Vector3(0f, 100f, 0f);
            rig.Arm();
            rig.Run(settleSeconds, SimRig.Sticks(0f, 0f, 0f, rig.HoverThrottle()));
            return rig;
        }

        // ------------------------------------------------------------ Schweben und Schub

        [TestCaseSource(nameof(Presets))]
        public void Hover_HoldsAltitudeAndStaysLevel(QuadConfig config)
        {
            var rig = AirborneRig(config);
            float hover = rig.HoverThrottle();
            float startY = rig.Position.Y;
            rig.Run(2f, SimRig.Sticks(0f, 0f, 0f, hover));

            // Die Gas-Stellung ist mit voller Spannung gerechnet; unter Last sackt die Drohne leicht.
            Assert.That(Math.Abs(rig.Velocity.Y), Is.LessThan(1.0f), "Steig-/Sinkrate beim Schweben");
            Assert.That(Math.Abs(rig.Position.Y - startY), Is.LessThan(1.5f), "Höhe driftet zu stark");
            Assert.That(rig.Up.Y, Is.GreaterThan(0.9999f), "Drohne muss waagerecht bleiben");
            Assert.That(rig.PilotRatesDeg.Length(), Is.LessThan(0.5f));
        }

        [TestCaseSource(nameof(Presets))]
        public void HoverThrottle_IsInRealisticRange(QuadConfig config)
        {
            var rig = new SimRig(config);
            float hover = rig.HoverThrottle();
            Assert.That(hover, Is.GreaterThan(0.15f).And.LessThan(0.60f), config.Name);
        }

        [TestCaseSource(nameof(Presets))]
        public void FullThrottle_ReachesMostOfTheConfiguredThrust(QuadConfig config)
        {
            var rig = AirborneRig(config);
            float peak = 0f;
            rig.Run(0.5f, SimRig.Sticks(0f, 0f, 0f, 1f), r =>
            {
                float g = r.Sim.Telemetry.TotalThrustNewton / (config.MassKg * QuadConfig.Gravity);
                if (g > peak) peak = g;
            });

            // Akku-Einbruch und Fahrtwind kosten Schub, aber nicht mehr als rund ein Drittel.
            Assert.That(peak, Is.GreaterThan(config.ThrustToWeight * 0.62f), "zu wenig Schub");
            Assert.That(peak, Is.LessThan(config.ThrustToWeight * 1.20f), "mehr Schub als eingestellt");
            Assert.That(rig.Velocity.Y, Is.GreaterThan(3f));
        }

        [Test]
        public void Freestyle_PerformanceNumbersArePlausible()
        {
            var rig = AirborneRig(QuadConfig.Freestyle5());
            Assert.That(rig.Sim.Telemetry.CurrentAmps, Is.InRange(3f, 12f), "Strom beim Schweben");

            rig.Run(0.4f, SimRig.Sticks(0f, 0f, 0f, 1f));
            Assert.That(rig.Sim.Telemetry.CurrentAmps, Is.InRange(80f, 170f), "Strom bei Vollgas");
            Assert.That(rig.Sim.Telemetry.BatteryVoltage, Is.InRange(19.5f, 23.5f), "Spannung bricht bei Vollgas ein");

            rig.Run(10f, SimRig.Sticks(0f, 0f, 0f, 1f));
            Assert.That(rig.Velocity.Y * 3.6f, Is.InRange(90f, 190f), "Steiggeschwindigkeit in km/h");
        }

        [TestCaseSource(nameof(Presets))]
        public void Disarmed_FallsWithTerminalVelocity(QuadConfig config)
        {
            var rig = new SimRig(config);
            rig.Position = new Vector3(0f, 2000f, 0f);
            ControlInput off = SimRig.Sticks(0f, 0f, 0f, 0f);
            off.ArmSwitch = false;
            rig.Run(15f, off);
            float speed = -rig.Velocity.Y;
            Assert.That(speed, Is.InRange(8f, 40f));
            Assert.That(rig.Sim.Telemetry.TotalThrustNewton, Is.EqualTo(0f).Within(1e-4f));
        }

        // ------------------------------------------------------------ Vorzeichen

        [Test]
        public void RollRight_LowersTheRightSide()
        {
            var rig = AirborneRig(QuadConfig.Freestyle5());
            rig.Run(0.15f, SimRig.Sticks(0.3f, 0f, 0f, rig.HoverThrottle()));
            Assert.That(rig.Right.Y, Is.LessThan(-0.05f));
            Assert.That(rig.PilotRatesDeg.X, Is.GreaterThan(0f));
        }

        [Test]
        public void PitchForward_LowersTheNose()
        {
            var rig = AirborneRig(QuadConfig.Freestyle5());
            rig.Run(0.15f, SimRig.Sticks(0f, 0.3f, 0f, rig.HoverThrottle()));
            Assert.That(rig.Forward.Y, Is.LessThan(-0.05f));
            Assert.That(rig.PilotRatesDeg.Y, Is.GreaterThan(0f));
        }

        [Test]
        public void YawRight_TurnsTheNoseToTheRight()
        {
            var rig = AirborneRig(QuadConfig.Freestyle5());
            rig.Run(0.15f, SimRig.Sticks(0f, 0f, 0.3f, rig.HoverThrottle()));
            Assert.That(rig.Forward.X, Is.GreaterThan(0.05f));
            Assert.That(rig.PilotRatesDeg.Z, Is.GreaterThan(0f));
        }

        // ------------------------------------------------------------ Sprungantwort

        struct StepResult
        {
            public float RiseMs;
            public float OvershootPercent;
            public float FinalError;
        }

        static StepResult MeasureStep(QuadConfig config, int axis, float stick, float dt, float throttle = -1f)
        {
            var rig = new SimRig(config);
            rig.Dt = dt;
            rig.Position = new Vector3(0f, 100f, 0f);
            rig.Arm();
            float hover = rig.HoverThrottle();
            rig.Run(0.5f, SimRig.Sticks(0f, 0f, 0f, hover));
            if (throttle < 0f) throttle = hover;

            float target = rig.Sim.Rates.Evaluate(axis, stick);
            ControlInput input = SimRig.Sticks(axis == 0 ? stick : 0f, axis == 1 ? stick : 0f, axis == 2 ? stick : 0f, throttle);
            float start = rig.Time;
            float t10 = -1f, t90 = -1f, peak = 0f, last = 0f;
            rig.Run(0.4f, input, r =>
            {
                Vector3 rates = r.PilotRatesDeg;
                float v = axis == 0 ? rates.X : (axis == 1 ? rates.Y : rates.Z);
                if (t10 < 0f && v >= 0.1f * target) t10 = r.Time - start;
                if (t90 < 0f && v >= 0.9f * target) t90 = r.Time - start;
                if (v > peak) peak = v;
                last = v;
            });

            StepResult result;
            result.RiseMs = t90 < 0f ? float.PositiveInfinity : (t90 - t10) * 1000f;
            result.OvershootPercent = (peak / target - 1f) * 100f;
            result.FinalError = Math.Abs(last - target) / target;
            return result;
        }

        [TestCaseSource(nameof(Presets))]
        public void RateStep_IsFastAndWellDamped(QuadConfig config)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                StepResult r = MeasureStep(config, axis, 0.6f, 0.002f);
                string label = config.Name + " Achse " + axis;
                Assert.That(r.RiseMs, Is.LessThan(90f), label + ": Anstieg zu langsam");
                Assert.That(r.OvershootPercent, Is.LessThan(12f), label + ": schwingt über");
                Assert.That(r.FinalError, Is.LessThan(0.03f), label + ": Sollrate nicht erreicht");
            }
        }

        [TestCaseSource(nameof(Presets))]
        public void RateStep_FullStickStillTracks(QuadConfig config)
        {
            StepResult r = MeasureStep(config, 0, 1f, 0.002f);
            Assert.That(r.RiseMs, Is.LessThan(110f));
            Assert.That(r.OvershootPercent, Is.LessThan(12f));
            Assert.That(r.FinalError, Is.LessThan(0.04f));
        }

        [TestCase(0.001f)]
        [TestCase(0.002f)]
        [TestCase(0.004f)]
        public void RateStep_BehavesTheSameAtOtherPhysicsRates(float dt)
        {
            StepResult r = MeasureStep(QuadConfig.Freestyle5(), 0, 0.6f, dt);
            Assert.That(r.RiseMs, Is.InRange(25f, 60f));
            Assert.That(r.OvershootPercent, Is.LessThan(10f));
            Assert.That(r.FinalError, Is.LessThan(0.02f));
        }

        [Test]
        public void RateStep_StaysStableAtHighThrottle()
        {
            for (int axis = 0; axis < 3; axis++)
            {
                StepResult r = MeasureStep(QuadConfig.Freestyle5(), axis, 0.6f, 0.002f, 0.9f);
                Assert.That(r.OvershootPercent, Is.LessThan(15f), "Achse " + axis);
                Assert.That(r.FinalError, Is.LessThan(0.04f), "Achse " + axis);
            }
        }

        [Test]
        public void Airmode_KeepsControlAtZeroThrottle()
        {
            var rig = AirborneRig(QuadConfig.Freestyle5());
            // Gas ganz raus, trotzdem rollen: Airmode muss die Motoren dafür anheben.
            rig.Run(0.3f, SimRig.Sticks(0.5f, 0f, 0f, 0f));
            float target = rig.Sim.Rates.Evaluate(0, 0.5f);
            Assert.That(rig.PilotRatesDeg.X, Is.GreaterThan(target * 0.8f));
        }

        // ------------------------------------------------------------ Flugmodi

        [Test]
        public void AngleMode_LevelsFromATilt()
        {
            var rig = AirborneRig(QuadConfig.Freestyle5());
            rig.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 40f * MathUtil.Deg2Rad);
            rig.Run(1.0f, SimRig.Sticks(0f, 0f, 0f, rig.HoverThrottle(), FlightMode.Angle));
            Assert.That(rig.Up.Y, Is.GreaterThan(0.999f));
        }

        [Test]
        public void AngleMode_HoldsTheCommandedAngle()
        {
            QuadConfig config = QuadConfig.Freestyle5();
            var rig = AirborneRig(config);
            rig.Run(1.0f, SimRig.Sticks(0.5f, 0f, 0f, rig.HoverThrottle(), FlightMode.Angle));
            float rollDeg = (float)Math.Asin(-rig.Right.Y) * MathUtil.Rad2Deg;
            Assert.That(rollDeg, Is.EqualTo(config.AngleLimitDeg * 0.5f).Within(2f));
        }

        [Test]
        public void AngleMode_RecoversFromUpsideDown()
        {
            var rig = AirborneRig(QuadConfig.Freestyle5());
            rig.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 170f * MathUtil.Deg2Rad);
            rig.Run(1.5f, SimRig.Sticks(0f, 0f, 0f, rig.HoverThrottle(), FlightMode.Angle));
            Assert.That(rig.Up.Y, Is.GreaterThan(0.99f));
        }

        [Test]
        public void HorizonMode_LevelsAtCenterAndFlipsAtFullStick()
        {
            var rig = AirborneRig(QuadConfig.Freestyle5());
            rig.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 30f * MathUtil.Deg2Rad);
            rig.Run(1.0f, SimRig.Sticks(0f, 0f, 0f, rig.HoverThrottle(), FlightMode.Horizon));
            Assert.That(rig.Up.Y, Is.GreaterThan(0.995f), "soll sich in der Mitte aufrichten");

            rig.Run(0.3f, SimRig.Sticks(1f, 0f, 0f, rig.HoverThrottle(), FlightMode.Horizon));
            Assert.That(rig.PilotRatesDeg.X, Is.GreaterThan(500f), "voller Stick soll wie Acro rollen");
        }

        [Test]
        public void AcroMode_DoesNotSelfLevel()
        {
            var rig = AirborneRig(QuadConfig.Freestyle5());
            rig.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 30f * MathUtil.Deg2Rad);
            rig.Run(0.5f, SimRig.Sticks(0f, 0f, 0f, rig.HoverThrottle(), FlightMode.Acro));
            float tiltDeg = (float)Math.Acos(MathUtil.Clamp(rig.Up.Y, -1f, 1f)) * MathUtil.Rad2Deg;
            Assert.That(tiltDeg, Is.EqualTo(30f).Within(1.5f));
        }

        // ------------------------------------------------------------ Schärfen, Failsafe, Turtle

        [Test]
        public void Arming_IsBlockedWhileThrottleIsUp()
        {
            var rig = new SimRig(QuadConfig.Freestyle5());
            rig.Run(0.2f, SimRig.Sticks(0f, 0f, 0f, 0.6f));
            Assert.That(rig.Sim.Armed, Is.False);
            Assert.That(rig.Sim.Telemetry.ArmingBlockedByThrottle, Is.True);
            Assert.That(rig.Sim.Telemetry.MotorRpm[0], Is.EqualTo(0f));

            rig.Run(0.2f, SimRig.Sticks(0f, 0f, 0f, 0f));
            Assert.That(rig.Sim.Armed, Is.True);
            Assert.That(rig.Sim.Telemetry.MotorRpm[0], Is.GreaterThan(500f), "Leerlauf nach dem Schärfen");
        }

        [Test]
        public void Failsafe_StopsTheMotors()
        {
            var rig = AirborneRig(QuadConfig.Freestyle5());
            ControlInput lost = SimRig.Sticks(0f, 0f, 0f, 0.5f);
            lost.SignalValid = false;
            rig.Run(0.5f, lost);
            Assert.That(rig.Sim.Armed, Is.False);
            Assert.That(rig.Sim.Telemetry.Failsafe, Is.True);
            Assert.That(rig.Sim.Telemetry.MotorRpm[0], Is.LessThan(200f));

            // Signal kommt mit Gas oben zurück: darf nicht von selbst wieder scharf werden.
            rig.Run(0.2f, SimRig.Sticks(0f, 0f, 0f, 0.5f));
            Assert.That(rig.Sim.Armed, Is.False);
        }

        [Test]
        public void Turtle_PushesDownOnTheStickSide()
        {
            var rig = new SimRig(QuadConfig.Freestyle5());
            rig.Arm();
            ControlInput turtle = SimRig.Sticks(1f, 0f, 0f, 0f);
            turtle.TurtleSwitch = true;
            FlightOutput output = default(FlightOutput);
            for (int i = 0; i < 100; i++) output = rig.Sim.Step(turtle, rig.CurrentState(), rig.Dt);

            Telemetry t = rig.Sim.Telemetry;
            Assert.That(t.TurtleActive, Is.True);
            Assert.That(t.MotorRpm[0], Is.GreaterThan(1000f), "hinten rechts läuft");
            Assert.That(t.MotorRpm[1], Is.GreaterThan(1000f), "vorn rechts läuft");
            Assert.That(t.MotorRpm[2], Is.LessThan(50f), "hinten links steht");
            Assert.That(t.MotorRpm[3], Is.LessThan(50f), "vorn links steht");
            Assert.That(output.ForceBody.Y, Is.LessThan(0f), "Schub zeigt zur Unterseite");
            Assert.That(output.TorqueBody.Z, Is.LessThan(0f), "Moment dreht über die rechte Seite");
        }

        // ------------------------------------------------------------ Akku

        [Test]
        public void Battery_DrainsAndGetsWeaker()
        {
            QuadConfig config = QuadConfig.Freestyle5();
            var rig = AirborneRig(config);
            float freshVoltage = rig.Sim.Telemetry.BatteryVoltage;
            float hover = rig.HoverThrottle();
            rig.Run(120f, SimRig.Sticks(0f, 0f, 0f, hover));

            Telemetry t = rig.Sim.Telemetry;
            Assert.That(t.UsedMah, Is.InRange(120f, 500f), "Verbrauch nach zwei Minuten Schweben");
            Assert.That(t.BatteryVoltage, Is.LessThan(freshVoltage - 0.5f));
            Assert.That(rig.Velocity.Y, Is.LessThan(-0.5f), "gleiches Gas reicht mit leererem Akku nicht mehr zum Schweben");
        }

        [Test]
        public void Battery_InfiniteOptionKeepsCapacity()
        {
            QuadConfig config = QuadConfig.Freestyle5();
            config.InfiniteBattery = true;
            var rig = AirborneRig(config);
            rig.Run(30f, SimRig.Sticks(0f, 0f, 0f, 1f));
            Assert.That(rig.Sim.Telemetry.UsedMah, Is.EqualTo(0f));
            Assert.That(rig.Sim.Telemetry.BatteryVoltage, Is.LessThan(config.Cells * 4.2f - 1f), "Einbruch unter Last bleibt");
        }

        [Test]
        public void Battery_RestingVoltageCurveIsMonotonic()
        {
            float previous = 0f;
            for (float soc = 0f; soc <= 1.0001f; soc += 0.05f)
            {
                float v = Battery.RestingCellVoltage(soc);
                Assert.That(v, Is.GreaterThanOrEqualTo(previous));
                previous = v;
            }
            Assert.That(Battery.RestingCellVoltage(1f), Is.EqualTo(4.2f).Within(1e-4f));
        }

        // ------------------------------------------------------------ Robustheit

        [Test]
        public void Wind_PushesAHoveringDrone()
        {
            var rig = AirborneRig(QuadConfig.Freestyle5());
            rig.Wind = new Vector3(10f, 0f, 0f);
            rig.Run(2f, SimRig.Sticks(0f, 0f, 0f, rig.HoverThrottle(), FlightMode.Angle));
            Assert.That(rig.Velocity.X, Is.GreaterThan(0.5f));
        }

        [TestCaseSource(nameof(Presets))]
        public void RandomSticks_NeverProduceInvalidNumbers(QuadConfig config)
        {
            var rig = AirborneRig(config);
            var random = new Random(12345);
            ControlInput input = SimRig.Sticks(0f, 0f, 0f, 0.5f);
            int steps = (int)(30f / rig.Dt);
            for (int i = 0; i < steps; i++)
            {
                if (i % 40 == 0)
                {
                    input.Roll = (float)(random.NextDouble() * 2.0 - 1.0);
                    input.Pitch = (float)(random.NextDouble() * 2.0 - 1.0);
                    input.Yaw = (float)(random.NextDouble() * 2.0 - 1.0);
                    input.Throttle = (float)random.NextDouble();
                    input.Mode = (FlightMode)random.Next(0, 3);
                }
                rig.Step(input);
                Assert.That(float.IsNaN(rig.Position.X) || float.IsInfinity(rig.Position.X), Is.False, "Schritt " + i);
                Assert.That(rig.AngularVelocityBody.Length() * MathUtil.Rad2Deg, Is.LessThan(3000f), "Schritt " + i);
                Assert.That(rig.Velocity.Length(), Is.LessThan(120f), "Schritt " + i);
            }
        }

        [Test]
        public void Reset_RestoresAFreshDrone()
        {
            var rig = AirborneRig(QuadConfig.Freestyle5());
            rig.Run(5f, SimRig.Sticks(0.2f, 0.1f, 0f, 1f));
            rig.Sim.Reset();
            Telemetry t = rig.Sim.Telemetry;
            Assert.That(rig.Sim.Armed, Is.False);
            Assert.That(t.UsedMah, Is.EqualTo(0f));
            Assert.That(t.MotorRpm[0], Is.EqualTo(0f));
            Assert.That(t.BatteryVoltage, Is.EqualTo(6f * 4.2f).Within(1e-3f));
        }
    }
}
