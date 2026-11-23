using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public enum ReplicationAction : byte { Create = 1, Update = 2, Destroy = 3 }

// replication manager, host side
// the game marks what happened to each object and once per tick WritePacket turns it into one packet:
// [MsgType.Replication][sequence][count] and then per object [id][action][type][length][fields]
// (a destroy has no more). the length lets a client skip an object it cannot read without losing the rest
public class WorldReplicationServer
{
    const float RepeatTime = 1f;   // creates and destroys are repeated for 1 s

    readonly LinkingContext m_linking;
    readonly Dictionary<int, ReplicationAction> m_pending = new Dictionary<int, ReplicationAction>();
    readonly Dictionary<int, (ReplicationAction action, float until)> m_repeat =
        new Dictionary<int, (ReplicationAction, float)>();
    int m_sequence;

    public WorldReplicationServer(LinkingContext linking)
    {
        m_linking = linking;
    }

    public void MarkCreate(int id)
    {
        m_pending[id] = ReplicationAction.Create;
        m_repeat[id] = (ReplicationAction.Create, Time.time + RepeatTime);
    }

    public void MarkDestroy(int id)
    {
        m_pending[id] = ReplicationAction.Destroy;
        m_repeat[id] = (ReplicationAction.Destroy, Time.time + RepeatTime);
    }

    public void MarkUpdate(int id)
    {
        // a create or a destroy already pending wins over an update
        if (!m_pending.ContainsKey(id)) m_pending[id] = ReplicationAction.Update;
    }

    // call it once per tick and send the bytes to every client
    public byte[] WritePacket()
    {
        // lost creates and destroys are fixed by sending them again for a while
        foreach (int id in m_repeat.Keys.ToList())
        {
            if (Time.time > m_repeat[id].until) m_repeat.Remove(id);
            else m_pending[id] = m_repeat[id].action;
        }

        // an object destroyed without MarkDestroy would crash the write, so we skip it
        foreach (int id in m_pending.Keys.ToList())
            if (m_pending[id] != ReplicationAction.Destroy && m_linking.Find(id) == null)
                m_pending.Remove(id);

        MemoryStream stream = new MemoryStream();
        BinaryWriter writer = new BinaryWriter(stream);

        writer.Write((byte)MsgType.Replication);   // 1. mark the packet
        writer.Write(m_sequence++);
        writer.Write(m_pending.Count);

        foreach (KeyValuePair<int, ReplicationAction> entry in m_pending)
        {
            writer.Write(entry.Key);               // 2. identify the object
            writer.Write((byte)entry.Value);
            if (entry.Value == ReplicationAction.Destroy) continue;

            ReplicatedObject obj = m_linking.Find(entry.Key).GetComponent<ReplicatedObject>();
            writer.Write(obj.type);                // 3. its class, which prefab
            WriteFields(writer, obj);              // 4. its fields
        }

        m_pending.Clear();
        return stream.ToArray();
    }

    // the object writes itself in a separate buffer first, so its length can go before it
    static void WriteFields(BinaryWriter writer, ReplicatedObject obj)
    {
        MemoryStream fields = new MemoryStream();
        BinaryWriter fieldWriter = new BinaryWriter(fields);
        obj.Write(fieldWriter);
        fieldWriter.Flush();

        writer.Write((ushort)fields.Length);
        writer.Write(fields.GetBuffer(), 0, (int)fields.Length);
    }
}