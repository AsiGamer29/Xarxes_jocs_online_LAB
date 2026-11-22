using System.IO;
using UnityEngine;

// a coin. position + value + kind travel in the replication packet
// only the host detects who takes it, the clients just show it
public class ReplicatedCoin : ReplicatedObject
{
    public int value = 1;
    public byte kind;   // 0 = normal coin, 1 = golden coin

    SpriteRenderer m_sr;

    void Awake()
    {
        m_sr = GetComponent<SpriteRenderer>();
    }

    public override void Write(BinaryWriter w)
    {
        base.Write(w);
        w.Write(value);
        w.Write(kind);
    }

    public override void Read(BinaryReader r)
    {
        base.Read(r);
        value = r.ReadInt32();
        kind = r.ReadByte();
        Paint();
    }

    // golden coins are yellow, normal ones keep the sprite color
    public void Paint()
    {
        if (m_sr == null) return;
        m_sr.color = kind == 1 ? new Color(1f, 0.85f, 0.2f) : Color.white;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!ServerSession.IsGameServer) return;

        // the capsule and the tag trigger of a player both enter, CoinSpawner ignores the second one
        PlayerNetSync player = other.GetComponentInParent<PlayerNetSync>();
        if (player == null || CoinSpawner.Instance == null) return;

        CoinSpawner.Instance.Collect(this, player.PlayerName);
    }
}