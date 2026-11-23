using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Spawns one Player per lobby member when the game scene loads, in the order the server sent the
// list, so every PC puts each player in the same place.
// The host registers every player in the linking context and so gives it its network ID; a client
// links its players to the IDs that come in the Welcome (Lab 6).
// The player of this PC is moved with the keyboard. The others are moved by the network, so
// their input is off and their body is kinematic.
public class GameSpawner : MonoBehaviour
{
    [SerializeField] GameObject playerPrefab;
    [Tooltip("One per player, in lobby order (host first). With more players than points, they repeat.")]
    [SerializeField]
    Vector2[] spawnPoints =
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
    bool m_linked;

    // Every player on screen, by name. TagGame reads it.
    public IReadOnlyDictionary<string, GameObject> Players { get { return m_players; } }

    [Header("Wrap")]
    [SerializeField] Transform leftBound;
    [SerializeField] Transform rightBound;

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
        gameObject.AddComponent<TagGameUI>();
        AddDebugOverlay();
    }

    void Update()
    {
        LinkFromWelcome();
        LabelNetworkIds();

        if (Time.time < m_nextCleanup) return;
        m_nextCleanup = Time.time + 0.5f;

        // Someone left the room: take their player off the screen.
        List<string> room = ServerSession.CurrentPlayers;
        if (room == null) return;

        List<string> gone = new List<string>();
        foreach (string name in m_players.Keys)
            if (!room.Contains(name)) gone.Add(name);
        foreach (string name in gone) RemovePlayer(name);
    }

    public int NetworkIdOf(string playerName)
    {
        GameObject player;
        if (!m_players.TryGetValue(playerName, out player)) return 0;
        return player.GetComponent<PlayerNetSync>().NetworkId;
    }

    public void RemovePlayer(int networkId)
    {
        GameObject player = ServerSession.Linking.Find(networkId);
        if (player != null) RemovePlayer(player.GetComponent<PlayerNetSync>().PlayerName);
    }

    public void RemovePlayer(string playerName)
    {
        GameObject player;
        if (!m_players.TryGetValue(playerName, out player)) return;

        ServerSession.Linking.Unregister(NetworkIdOf(playerName));
        m_players.Remove(playerName);
        Destroy(player);
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

        // Player color depending on the player
        PlayerColor color = player.GetComponent<PlayerColor>();
        if (color == null) color = player.AddComponent<PlayerColor>();
        color.SetIndex(index);

        // Only the host creates network IDs. A client waits for the Welcome (LinkFromWelcome).
        if (ServerSession.UdpClient == null) sync.NetworkId = ServerSession.Linking.Register(player);

        SetNameTag(player, playerName);
        m_players[playerName] = player;

        if (isLocal)
        {
            m_localPlayer = player;
            m_localSpawn = position;
            player.AddComponent<PlayerShooter>();

            PlayerController controller = player.GetComponent<PlayerController>();
            if (controller != null)
            {
                controller.leftBound = leftBound;
                controller.rightBound = rightBound;
            }
        }
        else MakeRemote(player);
    }

    // Client: the Welcome says which network ID is which player. Register them with those IDs.
    void LinkFromWelcome()
    {
        UdpGameClient udp = ServerSession.UdpClient;
        if (m_linked || udp == null || !udp.HasWelcome) return;

        WelcomeDTO welcome = udp.Welcome;
        for (int i = 0; i < welcome.objectIds.Length; i++)
        {
            GameObject player;
            if (!m_players.TryGetValue(welcome.names[i], out player)) continue;
            ServerSession.Linking.Register(player, welcome.objectIds[i]);
            player.GetComponent<PlayerNetSync>().NetworkId = welcome.objectIds[i];
        }
        m_linked = true;
    }

    // F1 shows the network: packets per second, pings, the ID above every player and the simulator.
    void AddDebugOverlay()
    {
        gameObject.AddComponent<NetDebugOverlay>();

        NetSim sim = ServerSession.Sim;
        if (sim != null)
        {
            NetDebugOverlay.AddSlider("Latency (s)", () => sim.latency, v => sim.latency = v, 0f, 0.5f);
            NetDebugOverlay.AddSlider("Jitter (s)", () => sim.jitter, v => sim.jitter = v, 0f, 0.2f);
            NetDebugOverlay.AddSlider("Packet loss (0 to 1)", () => sim.dropChance, v => sim.dropChance = v, 0f, 1f);
        }
        NetDebugOverlay.AddButton("Leave the game", () => { if (TagGame.Instance != null) TagGame.Instance.OnBackToMenuClicked(); });
    }

    void LabelNetworkIds()
    {
        NetDebugOverlay.NetworkedObjects = ServerSession.Linking.Count;
        foreach (KeyValuePair<int, GameObject> entry in ServerSession.Linking.All)
            NetDebugOverlay.Label(entry.Value, "id " + entry.Key);
    }

    static void MakeRemote(GameObject player)
    {
        PlayerController controller = player.GetComponent<PlayerController>();
        if (controller != null) controller.enabled = false;

        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.bodyType = RigidbodyType2D.Kinematic;
            body.useFullKinematicContacts = true;   // two kinematic bodies only touch each other with this on
            body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            body.linearVelocity = Vector2.zero;
        }
    }

    static void SetNameTag(GameObject player, string playerName)
    {
        Transform tagTransform = player.transform.Find("NameTag");
        if (tagTransform == null) return;
        tagTransform.GetComponent<TMP_Text>().text = playerName;
    }
}
