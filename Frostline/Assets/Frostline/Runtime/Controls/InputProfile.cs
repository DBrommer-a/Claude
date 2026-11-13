using System;
using Frostline.Flight.Core;
using UnityEngine;

namespace Frostline.Controls
{
    /// <summary>Ein Stick-Kanal: welche Achse des Geräts und ihre gemessenen Endpunkte.</summary>
    [Serializable]
    public sealed class AxisBinding
    {
        public string path = "";
        /// <summary>Rohwert bei Stick links / hinten / unten.</summary>
        public float low = -1f;
        public float center = 0f;
        /// <summary>Rohwert bei Stick rechts / vorn / oben.</summary>
        public float high = 1f;

        public bool IsAssigned { get { return !string.IsNullOrEmpty(path); } }

        /// <summary>Rohwert -> -1..+1 mit der Mitte bei 0.</summary>
        public float MapCentered(float raw)
        {
            float offset = raw - center;
            float highSpan = high - center;
            float lowSpan = low - center;
            bool towardHigh = Mathf.Abs(highSpan) > 1e-5f && Mathf.Sign(offset) == Mathf.Sign(highSpan);
            if (towardHigh) return Mathf.Clamp01(offset / highSpan);
            if (Mathf.Abs(lowSpan) > 1e-5f) return -Mathf.Clamp01(offset / lowSpan);
            return 0f;
        }

        /// <summary>Rohwert -> 0..1 vom unteren zum oberen Endpunkt (Gas).</summary>
        public float MapThrottle(float raw)
        {
            float span = high - low;
            if (Mathf.Abs(span) < 1e-5f) return 0f;
            return Mathf.Clamp01((raw - low) / span);
        }
    }

    /// <summary>Ein Schalter. Funktioniert für Tasten (0/1) und für Achsen (z. B. Kanal 5 am Sender).</summary>
    [Serializable]
    public sealed class SwitchBinding
    {
        public string path = "";
        public float offValue = 0f;
        public float onValue = 1f;

        public bool IsAssigned { get { return !string.IsNullOrEmpty(path); } }

        public bool IsOn(float raw)
        {
            return Mathf.Abs(raw - onValue) < Mathf.Abs(raw - offValue);
        }
    }

    /// <summary>Schalter mit bis zu drei Stellungen für den Flugmodus.</summary>
    [Serializable]
    public sealed class ModeBinding
    {
        public string path = "";
        public float acroValue = -1f;
        public float angleValue = 0f;
        public float horizonValue = 1f;
        public bool hasHorizon = false;

        public bool IsAssigned { get { return !string.IsNullOrEmpty(path); } }

        public FlightMode Resolve(float raw)
        {
            float toAcro = Mathf.Abs(raw - acroValue);
            float toAngle = Mathf.Abs(raw - angleValue);
            float toHorizon = hasHorizon ? Mathf.Abs(raw - horizonValue) : float.MaxValue;
            if (toAcro <= toAngle && toAcro <= toHorizon) return FlightMode.Acro;
            return toAngle <= toHorizon ? FlightMode.Angle : FlightMode.Horizon;
        }
    }

    /// <summary>Kalibrierung und Kanalzuordnung für ein Gerät; wird pro Gerät gespeichert.</summary>
    [Serializable]
    public sealed class InputProfile
    {
        public string deviceName = "";
        public bool calibrated = false;

        public AxisBinding roll = new AxisBinding();
        public AxisBinding pitch = new AxisBinding();
        public AxisBinding yaw = new AxisBinding();
        public AxisBinding throttle = new AxisBinding();

        public SwitchBinding arm = new SwitchBinding();
        public SwitchBinding turtle = new SwitchBinding();
        public SwitchBinding reset = new SwitchBinding();
        public ModeBinding mode = new ModeBinding();

        /// <summary>Totzone um die Stickmitte. Echte Sender brauchen fast keine.</summary>
        public float deadband = 0.005f;

        const string KeyPrefix = "Frostline.Input.";

        public static InputProfile Load(string deviceName)
        {
            string json = PlayerPrefs.GetString(KeyPrefix + deviceName, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    InputProfile loaded = JsonUtility.FromJson<InputProfile>(json);
                    if (loaded != null) return loaded;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Frostline: Sender-Profil unlesbar, nehme Standard. " + e.Message);
                }
            }
            return new InputProfile { deviceName = deviceName };
        }

        public void Save()
        {
            PlayerPrefs.SetString(KeyPrefix + deviceName, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }
    }
}
