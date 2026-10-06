using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class LobbyClient : MonoBehaviour
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

    const int MaxPacketSize = 64 * 1024;

    Socket m_connection;
    readonly List<Thread> m_threads = new List<Thread>();
    readonly ConcurrentQueue<byte[]> m_inbox = new ConcurrentQueue<byte[]>();
    volatile bool m_running;
    volatile State m_state = State.Idle;
    bool m_announcedConnected;

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

    void OnDestroy() { Disconnect(); }

  
    void Update()
    {
        if (!m_announcedConnected && m_state == State.Connected)
        {
            m_announcedConnected = true;
            AddEvent("[CLIENT] Connected with server " + serverIp + ":" + port);
        }

        byte[] data;
        while (m_inbox.TryDequeue(out data))
        {
            if (data == null) OnConnectionClosed();
            else OnPacketReceived(data);
        }
    }

    void OnConnectionClosed()
    {
        if (m_state == State.Closed) return;
        m_state = State.Closed;
        AddEvent("[CLIENT] Connection with the server was lost.");
    }

    void OnPacketReceived(byte[] data)
    {
        string text = Encoding.UTF8.GetString(data);
        int i = text.IndexOf(':');
        string type = i < 0 ? text : text.Substring(0, i);
        string content = i < 0 ? "" : text.Substring(i + 1);

        switch (type)
        {
            case "PLAYERS":
                Players.Clear();
                foreach (string n in content.Split(','))
                    if (n.Length > 0) Players.Add(n);
                PlayersVersion++;
                break;

            case "CHAT":
            case "LOG":
                AddEvent(content);
                break;
        }
    }

    void ClientThread()
    {
        Socket socket = null;
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            m_connection = socket;
            socket.Connect(new IPEndPoint(IPAddress.Parse(serverIp), port));
        }
        catch (SocketException e)
        {
            Fail(e.SocketErrorCode == SocketError.ConnectionRefused
                ? "Nobody listening in " + serverIp + ":" + port
                : "Unable to connect: " + e.SocketErrorCode);
            return;
        }
        catch (FormatException) { Fail("IP not valid"); return; }
        catch (ObjectDisposedException) { return; }               

        m_state = State.Connected;
        Debug.Log("[CLIENT] Connected to " + serverIp + ":" + port);

        SendString("JOIN:" + ServerSession.SanitizeName(userName)); 

        ReceiveLoop(socket);

        if (m_running) m_inbox.Enqueue(null);                      
    }

    void Fail(string message)
    {
        if (!m_running) return;                                    
        LastError = message;
        m_state = State.Failed;
        CloseSocket(m_connection);
    }

    void ReceiveLoop(Socket socket)
    {
        byte[] header = new byte[4];
        while (m_running)
        {
            if (!ReadExactly(socket, header, 4)) return;

            int size = BitConverter.ToInt32(header, 0);
            if (size <= 0 || size > MaxPacketSize) return;

            byte[] payload = new byte[size];
            if (!ReadExactly(socket, payload, size)) return;

            m_inbox.Enqueue(payload);
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

    void SendString(string text)
    {
        Socket s = m_connection;
        if (s == null) return;

        byte[] payload = Encoding.UTF8.GetBytes(text);
        byte[] framed = new byte[4 + payload.Length];
        BitConverter.GetBytes(payload.Length).CopyTo(framed, 0);
        payload.CopyTo(framed, 4);

        try { s.Send(framed); }
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
        try { socket.Shutdown(SocketShutdown.Both); } catch { }
        try { socket.Close(); } catch { }
    }
}
