using System;
using Frostline.Flight.Core;
using UnityEngine;

namespace Frostline.Flight
{
    /// <summary>Alles, was der Spieler im Menü einstellt. Wird zwischen den Sitzungen gespeichert.</summary>
    [Serializable]
    public sealed class PilotSettings
    {
        public QuadConfig config = QuadConfig.Freestyle5();
        public RateProfile rates = new RateProfile();
        public float fieldOfView = 90f;
        public float motorVolume = 0.5f;
        public bool showOsd = true;

        const string Key = "Frostline.Pilot";

        public static PilotSettings Load()
        {
            string json = PlayerPrefs.GetString(Key, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    PilotSettings loaded = JsonUtility.FromJson<PilotSettings>(json);
                    if (loaded != null && loaded.config != null && loaded.rates != null) return loaded;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Frostline: Einstellungen unlesbar, nehme Standard. " + e.Message);
                }
            }
            return new PilotSettings();
        }

        public void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }
    }
}
