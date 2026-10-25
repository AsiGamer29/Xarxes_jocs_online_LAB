using System.Collections.Generic;
using UnityEngine;

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

    static readonly Color TaggedTint = new Color(1f, 0.45f, 0.45f);

    GameSpawner m_spawner;
    GameStateDTO m_state;
    int m_shownRound;
    GUIStyle m_style;

    // Server only
    float m_phaseEnd;
    float m_immuneUntil;
    float m_nextSend;

    void Awake()
    {
        m_spawner = GetComponent<GameSpawner>();
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
                if (players.Count >= 2) StartRound(players, now);
                break;

            case GamePhase.Playing:
                if (players.Count < 2) { m_state.phase = GamePhase.Waiting; m_state.taggedPlayer = ""; break; }
                if (!players.Contains(m_state.taggedPlayer)) PassTo(RandomPlayer(players), now);   // it left
                m_state.timeLeft = Mathf.Max(0f, m_phaseEnd - now);
                if (m_state.timeLeft <= 0f) EndRound(now);
                else if (now >= m_immuneUntil) TryTag(players, now);
                break;

            case GamePhase.RoundOver:
                m_state.timeLeft = Mathf.Max(0f, m_phaseEnd - now);
                if (m_state.timeLeft > 0f) break;
                if (players.Count >= 2) StartRound(players, now);
                else m_state.phase = GamePhase.Waiting;
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
        m_phaseEnd = now + RoundTime;
        PassTo(RandomPlayer(players), now);
    }

    void EndRound(float now)
    {
        m_state.phase = GamePhase.RoundOver;
        m_state.loser = m_state.taggedPlayer;
        m_state.taggedPlayer = "";
        m_phaseEnd = now + RoundOverTime;
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
            sr.color = tagged ? TaggedTint : Color.white;
        }
    }

    void OnGUI()
    {
        if (!ServerSession.UdpGame) return;

        if (m_style == null)
        {
            m_style = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            m_style.normal.textColor = Color.white;
        }

        string me = ServerSession.LocalPlayerName;
        int seconds = Mathf.CeilToInt(m_state.timeLeft);
        string text;
        switch (m_state.phase)
        {
            case GamePhase.Playing:
                string who = m_state.taggedPlayer == me ? "YOU are IT! Touch someone!" : m_state.taggedPlayer + " is IT!";
                text = who + "   " + seconds + "s";
                break;
            case GamePhase.RoundOver:
                string lost = m_state.loser == me ? "You lost!" : m_state.loser + " lost!";
                text = lost + "   Next round in " + seconds + "s";
                break;
            default:
                text = "Waiting for at least 2 players";
                break;
        }
        // A dark box behind the text, or it is lost against the tiles.
        Vector2 size = m_style.CalcSize(new GUIContent(text));
        Rect box = new Rect((Screen.width - size.x) / 2f - 16f, 8f, size.x + 32f, size.y + 10f);
        GUI.Box(box, GUIContent.none);
        GUI.Box(box, GUIContent.none);   // twice: one default box is too transparent
        GUI.Label(box, text, m_style);
    }
}
