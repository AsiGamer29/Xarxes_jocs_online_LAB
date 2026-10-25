using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Spawns one Player per lobby member when the game scene loads, in the order the server sent the
// list, so every PC puts each player in the same place.
// The player of this PC is moved with the keyboard. The others will be moved by the network, so
// their input and physics are switched off.
public class GameSpawner : MonoBehaviour
{
    [SerializeField] GameObject playerPrefab;
    [Tooltip("One per player, in lobby order (host first). With more players than points, they repeat.")]
    [SerializeField] Vector2[] spawnPoints =
    {
        new Vector2(0f, 0.83f),     // middle platform
        new Vector2(12f, 4f),       // right platform
        new Vector2(-14.5f, 4f),    // left platform, away from its button
        new Vector2(-8f, -2f),      // lower left platform
    };

    void Start()
    {
        List<string> players = ServerSession.CurrentPlayers;
        string localName = ServerSession.LocalPlayerName;

        // Scene opened straight from the Editor, without the lobby: one local player to test with.
        if (players == null || players.Count == 0)
        {
            players = new List<string> { "Player" };
            localName = "Player";
        }

        List<string> names = new List<string>(players);
        for (int i = 0; i < names.Count; i++)
            Spawn(names[i], i, names[i] == localName);
    }

    void Spawn(string playerName, int index, bool isLocal)
    {
        Vector2 position = spawnPoints[index % spawnPoints.Length];
        GameObject player = Instantiate(playerPrefab, position, Quaternion.identity);
        player.name = "Player_" + playerName;
        player.tag = "Player";
        PlayerNetSync sync = player.AddComponent<PlayerNetSync>();
        sync.Init(playerName, isLocal);

        SetNameTag(player, playerName, isLocal);

        if (!isLocal) MakeRemote(player);
    }

    static void MakeRemote(GameObject player)
    {
        PlayerController controller = player.GetComponent<PlayerController>();
        if (controller != null) controller.enabled = false;

        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.bodyType = RigidbodyType2D.Kinematic;
            body.linearVelocity = Vector2.zero;
        }
    }

    void SetNameTag(GameObject player, string playerName, bool isLocal)
    {
        Transform tagTransform = player.transform.Find("NameTag");
        if (tagTransform == null) return;

        TMP_Text tag = tagTransform.GetComponent<TMP_Text>();
        tag.text = playerName;

    }

}
