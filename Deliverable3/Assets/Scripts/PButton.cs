using UnityEngine;
using UnityEngine.Events;

public class PButton : MonoBehaviour
{
    [Header("Sprites")]
    public Sprite unpressedSprite;
    public Sprite pressedSprite;

    public Vector2 pressedColliderSize = new Vector2(1f, 0.3f);
    public Vector2 pressedColliderOffset = new Vector2(0f, -0.35f);

    public int buttonId;

    public UnityEvent onPressed;

    private SpriteRenderer sr;
    private BoxCollider2D col;
    private bool isPressed = false;

    static readonly System.Collections.Generic.Dictionary<int, PButton> All = new System.Collections.Generic.Dictionary<int, PButton>();

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<BoxCollider2D>();
        sr.sprite = unpressedSprite;
        All[buttonId] = this;
    }

    void OnDestroy()
    {
        All.Remove(buttonId);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (isPressed) return;

        if (!collision.gameObject.CompareTag("Player")) return;

        PlayerNetSync sync = collision.gameObject.GetComponent<PlayerNetSync>();
        if (sync != null && !sync.IsLocal) return;

        float playerFeet = collision.collider.bounds.min.y;
        float buttonTop = col.bounds.max.y;
        if (playerFeet < buttonTop - 0.15f) return;

        Press();
        ServerSession.SendButtonPress(buttonId);
    }

    public static void PressFromNetwork(int id)
    {
        PButton b;
        if (All.TryGetValue(id, out b)) b.Press();
    }

    void Press()
    {
        if (isPressed) return;
        isPressed = true;
        sr.sprite = pressedSprite;

        col.size = pressedColliderSize;
        col.offset = pressedColliderOffset;

        onPressed?.Invoke();
    }
}