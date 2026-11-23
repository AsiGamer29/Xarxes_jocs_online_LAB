using System.Collections.Generic;
using UnityEngine;

// The one replication manager of the host: every object of the world that is created and destroyed
// while playing goes through it, so there is a single packet sequence and one packet per tick.
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

        foreach (ReplicatedObject obj in m_objects) m_replication.MarkUpdate(obj.id);
        ServerSession.UdpServer.SendReplication(m_replication.WritePacket());
    }
}
