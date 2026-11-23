using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// A floor button. Red and blue buttons switch each other: pressing one shows its platform, hides
// its blocks and releases the buttons of the other colour. The host decides which buttons are
// pressed and sends it over UDP. Clients only ask the host and then copy what it says.

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

    // red and blue buttons share the same buttonId values, so the colour is part of the key
    public int NetKey { get { return buttonId + (buttonColor == ButtonColor.Blue ? 8 : 0); } }

    // one bit per button, the bit is its NetKey
    public static int Mask
    {
        get
        {
            int mask = 0;
            foreach (KeyValuePair<int, PButton> pair in All)
                if (pair.Value.isPressed) mask |= 1 << pair.Key;
            return mask;
        }
    }

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<BoxCollider2D>();
        sr.sprite = unpressedSprite;
        defaultColliderSize = col.size;
        defaultColliderOffset = col.offset;
        if (platformTilemap != null) platformTilemap.gameObject.SetActive(false);
        if (unpressedTilemap != null) unpressedTilemap.gameObject.SetActive(true);
        All[NetKey] = this;
    }

    void OnDestroy()
    {
        PButton registered;
        if (All.TryGetValue(NetKey, out registered) && registered == this) All.Remove(NetKey);
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

        // the host (or a game without network) presses it, a client asks the host
        if (!ServerSession.UdpGame || ServerSession.IsGameServer)
        {
            Press();
            ServerSession.SendButtonState(Mask);
        }
        else ServerSession.SendButtonPress(NetKey);
    }

    // host: a client said it stepped on this button
    public static void PressFromNetwork(int key)
    {
        PButton b;
        if (All.TryGetValue(key, out b)) b.Press();
    }

    // client: copy the state of the host
    public static void ApplyMask(int mask)
    {
        foreach (KeyValuePair<int, PButton> pair in All)
            pair.Value.SetPressed((mask & (1 << pair.Key)) != 0);
        UpdateTilemaps();
    }

    public static void ResetAll()
    {
        foreach (PButton b in All.Values) b.SetPressed(false);
        UpdateTilemaps();
    }

    void Press()
    {
        if (isPressed) return;

        foreach (PButton b in All.Values)
            if (b.buttonColor != buttonColor) b.SetPressed(false);

        SetPressed(true);
        UpdateTilemaps();
    }

    void SetPressed(bool pressed)
    {
        isPressed = pressed;
        sr.sprite = pressed ? pressedSprite : unpressedSprite;
        col.size = pressed ? pressedColliderSize : defaultColliderSize;
        col.offset = pressed ? pressedColliderOffset : defaultColliderOffset;
    }

    // a colour shows its platform while any of its buttons is pressed
    static void UpdateTilemaps()
    {
        bool redOn = false, blueOn = false;
        foreach (PButton b in All.Values)
        {
            if (!b.isPressed) continue;
            if (b.buttonColor == ButtonColor.Red) redOn = true;
            else blueOn = true;
        }

        foreach (PButton b in All.Values)
        {
            bool on = b.buttonColor == ButtonColor.Red ? redOn : blueOn;
            if (b.platformTilemap != null) b.platformTilemap.gameObject.SetActive(on);
            if (b.unpressedTilemap != null) b.unpressedTilemap.gameObject.SetActive(!on);
        }
    }
}