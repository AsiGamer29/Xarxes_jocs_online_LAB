using System.Collections.Generic;
using System.IO;
using UnityEngine;

// replication manager, client side. it copies what the host says
// a create for a known id is an update and a destroy for an unknown id is ignored,
// that is what fixes the lost packets. every object comes with the length of its fields, so one
// object this client cannot read is skipped and the rest of the packet still works
public class WorldReplicationClient : MonoBehaviour
{
    public static WorldReplicationClient Instance;

    [SerializeField] List<GameObject> prefabs = new List<GameObject>();   // index = type

    int m_lastSequence = -1;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // the MsgType byte is already read
    public void ReadPacket(BinaryReader reader)
    {
        try
        {
            int sequence = reader.ReadInt32();
            if (sequence <= m_lastSequence) return;   // older than what we already have
            m_lastSequence = sequence;

            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                int id = reader.ReadInt32();
                ReplicationAction action = (ReplicationAction)reader.ReadByte();
                GameObject go = ServerSession.Linking.Find(id);

                if (action == ReplicationAction.Destroy)
                {
                    ServerSession.Linking.Unregister(id);
                    if (go != null) Destroy(go);
                    continue;
                }

                byte type = reader.ReadByte();
                int length = reader.ReadUInt16();
                byte[] fields = reader.ReadBytes(length);
                if (fields.Length < length) return;   // the packet was cut

                // create, or an update whose create was lost. a type that is not in the list is a player
                // this client has not linked yet (players come from GameSpawner): skip only that object
                if (go == null)
                {
                    if (type >= prefabs.Count) continue;
                    go = Instantiate(prefabs[type]);
                    go.GetComponent<ReplicatedObject>().id = id;
                    ServerSession.Linking.Register(go, id);
                }

                ReplicatedObject obj = go.GetComponent<ReplicatedObject>();
                if (obj == null) continue;
                try { obj.Read(new BinaryReader(new MemoryStream(fields))); }
                catch (EndOfStreamException) { }   // its fields are shorter than this build expects
            }
        }
        catch (EndOfStreamException)
        {
            // broken packet, ignore it
        }
    }
}