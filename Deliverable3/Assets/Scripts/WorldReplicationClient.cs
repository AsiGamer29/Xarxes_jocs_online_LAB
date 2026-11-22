using System.Collections.Generic;
using System.IO;
using UnityEngine;

// replication manager, client side. it copies what the host says
// a create for a known id is an update and a destroy for an unknown id is ignored,
// that is what fixes the lost packets
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
                // unknown prefab, we cannot know how long its fields are
                if (type >= prefabs.Count) return;

                // create, or an update whose create was lost
                if (go == null)
                {
                    go = Instantiate(prefabs[type]);
                    go.GetComponent<ReplicatedObject>().id = id;
                    ServerSession.Linking.Register(go, id);
                }
                go.GetComponent<ReplicatedObject>().Read(reader);
            }
        }
        catch (EndOfStreamException)
        {
            // broken packet, ignore it
        }
    }
}