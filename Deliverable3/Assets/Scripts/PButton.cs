using UnityEngine;
using UnityEngine.Events;

public class PButton : MonoBehaviour
{
    [Header("Sprites")]
    public Sprite unpressedSprite;
    public Sprite pressedSprite;

    public Vector2 pressedColliderSize = new Vector2(1f, 0.3f);
    public Vector2 pressedColliderOffset = new Vector2(0f, -0.35f);

    public UnityEvent onPressed;

    private SpriteRenderer sr;
    private BoxCollider2D col;
    private bool isPressed = false;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<BoxCollider2D>();
        sr.sprite = unpressedSprite;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (isPressed) return;

        if (!collision.gameObject.CompareTag("Player")) return;

        float playerFeet = collision.collider.bounds.min.y;
        float buttonTop = col.bounds.max.y;
        if (playerFeet < buttonTop - 0.15f) return;

        Press();
    }

    void Press()
    {
        isPressed = true;
        sr.sprite = pressedSprite;

        col.size = pressedColliderSize;
        col.offset = pressedColliderOffset;

        onPressed?.Invoke();
    }
}