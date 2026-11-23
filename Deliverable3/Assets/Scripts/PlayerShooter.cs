using UnityEngine;

// Local player only, GameSpawner adds it. The key asks the host for a bullet and the host decides.
// It only works standing still, the same rule the host checks.
// In the game scene opened without a network this PC is its own host, and it works the same.
public class PlayerShooter : MonoBehaviour
{
    [SerializeField] KeyCode shootKey = KeyCode.E;

    PlayerNetSync m_sync;
    PlayerController m_controller;
    float m_nextShot;

    void Start()
    {
        m_sync = GetComponent<PlayerNetSync>();
        m_controller = GetComponent<PlayerController>();
    }

    void Update()
    {
        if (m_sync.NetworkId == 0 || !Input.GetKeyDown(shootKey) || Time.time < m_nextShot) return;
        if (!BulletSpawner.IsStill(m_controller)) return;

        m_nextShot = Time.time + BulletSpawner.Cooldown;
        m_sync.PlayShoot();

        if (!ServerSession.HasAuthority) ServerSession.SendShoot(m_controller.FacingRight);
        else if (BulletSpawner.Instance != null) BulletSpawner.Instance.TryShoot(m_sync.NetworkId, m_controller.FacingRight);
        else Debug.LogWarning("[SHOTS] BulletSpawner is missing in the scene");
    }
}
