using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Frostline.Multiplayer
{
    /// <summary>
    /// Raum hosten oder per Code beitreten. Läuft über Unity Relay und Lobby („Sessions“ im
    /// Multiplayer-Services-Paket): kein eigener Server, der Host-Mac ist der Server.
    /// Ein Spieler hostet, bis zu 7 weitere treten mit dem Raum-Code bei.
    /// </summary>
    public sealed class MultiplayerSession : MonoBehaviour
    {
        public const int MaxPlayers = 8;
        const string NameKey = "Frostline.PilotName";

        public enum Status { Offline, Connecting, Host, Client, Failed }

        ISession session;

        public static MultiplayerSession Instance { get; private set; }
        public Status Current { get; private set; }
        public string Code { get; private set; }
        public string Error { get; private set; }
        public int PlayerCount { get { return session != null ? session.PlayerCount : 0; } }
        public bool InSession { get { return Current == Status.Host || Current == Status.Client; } }

        public static string PilotName
        {
            get
            {
                string name = PlayerPrefs.GetString(NameKey, "");
                if (string.IsNullOrEmpty(name))
                {
                    name = "Pilot " + UnityEngine.Random.Range(100, 999);
                    PlayerPrefs.SetString(NameKey, name);
                }
                return name;
            }
            set
            {
                string clean = (value ?? "").Trim();
                if (clean.Length > 24) clean = clean.Substring(0, 24);
                if (clean.Length > 0) PlayerPrefs.SetString(NameKey, clean);
            }
        }

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        static async Task EnsureSignedIn()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        public async void Host()
        {
            if (Current == Status.Connecting || InSession) return;
            Current = Status.Connecting;
            Error = null;
            try
            {
                await EnsureSignedIn();
                SessionOptions options = new SessionOptions
                {
                    Name = PilotName + " – Frostline",
                    MaxPlayers = MaxPlayers,
                    // Nur per Code erreichbar, nicht öffentlich gelistet.
                    IsPrivate = true,
                }.WithRelayNetwork();
                session = await MultiplayerService.Instance.CreateSessionAsync(options);
                Attach();
                Code = session.Code;
                Current = Status.Host;
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        public async void Join(string code)
        {
            if (Current == Status.Connecting || InSession) return;
            string clean = (code ?? "").Trim().ToUpperInvariant();
            if (clean.Length == 0)
            {
                Error = "Bitte den Raum-Code eingeben.";
                return;
            }
            Current = Status.Connecting;
            Error = null;
            try
            {
                await EnsureSignedIn();
                session = await MultiplayerService.Instance.JoinSessionByCodeAsync(clean);
                Attach();
                Code = session.Code;
                Current = Status.Client;
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        public async void Leave()
        {
            ISession leaving = session;
            Detach();
            session = null;
            Code = null;
            Current = Status.Offline;
            try
            {
                if (leaving != null) await leaving.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning("Frostline: Verlassen des Raums: " + e.Message);
            }
            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.IsListening) network.Shutdown();
        }

        void Attach()
        {
            session.RemovedFromSession += OnRemoved;
            session.Deleted += OnRemoved;
        }

        void Detach()
        {
            if (session == null) return;
            session.RemovedFromSession -= OnRemoved;
            session.Deleted -= OnRemoved;
        }

        void OnRemoved()
        {
            Detach();
            session = null;
            Code = null;
            Current = Status.Offline;
            Error = "Der Raum wurde geschlossen.";
        }

        void Fail(Exception e)
        {
            Current = Status.Failed;
            Error = e.Message;
            Debug.LogWarning("Frostline Multiplayer: " + e);
            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.IsListening) network.Shutdown();
        }

        void OnApplicationQuit()
        {
            if (InSession) Leave();
        }
    }
}
