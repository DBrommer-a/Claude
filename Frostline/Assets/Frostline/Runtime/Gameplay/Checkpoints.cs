using System.Collections.Generic;
using Frostline.Flight;
using UnityEngine;

namespace Frostline.Gameplay
{
    /// <summary>
    /// Checkpoints über die Insel verteilt. Wer in die Nähe fliegt, startet nach Absturz oder
    /// Signalverlust von dort neu statt vom Strand.
    /// </summary>
    public sealed class Checkpoints : MonoBehaviour
    {
        public DroneController drone;
        public float radius = 40f;

        readonly List<Transform> points = new List<Transform>();
        Transform current;
        string message;
        float messageUntil;
        GUIStyle style;

        public static Checkpoints Instance { get; private set; }
        public Transform Current { get { return current; } }

        void Awake()
        {
            Instance = this;
            if (drone == null) drone = FindFirstObjectByType<DroneController>();
            foreach (Transform child in transform) points.Add(child);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Clear()
        {
            current = null;
        }

        void Update()
        {
            if (drone == null) return;
            Vector3 p = drone.transform.position;
            float r2 = radius * radius;
            foreach (Transform point in points)
            {
                if (point == current) continue;
                if ((point.position - p).sqrMagnitude < r2)
                {
                    current = point;
                    message = "Checkpoint: " + point.name;
                    messageUntil = Time.unscaledTime + 2f;
                }
            }
        }

        void OnGUI()
        {
            if (message == null || Time.unscaledTime > messageUntil) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                style.normal.textColor = new Color(0.6f, 1f, 0.7f);
            }
            GUI.Label(new Rect(0f, Screen.height * 0.18f, Screen.width, 30f), message, style);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.6f);
            foreach (Transform child in transform) Gizmos.DrawWireSphere(child.position, radius);
        }
    }
}
