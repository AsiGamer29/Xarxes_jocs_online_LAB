using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

// UDP game channel (client side). Created when the game starts, after the TCP lobby gave us a name.
public class UdpGameClient : MonoBehaviour
{
    public enum State { Idle, Connecting, Connected, Failed, Closed }

    public string serverIp = "127.0.0.1";
    public int port = 9050;
    public string localName = "";     // the name the server gave us over TCP

    public readonly Dictionary<string, PlayerStateDTO> RemoteStates = new Dictionary<string, PlayerStateDTO>();
    public GameStateDTO GameState;
    public bool HasGameState;
    public string LastError = "";
    public State Status { get { return m_state; } }

    const int MaxPacketSize = 4 * 1024;
    const int HelloRetryMs = 300;
    const int HelloTimeoutMs = 8000;

    Socket m_connection;
    readonly List<Thread> m_threads = new List<Thread>();
    readonly ConcurrentQueue<byte[]> m_inbox = new ConcurrentQueue<byte[]>();
    volatile bool m_running;
    volatile State m_state = State.Idle;
    bool m_loggedFailure;

    public void StartNetwork()
    {
        if (m_running) return;
        m_running = true;
        m_state = State.Connecting;
        StartThread(ClientThread);
    }

    public void SendState(PlayerStateDTO state)
    {
        if (m_state != State.Connected) return;
        SendBytes(Serializer.Pack(MsgType.PlayerState, Serializer.Serialize(state)));
    }

    public void SendButtonPress(int id)
    {
        if (m_state != State.Connected) return;
        byte[] packet = Serializer.Pack(MsgType.ButtonPress, Serializer.Serialize(new ButtonPressDTO { buttonId = id }));
        for (int i = 0; i < 3; i++) SendBytes(packet);
    }

    public void Disconnect()
    {
        if (!m_running) return;
        m_running = false;

        CloseSocket(m_connection); m_connection = null;

        Thread[] threads;
        lock (m_threads) { threads = m_threads.ToArray(); m_threads.Clear(); }
        foreach (Thread t in threads) if (t != Thread.CurrentThread) t.Join(500);

        if (m_state == State.Connected || m_state == State.Connecting) m_state = State.Closed;
        Debug.Log("[UDP CLIENT] Disconnected");
    }

    void OnDestroy() { Disconnect(); }

    void Update()
    {
        byte[] data;
        while (m_inbox.TryDequeue(out data)) OnPacketReceived(data);

        if (m_state == State.Failed && !m_loggedFailure)
        {
            m_loggedFailure = true;
            Debug.LogWarning("[UDP CLIENT] " + LastError);
        }
    }

    void OnPacketReceived(byte[] data)
    {
        if (data.Length == 0) return;

        switch ((MsgType)data[0])
        {
            case MsgType.Text:
                if (Serializer.ReadText(data).StartsWith("HELLO_OK")) m_state = State.Connected;
                break;

            case MsgType.PlayerState:
                PlayerStateDTO state;
                if (Serializer.TryDeserialize(data, 1, out state)) ServerSession.StoreIfNewer(RemoteStates, state);
                break;

            case MsgType.GameState:
                GameStateDTO game;
                if (Serializer.TryDeserialize(data, 1, out game)) { GameState = game; HasGameState = true; }
                break;

            case MsgType.ButtonPress:
                ButtonPressDTO press;
                if (Serializer.TryDeserialize(data, 1, out press)) PButton.PressFromNetwork(press.buttonId);
                break;
        }
    }

    void ClientThread()
    {
        Socket socket = null;
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.ReceiveTimeout = 200;
            m_connection = socket;
            socket.Connect(new IPEndPoint(IPAddress.Parse(serverIp), port));
        }
        catch (SocketException e) { Fail("Unable to open UDP: " + e.SocketErrorCode); return; }
        catch (FormatException) { Fail("IP not valid"); return; }
        catch (ObjectDisposedException) { return; }

        Debug.Log("[UDP CLIENT] Registering with " + serverIp + ":" + port + " as " + localName);

        byte[] hello = Serializer.PackText("HELLO:" + localName);
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        long lastHello = -HelloRetryMs;
        byte[] buffer = new byte[MaxPacketSize];

        while (m_running)
        {
            if (m_state == State.Connecting)
            {
                long elapsed = clock.ElapsedMilliseconds;
                if (elapsed > HelloTimeoutMs) { Fail("The UDP server did not answer (firewall?)"); return; }
                if (elapsed - lastHello >= HelloRetryMs)
                {
                    lastHello = elapsed;
                    SendBytes(hello);
                }
            }

            int read;
            try { read = socket.Receive(buffer); }
            catch (SocketException e)
            {
                if (e.SocketErrorCode == SocketError.TimedOut
                    || e.SocketErrorCode == SocketError.WouldBlock
                    || e.SocketErrorCode == SocketError.ConnectionReset
                    || e.SocketErrorCode == SocketError.MessageSize) continue;
                return;
            }
            catch (ObjectDisposedException) { return; }

            if (read <= 0) continue;

            byte[] data = new byte[read];
            Buffer.BlockCopy(buffer, 0, data, 0, read);
            m_inbox.Enqueue(data);
        }
    }

    void Fail(string message)
    {
        if (!m_running) return;
        LastError = message;
        m_state = State.Failed;
        CloseSocket(m_connection);
    }

    void SendBytes(byte[] packet)
    {
        Socket s = m_connection;
        if (s == null) return;

        try { s.Send(packet); }
        catch (SocketException e) { Debug.Log("[UDP CLIENT] Send failed: " + e.SocketErrorCode); }
        catch (ObjectDisposedException) { }
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