using UnityEngine;

// Added to every player by GameSpawner. Game data only travels over UDP.
// The local player sends its state about 20 times per second, in binary (PlayerStateDTO).
// The others follow the last state received: the position is smoothed so that 20 packets per
// second look like continuous movement, and the animation uses the state that was sent.
public class PlayerNetSync : MonoBehaviour
{
    const float SendRate = 20f;   // packets per second
    const float Smooth = 15f;     // how fast a remote player catches up with the last position

    string m_name;
    bool m_isLocal;
    float m_nextSend;
    uint m_sequence;
    bool m_placed;

    SpriteRenderer m_sr;
    Animator m_anim;
    PlayerController m_controller;

    void Awake()
    {
        m_sr = GetComponent<SpriteRenderer>();
        m_anim = GetComponent<Animator>();
        m_controller = GetComponent<PlayerController>();
    }

    public void Init(string playerName, bool isLocal)
    {
        m_name = playerName;
        m_isLocal = isLocal;
    }

    public bool IsLocal { get { return m_isLocal; } }

    void Update()
    {
        if (!ServerSession.UdpGame) return;

        if (m_isLocal) SendState();
        else FollowState();
    }

    void SendState()
    {
        if (Time.time < m_nextSend || m_controller == null) return;
        m_nextSend = Time.time + 1f / SendRate;

        PlayerStateDTO state = new PlayerStateDTO
        {
            sequence = ++m_sequence,
            playerName = m_name,
            x = transform.position.x,
            y = transform.position.y,
            facingRight = m_controller.FacingRight,
            moving = m_controller.IsMoving,
            grounded = m_controller.IsGrounded,
            crouching = m_controller.IsCrouching,
            dashing = m_controller.IsDashing,
        };
        ServerSession.SendMyState(state);
    }

    void FollowState()
    {
        PlayerStateDTO state;
        if (!ServerSession.TryGetRemoteState(m_name, out state)) return;

        Vector2 target = new Vector2(state.x, state.y);
        Vector2 next = m_placed
            ? Vector2.Lerp(transform.position, target, 1f - Mathf.Exp(-Smooth * Time.deltaTime))
            : target;   // first packet: jump straight there
        m_placed = true;
        transform.position = new Vector3(next.x, next.y, transform.position.z);

        if (m_sr != null) m_sr.flipX = !state.facingRight;
        if (m_anim != null)
        {
            m_anim.SetFloat("Speed", state.moving ? 1f : 0f);
            m_anim.SetBool("IsGrounded", state.grounded);
            m_anim.SetBool("IsCrouching", state.crouching);
            m_anim.SetBool("IsDashing", state.dashing);
        }
    }
}
