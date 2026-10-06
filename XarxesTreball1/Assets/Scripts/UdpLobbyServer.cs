using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class UdpLobbyServer : MonoBehaviour
{
    public enum State { Idle, Starting, Running, Failed, Stopped }

    public int port = 9050;
    public string hostName = "Host";

    public readonly List<string> Players = new List<string>();
    public readonly List<string> Events = new List<string>();
    public int PlayersVersion;
    public int EventsVersion;
    public string LastError = "";
    public State Status { get { return m_state; } }

    const int MaxPacketSize = 4 * 1024;
    const float HeartbeatInterval = 1f;
    const float ClientTimeout = 5f;
    const int SioUdpConnReset = -1744830452;

    struct Packet { public byte[] data; public IPEndPoint from; }
    class Member { public IPEndPoint endpoint; public string name; public float lastSeen; }

    Socket m_socket;
    readonly List<Thread> m_threads = new List<Thread>();
    readonly ConcurrentQueue<Packet> m_inbox = new ConcurrentQueue<Packet>();
    readonly List<Member> m_members = new List<Member>();
    volatile bool m_running;
    volatile State m_state = State.Idle;
    float m_lastHeartbeat;

    public int ClientCount { get { return Math.Max(0, Players.Count - 1); } }


    public void StartNetwork()
    {
        if (m_running) return;
        m_running = true;
        m_state = State.Starting;

        m_members.Clear();
        m_members.Add(new Member { endpoint = null, name = ServerSession.SanitizeName(hostName) });
        RefreshPlayers();
        AddEvent("[SERVER] Server created in " + ServerSession.GetLocalIP() + ":" + port);

        StartThread(ServerThread);
    }

    public void Disconnect()
    {
        if (!m_running) return;

        if (m_state == State.Running) { Broadcast("CLOSE:"); Broadcast("CLOSE:"); }

        m_running = false;

        CloseSocket(m_socket); m_socket = null;

        Thread[] threads;
        lock (m_threads) { threads = m_threads.ToArray(); m_threads.Clear(); }
        foreach (Thread t in threads) if (t != Thread.CurrentThread) t.Join(500);

        m_state = State.Stopped;
        Debug.Log("[SERVER] Stopped");
    }

    void OnDestroy() { Disconnect(); }


    void Update()
    {
        Packet p;
        while (m_inbox.TryDequeue(out p)) HandleMessage(p.data, p.from);

        if (m_state != State.Running) return;

        float now = Time.realtimeSinceStartup;

        for (int i = m_members.Count - 1; i >= 0; i--)
        {
            Member m = m_members[i];
            if (m.endpoint != null && now - m.lastSeen > ClientTimeout)
                RemoveMember(m, " left the lobby (timeout)");
        }

        if (now - m_lastHeartbeat >= HeartbeatInterval)
        {
            m_lastHeartbeat = now;
            Broadcast("PLAYERS:" + string.Join(",", Players.ToArray()));
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
                ? "Port " + port + " is already being used."
                : "Unable to create server: " + e.SocketErrorCode;
            m_running = false;
            m_state = State.Failed;
            return;
        }

        m_socket = socket;
        m_state = State.Running;
        Debug.Log("[SERVER] Listening (UDP) on " + port);

        ReceiveLoop(socket);
    }

    void ReceiveLoop(Socket socket)
    {
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
        string text = Encoding.UTF8.GetString(data);
        int i = text.IndexOf(':');
        string type = i < 0 ? text : text.Substring(0, i);
        string content = i < 0 ? "" : text.Substring(i + 1);

        Member member = FindMember(from);
        if (member != null) member.lastSeen = Time.realtimeSinceStartup;

        switch (type)
        {
            case "JOIN":
                if (member != null) { SendText("WELCOME:" + member.name, from); return; }
                AddMember(from, content);
                break;

            case "CHAT":
                if (member == null) { SendText("CLOSE:", from); return; }
                HandleChat(member.name, content);
                break;

            case "PING":
                if (member == null) SendText("CLOSE:", from);
                break;

            case "LEAVE":
                if (member != null) RemoveMember(member, " left the lobby");
                break;
        }
    }

    void AddMember(IPEndPoint endpoint, string rawName)
    {
        string name = UniqueName(ServerSession.SanitizeName(rawName));
        m_members.Add(new Member { endpoint = endpoint, name = name, lastSeen = Time.realtimeSinceStartup });

        SendText("WELCOME:" + name, endpoint);
        BroadcastPlayers();
        Announce("[PLAYER] " + name + " joined the lobby");
    }

    void RemoveMember(Member m, string suffix)
    {
        if (m == null) return;
        if (!m_members.Remove(m)) return;

        BroadcastPlayers();
        Announce("[PLAYER] " + m.name + suffix);
    }

    void Announce(string line)
    {
        AddEvent(line);
        Broadcast("LOG:" + line);
    }

    public void SendHostChat(string text)
    {
        if (m_state != State.Running || m_members.Count == 0) return;
        HandleChat(m_members[0].name, text);
    }

    void HandleChat(string senderName, string raw)
    {
        string text = CleanChat(raw);
        if (text.Length == 0) return;
        Announce(senderName + ": " + text);
    }

    static string CleanChat(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        string t = raw.Replace("\r", " ").Replace("\n", " ").Trim();
        if (t.Length > 200) t = t.Substring(0, 200);
        return t;
    }

    void BroadcastPlayers()
    {
        RefreshPlayers();
        Broadcast("PLAYERS:" + string.Join(",", Players.ToArray()));
    }

    void Broadcast(string text)
    {
        byte[] payload = Encoding.UTF8.GetBytes(text);
        foreach (Member m in m_members)
            if (m.endpoint != null) SendPacket(payload, m.endpoint);
    }

    void SendText(string text, IPEndPoint to)
    {
        SendPacket(Encoding.UTF8.GetBytes(text), to);
    }

    void SendPacket(byte[] payload, IPEndPoint to)
    {
        Socket s = m_socket;
        if (s == null) return;

        try { s.SendTo(payload, to); }
        catch (SocketException e) { Debug.Log("[SERVER] Send failed: " + e.SocketErrorCode); }
        catch (ObjectDisposedException) { }
    }

    Member FindMember(IPEndPoint ep)
    {
        foreach (Member m in m_members) if (m.endpoint != null && m.endpoint.Equals(ep)) return m;
        return null;
    }

    string UniqueName(string baseName)
    {
        string name = baseName;
        int n = 2;
        while (NameTaken(name)) name = baseName + n++;
        return name;
    }

    bool NameTaken(string name)
    {
        foreach (Member m in m_members)
            if (string.Equals(m.name, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    void RefreshPlayers()
    {
        Players.Clear();
        foreach (Member m in m_members) Players.Add(m.name);
        PlayersVersion++;
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