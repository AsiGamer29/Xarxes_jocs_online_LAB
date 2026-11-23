using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Trampoline : MonoBehaviour
{
    public float bounceForce = 20f;

    public Sprite idleSprite;
    public Sprite squashSprite;
    public float squashTime = 0.1f;

    private SpriteRenderer sr;
    private BoxCollider2D col;
    private Coroutine squash;

    static readonly List<Trampoline> All = new List<Trampoline>();

    // every PC loads the same scene, so the trampolines sorted by position get the same number everywhere
    int Index
    {
        get
        {
            SortAll();
            return All.IndexOf(this);
        }
    }

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<BoxCollider2D>();
        All.Add(this);
    }

    void OnDestroy()
    {
        All.Remove(this);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (!collision.gameObject.CompareTag("Player")) return;

        // a remote player is kinematic and follows the network, its bounce reaches us as a message
        PlayerNetSync sync = collision.gameObject.GetComponent<PlayerNetSync>();
        if (sync != null && !sync.IsLocal) return;

        Rigidbody2D playerRb = collision.rigidbody;
        if (playerRb == null) return;

        playerRb.linearVelocity = new Vector2(playerRb.linearVelocity.x, bounceForce);

        Bounce();
        ServerSession.SendTrampoline(Index);
    }

    // somebody else bounced on this trampoline, show the animation too
    public static void PlayFromNetwork(int index)
    {
        SortAll();
        if (index >= 0 && index < All.Count) All[index].Bounce();
    }

    static void SortAll()
    {
        All.Sort(ComparePosition);
    }

    static int ComparePosition(Trampoline a, Trampoline b)
    {
        int byX = a.transform.position.x.CompareTo(b.transform.position.x);
        return byX != 0 ? byX : a.transform.position.y.CompareTo(b.transform.position.y);
    }

    void Bounce()
    {
        if (squash != null) StopCoroutine(squash);
        squash = StartCoroutine(SquashRoutine());
    }

    IEnumerator SquashRoutine()
    {
        sr.sprite = squashSprite;
        yield return new WaitForSeconds(squashTime);
        sr.sprite = idleSprite;
    }
}
