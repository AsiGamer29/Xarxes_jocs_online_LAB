using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuScreens : MonoBehaviour
{
    public enum MenuMode { Main, Create, Join }
    public MenuMode mode = MenuMode.Main;

    string m_name, m_ip, m_port, m_localIp;
    string m_error = "";
    bool m_busy;       
    bool m_done;       

    void Start()
    {
        Application.runInBackground = true;
        ServerSession.Reset();                 

        m_name = ServerSession.PlayerName;
        m_ip = ServerSession.ServerIp;
        m_port = ServerSession.Port.ToString();
        m_localIp = ServerSession.GetLocalIP();
    }


    void Update()
    {
        if (!m_busy || m_done) return;

    }

    bool ReadCommon(out int port)
    {
        ServerSession.PlayerName = ServerSession.SanitizeName(m_name);
        if (!int.TryParse(m_port, out port) || port < 1 || port > 65535)
        {
            m_error = "Not a valid port";
            return false;
        }
        ServerSession.Port = port;
        return true;
    }

    void CreateGame()
    {
        m_error = "";
        int port;
        if (!ReadCommon(out port)) return;

        ServerSession.IsHost = true;
    }

    void JoinGame()
    {
        m_error = "";
        int port;
        if (!ReadCommon(out port)) return;

        System.Net.IPAddress parsed;
        if (!System.Net.IPAddress.TryParse(m_ip.Trim(), out parsed))
        {
            m_error = "La IP no es válida (ejemplo: 192.168.1.20)";
            return;
        }
        ServerSession.ServerIp = m_ip.Trim();
        ServerSession.IsHost = false;

        m_busy = true;
    }

    void CancelJoin()
    {
        ServerSession.Reset();
        m_busy = false;
    }

}