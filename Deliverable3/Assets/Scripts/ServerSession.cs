using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Globalization;
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

    // True when the game is running over UDP (movement only goes through UDP for now).
    public static bool UdpGame
    {
        get { return SelectedProtocol == Protocol.UDP && (UdpServer != null || UdpClient != null); }
    }

    public static void SendMyPosition(Vector2 p)
    {
        if (UdpServer != null) UdpServer.SendHostPosition(p);
        else if (UdpClient != null) UdpClient.SendPosition(p);
    }

    public static bool TryGetRemotePosition(string name, out Vector2 p)
    {
        p = default(Vector2);
        if (UdpServer != null) return UdpServer.RemotePositions.TryGetValue(name, out p);
        if (UdpClient != null) return UdpClient.RemotePositions.TryGetValue(name, out p);
        return false;
    }

    // "x;y"  (InvariantCulture so "1,5" vs "1.5" never breaks between PCs)
    public static string FormatXY(Vector2 p)
    {
        return p.x.ToString("F2", CultureInfo.InvariantCulture) + ";" +
               p.y.ToString("F2", CultureInfo.InvariantCulture);
    }

    // Accepts "x;y" or "x;y;name"
    public static bool TryParsePos(string content, out Vector2 pos, out string name)
    {
        pos = default(Vector2);
        name = "";
        string[] a = content.Split(new char[] { ';' }, 3);
        if (a.Length < 2) return false;

        float x, y;
        if (!float.TryParse(a[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)) return false;
        if (!float.TryParse(a[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)) return false;

        pos = new Vector2(x, y);
        if (a.Length == 3) name = a[2];
        return true;
    }
}