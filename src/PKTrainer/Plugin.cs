using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace PKTrainer
{
    /// <summary>PK Trainer - House of Legacy: Insert / F9 / ปุ่มมุมซ้ายล่าง เพื่อเปิด-ปิดหน้าต่าง แก้ค่าในเกมที่กำลังรันได้ทันที</summary>
    [BepInPlugin("pk.hol.trainer", "PK Trainer - House of Legacy", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        const string Title = "PK Trainer - House of Legacy";

        // ---- ตำแหน่งช่องข้อมูลตัวละคร (ยืนยันกับหน้าจอเกมแล้ว) ----
        class Group
        {
            public string Name, En, Th;
            public Func<List<List<string>>> List;
            public int NameIdx, Age, Writing, Might, Business, Arts, Mood, Renown, Charisma, Health, Cunning;
        }

        static readonly Group[] Groups =
        {
            new Group { Name = "Lineage",  En = "Lineage",  Th = "สายตระกูล", List = () => Mainload.Member_now, NameIdx = 4, Age = 6, Writing = 7, Might = 8, Business = 9, Arts = 10, Mood = 11, Renown = 16, Charisma = 20, Health = 21, Cunning = 27 },
            new Group { Name = "Spouse",   En = "Spouse",   Th = "คู่สมรส",   List = () => Mainload.Member_qu,  NameIdx = 2, Age = 5, Writing = 6, Might = 7, Business = 8, Arts = 9,  Mood = 10, Renown = 12, Charisma = 15, Health = 16, Cunning = 19 },
            new Group { Name = "Retainer", En = "Retainer", Th = "ผู้ติดตาม", List = () => Mainload.MenKe_Now,  NameIdx = 2, Age = 3, Writing = 4, Might = 5, Business = 6, Arts = 7,  Mood = 8,  Renown = 11, Charisma = 13, Health = 14, Cunning = 15 },
        };

        const int LuckSub = 7; // luck อยู่ในฟิลด์ชื่อ "ชื่อ|..|luck|.."

        struct Field { public string Id, En, Th; public float Min, Max; }
        static readonly Field[] Fields =
        {
            new Field { Id = "age",      En = "Age",      Th = "อายุ",      Min = 0, Max = 150 },
            new Field { Id = "health",   En = "Health",   Th = "สุขภาพ",    Min = 0, Max = 100 },
            new Field { Id = "mood",     En = "Mood",     Th = "อารมณ์",    Min = 0, Max = 100 },
            new Field { Id = "renown",   En = "Renown",   Th = "ชื่อเสียง", Min = 0, Max = 100000 },
            new Field { Id = "charisma", En = "Charisma", Th = "เสน่ห์",    Min = 0, Max = 999 },
            new Field { Id = "luck",     En = "Luck",     Th = "โชคลาภ",    Min = 0, Max = 999 },
            new Field { Id = "writing",  En = "Writing",  Th = "อักษร",     Min = 0, Max = 999 },
            new Field { Id = "might",    En = "Might",    Th = "ยุทธ์",     Min = 0, Max = 999 },
            new Field { Id = "business", En = "Business", Th = "การค้า",    Min = 0, Max = 999 },
            new Field { Id = "arts",     En = "Arts",     Th = "ศิลป์",     Min = 0, Max = 999 },
            new Field { Id = "cunning",  En = "Cunning",  Th = "กลยุทธ์",   Min = 0, Max = 999 },
        };
        static readonly string[] Skills = { "writing", "might", "business", "arts", "cunning" };

        // ---- UI state ----
        ConfigEntry<bool> cfgThai;
        bool th = true;
        bool show;
        Rect win = new Rect(40, 40, 780, 580);
        int tab;
        string amtCoin = "100000", amtGold = "100", amtSlave = "10", amtRenown = "100";
        int filter; // 0 = all
        string search = "";
        Vector2 scroll;
        string selGroup, selId;
        readonly Dictionary<string, string> buf = new Dictionary<string, string>();
        string skillAll = "50";
        string status = "";
        GUISkin skin;

        string L(string en, string thai) { return th ? thai : en; }

        void Awake()
        {
            // กันเกมทำลาย GameObject ของ plugin ตอนเปลี่ยนซีน
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(gameObject);
            cfgThai = Config.Bind("General", "ThaiLanguage", true, "true = ภาษาไทย, false = English");
            th = cfgThai.Value;
            status = L("Ready", "พร้อมใช้งาน");
            Logger.LogInfo(Title + " loaded. Insert / F9 / button at bottom-left");
        }

        void OnDestroy() { Logger.LogWarning(Title + " object destroyed"); }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Insert) || Input.GetKeyDown(KeyCode.F9)) show = !show;
        }

        void OnGUI()
        {
            if (skin == null) BuildSkin();
            GUI.skin = skin;
            GUI.depth = -1000;
            if (GUI.Button(new Rect(6, Screen.height - 30, 120, 24), show ? L("PK Trainer: hide", "PK Trainer: ซ่อน") : "PK Trainer [Ins]"))
                show = !show;
            if (!show) return;
            win = GUI.Window(94711, win, DrawWindow, Title + "   [Insert / F9]");
        }

        void BuildSkin()
        {
            skin = Instantiate(GUI.skin);
            Font f = null;
            try { f = Font.CreateDynamicFontFromOSFont(new[] { "Tahoma", "Leelawadee UI", "Segoe UI", "Arial" }, 15); } catch { }
            if (f != null)
            {
                skin.font = f;
                foreach (var s in new[] { skin.label, skin.button, skin.textField, skin.window, skin.box, skin.toggle })
                { s.font = f; s.fontSize = 15; }
            }
        }

        void SetLang(bool thai)
        {
            th = thai;
            cfgThai.Value = thai;
            status = L("Ready", "พร้อมใช้งาน");
        }

        void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(tab == 0, L("Player", "ผู้เล่น"), "Button", GUILayout.Width(110))) tab = 0;
            if (GUILayout.Toggle(tab == 1, L("Characters", "ตัวละคร"), "Button", GUILayout.Width(110))) tab = 1;
            GUILayout.FlexibleSpace();
            if (GUILayout.Toggle(th, "ไทย", "Button", GUILayout.Width(60)) && !th) SetLang(true);
            if (GUILayout.Toggle(!th, "EN", "Button", GUILayout.Width(60)) && th) SetLang(false);
            GUILayout.EndHorizontal();

            if (Mainload.CGNum == null || Mainload.FamilyData == null)
            {
                GUILayout.Label(L("Game not loaded yet - load a save first.", "ยังไม่ได้โหลดเกม - โหลด save ก่อน"));
            }
            else
            {
                try
                {
                    if (tab == 0) DrawPlayer(); else DrawChars();
                }
                catch (Exception e) { status = "Error: " + e.Message; Logger.LogError(e); }
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label(status);
            GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }

        // ------------------------------------------------------------ Player
        static long Coins() { return long.Parse(Mainload.CGNum[0]) + 1000000000L * long.Parse(Mainload.CGNum[2]); }

        static void SetCoins(long v)
        {
            v = Math.Max(0, Math.Min(1999999999L, v));
            Mainload.CGNum[2] = v >= 1000000000L ? "1" : "0";
            Mainload.CGNum[0] = (v >= 1000000000L ? v - 1000000000L : v).ToString();
        }

        static bool ParseL(string s, out long v)
        {
            return long.TryParse(s.Replace(",", "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        }

        void Row(string label, string cur, ref string amt, Action<long> set, Action<long> add)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(110));
            GUILayout.Label(cur, GUILayout.Width(130));
            amt = GUILayout.TextField(amt, GUILayout.Width(120));
            long v;
            bool ok = ParseL(amt, out v);
            if (GUILayout.Button(L("Set", "ตั้งค่า"), GUILayout.Width(70)) && ok) { set(v); status = label + L(" set to ", " ตั้งเป็น ") + v.ToString("N0"); }
            if (GUILayout.Button(L("+ Add", "+ เพิ่ม"), GUILayout.Width(70)) && ok) { add(v); status = label + " + " + v.ToString("N0"); }
            if (GUILayout.Button(L("- Sub", "- ลด"), GUILayout.Width(70)) && ok) { add(-v); status = label + " - " + v.ToString("N0"); }
            GUILayout.EndHorizontal();
        }

        void DrawPlayer()
        {
            GUILayout.Space(8);
            GUILayout.Label(L("Player  (family level ", "ผู้เล่น  (ระดับตระกูล ") + Mainload.FamilyData[2] + ")");
            Row(L("Coin", "เหรียญ"), Coins().ToString("N0"), ref amtCoin, v => SetCoins(v), v => SetCoins(Coins() + v));
            Row(L("Gold", "ทอง"), Mainload.CGNum[1], ref amtGold,
                v => Mainload.CGNum[1] = Math.Max(0, Math.Min(2000000000L, v)).ToString(),
                v => Mainload.CGNum[1] = Math.Max(0, Math.Min(2000000000L, long.Parse(Mainload.CGNum[1]) + v)).ToString());
            Row(L("Slaves", "ทาส"), Mainload.NuLiNum.ToString(), ref amtSlave,
                v => Mainload.NuLiNum = (int)Math.Max(0, Math.Min(1000000, v)),
                v => Mainload.NuLiNum = (int)Math.Max(0, Math.Min(1000000, Mainload.NuLiNum + v)));
            Row(L("Renown", "ชื่อเสียง"), Mainload.FamilyData[3] + "/" + FormulaData.ShengWangForLv(int.Parse(Mainload.FamilyData[2])),
                ref amtRenown,
                v => FormulaData.FamilyLvSW((int)(v - long.Parse(Mainload.FamilyData[3]))),
                v => FormulaData.FamilyLvSW((int)v));
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L("Coin = 999,999,999", "เหรียญ = 999,999,999"))) { SetCoins(999999999); status = L("Coin = 999,999,999", "เหรียญ = 999,999,999"); }
            if (GUILayout.Button(L("Gold = 99,999", "ทอง = 99,999"))) { Mainload.CGNum[1] = "99999"; status = L("Gold = 99,999", "ทอง = 99,999"); }
            GUILayout.EndHorizontal();
        }

        // -------------------------------------------------------- Characters
        class Entry { public Group G; public List<string> M; }

        List<Entry> Collect()
        {
            var res = new List<Entry>();
            string q = search.ToLowerInvariant();
            for (int gi = 0; gi < Groups.Length; gi++)
            {
                if (filter != 0 && filter - 1 != gi) continue;
                var list = Groups[gi].List();
                if (list == null) continue;
                foreach (var m in list)
                {
                    if (m == null || m.Count <= Groups[gi].Cunning) continue;
                    if (q.Length > 0 && Name(Groups[gi], m).ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) < 0) continue;
                    res.Add(new Entry { G = Groups[gi], M = m });
                }
            }
            return res;
        }

        static string Name(Group g, List<string> m) { return m[g.NameIdx].Split('|')[0]; }

        static int Idx(Group g, string f)
        {
            switch (f)
            {
                case "age": return g.Age; case "health": return g.Health; case "mood": return g.Mood;
                case "renown": return g.Renown; case "charisma": return g.Charisma; case "writing": return g.Writing;
                case "might": return g.Might; case "business": return g.Business; case "arts": return g.Arts;
                case "cunning": return g.Cunning;
            }
            return -1;
        }

        static float Get(Group g, List<string> m, string f)
        {
            string s;
            if (f == "luck")
            {
                var p = m[g.NameIdx].Split('|');
                s = p.Length > LuckSub ? p[LuckSub] : "0";
            }
            else s = m[Idx(g, f)];
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0f;
        }

        static string Fmt(float v)
        {
            return Mathf.Approximately(v, Mathf.Round(v)) ? ((long)Mathf.Round(v)).ToString() : v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        static void Set(Group g, List<string> m, string f, float v)
        {
            if (f == "luck")
            {
                var p = m[g.NameIdx].Split('|');
                if (p.Length > LuckSub) { p[LuckSub] = Fmt(v); m[g.NameIdx] = string.Join("|", p); }
            }
            else m[Idx(g, f)] = Fmt(v);
        }

        void LoadBuf(Group g, List<string> m)
        {
            foreach (var fl in Fields) buf[fl.Id] = Fmt(Get(g, m, fl.Id));
        }

        void DrawChars()
        {
            GUILayout.BeginHorizontal();
            string[] names = { L("All", "ทั้งหมด"), Groups[0].En, Groups[1].En, Groups[2].En };
            if (th) names = new[] { "ทั้งหมด", Groups[0].Th, Groups[1].Th, Groups[2].Th };
            for (int i = 0; i < names.Length; i++)
                if (GUILayout.Toggle(filter == i, names[i], "Button", GUILayout.Width(95))) filter = i;
            GUILayout.Label(L("Search:", "ค้นหา:"), GUILayout.Width(60));
            search = GUILayout.TextField(search, GUILayout.Width(150));
            GUILayout.EndHorizontal();

            var entries = Collect();
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(310));
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(410));
            foreach (var e in entries)
            {
                bool sel = selId == e.M[0] && selGroup == e.G.Name;
                string label = Name(e.G, e.M) + "  (" + (th ? e.G.Th : e.G.En) + ", " + e.M[e.G.Age] + ")";
                if (GUILayout.Toggle(sel, label, "Button") && !sel)
                {
                    selId = e.M[0]; selGroup = e.G.Name; LoadBuf(e.G, e.M);
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.BeginVertical();
            Entry cur = null;
            foreach (var e in entries) if (selId == e.M[0] && selGroup == e.G.Name) { cur = e; break; }
            if (cur == null)
            {
                GUILayout.Label(L("Select a character on the left", "เลือกตัวละครทางซ้าย"));
            }
            else
            {
                GUILayout.Label(Name(cur.G, cur.M) + "  [" + (th ? cur.G.Th : cur.G.En) + "]  id " + cur.M[0]);
                foreach (var fl in Fields)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(th ? fl.Th : fl.En, GUILayout.Width(90));
                    string v;
                    if (!buf.TryGetValue(fl.Id, out v)) v = "";
                    buf[fl.Id] = GUILayout.TextField(v, GUILayout.Width(90));
                    GUILayout.Label(L("now ", "ตอนนี้ ") + Fmt(Get(cur.G, cur.M, fl.Id)));
                    GUILayout.EndHorizontal();
                }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(L("Apply", "ใช้ค่า"))) Apply(cur);
                if (GUILayout.Button(L("Reset form", "รีเซ็ตฟอร์ม"))) LoadBuf(cur.G, cur.M);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(L("Health+Mood = 100", "สุขภาพ+อารมณ์ = 100"))) { buf["health"] = "100"; buf["mood"] = "100"; Apply(cur); }
                skillAll = GUILayout.TextField(skillAll, GUILayout.Width(50));
                if (GUILayout.Button(L("All skills =", "ทุกทักษะ ="))) { foreach (var s in Skills) buf[s] = skillAll; Apply(cur); }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        void Apply(Entry e)
        {
            int n = 0;
            foreach (var fl in Fields)
            {
                float v;
                if (!buf.ContainsKey(fl.Id) || !float.TryParse(buf[fl.Id], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
                v = Mathf.Clamp(v, fl.Min, fl.Max);
                if (Fmt(Get(e.G, e.M, fl.Id)) != Fmt(v)) { Set(e.G, e.M, fl.Id, v); n++; }
            }
            LoadBuf(e.G, e.M);
            status = Name(e.G, e.M) + ": " + n + L(" field(s) changed", " ช่องที่เปลี่ยน");
        }
    }
}
