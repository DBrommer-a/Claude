using System;
using System.Collections;
using System.IO;
using Frostline.Flight;
using Frostline.View;
using UnityEngine;

namespace Frostline.Multiplayer
{
    /// <summary>
    /// Multiplayer-Test mit zwei laufenden Spielen (tools/mac/frostline.sh multiplayer):
    ///   -frostlineHost &lt;Datei&gt;      hosten, Raum-Code in die Datei schreiben, Lichtstimmung „Mittag“ für alle
    ///   -frostlineJoin &lt;Datei&gt;      auf die Datei warten, beitreten, Host-Drohne und Licht prüfen, Bild machen
    ///   -frostlineMpBild &lt;png&gt;      Bild des Mitspielers (FPV-Sicht auf die Host-Drohne)
    ///   -frostlineMpDauer &lt;s&gt;       wie lange der Host offen bleibt (Standard 150 s)
    /// Ergebnisse stehen als „FROSTLINE_MP …“ im Player-Log.
    /// </summary>
    public sealed class MultiplayerTest : MonoBehaviour
    {
        string hostFile, joinFile, picture;
        float duration = 150f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Attach()
        {
            string[] args = Environment.GetCommandLineArgs();
            string host = Argument(args, "-frostlineHost"), join = Argument(args, "-frostlineJoin");
            if (host == null && join == null) return;
            var go = new GameObject("Frostline Multiplayer-Test");
            DontDestroyOnLoad(go);
            MultiplayerTest test = go.AddComponent<MultiplayerTest>();
            test.hostFile = host;
            test.joinFile = join;
            test.picture = Argument(args, "-frostlineMpBild");
            float d;
            if (float.TryParse(Argument(args, "-frostlineMpDauer"), out d)) test.duration = d;
        }

        static string Argument(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static void Log(string text)
        {
            Debug.Log("FROSTLINE_MP " + text);
        }

        IEnumerator Start()
        {
            // Ladebildschirm und Dienste hochfahren lassen.
            yield return new WaitForSecondsRealtime(8f);
            MultiplayerSession session = FindAnyObjectByType<MultiplayerSession>();
            if (session == null)
            {
                Log("FEHLER: keine MultiplayerSession in der Szene");
                Quit(2);
                yield break;
            }
            if (hostFile != null) yield return RunHost(session);
            else yield return RunClient(session);
        }

        IEnumerator RunHost(MultiplayerSession session)
        {
            MultiplayerSession.PilotName = "Host (Test)";
            MultiplayerSession.Profile = "testhost";
            session.Host();
            yield return WaitWhileConnecting(session, 60f);
            if (session.Current != MultiplayerSession.Status.Host)
            {
                Log("FEHLER Host: " + session.Error);
                File.WriteAllText(hostFile, "FEHLER " + session.Error);
                Quit(1);
                yield break;
            }
            Log("Host bereit, Raum-Code " + session.Code);

            // Eigene Drohne 6 m vor den Startplatz in Augenhöhe stellen, damit der Mitspieler sie im Bild hat.
            DroneController drone = FindAnyObjectByType<DroneController>();
            DroneRespawn respawn = drone != null ? drone.GetComponent<DroneRespawn>() : null;
            if (drone != null && respawn != null && respawn.spawnPoint != null)
            {
                Transform spawn = respawn.spawnPoint;
                drone.enabled = false;
                drone.Body.isKinematic = true;
                drone.transform.SetPositionAndRotation(spawn.position + spawn.forward * 6f + Vector3.up * 1.2f,
                    Quaternion.LookRotation(-spawn.forward));
            }

            // Licht für alle: Der Mitspieler muss danach „Mittag“ zeigen, obwohl er abends startet.
            if (LightingMoods.Instance != null)
            {
                for (int i = 0; i < LightingMoods.Instance.moods.Length; i++)
                    if (LightingMoods.Instance.moods[i].name == "Mittag") LightingMoods.Instance.Select(i);
                Log("Host-Lichtstimmung: Mittag");
            }

            File.WriteAllText(hostFile, session.Code);
            float end = Time.realtimeSinceStartup + duration;
            int lastPlayers = -1, lastDrones = -1;
            while (Time.realtimeSinceStartup < end && session.InSession)
            {
                int players = session.PlayerCount, drones = NetworkDrone.All.Count;
                if (players != lastPlayers || drones != lastDrones)
                {
                    Log("Host: " + players + " Spieler im Raum, " + drones + " Netzwerk-Drohnen");
                    lastPlayers = players;
                    lastDrones = drones;
                }
                yield return new WaitForSecondsRealtime(1f);
            }
            Log("Host beendet (" + (session.InSession ? "Zeit um" : "Raum geschlossen") + ")");
            session.Leave();
            yield return new WaitForSecondsRealtime(2f);
            Quit(0);
        }

        IEnumerator RunClient(MultiplayerSession session)
        {
            float giveUp = Time.realtimeSinceStartup + 150f;
            while (!File.Exists(joinFile) && Time.realtimeSinceStartup < giveUp) yield return new WaitForSecondsRealtime(1f);
            string code = File.Exists(joinFile) ? File.ReadAllText(joinFile).Trim() : "";
            if (code.Length == 0 || code.StartsWith("FEHLER"))
            {
                Log("FEHLER Mitspieler: kein Raum-Code vom Host (" + code + ")");
                Quit(1);
                yield break;
            }

            // Erst jetzt den Namen setzen: Beide Spiele auf einem Mac teilen sich die Einstellungen (PlayerPrefs),
            // und die Host-Drohne hat ihren Namen beim Hosten schon gelesen.
            MultiplayerSession.PilotName = "Gast (Test)";
            MultiplayerSession.Profile = "testgast";
            session.Join(code);
            yield return WaitWhileConnecting(session, 60f);
            if (session.Current != MultiplayerSession.Status.Client)
            {
                Log("FEHLER Beitritt: " + session.Error);
                Quit(1);
                yield break;
            }
            Log("Mitspieler verbunden, Raum " + code + ", " + session.PlayerCount + " Spieler");

            // Auf die Drohne des Hosts warten (fremde Netzwerk-Drohne, die schon Positionen bekommt).
            NetworkDrone other = null;
            float wait = Time.realtimeSinceStartup + 25f;
            while (other == null && Time.realtimeSinceStartup < wait)
            {
                foreach (NetworkDrone d in NetworkDrone.All)
                    if (!d.IsOwner && d.transform.position.sqrMagnitude > 1f) other = d;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            if (other == null) Log("FEHLER: Drohne des Hosts nicht gesehen (" + NetworkDrone.All.Count + " Netzwerk-Drohnen)");
            else Log("Host-Drohne gesehen: „" + other.PilotName + "“ bei " + other.transform.position.ToString("F1"));

            // Licht und Wetter kommen vom Host.
            yield return new WaitForSecondsRealtime((LightingMoods.Instance != null ? LightingMoods.Instance.blendSeconds : 0f) + 4f);
            WeatherSystem weather = WeatherSystem.Instance;
            Log("Licht beim Mitspieler: Uhrzeit " + (weather != null ? weather.timeOfDay.ToString("0.0") : "?")
                + ", Stimmung " + (LightingMoods.Instance != null ? LightingMoods.Instance.CurrentName : "-")
                + ", vom Host gesperrt " + (weather != null && weather.lockedByHost));

            if (!string.IsNullOrEmpty(picture))
            {
                // Für das Bild 4 m vor der Host-Drohne schweben, Nase zu Nase: Am Boden verdecken Farne die Sicht.
                DroneController own = FindAnyObjectByType<DroneController>();
                if (own != null && other != null)
                {
                    own.enabled = false;
                    own.Body.isKinematic = true;
                    Vector3 facing = Vector3.ProjectOnPlane(other.transform.forward, Vector3.up).normalized;
                    if (facing.sqrMagnitude < 0.01f) facing = Vector3.forward;
                    own.transform.SetPositionAndRotation(other.transform.position + facing * 4f + Vector3.up * 0.4f, Quaternion.LookRotation(-facing));
                    yield return new WaitForSecondsRealtime(2f);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(picture)));
                ScreenCapture.CaptureScreenshot(picture);
                yield return new WaitForSecondsRealtime(2f);
                Log("Bild: " + picture);
            }
            Log(other != null ? "OK" : "TEILWEISE");
            session.Leave();
            yield return new WaitForSecondsRealtime(2f);
            Quit(other != null ? 0 : 1);
        }

        static IEnumerator WaitWhileConnecting(MultiplayerSession session, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            yield return null;
            while ((session.Current == MultiplayerSession.Status.Connecting || session.Current == MultiplayerSession.Status.Offline)
                   && Time.realtimeSinceStartup < end)
                yield return new WaitForSecondsRealtime(0.5f);
        }

        static void Quit(int code)
        {
            Application.Quit(code);
        }
    }
}
