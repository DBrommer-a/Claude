using System.Collections.Generic;
using Frostline.Flight;
using UnityEngine;

namespace Frostline.Gameplay
{
    /// <summary>
    /// Freestyle: leuchtende Ringe an Landmarken (Funkmast, Wrack, Kran ...). Durchfliegen sammelt
    /// sie ein; der Fortschritt bleibt gespeichert.
    /// </summary>
    public sealed class Collectibles : MonoBehaviour
    {
        public DroneController drone;
        public float pickupRadius = 3f;
        public float spinDegreesPerSecond = 60f;

        const string Key = "Frostline.Collected";

        readonly List<Transform> items = new List<Transform>();
        readonly HashSet<string> collected = new HashSet<string>();
        string message;
        float messageUntil;
        GUIStyle style;

        public int Total { get { return items.Count; } }
        public int CollectedCount { get { return collected.Count; } }

        void Awake()
        {
            if (drone == null) drone = FindFirstObjectByType<DroneController>();
            foreach (Transform child in transform) items.Add(child);
            foreach (string id in PlayerPrefs.GetString(Key, "").Split('|'))
            {
                if (id.Length > 0) collected.Add(id);
            }
            foreach (Transform item in items) item.gameObject.SetActive(!collected.Contains(item.name));
        }

        public void ResetProgress()
        {
            collected.Clear();
            PlayerPrefs.DeleteKey(Key);
            foreach (Transform item in items) item.gameObject.SetActive(true);
        }

        void Update()
        {
            float spin = spinDegreesPerSecond * Time.deltaTime;
            Vector3 p = drone != null ? drone.transform.position : Vector3.zero;
            float r2 = pickupRadius * pickupRadius;
            foreach (Transform item in items)
            {
                if (!item.gameObject.activeSelf) continue;
                item.Rotate(0f, spin, 0f, Space.World);
                if (drone != null && (item.position - p).sqrMagnitude < r2) Collect(item);
            }
        }

        void Collect(Transform item)
        {
            item.gameObject.SetActive(false);
            collected.Add(item.name);
            PlayerPrefs.SetString(Key, string.Join("|", collected));
            PlayerPrefs.Save();
            FrostlineEvents.RaiseGatePassed(-1);
            message = item.name + "   " + collected.Count + " / " + items.Count;
            messageUntil = Time.unscaledTime + 2.5f;
        }

        void OnGUI()
        {
            if (message == null || Time.unscaledTime > messageUntil) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                style.normal.textColor = new Color(1f, 0.85f, 0.3f);
            }
            GUI.Label(new Rect(0f, Screen.height * 0.25f, Screen.width, 40f), message, style);
        }
    }
}
