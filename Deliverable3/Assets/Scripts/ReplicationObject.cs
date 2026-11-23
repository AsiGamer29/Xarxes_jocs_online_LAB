using System.IO;
using UnityEngine;

// base class of every object the host replicates to the clients
// it writes and reads its own fields, the manager does not know what a bullet is
public class ReplicatedObject : MonoBehaviour
{
    public int id;      // network id, from the linking context
    public byte type;   // which prefab it is, same index in the prefab list of the clients

    // the game is 2D so only x and y travel
    public virtual void Write(BinaryWriter w)
    {
        w.Write(transform.position.x);
        w.Write(transform.position.y);
    }

    // must read exactly what Write wrote, in the same order
    public virtual void Read(BinaryReader r)
    {
        float x = r.ReadSingle();
        float y = r.ReadSingle();
        transform.position = new Vector3(x, y, transform.position.z);
    }
}