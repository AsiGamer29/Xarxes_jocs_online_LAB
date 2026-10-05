using System.Net;
using System.Net.Sockets;
using UnityEngine;

public static class ServerSession
{

    public const string SceneMenu   = "MainMenu";
    public const string SceneCreate = "CreateGame";
    public const string SceneJoin   = "JoinGame";
    public const string SceneLobby  = "Lobby";


    public static string PlayerName = "Player";
    public static string ServerIp   = "127.0.0.1";
    public static int    Port       = 9050;
    public static bool   IsHost;



    public static void Reset()
    {


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
