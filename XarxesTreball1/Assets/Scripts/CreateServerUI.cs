using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class CreateServerUI : MonoBehaviour
{
    [Header("Fields")]
    [SerializeField] TMP_InputField ipField;       
    [SerializeField] TMP_InputField nameField;
    [SerializeField] TMP_InputField portField;

    [Header("button and status")]
    [SerializeField] Button createButton;
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
            ShowStatus("Not a valid port", true);
            return;
        }

        ServerSession.PlayerName = ServerSession.SanitizeName(nameField.text);
        ServerSession.Port = port;
        ServerSession.IsHost = true;

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
