using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

// UDP game channel, host side. Lobby, chat, START and buttons go through TCP (LobbyServer).
// This one only carries what is sent many times per second: player states and the game state.
public class UdpGameServer : MonoBehaviour
{
    public enum State { Idle, Starting, Running, Failed, Stopped }

    public int port = 9050;

    public readonly Dictionary<string, PlayerStateDTO> RemoteStates = new Dictionary<string, PlayerStateDTO>();
    public string LastError = "";
    public State Status { get { return m_state; } }

    const int MaxPacketSize = 4 * 1024;
    const float PruneInterval = 0.5f;
    const int SioUdpConnReset = -1744830452;

    struct Packet { public byte[] data; public IPEndPoint from; }
    class Peer { public IPEndPoint endpoint; public string name; }

    Socket m_socket;
    readonly List<Thread> m_threads = new List<Thread>();
    readonly ConcurrentQueue<Packet> m_inbox = new ConcurrentQueue<Packet>();
    readonly List<Peer> m_peers = new List<Peer>();
    volatile bool m_running;
    volatile State m_state = State.Idle;
    float m_nextPrune;

    public void StartNetwork()
    {
        if (m_running) return;
        m_running = true;
        m_state = State.Starting;
        StartThread(ServerThread);
    }

    public void Disconnect()
    {
        if (!m_running) return;
        m_running = false;

        CloseSocket(m_socket); m_socket = null;

        Thread[] threads;
        lock (m_threads) { threads = m_threads.ToArray(); m_threads.Clear(); }
        foreach (Thread t in threads) if (t != Thread.CurrentThread) t.Join(500);

        m_state = State.Stopped;
        Debug.Log("[UDP SERVER] Stopped");
    }

    void OnDestroy() { Disconnect(); }

    void Update()
    {
        Packet p;
        while (m_inbox.TryDequeue(out p)) HandleMessage(p.data, p.from);

        if (m_state != State.Running) return;
        if (Time.realtimeSinceStartup < m_nextPrune) return;
        m_nextPrune = Time.realtimeSinceStartup + PruneInterval;

        // TCP decides who is in the room: forget the UDP peers that left it.
        LobbyServer lobby = ServerSession.Server;
        if (lobby == null) return;
        for (int i = m_peers.Count - 1; i >= 0; i--)
        {
            if (lobby.Players.Contains(m_peers[i].name)) continue;
            RemoteStates.Remove(m_peers[i].name);
            m_peers.RemoveAt(i);
        }
    }

    void ServerThread()
    {
        Socket socket = null;
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
            socket.ReceiveTimeout = 200;
            try { socket.IOControl(SioUdpConnReset, new byte[] { 0 }, null); } catch { }
        }
        catch (SocketException e)
        {
            if (socket != null) { try { socket.Close(); } catch { } }
            LastError = e.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? "UDP port " + port + " is already being used."
                : "Unable to create UDP server: " + e.SocketErrorCode;
            m_running = false;
            m_state = State.Failed;
            return;
        }

        m_socket = socket;
        m_state = State.Running;
        Debug.Log("[UDP SERVER] Listening on " + port);

        byte[] buffer = new byte[MaxPacketSize];
        while (m_running)
        {
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            int read;
            try { read = socket.ReceiveFrom(buffer, ref remote); }
            catch (SocketException e)
            {
                if (IsTransient(e.SocketErrorCode)) continue;
                return;
            }
            catch (ObjectDisposedException) { return; }

            if (read <= 0) continue;

            byte[] data = new byte[read];
            Buffer.BlockCopy(buffer, 0, data, 0, read);
            m_inbox.Enqueue(new Packet { data = data, from = (IPEndPoint)remote });
        }
    }

    static bool IsTransient(SocketError error)
    {
        return error == SocketError.TimedOut
            || error == SocketError.WouldBlock
            || error == SocketError.ConnectionReset
            || error == SocketError.MessageSize;
    }

    void HandleMessage(byte[] data, IPEndPoint from)
    {
        if (data.Length == 0) return;

        switch ((MsgType)data[0])
        {
            case MsgType.Text:
                HandleText(Serializer.ReadText(data), from);
                break;

            case MsgType.PlayerState:
                {
                    Peer peer = FindPeer(from);
                    PlayerStateDTO state;
                    if (peer == null || !Serializer.TryDeserialize(data, 1, out state)) return;
                    state.playerName = peer.name;   // trust the address, not what the packet says
                    if (ServerSession.StoreIfNewer(RemoteStates, state))
                        SendToOthers(Serializer.Pack(MsgType.PlayerState, Serializer.Serialize(state)), peer);
                    break;
                }
        }
    }

    // A client says who it is: HELLO:<name>. The name must be one the TCP lobby gave out.
    void HandleText(string text, IPEndPoint from)
    {
        int i = text.IndexOf(':');
        string type = i < 0 ? text : text.Substring(0, i);
        string content = i < 0 ? "" : text.Substring(i + 1);
        if (type != "HELLO") return;

        LobbyServer lobby = ServerSession.Server;
        if (lobby == null) return;

        int index = lobby.Players.IndexOf(content);
        if (index <= 0) return;   // -1: not in the room. 0: that is the host.

        Peer peer = FindPeerByName(content);
        if (peer == null)
        {
            peer = new Peer { name = content };
            m_peers.Add(peer);
        }
        peer.endpoint = from;
        SendPacket(Serializer.PackText("HELLO_OK:"), from);
    }

    // The host plays too: its own state goes to every client.
    public void SendHostState(PlayerStateDTO state)
    {
        if (m_state != State.Running || ServerSession.Server == null) return;
        state.playerName = ServerSession.Server.LocalName;
        SendToOthers(Serializer.Pack(MsgType.PlayerState, Serializer.Serialize(state)), null);
    }

    public void SendGameState(GameStateDTO game)
    {
        if (m_state != State.Running) return;
        SendToOthers(Serializer.Pack(MsgType.GameState, Serializer.Serialize(game)), null);
    }

    // Every client except 'skip' (the one who sent it). null = all of them.
    void SendToOthers(byte[] packet, Peer skip)
    {
        foreach (Peer p in m_peers)
            if (p.endpoint != null && p != skip) SendPacket(packet, p.endpoint);
    }

    void SendPacket(byte[] payload, IPEndPoint to)
    {
        Socket s = m_socket;
        if (s == null) return;

        try { s.SendTo(payload, to); }
        catch (SocketException e) { Debug.Log("[UDP SERVER] Send failed: " + e.SocketErrorCode); }
        catch (ObjectDisposedException) { }
    }

    Peer FindPeer(IPEndPoint ep)
    {
        foreach (Peer p in m_peers) if (p.endpoint != null && p.endpoint.Equals(ep)) return p;
        return null;
    }

    Peer FindPeerByName(string name)
    {
        foreach (Peer p in m_peers) if (p.name == name) return p;
        return null;
    }

    void StartThread(ThreadStart work)
    {
        Thread t = new Thread(work);
        t.IsBackground = true;
        lock (m_threads) m_threads.Add(t);
        t.Start();
    }

    void CloseSocket(Socket socket)
    {
        if (socket == null) return;
        try { socket.Close(); } catch { }
    }
}