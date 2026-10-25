using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Pilla-pilla. Added to the GameSpawner object when the game scene starts.
// The server (host) runs the rules and sends a GameStateDTO 10 times per second; every PC,
// the host included, shows that state: who "la lleva", the time left and who lost.
// Rules: a random player has it. Touching another player passes it on, with a short immunity so it
// cannot bounce straight back. When the time runs out, whoever has it loses, and after a few
// seconds a new round starts with everybody back at their spawn point.
public class TagGame : MonoBehaviour
{
    const float RoundTime = 60f;
    const float RoundOverTime = 5f;     // how long the result stays on screen
    const float TagDistance = 1.1f;     // centre to centre, in world units
    const float TagImmunity = 1.5f;     // after a tag (or a new round) nobody can tag for this long
    const float SendInterval = 0.1f;
    const int RoundsToWin = 2;

    static readonly Color TaggedTint = new Color(1f, 0.45f, 0.45f);
    static readonly Color WinColor = new Color(0.4f, 1f, 0.4f);
    static readonly Color LoseColor = new Color(1f, 0.35f, 0.35f);

    GameSpawner m_spawner;
    GameStateDTO m_state;
    int m_shownRound;
    GUIStyle m_style;
    GUIStyle m_bigStyle;

    // Server only
    float m_phaseEnd;
    float m_immuneUntil;
    float m_nextSend;
    bool m_restartRequested;
    readonly Dictionary<string, int> m_wins = new Dictionary<string, int>();

    void Awake()
    {
        m_spawner = GetComponent<GameSpawner>();
        m_state.taggedPlayer = "";
        m_state.loser = "";
        m_state.scores = "";
        m_state.winners = "";
    }

    void Update()
    {
        if (!ServerSession.UdpGame) return;

        if (ServerSession.IsGameServer) RunRules();
        else
        {
            GameStateDTO received;
            if (ServerSession.TryGetGameState(out received)) m_state = received;
        }

        // A new round: every PC puts its own player back at its spawn point.
        if (m_state.round != m_shownRound)
        {
            m_shownRound = m_state.round;
            m_spawner.RespawnLocal();
        }

        PaintTagged();
    }

    // ------------------------------------------------------------------------------ server rules
    void RunRules()
    {
        float now = Time.time;
        List<string> players = PlayersInGame();

        switch (m_state.phase)
        {
            case GamePhase.Waiting:
                m_wins.Clear();
                m_state.scores = "";
                m_state.winners = "";
                if (players.Count >= 2) StartRound(players, now);
                break;

            case GamePhase.Playing:
                if (players.Count < 2) { m_state.phase = GamePhase.Waiting; m_state.taggedPlayer = ""; break; }
                if (!players.Contains(m_state.taggedPlayer)) PassTo(RandomPlayer(players), now);   // it left
                m_state.timeLeft = Mathf.Max(0f, m_phaseEnd - now);
                if (m_state.timeLeft <= 0f) EndRound(players, now);
                else if (now >= m_immuneUntil) TryTag(players, now);
                break;

            case GamePhase.RoundOver:
                m_state.timeLeft = Mathf.Max(0f, m_phaseEnd - now);
                if (m_state.timeLeft > 0f) break;
                if (players.Count >= 2) StartRound(players, now);
                else m_state.phase = GamePhase.Waiting;
                break;

            case GamePhase.MatchOver:
                m_state.timeLeft = 0f;
                if (m_restartRequested)
                {
                    m_restartRequested = false;
                    m_wins.Clear();
                    if (players.Count >= 2) StartRound(players, now);
                    else m_state.phase = GamePhase.Waiting;
                }
                break;
        }

        if (now >= m_nextSend)
        {
            m_nextSend = now + SendInterval;
            ServerSession.SendGameState(m_state);
        }
    }

    void StartRound(List<string> players, float now)
    {
        m_state.round++;
        m_state.phase = GamePhase.Playing;
        m_state.loser = "";
        m_state.winners = "";
        m_state.scores = BuildScores(players);
        m_phaseEnd = now + RoundTime;
        PassTo(RandomPlayer(players), now);
    }

    void EndRound(List<string> players, float now)
    {
        m_state.loser = m_state.taggedPlayer;
        m_state.taggedPlayer = "";

        foreach (string p in players)
        {
            if (p == m_state.loser) continue;
            int w;
            m_wins.TryGetValue(p, out w);
            m_wins[p] = w + 1;
        }
        m_state.scores = BuildScores(players);

        string winners = MatchWinners(players);
        if (winners.Length > 0)
        {
            m_state.phase = GamePhase.MatchOver;
            m_state.winners = winners;
            m_state.timeLeft = 0f;
        }
        else
        {
            m_state.phase = GamePhase.RoundOver;
            m_phaseEnd = now + RoundOverTime;
        }
    }

    string MatchWinners(List<string> players)
    {
        List<string> result = new List<string>();
        foreach (string p in players)
        {
            int w;
            if (m_wins.TryGetValue(p, out w) && w >= RoundsToWin) result.Add(p);
        }
        return string.Join(",", result.ToArray());
    }

    string BuildScores(List<string> players)
    {
        List<string> parts = new List<string>();
        foreach (string p in players)
        {
            int w;
            m_wins.TryGetValue(p, out w);
            parts.Add(p + ":" + w);
        }
        return string.Join(",", parts.ToArray());
    }

    void TryTag(List<string> players, float now)
    {
        GameObject tagged;
        if (!m_spawner.Players.TryGetValue(m_state.taggedPlayer, out tagged)) return;

        foreach (string other in players)
        {
            if (other == m_state.taggedPlayer) continue;
            GameObject target = m_spawner.Players[other];
            if (Vector2.Distance(tagged.transform.position, target.transform.position) <= TagDistance)
            {
                PassTo(other, now);
                return;
            }
        }
    }

    void PassTo(string player, float now)
    {
        m_state.taggedPlayer = player;
        m_immuneUntil = now + TagImmunity;
    }

    // Still in the room and with a player on screen.
    List<string> PlayersInGame()
    {
        List<string> result = new List<string>();
        List<string> room = ServerSession.CurrentPlayers;
        if (room == null) return result;
        foreach (string name in room)
            if (m_spawner.Players.ContainsKey(name)) result.Add(name);
        return result;
    }

    static string RandomPlayer(List<string> players)
    {
        return players[Random.Range(0, players.Count)];
    }

    // ------------------------------------------------------------------------------ what everybody sees
    void PaintTagged()
    {
        foreach (KeyValuePair<string, GameObject> pair in m_spawner.Players)
        {
            SpriteRenderer sr = pair.Value.GetComponent<SpriteRenderer>();
            if (sr == null) continue;
            bool tagged = m_state.phase == GamePhase.Playing && pair.Key == m_state.taggedPlayer;
            if (tagged) sr.color = TaggedTint;
            else sr.color = Color.white;
        }
    }

    // Arranging score
    string ScoreLine()
    {
        if (string.IsNullOrEmpty(m_state.scores)) return "";
        List<string> parts = new List<string>();
        foreach (string entry in m_state.scores.Split(','))
        {
            string[] kv = entry.Split(':');
            if (kv.Length == 2) parts.Add(kv[0] + " " + kv[1]);
        }
        return string.Join("  -  ", parts.ToArray());
    }

    bool IsWinner(string name)
    {
        if (string.IsNullOrEmpty(m_state.winners)) return false;
        foreach (string w in m_state.winners.Split(','))
            if (w == name) return true;
        return false;
    }

    void OnGUI()
    {
        if (!ServerSession.UdpGame) return;

        if (m_style == null)
        {
            m_style = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            m_style.normal.textColor = Color.white;
            m_bigStyle = new GUIStyle(m_style) { fontSize = 96 };
        }

        string me = ServerSession.LocalPlayerName;

        if (m_state.phase == GamePhase.MatchOver)
        {
            DrawMatchOver(me);
            return;
        }

        int seconds = Mathf.CeilToInt(m_state.timeLeft);
        string text;

        switch (m_state.phase)
        {
            case GamePhase.Playing:
                string who;
                if (m_state.taggedPlayer == me) who = "YOU are IT! Touch someone!";
                else who = m_state.taggedPlayer + " is IT!";
                text = who + "   " + seconds + "s";
                break;
            case GamePhase.RoundOver:
                string lost;
                if (m_state.loser == me) lost = "You lost the round!";
                else lost = m_state.loser + " lost the round!";
                text = lost + "   Next round in " + seconds + "s";
                break;
            default:
                text = "Waiting for at least 2 players";
                break;
        }

        float bottom = DrawBox(text, 8f);

        string score = ScoreLine();
        if (score.Length > 0) DrawBox(score, bottom + 6f);
    }

    float DrawBox(string text, float y)
    {
        Vector2 size = m_style.CalcSize(new GUIContent(text));
        Rect box = new Rect((Screen.width - size.x) / 2f - 16f, y, size.x + 32f, size.y + 10f);
        GUI.Box(box, GUIContent.none);
        GUI.Box(box, GUIContent.none);
        GUI.Label(box, text, m_style);
        return box.yMax;
    }

    void DrawMatchOver(string me)
    {
        bool won = IsWinner(me);

        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float cx = Screen.width / 2f;
        float top = Screen.height * 0.2f;

        string title;
        if (won)
        {
            title = "VICTORY!";
            m_bigStyle.normal.textColor = WinColor;
        }
        else
        {
            title = "DEFEAT";
            m_bigStyle.normal.textColor = LoseColor;
        }
        GUI.Label(new Rect(0f, top, Screen.width, 120f), title, m_bigStyle);
        GUI.Label(new Rect(0f, top + 120f, Screen.width, 50f), ScoreLine(), m_style);

        float y = top + 200f;
        if (ServerSession.IsGameServer)
        {
            if (GUI.Button(new Rect(cx - 120f, y, 240f, 50f), "Play again")) m_restartRequested = true;
            y += 65f;
        }
        else
        {
            GUI.Label(new Rect(0f, y, Screen.width, 40f), "Waiting for the host...", m_style);
            y += 65f;
        }

        if (GUI.Button(new Rect(cx - 120f, y, 240f, 50f), "Back to menu")) LeaveMatch();
    }

    void LeaveMatch()
    {
        if (ServerSession.UdpClient != null) ServerSession.UdpClient.Leave();
        ServerSession.Reset();
        SceneManager.LoadScene(ServerSession.SceneMenu);
    }

}
