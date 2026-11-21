using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PKTrainer
{
    /// <summary>PK Trainer - House of Legacy: Insert / F9 / ปุ่มมุมซ้ายล่าง เพื่อเปิด-ปิดหน้าต่าง แก้ค่าในเกมที่กำลังรันได้ทันที</summary>
    [BepInPlugin("pk.hol.trainer", "PK Trainer - House of Legacy", "1.4.0")]
    public partial class Plugin : BaseUnityPlugin
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
        Rect win = new Rect(30, 30, 1000, 740);
        int tab;
        string amtCoin = "100000", amtGold = "100", amtSlave = "10", amtRenown = "100";
        int filter; // 0 = all
        string search = "";
        Vector2 scroll, pageScroll;
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
            InitMirror();
            Logger.LogInfo(Title + " loaded. Insert / F9 / button at bottom-left");
        }

        void OnDestroy() { ReleaseUi(); Logger.LogWarning(Title + " object destroyed"); }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Insert) || Input.GetKeyDown(KeyCode.F9)) show = !show;
            MirrorUpdate();

            // กันคลิก/ซูมทะลุ: ขณะเมาส์อยู่บนหน้าต่างของเรา ปิด EventSystem ของเกมและห้ามกล้องขยับ
            var mp = Input.mousePosition;
            var g = new Vector2(mp.x, Screen.height - mp.y);
            bool over = (show && win.Contains(g)) || new Rect(6, Screen.height - 30, 120, 24).Contains(g);
            if (over)
            {
                if (!hovering) { hovering = true; prevCamMove = Mainload.isCanMoveCamera; }
                Mainload.isCanMoveCamera = false;
                if (blockedEs == null)
                {
                    var es = EventSystem.current;
                    if (es != null && es.enabled) { es.enabled = false; blockedEs = es; }
                }
            }
            else if (hovering)
            {
                hovering = false;
                Mainload.isCanMoveCamera = prevCamMove;
                ReleaseUi();
            }
        }

        bool hovering, prevCamMove;
        EventSystem blockedEs;

        // หมายเหตุ: ตอนปิด EventSystem แล้ว EventSystem.current จะกลายเป็น null จึงต้องจำตัวที่ปิดไว้เอง
        void ReleaseUi()
        {
            if (blockedEs != null) blockedEs.enabled = true;
            blockedEs = null;
        }

        void OnDisable() { ReleaseUi(); }

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

        GUIStyle stNav, stH1, stCap, stSec, stChip;

        void BuildStyles()
        {
            stNav = new GUIStyle(skin.button) { alignment = TextAnchor.MiddleLeft, fontSize = 16, fixedHeight = 36, padding = new RectOffset(16, 8, 4, 4) };
            stH1 = new GUIStyle(skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, wordWrap = false };
            stSec = new GUIStyle(skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            stCap = new GUIStyle(skin.label) { fontSize = 13, wordWrap = true };
            stCap.normal.textColor = new Color(0.78f, 0.78f, 0.78f);
            stChip = new GUIStyle(skin.label) { fontSize = 15, richText = true, alignment = TextAnchor.MiddleLeft };
        }

        // ---- โครงหน้า: ซ้ายเป็นเมนู ขวาเป็นเนื้อหา ----
        struct Page { public int Id; public string En, Th, DescEn, DescTh; }
        static readonly Page[] PagesFamily =
        {
            new Page { Id = 0, En = "Overview", Th = "ภาพรวม", DescEn = "Money, slaves, renown and your names.", DescTh = "เงิน ทอง ทาส ชื่อเสียง และการเปลี่ยนชื่อ" },
        };
        static readonly Page[] PagesPeople =
        {
            new Page { Id = 1, En = "Characters", Th = "ตัวละคร", DescEn = "Edit age, health and skills of anyone in the family.", DescTh = "แก้อายุ สุขภาพ ทักษะ ของทุกคนในตระกูล" },
            new Page { Id = 5, En = "Talent & advisors", Th = "ค้นหาคนเก่ง/จ้างที่ปรึกษา", DescEn = "Find the most talented people by score and hire them as advisors.", DescTh = "ค้นหาคนเก่งตามคะแนน แล้วจ้างเป็นที่ปรึกษา" },
            new Page { Id = 3, En = "Marriage & relations", Th = "แต่งงาน/ความสัมพันธ์", DescEn = "Marry into/out of any family, the court or commoners; raise relations.", DescTh = "สั่งแต่งงานกับตระกูลอื่น ราชสำนัก สามัญชน และเพิ่มความสัมพันธ์" },
        };
        static readonly Page[] PagesAssets =
        {
            new Page { Id = 4, En = "Storehouse", Th = "คลังและเงิน", DescEn = "Spawn items with their effects, set coin and gold.", DescTh = "เสกไอเทมพร้อมดูสรรพคุณ และตั้งเหรียญ/ทอง" },
            new Page { Id = 6, En = "Farm", Th = "ฟาร์ม", DescEn = "Auto-collect produce and make crops, trees and animals produce instantly.", DescTh = "เก็บผลผลิตอัตโนมัติ และทำให้พืช ต้นไม้ สัตว์ ออกผลผลิตทันที" },
            new Page { Id = 2, En = "Map & blueprints", Th = "แผนที่/ผังอาคาร", DescEn = "Clear chosen building types and import blueprint files.", DescTh = "เคลียร์อาคารตามชนิดที่เลือก และนำเข้าไฟล์ผังอาคาร" },
        };

        void NavGroup(string caption, Page[] pages)
        {
            GUILayout.Space(8);
            GUILayout.Label(caption, stCap);
            foreach (var pg in pages)
                if (GUILayout.Toggle(tab == pg.Id, L(pg.En, pg.Th), stNav)) tab = pg.Id;
        }

        Page CurrentPage()
        {
            foreach (var grp in new[] { PagesFamily, PagesPeople, PagesAssets })
                foreach (var pg in grp) if (pg.Id == tab) return pg;
            return PagesFamily[0];
        }

        void DrawHeader(bool loaded)
        {
            GUILayout.BeginHorizontal();
            if (loaded)
            {
                GUILayout.Label("<b>" + L("Coin", "เหรียญ") + "</b>  " + Coins().ToString("N0"), stChip, GUILayout.Width(240));
                GUILayout.Label("<b>" + L("Gold", "ทอง") + "</b>  " + Mainload.CGNum[1], stChip, GUILayout.Width(150));
                GUILayout.Label("<b>" + L("Family Lv", "ระดับตระกูล") + "</b>  " + Mainload.FamilyData[2], stChip, GUILayout.Width(170));
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Toggle(th, "ไทย", "Button", GUILayout.Width(60)) && !th) SetLang(true);
            if (GUILayout.Toggle(!th, "EN", "Button", GUILayout.Width(60)) && th) SetLang(false);
            GUILayout.EndHorizontal();
        }

        void BuildSkin()
        {
            skin = Instantiate(GUI.skin);
            skin.hideFlags = HideFlags.HideAndDontSave;
            skin.window.normal.textColor = Color.white;
            skin.window.onNormal.textColor = Color.white;
            Font f = null;
            try { f = Font.CreateDynamicFontFromOSFont(new[] { "Tahoma", "Leelawadee UI", "Segoe UI", "Arial" }, 15); } catch { }
            if (f != null)
            {
                skin.font = f;
                foreach (var s in new[] { skin.label, skin.button, skin.textField, skin.window, skin.box, skin.toggle })
                { s.font = f; s.fontSize = 15; }
            }
            BuildStyles();
        }

        // เลื่อนรายการด้วยล้อเมาส์ (กันกรณี ScrollView ไม่รับล้อ)
        static Vector2 WheelScroll(Vector2 sc)
        {
            var r = GUILayoutUtility.GetLastRect();
            var e = Event.current;
            if (e.type == EventType.ScrollWheel && r.Contains(e.mousePosition))
            {
                sc.y += e.delta.y * 24f;
                e.Use();
            }
            return sc;
        }

        void SetLang(bool thai)
        {
            th = thai;
            cfgThai.Value = thai;
            status = L("Ready", "พร้อมใช้งาน");
        }

        void DrawWindow(int id)
        {
            bool loaded = Mainload.CGNum != null && Mainload.FamilyData != null;
            DrawHeader(loaded);
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();

            // ---- เมนูซ้าย
            GUILayout.BeginVertical(GUILayout.Width(210));
            NavGroup(L("FAMILY", "ตระกูล"), PagesFamily);
            NavGroup(L("PEOPLE", "ผู้คน"), PagesPeople);
            NavGroup(L("ASSETS", "ทรัพย์สิน"), PagesAssets);
            GUILayout.FlexibleSpace();
            GUILayout.Label(L("Insert / F9 to hide", "กด Insert / F9 เพื่อซ่อน"), stCap);
            GUILayout.EndVertical();

            // ---- เนื้อหาขวา
            GUILayout.BeginVertical(GUILayout.Width(750));
            var pgInfo = CurrentPage();
            GUILayout.Label(L(pgInfo.En, pgInfo.Th), stH1);
            GUILayout.Label(L(pgInfo.DescEn, pgInfo.DescTh), stCap);
            GUILayout.Space(4);
            if (!loaded)
            {
                GUILayout.Label(L("Game not loaded yet - load a save first.", "ยังไม่ได้โหลดเกม - โหลด save ก่อน"));
            }
            else
            {
                pageScroll = GUILayout.BeginScrollView(pageScroll, GUILayout.Height(575));
                try
                {
                    if (tab == 0) DrawPlayer(); else if (tab == 1) DrawChars(); else if (tab == 3) DrawMarriage(); else if (tab == 4) DrawItems(); else if (tab == 5) DrawTalent(); else if (tab == 6) DrawFarm(); else DrawMap();
                }
                catch (Exception e) { status = "Error: " + e.Message; Logger.LogError(e); }
                GUILayout.EndScrollView();
                pageScroll = WheelScroll(pageScroll);
            }
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(status, stCap);
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
            GUILayout.Space(4);
            GUILayout.Label(L("Resources", "ทรัพยากร"), stSec);
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
            GUILayout.Label(L("Family level", "ระดับตระกูล"), stSec);
            GUILayout.BeginHorizontal();
            GUILayout.Label(L("Level ", "ระดับ ") + Mainload.FamilyData[2] + "  (" + L("max 80", "สูงสุด 80") + ")", GUILayout.Width(150));
            lvTarget = GUILayout.TextField(lvTarget, GUILayout.Width(60));
            int lt;
            if (GUILayout.Button(L("Raise to this level", "เพิ่มถึงระดับนี้"), GUILayout.Width(200)) && int.TryParse(lvTarget, out lt)) RaiseFamilyLevel(lt);
            if (GUILayout.Button("+1", GUILayout.Width(60))) RaiseFamilyLevel(int.Parse(Mainload.FamilyData[2]) + 1);
            if (GUILayout.Button("+5", GUILayout.Width(60))) RaiseFamilyLevel(int.Parse(Mainload.FamilyData[2]) + 5);
            GUILayout.EndHorizontal();
            GUILayout.Label(L("Raising uses the game's own renown rule, so buildings and options unlock normally. Lowering is not supported.",
                              "เพิ่มด้วยกฎชื่อเสียงของเกมเอง อาคารและตัวเลือกจะปลดล็อกตามปกติ (ลดระดับไม่รองรับ)"), stCap);

            GUILayout.Space(10);
            GUILayout.Label(L("Time", "เวลา"), stSec);
            GUILayout.BeginHorizontal();
            if (Mainload.Time_now != null && Mainload.Time_now.Count >= 3)
                GUILayout.Label(L("Year ", "ปี ") + Mainload.Time_now[0] + L("  Month ", "  เดือน ") + Mainload.Time_now[1] + L("  Day ", "  วัน ") + Mainload.Time_now[2], GUILayout.Width(260));
            foreach (var d in new[] { 1, 7, 30 })
                if (GUILayout.Button(L("Skip ", "ข้าม ") + d + L(" day(s)", " วัน"), GUILayout.Width(130)) && skipDaysLeft == 0) { skipDaysLeft = d; StartCoroutine(SkipDays()); }
            GUILayout.EndHorizontal();
            if (skipDaysLeft > 0) GUILayout.Label(L("Skipping... days left: ", "กำลังข้ามเวลา... เหลือ ") + skipDaysLeft, stCap);
            GUILayout.Label(L("Days run one by one so the game processes everything normally (births, deaths, income).", "ข้ามทีละวันให้เกมประมวลผลทุกอย่างตามปกติ (เกิด ตาย รายได้)"), stCap);

            GUILayout.Space(10);
            GUILayout.Label(L("Quick actions", "ปุ่มลัด"), stSec);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L("Coin = 999,999,999", "เหรียญ = 999,999,999"))) { SetCoins(999999999); status = L("Coin = 999,999,999", "เหรียญ = 999,999,999"); }
            if (GUILayout.Button(L("Gold = 99,999", "ทอง = 99,999"))) { Mainload.CGNum[1] = "99999"; status = L("Gold = 99,999", "ทอง = 99,999"); }
            GUILayout.EndHorizontal();

            // ---- เปลี่ยนชื่อ
            GUILayout.Space(14);
            GUILayout.Label(L("Names", "ชื่อ"), stSec);
            GUILayout.BeginHorizontal();
            GUILayout.Label(L("Family name", "ชื่อตระกูล"), GUILayout.Width(110));
            GUILayout.Label(Mainload.FamilyData[1], GUILayout.Width(130));
            famBuf = GUILayout.TextField(famBuf ?? "", GUILayout.Width(180));
            if (GUILayout.Button(L("Rename", "เปลี่ยนชื่อ"), GUILayout.Width(110))) status = RenameFamily(famBuf);
            GUILayout.EndHorizontal();

            var fe = FounderEntry();
            GUILayout.BeginHorizontal();
            GUILayout.Label(L("Founder name", "ชื่อตัวเอง"), GUILayout.Width(110));
            GUILayout.Label(fe == null ? "-" : Name(fe.G, fe.M), GUILayout.Width(130));
            founderBuf = GUILayout.TextField(founderBuf ?? "", GUILayout.Width(180));
            if (GUILayout.Button(L("Rename", "เปลี่ยนชื่อ"), GUILayout.Width(110)) && fe != null) status = RenameMember(fe.G, fe.M, founderBuf);
            GUILayout.EndHorizontal();
            GUILayout.Label(L("Tip: any other person can be renamed in the Characters tab. Reopen the info panel to see the new name.",
                              "เคล็ดลับ: คนอื่นเปลี่ยนชื่อได้ในแท็บตัวละคร เปิดหน้าข้อมูลใหม่เพื่อดูชื่อที่เปลี่ยน"));
        }

        string famBuf, founderBuf, nameBuf, lvTarget = "30";
        int skipDaysLeft;

        void RaiseFamilyLevel(int target)
        {
            try
            {
                target = Math.Min(80, target);
                int lv = int.Parse(Mainload.FamilyData[2]), guard = 0;
                while (lv < target && guard++ < 200)
                {
                    int need = FormulaData.ShengWangForLv(lv) - int.Parse(Mainload.FamilyData[3]);
                    FormulaData.FamilyLvSW(Math.Max(need, 1));
                    int now = int.Parse(Mainload.FamilyData[2]);
                    if (now == lv) break;
                    lv = now;
                }
                status = L("Family level is now ", "ระดับตระกูลตอนนี้ ") + Mainload.FamilyData[2];
            }
            catch (Exception e) { Logger.LogError(e); status = "Error: " + e.Message; }
        }

        System.Collections.IEnumerator SkipDays()
        {
            while (skipDaysLeft > 0)
            {
                Mainload.isTimeGoDay = true;
                yield return new WaitForSecondsRealtime(0.8f);
                skipDaysLeft--;
            }
            status = L("Time skipped.", "ข้ามเวลาเสร็จแล้ว");
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

            nameBuf = Name(g, m);
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
                string label = Name(e.G, e.M) + "\n" + (th ? e.G.Th : e.G.En) + ", " + e.M[e.G.Age];
                GUILayout.BeginHorizontal();
                Portrait(LookOf(e.G, e.M), 36, 54);
                if (GUILayout.Toggle(sel, label, "Button", GUILayout.Height(54)) && !sel)
                {
                    selId = e.M[0]; selGroup = e.G.Name; LoadBuf(e.G, e.M);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            scroll = WheelScroll(scroll);
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
                GUILayout.BeginHorizontal();
                Portrait(LookOf(cur.G, cur.M), 96, 144);
                GUILayout.BeginVertical();
                GUILayout.Label(Name(cur.G, cur.M), stH1);
                GUILayout.Label("[" + (th ? cur.G.Th : cur.G.En) + "]  id " + cur.M[0] + "   " + L("age ", "อายุ ") + cur.M[cur.G.Age], stCap);
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label(L("Name", "ชื่อ"), GUILayout.Width(90));
                nameBuf = GUILayout.TextField(nameBuf ?? "", GUILayout.Width(210));
                if (GUILayout.Button(L("Rename", "เปลี่ยนชื่อ"), GUILayout.Width(100)))
                {
                    status = RenameMember(cur.G, cur.M, nameBuf);
                    LoadBuf(cur.G, cur.M);
                }
                GUILayout.EndHorizontal();
                GUILayout.Label(L("Type a number, then Set / + / - (applies immediately)", "ใส่ตัวเลข แล้วกด ตั้ง / + / - (มีผลทันที)"), stCap);
                foreach (var fl in Fields)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(th ? fl.Th : fl.En, GUILayout.Width(80));
                    GUILayout.Label(Fmt(Get(cur.G, cur.M, fl.Id)), GUILayout.Width(70));
                    string v;
                    if (!buf.TryGetValue(fl.Id, out v)) v = "10";
                    v = GUILayout.TextField(v, GUILayout.Width(70));
                    buf[fl.Id] = v;
                    float num;
                    bool ok = float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out num);
                    if (GUILayout.Button(L("Set", "ตั้งค่า"), GUILayout.Width(70)) && ok) ChangeField(cur, fl, num, 0);
                    if (GUILayout.Button("+", GUILayout.Width(50)) && ok) ChangeField(cur, fl, num, 1);
                    if (GUILayout.Button("-", GUILayout.Width(50)) && ok) ChangeField(cur, fl, num, -1);
                    GUILayout.EndHorizontal();
                }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(L("Health+Mood = 100", "สุขภาพ+อารมณ์ = 100")))
                {
                    foreach (var fl in Fields) if (fl.Id == "health" || fl.Id == "mood") ChangeField(cur, fl, 100, 0);
                }
                skillAll = GUILayout.TextField(skillAll, GUILayout.Width(50));
                if (GUILayout.Button(L("All skills =", "ทุกทักษะ =")))
                {
                    float sv; float.TryParse(skillAll, NumberStyles.Float, CultureInfo.InvariantCulture, out sv);
                    foreach (var fl in Fields) if (Array.IndexOf(Skills, fl.Id) >= 0) ChangeField(cur, fl, sv, 0);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            // ---- ทั้งตระกูลในคลิกเดียว (สายตระกูล)
            GUILayout.Space(8);
            GUILayout.Label(L("Whole family (lineage)", "ทั้งตระกูล (สายตระกูล)"), stSec);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L("Everyone: health + mood = 100", "ทุกคน: สุขภาพ+อารมณ์ = 100"), GUILayout.Height(30))) BulkLineage(0);
            if (GUILayout.Button(L("Everyone: stamina full", "ทุกคน: พลังกายเต็ม"), GUILayout.Height(30))) BulkLineage(1);
            if (GUILayout.Button(L("Everyone: all skills =", "ทุกคน: ทุกทักษะ =") + " " + skillAll, GUILayout.Height(30))) BulkLineage(2);
            GUILayout.EndHorizontal();
        }

        void BulkLineage(int what)
        {
            int n = 0;
            try
            {
                var g = Groups[0];
                float sv;
                float.TryParse(skillAll, NumberStyles.Float, CultureInfo.InvariantCulture, out sv);
                sv = Mathf.Clamp(sv, 0, 999);
                foreach (var m in Mainload.Member_now)
                {
                    if (m == null || m.Count <= 30) continue;
                    if (what == 0) { Set(g, m, "health", 100); Set(g, m, "mood", 100); }
                    else if (what == 1) { int age; if (int.TryParse(m[g.Age], out age)) m[30] = FormulaData.GetTiliMax(age).ToString(); }
                    else { foreach (var s in Skills) Set(g, m, s, sv); }
                    n++;
                }
                status = L("Done for ", "ทำให้ ") + n + L(" people", " คนแล้ว");
            }
            catch (Exception ex) { Logger.LogError(ex); status = "Error: " + ex.Message; }
        }

        // mode: 0 = ตั้งเป็นค่านั้น, 1 = บวก, -1 = ลบ  (จำกัดในช่วงของช่องนั้น)
        void ChangeField(Entry e, Field fl, float num, int mode)
        {
            float cur = Get(e.G, e.M, fl.Id);
            float v = mode == 0 ? num : cur + mode * num;
            v = Mathf.Clamp(v, fl.Min, fl.Max);
            Set(e.G, e.M, fl.Id, v);
            status = Name(e.G, e.M) + ": " + (th ? fl.Th : fl.En) + " = " + Fmt(Get(e.G, e.M, fl.Id));
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
