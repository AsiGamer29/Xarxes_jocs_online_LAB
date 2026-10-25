using System;

// The first byte of every UDP packet says what follows (Lab 3, slide 18).
// Lobby and chat stay as text; everything about the game travels in binary through Serializer.
public enum MsgType : byte
{
    Text = 1,         // lobby and chat, as before: JOIN:Anna, CHAT:hello...
    Player = 2,       // a PlayerDTO2 (Lab 3)
    PlayerState = 3,  // where a player is and what it is doing, about 20 times per second
    GameState = 4,    // the tag round, decided by the server, about 10 times per second
    ButtonPress = 5,
}

// What a player is doing right now. We send the state (not the input): every PC moves its own
// player with its own physics, and the others only have to show it.
[Serializable]
public struct PlayerStateDTO
{
    public uint sequence;      // grows with every packet: one that arrives late is dropped
    public string playerName;  // the server overwrites it with the name it knows for that sender
    public float x;
    public float y;
    public bool facingRight;
    public bool moving;
    public bool grounded;
    public bool crouching;
    public bool dashing;
}

[Serializable]
public struct ButtonPressDTO
{
    public int buttonId;
}

public enum GamePhase : byte { Waiting = 0, Playing = 1, RoundOver = 2, MatchOver = 3 }

// The tag round as the server sees it. It is sent again and again, so a lost packet only means
// the next one arrives 0.1 s later.
[Serializable]
public struct GameStateDTO
{
    public int round;
    public GamePhase phase;
    public string taggedPlayer;   // the one who "la lleva"
    public float timeLeft;        // seconds left in the round, or until the next one starts
    public string loser;          // who had it when the time ran out
    public string scores;         // Rounds won per player
    public string winners;        // Best of 3
}
