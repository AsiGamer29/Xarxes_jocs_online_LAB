using System.Collections.Generic;
using UnityEngine;

// The one replication manager of the host: the players, the coins and the bullets all go through it,
// so there is a single packet sequence and one packet per tick with the whole world.
// In the game scene opened without a network it has no manager and only gives out the network ids.
public class WorldReplicationHost : MonoBehaviour
{
    const float TickInterval = 0.05f;   // 20 replication packets per second

    public static WorldReplicationHost Instance;

    readonly List<ReplicatedObject> m_objects = new List<ReplicatedObject>();
    WorldReplicationServer m_replication;
    float m_nextTick;

    void Awake()
    {
        Instance = this;
        if (ServerSession.IsGameServer) m_replication = new WorldReplicationServer(ServerSession.Linking);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // gives the object its network id and tells the clients to create it
    public void Spawn(ReplicatedObject obj)
    {
        obj.id = ServerSession.Linking.Register(obj.gameObject);
        m_objects.Add(obj);
        if (m_replication != null) m_replication.MarkCreate(obj.id);
    }

    // an object every PC already has with this id (the players: GameSpawner creates them on every PC
    // and the Welcome gives the ids), so only its updates travel
    public void Track(ReplicatedObject obj)
    {
        if (!m_objects.Contains(obj)) m_objects.Add(obj);
    }

    public void Untrack(ReplicatedObject obj)
    {
        m_objects.Remove(obj);
    }

    // tells the clients to destroy it, then destroys it here
    public void Despawn(ReplicatedObject obj)
    {
        if (m_replication != null) m_replication.MarkDestroy(obj.id);
        ServerSession.Linking.Unregister(obj.id);
        m_objects.Remove(obj);
        Destroy(obj.gameObject);
    }

    void Update()
    {
        if (m_replication == null || Time.time < m_nextTick) return;
        m_nextTick = Time.time + TickInterval;

        m_objects.RemoveAll(obj => obj == null);
        foreach (ReplicatedObject obj in m_objects) m_replication.MarkUpdate(obj.id);
        ServerSession.UdpServer.SendReplication(m_replication.WritePacket());
    }
}
