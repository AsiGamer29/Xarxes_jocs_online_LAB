// =================================================================================================
//  Lab 7 - World state replication: TEST SCENE
//  XJO - Xarxes per a Jocs Online
// =================================================================================================
//
//  Given. Nothing to modify. First it checks your ReplicationManagerServer (TODO 1 and 2) and
//  ReplicationManagerClient (TODO 3 to 5) with exact situations: lost Creates, lost Destroys, old
//  packets, 2000 ticks of a bad network. Prints PASS, FAIL or SKIPPED per check.
//  Then the live view, the server world on the left, the client copy on the right, and in between
//  a simulated network with loss, latency and jitter sliders (slide 19).
//  'useSolution' runs the *Solution classes instead of yours.
//  Command line (test builds only), -solution forces useSolution, -quitwhendone quits after the
//  checks, -resultfile <path> also writes the check lines to that file, -livefor <s> runs the live
//  view for s seconds at 20 % loss, then 1.5 s of clean network, logs its counters and quits.
//
// =================================================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Lab7
{
    public class ReplicationTest : MonoBehaviour
    {
        public bool useSolution = false;

        // ---------------------------------------------------------------- the code under test

        // Swappable, so the solution, or a broken version, can be checked.
        public class ServerOps
        {
            public Action<int> markCreate, markUpdate, markDestroy;
            public Func<byte[]> write;
        }

        public class Impl
        {
            public Func<Lab7LinkingContext, Func<float>, ServerOps> server;                    // clock null = Time.time
            public Func<GameObject, Lab7LinkingContext, List<GameObject>, Action<BinaryReader>> client;
        }

        public static Impl Student()
        {
            return new Impl
            {
                server = (linking, clock) =>
                {
                    var s = new ReplicationManagerServer(linking);
                    if (clock != null) s.clock = clock;
                    return new ServerOps { markCreate = s.MarkCreate, markUpdate = s.MarkUpdate,
                                           markDestroy = s.MarkDestroy, write = s.WritePacket };
                },
                client = (host, linking, prefabs) =>
                {
                    var c = host.AddComponent<ReplicationManagerClient>();
                    c.Init(linking, prefabs);
                    return c.ReadPacket;
                },
            };
        }

        public static Impl Solution()
        {
            return new Impl
            {
                server = (linking, clock) =>
                {
                    var s = new ReplicationManagerServerSolution(linking);
                    if (clock != null) s.clock = clock;
                    return new ServerOps { markCreate = s.MarkCreate, markUpdate = s.MarkUpdate,
                                           markDestroy = s.MarkDestroy, write = s.WritePacket };
                },
                client = (host, linking, prefabs) =>
                {
                    var c = host.AddComponent<ReplicationManagerClientSolution>();
                    c.Init(linking, prefabs);
                    return c.ReadPacket;
                },
            };
        }

        public class Result
        {
            public int pass, fail, skipped;
            public readonly HashSet<int> skippedTodos = new HashSet<int>();
        }

        public const int Checks = 15;

        // ---------------------------------------------------------------- a server and a client

        static float s_now;                                  // the checks' clock, set by hand

        class World
        {
            public readonly Lab7LinkingContext sl = new Lab7LinkingContext(), cl = new Lab7LinkingContext();
            public readonly ServerOps srv;
            public readonly Action<BinaryReader> cli;
            readonly Impl m_impl;
            readonly List<GameObject> m_prefabs;
            readonly Cleanup m_cleanup;

            public World(Impl impl, List<GameObject> prefabs, Cleanup cleanup, Func<float> clock)
            {
                m_impl = impl; m_prefabs = prefabs; m_cleanup = cleanup;
                cleanup.contexts.Add(sl); cleanup.contexts.Add(cl);
                srv = impl.server(sl, clock);
                cli = impl.client(cleanup.Host(), cl, prefabs);
            }

            public int Spawn(byte type, float x)
            {
                GameObject go = Instantiate(m_prefabs[type]);
                go.transform.position = new Vector3(x, 0, 0);
                int id = sl.Register(go);
                go.GetComponent<NetObject>().id = id;
                srv.markCreate(id);
                return id;
            }

            public void Move(int id, float x, int hp = -1)
            {
                GameObject go = sl.Find(id);
                go.transform.position = new Vector3(x, 1, 2);
                go.transform.rotation = Quaternion.Euler(0, x * 10, 0);
                NetPlayer np = go.GetComponent<NetPlayer>();
                if (hp >= 0 && np != null) np.health = hp;
                srv.markUpdate(id);
            }

            public void Kill(int id)
            {
                Destroy(sl.Find(id));
                sl.Unregister(id);
                srv.markDestroy(id);
            }

            public void Deliver(byte[] p)
            {
                var r = new BinaryReader(new MemoryStream(p));
                byte msg = r.ReadByte();
                if (msg != (byte)MsgType.Replication) throw new Exception("first byte is " + msg + ", not MsgType.Replication");
                cli(r);
            }

            // Reads the packet with a new, empty client, and says if it stopped exactly at the end.
            public string FullyRead(byte[] p)
            {
                var r = new BinaryReader(new MemoryStream(p));
                r.ReadByte();
                var linking = new Lab7LinkingContext();
                m_cleanup.contexts.Add(linking);
                m_impl.client(m_cleanup.Host(), linking, m_prefabs)(r);
                long at = r.BaseStream.Position;
                return at == p.Length ? "" : "read " + at + " of " + p.Length + " bytes";
            }

            public string Diff() { return WorldDiff(sl, cl); }
        }

        // "" when both worlds hold the same objects with the same fields.
        static string WorldDiff(Lab7LinkingContext sl, Lab7LinkingContext cl)
        {
            List<int> a = sl.Ids.OrderBy(i => i).ToList(), b = cl.Ids.OrderBy(i => i).ToList();
            if (!a.SequenceEqual(b))
                return "IDs on the server [" + string.Join(",", a) + "], on the client [" + string.Join(",", b) + "]";
            foreach (int id in a)
            {
                GameObject s = sl.Find(id), c = cl.Find(id);
                if (c == null) return "ID " + id + " is null on the client";
                if (Vector3.Distance(s.transform.position, c.transform.position) > 1e-4f)
                    return "ID " + id + " is at " + c.transform.position + " on the client, " + s.transform.position + " on the server";
                if (Mathf.Abs(Mathf.DeltaAngle(s.transform.eulerAngles.y, c.transform.eulerAngles.y)) > 0.01f)
                    return "ID " + id + " has another rotation on the client";
                NetObject sn = s.GetComponent<NetObject>(), cn = c.GetComponent<NetObject>();
                if (cn == null || sn.GetType() != cn.GetType()) return "ID " + id + " is another type on the client";
                if (sn is NetPlayer sp && sp.health != ((NetPlayer)cn).health)
                    return "ID " + id + " has health " + ((NetPlayer)cn).health + " on the client, " + sp.health + " on the server";
                if (cn.id != id) return "ID " + id + " has NetObject.id " + cn.id + " on the client";
            }
            return "";
        }

        // Everything the checks created, destroyed at the end.
        class Cleanup
        {
            public readonly List<Lab7LinkingContext> contexts = new List<Lab7LinkingContext>();
            public readonly List<GameObject> hosts = new List<GameObject>();
            public GameObject Host() { var h = new GameObject("ReplicationClient (check)"); hosts.Add(h); return h; }
            public void Run()
            {
                foreach (Lab7LinkingContext c in contexts)
                    foreach (int id in c.Ids.ToList()) { GameObject go = c.Find(id); if (go != null) Destroy(go); }
                foreach (GameObject h in hosts) if (h != null) Destroy(h);
            }
        }

        // ---------------------------------------------------------------- the checks

        // One side stops at its first unfinished TODO (or error). Everything after that needs it is SKIPPED.
        class Runner
        {
            public Result r;
            public Action<string> log;
            string m_server, m_client;                       // why that side is stopped, null if fine

            string Blocked(bool needsClient)
            {
                if (m_server != null) return m_server;
                return needsClient ? m_client : null;
            }

            void Stop(Exception e, bool needsClient)
            {
                if (e is Lab7TodoException t)
                {
                    if (r.skippedTodos.Add(t.number))
                        log("  " + Tag("SKIPPED") + " [TODO " + t.number + "] not done yet. " + t.Message);
                    string why = "waits for TODO " + t.number;
                    if (t.number <= 2) m_server = m_server ?? why; else m_client = m_client ?? why;
                }
                else if (needsClient) m_client = m_client ?? "after an earlier FAIL";
                else m_server = m_server ?? "after an earlier FAIL";
            }

            // A step with no verdict, setting up the next checks.
            public void Step(bool needsClient, Action call)
            {
                if (Blocked(needsClient) != null) return;
                try { call(); }
                catch (Lab7TodoException e) { Stop(e, needsClient); }
                catch (Exception e)
                {
                    r.fail++;
                    log("    " + Tag("FAIL") + " " + e.GetType().Name + ". " + e.Message);
                    Stop(e, needsClient);
                }
            }

            // body returns "" for PASS, or what went wrong.
            public void Check(string what, bool needsClient, Func<string> body)
            {
                string blocked = Blocked(needsClient);
                if (blocked != null)
                {
                    r.skipped++;
                    log("    " + Tag("SKIPPED") + " " + what + "   (" + blocked + ")");
                    return;
                }
                string got;
                try { got = body(); }
                catch (Lab7TodoException e)
                {
                    Stop(e, needsClient);
                    r.skipped++;
                    log("    " + Tag("SKIPPED") + " " + what + "   (" + Blocked(needsClient) + ")");
                    return;
                }
                catch (Exception e)
                {
                    got = e.GetType().Name + ". " + e.Message +
                          (e is EndOfStreamException || e is ArgumentOutOfRangeException
                              ? ". Read must read exactly what Write wrote, in the same order (slide 9)" : "");
                    Stop(e, needsClient);
                }
                bool ok = got == "";
                if (ok) r.pass++; else r.fail++;
                log("    " + Tag(ok ? "PASS" : "FAIL") + " " + what + (ok ? "" : "   (" + got + ")"));
            }
        }

        static List<GameObject> MakePrefabs(Transform parent)
        {
            var player = new GameObject("Prefab 0, player");
            player.AddComponent<NetPlayer>().type = 0;
            var bullet = new GameObject("Prefab 1, bullet");
            bullet.AddComponent<NetObject>().type = 1;
            foreach (GameObject go in new[] { player, bullet })
            {
                go.SetActive(false);                         // templates, never in the game themselves
                if (parent != null) go.transform.SetParent(parent, false);
            }
            return new List<GameObject> { player, bullet };
        }

        // The checks of the 2026-10-21 harness, in Unity. Needs Play mode (it calls Destroy).
        public static Result RunChecks(Impl impl, Action<string> log, List<GameObject> prefabs)
        {
            var r = new Result();
            var run = new Runner { r = r, log = log };
            var cleanup = new Cleanup();
            Func<float> clock = () => s_now;
            try
            {
                s_now = 0;
                var w = new World(impl, prefabs, cleanup, clock);
                byte[] p = null;

                log("[Header] TODO 1, then TODO 3");
                run.Step(false, () => p = w.srv.write());
                run.Check("(a) N=0, the empty packet is 9 bytes", false,
                          () => p.Length == 9 ? "" : "got " + p.Length + " bytes, MsgType 1 + sequence 4 + count 4");
                run.Check("(a) the client reads it and stays empty", true,
                          () => { w.Deliver(p); return w.cl.Count == 0 ? "" : "the client has " + w.cl.Count + " objects"; });

                log("");
                log("[Create] TODO 2, then TODO 4");
                int a = 0, b = 0, c = 0;
                run.Step(false, () => { a = w.Spawn(0, 1); b = w.Spawn(1, 2); c = w.Spawn(1, 3); p = w.srv.write(); });
                run.Check("(b) every byte of a Create packet is read", true, () => w.FullyRead(p));
                run.Check("(b) 3 Creates, the client has the same world", true, () => { w.Deliver(p); return w.Diff(); });

                log("");
                log("[Update] a player and a bullet move, the player loses health");
                run.Step(false, () =>
                {
                    s_now = 2; w.srv.write();                // the Create repeats expire
                    w.Move(a, 5, 40); w.Move(b, 6);
                    p = w.srv.write();
                });
                run.Check("(c) Update of a player and a bullet is 9 + 26 + 22 = 57 bytes", false,
                          () => p.Length == 57 ? "" : "got " + p.Length + " bytes (slide 17)");
                run.Check("(c) every byte of an Update packet is read", true, () => w.FullyRead(p));
                run.Check("(c) positions, rotation and health match", true, () => { w.Deliver(p); return w.Diff(); });

                log("");
                log("[Destroy] TODO 5");
                run.Step(false, () => { w.Kill(c); p = w.srv.write(); });
                run.Check("(d) Destroy, the client has 2 objects", true, () =>
                {
                    w.Deliver(p);
                    return w.cl.Count != 2 ? "the client has " + w.cl.Count + " objects" : w.Diff();
                });
                int d = 0;
                run.Step(false, () => { d = w.Spawn(1, 9); w.Kill(d); p = w.srv.write(); });
                run.Check("(e) Create and Destroy in one tick, the client never sees it", true, () =>
                {
                    w.Deliver(p);
                    return w.cl.Find(d) != null ? "the client created it" : w.Diff();
                });

                log("");
                log("[Lost and late packets] slide 15");
                int e = 0, f = 0;
                run.Step(false, () =>
                {
                    s_now = 10; w.srv.write();
                    e = w.Spawn(1, 4); w.srv.write();        // this Create is lost
                    s_now = 10.05f; p = w.srv.write();       // its repeat arrives
                });
                run.Check("(f) lost Create, the repeat creates it", true, () =>
                {
                    w.Deliver(p);
                    return w.cl.Find(e) == null ? "the client never created it" : w.Diff();
                });

                run.Step(false, () =>
                {
                    s_now = 20; w.srv.write();
                    f = w.Spawn(1, 4); w.srv.write();        // this Create is lost
                    s_now = 21.5f; w.srv.write();            // the repeats expired, all lost too
                    w.Move(f, 8); p = w.srv.write();
                });
                run.Check("(g) Create and all its repeats lost, a later Update creates it", true, () =>
                {
                    w.Deliver(p);
                    return w.cl.Find(f) == null ? "the client never created it. Update carries the type (slide 15)" : w.Diff();
                });

                byte[] older = null, newer = null;
                run.Step(false, () => { w.Move(a, 11); older = w.srv.write(); w.Move(a, 12); newer = w.srv.write(); });
                run.Check("(h) old packet after a newer one is dropped, nothing goes back", true, () =>
                {
                    w.Deliver(newer); w.Deliver(older);
                    float x = w.cl.Find(a).transform.position.x;
                    return x == 12 ? "" : "the player went back to x " + x + ". Compare the sequence (TODO 3)";
                });

                run.Step(false, () =>
                {
                    s_now = 30; w.srv.write();
                    w.Kill(b); w.srv.write();                // this Destroy is lost
                    s_now = 30.05f; p = w.srv.write();
                });
                run.Check("(i) lost Destroy, the repeat destroys it", true, () =>
                {
                    w.Deliver(p);
                    return w.cl.Find(b) != null ? "a ghost stays on the client" : w.Diff();
                });
                run.Step(false, () => { s_now = 30.1f; p = w.srv.write(); });
                run.Check("(j) Destroy repeated for an ID already gone is ignored", true, () => { w.Deliver(p); return w.Diff(); });

                log("");
                log("[Bad network] 2000 ticks at 20 % loss, with jitter that reorders packets");
                string stats = null;
                run.Check("(k) both worlds equal after 1 s of clean network", true, () =>
                {
                    var rnd = new System.Random(7);
                    var w2 = new World(impl, prefabs, cleanup, clock);
                    var live = new List<int>();
                    var queue = new List<(float at, byte[] data)>();
                    int maxLen = 0, delivered = 0, sent = 0, reordered = 0, lastSeqDelivered = -1, seq = 0;
                    var seqOf = new Dictionary<byte[], int>();
                    for (int t = 0; t < 2000; t++)
                    {
                        s_now = t * 0.05f;
                        if (rnd.NextDouble() < 0.2 && live.Count < 40) live.Add(w2.Spawn((byte)rnd.Next(2), (float)rnd.NextDouble()));
                        if (rnd.NextDouble() < 0.15 && live.Count > 0) { int k = live[rnd.Next(live.Count)]; live.Remove(k); w2.Kill(k); }
                        foreach (int k in live) if (rnd.NextDouble() < 0.5) w2.Move(k, (float)rnd.NextDouble() * 100, rnd.Next(100));
                        byte[] pk = w2.srv.write(); seqOf[pk] = seq++; sent++; maxLen = Math.Max(maxLen, pk.Length);
                        if (rnd.NextDouble() >= 0.2) queue.Add((s_now + 0.1f + (float)(rnd.NextDouble() * 0.12 - 0.06), pk));
                        foreach (var q in queue.Where(q => q.at <= s_now).OrderBy(q => q.at).ToList())
                        {
                            if (seqOf[q.data] < lastSeqDelivered) reordered++;
                            lastSeqDelivered = Math.Max(lastSeqDelivered, seqOf[q.data]);
                            w2.Deliver(q.data); queue.Remove(q); delivered++;
                        }
                    }
                    int lost = sent - delivered - queue.Count;
                    foreach (var q in queue.ToList()) w2.Deliver(q.data);
                    for (int t = 0; t < 22; t++)              // 1.1 s of clean network
                    {
                        s_now += 0.05f;
                        foreach (int k in live) w2.Move(k, k + t);
                        w2.Deliver(w2.srv.write());
                    }
                    stats = sent + " packets, " + lost + " lost, " + reordered + " out of order, " +
                            live.Count + " objects at the end, biggest packet " + maxLen + " bytes";
                    return w2.Diff();
                });
                if (stats != null) log("    " + stats);
            }
            finally
            {
                cleanup.Run();
            }
            log("");
            log("TOTAL  " + Tag("PASS") + " " + r.pass + "   " + Tag("FAIL") + " " + r.fail + "   " + Tag("SKIPPED") + " " + r.skipped);
            return r;
        }

        // ---------------------------------------------------------------- the scene

        readonly List<string> m_log = new List<string>();
        Vector2 m_scroll;
        List<GameObject> m_prefabs;
        Live m_live;

        System.Collections.IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-solution") >= 0) useSolution = true;
            bool quit = Array.IndexOf(args, "-quitwhendone") >= 0;
            if (quit) Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);

            m_prefabs = MakePrefabs(transform);
            Impl impl = useSolution ? Solution() : Student();
            RunChecks(impl, Log, m_prefabs);

            int rf = Array.IndexOf(args, "-resultfile");
            if (rf >= 0 && rf + 1 < args.Length) File.WriteAllLines(args[rf + 1], m_log.Select(Plain));

            if (quit) { Application.Quit(); yield break; }
            m_live = new Live(impl, m_prefabs, gameObject);

            int lf = Array.IndexOf(args, "-livefor");
            if (lf < 0 || lf + 1 >= args.Length) yield break;
            m_live.loss = 0.2f; m_live.jitter = 0.06f;
            yield return new WaitForSeconds(float.Parse(args[lf + 1], System.Globalization.CultureInfo.InvariantCulture));
            string bad = "[Live] bad network, sent " + m_live.sent + ", lost " + m_live.lost + ", arrived after a newer one " +
                         m_live.late + ", server " + m_live.sl.Count + " objects, client " + m_live.cl.Count;
            m_live.loss = 0f; m_live.latency = 0f; m_live.jitter = 0f;
            yield return new WaitForSeconds(1.5f);
            string clean = "[Live] after 1.5 s of clean network, not on the client yet " + m_live.Missing + ", ghosts " +
                           m_live.Ghosts + ", biggest packet " + m_live.maxBytes + " bytes, error " + (m_live.error ?? "none");
            Debug.Log(bad); Debug.Log(clean);
            if (rf >= 0 && rf + 1 < args.Length) File.AppendAllLines(args[rf + 1], new[] { bad, clean });
            Application.Quit();
        }

        void Update()
        {
            if (m_live != null) m_live.Update();
        }

        void Log(string line)
        {
            Debug.Log(Plain(line));                          // console without the color tags
            m_log.Add(line);
        }

        public static string Plain(string line) { return System.Text.RegularExpressions.Regex.Replace(line, "<.*?>", ""); }

        static string Tag(string t)
        {
            string color = t == "PASS" ? "#5fd35f" : t == "FAIL" ? "#ff6060" : "#e0c050";
            return "<color=" + color + ">" + t + "</color>";
        }

        // ---------------------------------------------------------------- the live view

        class Live
        {
            public float loss = 0.1f, latency = 0.1f, jitter = 0.03f;
            public string error;
            public int sent, lost, late, lastBytes, maxBytes;

            public readonly Lab7LinkingContext sl = new Lab7LinkingContext(), cl = new Lab7LinkingContext();
            readonly ServerOps m_srv;
            readonly Action<BinaryReader> m_cli;
            readonly List<GameObject> m_prefabs;
            readonly List<(float at, int seq, byte[] data)> m_queue = new List<(float, int, byte[])>();
            readonly List<(int id, Vector3 velocity, float dieAt)> m_bullets = new List<(int, Vector3, float)>();
            readonly List<int> m_players = new List<int>();
            float m_nextTick, m_nextShot;
            int m_lastSeq = -1;

            public Live(Impl impl, List<GameObject> prefabs, GameObject host)
            {
                m_prefabs = prefabs;
                m_srv = impl.server(sl, null);               // Time.time
                m_cli = impl.client(host, cl, prefabs);
                for (int i = 0; i < 3; i++) m_players.Add(Spawn(0, Vector3.zero));
            }

            int Spawn(byte type, Vector3 at)
            {
                GameObject go = Instantiate(m_prefabs[type]);
                go.name = type == 0 ? "Player (server)" : "Bullet (server)";
                go.transform.position = at;
                int id = sl.Register(go);
                go.GetComponent<NetObject>().id = id;
                m_srv.markCreate(id);
                return id;
            }

            void Kill(int id)
            {
                Destroy(sl.Find(id));
                sl.Unregister(id);
                m_srv.markDestroy(id);
            }

            public void Update()
            {
                if (error != null) return;
                try
                {
                    if (Time.time >= m_nextTick) { m_nextTick = Time.time + 0.05f; Tick(); }   // 20 per second
                    foreach (var q in m_queue.Where(q => q.at <= Time.time).OrderBy(q => q.at).ToList())
                    {
                        if (q.seq < m_lastSeq) late++;       // the client must drop it (TODO 3)
                        m_lastSeq = Math.Max(m_lastSeq, q.seq);
                        var r = new BinaryReader(new MemoryStream(q.data));
                        r.ReadByte();
                        m_cli(r);
                        m_queue.Remove(q);
                    }
                }
                catch (Lab7TodoException e) { error = "[TODO " + e.number + "] not done yet. " + e.Message + ". Finish TODO 1 to 5 to see the live view."; }
                catch (Exception e) { error = e.GetType().Name + ". " + e.Message; }
            }

            void Tick()
            {
                float t = Time.time;
                for (int i = 0; i < m_players.Count; i++)        // players run in circles
                {
                    GameObject p = sl.Find(m_players[i]);
                    float angle = t * (0.6f + 0.25f * i) + i * 2.1f, radius = 2.5f + 1.5f * i;
                    p.transform.position = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                    p.transform.rotation = Quaternion.Euler(0, -angle * Mathf.Rad2Deg, 0);  // facing along the circle
                    NetPlayer np = p.GetComponent<NetPlayer>();
                    if (UnityEngine.Random.value < 0.02f) np.health = np.health <= 10 ? 100 : np.health - 10;
                }
                if (t >= m_nextShot)                              // a random player fires
                {
                    m_nextShot = t + 0.25f;
                    GameObject p = sl.Find(m_players[UnityEngine.Random.Range(0, m_players.Count)]);
                    Vector3 dir = p.transform.rotation * Vector3.forward;
                    m_bullets.Add((Spawn(1, p.transform.position), dir * 6f, t + 1.5f));
                }
                for (int i = m_bullets.Count - 1; i >= 0; i--)
                {
                    GameObject b = sl.Find(m_bullets[i].id);
                    b.transform.position += m_bullets[i].velocity * 0.05f;
                    b.transform.rotation = Quaternion.LookRotation(m_bullets[i].velocity);
                    if (t > m_bullets[i].dieAt) { Kill(m_bullets[i].id); m_bullets.RemoveAt(i); }
                }

                foreach (int id in sl.Ids.ToList()) m_srv.markUpdate(id);   // everything, every tick (slide 12)
                byte[] packet = m_srv.write();
                sent++; lastBytes = packet.Length; maxBytes = Math.Max(maxBytes, packet.Length);
                if (UnityEngine.Random.value < loss) { lost++; return; }
                float delay = Mathf.Max(0f, latency + UnityEngine.Random.Range(-jitter, jitter));
                m_queue.Add((t + delay, BitConverter.ToInt32(packet, 1), packet));
            }

            // Objects the server has and the client not yet, and ghosts the client still has.
            public int Missing { get { return sl.Ids.Count(id => cl.Find(id) == null); } }
            public int Ghosts { get { return cl.Ids.Count(id => sl.Find(id) == null); } }
            public int InFlight { get { return m_queue.Count; } }
        }

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 14 };
            float w = Screen.width, h = Screen.height;
            GUILayout.BeginArea(new Rect(10, 10, w - 20, h * 0.38f));
            GUILayout.Label("<b>LAB 7 REPLICATION TEST</b>   " + (useSolution ? "running the solution" : "running your code"), style);
            m_scroll = GUILayout.BeginScrollView(m_scroll);
            foreach (string line in m_log) GUILayout.Label(line, style);
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            if (m_live == null) return;
            float top = h * 0.38f + 20, panelH = h - top - 120, panelW = (w - 30) / 2;
            var left = new Rect(10, top, panelW, panelH);
            var right = new Rect(20 + panelW, top, panelW, panelH);
            GUI.Box(left, "");
            GUI.Box(right, "");
            GUI.Label(new Rect(left.x + 6, left.y + 4, panelW, 22), "<b>SERVER</b>, the real world", style);
            GUI.Label(new Rect(right.x + 6, right.y + 4, panelW, 22), "<b>CLIENT</b>, the copy it rebuilds from packets", style);
            if (m_live.error != null)
            {
                GUI.Label(new Rect(left.x + 6, left.y + 30, w - 40, 60), "<color=#e0c050>" + m_live.error + "</color>", style);
            }
            else
            {
                Draw(m_live.sl, left, new Color(0.35f, 0.6f, 1f), style);
                Draw(m_live.cl, right, new Color(0.4f, 0.85f, 0.4f), style);
            }

            float y = top + panelH + 8;
            m_live.loss = Slider(new Rect(10, y, 420, 24), "Loss " + Mathf.RoundToInt(m_live.loss * 100) + " %", m_live.loss, 0f, 0.5f, style);
            m_live.latency = Slider(new Rect(10, y + 26, 420, 24), "Latency " + Mathf.RoundToInt(m_live.latency * 1000) + " ms", m_live.latency, 0f, 0.4f, style);
            m_live.jitter = Slider(new Rect(10, y + 52, 420, 24), "Jitter " + Mathf.RoundToInt(m_live.jitter * 1000) + " ms", m_live.jitter, 0f, 0.2f, style);
            GUI.Label(new Rect(450, y, w - 460, 110),
                "Packets sent " + m_live.sent + ", lost " + m_live.lost + ", in flight " + m_live.InFlight +
                ", arrived after a newer one " + m_live.late + " (dropped by the sequence check)\n" +
                "Last packet " + m_live.lastBytes + " bytes, biggest " + m_live.maxBytes + " bytes. " +
                "Server " + m_live.sl.Count + " objects, client " + m_live.cl.Count + "\n" +
                "Not on the client yet " + m_live.Missing + ", ghosts on the client " + m_live.Ghosts +
                ". Both should drop to 0 within about a second, whatever the sliders say (slide 15)", style);
        }

        static float Slider(Rect r, string label, float value, float min, float max, GUIStyle style)
        {
            GUI.Label(new Rect(r.x, r.y, 140, r.height), label, style);
            return GUI.HorizontalSlider(new Rect(r.x + 145, r.y + 6, r.width - 145, r.height), value, min, max);
        }

        // Top view, x to the right, z up, from -8 to 8.
        static void Draw(Lab7LinkingContext world, Rect panel, Color color, GUIStyle style)
        {
            Color old = GUI.color;
            foreach (int id in world.Ids)
            {
                GameObject go = world.Find(id);
                if (go == null) continue;
                Vector3 p = go.transform.position;
                float px = panel.x + (p.x + 8f) / 16f * panel.width, py = panel.y + (8f - p.z) / 16f * panel.height;
                if (px < panel.x || px > panel.xMax || py < panel.y + 24 || py > panel.yMax) continue;
                NetPlayer player = go.GetComponent<NetPlayer>();
                float size = player != null ? 26 : 8;
                GUI.color = player != null ? color : new Color(1f, 0.85f, 0.3f);
                GUI.DrawTexture(new Rect(px - size / 2, py - size / 2, size, size), Texture2D.whiteTexture);
                GUI.color = old;
                if (player != null)
                    GUI.Label(new Rect(px + 15, py - 12, 120, 22), id + "  hp " + player.health, style);
            }
            GUI.color = old;
        }
    }
}
