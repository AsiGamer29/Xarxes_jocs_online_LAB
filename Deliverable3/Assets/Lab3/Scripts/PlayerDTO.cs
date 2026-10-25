// =================================================================================================
//  Lab 3 - Data serialization: THE DATA YOU SEND
//  XJO - Xarxes per a Jocs Online
// =================================================================================================
//
//  Given. Do not change it: the test scene and the slides use these exact fields.
//  A DTO (data transfer object) is a small class or struct made only to be sent (slide 13).
//  Never send a GameObject or a MonoBehaviour.
//
// =================================================================================================

using System;
using System.Collections.Generic;

// Challenge 1 (slide 15)
[Serializable]
public struct PlayerDTO
{
    public string playerName;
    public int level;
    public List<string> ownedPokemons;
}

// Challenge 2 (slide 16)
[Serializable]
public class Pokemon
{
    public string name;
}

[Serializable]
public struct PlayerDTO2
{
    public string playerName;
    public int level;
    public List<Pokemon> ownedPokemons;
}

// MsgType (challenge 3, slide 18) is in Scripts/NetMessages.cs, with the game messages.
