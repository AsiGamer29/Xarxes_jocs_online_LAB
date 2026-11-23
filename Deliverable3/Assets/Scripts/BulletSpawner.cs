using System.Collections.Generic;
using UnityEngine;

// Turns a shoot request into a bullet and decides what a hit does.
// It decides on the host, or on this PC alone when the game scene is opened without a network.
// The clients only ask for a shot, they never create a bullet themselves.
public class BulletSpawner : MonoBehaviour
{
    public const float Cooldown = 0.6f;
    const float CooldownTolerance = 0.15f;   // a request can arrive a bit early because of jitter

    public static BulletSpawner Instance;

    [SerializeField] GameObject bulletPrefab;
    [SerializeField] float bulletSpeed = 14f;

    [Header("Slow")]
    [SerializeField] float slowSeconds = 2f;
    [SerializeField, Range(0.1f, 1f)] float slowSpeedFactor = 0.7f;   // 1 = no slow

    [Header("Testing")]
    [SerializeField] bool hurtShooter;   // lets the bullets hit the player who shot them, to test the slow alone

    readonly Dictionary<int, float> m_nextShot = new Dictionary<int, float>();

    public bool HurtsShooter { get { return hurtShooter; } }

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void TryShoot(int shooterId, bool facingRight)
    {
        if (WorldReplicationHost.Instance == null || bulletPrefab == null)
        {
            Debug.LogWarning("[SHOTS] the scene needs a WorldReplicationHost and a Bullet Prefab in BulletSpawner");
            return;
        }

        GameObject shooter = ServerSession.Linking.Find(shooterId);
        if (shooter == null || !CanShoot(shooterId) || !IsStill(shooterId, shooter))
        {
            Debug.Log("[SHOTS] shot of " + shooterId + " refused (cooldown, or it was not standing still)");
            return;
        }

        m_nextShot[shooterId] = Time.time + Cooldown;

        // the shooter plays the animation on its own PC, the others see it from the bullet (ReplicatedBullet)
        PlayerNetSync sync = shooter.GetComponent<PlayerNetSync>();
        if (!sync.IsLocal) sync.PlayShoot();

        GameObject go = Instantiate(bulletPrefab, BulletSpawnPosition.Of(shooter, facingRight), Quaternion.identity);
        ReplicatedBullet bullet = go.GetComponent<ReplicatedBullet>();
        bullet.ownerId = shooterId;
        bullet.SetVelocity(new Vector2(facingRight ? bulletSpeed : -bulletSpeed, 0f));
        WorldReplicationHost.Instance.Spawn(bullet);
    }

    // every player moves itself, so the victim is told to slow down on its own PC
    public void OnHit(PlayerNetSync victim)
    {
        if (victim.IsLocal) victim.GetComponent<PlayerController>().ApplySlow(slowSeconds, slowSpeedFactor);
        else ServerSession.SendSlow(victim.NetworkId, slowSeconds, slowSpeedFactor);

        Debug.Log("[SHOTS] " + victim.PlayerName + " hit, slowed for " + slowSeconds + " s");
    }

    bool CanShoot(int shooterId)
    {
        float next;
        return !m_nextShot.TryGetValue(shooterId, out next) || Time.time >= next - CooldownTolerance;
    }

    // the shoot animation only exists standing still, so that is the only time a player can shoot
    public static bool IsStill(PlayerController controller)
    {
        return IsStill(controller.IsMoving, controller.IsGrounded, controller.IsCrouching, controller.IsDashing);
    }

    static bool IsStill(bool moving, bool grounded, bool crouching, bool dashing)
    {
        return grounded && !moving && !crouching && !dashing;
    }

    // the host checks the last state it has of a remote shooter, the player's own PC checked it first
    static bool IsStill(int shooterId, GameObject shooter)
    {
        if (shooter.GetComponent<PlayerNetSync>().IsLocal) return IsStill(shooter.GetComponent<PlayerController>());

        PlayerStateDTO state;
        if (!ServerSession.TryGetRemoteState(shooterId, out state)) return false;
        return IsStill(state.moving, state.grounded, state.crouching, state.dashing);
    }
}
