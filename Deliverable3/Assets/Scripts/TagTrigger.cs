using UnityEngine;

// Trigger circle on every player. When two of them overlap it tells TagGame, which only acts on the
// host: the host is the one who decides who has it.
public class TagTrigger : MonoBehaviour
{
    PlayerNetSync m_player;
    public string PlayerName
    {
        get
        {
            if (m_player == null) m_player = GetComponentInParent<PlayerNetSync>();
            return m_player != null ? m_player.PlayerName : null;
        }
    }

    void OnTriggerStay2D(Collider2D other)
    {
        if (TagGame.Instance == null) return;

        TagTrigger otherTrigger = other.GetComponent<TagTrigger>();
        if (otherTrigger == null) return;

        string a = PlayerName;
        string b = otherTrigger.PlayerName;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a == b) return;

        TagGame.Instance.ReportTouch(a, b);
    }
}