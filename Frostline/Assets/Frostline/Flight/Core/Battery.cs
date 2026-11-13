using System;

namespace Frostline.Flight.Core
{
    /// <summary>
    /// LiPo-Modell: Ruhespannung nach Ladezustand, Einbruch unter Last über den Innenwiderstand.
    /// Dadurch wird ein Punch-out mit fast leerem Akku spürbar schwächer.
    /// </summary>
    public sealed class Battery
    {
        // Ruhespannung einer Zelle über dem Ladezustand (0 %, 10 %, ... 100 %).
        static readonly float[] OpenCircuitVoltage =
        {
            3.27f, 3.69f, 3.73f, 3.77f, 3.80f, 3.84f, 3.87f, 3.95f, 4.02f, 4.11f, 4.20f,
        };

        const float SagFilterHz = 8f;
        const float MinCellVoltage = 2.5f;

        int cells;
        float capacityMah;
        float resistance;
        bool infinite;
        Pt1Filter sagFilter;

        public float UsedMah { get; private set; }
        public float Voltage { get; private set; }
        public float CurrentAmps { get; private set; }

        public float StateOfCharge
        {
            get { return capacityMah <= 0f ? 1f : MathUtil.Clamp01(1f - UsedMah / capacityMah); }
        }

        public float CellVoltage { get { return Voltage / Math.Max(1, cells); } }

        public void Configure(QuadConfig cfg)
        {
            cells = Math.Max(1, cfg.Cells);
            capacityMah = cfg.CapacityMah;
            resistance = cfg.InternalResistanceOhm;
            infinite = cfg.InfiniteBattery;
            if (Voltage <= 0f) Reset();
        }

        public void Reset()
        {
            UsedMah = 0f;
            CurrentAmps = 0f;
            Voltage = cells * RestingCellVoltage(1f);
            sagFilter.Reset(Voltage);
        }

        public static float RestingCellVoltage(float stateOfCharge)
        {
            float x = MathUtil.Clamp01(stateOfCharge) * (OpenCircuitVoltage.Length - 1);
            int i = (int)x;
            if (i >= OpenCircuitVoltage.Length - 1) return OpenCircuitVoltage[OpenCircuitVoltage.Length - 1];
            return MathUtil.Lerp(OpenCircuitVoltage[i], OpenCircuitVoltage[i + 1], x - i);
        }

        /// <summary>Verbucht den Strom dieses Schritts und liefert die Spannung für den nächsten.</summary>
        public void Step(float currentAmps, float dt)
        {
            CurrentAmps = currentAmps;
            if (!infinite)
            {
                UsedMah += currentAmps * dt * (1000f / 3600f);
            }

            float resting = cells * RestingCellVoltage(StateOfCharge);
            float loaded = resting - currentAmps * resistance;
            float floor = cells * MinCellVoltage;
            if (loaded < floor) loaded = floor;
            Voltage = sagFilter.Apply(loaded, SagFilterHz, dt);
        }
    }
}
