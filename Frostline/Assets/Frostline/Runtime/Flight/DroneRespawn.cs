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
            Vector3 position = spawnPoint != null ? spawnPoint.position : startPosition;
            Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : startRotation;

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
        }
    }
}
