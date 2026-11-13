using System.Collections.Generic;
using UnityEngine;

namespace Frostline.Gameplay
{
    /// <summary>Eine Strecke: Gates in Reihenfolge der Kinder, Startpunkt, Rundenzahl.</summary>
    public sealed class RaceTrack : MonoBehaviour
    {
        public string trackName = "Strecke";
        [Range(1, 10)] public int laps = 2;
        [Tooltip("Hier steht die Drohne beim Start, Blick in Richtung erstes Gate.")]
        public Transform startPoint;

        readonly List<RaceGate> gates = new List<RaceGate>();

        public IReadOnlyList<RaceGate> Gates
        {
            get
            {
                if (gates.Count == 0) Collect();
                return gates;
            }
        }

        public string BestLapKey { get { return "Frostline.BestLap." + trackName; } }

        public float BestLap
        {
            get { return PlayerPrefs.GetFloat(BestLapKey, 0f); }
            set
            {
                PlayerPrefs.SetFloat(BestLapKey, value);
                PlayerPrefs.Save();
            }
        }

        void Awake()
        {
            Collect();
        }

        void Collect()
        {
            gates.Clear();
            // Reihenfolge = Reihenfolge in der Hierarchie, so lässt sie sich im Editor einfach ändern.
            foreach (Transform child in transform)
            {
                RaceGate gate = child.GetComponent<RaceGate>();
                if (gate != null) gates.Add(gate);
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
            Vector3 previous = startPoint != null ? startPoint.position : transform.position;
            foreach (Transform child in transform)
            {
                RaceGate gate = child.GetComponent<RaceGate>();
                if (gate == null) continue;
                Vector3 p = gate.OpeningCenter;
                Gizmos.DrawLine(previous, p);
                previous = p;
            }
        }
    }
}
