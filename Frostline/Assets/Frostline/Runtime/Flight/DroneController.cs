using Frostline.Controls;
using Frostline.Flight.Core;
using UnityEngine;
using Quat = System.Numerics.Quaternion;
using Vec3 = System.Numerics.Vector3;

namespace Frostline.Flight
{
    /// <summary>
    /// Verbindet den Flugphysik-Kern mit Unitys Rigidbody: liest die Sticks, lässt den Kern
    /// Kräfte ausrechnen und gibt sie an die Physik weiter. Kollisionen macht Unity.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(-50)]
    public sealed class DroneController : MonoBehaviour
    {
        [Tooltip("Woher die Stick-Eingaben kommen. Leer = erstes StickInput in der Szene.")]
        public StickInput input;

        public QuadConfig config = QuadConfig.Freestyle5();
        public RateProfile rates = new RateProfile();

        [Tooltip("Wind in Weltkoordinaten, m/s.")]
        public Vector3 wind;

        [Tooltip("Ab dieser Aufprallgeschwindigkeit (m/s) zählt eine Berührung als Absturz.")]
        public float crashSpeed = 4f;

        Rigidbody body;
        QuadSim sim;
        ControlInput lastInput;
        bool wasArmed;

        public Telemetry Telemetry { get { return Sim.Telemetry; } }
        public ControlInput LastInput { get { return lastInput; } }
        public Rigidbody Body { get { return body; } }
        public float SpeedMetersPerSecond { get { return body != null ? body.linearVelocity.magnitude : 0f; } }

        QuadSim Sim
        {
            get
            {
                if (sim == null) sim = new QuadSim(config, rates);
                return sim;
            }
        }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            if (input == null) input = FindAnyObjectByType<StickInput>();
            ApplySettings();
        }

        /// <summary>Nach Änderungen an <see cref="config"/> oder <see cref="rates"/> aufrufen.</summary>
        public void ApplySettings()
        {
            Sim.SetConfig(config);
            Sim.Rates = rates;

            if (body == null) body = GetComponent<Rigidbody>();
            Vec3 inertia = Sim.InertiaDiagonal;
            body.mass = config.MassKg;
            body.automaticCenterOfMass = false;
            body.centerOfMass = Vector3.zero;
            body.automaticInertiaTensor = false;
            body.inertiaTensor = new Vector3(inertia.X, inertia.Y, inertia.Z);
            body.inertiaTensorRotation = Quaternion.identity;
            // Luftwiderstand rechnet der Kern selbst.
            body.linearDamping = 0f;
            body.angularDamping = 0f;
            body.useGravity = true;
            body.maxAngularVelocity = 70f;
            body.maxLinearVelocity = 200f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        /// <summary>Frischer Akku, Motoren aus, entschärft.</summary>
        public void ResetFlightState()
        {
            Sim.Reset();
            wasArmed = false;
        }

        void OnCollisionEnter(Collision collision)
        {
            // Gates melden sich selbst; hier nur Boden, Wände, Gebäude.
            if (collision.collider.GetComponentInParent<Frostline.Gameplay.RaceGate>() != null)
            {
                FrostlineEvents.RaiseGateTouched();
                return;
            }
            float impact = collision.relativeVelocity.magnitude;
            if (impact >= crashSpeed) FrostlineEvents.RaiseCrashed(impact);
            else if (impact > 0.5f) FrostlineEvents.RaiseGroundTouched();
        }

        void FixedUpdate()
        {
            lastInput = input != null ? input.Read() : default(ControlInput);

            Quaternion rotation = body.rotation;
            Vector3 velocity = body.linearVelocity;
            Vector3 spin = Quaternion.Inverse(rotation) * body.angularVelocity;

            BodyState state;
            state.Rotation = new Quat(rotation.x, rotation.y, rotation.z, rotation.w);
            state.Velocity = new Vec3(velocity.x, velocity.y, velocity.z);
            state.AngularVelocityBody = new Vec3(spin.x, spin.y, spin.z);
            state.Wind = new Vec3(wind.x, wind.y, wind.z);

            FlightOutput output = Sim.Step(lastInput, state, Time.fixedDeltaTime);

            bool armed = Sim.Telemetry.Armed;
            if (armed && !wasArmed) FrostlineEvents.RaiseArmed();
            wasArmed = armed;

            body.AddRelativeForce(new Vector3(output.ForceBody.X, output.ForceBody.Y, output.ForceBody.Z), ForceMode.Force);
            body.AddRelativeTorque(new Vector3(output.TorqueBody.X, output.TorqueBody.Y, output.TorqueBody.Z), ForceMode.Force);
        }
    }
}
