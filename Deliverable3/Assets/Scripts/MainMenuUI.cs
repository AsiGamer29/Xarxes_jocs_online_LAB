using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// First screen. If we came back because the connection failed, says why at the bottom.
public class MainMenuUI : MonoBehaviour
{
    void Start()
    {
        Application.runInBackground = true;
        ServerSession.Reset();
        ShowDisconnectReason();
    }

    public void OnHostClicked()
    {
        SceneManager.LoadScene(ServerSession.SceneCreate);
    }

    public void OnJoinClicked()
    {
        SceneManager.LoadScene(ServerSession.SceneJoin);
    }

    void ShowDisconnectReason()
    {
        string reason = ServerSession.DisconnectReason;
        ServerSession.DisconnectReason = "";
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (string.IsNullOrEmpty(reason) || canvas == null) return;

        GameObject go = new GameObject("DisconnectReason", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 80f);
        rt.sizeDelta = new Vector2(1200f, 60f);

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = reason;
        text.fontSize = 36;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(1f, 0.45f, 0.45f);
        text.raycastTarget = false;
    }
}
