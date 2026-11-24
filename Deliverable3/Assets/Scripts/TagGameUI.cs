using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// In-game HUD (who has it, time, score), the coin board and the victory / defeat screen.
// game scene starts, and filled every frame from TagGame.State.
public class TagGameUI : MonoBehaviour
{
    static readonly Color WinColor = new Color(0.4f, 1f, 0.4f);
    static readonly Color LoseColor = new Color(1f, 0.35f, 0.35f);

    [SerializeField] KeyCode toggleCoinBoardKey = KeyCode.Tab;
    const string CoinBoardPref = "ShowCoinBoard";

    TMP_Text m_status;
    TMP_Text m_score;
    TMP_Text m_title;
    TMP_Text m_finalScore;
    TMP_Text m_waiting;
    GameObject m_hudBar;
    GameObject m_panel;
    GameObject m_playAgain;

    //coin counter
    GameSpawner m_spawner;
    GameObject m_coinBoard;
    TMP_Text m_coinBoardText;
    bool m_showCoinBoard = true;
    readonly List<PlayerNetSync> m_sorted = new List<PlayerNetSync>();
    readonly StringBuilder m_sb = new StringBuilder();

    void Start()
    {
        m_spawner = GetComponent<GameSpawner>();
        m_showCoinBoard = PlayerPrefs.GetInt(CoinBoardPref, 1) == 1;
        BuildUI();
    }

    void BuildUI()
    {
        // Canvas
        GameObject canvasGo = new GameObject("TagGameCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        if (FindAnyObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        // HUD: a dark bar across the top so the white text is readable over any part of the map
        RectTransform barRt = NewRect("HUDBar", canvasGo.transform);
        barRt.anchorMin = new Vector2(0f, 1f);
        barRt.anchorMax = new Vector2(1f, 1f);
        barRt.pivot = new Vector2(0.5f, 1f);
        barRt.anchoredPosition = Vector2.zero;
        barRt.sizeDelta = new Vector2(0f, 150f);
        Image barImage = barRt.gameObject.AddComponent<Image>();
        barImage.color = new Color(0f, 0f, 0f, 0.7f);
        barImage.raycastTarget = false;
        m_hudBar = barRt.gameObject;

        m_status = NewText("StatusText", m_hudBar.transform, 52, TextAlignmentOptions.Center,
                           new Vector2(0.5f, 1f), new Vector2(0f, -45f), new Vector2(1800f, 70f));
        m_status.fontStyle = FontStyles.Bold;
        m_score = NewText("ScoreText", m_hudBar.transform, 38, TextAlignmentOptions.Center,
                          new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(1800f, 55f));

        //coin counter
        RectTransform boardRt = NewRect("CoinBoard", canvasGo.transform);
        boardRt.anchorMin = new Vector2(1f, 1f);
        boardRt.anchorMax = new Vector2(1f, 1f);
        boardRt.pivot = new Vector2(1f, 1f);
        boardRt.anchoredPosition = new Vector2(-20f, -165f);
        boardRt.sizeDelta = new Vector2(380f, 200f);
        Image boardBg = boardRt.gameObject.AddComponent<Image>();
        boardBg.color = new Color(0f, 0f, 0f, 0.55f);
        boardBg.raycastTarget = false;
        m_coinBoard = boardRt.gameObject;

        m_coinBoardText = NewText("CoinBoardText", m_coinBoard.transform, 30, TextAlignmentOptions.TopLeft,
                                  new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(350f, 180f));
        m_coinBoardText.rectTransform.anchorMin = Vector2.zero;
        m_coinBoardText.rectTransform.anchorMax = Vector2.one;
        m_coinBoardText.rectTransform.offsetMin = new Vector2(15f, 10f);
        m_coinBoardText.rectTransform.offsetMax = new Vector2(-15f, -10f);
        m_coinBoardText.overflowMode = TextOverflowModes.Ellipsis;

        m_coinBoard.SetActive(false);

        //win/lose panel
        RectTransform panelRt = NewRect("MatchOverPanel", canvasGo.transform);
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;
        Image bg = panelRt.gameObject.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.75f);
        m_panel = panelRt.gameObject;

        Vector2 mid = new Vector2(0.5f, 0.5f);
        m_title = NewText("Title", m_panel.transform, 120, TextAlignmentOptions.Center,
                          mid, new Vector2(0f, 180f), new Vector2(1200f, 160f));
        m_title.fontStyle = FontStyles.Bold;
        m_finalScore = NewText("FinalScore", m_panel.transform, 48, TextAlignmentOptions.Center,
                               mid, new Vector2(0f, 50f), new Vector2(1400f, 80f));
        m_waiting = NewText("Waiting", m_panel.transform, 36, TextAlignmentOptions.Center,
                            mid, new Vector2(0f, -70f), new Vector2(1200f, 60f));
        m_waiting.text = "Waiting for the host...";

        Button again = NewButton("Play again", m_panel.transform, new Vector2(0f, -70f), OnPlayAgain);
        m_playAgain = again.gameObject;
        NewButton("Back to menu", m_panel.transform, new Vector2(0f, -160f), OnBack);

        m_panel.SetActive(false);
        m_hudBar.SetActive(false);
    }

    void OnPlayAgain() { if (TagGame.Instance != null) TagGame.Instance.OnPlayAgainClicked(); }
    void OnBack() { if (TagGame.Instance != null) TagGame.Instance.OnBackToMenuClicked(); }

    void Update()
    {
        if (m_panel == null) return;

        if (Input.GetKeyDown(toggleCoinBoardKey))
        {
            m_showCoinBoard = !m_showCoinBoard;
            PlayerPrefs.SetInt(CoinBoardPref, m_showCoinBoard ? 1 : 0);
        }

        TagGame game = TagGame.Instance;
        if (game == null || !ServerSession.UdpGame)
        {
            SetActive(m_hudBar, false);
            SetActive(m_panel, false);
            SetActive(m_coinBoard, false);
            return;
        }

        GameStateDTO state = game.State;
        string me = ServerSession.LocalPlayerName;
        bool matchOver = state.phase == GamePhase.MatchOver;
        string score = game.ScoreLine();

        SetActive(m_panel, matchOver);
        SetActive(m_hudBar, !matchOver);
        SetActive(m_score.gameObject, score.Length > 0);

        bool showBoard = m_showCoinBoard && !matchOver;
        SetActive(m_coinBoard, showBoard);
        if (showBoard) UpdateCoinBoard(me);

        if (matchOver)
        {
            bool won = game.IsWinner(me);
            m_title.text = won ? "VICTORY!" : "DEFEAT";
            m_title.color = won ? WinColor : LoseColor;
            m_finalScore.text = score;

            bool host = ServerSession.IsGameServer;
            SetActive(m_playAgain, host);
            SetActive(m_waiting.gameObject, !host);
            return;
        }

        m_status.text = StatusLine(state, me);
        m_score.text = score;
    }

    void UpdateCoinBoard(string me)
    {
        if (m_spawner == null) return;

        m_sorted.Clear();
        foreach (KeyValuePair<string, GameObject> pair in m_spawner.Players)
        {
            if (pair.Value == null) continue;
            PlayerNetSync sync = pair.Value.GetComponent<PlayerNetSync>();
            if (sync != null) m_sorted.Add(sync);
        }
        m_sorted.Sort((a, b) =>
        {
            int c = b.Coins.CompareTo(a.Coins);
            return c != 0 ? c : string.CompareOrdinal(a.PlayerName, b.PlayerName);
        });

        m_sb.Length = 0;
        m_sb.Append("COINS  [").Append(toggleCoinBoardKey).Append("]\n");
        for (int i = 0; i < m_sorted.Count; i++)
        {
            PlayerNetSync p = m_sorted[i];
            m_sb.Append(i + 1).Append(". ");
            if (p.PlayerName == me) m_sb.Append("> ");
            m_sb.Append(p.PlayerName).Append("  ").Append(p.Coins).Append('\n');
        }

        string text = m_sb.ToString();
        if (m_coinBoardText.text != text) m_coinBoardText.text = text;

        RectTransform rt = (RectTransform)m_coinBoard.transform;
        rt.sizeDelta = new Vector2(380f, 30f + 38f * (m_sorted.Count + 1));
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

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static TMP_Text NewText(string name, Transform parent, int size, TextAlignmentOptions align,
                            Vector2 anchor, Vector2 position, Vector2 sizeDelta)
    {
        RectTransform rt = NewRect(name, parent);
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = sizeDelta;

        TextMeshProUGUI text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.alignment = align;
        text.color = Color.white;
        text.richText = false;
        text.raycastTarget = false;
        return text;
    }

    static Button NewButton(string label, Transform parent, Vector2 position, UnityAction onClick)
    {
        RectTransform rt = NewRect(label + "Button", parent);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = new Vector2(360f, 70f);

        Image image = rt.gameObject.AddComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.25f, 0.95f);
        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        RectTransform textRt = NewRect("Label", rt);
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        TextMeshProUGUI text = textRt.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 34;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        return button;
    }
}
