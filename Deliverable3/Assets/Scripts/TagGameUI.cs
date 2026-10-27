using TMPro;
using UnityEngine;
using UnityEngine.UI;


public class TagGameUI : MonoBehaviour
{
    [Header("HUD")]
    [SerializeField] TMP_Text statusText;
    [SerializeField] TMP_Text scoreText;

    [Header("Win / lose screen")]
    [SerializeField] GameObject matchOverPanel;
    [SerializeField] TMP_Text titleText;
    [SerializeField] TMP_Text finalScoreText;
    [SerializeField] TMP_Text waitingText;
    [SerializeField] Button playAgainButton;
    [SerializeField] Button backButton;

    static readonly Color WinColor = new Color(0.4f, 1f, 0.4f);
    static readonly Color LoseColor = new Color(1f, 0.35f, 0.35f);

    void Start()
    {
        statusText.richText = false;
        scoreText.richText = false;
        titleText.richText = false;
        finalScoreText.richText = false;

        matchOverPanel.SetActive(false);
        statusText.gameObject.SetActive(false);
        scoreText.gameObject.SetActive(false);

        playAgainButton.onClick.AddListener(delegate { if (TagGame.Instance != null) TagGame.Instance.OnPlayAgainClicked(); });
        backButton.onClick.AddListener(delegate { if (TagGame.Instance != null) TagGame.Instance.OnBackToMenuClicked(); });
    }

    void Update()
    {
        TagGame game = TagGame.Instance;
        if (game == null || !ServerSession.UdpGame)
        {
            SetActive(statusText.gameObject, false);
            SetActive(scoreText.gameObject, false);
            SetActive(matchOverPanel, false);
            return;
        }

        GameStateDTO state = game.State;
        string me = ServerSession.LocalPlayerName;
        bool matchOver = state.phase == GamePhase.MatchOver;
        string score = game.ScoreLine();

        SetActive(matchOverPanel, matchOver);
        SetActive(statusText.gameObject, !matchOver);
        SetActive(scoreText.gameObject, !matchOver && score.Length > 0);

        if (matchOver)
        {
            bool won = game.IsWinner(me);
            titleText.text = won ? "VICTORY!" : "DEFEAT";
            titleText.color = won ? WinColor : LoseColor;
            finalScoreText.text = score;

            bool host = ServerSession.IsGameServer;
            SetActive(playAgainButton.gameObject, host);
            SetActive(waitingText.gameObject, !host);
            return;
        }

        statusText.text = StatusLine(state, me);
        scoreText.text = score;
    }

    static string StatusLine(GameStateDTO state, string me)
    {
        int seconds = Mathf.CeilToInt(state.timeLeft);

        switch (state.phase)
        {
            case GamePhase.Playing:
                string who;
                if (state.taggedPlayer == me) who = "Tag someone to free yourself!";
                else who = state.taggedPlayer + " is IT!";
                return who + "   " + seconds + "s";

            case GamePhase.RoundOver:
                string lost;
                if (state.loser == me) lost = "YOU LOST!";
                else lost = state.loser + " lost the round!";
                return lost + "   Next round in " + seconds + "s";

            default:
                return "Waiting for at least 2 players...";
        }
    }

    static void SetActive(GameObject go, bool active)
    {
        if (go.activeSelf != active) go.SetActive(active);
    }
}