using System.Collections.Generic;
using Frostline.Audio;
using Frostline.Flight;
using Frostline.Gameplay;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Frostline.Multiplayer
{
    /// <summary>
    /// Die Drohne eines Mitspielers. Jeder fliegt seine eigene Drohne lokal (keine
    /// Eingabeverzögerung) und schickt 30-mal pro Sekunde Position, Drehung, Geschwindigkeit
    /// und Gas. Fremde Drohnen werden mit rund 100 ms Verzögerung weich abgespielt; sie haben
    /// keine Physik und keine Kollision („Geister“), damit Netzwerk-Lag nie unfair wirkt.
    /// </summary>
    public sealed class NetworkDrone : NetworkBehaviour
    {
        public float sendRateHz = 30f;
        public Transform model;
        public MotorAudio motorAudio;

        static readonly List<NetworkDrone> all = new List<NetworkDrone>();
        public static IReadOnlyList<NetworkDrone> All { get { return all; } }

        readonly NetworkVariable<FixedString32Bytes> pilotName = new NetworkVariable<FixedString32Bytes>(
            default(FixedString32Bytes), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        readonly SnapshotInterpolator interpolator = new SnapshotInterpolator();
        DroneController local;
        float nextSend;

        public string PilotName { get { return pilotName.Value.ToString(); } }

        public override void OnNetworkSpawn()
        {
            all.Add(this);
            if (IsOwner)
            {
                local = FindAnyObjectByType<DroneController>();
                string name = MultiplayerSession.PilotName;
                if (name.Length > 28) name = name.Substring(0, 28);
                pilotName.Value = new FixedString32Bytes(name);
            }
            // Die eigene Netzwerk-Drohne bleibt unsichtbar; man sieht seine echte Drohne.
            SetVisible(!IsOwner);
        }

        public override void OnNetworkDespawn()
        {
            all.Remove(this);
        }

        void SetVisible(bool visible)
        {
            if (model != null) model.gameObject.SetActive(visible);
            if (motorAudio != null) motorAudio.enabled = visible;
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner) UpdateOwner();
            else UpdateRemote();
        }

        void UpdateOwner()
        {
            if (local == null) local = FindAnyObjectByType<DroneController>();
            if (local == null) return;

            Transform t = local.transform;
            transform.SetPositionAndRotation(t.position, t.rotation);

            if (Time.unscaledTime < nextSend) return;
            nextSend = Time.unscaledTime + 1f / Mathf.Max(1f, sendRateHz);
            Vector3 velocity = local.Body != null ? local.Body.linearVelocity : Vector3.zero;
            StateRpc(NetworkManager.ServerTime.Time, t.position, t.rotation, velocity, local.LastInput.Throttle);
        }

        void UpdateRemote()
        {
            DroneSnapshot s;
            if (!interpolator.TrySample(NetworkManager.ServerTime.Time, out s)) return;
            transform.SetPositionAndRotation(s.Position.ToUnity(), s.Rotation.ToUnity());
            // Motorsound folgt dem Gaswert.
            if (motorAudio != null) motorAudio.overrideLoad = 0.25f + 0.75f * Mathf.Clamp01(s.Throttle);
        }

        [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
        void StateRpc(double time, Vector3 position, Quaternion rotation, Vector3 velocity, float throttle)
        {
            interpolator.Add(new DroneSnapshot
            {
                Time = time,
                Position = position.ToNumerics(),
                Rotation = rotation.ToNumerics(),
                Velocity = velocity.ToNumerics(),
                Throttle = throttle,
            });
        }
    }
}
