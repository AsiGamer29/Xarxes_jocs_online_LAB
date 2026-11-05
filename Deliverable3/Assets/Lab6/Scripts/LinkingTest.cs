// =================================================================================================
//  Lab 6 - From lobby to multiplayer game: TEST SCENE
//  XJO - Xarxes per a Jocs Online
// =================================================================================================
//
//  Given. Nothing to modify. Checks your LinkingContext (TODO 1 to 3) with objects that share a
//  name, as in the Warning slide (slide 11), then your NetSim (TODO 4 and 5) with real UDP packets
//  sent to your own PC. Prints PASS, FAIL or SKIPPED per check.
//  'useSolution' runs LinkingContextSolution and NetSimSolution instead of yours.
//  Command line (test builds only), -solution forces useSolution, -quitwhendone quits at the end.
//
// =================================================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

public class LinkingTest : MonoBehaviour
{
    public bool useSolution = false;

    readonly List<string> m_log = new List<string>();
    Vector2 m_scroll;

    // The functions under test, swappable so the solution, or a broken version, can be checked.
    public class Ops
    {
        public Func<GameObject, int> register;
        public Action<GameObject, int> registerId;
        public Func<int, GameObject> find;
        public Action<int> unregister;
    }

    public class SimOps
    {
        public Action<byte[], EndPoint> send;
        public Action update;
        public Action<float, float, float> configure;        // latency, jitter, dropChance
    }

    public static Func<Ops> Student()
    {
        return () =>
        {
            var c = new LinkingContext();
            return new Ops { register = c.Register, registerId = c.Register, find = c.Find, unregister = c.Unregister };
        };
    }

    public static Func<Ops> Solution()
    {
        return () =>
        {
            var c = new LinkingContextSolution();
            return new Ops { register = c.Register, registerId = c.Register, find = c.Find, unregister = c.Unregister };
        };
    }

    public static Func<Socket, SimOps> StudentSim()
    {
        return s =>
        {
            var n = new NetSim(s);
            return new SimOps { send = n.Send, update = n.Update,
                                configure = (l, j, d) => { n.latency = l; n.jitter = j; n.dropChance = d; } };
        };
    }

    public static Func<Socket, SimOps> SolutionSim()
    {
        return s =>
        {
            var n = new NetSimSolution(s);
            return new SimOps { send = n.Send, update = n.Update,
                                configure = (l, j, d) => { n.latency = l; n.jitter = j; n.dropChance = d; } };
        };
    }

    public class Result { public int pass, fail, skipped; public readonly HashSet<int> skippedTodos = new HashSet<int>(); }

    IEnumerator Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (Array.IndexOf(args, "-solution") >= 0) useSolution = true;

        var r = new Result();
        RunLinking(useSolution ? Solution() : Student(), Log, r);
        Log("");
        yield return RunNetSim(useSolution ? SolutionSim() : StudentSim(), Log, r);
        Log("");
        Log("TOTAL  " + Tag("PASS") + " " + r.pass + "   " + Tag("FAIL") + " " + r.fail +
            "   " + Tag("SKIPPED") + " " + r.skipped);

        if (Array.IndexOf(args, "-quitwhendone") >= 0) Application.Quit();
    }

    // ------------------------------------------------------------------ LinkingContext, TODO 1 to 3

    public static Result RunLinking(Func<Ops> make, Action<string> log, Result r = null)
    {
        r = r ?? new Result();
        var created = new List<GameObject>();
        GameObject Make(string name) { var go = new GameObject(name); created.Add(go); return go; }

        try
        {
            log("[Server] two objects, both called \"Cube\" (TODO 1)");
            Ops server = make();
            GameObject red = Make("Cube"), grey = Make("Cube");
            int idRed = 0, idGrey = 0;
            if (Step(() => { idRed = server.register(red); idGrey = server.register(grey); }, log, r))
            {
                Check("first ID is 1", idRed == 1, "got " + idRed, log, r);
                Check("second ID is 2", idGrey == 2, "got " + idGrey, log, r);
            }

            log("");
            log("[Server] find them by ID (TODO 2)");
            GameObject f1 = null, f2 = null, f99 = red;
            if (Step(() => { f1 = server.find(idRed); f2 = server.find(idGrey); }, log, r))
            {
                Check("Find(1) is the first Cube", f1 == red, Describe(f1, red, grey), log, r);
                Check("Find(2) is the second Cube, same name, told apart", f2 == grey, Describe(f2, red, grey), log, r);
            }
            if (Step(() => f99 = server.find(99), log, r))
                Check("Find(99), unknown ID -> null", f99 == null, "got an object", log, r);

            log("");
            log("[Client] uses the ID the server sent (TODO 1 and 2)");
            Ops client = make();
            GameObject clientCube = Make("Cube");
            GameObject c7 = null, c1 = clientCube;
            if (Step(() => client.registerId(clientCube, 7), log, r) &&
                Step(() => { c7 = client.find(7); c1 = client.find(1); }, log, r))
            {
                Check("Register(go, 7), then Find(7) is that object", c7 == clientCube, c7 == null ? "got null" : "got another object", log, r);
                Check("the client never invented ID 1", c1 == null, "Find(1) gave an object", log, r);
            }

            log("");
            log("[Server] destroy the first Cube (TODO 3)");
            GameObject after1 = red, after2 = null;
            if (Step(() => server.unregister(idRed), log, r) &&
                Step(() => { after1 = server.find(idRed); after2 = server.find(idGrey); }, log, r))
            {
                Check("Find(1) after Unregister(1) -> null", after1 == null, "still there", log, r);
                Check("Find(2) still there", after2 == grey, after2 == null ? "got null" : "got another object", log, r);
            }
            if (Step(() => server.unregister(99), log, r))
                Check("Unregister(99), unknown ID, is not an error", true, "", log, r);
            int idNew = 0;
            if (Step(() => idNew = server.register(Make("Bullet")), log, r))
                Check("a new object gets ID 3, never a reused 1", idNew == 3, "got " + idNew, log, r);

            log("");
            log("[Warning slide] both sides create a red and a grey Cube, the client in the other order");
            Ops s2 = make(), c2 = make();
            GameObject sRed = Make("Cube"), sGrey = Make("Cube"), cGrey = Make("Cube"), cRed = Make("Cube");
            GameObject moved = null;
            int netRed = 0, netGrey = 0;
            if (Step(() =>
                {
                    netRed = s2.register(sRed);              // the server registers red first
                    netGrey = s2.register(sGrey);
                    c2.registerId(cGrey, netGrey);           // the client grey first, with the server's IDs
                    c2.registerId(cRed, netRed);
                    moved = c2.find(netRed);                 // "Update, ID 1, position"
                }, log, r))
            {
                log("    server says \"move ID " + netRed + "\" -> the client moves its " +
                    (moved == cRed ? "red" : moved == cGrey ? "grey" : "(nothing)") + " Cube");
                Check("the client moves the right Cube", moved == cRed, "wrong object", log, r);
            }
        }
        finally
        {
            foreach (GameObject go in created) if (go != null) UnityEngine.Object.DestroyImmediate(go);
        }
        return r;
    }

    static string Describe(GameObject got, GameObject first, GameObject second)
    {
        return got == null ? "got null" : got == first ? "got the first Cube" : got == second ? "got the second Cube" : "got another object";
    }

    // ------------------------------------------------------------------ NetSim, TODO 4 and 5

    public static IEnumerator RunNetSim(Func<Socket, SimOps> make, Action<string> log, Result r)
    {
        log("[NetSim] real UDP packets to 127.0.0.1 (TODO 4 and 5)");
        Socket rx = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        Socket tx = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        rx.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        rx.Blocking = false;
        EndPoint to = rx.LocalEndPoint;
        var got = new List<int>();

        // A. latency 0.1 s, nothing else: none early, then all of them, in order
        SimOps sim = make(tx);
        sim.configure(0.1f, 0f, 0f);
        bool ok = Step(() => { for (int i = 0; i < 50; i++) sim.send(BitConverter.GetBytes(i), to); }, log, r);
        if (ok)
        {
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 0.05f)
            {
                if (!Step(() => sim.update(), log, r)) { ok = false; break; }
                yield return null;
            }
        }
        if (ok)
        {
            Drain(rx, got);
            Check("latency 0.1 s, nothing arrives in the first 0.05 s", got.Count == 0, got.Count + " arrived", log, r);
            yield return Pump(sim, 0.3f);
            Drain(rx, got);
            Check("jitter 0, " + got.Count + " of 50 arrive, " + (InOrder(got) ? "in order" : "OUT OF ORDER"),
                  got.Count == 50 && InOrder(got), "a forward loop keeps the order (TODO 5)", log, r);

            // B. half of them lost
            got.Clear();
            sim = make(tx);
            sim.configure(0f, 0f, 0.5f);
            for (int i = 0; i < 100; i++) sim.send(BitConverter.GetBytes(i), to);
            yield return Pump(sim, 0.2f);
            Drain(rx, got);
            Check("dropChance 0.5, " + got.Count + " of 100 packets arrive", got.Count >= 25 && got.Count <= 75,
                  "expected about 50", log, r);

            // C. jitter: one packet every 10 ms, each late by 0.05 to 0.15 s
            got.Clear();
            sim = make(tx);
            sim.configure(0.1f, 0.05f, 0f);
            int sent = 0;
            float t0 = Time.realtimeSinceStartup;
            while (sent < 50)
            {
                if (Time.realtimeSinceStartup - t0 >= sent * 0.01f) sim.send(BitConverter.GetBytes(sent++), to);
                sim.update();
                yield return null;
            }
            yield return Pump(sim, 0.3f);
            Drain(rx, got);
            int late = 0;
            for (int i = 1; i < got.Count; i++) if (got[i] < got[i - 1]) late++;
            Check("jitter 0.05 s, " + got.Count + " of 50 arrive", got.Count == 50, "some were lost", log, r);
            log("    " + late + " arrived before an older one. That is jitter reordering packets (slide 20)");
        }
        rx.Close();
        tx.Close();
    }

    static IEnumerator Pump(SimOps sim, float seconds)
    {
        float start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < seconds) { sim.update(); yield return null; }
    }

    static void Drain(Socket rx, List<int> got)
    {
        byte[] buf = new byte[16];
        EndPoint from = new IPEndPoint(IPAddress.Any, 0);
        while (rx.Available > 0)
        {
            rx.ReceiveFrom(buf, ref from);
            got.Add(BitConverter.ToInt32(buf, 0));
        }
    }

    static bool InOrder(List<int> got)
    {
        for (int i = 1; i < got.Count; i++) if (got[i] < got[i - 1]) return false;
        return true;
    }

    // ------------------------------------------------------------------ reporting

    // Runs one call. Unfinished TODO, SKIPPED, warned once. Any other exception, FAIL with a hint.
    static bool Step(Action call, Action<string> log, Result r)
    {
        try { call(); return true; }
        catch (Lab6TodoException e)
        {
            if (r.skippedTodos.Add(e.number)) log("  " + Tag("SKIPPED") + " [TODO " + e.number + "] not done yet. " + e.Message);
            r.skipped++;
        }
        catch (KeyNotFoundException)
        {
            log("  " + Tag("FAIL") + " KeyNotFoundException. Find must return null for an unknown ID, use TryGetValue (slide 15)");
            r.fail++;
        }
        catch (Exception e)
        {
            log("  " + Tag("FAIL") + " " + e.GetType().Name + ". " + e.Message);
            r.fail++;
        }
        return false;
    }

    static void Check(string what, bool ok, string got, Action<string> log, Result r)
    {
        if (ok) r.pass++; else r.fail++;
        log("    " + Tag(ok ? "PASS" : "FAIL") + " " + what + (ok ? "" : "   (" + got + ")"));
    }

    static string Tag(string t)
    {
        string color = t == "PASS" ? "#5fd35f" : t == "FAIL" ? "#ff6060" : "#e0c050";
        return "<color=" + color + ">" + t + "</color>";
    }

    void Log(string line)
    {
        // Console without the color tags, screen with them.
        Debug.Log(System.Text.RegularExpressions.Regex.Replace(line, "<.*?>", ""));
        m_log.Add(line);
    }

    void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 14 };
        GUILayout.BeginArea(new Rect(10, 10, Screen.width - 20, Screen.height - 20));
        GUILayout.Label("<b>LAB 6 LINKING CONTEXT AND NETSIM TEST</b>   " +
                        (useSolution ? "running the solution" : "running your code"), style);
        m_scroll = GUILayout.BeginScrollView(m_scroll);
        foreach (string line in m_log) GUILayout.Label(line, style);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
}
