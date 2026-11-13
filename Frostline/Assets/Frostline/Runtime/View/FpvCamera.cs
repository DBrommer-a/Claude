using Frostline.Flight;
using UnityEngine;

namespace Frostline.View
{
    /// <summary>
    /// FPV-Kamera an der Drohne. Der Kamerawinkel (Uptilt) zeigt nach oben, damit man im
    /// schnellen Vorwärtsflug trotz geneigter Drohne nach vorn schaut.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class FpvCamera : MonoBehaviour
    {
        public DroneController drone;

        [Range(0f, 70f)] public float tiltDegrees = 30f;
        [Tooltip("Vertikaler Blickwinkel in Grad.")]
        [Range(50f, 130f)] public float fieldOfView = 90f;
        [Tooltip("true: Winkel kommt aus dem Drohnen-Preset.")]
        public bool tiltFromDrone = true;

        Camera cam;

        void Awake()
        {
            cam = GetComponent<Camera>();
            if (drone == null) drone = GetComponentInParent<DroneController>();
            Apply();
        }

        public void Apply()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (tiltFromDrone && drone != null) tiltDegrees = drone.config.CameraTiltDeg;
            transform.localRotation = Quaternion.Euler(-tiltDegrees, 0f, 0f);
            cam.fieldOfView = fieldOfView;
        }

        void OnValidate()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam != null) cam.fieldOfView = fieldOfView;
            transform.localRotation = Quaternion.Euler(-tiltDegrees, 0f, 0f);
        }
    }
}
