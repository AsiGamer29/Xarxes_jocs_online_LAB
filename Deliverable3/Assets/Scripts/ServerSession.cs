using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

public static class ServerSession
{
    public const string SceneMenu = "MainMenu";
    public const string SceneCreate = "CreateGame";
    public const string SceneJoin = "JoinGame";
    public const string SceneLobby = "Lobby";
    public const string SceneGame = "SampleScene";

    public static string PlayerName = "Player";
    public static string ServerIp = "127.0.0.1";
    public static int Port = 9050;
    public static bool IsHost;

    public static LobbyServer Server;
    public static LobbyClient Client;

    public enum Protocol { TCP, UDP }
    public static Protocol SelectedProtocol = Protocol.TCP;
    public static UdpLobbyServer UdpServer;
    public static UdpLobbyClient UdpClient;

    // The room as the server sent it (host first), whatever the protocol and role. Null with no session.
    public static List<string> CurrentPlayers
    {
        get
        {
            if (Server != null) return Server.Players;
            if (Client != null) return Client.Players;
            if (UdpServer != null) return UdpServer.Players;
            if (UdpClient != null) return UdpClient.Players;
            return null;
        }
    }

    // The name this PC plays with. The server may have changed it (Anna -> Anna2), so it comes from the server.
    public static string LocalPlayerName
    {
        get
        {
            if (Server != null) return Server.LocalName;
            if (Client != null) return Client.LocalName;
            if (UdpServer != null) return UdpServer.LocalName;
            if (UdpClient != null) return UdpClient.LocalName;
            return "";
        }
    }

    public static void Reset()
    {
        if (Server != null)
        {
            Server.Disconnect();
            Object.Destroy(Server.gameObject);
            Server = null;
        }

        if (Client != null)
        {
            Client.Disconnect();
            Object.Destroy(Client.gameObject);
            Client = null;
        }

        if (UdpServer != null)
        {
            UdpServer.Disconnect();
            Object.Destroy(UdpServer.gameObject);
            UdpServer = null;
        }

        if (UdpClient != null)
        {
            UdpClient.Disconnect();
            Object.Destroy(UdpClient.gameObject);
            UdpClient = null;
        }

        IsHost = false;
    }

    public static string SanitizeName(string raw)
    {
        string name = string.IsNullOrEmpty(raw) ? "" : raw.Trim();
        name = name.Replace(":", "").Replace(",", "").Replace("\n", "").Replace("\r", "");
        if (name.Length > 16) name = name.Substring(0, 16);
        if (name.Length == 0) name = "Player";
        return name;
    }

    public static string GetLocalIP()
    {
        try
        {
            using (Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                s.Connect("8.8.8.8", 65530);
                IPEndPoint ep = s.LocalEndPoint as IPEndPoint;
                if (ep != null) return ep.Address.ToString();
            }
        }
        catch { }

        try
        {
            foreach (IPAddress a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                if (a.AddressFamily == AddressFamily.InterNetwork) return a.ToString();
        }
        catch { }

        return "127.0.0.1";
    }
}