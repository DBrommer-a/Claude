using UnityEngine;

namespace Frostline.Gameplay
{
    /// <summary>Halbtransparente Drohne, die die beste Runde nachfliegt.</summary>
    public sealed class GhostDrone : MonoBehaviour
    {
        ReplayBuffer recording;
        float startTime;
        bool playing;

        public bool HasRecording { get { return recording != null && recording.Count > 1; } }

        /// <summary>Lädt eine Runde; abgespielt wird erst mit <see cref="Restart"/> beim Rundenstart.</summary>
        public void Load(ReplayBuffer buffer)
        {
            recording = buffer;
            Stop();
        }

        public void Restart()
        {
            if (!HasRecording) return;
            startTime = Time.time;
            playing = true;
            gameObject.SetActive(true);
        }

        public void Stop()
        {
            playing = false;
            gameObject.SetActive(false);
        }

        void Update()
        {
            if (!playing || recording == null) return;
            float t = Time.time - startTime;
            if (t > recording.EndTime)
            {
                playing = false;
                gameObject.SetActive(false);
                return;
            }
            ReplayFrame frame = recording.Sample(t);
            transform.SetPositionAndRotation(frame.Position.ToUnity(), frame.Rotation.ToUnity());
        }
    }
}
