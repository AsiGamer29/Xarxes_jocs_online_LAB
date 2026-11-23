using System;

// The first byte of every UDP packet says what follows.
// Lobby, chat and buttons go through TCP as text; the game itself goes through UDP in binary.
public enum MsgType : byte
{
    Hello = 1,        // client -> host: my name and game version. Repeated until Welcome arrives
    PlayerState = 2,  // client -> host: where my player is and what it is doing, about 20 times per second
    GameState = 3,    // the tag round, decided by the host, about 10 times per second
    Welcome = 4,      // host -> client: your player ID, your object's network ID, every player in the world
    Refuse = 5,       // host -> client: why you cannot play (old version, not in this game)
    Ping = 6,         // client -> host, every second: keeps the virtual connection alive
    Pong = 7,         // host -> client: the Ping's time sent back, to measure the round trip
    PlayerLeft = 8,   // host -> clients: this network ID timed out, take it off the screen
    Replication = 9,  // host -> clients: the world (players, coins, bullets) created, updated or destroyed
    ButtonPress = 10, // client -> host: i stepped on this button, repeated for a while
    ButtonState = 11, // host -> clients: which buttons are pressed, again and again
    Shoot = 12,       // client -> host: i want to shoot, facing this way. Sent three times
    Trampoline = 14,  // client -> host -> the others: somebody bounced on this trampoline, play its animation
}

// What a player is doing right now, sent by its own PC to the host. We send the state (not the
// input): every PC moves its own player with its own physics, and the others only have to show it.
[Serializable]
public struct PlayerStateDTO
{
    public uint sequence;      // grows with every packet: one that arrives late is dropped
    public int objectId;       // network ID of the player; the host overwrites it with the sender's
    public float x;
    public float y;
    public bool facingRight;
    public bool moving;
    public bool grounded;
    public bool crouching;
    public bool dashing;
}

// The answer to Hello. With it a client knows which network ID is which player.
[Serializable]
public struct WelcomeDTO
{
    public int playerId;        // position in the lobby, the host is 0
    public int objectId;        // network ID of this client's own player
    public int[] objectIds;     // every player in the world...
    public string[] names;      // ...and its name, same order
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
