using TMPro;
using UnityEngine;

public class TagHUD : MonoBehaviour
{
    [SerializeField] TMP_Text statusText;
    [SerializeField] TMP_Text scoreText;

    void Start()
    {
        statusText.richText = false;
        scoreText.richText = false;
    }

    void Update()
    {
        TagGame game = TagGame.Instance;
        bool show = game != null && ServerSession.UdpGame && !game.MatchOver;

        statusText.gameObject.SetActive(show);
        string score = show ? game.ScoreLine() : "";
        scoreText.gameObject.SetActive(show && score.Length > 0);
        if (!show) return;

        statusText.text = StatusLine(game.State, ServerSession.LocalPlayerName);
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
}