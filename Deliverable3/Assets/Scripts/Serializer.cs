using System;
using System.IO;

// Turns the UDP messages into bytes and back (BinaryWriter / BinaryReader, Lab 3).
// Every packet is [1 byte MsgType][payload]: the first byte says how to read the rest.
// Each TryRead reads in the same order its Write wrote, and returns false for a broken packet
// instead of throwing.
public static class Serializer
{
    const int MaxPlayers = 64;   // a Welcome with more is garbage

    // ---------------------------------------------------------------------------- player state ---
    // Client -> host only. The host sends the players to everyone in the replication packet.
    public static byte[] Write(PlayerStateDTO dto)
    {
        BinaryWriter w = Start(MsgType.PlayerState);
        w.Write(dto.sequence);
        w.Write(dto.objectId);
        w.Write(dto.x);
        w.Write(dto.y);
        w.Write(PackFlags(dto));
        return Finish(w);
    }

    // The five bools go in one byte: bit 0 facingRight, 1 moving, 2 grounded, 3 crouching, 4 dashing.
    // PlayerNetSync uses the same byte in the replication packet.
    public static byte PackFlags(PlayerStateDTO dto)
    {
        byte flags = 0;
        if (dto.facingRight) flags |= 1;
        if (dto.moving) flags |= 2;
        if (dto.grounded) flags |= 4;
        if (dto.crouching) flags |= 8;
        if (dto.dashing) flags |= 16;
        return flags;
    }

    public static void UnpackFlags(byte flags, ref PlayerStateDTO dto)
    {
        dto.facingRight = (flags & 1) != 0;
        dto.moving = (flags & 2) != 0;
        dto.grounded = (flags & 4) != 0;
        dto.crouching = (flags & 8) != 0;
        dto.dashing = (flags & 16) != 0;
    }

    public static bool TryRead(byte[] packet, out PlayerStateDTO dto)
    {
        dto = new PlayerStateDTO();
        try
        {
            BinaryReader r = Reader(packet);
            dto.sequence = r.ReadUInt32();
            dto.objectId = r.ReadInt32();
            dto.x = r.ReadSingle();
            dto.y = r.ReadSingle();
            UnpackFlags(r.ReadByte(), ref dto);
            return true;
        }
        catch (Exception) { return false; }
    }

    // ------------------------------------------------------------------------------ game state ---
    public static byte[] Write(GameStateDTO dto)
    {
        BinaryWriter w = Start(MsgType.GameState);
        w.Write(dto.round);
        w.Write((byte)dto.phase);
        w.Write(dto.taggedPlayer ?? "");
        w.Write(dto.timeLeft);
        w.Write(dto.loser ?? "");
        w.Write(dto.scores ?? "");
        w.Write(dto.winners ?? "");
        return Finish(w);
    }

    public static bool TryRead(byte[] packet, out GameStateDTO dto)
    {
        dto = new GameStateDTO();
        try
        {
            BinaryReader r = Reader(packet);
            dto.round = r.ReadInt32();
            dto.phase = (GamePhase)r.ReadByte();
            dto.taggedPlayer = r.ReadString();
            dto.timeLeft = r.ReadSingle();
            dto.loser = r.ReadString();
            dto.scores = r.ReadString();
            dto.winners = r.ReadString();
            return true;
        }
        catch (Exception) { return false; }
    }

    // ---------------------------------------------------------------------- hello and welcome ---
    public static byte[] WriteHello(string name, int version)
    {
        BinaryWriter w = Start(MsgType.Hello);
        w.Write(name ?? "");
        w.Write(version);
        return Finish(w);
    }

    public static bool TryReadHello(byte[] packet, out string name, out int version)
    {
        name = ""; version = 0;
        try
        {
            BinaryReader r = Reader(packet);
            name = r.ReadString();
            version = r.ReadInt32();
            return true;
        }
        catch (Exception) { return false; }
    }

    public static byte[] Write(WelcomeDTO dto)
    {
        BinaryWriter w = Start(MsgType.Welcome);
        w.Write(dto.playerId);
        w.Write(dto.objectId);
        w.Write(dto.objectIds.Length);
        for (int i = 0; i < dto.objectIds.Length; i++)
        {
            w.Write(dto.objectIds[i]);
            w.Write(dto.names[i] ?? "");
        }
        return Finish(w);
    }

    public static bool TryRead(byte[] packet, out WelcomeDTO dto)
    {
        dto = new WelcomeDTO();
        try
        {
            BinaryReader r = Reader(packet);
            dto.playerId = r.ReadInt32();
            dto.objectId = r.ReadInt32();
            int count = r.ReadInt32();
            if (count < 0 || count > MaxPlayers) return false;

            dto.objectIds = new int[count];
            dto.names = new string[count];
            for (int i = 0; i < count; i++)
            {
                dto.objectIds[i] = r.ReadInt32();
                dto.names[i] = r.ReadString();
            }
            return true;
        }
        catch (Exception) { return false; }
    }

    public static byte[] WriteRefuse(string reason)
    {
        BinaryWriter w = Start(MsgType.Refuse);
        w.Write(reason ?? "");
        return Finish(w);
    }

    public static bool TryReadRefuse(byte[] packet, out string reason)
    {
        reason = "";
        try { reason = Reader(packet).ReadString(); return true; }
        catch (Exception) { return false; }
    }

    // -------------------------------------------------------------- virtual connection (Lab 6) ---
    // Ping carries the client's clock and its last measured ping, so the host can show it.
    public static byte[] WritePing(float time, int pingMs)
    {
        BinaryWriter w = Start(MsgType.Ping);
        w.Write(time);
        w.Write(pingMs);
        return Finish(w);
    }

    public static bool TryReadPing(byte[] packet, out float time, out int pingMs)
    {
        time = 0f; pingMs = 0;
        try
        {
            BinaryReader r = Reader(packet);
            time = r.ReadSingle();
            pingMs = r.ReadInt32();
            return true;
        }
        catch (Exception) { return false; }
    }

    // Pong sends the Ping's time back unchanged: now minus that time is the round trip.
    public static byte[] WritePong(float time)
    {
        BinaryWriter w = Start(MsgType.Pong);
        w.Write(time);
        return Finish(w);
    }

    public static bool TryReadPong(byte[] packet, out float time)
    {
        time = 0f;
        try { time = Reader(packet).ReadSingle(); return true; }
        catch (Exception) { return false; }
    }

    public static byte[] WritePlayerLeft(int objectId)
    {
        BinaryWriter w = Start(MsgType.PlayerLeft);
        w.Write(objectId);
        return Finish(w);
    }

    public static bool TryReadPlayerLeft(byte[] packet, out int objectId)
    {
        objectId = 0;
        try { objectId = Reader(packet).ReadInt32(); return true; }
        catch (Exception) { return false; }
    }

    // ------------------------------------------------------------------------------- buttons ---
    public static byte[] WriteButtonPress(uint sequence, int key)
    {
        BinaryWriter w = Start(MsgType.ButtonPress);
        w.Write(sequence);
        w.Write((byte)key);
        return Finish(w);
    }

    public static bool TryReadButtonPress(byte[] packet, out uint sequence, out int key)
    {
        sequence = 0; key = 0;
        try
        {
            BinaryReader r = Reader(packet);
            sequence = r.ReadUInt32();
            key = r.ReadByte();
            return true;
        }
        catch (Exception) { return false; }
    }

    public static byte[] WriteButtonState(uint sequence, int mask)
    {
        BinaryWriter w = Start(MsgType.ButtonState);
        w.Write(sequence);
        w.Write(mask);
        return Finish(w);
    }

    public static bool TryReadButtonState(byte[] packet, out uint sequence, out int mask)
    {
        sequence = 0; mask = 0;
        try
        {
            BinaryReader r = Reader(packet);
            sequence = r.ReadUInt32();
            mask = r.ReadInt32();
            return true;
        }
        catch (Exception) { return false; }
    }

    // ------------------------------------------------------------------------------- shooting ---
    public static byte[] WriteShoot(uint sequence, bool facingRight)
    {
        BinaryWriter w = Start(MsgType.Shoot);
        w.Write(sequence);
        w.Write(facingRight);
        return Finish(w);
    }

    public static bool TryReadShoot(byte[] packet, out uint sequence, out bool facingRight)
    {
        sequence = 0; facingRight = true;
        try
        {
            BinaryReader r = Reader(packet);
            sequence = r.ReadUInt32();
            facingRight = r.ReadBoolean();
            return true;
        }
        catch (Exception) { return false; }
    }

    // ---------------------------------------------------------------------------- trampolines ---
    public static byte[] WriteTrampoline(int index)
    {
        BinaryWriter w = Start(MsgType.Trampoline);
        w.Write((byte)index);
        return Finish(w);
    }

    public static bool TryReadTrampoline(byte[] packet, out int index)
    {
        index = 0;
        try { index = Reader(packet).ReadByte(); return true; }
        catch (Exception) { return false; }
    }

    // ------------------------------------------------------------------------------- helpers ---
    static BinaryWriter Start(MsgType type)
    {
        BinaryWriter w = new BinaryWriter(new MemoryStream());
        w.Write((byte)type);
        return w;
    }

    static byte[] Finish(BinaryWriter w)
    {
        w.Flush();
        return ((MemoryStream)w.BaseStream).ToArray();
    }

    // Skips the MsgType byte, the caller already looked at it.
    static BinaryReader Reader(byte[] packet)
    {
        return new BinaryReader(new MemoryStream(packet, 1, packet.Length - 1));
    }
}
