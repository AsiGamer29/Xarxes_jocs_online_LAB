using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using UnityEngine;

// UDP game channel, client side. Created when the game starts, once TCP has told us our name.
// Virtual connection (Lab 6): Hello every half second until Welcome or Refuse arrives, then Ping
// every second. 5 s without hearing from the host means the connection is lost.
// Everything we send goes through NetSim, so latency and loss can be added on purpose.
public class UdpGameClient : MonoBehaviour
{
    public enum State { Idle, Connecting, Connected, Failed, Closed }

    public string serverIp = "127.0.0.1";
    public int port = 9050;
    public string localName = "";     // the name the server gave us over TCP

    public readonly Dictionary<int, PlayerStateDTO> RemoteStates = new Dictionary<int, PlayerStateDTO>();
    public GameStateDTO GameState;
    public bool HasGameState;
    public WelcomeDTO Welcome;
    public bool HasWelcome;
    public int PingMs;
    public string LastError = "";
    public State Status { get { return m_state; } }
    public NetSim Sim { get { return m_sim; } }

    const int MaxPacketSize = 4 * 1024;
    const int SioUdpConnReset = -1744830452;
    const float HelloInterval = 0.5f;
    const float HelloTimeout = 8f;
    const float PingInterval = 1f;
    const float Timeout = 5f;

    Socket m_socket;
    NetSim m_sim;
    IPEndPoint m_server;
    readonly List<Thread> m_threads = new List<Thread>();
    readonly ConcurrentQueue<byte[]> m_inbox = new ConcurrentQueue<byte[]>();
    volatile bool m_running;
    State m_state = State.Idle;
    float m_connectStart;
    float m_nextHello;
    float m_nextPing;
    float m_lastHeard;
    bool m_loggedFailure;

    public void StartNetwork()
    {
        if (m_running) return;

        try
        {
            m_server = new IPEndPoint(IPAddress.Parse(serverIp), port);
            m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            m_socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            m_socket.ReceiveTimeout = 200;
            try { m_socket.IOControl(SioUdpConnReset, new byte[] { 0 }, null); } catch { }
        }
        catch (SocketException e) { Fail("Unable to open UDP: " + e.SocketErrorCode); return; }
        catch (FormatException) { Fail("IP not valid"); return; }

        m_sim = new NetSim(m_socket);
        m_running = true;
        m_state = State.Connecting;
        m_connectStart = Time.realtimeSinceStartup;
        Debug.Log("[UDP CLIENT] Registering with " + serverIp + ":" + port + " as " + localName);
        StartThread(ReceiveThread);
    }

    public void SendState(PlayerStateDTO state)
    {
        if (m_state != State.Connected) return;
        state.objectId = Welcome.objectId;
        SendPacket(Serializer.Write(state));
    }

    public void Disconnect()
    {
        if (!m_running) return;
        m_running = false;

        CloseSocket(m_socket); m_socket = null;

        Thread[] threads;
        lock (m_threads) { threads = m_threads.ToArray(); m_threads.Clear(); }
        foreach (Thread t in threads) if (t != Thread.CurrentThread) t.Join(500);

        if (m_state == State.Connected || m_state == State.Connecting) m_state = State.Closed;
        NetDebugOverlay.SetLine("You", null);
        Debug.Log("[UDP CLIENT] Disconnected");
    }

    void OnDestroy() { Disconnect(); }

    void Update()
    {
        byte[] data;
        while (m_inbox.TryDequeue(out data)) OnPacketReceived(data);

        if (m_sim != null && m_socket != null) m_sim.Update();

        float now = Time.realtimeSinceStartup;
        if (m_state == State.Connecting) KeepJoining(now);
        else if (m_state == State.Connected) KeepAlive(now);

        if (m_state == State.Failed && !m_loggedFailure)
        {
            m_loggedFailure = true;
            Debug.LogWarning("[UDP CLIENT] " + LastError);
        }
    }

    void KeepJoining(float now)
    {
        if (now - m_connectStart > HelloTimeout) { Fail("The host did not answer over UDP (firewall?)"); return; }
        if (now < m_nextHello) return;
        m_nextHello = now + HelloInterval;
        SendPacket(Serializer.WriteHello(localName, ServerSession.GameVersion));
    }

    void KeepAlive(float now)
    {
        if (now - m_lastHeard > Timeout) { Fail("Connection lost"); return; }

        if (now >= m_nextPing)
        {
            m_nextPing = now + PingInterval;
            SendPacket(Serializer.WritePing(now, PingMs));
        }
        NetDebugOverlay.SetLine("You", "player " + Welcome.playerId + ", id " + Welcome.objectId + ", ping " + PingMs + " ms");
    }

    void OnPacketReceived(byte[] data)
    {
        if (data.Length == 0) return;
        NetDebugOverlay.CountReceived(data.Length);
        m_lastHeard = Time.realtimeSinceStartup;

        switch ((MsgType)data[0])
        {
            case MsgType.Welcome:
                WelcomeDTO welcome;
                if (!Serializer.TryRead(data, out welcome)) return;
                Welcome = welcome;
                HasWelcome = true;
                if (m_state == State.Connecting) m_state = State.Connected;
                break;

            case MsgType.Refuse:
                string reason;
                if (Serializer.TryReadRefuse(data, out reason)) Fail("The host refused: " + reason);
                break;

            case MsgType.Pong:
                float sentAt;
                if (Serializer.TryReadPong(data, out sentAt))
                    PingMs = Mathf.RoundToInt((Time.realtimeSinceStartup - sentAt) * 1000f);
                break;

            case MsgType.PlayerState:
                PlayerStateDTO state;
                if (Serializer.TryRead(data, out state)) ServerSession.StoreIfNewer(RemoteStates, state);
                break;

            case MsgType.GameState:
                GameStateDTO game;
                if (Serializer.TryRead(data, out game)) { GameState = game; HasGameState = true; }
                break;

            case MsgType.PlayerLeft:
                int objectId;
                if (!Serializer.TryReadPlayerLeft(data, out objectId)) return;
                RemoteStates.Remove(objectId);
                GameSpawner spawner = FindAnyObjectByType<GameSpawner>();
                if (spawner != null) spawner.RemovePlayer(objectId);
                break;

            case MsgType.Replication:
                if (WorldReplicationClient.Instance != null)
                    WorldReplicationClient.Instance.ReadPacket(new BinaryReader(new MemoryStream(data, 1, data.Length - 1)));
                break;
        }
    }

    // Only receives. Packets that do not come from the host's port are ignored. The address is not
    // compared: with a VPN (Radmin, Hamachi) the answer can come from another address of the host.
    void ReceiveThread()
    {
        Socket socket = m_socket;
        byte[] buffer = new byte[MaxPacketSize];

        while (m_running)
        {
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            int read;
            try { read = socket.ReceiveFrom(buffer, ref remote); }
            catch (SocketException e)
            {
                if (e.SocketErrorCode == SocketError.TimedOut
                    || e.SocketErrorCode == SocketError.WouldBlock
                    || e.SocketErrorCode == SocketError.ConnectionReset
                    || e.SocketErrorCode == SocketError.MessageSize) continue;
                return;
            }
            catch (ObjectDisposedException) { return; }

            if (read <= 0 || ((IPEndPoint)remote).Port != m_server.Port) continue;

            byte[] data = new byte[read];
            Buffer.BlockCopy(buffer, 0, data, 0, read);
            m_inbox.Enqueue(data);
        }
    }

    void Fail(string message)
    {
        LastError = message;
        m_state = State.Failed;
    }

    void SendPacket(byte[] packet)
    {
        if (m_sim == null || m_socket == null) return;
        NetDebugOverlay.CountSent(packet.Length);
        m_sim.Send(packet, m_server);
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
