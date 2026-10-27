using UnityEngine;
using UnityEngine.UI;

public class MatchOverUI : MonoBehaviour
{
    [SerializeField] GameObject panel;
    [SerializeField] Button playAgainButton;
    [SerializeField] Button backButton;

    void Start()
    {
        panel.SetActive(false);

        playAgainButton.gameObject.SetActive(ServerSession.IsGameServer);

        playAgainButton.onClick.AddListener(delegate { TagGame.Instance.OnPlayAgainClicked(); });
        backButton.onClick.AddListener(delegate { TagGame.Instance.OnBackToMenuClicked(); });
    }

    void Update()
    {
        bool show = TagGame.Instance != null && TagGame.Instance.MatchOver;
        if (panel.activeSelf != show) panel.SetActive(show);
    }
}