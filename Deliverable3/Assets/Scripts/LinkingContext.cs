using System.Collections.Generic;
using UnityEngine;

// Network ID <-> GameObject, one table per machine (Lab 6, Glazer & Madhav chapter 5).
// A network ID means the same object on every PC. Only the server creates IDs (1, 2, 3...) and it
// never reuses one, so a late packet about an old object can never move a new one.
public class LinkingContext
{
    readonly Dictionary<int, GameObject> m_objects = new Dictionary<int, GameObject>();
    int m_nextId = 1;   // 0 means "no ID yet"

    public int Count { get { return m_objects.Count; } }

    public IEnumerable<KeyValuePair<int, GameObject>> All { get { return m_objects; } }

    // Server: gives the object the next free ID.
    public int Register(GameObject go)
    {
        int id = m_nextId++;
        m_objects[id] = go;
        return id;
    }

    // Client: stores the object under the ID the server sent.
    public void Register(GameObject go, int id)
    {
        m_objects[id] = go;
    }

    // Null if unknown. With UDP a message can arrive after its object was destroyed, that is normal.
    public GameObject Find(int id)
    {
        GameObject go;
        return m_objects.TryGetValue(id, out go) ? go : null;
    }

    public void Unregister(int id)
    {
        m_objects.Remove(id);
    }
}
