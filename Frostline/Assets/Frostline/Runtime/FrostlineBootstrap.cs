using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Frostline
{
    /// <summary>
    /// Stellt beim Start die Takte ein, auf die sich die Flugphysik verlässt.
    /// Läuft automatisch, muss an keinem Objekt hängen.
    /// </summary>
    public static class FrostlineBootstrap
    {
        /// <summary>Physik mit 500 Hz, unabhängig von der Bildrate.</summary>
        public const float PhysicsDeltaTime = 0.002f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            Time.fixedDeltaTime = PhysicsDeltaTime;
            // Bricht die Bildrate ein, rechnet Unity höchstens 50 ms Physik pro Bild nach.
            Time.maximumDeltaTime = 0.05f;

            // Eingaben werden zeitlich auf die Physik-Schritte verteilt statt einmal pro Bild
            // gelesen. So kommen 1000 Hz vom Sender auch bei 60 Bildern pro Sekunde fein an.
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInFixedUpdate;
            InputSystem.pollingFrequency = 500f;
        }
    }
}
