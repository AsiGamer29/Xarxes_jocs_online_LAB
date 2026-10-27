using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Spawns one Player per lobby member when the game scene loads, in the order the server sent the
// list, so every PC puts each player in the same place.
// The player of this PC is moved with the keyboard. The others will be moved by the network, so
// their input and physics are switched off.
public class GameSpawner : MonoBehaviour
{
    [SerializeField] GameObject playerPrefab;
    [Tooltip("One per player, in lobby order (host first). With more players than points, they repeat.")]
    [SerializeField] Vector2[] spawnPoints =
    {
        new Vector2(0f, 0.83f),     // middle platform
        new Vector2(12f, 4f),       // right platform
        new Vector2(-14.5f, 4f),    // left platform, away from its button
        new Vector2(-8f, -2f),      // lower left platform
    };

    readonly Dictionary<string, GameObject> m_players = new Dictionary<string, GameObject>();
    GameObject m_localPlayer;
    Vector2 m_localSpawn;
    float m_nextCleanup;

    // Every player on screen, by name. TagGame reads it.
    public IReadOnlyDictionary<string, GameObject> Players { get { return m_players; } }

    void Start()
    {
        List<string> players = ServerSession.CurrentPlayers;
        string localName = ServerSession.LocalPlayerName;

        // Scene opened straight from the Editor, without the lobby: one local player to test with.
        if (players == null || players.Count == 0)
        {
            players = new List<string> { "Player" };
            localName = "Player";
        }

        List<string> names = new List<string>(players);
        for (int i = 0; i < names.Count; i++)
            Spawn(names[i], i, names[i] == localName);

        gameObject.AddComponent<TagGame>();
    }

    // Someone left the room: take their player off the screen.
    void Update()
    {
        if (Time.time < m_nextCleanup) return;
        m_nextCleanup = Time.time + 0.5f;

        List<string> room = ServerSession.CurrentPlayers;
        if (room == null) return;

        List<string> gone = new List<string>();
        foreach (string name in m_players.Keys)
            if (!room.Contains(name)) gone.Add(name);

        foreach (string name in gone)
        {
            Destroy(m_players[name]);
            m_players.Remove(name);
        }
    }

    // New round: back to the start. Only the local player; the others follow through the network.
    public void RespawnLocal()
    {
        if (m_localPlayer == null) return;
        m_localPlayer.transform.position = m_localSpawn;

        Rigidbody2D body = m_localPlayer.GetComponent<Rigidbody2D>();
        if (body != null) body.linearVelocity = Vector2.zero;
    }

    void Spawn(string playerName, int index, bool isLocal)
    {
        Vector2 position = spawnPoints[index % spawnPoints.Length];
        GameObject player = Instantiate(playerPrefab, position, Quaternion.identity);
        player.name = "Player_" + playerName;
        player.tag = "Player";
        PlayerNetSync sync = player.AddComponent<PlayerNetSync>();
        sync.Init(playerName, isLocal);

        SetNameTag(player, playerName, isLocal);
        m_players[playerName] = player;

        if (isLocal)
        {
            m_localPlayer = player;
            m_localSpawn = position;
        }
        else MakeRemote(player);
    }

    static void MakeRemote(GameObject player)
    {
        PlayerController controller = player.GetComponent<PlayerController>();
        if (controller != null) controller.enabled = false;

        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.bodyType = RigidbodyType2D.Kinematic;
            body.useFullKinematicContacts = true; // Sin esto puede que falle por como funciona la dinamica entre kinematics
            body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            body.linearVelocity = Vector2.zero;
        }
    }

    void SetNameTag(GameObject player, string playerName, bool isLocal)
    {
        Transform tagTransform = player.transform.Find("NameTag");
        if (tagTransform == null) return;

        TMP_Text tag = tagTransform.GetComponent<TMP_Text>();
        tag.text = playerName;

    }

}
