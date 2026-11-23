using UnityEngine;

// Goes on an empty child of the Player prefab and marks where its bullets come out.
// Place it in the Editor for a player facing right. For a player facing left it is mirrored.
public class BulletSpawnPosition : MonoBehaviour
{
    public static Vector3 Of(GameObject player, bool facingRight)
    {
        Vector3 pivot = player.transform.position;
        BulletSpawnPosition marker = player.GetComponentInChildren<BulletSpawnPosition>();

        if (marker == null)
        {
            Debug.LogWarning("[SHOTS] the Player prefab has no Bullet Spawn Position, using its pivot");
            return pivot;
        }

        Vector3 offset = marker.transform.position - pivot;
        float side = facingRight ? 1f : -1f;
        return new Vector3(pivot.x + Mathf.Abs(offset.x) * side, pivot.y + offset.y, pivot.z);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, 0.1f);
    }
}
