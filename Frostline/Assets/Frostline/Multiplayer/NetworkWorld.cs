using System;
using System.Collections.Generic;
using System.Text;
using Frostline.Gameplay;
using Frostline.View;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Frostline.Multiplayer
{
    /// <summary>
    /// Was alle Spieler gemeinsam haben: Wetter, Tageszeit und Wolken-Seed bestimmt der Host;
    /// Rennen startet der Host für alle mit Countdown und Zeitnahme nach Serveruhr; Rundenzeiten
    /// laufen beim Host zusammen. Dazu der Modus „Fangen“.
    /// </summary>
    public sealed class NetworkWorld : NetworkBehaviour
    {
        public float tagDistance = 2.5f;
        public float tagCooldown = 3f;

        readonly NetworkVariable<int> weather = new NetworkVariable<int>();
        readonly NetworkVariable<float> timeOfDay = new NetworkVariable<float>(10f);
        readonly NetworkVariable<int> cloudSeed = new NetworkVariable<int>(1);
        readonly NetworkVariable<bool> tagMode = new NetworkVariable<bool>();
        readonly NetworkVariable<ulong> tagged = new NetworkVariable<ulong>(ulong.MaxValue);

        readonly Dictionary<string, float> bestLaps = new Dictionary<string, float>();
        string boardTrack = "";
        float tagTimer;
        RaceManager race;
        WeatherSystem weatherSystem;
        Func<double> previousClock;

        public static NetworkWorld Instance { get; private set; }
        public string Leaderboard { get; private set; }
        public bool TagMode { get { return tagMode.Value; } }
        public ulong TaggedClient { get { return tagged.Value; } }
        public string Announcement { get; private set; }
        public float AnnouncementUntil { get; private set; }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            race = FindAnyObjectByType<RaceManager>();
            weatherSystem = WeatherSystem.Instance;

            if (race != null)
            {
                // Countdown und Zeitnahme laufen für alle nach der Serveruhr.
                previousClock = race.Clock;
                race.Clock = () => NetworkManager.ServerTime.Time;
                race.LapReported += OnLocalLap;
            }

            if (IsServer)
            {
                if (weatherSystem != null)
                {
                    PushWeather(weatherSystem.State);
                    weatherSystem.StateChangedLocally += PushWeather;
                }
            }
            else
            {
                weather.OnValueChanged += (a, b) => ApplyWeather();
                timeOfDay.OnValueChanged += (a, b) => ApplyWeather();
                cloudSeed.OnValueChanged += (a, b) => ApplyWeather();
                if (weatherSystem != null) weatherSystem.lockedByHost = true;
                ApplyWeather();
            }
            tagged.OnValueChanged += OnTaggedChanged;
        }

        public override void OnNetworkDespawn()
        {
            if (race != null)
            {
                race.LapReported -= OnLocalLap;
                if (previousClock != null) race.Clock = previousClock;
            }
            if (weatherSystem != null)
            {
                weatherSystem.StateChangedLocally -= PushWeather;
                weatherSystem.lockedByHost = false;
            }
            tagged.OnValueChanged -= OnTaggedChanged;
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------ Wetter

        void PushWeather(WorldState state)
        {
            if (!IsServer) return;
            weather.Value = (int)state.Weather;
            timeOfDay.Value = state.TimeOfDay;
            cloudSeed.Value = state.CloudSeed;
        }

        void ApplyWeather()
        {
            if (weatherSystem == null) return;
            weatherSystem.Apply(new WorldState
            {
                Weather = (Weather)weather.Value,
                TimeOfDay = timeOfDay.Value,
                CloudSeed = cloudSeed.Value,
            });
        }

        // ------------------------------------------------------------ Rennen

        /// <summary>Nur Host: Rennen auf Strecke <paramref name="trackIndex"/> für alle, Start in 5 s.</summary>
        public void StartRaceForAll(int trackIndex)
        {
            if (!IsServer || race == null || trackIndex < 0 || trackIndex >= race.tracks.Count) return;
            bestLaps.Clear();
            boardTrack = race.tracks[trackIndex].trackName;
            StartRaceRpc(trackIndex, NetworkManager.ServerTime.Time + 5.0);
        }

        [Rpc(SendTo.Everyone)]
        void StartRaceRpc(int trackIndex, double startTime)
        {
            if (race == null || trackIndex < 0 || trackIndex >= race.tracks.Count) return;
            race.StartRace(race.tracks[trackIndex], startTime);
            Announce("Rennen: " + race.tracks[trackIndex].trackName);
        }

        void OnLocalLap(RaceTrack track, float lap)
        {
            ReportLapRpc(new FixedString64Bytes(Truncate(MultiplayerSession.PilotName, 28)), new FixedString64Bytes(Truncate(track.trackName, 28)), lap);
        }

        [Rpc(SendTo.Server)]
        void ReportLapRpc(FixedString64Bytes pilot, FixedString64Bytes track, float lap)
        {
            string trackName = track.ToString();
            if (trackName != boardTrack)
            {
                boardTrack = trackName;
                bestLaps.Clear();
            }
            string name = pilot.ToString();
            float best;
            if (!bestLaps.TryGetValue(name, out best) || lap < best) bestLaps[name] = lap;

            var entries = new List<KeyValuePair<string, float>>(bestLaps);
            entries.Sort((a, b) => a.Value.CompareTo(b.Value));
            var text = new StringBuilder(trackName);
            for (int i = 0; i < entries.Count && i < 8; i++)
            {
                text.Append('\n').Append(i + 1).Append(". ").Append(entries[i].Key).Append("  ").Append(RaceManager.FormatTime(entries[i].Value));
            }
            string board = text.ToString();
            if (board.Length > 480) board = board.Substring(0, 480);
            BoardRpc(new FixedString512Bytes(board));
        }

        [Rpc(SendTo.Everyone)]
        void BoardRpc(FixedString512Bytes board)
        {
            Leaderboard = board.ToString();
        }

        // ------------------------------------------------------------ Fangen

        /// <summary>Nur Host: Fangen an/aus. Beim Start ist ein zufälliger Spieler dran.</summary>
        public void ToggleTagMode()
        {
            if (!IsServer) return;
            tagMode.Value = !tagMode.Value;
            if (tagMode.Value)
            {
                IReadOnlyList<ulong> clients = NetworkManager.ConnectedClientsIds;
                tagged.Value = clients.Count > 0 ? clients[UnityEngine.Random.Range(0, clients.Count)] : NetworkManager.LocalClientId;
                tagTimer = tagCooldown;
            }
            else
            {
                tagged.Value = ulong.MaxValue;
            }
        }

        void Update()
        {
            if (!IsSpawned || !IsServer || !tagMode.Value) return;
            tagTimer -= Time.deltaTime;
            if (tagTimer > 0f) return;

            NetworkDrone hunter = null;
            foreach (NetworkDrone d in NetworkDrone.All)
            {
                if (d.OwnerClientId == tagged.Value) hunter = d;
            }
            if (hunter == null) return;

            foreach (NetworkDrone d in NetworkDrone.All)
            {
                if (d == hunter) continue;
                if ((d.transform.position - hunter.transform.position).sqrMagnitude < tagDistance * tagDistance)
                {
                    tagged.Value = d.OwnerClientId;
                    tagTimer = tagCooldown;
                    break;
                }
            }
        }

        void OnTaggedChanged(ulong previous, ulong current)
        {
            if (current == ulong.MaxValue) return;
            string name = "?";
            foreach (NetworkDrone d in NetworkDrone.All)
            {
                if (d.OwnerClientId == current) name = d.PilotName;
            }
            Announce(current == NetworkManager.LocalClientId ? "DU BIST DRAN!" : name + " ist dran");
        }

        void Announce(string text)
        {
            Announcement = text;
            AnnouncementUntil = Time.unscaledTime + 3f;
        }

        static string Truncate(string text, int max)
        {
            return text.Length > max ? text.Substring(0, max) : text;
        }
    }
}
