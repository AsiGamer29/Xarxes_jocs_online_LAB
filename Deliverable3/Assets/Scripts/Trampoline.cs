using UnityEngine;
using System.Collections;

public class Trampoline : MonoBehaviour
{
    public float bounceForce = 20f;

    public Sprite idleSprite;
    public Sprite squashSprite;
    public float squashTime = 0.1f;

    private SpriteRenderer sr;
    private BoxCollider2D col;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<BoxCollider2D>();
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (!collision.gameObject.CompareTag("Player")) return;

        Rigidbody2D playerRb = collision.rigidbody;
        if (playerRb == null) return;

        playerRb.linearVelocity = new Vector2(playerRb.linearVelocity.x, bounceForce);

        Bounce();
    }

    void Bounce()
    {
        StartCoroutine(SquashRoutine());
    }

    IEnumerator SquashRoutine()
    {
        sr.sprite = squashSprite;
        yield return new WaitForSeconds(squashTime);
        sr.sprite = idleSprite;
    }
}