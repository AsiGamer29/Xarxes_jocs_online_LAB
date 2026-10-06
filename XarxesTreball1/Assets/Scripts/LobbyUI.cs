// =================================================================================================
//  LobbyUI - escena S_Lobby (sala de espera SIN chat)
//  Es la MISMA escena para el host y para los clientes. Muestra:
//    - una cabecera (IP y puerto si eres host / a qué servidor estás conectado si eres cliente)
//    - la lista de jugadores conectados
//    - un log de eventos: "Servidor creado...", "Anna se ha unido", "Marc se ha desconectado"
//
//  No sabe nada de sockets: lee Players / Events del LobbyServer o del LobbyClient, que
//  sobrevivieron al cambio de escena (DontDestroyOnLoad). Solo repinta cuando algo cambia.
//
//  Va en un GameObject vacío ("LobbyController"). Las referencias se arrastran en el Inspector.
// =================================================================================================

using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    [Header("Textos")]
    [SerializeField] TMP_Text headerText;          
    [SerializeField] TMP_Text playersText;         
    [SerializeField] TMP_Text logText;            
    [SerializeField] TMP_Text statusText;         

    [Header("Log con scroll")]
    [SerializeField] ScrollRect logScroll;

    [Header("Botón salir")]
    [SerializeField] TMP_Text leaveButtonText;    

    bool m_isHost;
    bool m_noSession;
    bool m_showedDisconnected;
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
            headerText.text = "SALA  (eres el HOST)\nIP: " + ServerSession.GetLocalIP() +
                              "     Puerto: " + ServerSession.Port + "     → pásales esta IP a los demás";
            leaveButtonText.text = "Cerrar sala";
        }
        else
        {
            headerText.text = "SALA\nConectado a " + ServerSession.ServerIp + ":" + ServerSession.Port;
            leaveButtonText.text = "Salir de la sala";
        }

        statusText.text = "";
    }

    void Update()
    {
        if (m_noSession) return;

        if (PlayersVersion != m_playersVersion) RefreshPlayers();
        if (EventsVersion != m_eventsVersion) RefreshEvents();

        if (!Connected && !m_showedDisconnected)
        {
            m_showedDisconnected = true;
            statusText.color = new Color(1f, 0.4f, 0.4f);
            statusText.text = "Desconectado del servidor. Pulsa Salir para volver al menú.";
        }
    }

    void RefreshPlayers()
    {
        m_playersVersion = PlayersVersion;

        List<string> players = Players;
        StringBuilder sb = new StringBuilder();
        sb.Append("Jugadores (").Append(players.Count).Append(")\n\n");
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

        // Bajar al final para ver siempre el último evento
        if (logScroll != null)
        {
            Canvas.ForceUpdateCanvases();
            logScroll.verticalNormalizedPosition = 0f;
        }
    }

    // ---------------------------------------------------------------------------- botón (OnClick)

    public void OnLeaveClicked()
    {
        if (!m_isHost && ServerSession.Client != null) ServerSession.Client.Leave();   // manda LEAVE:
        ServerSession.Reset();                                                       // cierra sockets
        SceneManager.LoadScene(ServerSession.SceneMenu);
    }
}
