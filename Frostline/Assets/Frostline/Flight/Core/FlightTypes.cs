using System.Numerics;

namespace Frostline.Flight.Core
{
    /// <summary>Flugmodi wie bei Betaflight.</summary>
    public enum FlightMode
    {
        /// <summary>Rate-Modus: Stick = Drehrate, keine Selbststabilisierung.</summary>
        Acro = 0,
        /// <summary>Stick = Neigungswinkel, Drohne richtet sich selbst auf.</summary>
        Angle = 1,
        /// <summary>Um die Mitte selbststabilisierend, bei vollem Ausschlag Acro.</summary>
        Horizon = 2,
    }

    /// <summary>
    /// Kalibrierte Pilot-Eingabe. Egal ob USB-Sender, Gamepad oder iPhone-App:
    /// alles landet in dieser einen Struktur.
    /// </summary>
    public struct ControlInput
    {
        /// <summary>-1..+1, positiv = nach rechts rollen.</summary>
        public float Roll;
        /// <summary>-1..+1, positiv = Stick nach vorn = Nase runter.</summary>
        public float Pitch;
        /// <summary>-1..+1, positiv = nach rechts gieren.</summary>
        public float Yaw;
        /// <summary>0..1.</summary>
        public float Throttle;
        public bool ArmSwitch;
        public bool TurtleSwitch;
        public FlightMode Mode;
        /// <summary>false = Verbindung zum Sender verloren -> Failsafe (Motoren aus).</summary>
        public bool SignalValid;
    }

    /// <summary>
    /// Zustand des Starrkörpers. Achsen wie in Unity: x rechts, y oben, z vorn.
    /// </summary>
    public struct BodyState
    {
        /// <summary>Körper -> Welt.</summary>
        public Quaternion Rotation;
        /// <summary>Geschwindigkeit in Weltkoordinaten, m/s.</summary>
        public Vector3 Velocity;
        /// <summary>Drehrate in Körperkoordinaten, rad/s.</summary>
        public Vector3 AngularVelocityBody;
        /// <summary>Wind in Weltkoordinaten, m/s.</summary>
        public Vector3 Wind;
    }

    /// <summary>Kraft (N) und Drehmoment (Nm) in Körperkoordinaten, ohne Schwerkraft.</summary>
    public struct FlightOutput
    {
        public Vector3 ForceBody;
        public Vector3 TorqueBody;
    }

    /// <summary>Messwerte für OSD, Sound, Telemetrie zur iPhone-App.</summary>
    public sealed class Telemetry
    {
        public readonly float[] MotorRpm = new float[4];
        public readonly float[] MotorCommand = new float[4];
        public float MaxRpm;
        public float BatteryVoltage;
        public float CellVoltage;
        public float CurrentAmps;
        public float UsedMah;
        public float BatteryPercent;
        /// <summary>Sollwerte in Grad/s: Roll, Pitch, Yaw.</summary>
        public Vector3 RateSetpointDeg;
        public float TotalThrustNewton;
        public bool Armed;
        /// <summary>Arm-Schalter ist an, aber Gas war beim Einschalten nicht unten.</summary>
        public bool ArmingBlockedByThrottle;
        public bool Failsafe;
        public bool TurtleActive;
        public FlightMode Mode;
    }

    /// <summary>
    /// Schnittstelle für austauschbare Flugphysik (z. B. später ein Adapter für Yue).
    /// </summary>
    public interface IFlightModel
    {
        string Name { get; }
        float MassKg { get; }
        /// <summary>Trägheitsmomente um die Unity-Körperachsen (x = Pitch, y = Yaw, z = Roll), kg m².</summary>
        Vector3 InertiaDiagonal { get; }
        Telemetry Telemetry { get; }
        void Reset();
        FlightOutput Step(in ControlInput input, in BodyState state, float dt);
    }
}
