using UnityEngine;

// host only. spawns the coins, decides when a player takes one and respawns it later
// the clients never create a coin themselves, they only copy the replication packets
public class CoinSpawner : MonoBehaviour
{
    public static CoinSpawner Instance;

    [SerializeField] GameObject coinPrefab;
    [SerializeField] Transform[] spawnPoints;
    [SerializeField] float respawnTime = 5f;
    [SerializeField] float sendInterval = 0.1f;   // 10 replication packets per second
    [Range(0f, 1f)][SerializeField] float goldenChance = 0.2f;

    class Slot
    {
        public ReplicatedCoin coin;   // null while it waits to respawn
        public float respawnAt;
    }

    Slot[] m_slots;
    WorldReplicationServer m_replication;
    float m_nextSend;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (!ServerSession.IsGameServer)
        {
            enabled = false;
            return;
        }

        m_replication = new WorldReplicationServer(ServerSession.Linking);
        m_slots = new Slot[spawnPoints.Length];
        for (int i = 0; i < m_slots.Length; i++)
        {
            m_slots[i] = new Slot();
            Spawn(i);
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        for (int i = 0; i < m_slots.Length; i++)
            if (m_slots[i].coin == null && Time.time >= m_slots[i].respawnAt) Spawn(i);

        if (Time.time < m_nextSend) return;
        m_nextSend = Time.time + sendInterval;

        // full world every tick, the coins are few. it also lets a client that
        // loaded the scene late create the coins it missed
        foreach (Slot slot in m_slots)
            if (slot.coin != null) m_replication.MarkUpdate(slot.coin.id);

        ServerSession.UdpServer.SendReplication(m_replication.WritePacket());
    }

    void Spawn(int index)
    {
        GameObject go = Instantiate(coinPrefab, spawnPoints[index].position, Quaternion.identity);
        ReplicatedCoin coin = go.GetComponent<ReplicatedCoin>();

        bool golden = Random.value < goldenChance;
        coin.kind = golden ? (byte)1 : (byte)0;
        coin.value = golden ? 5 : 1;
        coin.Paint();

        coin.id = ServerSession.Linking.Register(go);
        m_slots[index].coin = coin;
        m_replication.MarkCreate(coin.id);
    }

    public void Collect(ReplicatedCoin coin, string playerName)
    {
        for (int i = 0; i < m_slots.Length; i++)
        {
            // the second trigger of the same frame finds the slot already empty
            if (m_slots[i].coin != coin) continue;

            m_slots[i].coin = null;
            m_slots[i].respawnAt = Time.time + respawnTime;

            m_replication.MarkDestroy(coin.id);
            ServerSession.Linking.Unregister(coin.id);
            Destroy(coin.gameObject);

            Debug.Log("[COINS] " + playerName + " took a coin worth " + coin.value);
            return;
        }
    }
}