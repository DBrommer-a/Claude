using System;
using System.Numerics;
using Frostline.Flight.Core;

namespace Frostline.Tests
{
    /// <summary>
    /// Einfacher Starrkörper-Integrator, der im Test die Rolle von Unitys Physik übernimmt.
    /// </summary>
    public sealed class SimRig
    {
        public readonly QuadSim Sim;
        public Vector3 Position;
        public Vector3 Velocity;
        public Vector3 AngularVelocityBody;
        public Quaternion Rotation = Quaternion.Identity;
        public Vector3 Wind;
        public float Time;
        public float Dt = 0.002f;
        public FlightOutput LastOutput;

        public SimRig(QuadConfig config, RateProfile rates = null)
        {
            Sim = new QuadSim(config, rates ?? new RateProfile());
        }

        public Vector3 Right { get { return Vector3.Transform(Vector3.UnitX, Rotation); } }
        public Vector3 Up { get { return Vector3.Transform(Vector3.UnitY, Rotation); } }
        public Vector3 Forward { get { return Vector3.Transform(Vector3.UnitZ, Rotation); } }

        /// <summary>Drehraten in Pilot-Achsen (Roll rechts, Nase runter, Gier rechts), Grad/s.</summary>
        public Vector3 PilotRatesDeg
        {
            get
            {
                return new Vector3(-AngularVelocityBody.Z, AngularVelocityBody.X, AngularVelocityBody.Y) * MathUtil.Rad2Deg;
            }
        }

        public static ControlInput Sticks(float roll, float pitch, float yaw, float throttle, FlightMode mode = FlightMode.Acro)
        {
            ControlInput input;
            input.Roll = roll;
            input.Pitch = pitch;
            input.Yaw = yaw;
            input.Throttle = throttle;
            input.ArmSwitch = true;
            input.TurtleSwitch = false;
            input.Mode = mode;
            input.SignalValid = true;
            return input;
        }

        /// <summary>Schärft die Drohne (Gas unten) und lässt die Motoren kurz anlaufen.</summary>
        public void Arm()
        {
            Sim.Step(Sticks(0f, 0f, 0f, 0f), CurrentState(), Dt);
        }

        public BodyState CurrentState()
        {
            BodyState s;
            s.Rotation = Rotation;
            s.Velocity = Velocity;
            s.AngularVelocityBody = AngularVelocityBody;
            s.Wind = Wind;
            return s;
        }

        public void Step(in ControlInput input)
        {
            LastOutput = Sim.Step(input, CurrentState(), Dt);

            Vector3 forceWorld = Vector3.Transform(LastOutput.ForceBody, Rotation);
            forceWorld.Y -= Sim.MassKg * QuadConfig.Gravity;
            Velocity += forceWorld / Sim.MassKg * Dt;
            Position += Velocity * Dt;

            Vector3 inertia = Sim.InertiaDiagonal;
            Vector3 momentum = inertia * AngularVelocityBody;
            Vector3 torque = LastOutput.TorqueBody - Vector3.Cross(AngularVelocityBody, momentum);
            AngularVelocityBody += torque / inertia * Dt;

            float angle = AngularVelocityBody.Length() * Dt;
            if (angle > 1e-9f)
            {
                Vector3 axis = Vector3.Normalize(AngularVelocityBody);
                Rotation = Quaternion.Normalize(Rotation * Quaternion.CreateFromAxisAngle(axis, angle));
            }
            Time += Dt;
        }

        public void Run(float seconds, in ControlInput input, Action<SimRig> each = null)
        {
            int steps = (int)Math.Round(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                Step(input);
                if (each != null) each(this);
            }
        }

        /// <summary>Gas-Stellung, bei der die Drohne mit vollem Akku schwebt.</summary>
        public float HoverThrottle()
        {
            QuadConfig c = Sim.Config;
            float voltage = Sim.Telemetry.BatteryVoltage;
            float maxRpm = c.MaxRpmAt(voltage);
            float hoverRpm = (float)Math.Sqrt(c.MassKg * QuadConfig.Gravity / 4f / c.ThrustCoefficient);
            float command = hoverRpm / maxRpm;
            return (command - c.IdleThrottle) / (1f - c.IdleThrottle);
        }
    }
}
