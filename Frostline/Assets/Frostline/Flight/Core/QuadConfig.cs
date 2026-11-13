using System;

namespace Frostline.Flight.Core
{
    /// <summary>
    /// Alle einstellbaren Werte einer Drohne. Die Presets unten sind Startwerte; Gewicht,
    /// Motor-KV, Propellergröße und Schub-Gewicht-Verhältnis lassen sich im Spiel ändern.
    /// </summary>
    [Serializable]
    public sealed class QuadConfig
    {
        public string Name = "Freestyle 5\"";

        // --- Rahmen ---
        /// <summary>Abfluggewicht mit Akku und Kamera, kg.</summary>
        public float MassKg = 0.70f;
        /// <summary>Abstand Motor zur Längsachse (halbe Breite), m.</summary>
        public float ArmX = 0.0795f;
        /// <summary>Abstand Motor zur Querachse (halbe Länge), m.</summary>
        public float ArmZ = 0.0795f;
        public float InertiaRoll = 0.0019f;
        public float InertiaPitch = 0.0023f;
        public float InertiaYaw = 0.0032f;

        // --- Antrieb ---
        public int Cells = 6;
        public float MotorKv = 1950f;
        public float PropDiameterInch = 5.1f;
        public float PropPitchInch = 4.3f;
        /// <summary>Standschub aller vier Motoren geteilt durch Gewicht, bei 3,9 V pro Zelle.</summary>
        public float ThrustToWeight = 8.5f;
        /// <summary>Anteil der Leerlaufdrehzahl (KV x Spannung), den der Motor mit Propeller erreicht.</summary>
        public float LoadedRpmFactor = 0.70f;
        /// <summary>Zeitkonstante beim Hochdrehen, s.</summary>
        public float MotorTauUp = 0.035f;
        /// <summary>Zeitkonstante beim Abbremsen, s.</summary>
        public float MotorTauDown = 0.045f;
        /// <summary>Leerlauf bei scharfer Drohne (wie DShot-Idle), 0..1.</summary>
        public float IdleThrottle = 0.055f;
        /// <summary>Wirkungsgrad Motor + Regler.</summary>
        public float MotorEfficiency = 0.78f;
        /// <summary>Trägheit von Propeller und Motorglocke, kg m². Macht Gieren beim Gasgeben direkt.</summary>
        public float RotorInertia = 9e-6f;
        /// <summary>Größtes Motor-Drehmoment relativ zum Drehmoment bei Vollgas im Stand (Strombegrenzung).</summary>
        public float MotorTorqueLimit = 1.3f;
        /// <summary>Verhältnis Propeller-Drehmoment zu Schub, geteilt durch den Durchmesser.</summary>
        public float PropTorqueRatio = 0.11f;
        /// <summary>Wirksame Steigung relativ zur geometrischen (Schub wird erst darüber null).</summary>
        public float PitchSpeedFactor = 1.25f;

        // --- Akku ---
        public float CapacityMah = 1300f;
        /// <summary>Innenwiderstand von Akku, Kabeln und Reglern zusammen, Ohm.</summary>
        public float InternalResistanceOhm = 0.030f;
        /// <summary>true: Akku wird nicht leer, Spannung bricht aber unter Last trotzdem ein.</summary>
        public bool InfiniteBattery = false;

        // --- Luftwiderstand ---
        /// <summary>Widerstandsfläche (cw x A) von vorn, m².</summary>
        public float DragAreaFront = 0.0065f;
        public float DragAreaSide = 0.0085f;
        public float DragAreaTop = 0.018f;
        /// <summary>Linearer Widerstand (Rotor-Drag), N pro m/s.</summary>
        public float LinearDrag = 0.03f;
        /// <summary>Dämpfung der Drehung, Nm pro (rad/s)².</summary>
        public float AngularDrag = 4e-5f;
        public float AirDensity = 1.225f;

        // --- Regler ---
        public PidGains PidRoll = new PidGains(90f, 80f, 1.6f, 0.8f);
        public PidGains PidPitch = new PidGains(90f, 80f, 1.6f, 0.8f);
        public PidGains PidYaw = new PidGains(30f, 30f, 0f, 0.3f);
        /// <summary>Glättung der Stick-Signale, Hz.</summary>
        public float RcSmoothingHz = 50f;
        /// <summary>Maximaler Neigungswinkel im Angle-Modus, Grad.</summary>
        public float AngleLimitDeg = 55f;
        /// <summary>Angle-Modus: Drehrate pro Winkelfehler, 1/s.</summary>
        public float LevelGain = 7f;
        /// <summary>Horizon-Modus: Ab diesem Stickausschlag ist die Selbststabilisierung ganz aus.</summary>
        public float HorizonTransition = 0.75f;
        /// <summary>Anteil des Schubs, den der Turtle-Modus zum Umdrehen nutzt.</summary>
        public float TurtlePower = 0.45f;

        // --- Kamera (nur Ansicht, keine Physik) ---
        public float CameraTiltDeg = 30f;

        public const float ReferenceCellVoltage = 3.9f;
        public const float Gravity = 9.81f;

        public float ReferenceVoltage { get { return Cells * ReferenceCellVoltage; } }

        public float MaxRpmAt(float voltage)
        {
            return MotorKv * voltage * LoadedRpmFactor;
        }

        /// <summary>Schubbeiwert in N pro (U/min)², so gewählt, dass das Schub-Gewicht-Verhältnis stimmt.</summary>
        public float ThrustCoefficient
        {
            get
            {
                float maxThrustPerMotor = ThrustToWeight * MassKg * Gravity / 4f;
                float rpm = MaxRpmAt(ReferenceVoltage);
                return maxThrustPerMotor / (rpm * rpm);
            }
        }

        public float PropDiameterMeters { get { return PropDiameterInch * 0.0254f; } }
        public float PropPitchMeters { get { return PropPitchInch * 0.0254f; } }

        public QuadConfig Clone()
        {
            return (QuadConfig)MemberwiseClone();
        }

        // ------------------------------------------------------------------ Presets

        public static QuadConfig Freestyle5()
        {
            return new QuadConfig();
        }

        public static QuadConfig Racer5()
        {
            return new QuadConfig
            {
                Name = "Racer 5\"",
                MassKg = 0.56f,
                ArmX = 0.070f,
                ArmZ = 0.084f,
                InertiaRoll = 0.0013f,
                InertiaPitch = 0.0017f,
                InertiaYaw = 0.0025f,
                Cells = 6,
                MotorKv = 2100f,
                PropDiameterInch = 5.1f,
                PropPitchInch = 4.8f,
                ThrustToWeight = 12f,
                MotorTauUp = 0.030f,
                MotorTauDown = 0.038f,
                RotorInertia = 8e-6f,
                CapacityMah = 1100f,
                InternalResistanceOhm = 0.032f,
                DragAreaFront = 0.0045f,
                DragAreaSide = 0.0070f,
                DragAreaTop = 0.016f,
                LinearDrag = 0.025f,
                CameraTiltDeg = 45f,
            };
        }

        public static QuadConfig Cinewhoop3()
        {
            return new QuadConfig
            {
                Name = "Cinewhoop 3\"",
                MassKg = 0.40f,
                ArmX = 0.052f,
                ArmZ = 0.052f,
                InertiaRoll = 5.5e-4f,
                InertiaPitch = 6.5e-4f,
                InertiaYaw = 1.0e-3f,
                Cells = 4,
                MotorKv = 3600f,
                PropDiameterInch = 3.0f,
                PropPitchInch = 2.0f,
                ThrustToWeight = 3.8f,
                MotorTauUp = 0.040f,
                MotorTauDown = 0.050f,
                RotorInertia = 1.5e-6f,
                CapacityMah = 850f,
                InternalResistanceOhm = 0.036f,
                DragAreaFront = 0.014f,
                DragAreaSide = 0.014f,
                DragAreaTop = 0.028f,
                LinearDrag = 0.05f,
                AngularDrag = 6e-5f,
                PidRoll = new PidGains(80f, 70f, 1.8f, 0.7f),
                PidPitch = new PidGains(80f, 70f, 1.8f, 0.7f),
                PidYaw = new PidGains(40f, 35f, 0f, 0.5f),
                CameraTiltDeg = 15f,
            };
        }

        /// <summary>
        /// Die Parrot Bebop aus dem Original-KestrelFPV: 400 g, 10 cm Hebelarm, maximal
        /// 14.000 U/min, Schweben bei rund 8.200 U/min. Zum Vergleich mit dem Original.
        /// </summary>
        public static QuadConfig KestrelBebop()
        {
            return new QuadConfig
            {
                Name = "Bebop (KestrelFPV Original)",
                MassKg = 0.40f,
                ArmX = 0.10f,
                ArmZ = 0.10f,
                InertiaRoll = 10e-4f,
                InertiaPitch = 12e-4f,
                InertiaYaw = 15e-4f,
                Cells = 3,
                MotorKv = 1709f,
                PropDiameterInch = 6.0f,
                PropPitchInch = 3.0f,
                ThrustToWeight = 3.0f,
                MotorTauUp = 0.050f,
                MotorTauDown = 0.060f,
                RotorInertia = 5e-6f,
                CapacityMah = 1200f,
                InternalResistanceOhm = 0.060f,
                DragAreaFront = 0.012f,
                DragAreaSide = 0.012f,
                DragAreaTop = 0.025f,
                LinearDrag = 0.14f,
                PidRoll = new PidGains(60f, 40f, 1.8f, 0.6f),
                PidPitch = new PidGains(60f, 40f, 1.8f, 0.6f),
                PidYaw = new PidGains(35f, 25f, 0f, 0.4f),
                CameraTiltDeg = 10f,
            };
        }

        public static QuadConfig[] AllPresets()
        {
            return new[] { Freestyle5(), Racer5(), Cinewhoop3(), KestrelBebop() };
        }
    }
}
