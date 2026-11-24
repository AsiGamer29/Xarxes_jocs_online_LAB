using UnityEngine;

// host only. spawns the coins, decides who takes one and respawns it later
// the coins travel in the replication packet of WorldReplicationHost, like the players and the bullets,
// so the clients never create a coin themselves
public class CoinSpawner : MonoBehaviour
{
    public static CoinSpawner Instance;

    [SerializeField] GameObject coinPrefab;
    [SerializeField] Transform[] spawnPoints;
    [SerializeField] float respawnTime = 5f;
    [Range(0f, 1f)][SerializeField] float goldenChance = 0.2f;

    class Slot
    {
        public ReplicatedCoin coin;   // null while it waits to respawn
        public float respawnAt;
    }

    Slot[] m_slots;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (!ServerSession.HasAuthority || WorldReplicationHost.Instance == null)
        {
            enabled = false;
            return;
        }

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
    }

    void Spawn(int index)
    {
        GameObject go = Instantiate(coinPrefab, spawnPoints[index].position, Quaternion.identity);
        ReplicatedCoin coin = go.GetComponent<ReplicatedCoin>();

        bool golden = Random.value < goldenChance;
        coin.kind = golden ? (byte)1 : (byte)0;
        coin.value = golden ? 5 : 1;
        coin.Paint();

        WorldReplicationHost.Instance.Spawn(coin);
        m_slots[index].coin = coin;
    }

    public void Collect(ReplicatedCoin coin, PlayerNetSync player)
    {
        for (int i = 0; i < m_slots.Length; i++)
        {
            // the second trigger of the same frame finds the slot already empty
            if (m_slots[i].coin != coin) continue;

            m_slots[i].coin = null;
            m_slots[i].respawnAt = Time.time + respawnTime;

            //not counted in waiting time
            bool inRound = TagGame.Instance == null || TagGame.Instance.State.phase == GamePhase.Playing;
            if (inRound) player.AddCoins(coin.value);

            WorldReplicationHost.Instance.Despawn(coin);
            Debug.Log("[COINS] " + player.PlayerName + " took a coin worth " + coin.value + (inRound ? "" : " (out of round, not counted)"));
            return;
        }
    }
}
