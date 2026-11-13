using System;

namespace Frostline.Flight.Core
{
    public enum RateType
    {
        Betaflight = 0,
        Actual = 1,
    }

    /// <summary>Betaflight-Rates einer Achse, Werte wie im Configurator (z. B. 1.00 / 0.70 / 0.00).</summary>
    [Serializable]
    public struct BetaflightRate
    {
        public float RcRate;
        public float SuperRate;
        public float Expo;

        public BetaflightRate(float rcRate, float superRate, float expo)
        {
            RcRate = rcRate;
            SuperRate = superRate;
            Expo = expo;
        }
    }

    /// <summary>Actual-Rates einer Achse: Mitten-Empfindlichkeit und Maximalrate in Grad/s, Expo 0..1.</summary>
    [Serializable]
    public struct ActualRate
    {
        public float CenterSensitivity;
        public float MaxRate;
        public float Expo;

        public ActualRate(float centerSensitivity, float maxRate, float expo)
        {
            CenterSensitivity = centerSensitivity;
            MaxRate = maxRate;
            Expo = expo;
        }
    }

    [Serializable]
    public sealed class RateProfile
    {
        public RateType Type = RateType.Betaflight;

        public BetaflightRate BetaflightRoll = new BetaflightRate(1.0f, 0.70f, 0f);
        public BetaflightRate BetaflightPitch = new BetaflightRate(1.0f, 0.70f, 0f);
        public BetaflightRate BetaflightYaw = new BetaflightRate(1.0f, 0.70f, 0f);

        public ActualRate ActualRoll = new ActualRate(70f, 670f, 0f);
        public ActualRate ActualPitch = new ActualRate(70f, 670f, 0f);
        public ActualRate ActualYaw = new ActualRate(70f, 670f, 0f);

        /// <summary>Stick (-1..1) -> Soll-Drehrate in Grad/s. axis: 0 Roll, 1 Pitch, 2 Yaw.</summary>
        public float Evaluate(int axis, float stick)
        {
            if (Type == RateType.Actual)
            {
                ActualRate r = axis == 0 ? ActualRoll : (axis == 1 ? ActualPitch : ActualYaw);
                return Rates.Actual(stick, r.CenterSensitivity, r.MaxRate, r.Expo);
            }
            BetaflightRate b = axis == 0 ? BetaflightRoll : (axis == 1 ? BetaflightPitch : BetaflightYaw);
            return Rates.Betaflight(stick, b.RcRate, b.SuperRate, b.Expo);
        }

        public float MaxRate(int axis)
        {
            return Math.Abs(Evaluate(axis, 1f));
        }

        public RateProfile Clone()
        {
            return (RateProfile)MemberwiseClone();
        }
    }

    /// <summary>
    /// Die beiden gängigen Rate-Kurven als Formel. Eigene Implementierung der öffentlich
    /// dokumentierten Kurven; es wurde kein Betaflight-Quelltext übernommen.
    /// </summary>
    public static class Rates
    {
        /// <summary>Höchste Drehrate, die überhaupt herauskommen darf (Grad/s).</summary>
        public const float RateLimit = 1998f;

        public static float Betaflight(float stick, float rcRate, float superRate, float expo)
        {
            stick = MathUtil.Clamp(stick, -1f, 1f);
            float abs = Math.Abs(stick);

            if (expo > 0f)
            {
                stick = stick * abs * abs * abs * expo + stick * (1f - expo);
            }

            if (rcRate > 2f)
            {
                rcRate += 14.54f * (rcRate - 2f);
            }

            float rate = 200f * rcRate * stick;
            if (superRate > 0f)
            {
                float factor = 1f / MathUtil.Clamp(1f - abs * superRate, 0.01f, 1f);
                rate *= factor;
            }
            return MathUtil.Clamp(rate, -RateLimit, RateLimit);
        }

        public static float Actual(float stick, float centerSensitivity, float maxRate, float expo)
        {
            stick = MathUtil.Clamp(stick, -1f, 1f);
            float abs = Math.Abs(stick);
            float p5 = stick * stick * stick * stick * stick;
            float curve = abs * (p5 * expo + stick * (1f - expo));
            float stickMovement = Math.Max(0f, maxRate - centerSensitivity);
            float rate = stick * centerSensitivity + stickMovement * curve;
            return MathUtil.Clamp(rate, -RateLimit, RateLimit);
        }
    }
}
