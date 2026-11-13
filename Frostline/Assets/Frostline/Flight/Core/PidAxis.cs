using System;

namespace Frostline.Flight.Core
{
    /// <summary>
    /// Regler-Verstärkungen in physikalischen Einheiten. Der Ausgang des Reglers ist eine
    /// gewünschte Winkelbeschleunigung (rad/s²); dadurch passen dieselben Werte für leichte
    /// und schwere Drohnen.
    /// </summary>
    [Serializable]
    public struct PidGains
    {
        /// <summary>1/s: Winkelbeschleunigung pro Ratenfehler.</summary>
        public float P;
        /// <summary>1/s².</summary>
        public float I;
        /// <summary>Dimensionslos: Dämpfung auf die gemessene Winkelbeschleunigung.</summary>
        public float D;
        /// <summary>Dimensionslos: Vorsteuerung aus der Änderung des Sollwerts.</summary>
        public float FF;

        public PidGains(float p, float i, float d, float ff)
        {
            P = p;
            I = i;
            D = d;
            FF = ff;
        }
    }

    /// <summary>Raten-PID für eine Achse. Einheiten: rad/s rein, rad/s² raus.</summary>
    public sealed class PidAxis
    {
        /// <summary>Begrenzung des I-Anteils in rad/s².</summary>
        public float IntegralLimit = 60f;
        /// <summary>Tiefpass auf dem D-Anteil.</summary>
        public float DTermCutoffHz = 110f;
        /// <summary>I-Term-Relax: Oberhalb dieser Sollwert-Bewegung (rad/s) wird nicht mehr integriert.</summary>
        public float RelaxThreshold = 40f * MathUtil.Deg2Rad;
        public float RelaxCutoffHz = 15f;

        float integral;
        float previousMeasurement;
        float previousSetpoint;
        bool hasPrevious;
        Pt1Filter dFilter;
        Pt1Filter relaxFilter;

        public float Integral { get { return integral; } }

        public void Reset()
        {
            integral = 0f;
            hasPrevious = false;
            dFilter.Clear();
            relaxFilter.Clear();
        }

        public void ResetIntegral()
        {
            integral = 0f;
        }

        public float Update(float setpoint, float measurement, float dt, in PidGains gains, bool freezeIntegral)
        {
            float error = setpoint - measurement;

            // I-Term-Relax: bei schnellen Stickbewegungen nicht aufintegrieren,
            // sonst federt die Drohne nach einem Flip zurück.
            float slowSetpoint = relaxFilter.Apply(setpoint, RelaxCutoffHz, dt);
            float relax = 1f - Math.Abs(setpoint - slowSetpoint) / RelaxThreshold;
            if (relax < 0f) relax = 0f;

            if (!freezeIntegral)
            {
                integral += gains.I * error * relax * dt;
                integral = MathUtil.Clamp(integral, -IntegralLimit, IntegralLimit);
            }

            float dTerm = 0f;
            float ffTerm = 0f;
            if (hasPrevious && dt > 0f)
            {
                float measuredAccel = (measurement - previousMeasurement) / dt;
                dTerm = -gains.D * dFilter.Apply(measuredAccel, DTermCutoffHz, dt);
                ffTerm = gains.FF * (setpoint - previousSetpoint) / dt;
            }

            previousMeasurement = measurement;
            previousSetpoint = setpoint;
            hasPrevious = true;

            return gains.P * error + integral + dTerm + ffTerm;
        }
    }
}
