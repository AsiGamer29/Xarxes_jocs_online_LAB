using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// A floor button. Red and blue buttons switch each other: pressing one shows its platform, hides
// its blocks and releases the buttons of the other colour. Only the local player can press a
// button on each PC; the press then travels through TCP so every PC sees it.

public class PButton : MonoBehaviour
{
    [Header("Sprites")]
    public Sprite unpressedSprite;
    public Sprite pressedSprite;

    public Vector2 pressedColliderSize = new Vector2(1f, 0.3f);
    public Vector2 pressedColliderOffset = new Vector2(0f, -0.35f);

    public int buttonId;

    public enum ButtonColor { Red, Blue }
    public ButtonColor buttonColor;

    public Tilemap platformTilemap;
    public Tilemap unpressedTilemap;

    private SpriteRenderer sr;
    private BoxCollider2D col;
    private bool isPressed = false;
    private Vector2 defaultColliderSize;
    private Vector2 defaultColliderOffset;

    static readonly Dictionary<int, PButton> All = new Dictionary<int, PButton>();

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<BoxCollider2D>();
        sr.sprite = unpressedSprite;
        defaultColliderSize = col.size;
        defaultColliderOffset = col.offset;
        if (platformTilemap != null) platformTilemap.gameObject.SetActive(false);
        if (unpressedTilemap != null) unpressedTilemap.gameObject.SetActive(true);
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

    public static void ResetAll()
    {
        foreach (PButton b in All.Values) b.ResetButton();
    }

    void ResetButton()
    {
        isPressed = false;
        sr.sprite = unpressedSprite;

        col.size = defaultColliderSize;
        col.offset = defaultColliderOffset;

        if (platformTilemap != null) platformTilemap.gameObject.SetActive(false);
        if (unpressedTilemap != null) unpressedTilemap.gameObject.SetActive(true);
    }

    void UnpressOtherColor()
    {
        foreach (PButton b in All.Values)
            if (b.buttonColor != buttonColor) b.ResetButton();
    }

    void Press()
    {
        if (isPressed) return;
        isPressed = true;
        sr.sprite = pressedSprite;

        col.size = pressedColliderSize;
        col.offset = pressedColliderOffset;

        if (unpressedTilemap != null) unpressedTilemap.gameObject.SetActive(false);
        if (platformTilemap != null) platformTilemap.gameObject.SetActive(true);

        UnpressOtherColor();

    }
}