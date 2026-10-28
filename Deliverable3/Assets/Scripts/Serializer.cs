using System;
using System.IO;
using System.Text;

// Turns the game messages into bytes and back (BinaryWriter / BinaryReader, Lab 3).
// Every UDP packet is [1 byte MsgType][payload]: the first byte says how to read the rest.
public static class Serializer
{
    public static byte[] Pack(MsgType type, byte[] payload)
    {
        byte[] packet = new byte[1 + payload.Length];
        packet[0] = (byte)type;
        Buffer.BlockCopy(payload, 0, packet, 1, payload.Length);
        return packet;
    }

    public static byte[] PackText(string text)
    {
        return Pack(MsgType.Text, Encoding.UTF8.GetBytes(text));
    }

    public static string ReadText(byte[] packet)
    {
        return Encoding.UTF8.GetString(packet, 1, packet.Length - 1);
    }

    // The five bools go in one byte: bit 0 facingRight, 1 moving, 2 grounded, 3 crouching, 4 dashing.
    public static byte[] Serialize(PlayerStateDTO dto)
    {
        MemoryStream stream = new MemoryStream();
        BinaryWriter writer = new BinaryWriter(stream);

        writer.Write(dto.sequence);
        writer.Write(dto.playerName ?? "");
        writer.Write(dto.x);
        writer.Write(dto.y);

        byte flags = 0;
        if (dto.facingRight) flags |= 1;
        if (dto.moving) flags |= 2;
        if (dto.grounded) flags |= 4;
        if (dto.crouching) flags |= 8;
        if (dto.dashing) flags |= 16;
        writer.Write(flags);

        return stream.ToArray();
    }

    // Reads in the same order it was written. Returns false for a broken packet instead of throwing.
    public static bool TryDeserialize(byte[] data, int offset, out PlayerStateDTO dto)
    {
        dto = new PlayerStateDTO();
        try
        {
            BinaryReader reader = new BinaryReader(new MemoryStream(data, offset, data.Length - offset));
            dto.sequence = reader.ReadUInt32();
            dto.playerName = reader.ReadString();
            dto.x = reader.ReadSingle();
            dto.y = reader.ReadSingle();

            byte flags = reader.ReadByte();
            dto.facingRight = (flags & 1) != 0;
            dto.moving = (flags & 2) != 0;
            dto.grounded = (flags & 4) != 0;
            dto.crouching = (flags & 8) != 0;
            dto.dashing = (flags & 16) != 0;
            return true;
        }
        catch (IOException) { return false; }
    }

    public static byte[] Serialize(GameStateDTO dto)
    {
        MemoryStream stream = new MemoryStream();
        BinaryWriter writer = new BinaryWriter(stream);

        writer.Write(dto.round);
        writer.Write((byte)dto.phase);
        writer.Write(dto.taggedPlayer ?? "");
        writer.Write(dto.timeLeft);
        writer.Write(dto.loser ?? "");
        writer.Write(dto.scores ?? "");
        writer.Write(dto.winners ?? "");

        return stream.ToArray();
    }

    public static bool TryDeserialize(byte[] data, int offset, out GameStateDTO dto)
    {
        dto = new GameStateDTO();
        try
        {
            BinaryReader reader = new BinaryReader(new MemoryStream(data, offset, data.Length - offset));
            dto.round = reader.ReadInt32();
            dto.phase = (GamePhase)reader.ReadByte();
            dto.taggedPlayer = reader.ReadString();
            dto.timeLeft = reader.ReadSingle();
            dto.loser = reader.ReadString();
            dto.scores = reader.ReadString();
            dto.winners = reader.ReadString();
            return true;
        }
        catch (IOException) { return false; }
    }
}
