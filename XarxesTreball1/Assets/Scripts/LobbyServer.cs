using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class LobbyServer : MonoBehaviour
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

    const int MaxPacketSize = 64 * 1024;
    struct Packet { public byte[] data; public Socket from; }
    class Member { public Socket socket; public string name; }

    Socket m_listener;
    readonly List<Socket> m_clients = new List<Socket>();
    readonly List<Thread> m_threads = new List<Thread>();
    readonly ConcurrentQueue<Packet> m_inbox = new ConcurrentQueue<Packet>();
    readonly List<Member> m_members = new List<Member>();
    volatile bool m_running;
    volatile State m_state = State.Idle;

    public int ClientCount { get { return Math.Max(0, Players.Count - 1); } }


    public void StartNetwork()
    {
        if (m_running) return;
        m_running = true;
        m_state = State.Starting;

        m_members.Clear();
        m_members.Add(new Member { socket = null, name = ServerSession.SanitizeName(hostName) });
        RefreshPlayers();
        AddEvent("[SERVER] Server created in " + ServerSession.GetLocalIP() + ":" + port);

        StartThread(ServerThread);
    }

    public void Disconnect()
    {
        if (!m_running) return;
        m_running = false;

        Socket[] clients;
        lock (m_clients) { clients = m_clients.ToArray(); m_clients.Clear(); }
        foreach (Socket c in clients) CloseSocket(c);

        CloseSocket(m_listener); m_listener = null;

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
        while (m_inbox.TryDequeue(out p))
        {
            if (p.data == null) RemoveMember(p.from);
            else HandleMessage(p.data, p.from);
        }
    }


    void ServerThread()
    {
        Socket listener = null;
        try
        {
            listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Any, port));
            listener.Listen(10);
        }
        catch (SocketException e)
        {
            if (listener != null) { try { listener.Close(); } catch { } }
            LastError = e.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? "Port " + port + " is already being used."
                : "Unable to create server: " + e.SocketErrorCode;
            m_running = false;
            m_state = State.Failed;
            return;
        }

        m_listener = listener;
        m_state = State.Running;
        Debug.Log("[SERVER] Listening on " + port);

        while (m_running)
        {
            Socket client;
            try { client = listener.Accept(); }
            catch (SocketException) { break; }
            catch (ObjectDisposedException) { break; }

            lock (m_clients) m_clients.Add(client);
            Debug.Log("[SERVER] Client connected: " + client.RemoteEndPoint);

            Socket captured = client;
            StartThread(delegate { ClientThread(captured); });
        }
    }


    void ClientThread(Socket client)
    {
        ReceiveLoop(client);
        lock (m_clients) m_clients.Remove(client);
        m_inbox.Enqueue(new Packet { data = null, from = client });
        CloseSocket(client);
    }

    void ReceiveLoop(Socket socket)
    {
        byte[] header = new byte[4];
        while (m_running)
        {
            if (!ReadExactly(socket, header, 4)) return;

            int size = BitConverter.ToInt32(header, 0);
            if (size <= 0 || size > MaxPacketSize) { Debug.Log("[SERVER] Invalid packet size: " + size); return; }

            byte[] payload = new byte[size];
            if (!ReadExactly(socket, payload, size)) return;

            m_inbox.Enqueue(new Packet { data = payload, from = socket });
        }
    }


    bool ReadExactly(Socket socket, byte[] buffer, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read;
            try { read = socket.Receive(buffer, total, count - total, SocketFlags.None); }
            catch (SocketException) { return false; }
            catch (ObjectDisposedException) { return false; }

            if (read == 0) return false;
            total += read;
        }
        return true;
    }


    void HandleMessage(byte[] data, Socket from)
    {
        string text = Encoding.UTF8.GetString(data);
        int i = text.IndexOf(':');
        string type = i < 0 ? text : text.Substring(0, i);
        string content = i < 0 ? "" : text.Substring(i + 1);

        Member member = FindMember(from);

        switch (type)
        {
            case "JOIN":
                if (member != null) return;
                AddMember(from, content);
                break;

            case "CHAT":
                if (member == null) return;
                HandleChat(member.name, content);
                break;

            case "LEAVE":
                RemoveMember(from);
                CloseSocket(from);
                break;
        }
    }

    void AddMember(Socket socket, string rawName)
    {
        string name = UniqueName(ServerSession.SanitizeName(rawName));
        m_members.Add(new Member { socket = socket, name = name });

        BroadcastPlayers();
        Announce("[PLAYER] " + name + " joined the lobby");
    }

    void RemoveMember(Socket socket)
    {
        Member m = FindMember(socket);
        if (m == null) return;

        m_members.Remove(m);
        BroadcastPlayers();
        Announce("[PLAYER]" + m.name + " left the lobby");
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
            if (m.socket != null) SendPacket(payload, m.socket);
    }

    void SendPacket(byte[] payload, Socket to)
    {
        byte[] framed = new byte[4 + payload.Length];
        BitConverter.GetBytes(payload.Length).CopyTo(framed, 0);
        payload.CopyTo(framed, 4);

        try { to.Send(framed); }
        catch (SocketException e) { Debug.Log("[SERVER] Send failed: " + e.SocketErrorCode); }
        catch (ObjectDisposedException) { }
    }

    Member FindMember(Socket s)
    {
        foreach (Member m in m_members) if (m.socket == s) return m;
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
        try { socket.Shutdown(SocketShutdown.Both); } catch { }
        try { socket.Close(); } catch { }
    }
}
