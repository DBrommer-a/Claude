using UnityEngine;

namespace Frostline.View
{
    /// <summary>Verfolgerkamera für Replays: hinterher, kreisend oder fest.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class ChaseCamera : MonoBehaviour
    {
        public enum Mode { Chase, Orbit, Fixed }

        public Transform target;
        public Mode mode = Mode.Chase;
        public float distance = 2.2f;
        public float height = 0.7f;
        public float smoothing = 6f;
        public float orbitSpeed = 20f;

        float orbitAngle;
        Vector3 fixedPosition;

        public void NextMode()
        {
            mode = (Mode)(((int)mode + 1) % 3);
            if (mode == Mode.Fixed && target != null) fixedPosition = transform.position;
        }

        public void SnapToTarget()
        {
            if (target == null) return;
            transform.position = DesiredPosition();
            transform.rotation = Quaternion.LookRotation(target.position - transform.position, Vector3.up);
        }

        Vector3 DesiredPosition()
        {
            switch (mode)
            {
                case Mode.Orbit:
                    Vector3 around = Quaternion.Euler(0f, orbitAngle, 0f) * Vector3.back * (distance * 2.5f);
                    return target.position + around + Vector3.up * height * 2f;
                case Mode.Fixed:
                    return fixedPosition;
                default:
                    // Hinter der Flugrichtung, nicht hinter der Nase: bei Flips bleibt das Bild ruhig.
                    Vector3 back = -Vector3.ProjectOnPlane(target.forward, Vector3.up);
                    if (back.sqrMagnitude < 1e-3f) back = Vector3.back;
                    return target.position + back.normalized * distance + Vector3.up * height;
            }
        }

        void LateUpdate()
        {
            if (target == null) return;
            orbitAngle += orbitSpeed * Time.unscaledDeltaTime;
            Vector3 desired = DesiredPosition();
            float blend = 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);
            transform.position = mode == Mode.Fixed ? desired : Vector3.Lerp(transform.position, desired, blend);
            Vector3 toTarget = target.position - transform.position;
            if (toTarget.sqrMagnitude < 1e-6f) return;
            Quaternion look = Quaternion.LookRotation(toTarget, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, blend);
        }
    }
}
