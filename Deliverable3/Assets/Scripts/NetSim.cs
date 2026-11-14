using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

// Network simulator (Lab 6). Every UDP packet we send goes through here, on both sides, so we can
// add latency, jitter and loss on purpose with the sliders of the debug overlay (F1).
// With everything at 0 a packet leaves at once, as if the simulator was not there.
// Send and Update must be called from the main thread (they use Unity's Random and Time).
public class NetSim
{
    public float latency;      // seconds
    public float jitter;       // seconds, the delay changes by up to this much, so packets can reorder
    public float dropChance;   // 0 to 1

    readonly Socket m_socket;
    readonly List<(float at, byte[] data, EndPoint to)> m_queue = new List<(float, byte[], EndPoint)>();

    public NetSim(Socket socket) { m_socket = socket; }

    public int Pending { get { return m_queue.Count; } }

    public void Send(byte[] data, EndPoint to)
    {
        if (Random.value < dropChance) return;   // lost

        float delay = latency + Random.Range(-jitter, jitter);
        if (delay <= 0f && m_queue.Count == 0) SendNow(data, to);
        else m_queue.Add((Time.time + delay, data, to));
    }

    // Sends every queued packet whose time has come. Forward loop: with no jitter, no reordering.
    public void Update()
    {
        for (int i = 0; i < m_queue.Count; )
        {
            if (Time.time >= m_queue[i].at)
            {
                SendNow(m_queue[i].data, m_queue[i].to);
                m_queue.RemoveAt(i);
            }
            else i++;
        }
    }

    void SendNow(byte[] data, EndPoint to)
    {
        try { m_socket.SendTo(data, to); }
        catch (SocketException e) { Debug.Log("[NETSIM] Send failed: " + e.SocketErrorCode); }
        catch (System.ObjectDisposedException) { }
    }
}
