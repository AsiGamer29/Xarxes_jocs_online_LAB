using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class CreateServerUI : MonoBehaviour
{
    [Header("Campos")]
    [SerializeField] TMP_InputField ipField;
    [SerializeField] TMP_InputField nameField;
    [SerializeField] TMP_InputField portField;

    [Header("Botones y mensajes")]
    [SerializeField] Button createButton;
    [SerializeField] Button backButton;
    [SerializeField] TMP_Text statusText;

    bool m_busy;
    bool m_done;

    void Start()
    {
        Application.runInBackground = true;
        ServerSession.Reset();

        nameField.characterLimit = 12;
        nameField.text = ServerSession.PlayerName;

        portField.contentType = TMP_InputField.ContentType.IntegerNumber;
        portField.characterLimit = 5;
        portField.text = ServerSession.Port.ToString();

        ipField.readOnly = true;
        ipField.text = ServerSession.GetLocalIP();

        ShowStatus("", false);
    }

    public void OnCreateClicked()
    {
        if (m_busy) return;

        int port;
        if (!int.TryParse(portField.text, out port) || port < 1 || port > 65535)
        {
            ShowStatus("Unvalid port", true);
            return;
        }

        ServerSession.PlayerName = ServerSession.SanitizeName(nameField.text);
        ServerSession.Port = port;
        ServerSession.IsHost = true;

        // TCP: lobby, chat, start (and whatever reliable data comes later)
        GameObject tcpGo = new GameObject("LobbyServer");
        DontDestroyOnLoad(tcpGo);
        LobbyServer server = tcpGo.AddComponent<LobbyServer>();
        server.port = port;
        server.hostName = ServerSession.PlayerName;
        ServerSession.Server = server;

        // UDP: movement and game state. Same port number, different protocol.
        GameObject udpGo = new GameObject("UdpGameServer");
        DontDestroyOnLoad(udpGo);
        UdpGameServer udpServer = udpGo.AddComponent<UdpGameServer>();
        udpServer.port = port;
        ServerSession.UdpServer = udpServer;

        server.StartNetwork();
        udpServer.StartNetwork();
        SetBusy(true);
        ShowStatus("Creating server, please wait...", false);
    }

    public void OnBackClicked()
    {
        ServerSession.Reset();
        SceneManager.LoadScene(ServerSession.SceneMenu);
    }

    void Update()
    {
        if (!m_busy || m_done || ServerSession.Server == null || ServerSession.UdpServer == null) return;

        LobbyServer.State tcp = ServerSession.Server.Status;
        UdpGameServer.State udp = ServerSession.UdpServer.Status;

        if (tcp == LobbyServer.State.Running && udp == UdpGameServer.State.Running)
        {
            m_done = true;
            SceneManager.LoadScene(ServerSession.SceneLobby);
        }
        else if (tcp == LobbyServer.State.Failed || udp == UdpGameServer.State.Failed)
        {
            string error = tcp == LobbyServer.State.Failed ? ServerSession.Server.LastError
                                                           : ServerSession.UdpServer.LastError;
            ShowStatus(error, true);
            ServerSession.Reset();
            SetBusy(false);
        }
    }

    void SetBusy(bool busy)
    {
        m_busy = busy;
        createButton.interactable = !busy;
        backButton.interactable = !busy;
        nameField.interactable = !busy;
        portField.interactable = !busy;
    }

    void ShowStatus(string message, bool isError)
    {
        statusText.text = message;
    }
}
