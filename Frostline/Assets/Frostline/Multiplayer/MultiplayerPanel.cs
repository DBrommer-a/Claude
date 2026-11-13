using Frostline.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Frostline.Multiplayer
{
    /// <summary>
    /// F2: Multiplayer-Fenster (Raum hosten, Code eingeben, Spieler, Rennen für alle, Fangen).
    /// Dazu im Flug: Raum-Code, Namen über fremden Drohnen, Bestenliste und Ansagen.
    /// </summary>
    public sealed class MultiplayerPanel : MonoBehaviour
    {
        public MultiplayerSession session;

        bool open;
        bool previousKey;
        string codeInput = "";
        string nameInput;
        GUIStyle heading, wrapped, nameTag, big, board;

        void Awake()
        {
            if (session == null) session = FindFirstObjectByType<MultiplayerSession>();
            nameInput = MultiplayerSession.PilotName;
        }

        void Update()
        {
            Keyboard keys = Keyboard.current;
            bool key = keys != null && keys.f2Key.isPressed;
            if (key && !previousKey) open = !open;
            previousKey = key;
        }

        void Styles()
        {
            if (heading != null) return;
            heading = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            wrapped = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            nameTag = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            nameTag.normal.textColor = new Color(1f, 0.85f, 0.4f);
            big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            big.normal.textColor = new Color(1f, 0.5f, 0.3f);
            board = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            board.normal.textColor = Color.white;
        }

        void OnGUI()
        {
            if (session == null) return;
            Styles();
            DrawNameTags();
            DrawHud();
            if (open) DrawWindow();
        }

        void DrawNameTags()
        {
            Camera cam = Camera.main;
            if (cam == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;
            NetworkWorld world = NetworkWorld.Instance;
            foreach (NetworkDrone drone in NetworkDrone.All)
            {
                if (drone == null || drone.IsOwner) continue;
                Vector3 p = drone.transform.position + Vector3.up * 0.6f;
                Vector3 screen = cam.WorldToScreenPoint(p);
                if (screen.z <= 0f || screen.z > 600f) continue;
                string label = drone.PilotName + "  " + Mathf.RoundToInt(screen.z) + " m";
                if (world != null && world.TagMode && world.TaggedClient == drone.OwnerClientId) label = "● " + label;
                GUI.Label(new Rect(screen.x - 100f, Screen.height - screen.y - 24f, 200f, 22f), label, nameTag);
            }
        }

        void DrawHud()
        {
            if (!session.InSession) return;
            GUI.Label(new Rect(20f, 48f, 400f, 22f), "Raum " + session.Code + "  ·  " + session.PlayerCount + " Spieler  ·  F2", wrapped);

            NetworkWorld world = NetworkWorld.Instance;
            if (world == null) return;
            if (!string.IsNullOrEmpty(world.Leaderboard))
            {
                GUI.Label(new Rect(20f, Screen.height * 0.35f, 320f, 220f), world.Leaderboard, board);
            }
            if (world.Announcement != null && Time.unscaledTime < world.AnnouncementUntil)
            {
                GUI.Label(new Rect(0f, Screen.height * 0.3f, Screen.width, 50f), world.Announcement, big);
            }
        }

        void DrawWindow()
        {
            var area = new Rect(Screen.width * 0.5f - 260f, 60f, 520f, 520f);
            GUI.Box(area, GUIContent.none);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 14f, area.y + 10f, area.width - 28f, area.height - 20f));
            GUILayout.Label("Multiplayer", heading);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Dein Name", GUILayout.Width(90f));
            nameInput = GUILayout.TextField(nameInput ?? "", 24);
            if (GUILayout.Button("Übernehmen", GUILayout.Width(110f))) MultiplayerSession.PilotName = nameInput;
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);

            switch (session.Current)
            {
                case MultiplayerSession.Status.Offline:
                case MultiplayerSession.Status.Failed:
                    GUILayout.Label("Ein Spieler hostet, die anderen treten mit dem Raum-Code bei (bis 8 Drohnen). Läuft über Unity Relay, ohne eigenen Server.", wrapped);
                    if (GUILayout.Button("Raum hosten", GUILayout.Height(34f)))
                    {
                        MultiplayerSession.PilotName = nameInput;
                        session.Host();
                    }
                    GUILayout.Space(6f);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Raum-Code", GUILayout.Width(90f));
                    codeInput = GUILayout.TextField(codeInput, 12, GUILayout.Width(160f));
                    if (GUILayout.Button("Beitreten", GUILayout.Height(28f)))
                    {
                        MultiplayerSession.PilotName = nameInput;
                        session.Join(codeInput);
                    }
                    GUILayout.EndHorizontal();
                    break;

                case MultiplayerSession.Status.Connecting:
                    GUILayout.Label("Verbinde …", heading);
                    break;

                default:
                    DrawSession();
                    break;
            }

            if (!string.IsNullOrEmpty(session.Error))
            {
                GUILayout.Space(8f);
                GUILayout.Label("Fehler: " + session.Error, wrapped);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Schließen (F2)", GUILayout.Height(26f))) open = false;
            GUILayout.EndArea();
        }

        void DrawSession()
        {
            bool host = session.Current == MultiplayerSession.Status.Host;
            GUILayout.Label("Raum-Code:  " + session.Code, heading);
            GUILayout.Label(host ? "Du bist Host. Gib den Code an deine Mitspieler weiter." : "Verbunden.", wrapped);

            GUILayout.Space(6f);
            GUILayout.Label("Spieler", heading);
            foreach (NetworkDrone drone in NetworkDrone.All)
            {
                if (drone != null) GUILayout.Label("• " + drone.PilotName + (drone.IsOwner ? " (du)" : ""), wrapped);
            }

            NetworkWorld world = NetworkWorld.Instance;
            RaceManager race = FindFirstObjectByType<RaceManager>();
            if (host && world != null)
            {
                GUILayout.Space(6f);
                GUILayout.Label("Für alle starten", heading);
                if (race != null)
                {
                    for (int i = 0; i < race.tracks.Count; i++)
                    {
                        if (GUILayout.Button("Rennen: " + race.tracks[i].trackName, GUILayout.Height(26f))) world.StartRaceForAll(i);
                    }
                }
                if (GUILayout.Button(world.TagMode ? "Fangen beenden" : "Fangen starten", GUILayout.Height(26f))) world.ToggleTagMode();
                GUILayout.Label("Wetter und Tageszeit stellst du wie gewohnt im Menü (F1 › Spiel) ein; alle sehen dasselbe.", wrapped);
            }
            else
            {
                GUILayout.Label("Wetter, Tageszeit und Rennen bestimmt der Host.", wrapped);
            }

            GUILayout.Space(8f);
            if (GUILayout.Button("Raum verlassen", GUILayout.Height(30f))) session.Leave();
        }
    }
}
