using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class CreateGameUI : MonoBehaviour
{
    [Header("Campos")]
    [SerializeField] TMP_InputField ipField;
    [SerializeField] TMP_InputField nameField;
    [SerializeField] TMP_InputField portField;

    [Header("Protocolo")]
    [SerializeField] TMP_Dropdown protocolDropdown;

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

        if (protocolDropdown != null)
        {
            protocolDropdown.ClearOptions();
            protocolDropdown.AddOptions(new List<string> { "TCP", "UDP" });
            protocolDropdown.value = (int)ServerSession.SelectedProtocol;
        }

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

        if (protocolDropdown != null)
        {
            ServerSession.SelectedProtocol = (ServerSession.Protocol)protocolDropdown.value;
        }
        else
        {
            ServerSession.SelectedProtocol = ServerSession.Protocol.TCP; //default
        }
        if (ServerSession.SelectedProtocol == ServerSession.Protocol.UDP)
        {
            GameObject udpGo = new GameObject("UdpLobbyServer");
            DontDestroyOnLoad(udpGo);
            UdpLobbyServer udpServer = udpGo.AddComponent<UdpLobbyServer>();
            udpServer.port = port;
            udpServer.hostName = ServerSession.PlayerName;
            ServerSession.UdpServer = udpServer;

            udpServer.StartNetwork();
            SetBusy(true);
            ShowStatus("Creating server, please wait...", false);
            return;
        }

        GameObject go = new GameObject("LobbyServer");
        DontDestroyOnLoad(go);
        LobbyServer server = go.AddComponent<LobbyServer>();
        server.port = port;
        server.hostName = ServerSession.PlayerName;
        ServerSession.Server = server;

        server.StartNetwork();
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
        if (m_busy && !m_done && ServerSession.UdpServer != null)
        {
            UpdateUdp();
            return;
        }

        if (!m_busy || m_done || ServerSession.Server == null) return;
        
        LobbyServer.State st = ServerSession.Server.Status;

        if (st == LobbyServer.State.Running)
        {
            m_done = true;
            SceneManager.LoadScene(ServerSession.SceneLobby);
        }
        else if (st == LobbyServer.State.Failed)
        {
            ShowStatus(ServerSession.Server.LastError, true);
            ServerSession.Reset();
            SetBusy(false);
        }
    }

    void UpdateUdp()
    {
        UdpLobbyServer.State st = ServerSession.UdpServer.Status;

        if (st == UdpLobbyServer.State.Running)
        {
            m_done = true;
            SceneManager.LoadScene(ServerSession.SceneLobby);
        }
        else if (st == UdpLobbyServer.State.Failed)
        {
            ShowStatus(ServerSession.UdpServer.LastError, true);
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
        if (protocolDropdown != null) protocolDropdown.interactable = !busy;
    }

    void ShowStatus(string message, bool isError)
    {
        statusText.text = message;
    }
}
