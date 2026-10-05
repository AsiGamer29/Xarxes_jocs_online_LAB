using System.Net;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class JoinServerUI : MonoBehaviour
{
    [Header("Fields")]
    [SerializeField] TMP_InputField nameField;
    [SerializeField] TMP_InputField ipField;
    [SerializeField] TMP_InputField portField;

    [Header("Butons and status")]
    [SerializeField] Button joinButton;
    [SerializeField] Button cancelButton;
    [SerializeField] Button backButton;
    [SerializeField] TMP_Text statusText;

    bool m_busy;       
    bool m_done;       

    void Start()
    {
        Application.runInBackground = true;
        ServerSession.Reset();

        nameField.characterLimit = 16;
        nameField.text = ServerSession.PlayerName;

        ipField.characterLimit = 15;               // 255.255.255.255
        ipField.text = ServerSession.ServerIp;

        portField.contentType = TMP_InputField.ContentType.IntegerNumber;
        portField.characterLimit = 5;
        portField.text = ServerSession.Port.ToString();

        SetBusy(false);
        ShowStatus("", false);
    }

    public void OnJoinClicked()
    {
        if (m_busy) return;

        int port;
        if (!int.TryParse(portField.text, out port) || port < 1 || port > 65535)
        {
            ShowStatus("Puerto no válido (1-65535)", true);
            return;
        }

        string ip = ipField.text.Trim();
        IPAddress parsed;
        if (!IPAddress.TryParse(ip, out parsed))
        {
            ShowStatus("La IP no es válida (ejemplo: 192.168.1.20)", true);
            return;
        }

        ServerSession.PlayerName = ServerSession.SanitizeName(nameField.text);
        ServerSession.ServerIp = ip;
        ServerSession.Port = port;
        ServerSession.IsHost = false;

        // El cliente vive en un GameObject que NO se destruye al cambiar de escena
        GameObject go = new GameObject("LobbyClient");
        DontDestroyOnLoad(go);
    }

    public void OnCancelClicked()
    {
        ServerSession.Reset();                       
        SetBusy(false);
        ShowStatus("", false);
    }

    public void OnBackClicked()
    {
        ServerSession.Reset();
        SceneManager.LoadScene(ServerSession.SceneMenu);
    }

    void Update()
    {

    }

    void SetBusy(bool busy)
    {
        m_busy = busy;
        joinButton.interactable = !busy;
        nameField.interactable = !busy;
        ipField.interactable = !busy;
        portField.interactable = !busy;
        cancelButton.gameObject.SetActive(busy);   
    }

    void ShowStatus(string message, bool isError)
    {
        statusText.text = message;
        statusText.color = isError ? new Color(1f, 0.4f, 0.4f) : Color.white;
    }
}
