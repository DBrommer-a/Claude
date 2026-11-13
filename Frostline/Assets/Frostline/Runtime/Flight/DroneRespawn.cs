using Frostline.Controls;
using UnityEngine;

namespace Frostline.Flight
{
    /// <summary>Setzt die Drohne auf Knopfdruck an den Startpunkt zurück.</summary>
    [RequireComponent(typeof(DroneController))]
    public sealed class DroneRespawn : MonoBehaviour
    {
        [Tooltip("Leer = Position beim Start der Szene.")]
        public Transform spawnPoint;

        [Tooltip("Fällt die Drohne unter diese Höhe (z. B. ins Meer), wird sie zurückgesetzt.")]
        public float killHeight = -5f;

        DroneController drone;
        Vector3 startPosition;
        Quaternion startRotation;

        void Awake()
        {
            drone = GetComponent<DroneController>();
            startPosition = transform.position;
            startRotation = transform.rotation;
        }

        void FixedUpdate()
        {
            StickInput input = drone.input;
            bool requested = input != null && input.ConsumeResetRequest();
            if (requested || drone.Body.position.y < killHeight)
            {
                Respawn();
            }
        }

        public void Respawn()
        {
            // Im Rennen geht es am zuletzt durchflogenen Gate weiter.
            Frostline.Gameplay.RaceManager race = Frostline.Gameplay.RaceManager.Instance;
            if (race != null && race.TryRespawnAtLastGate())
            {
                drone.ResetFlightState();
                FrostlineEvents.RaiseRespawned();
                return;
            }

            Transform checkpoint = Frostline.Gameplay.Checkpoints.Instance != null ? Frostline.Gameplay.Checkpoints.Instance.Current : null;
            Transform target = checkpoint != null ? checkpoint : spawnPoint;
            Vector3 position = target != null ? target.position : startPosition;
            Quaternion rotation = target != null ? Quaternion.Euler(0f, target.eulerAngles.y, 0f) : startRotation;

            Rigidbody body = drone.Body;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            drone.ResetFlightState();
            FrostlineEvents.RaiseRespawned();
        }
    }
}
