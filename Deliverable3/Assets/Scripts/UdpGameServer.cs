using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

// UDP game channel, host side. Lobby, chat, START and buttons go through TCP (LobbyServer).
// On top of UDP we keep a virtual connection with every client (Lab 6): Hello / Welcome to join,
// Ping / Pong every second, and 5 s of silence means that client is gone.
// Everything we send goes through NetSim, so latency and loss can be added on purpose.
public class UdpGameServer : MonoBehaviour
{
    public enum State { Idle, Starting, Running, Failed, Stopped }

    public int port = 9050;

    public readonly Dictionary<int, PlayerStateDTO> RemoteStates = new Dictionary<int, PlayerStateDTO>();
    public string LastError = "";
    public State Status { get { return m_state; } }
    public NetSim Sim { get { return m_sim; } }

    const int MaxPacketSize = 4 * 1024;
    const float CheckInterval = 0.5f;
    const float Timeout = 5f;
    const int SioUdpConnReset = -1744830452;

    struct Packet { public byte[] data; public IPEndPoint from; }

    // One per client (Lab 6 slide 9). The address comes from ReceiveFrom, never from inside a message.
    class ClientProxy
    {
        public IPEndPoint endpoint;
        public string name;
        public int playerId;
        public int objectId;
        public float lastSeen;
        public int pingMs;
    }

    Socket m_socket;
    NetSim m_sim;
    readonly List<Thread> m_threads = new List<Thread>();
    readonly ConcurrentQueue<Packet> m_inbox = new ConcurrentQueue<Packet>();
    readonly List<ClientProxy> m_clients = new List<ClientProxy>();
    volatile bool m_running;
    volatile State m_state = State.Idle;
    float m_nextCheck;

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
        m_sim.Update();
        ShowClients();

        if (Time.realtimeSinceStartup < m_nextCheck) return;
        m_nextCheck = Time.realtimeSinceStartup + CheckInterval;
        CheckClients();
    }

    // TCP decides who is in the room, and UDP silence decides who is still there.
    void CheckClients()
    {
        LobbyServer lobby = ServerSession.Server;
        float now = Time.realtimeSinceStartup;

        for (int i = m_clients.Count - 1; i >= 0; i--)
        {
            ClientProxy c = m_clients[i];
            if (lobby != null && !lobby.Players.Contains(c.name)) Forget(c);
            else if (now - c.lastSeen > Timeout) TimeOut(c);
        }
    }

    // 5 s without a single packet: take its player off every screen and out of the lobby.
    void TimeOut(ClientProxy c)
    {
        Debug.Log("[UDP SERVER] " + c.name + " timed out");
        Forget(c);
        SendToOthers(Serializer.WritePlayerLeft(c.objectId), null);

        GameSpawner spawner = FindAnyObjectByType<GameSpawner>();
        if (spawner != null) spawner.RemovePlayer(c.objectId);
        if (ServerSession.Server != null) ServerSession.Server.Kick(c.name, "timed out");
    }

    void Forget(ClientProxy c)
    {
        m_clients.Remove(c);
        RemoteStates.Remove(c.objectId);
        NetDebugOverlay.SetLine(c.name, null);
    }

    void ShowClients()
    {
        float now = Time.realtimeSinceStartup;
        foreach (ClientProxy c in m_clients)
            NetDebugOverlay.SetLine(c.name, "player " + c.playerId + ", id " + c.objectId + ", ping " + c.pingMs +
                                    " ms, silent " + (now - c.lastSeen).ToString("0.0") + " s");
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
        m_sim = new NetSim(socket);
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
        NetDebugOverlay.CountReceived(data.Length);

        ClientProxy client = FindClient(from);
        if (client != null) client.lastSeen = Time.realtimeSinceStartup;

        switch ((MsgType)data[0])
        {
            case MsgType.Hello:
                HandleHello(data, from);
                break;

            case MsgType.PlayerState:
                {
                    PlayerStateDTO state;
                    if (client == null || !Serializer.TryRead(data, out state)) return;
                    state.objectId = client.objectId;   // trust the address, not what the packet says
                    if (ServerSession.StoreIfNewer(RemoteStates, state))
                        SendToOthers(Serializer.Write(state), client);
                    break;
                }

            case MsgType.Ping:
                {
                    float time;
                    int pingMs;
                    if (client == null || !Serializer.TryReadPing(data, out time, out pingMs)) return;
                    client.pingMs = pingMs;
                    SendPacket(Serializer.WritePong(time), from);
                    break;
                }
        }
    }

    // Hello: <name, version>. The name must be one the TCP lobby gave out. The answer is Welcome,
    // with the network ID of every player, so the client knows which ID is which player.
    void HandleHello(byte[] data, IPEndPoint from)
    {
        string name;
        int version;
        if (!Serializer.TryReadHello(data, out name, out version)) return;

        if (version != ServerSession.GameVersion)
        {
            SendPacket(Serializer.WriteRefuse("Different game version (host " + ServerSession.GameVersion +
                                              ", you " + version + ")"), from);
            return;
        }

        LobbyServer lobby = ServerSession.Server;
        if (lobby == null) return;

        int playerId = lobby.Players.IndexOf(name);
        if (playerId <= 0)   // -1: not in the room. 0: that is the host.
        {
            SendPacket(Serializer.WriteRefuse("You are not in this game"), from);
            return;
        }

        // The game scene is still loading here: no IDs yet. The client repeats Hello, so wait.
        GameSpawner spawner = FindAnyObjectByType<GameSpawner>();
        int objectId = spawner != null ? spawner.NetworkIdOf(name) : 0;
        if (objectId == 0) return;

        ClientProxy client = FindClientByName(name);
        if (client == null)
        {
            client = new ClientProxy { name = name };
            m_clients.Add(client);
            Debug.Log("[UDP SERVER] " + name + " joined as player " + playerId + ", network ID " + objectId);
        }
        client.endpoint = from;
        client.playerId = playerId;
        client.objectId = objectId;
        client.lastSeen = Time.realtimeSinceStartup;

        SendPacket(Serializer.Write(BuildWelcome(spawner, client)), from);
    }

    static WelcomeDTO BuildWelcome(GameSpawner spawner, ClientProxy client)
    {
        List<int> ids = new List<int>();
        List<string> names = new List<string>();
        foreach (string playerName in spawner.Players.Keys)
        {
            ids.Add(spawner.NetworkIdOf(playerName));
            names.Add(playerName);
        }

        return new WelcomeDTO
        {
            playerId = client.playerId,
            objectId = client.objectId,
            objectIds = ids.ToArray(),
            names = names.ToArray(),
        };
    }

    // The host plays too: its own state goes to every client.
    public void SendHostState(PlayerStateDTO state)
    {
        if (m_state != State.Running) return;
        SendToOthers(Serializer.Write(state), null);
    }

    public void SendGameState(GameStateDTO game)
    {
        if (m_state != State.Running) return;
        SendToOthers(Serializer.Write(game), null);
    }

    // Every client except 'skip' (the one who sent it). null = all of them.
    void SendToOthers(byte[] packet, ClientProxy skip)
    {
        foreach (ClientProxy c in m_clients)
            if (c.endpoint != null && c != skip) SendPacket(packet, c.endpoint);
    }

    void SendPacket(byte[] packet, IPEndPoint to)
    {
        if (m_sim == null || m_socket == null) return;
        NetDebugOverlay.CountSent(packet.Length);
        m_sim.Send(packet, to);
    }

    ClientProxy FindClient(IPEndPoint ep)
    {
        foreach (ClientProxy c in m_clients) if (c.endpoint != null && c.endpoint.Equals(ep)) return c;
        return null;
    }

    ClientProxy FindClientByName(string name)
    {
        foreach (ClientProxy c in m_clients) if (c.name == name) return c;
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
