// =================================================================================================
//  CreateGameUI - escena S_CreateGame (el HOST crea el servidor)
//
//  La interfaz se monta en el editor (Canvas + TextMeshPro). Este script solo:
//    - rellena los campos al empezar (tu IP, nombre, puerto)
//    - al pulsar "Crear partida" arranca el LobbyServer y espera a que esté listo
//    - cuando el servidor está escuchando, carga S_Lobby
//
//  Va en un GameObject vacío ("CreateGameController"). Las referencias se arrastran en el Inspector.
// =================================================================================================

using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class CreateGameUI : MonoBehaviour
{
    [Header("Campos")]
    [SerializeField] TMP_InputField ipField;       // solo lectura: tu IP, para que la copies
    [SerializeField] TMP_InputField nameField;
    [SerializeField] TMP_InputField portField;

    [Header("Botones y mensajes")]
    [SerializeField] Button createButton;
    [SerializeField] Button backButton;
    [SerializeField] TMP_Text statusText;

    bool m_busy;       // esperando a que el servidor arranque
    bool m_done;       // ya hemos pedido cambiar de escena

    void Start()
    {
        Application.runInBackground = true;
        ServerSession.Reset();                       // por si volvemos desde una sala

        nameField.characterLimit = 16;
        nameField.text = ServerSession.PlayerName;

        portField.contentType = TMP_InputField.ContentType.IntegerNumber;   // solo números
        portField.characterLimit = 5;
        portField.text = ServerSession.Port.ToString();

        ipField.readOnly = true;                   // se puede seleccionar y copiar, pero no editar
        ipField.text = ServerSession.GetLocalIP();

        ShowStatus("", false);
    }

    // ---------------------------------------------------------------------------- botones (OnClick)

    public void OnCreateClicked()
    {
        if (m_busy) return;

        int port;
        if (!int.TryParse(portField.text, out port) || port < 1 || port > 65535)
        {
            ShowStatus("Puerto no válido (1-65535)", true);
            return;
        }

        ServerSession.PlayerName = ServerSession.SanitizeName(nameField.text);
        ServerSession.Port = port;
        ServerSession.IsHost = true;

        // El servidor vive en un GameObject que NO se destruye al cambiar de escena
        GameObject go = new GameObject("LobbyServer");
        DontDestroyOnLoad(go);
        LobbyServer server = go.AddComponent<LobbyServer>();
        server.port = port;
        server.hostName = ServerSession.PlayerName;
        ServerSession.Server = server;

        server.StartNetwork();
        SetBusy(true);
        ShowStatus("Creando servidor...", false);
    }

    public void OnBackClicked()
    {
        ServerSession.Reset();
        SceneManager.LoadScene(ServerSession.SceneMenu);
    }

    // ---------------------------------------------------------------------------- lógica

    void Update()
    {
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
        statusText.color = isError ? new Color(1f, 0.4f, 0.4f) : Color.white;
    }
}
