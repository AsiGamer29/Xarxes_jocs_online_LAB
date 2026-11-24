using System.IO;
using TMPro;
using UnityEngine;

// The network side of a player, added to every player by GameSpawner. It is a ReplicatedObject, so
// the host sends every player in the same replication packet as the coins and the bullets.
// We send state, not input: every PC moves its own player. A client sends its state to the host
// about 20 times per second (PlayerState), the host keeps the last one and replicates it.
// The host decides what happens to a player (a bullet slows it down, a coin adds to its count),
// so those fields only go host -> clients.
// A client never applies the host's copy of its own position: it would pull the player back one
// round trip. From its own copy it only takes what the host decided: the slow and the coins.
public class PlayerNetSync : ReplicatedObject
{
    public const byte ReplicationType = 255;   // not in the clients' prefab list: GameSpawner creates players

    const float SendRate = 20f;   // PlayerState packets per second
    const float Smooth = 15f;     // how fast a remote player catches up with the last position
    const float BigJump = 5f;     // the level wraps around: a jump this long is not smoothed
    const string ShootTrigger = "Shoot";

    string m_name;
    bool m_isLocal;
    float m_nextSend;
    uint m_sequence;
    bool m_placed;
    bool m_hasShootTrigger;

    // The last state of a remote player: on the host from PlayerState, on a client from the replication packet.
    PlayerStateDTO m_state;
    bool m_hasState;

    // Decided by the host.
    float m_slowUntil;
    float m_slowFactor = 1f;
    int m_coins;

    SpriteRenderer m_sr;
    Animator m_anim;
    PlayerController m_controller;
    TMP_Text m_nameTag;

    void Awake()
    {
        m_sr = GetComponent<SpriteRenderer>();
        m_anim = GetComponent<Animator>();
        m_controller = GetComponent<PlayerController>();
        m_hasShootTrigger = HasTrigger(ShootTrigger);

        Transform tag = transform.Find("NameTag");
        if (tag != null) m_nameTag = tag.GetComponent<TMP_Text>();
    }

    public void Init(string playerName, bool isLocal)
    {
        m_name = playerName;
        m_isLocal = isLocal;
        type = ReplicationType;
        RefreshNameTag();
    }

    public bool IsLocal { get { return m_isLocal; } }
    public string PlayerName { get { return m_name; } }
    public int NetworkId { get { return id; } set { id = value; } }   // 0 until known
    public int Coins { get { return m_coins; } }

    // The local player plays it when it shoots, the others when the first copy of the bullet arrives.
    public void PlayShoot()
    {
        if (m_hasShootTrigger) m_anim.SetTrigger(ShootTrigger);
    }

    // Host: a bullet hit this player. Its own PC applies it when the next replication packet arrives.
    public void Slow(float seconds, float speedFactor)
    {
        m_slowUntil = Time.time + seconds;
        m_slowFactor = speedFactor;
        if (m_isLocal && m_controller != null) m_controller.ApplySlow(seconds, speedFactor);
    }

    // Host: this player took a coin.
    public void AddCoins(int value)
    {
        m_coins += value;
        RefreshNameTag();
    }

    // Host: new match, reset to 0 coins.
    public void ResetCoins()
    {
        m_coins = 0;
        RefreshNameTag();
    }

    // ------------------------------------------------------------------ replication packet ---
    // 1. position, 2. what it is doing (one byte of flags), 3. how long it stays slow and how slow,
    // 4. coins. Read must read exactly this, in this order.
    public override void Write(BinaryWriter w)
    {
        PlayerStateDTO state = m_isLocal || !m_hasState ? LocalState() : m_state;
        w.Write(state.x);
        w.Write(state.y);
        w.Write(Serializer.PackFlags(state));
        w.Write(Mathf.Max(0f, m_slowUntil - Time.time));
        w.Write(m_slowFactor);
        w.Write(m_coins);
    }

    public override void Read(BinaryReader r)
    {
        PlayerStateDTO state = new PlayerStateDTO();
        state.x = r.ReadSingle();
        state.y = r.ReadSingle();
        Serializer.UnpackFlags(r.ReadByte(), ref state);
        float slowLeft = r.ReadSingle();
        float slowFactor = r.ReadSingle();
        int coins = r.ReadInt32();

        if (coins != m_coins)
        {
            m_coins = coins;
            RefreshNameTag();
        }

        if (m_isLocal)
        {
            if (slowLeft > 0f && m_controller != null) m_controller.ApplySlow(slowLeft, slowFactor);
            return;
        }
        m_state = state;
        m_hasState = true;
    }

    void Update()
    {
        if (!ServerSession.UdpGame || id == 0) return;

        if (!m_isLocal) FollowState();
        else if (!ServerSession.IsGameServer) SendState();   // the host's own player travels in the replication packet
    }

    void SendState()
    {
        if (Time.time < m_nextSend || m_controller == null) return;
        m_nextSend = Time.time + 1f / SendRate;

        PlayerStateDTO state = LocalState();
        state.sequence = ++m_sequence;
        state.objectId = id;
        ServerSession.SendMyState(state);
    }

    PlayerStateDTO LocalState()
    {
        PlayerStateDTO state = new PlayerStateDTO { x = transform.position.x, y = transform.position.y };
        if (m_controller == null) return state;

        state.facingRight = m_controller.FacingRight;
        state.moving = m_controller.IsMoving;
        state.grounded = m_controller.IsGrounded;
        state.crouching = m_controller.IsCrouching;
        state.dashing = m_controller.IsDashing;
        return state;
    }

    void FollowState()
    {
        if (ServerSession.IsGameServer)
        {
            PlayerStateDTO received;
            if (ServerSession.TryGetRemoteState(id, out received)) { m_state = received; m_hasState = true; }
        }
        if (!m_hasState) return;

        Vector2 target = new Vector2(m_state.x, m_state.y);
        bool bigJump = m_placed && Vector2.Distance(transform.position, target) > BigJump;
        Vector2 next = (m_placed && !bigJump)
            ? Vector2.Lerp(transform.position, target, 1f - Mathf.Exp(-Smooth * Time.deltaTime))
            : target;   // first state, or it went through the side of the level: jump straight there
        m_placed = true;
        transform.position = new Vector3(next.x, next.y, transform.position.z);

        if (m_sr != null) m_sr.flipX = !m_state.facingRight;
        if (m_anim != null)
        {
            m_anim.SetFloat("Speed", m_state.moving ? 1f : 0f);
            m_anim.SetBool("IsGrounded", m_state.grounded);
            m_anim.SetBool("IsCrouching", m_state.crouching);
            m_anim.SetBool("IsDashing", m_state.dashing);
        }
    }

    void RefreshNameTag()
    {
        if (m_nameTag == null) return;
        m_nameTag.text = m_coins > 0 ? m_name + " (" + m_coins + ")" : m_name;
    }

    bool HasTrigger(string triggerName)
    {
        if (m_anim == null) return false;
        foreach (AnimatorControllerParameter parameter in m_anim.parameters)
            if (parameter.name == triggerName && parameter.type == AnimatorControllerParameterType.Trigger) return true;
        return false;
    }
}
