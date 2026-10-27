using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TagGame : MonoBehaviour
{
    const float RoundTime = 60f;
    const float RoundOverTime = 5f;
    const float TagCooldown = 1f;
    const float SendInterval = 0.1f;
    const int RoundsToWin = 3;
    public static TagGame Instance;
    string m_touched;

    static readonly Color TaggedTint = new Color(1f, 0.45f, 0.45f);

    GameSpawner m_spawner;
    GameStateDTO m_state;
    int m_shownRound;

    // Server only
    float m_phaseEnd;
    float m_immuneUntil;
    float m_nextSend;
    bool m_restartRequested;
    readonly Dictionary<string, int> m_wins = new Dictionary<string, int>();

    void Awake()
    {
        Instance = this;
        m_spawner = GetComponent<GameSpawner>();
        m_state.taggedPlayer = "";
        m_state.loser = "";
        m_state.scores = "";
        m_state.winners = "";
    }

    void Update()
    {
        if (!ServerSession.UdpGame) return;

        if (!ServerSession.IsGameServer && (ServerSession.Client == null || ServerSession.Client.Status != LobbyClient.State.Connected))
        {
            LeaveMatch();
            return;
        }

        if (ServerSession.IsGameServer) RunRules();
        else
        {
            GameStateDTO received;
            if (ServerSession.TryGetGameState(out received)) m_state = received;
        }

        // Every PC puts its own player back at its spawn point.
        if (m_state.round != m_shownRound)
        {
            m_shownRound = m_state.round;
            m_spawner.RespawnLocal();
            PButton.ResetAll();
        }

        PaintTagged();
    }

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
                if (!players.Contains(m_state.taggedPlayer)) PassTo(RandomPlayer(players), now);
                m_state.timeLeft = Mathf.Max(0f, m_phaseEnd - now);
                if (m_state.timeLeft <= 0f) EndRound(players, now);
                else if (m_touched != null && now >= m_immuneUntil && players.Contains(m_touched))
                {
                    PassTo(m_touched, now);
                }
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

        m_touched = null;
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

    void PassTo(string player, float now)
    {
        m_state.taggedPlayer = player;
        m_immuneUntil = now + TagCooldown;
    }

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

    public bool IsWinner(string name)
    {
        if (string.IsNullOrEmpty(m_state.winners)) return false;
        foreach (string w in m_state.winners.Split(','))
            if (w == name) return true;
        return false;
    }

    public void OnPlayAgainClicked()
    {
        if (ServerSession.IsGameServer) m_restartRequested = true;
    }

    public void OnBackToMenuClicked()
    {
        LeaveMatch();
    }

    void LeaveMatch()
    {
        if (ServerSession.Client != null) ServerSession.Client.Leave();
        ServerSession.Reset();
        SceneManager.LoadScene(ServerSession.SceneMenu);
    }

    public void ReportTouch(string a, string b)
    {
        if (!ServerSession.IsGameServer || m_state.phase != GamePhase.Playing) return;

        if (a == m_state.taggedPlayer) m_touched = b;
        else if (b == m_state.taggedPlayer) m_touched = a;
    }
}
