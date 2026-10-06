using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    [SerializeField] TMP_Text headerText;          
    [SerializeField] TMP_Text playersText;         
    [SerializeField] TMP_Text logText;            
    [SerializeField] TMP_Text statusText;         
    [SerializeField] ScrollRect logScroll;
    [SerializeField] TMP_Text leaveButtonText;
    [SerializeField] TMP_InputField chatInput;
    [SerializeField] Button sendButton;

    bool m_isHost;
    bool m_noSession;
    bool m_showedDisconnected;
    bool m_leaving;
    int m_playersVersion = -1;
    int m_eventsVersion = -1;



    bool HasSession { get { return m_isHost ? ServerSession.Server != null : ServerSession.Client != null; } }
    List<string> Players { get { return m_isHost ? ServerSession.Server.Players : ServerSession.Client.Players; } }
    List<string> Events { get { return m_isHost ? ServerSession.Server.Events : ServerSession.Client.Events; } }
    int PlayersVersion { get { return m_isHost ? ServerSession.Server.PlayersVersion : ServerSession.Client.PlayersVersion; } }
    int EventsVersion { get { return m_isHost ? ServerSession.Server.EventsVersion : ServerSession.Client.EventsVersion; } }
    bool Connected
    {
        get
        {
            return m_isHost ? ServerSession.Server.Status == LobbyServer.State.Running
                            : ServerSession.Client.Status == LobbyClient.State.Connected;
        }
    }

    void Start()
    {
        Application.runInBackground = true;
        m_isHost = ServerSession.IsHost;

        if (!HasSession)
        {
            m_noSession = true;
            SceneManager.LoadScene(ServerSession.SceneMenu);
            return;
        }

        if (m_isHost)
        {
            headerText.text = "LOBBY\nHosting in IP: " + ServerSession.GetLocalIP() +
                              ": " + ServerSession.Port;
            leaveButtonText.text = "Stop hosting";
        }
        else
        {
            headerText.text = "LOBBY\nConnected to " + ServerSession.ServerIp + ":" + ServerSession.Port;
        }

        logText.richText = false;
        chatInput.characterLimit = 200;
        chatInput.lineType = TMP_InputField.LineType.SingleLine;
        chatInput.onSubmit.AddListener(delegate { OnSendClicked(); });
        sendButton.onClick.AddListener(OnSendClicked);

        statusText.text = "";
    }

    void Update()
    {
        if (m_noSession || m_leaving || !HasSession) return;

        if (PlayersVersion != m_playersVersion) RefreshPlayers();
        if (EventsVersion != m_eventsVersion) RefreshEvents();

        if (!Connected && !m_showedDisconnected)
        {
            m_showedDisconnected = true;
            chatInput.interactable = false;
            sendButton.interactable = false;
            statusText.text = "Disconnected.";
        }
    }

    void RefreshPlayers()
    {
        m_playersVersion = PlayersVersion;

        List<string> players = Players;
        StringBuilder sb = new StringBuilder();
        sb.Append("Players (").Append(players.Count).Append(")\n\n");
        for (int i = 0; i < players.Count; i++)
        {
            sb.Append("• ").Append(players[i]);
            if (i == 0) sb.Append("  (host)");     
            sb.Append('\n');
        }
        playersText.text = sb.ToString();
    }

    void RefreshEvents()
    {
        m_eventsVersion = EventsVersion;

        StringBuilder sb = new StringBuilder();
        foreach (string line in Events) sb.Append(line).Append('\n');
        logText.text = sb.ToString();

        if (logScroll != null)
        {
            Canvas.ForceUpdateCanvases();
            logScroll.verticalNormalizedPosition = 0f;
        }
    }

    public void OnLeaveClicked()
    {
        m_leaving = true;
        if (!m_isHost && ServerSession.Client != null) ServerSession.Client.Leave();
        ServerSession.Reset();
        SceneManager.LoadScene(ServerSession.SceneMenu);
    }

    public void OnSendClicked()
    {
        string text = chatInput.text.Trim();
        if (text.Length == 0) return;

        if (m_isHost) ServerSession.Server.SendHostChat(text);
        else ServerSession.Client.SendChat(text);

        chatInput.text = "";
        chatInput.ActivateInputField();
    }
}
