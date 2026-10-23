using UnityEngine;

// Added to every player in scene by GameSpawner script
public class PlayerNetSync : MonoBehaviour
{
    const float SendRate = 20f;   // packets per second
    const float Smooth = 15f;     

    string m_name;
    bool m_isLocal;
    float m_nextSend;
    bool m_hasTarget;
    Vector2 m_target;

    SpriteRenderer m_sr;
    Animator m_anim;

    void Awake()
    {
        m_sr = GetComponent<SpriteRenderer>();
        m_anim = GetComponent<Animator>();
    }

    public void Init(string playerName, bool isLocal)
    {
        m_name = playerName;
        m_isLocal = isLocal;
    }

    void Update()
    {
        // movement only over UDP, needs to be changed when we combine UDP and TCP in the same session (the movement will still work with UDP)
        if (!ServerSession.UdpGame) return;   

        if (m_isLocal)
        {
            if (Time.time >= m_nextSend)
            {
                m_nextSend = Time.time + 1f / SendRate;
                ServerSession.SendMyPosition(transform.position);
            }
            return;
        }

        Vector2 p;
        if (ServerSession.TryGetRemotePosition(m_name, out p))
        {
            if (!m_hasTarget) { transform.position = new Vector3(p.x, p.y, transform.position.z); m_hasTarget = true; }
            m_target = p;
        }
        if (!m_hasTarget) return;

        Vector2 cur = transform.position;
        Vector2 next = Vector2.Lerp(cur, m_target, 1f - Mathf.Exp(-Smooth * Time.deltaTime));
        float dx = next.x - cur.x;
        float dy = next.y - cur.y;

        if (Mathf.Abs(dx) > 0.001f && m_sr != null) m_sr.flipX = dx < 0;
        if (m_anim != null)
        {
            m_anim.SetFloat("Speed", Mathf.Abs(dx) > 0.001f ? 1f : 0f);
            m_anim.SetBool("IsGrounded", Mathf.Abs(dy) < 0.01f);
        }

        transform.position = new Vector3(next.x, next.y, transform.position.z);
    }
}