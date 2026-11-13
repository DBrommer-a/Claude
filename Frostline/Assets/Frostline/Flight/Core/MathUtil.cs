using System;

namespace Frostline.Flight.Core
{
    public static class MathUtil
    {
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public const float Rad2Deg = (float)(180.0 / Math.PI);

        public static float Clamp(float v, float min, float max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        public static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        /// <summary>Totzone um die Mitte, danach wieder auf den vollen Bereich gestreckt.</summary>
        public static float ApplyDeadband(float v, float deadband)
        {
            float a = Math.Abs(v);
            if (a <= deadband) return 0f;
            if (deadband >= 0.999f) return 0f;
            return Math.Sign(v) * (a - deadband) / (1f - deadband);
        }
    }

    /// <summary>Tiefpass 1. Ordnung.</summary>
    public struct Pt1Filter
    {
        float state;
        bool initialized;

        public float Value { get { return state; } }

        public void Reset(float value)
        {
            state = value;
            initialized = true;
        }

        public void Clear()
        {
            state = 0f;
            initialized = false;
        }

        public float Apply(float input, float cutoffHz, float dt)
        {
            if (!initialized || cutoffHz <= 0f)
            {
                state = input;
                initialized = true;
                return state;
            }
            float rc = 1f / (2f * (float)Math.PI * cutoffHz);
            state += dt / (rc + dt) * (input - state);
            return state;
        }
    }

    /// <summary>Tiefpass 2. Ordnung aus zwei PT1-Stufen; cutoffHz ist die -3-dB-Frequenz des Gesamtfilters.</summary>
    public struct Pt2Filter
    {
        const float CutoffCorrection = 1.553774f; // 1 / sqrt(sqrt(2) - 1)
        Pt1Filter a;
        Pt1Filter b;

        public float Value { get { return b.Value; } }

        public void Reset(float value)
        {
            a.Reset(value);
            b.Reset(value);
        }

        public void Clear()
        {
            a.Clear();
            b.Clear();
        }

        public float Apply(float input, float cutoffHz, float dt)
        {
            float stage = cutoffHz * CutoffCorrection;
            return b.Apply(a.Apply(input, stage, dt), stage, dt);
        }
    }
}
