using System;

namespace Frostline.Gameplay
{
    /// <summary>
    /// Analoges Videosignal am Kartenrand: Je weiter hinaus, desto stärker rauscht das Bild,
    /// bis das Signal abreißt. Die Grenze ist ein Kreis um die Inselmitte; Höhe zählt mit,
    /// damit man nicht über die Wolken hinaus entkommt.
    /// </summary>
    public sealed class SignalModel
    {
        public float CenterX;
        public float CenterZ;
        /// <summary>Bis hier ist das Bild sauber.</summary>
        public float CleanRadius = 2300f;
        /// <summary>Hier reißt das Signal ab.</summary>
        public float LostRadius = 2900f;
        public float CleanAltitude = 2200f;
        public float LostAltitude = 3200f;
        /// <summary>So lange ohne Signal, dann Neustart am letzten Checkpoint.</summary>
        public float LostSeconds = 1.5f;

        float lostTimer;

        /// <summary>0 = sauber, 1 = nur noch Rauschen.</summary>
        public float Noise { get; private set; }
        public bool Lost { get; private set; }

        /// <summary>Aktualisiert den Zustand. true = Signal gerade endgültig verloren, Drohne neu starten.</summary>
        public bool Update(float x, float y, float z, float deltaTime)
        {
            float dx = x - CenterX;
            float dz = z - CenterZ;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);

            float horizontal = Ramp(distance, CleanRadius, LostRadius);
            float vertical = Ramp(y, CleanAltitude, LostAltitude);
            float strength = Math.Max(horizontal, vertical);
            // Rauschen setzt erst leicht, dann immer schneller ein, wie bei echtem 5,8-GHz-Video.
            Noise = strength * strength;

            if (strength >= 1f)
            {
                lostTimer += deltaTime;
                if (!Lost && lostTimer >= LostSeconds)
                {
                    Lost = true;
                    return true;
                }
            }
            else
            {
                lostTimer = 0f;
                Lost = false;
            }
            return false;
        }

        public void Reset()
        {
            lostTimer = 0f;
            Lost = false;
            Noise = 0f;
        }

        static float Ramp(float value, float start, float end)
        {
            if (value <= start) return 0f;
            if (value >= end) return 1f;
            return (value - start) / (end - start);
        }
    }
}
