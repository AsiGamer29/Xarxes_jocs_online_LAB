using System.IO;
using UnityEngine;

// A bullet. Position, velocity and owner travel in the replication packet.
// Its collider is a trigger and it is the hitbox: every frame the bullet moves that shape along its path,
// so it never tunnels through a wall, never gets stuck in one and nobody can stand on it.
// The host (or this PC alone, without a network) moves it and decides what it hits.
// On a client it is only a copy: it follows the host until the host says it is destroyed.
[RequireComponent(typeof(Collider2D))]
public class ReplicatedBullet : ReplicatedObject
{
    const float CorrectionSpeed = 15f;   // how fast a copy closes the gap to the last position of the host
    const float SnapDistance = 2f;       // a bigger gap than this is not smoothed, the copy jumps

    [SerializeField] float lifeTime = 2f;   // seconds until it disappears if it hits nothing

    public Vector2 velocity;
    public int ownerId;   // network id of the player who shot it

    static readonly RaycastHit2D[] s_hits = new RaycastHit2D[8];

    SpriteRenderer m_sprite;
    Collider2D m_collider;
    ContactFilter2D m_filter;
    Vector2 m_correction;   // what is left of the gap to the host, closed a little every frame
    float m_deadline;
    bool m_finished;
    bool m_placed;
    bool m_shotShown;

    void Awake()
    {
        m_sprite = GetComponentInChildren<SpriteRenderer>();
        m_collider = GetComponent<Collider2D>();
        m_collider.isTrigger = true;    // a solid bullet could be stood on
        m_filter.useTriggers = false;   // walls and players, not the trigger zones
        m_deadline = Time.time + lifeTime;
    }

    public void SetVelocity(Vector2 value)
    {
        velocity = value;

        // the sprite looks right, so it is flipped when the bullet goes left
        if (m_sprite != null && value.x != 0f) m_sprite.flipX = value.x < 0f;
    }

    public override void Write(BinaryWriter w)
    {
        base.Write(w);
        w.Write(velocity.x);
        w.Write(velocity.y);
        w.Write(ownerId);
    }

    public override void Read(BinaryReader r)
    {
        Vector3 shown = transform.position;
        base.Read(r);

        // the position of the host is blended in over a few frames instead of jumping to it
        if (m_placed && Vector2.Distance(shown, transform.position) < SnapDistance)
        {
            m_correction = transform.position - shown;
            transform.position = shown;
        }
        m_placed = true;

        float vx = r.ReadSingle();
        float vy = r.ReadSingle();
        SetVelocity(new Vector2(vx, vy));
        ownerId = r.ReadInt32();
        ShowShot();
    }

    // a client learns that someone shot from the first copy of its bullet and plays the animation
    void ShowShot()
    {
        if (m_shotShown) return;

        GameObject owner = ServerSession.Linking.Find(ownerId);
        if (owner == null) return;
        m_shotShown = true;

        Vector3 position = transform.position;
        position.z = owner.transform.position.z;
        transform.position = position;

        PlayerNetSync sync = owner.GetComponent<PlayerNetSync>();
        if (sync != null && !sync.IsLocal) sync.PlayShoot();
    }

    void Update()
    {
        if (m_finished) return;

        Vector2 step = velocity * Time.deltaTime;

        if (!ServerSession.HasAuthority)
        {
            Vector2 fix = m_correction * (1f - Mathf.Exp(-CorrectionSpeed * Time.deltaTime));
            m_correction -= fix;
            transform.position += (Vector3)(step + fix);
            return;
        }

        if (step.sqrMagnitude > 0f)
        {
            Physics2D.SyncTransforms();   // the collider must be where the bullet is before it is moved
            Sweep(step);
        }
        if (m_finished) return;

        transform.position += (Vector3)step;
        if (Time.time >= m_deadline) Remove();
    }

    // moves the collider along the path of this frame: the first wall stops the bullet, and so does the first player it can hit
    void Sweep(Vector2 step)
    {
        Vector2 direction = step.normalized;
        int count = m_collider.Cast(direction, m_filter, s_hits, step.magnitude);

        int nearest = -1;
        for (int i = 0; i < count; i++)
        {
            PlayerNetSync player = s_hits[i].collider.GetComponentInParent<PlayerNetSync>();
            if (player != null && !CanHit(player)) continue;
            if (nearest < 0 || s_hits[i].distance < s_hits[nearest].distance) nearest = i;
        }
        if (nearest < 0) return;

        transform.position += (Vector3)(direction * s_hits[nearest].distance);
        Stop(s_hits[nearest].collider.GetComponentInParent<PlayerNetSync>());
    }

    // a bullet goes through the player who shot it
    bool CanHit(PlayerNetSync player)
    {
        return player.NetworkId != ownerId || (BulletSpawner.Instance != null && BulletSpawner.Instance.HurtsShooter);
    }

    void Stop(PlayerNetSync hitPlayer)
    {
        if (hitPlayer != null && BulletSpawner.Instance != null) BulletSpawner.Instance.OnHit(hitPlayer);
        Remove();
    }

    void Remove()
    {
        m_finished = true;
        if (WorldReplicationHost.Instance != null) WorldReplicationHost.Instance.Despawn(this);
        else Destroy(gameObject);
    }
}
