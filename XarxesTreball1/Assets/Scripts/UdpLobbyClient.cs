using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class UdpLobbyClient : MonoBehaviour
{
    public enum State { Idle, Connecting, Connected, Failed, Closed }

    public string serverIp = "127.0.0.1";
    public int port = 9050;
    public string userName = "Player";

    public readonly List<string> Players = new List<string>();
    public readonly List<string> Events = new List<string>();
    public int PlayersVersion;
    public int EventsVersion;
    public string LastError = "";
    public State Status { get { return m_state; } }

    const int MaxPacketSize = 4 * 1024;
    const int JoinRetryMs = 500;
    const int JoinTimeoutMs = 5000;
    const float PingInterval = 1f;
    const float ServerTimeout = 5f;

    Socket m_connection;
    readonly List<Thread> m_threads = new List<Thread>();
    readonly ConcurrentQueue<byte[]> m_inbox = new ConcurrentQueue<byte[]>();
    volatile bool m_running;
    volatile State m_state = State.Idle;
    bool m_announcedConnected;
    float m_lastReceived;
    float m_lastPing;

    public void StartNetwork()
    {
        if (m_running) return;
        m_running = true;
        m_state = State.Connecting;
        StartThread(ClientThread);
    }

    public void SendChat(string text)
    {
        if (m_state != State.Connected) return;
        if (string.IsNullOrWhiteSpace(text)) return;
        SendString("CHAT:" + text);
    }

    public void Leave()
    {
        SendString("LEAVE:");
        Disconnect();
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
        Debug.Log("[CLIENT] Disconnected");
    }

    void OnApplicationQuit()
    {
        if (m_running && m_state == State.Connected) SendString("LEAVE:");
    }

    void OnDestroy() { Disconnect(); }


    void Update()
    {
        float now = Time.realtimeSinceStartup;

        if (!m_announcedConnected && m_state == State.Connected)
        {
            m_announcedConnected = true;
            AddEvent("[CLIENT] Connected with server " + serverIp + ":" + port);
        }

        byte[] data;
        while (m_inbox.TryDequeue(out data))
        {
            m_lastReceived = now;
            OnPacketReceived(data, now);
        }

        if (m_state == State.Connected)
        {
            if (now - m_lastReceived > ServerTimeout) { OnConnectionClosed(); return; }

            if (now - m_lastPing >= PingInterval)
            {
                m_lastPing = now;
                SendString("PING:");
            }
        }
    }

    void OnConnectionClosed()
    {
        if (m_state == State.Closed) return;
        m_state = State.Closed;
        AddEvent("[CLIENT] Connection with the server was lost.");
    }

    void OnPacketReceived(byte[] data, float now)
    {
        string text = Encoding.UTF8.GetString(data);
        int i = text.IndexOf(':');
        string type = i < 0 ? text : text.Substring(0, i);
        string content = i < 0 ? "" : text.Substring(i + 1);

        switch (type)
        {
            case "WELCOME":
                if (m_state == State.Connecting)
                {
                    m_lastReceived = now;
                    m_lastPing = now;
                    m_state = State.Connected;
                }
                break;

            case "PLAYERS":
                List<string> list = new List<string>();
                foreach (string n in content.Split(','))
                    if (n.Length > 0) list.Add(n);

                if (!SameList(list, Players))
                {
                    Players.Clear();
                    Players.AddRange(list);
                    PlayersVersion++;
                }
                break;

            case "LOG":
                AddEvent(content);
                break;

            case "CLOSE":
                OnConnectionClosed();
                break;
        }
    }

    static bool SameList(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
        return true;
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
        catch (SocketException e)
        {
            Fail("Unable to connect: " + e.SocketErrorCode);
            return;
        }
        catch (FormatException) { Fail("IP not valid"); return; }
        catch (ObjectDisposedException) { return; }

        Debug.Log("[CLIENT] Joining (UDP) " + serverIp + ":" + port);

        string joinMessage = "JOIN:" + ServerSession.SanitizeName(userName);
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        long lastJoin = -JoinRetryMs;
        byte[] buffer = new byte[MaxPacketSize];

        while (m_running)
        {
            if (m_state == State.Connecting)
            {
                long elapsed = clock.ElapsedMilliseconds;
                if (elapsed > JoinTimeoutMs)
                {
                    Fail("Nobody answering in " + serverIp + ":" + port);
                    return;
                }
                if (elapsed - lastJoin >= JoinRetryMs)
                {
                    lastJoin = elapsed;
                    SendString(joinMessage);
                }
            }

            int read;
            try { read = socket.Receive(buffer); }
            catch (SocketException e)
            {
                if (e.SocketErrorCode == SocketError.ConnectionReset && m_state == State.Connecting)
                {
                    Fail("Nobody listening in " + serverIp + ":" + port);
                    return;
                }
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

    void SendString(string text)
    {
        Socket s = m_connection;
        if (s == null) return;

        byte[] payload = Encoding.UTF8.GetBytes(text);

        try { s.Send(payload); }
        catch (SocketException e) { Debug.Log("[CLIENT] Send failed: " + e.SocketErrorCode); }
        catch (ObjectDisposedException) { }
    }

    void AddEvent(string line)
    {
        Events.Add(line);
        if (Events.Count > 200) Events.RemoveAt(0);
        EventsVersion++;
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