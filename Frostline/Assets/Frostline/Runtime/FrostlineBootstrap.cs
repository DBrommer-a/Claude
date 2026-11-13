using UnityEngine;
using UnityEngine.InputSystem;

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
            // Auf einer Kopie, damit im Editor kein Einstellungs-Asset des Projekts verändert wird.
            InputSettings current = InputSystem.settings;
            if (current.updateMode != InputSettings.UpdateMode.ProcessEventsInFixedUpdate)
            {
                InputSettings copy = Object.Instantiate(current);
                copy.name = "Frostline Input Settings";
                copy.hideFlags = HideFlags.HideAndDontSave;
                copy.updateMode = InputSettings.UpdateMode.ProcessEventsInFixedUpdate;
                InputSystem.settings = copy;
            }
            InputSystem.pollingFrequency = 500f;
        }
    }
}
