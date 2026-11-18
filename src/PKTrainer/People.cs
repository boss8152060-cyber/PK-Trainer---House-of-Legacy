using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PKTrainer
{
    // ===================================================================
    //  People Finder (ค้นหา/ให้คะแนนคนในเกม) + การเปลี่ยนชื่อ
    // ===================================================================
    public partial class Plugin
    {
        // ---------------------------------------------------------- model
        class Person
        {
            public string Key, Src, Id, Name, ClanLabel;
            public bool Female;
            public int ClassId;      // 0 commoner, 1 official, 2 titled noble, 3 clan daughter, 4 courtesan
            public float Age, W, M, B, A, C, Mood, Charisma, Stamina, Renown, Luck;
            public float Score;
        }

        static float F(string s)
        {
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0f;
        }

        static string At(List<string> m, int i) { return i >= 0 && i < m.Count && m[i] != null ? m[i] : "0"; }

        static string[] Blk(string s) { return (s ?? "").Split('|'); }


        static string ClanName(int a)
        {
            var l = Mainload.ShiJia_Now;
            return l != null && a < l.Count && l[a].Count > 1 ? l[a][1] : "#" + a;
        }

        // layout ร่วมของ Member_Hanmen และ Member_other
        static Person FromClanLayout(List<string> m, string src)
        {
            var b = Blk(At(m, 2));
            var p = new Person { Src = src, Id = m[0], Name = b[0] };
            p.Female = b.Length > 4 && b[4] == "0";
            p.Luck = b.Length > 7 ? F(b[7]) : 0;
            p.Age = F(At(m, 3));
            p.W = F(At(m, 4)); p.M = F(At(m, 5)); p.B = F(At(m, 6)); p.A = F(At(m, 7));
            p.Mood = F(At(m, 8));
            p.Renown = F(At(m, 17)); p.Charisma = F(At(m, 19)); p.Stamina = F(At(m, 20)); p.C = F(At(m, 22));
            if (At(m, 11) != "0|0") p.ClassId = 2;
            else
            {
                var sf = Blk(At(m, 9))[0].Split('@')[0];
                p.ClassId = sf != "0" ? 1 : 0;
            }
            return p;
        }

        List<Person> BuildPeople()
        {
            var res = new List<Person>();
            try
            {
                var hc = Mainload.HanMen_City;
                if (hc != null)
                    foreach (var m in hc.ToArray())
                    {
                        if (m == null || m.Count < 23) continue;
                        var p = FromClanLayout(m, "city");
                        res.Add(p);
                    }

                var hm = Mainload.Member_Hanmen;
                if (hm != null)
                    for (int i = 0; i < hm.Count; i++)
                    {
                        var m = hm[i]; if (m == null || m.Count < 23) continue;
                        var p = FromClanLayout(m, "scholar");
                        res.Add(p);
                    }

                var mo = Mainload.Member_other;
                if (mo != null)
                    for (int a = 0; a < mo.Count; a++)
                        for (int bi = 0; bi < mo[a].Count; bi++)
                        {
                            var m = mo[a][bi]; if (m == null || m.Count < 23) continue;
                            var p = FromClanLayout(m, "clanman");
                            p.ClanLabel = ClanName(a);
                            res.Add(p);
                        }

                var mq = Mainload.Member_Other_qu;
                if (mq != null)
                    for (int a = 0; a < mq.Count; a++)
                        for (int bi = 0; bi < mq[a].Count; bi++)
                        {
                            var m = mq[a][bi]; if (m == null || m.Count < 18) continue;
                            var b = Blk(At(m, 2));
                            var p = new Person { Src = "clanwoman", Id = m[0], Name = b[0], Female = true, ClassId = 3, ClanLabel = ClanName(a) };
                            p.Luck = b.Length > 7 ? F(b[7]) : 0;
                            p.Age = F(At(m, 3));
                            p.W = F(At(m, 4)); p.M = F(At(m, 5)); p.B = F(At(m, 6)); p.A = F(At(m, 7));
                            p.Mood = F(At(m, 8)); p.Renown = F(At(m, 10));
                            p.Charisma = F(At(m, 15)); p.Stamina = F(At(m, 16)); p.C = F(At(m, 17));
                            res.Add(p);
                        }

                var ql = Mainload.Member_Qinglou;
                if (ql != null)
                    for (int c = 0; c < ql.Count; c++)
                        for (int i = 0; i < ql[c].Count; i++)
                        {
                            var m = ql[c][i]; if (m == null || m.Count < 12) continue;
                            var b = Blk(At(m, 3));
                            var p = new Person { Src = "qinglou", Id = m[1], Name = b[0], Female = true, ClassId = 4, ClanLabel = "#" + c };
                            p.Luck = b.Length > 7 ? F(b[7]) : 0;
                            p.Age = F(At(m, 4));
                            p.W = F(At(m, 5)); p.M = F(At(m, 6)); p.B = F(At(m, 7)); p.A = F(At(m, 8));
                            p.Charisma = F(At(m, 10)); p.Stamina = F(At(m, 11));
                            res.Add(p);
                        }
            }
            catch (Exception e) { Logger.LogError(e); }

            foreach (var p in res) p.Key = p.Src + "#" + p.Id;
            return res;
        }

        // ------------------------------------------------------- UI state
        List<Person> people = new List<Person>();
        List<Person> view = new List<Person>();
        bool finderDirty = true;
        float lastBuild = -100f;
        readonly bool[] srcOn = { true, true, true, true, true };  // city, scholar, clanman, clanwoman, qinglou
        int fGender, fClass, sortKey;                            // 0 = all / score
        string fAgeMin = "0", fAgeMax = "100", fSearch = "";
        string selKey = "";
        Vector2 finderScroll;
        // น้ำหนัก: Writing, Might, Business, Arts, Cunning, Charisma, Luck
        readonly float[] wts = { 1, 1, 1, 1, 1, 0, 0 };

        static readonly string[] SrcEn = { "In this city", "Scholars", "Clan men", "Clan women", "Qinglou" };
        static readonly string[] SrcTh = { "คนในเมืองนี้", "นักปราชญ์", "ชายตระกูลอื่น", "หญิงตระกูลอื่น", "ชิงโหลว" };
        static readonly string[] WtEn = { "Writing", "Might", "Business", "Arts", "Cunning", "Charisma", "Luck" };
        static readonly string[] WtTh = { "อักษร", "ยุทธ์", "การค้า", "ศิลป์", "กลยุทธ์", "เสน่ห์", "โชคลาภ" };

        string ClassName(Person p)
        {
            switch (p.ClassId)
            {
                case 0: return L("Commoner", "สามัญชน");
                case 1: return L("Official", "ข้าราชการ");
                case 2: return L("Noble", "ขุนนาง");
                case 3: return L("Clan", "ตระกูล");
                default: return L("Courtesan", "ชิงโหลว");
            }
        }

        float[] Vals(Person p) { return new[] { p.W, p.M, p.B, p.A, p.C, p.Charisma, p.Luck }; }

        float ScoreOf(Person p)
        {
            var v = Vals(p);
            float s = 0, w = 0;
            for (int i = 0; i < wts.Length; i++) { s += wts[i] * v[i]; w += wts[i]; }
            return w > 0 ? s / w : 0;
        }

        void Rebuild()
        {
            people = BuildPeople();
            float amin = Mathf.Max(0, F(fAgeMin)), amax = F(fAgeMax) <= 0 ? 999 : F(fAgeMax);
            string q = fSearch.ToLowerInvariant();
            view = new List<Person>();
            foreach (var p in people)
            {
                int si = SrcIndex(p);
                if (!srcOn[si]) continue;
                if (fGender == 1 && p.Female) continue;
                if (fGender == 2 && !p.Female) continue;
                if (fClass == 1 && p.ClassId != 0) continue;
                if (fClass == 2 && p.ClassId != 1) continue;
                if (fClass == 3 && p.ClassId != 2 && p.ClassId != 3) continue;
                if (p.Age < amin || p.Age > amax) continue;
                if (q.Length > 0 && p.Name.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) < 0) continue;
                p.Score = ScoreOf(p);
                view.Add(p);
            }
            view.Sort((x, y) => SortVal(y).CompareTo(SortVal(x)));
            lastBuild = Time.unscaledTime;
            finderDirty = false;
        }

        float SortVal(Person p)
        {
            switch (sortKey)
            {
                case 1: return -p.Age;
                case 2: return p.W; case 3: return p.M; case 4: return p.B; case 5: return p.A; case 6: return p.C;
                case 7: return p.Charisma; case 8: return p.Luck;
                default: return p.Score;
            }
        }

        void SetPreset(float w, float m, float b, float a, float c, float ch, float lk)
        {
            wts[0] = w; wts[1] = m; wts[2] = b; wts[3] = a; wts[4] = c; wts[5] = ch; wts[6] = lk;
            finderDirty = true;
        }

        // ----------------------------------------------------------- draw
        void DrawFinder()
        {
            if (Event.current.type == EventType.Layout && (finderDirty || Time.unscaledTime - lastBuild > 3f)) Rebuild();

            // --- filters
            GUILayout.BeginHorizontal();
            GUILayout.Label(L("Source:", "แหล่ง:"), GUILayout.Width(55));
            for (int i = 0; i < 5; i++)
            {
                bool n = GUILayout.Toggle(srcOn[i], th ? SrcTh[i] : SrcEn[i], "Button", GUILayout.Width(105));
                if (n != srcOn[i]) { srcOn[i] = n; finderDirty = true; }
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(L("Refresh", "รีเฟรช"), GUILayout.Width(80))) finderDirty = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(L("Gender:", "เพศ:"), GUILayout.Width(55));
            string[] gn = th ? new[] { "ทั้งหมด", "ชาย", "หญิง" } : new[] { "All", "Male", "Female" };
            for (int i = 0; i < 3; i++)
                if (GUILayout.Toggle(fGender == i, gn[i], "Button", GUILayout.Width(70)) && fGender != i) { fGender = i; finderDirty = true; }
            GUILayout.Space(10);
            GUILayout.Label(L("Class:", "ชนชั้น:"), GUILayout.Width(55));
            string[] cn = th ? new[] { "ทั้งหมด", "สามัญชน", "ข้าราชการ", "ขุนนาง/ตระกูล" } : new[] { "All", "Commoner", "Official", "Noble/Clan" };
            for (int i = 0; i < 4; i++)
                if (GUILayout.Toggle(fClass == i, cn[i], "Button", GUILayout.Width(i == 3 ? 120 : 85)) && fClass != i) { fClass = i; finderDirty = true; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(L("Age:", "อายุ:"), GUILayout.Width(55));
            string a1 = GUILayout.TextField(fAgeMin, GUILayout.Width(45));
            GUILayout.Label("-", GUILayout.Width(12));
            string a2 = GUILayout.TextField(fAgeMax, GUILayout.Width(45));
            if (a1 != fAgeMin || a2 != fAgeMax) { fAgeMin = a1; fAgeMax = a2; finderDirty = true; }
            GUILayout.Space(20);
            GUILayout.Label(L("Search:", "ค้นหา:"), GUILayout.Width(60));
            string s1 = GUILayout.TextField(fSearch, GUILayout.Width(160));
            if (s1 != fSearch) { fSearch = s1; finderDirty = true; }
            GUILayout.Space(20);
            GUILayout.Label(L("Sort:", "เรียง:"), GUILayout.Width(45));
            string[] sn = th ? new[] { "คะแนน", "อายุ(น้อย)", "อักษร", "ยุทธ์", "การค้า", "ศิลป์", "กลยุทธ์", "เสน่ห์", "โชค" }
                             : new[] { "Score", "Age(low)", "Writ", "Might", "Biz", "Arts", "Cunn", "Charm", "Luck" };
            for (int i = 0; i < sn.Length; i++)
                if (GUILayout.Toggle(sortKey == i, sn[i], "Button", GUILayout.Width(i == 1 ? 80 : 58)) && sortKey != i) { sortKey = i; finderDirty = true; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            // --- list (left)
            GUILayout.BeginVertical(GUILayout.Width(700));
            GUILayout.BeginHorizontal();
            GUILayout.Label(L("Name", "ชื่อ"), GUILayout.Width(200));
            string[] hd = { "", L("Age", "อายุ"), L("Class", "ชนชั้น"), "Wri", "Mig", "Biz", "Art", "Cun", "Cha", L("Score", "คะแนน") };
            int[] hw = { 24, 34, 74, 38, 38, 38, 38, 38, 38, 52 };
            for (int i = 0; i < hd.Length; i++) GUILayout.Label(hd[i], GUILayout.Width(hw[i]));
            GUILayout.EndHorizontal();

            finderScroll = GUILayout.BeginScrollView(finderScroll, GUILayout.Height(400));
            foreach (var p in view)
            {
                GUILayout.BeginHorizontal();
                bool sel = selKey == p.Key;
                if (GUILayout.Toggle(sel, p.Name, "Button", GUILayout.Width(200)) && !sel) selKey = p.Key;
                GUILayout.Label(p.Female ? "F" : "M", GUILayout.Width(24));
                GUILayout.Label(p.Age.ToString("0"), GUILayout.Width(34));
                GUILayout.Label(ClassName(p), GUILayout.Width(74));
                GUILayout.Label(p.W.ToString("0"), GUILayout.Width(38));
                GUILayout.Label(p.M.ToString("0"), GUILayout.Width(38));
                GUILayout.Label(p.B.ToString("0"), GUILayout.Width(38));
                GUILayout.Label(p.A.ToString("0"), GUILayout.Width(38));
                GUILayout.Label(p.C.ToString("0"), GUILayout.Width(38));
                GUILayout.Label(p.Charisma.ToString("0"), GUILayout.Width(38));
                GUILayout.Label(p.Score.ToString("0.0"), GUILayout.Width(52));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            finderScroll = WheelScroll(finderScroll);
            GUILayout.Label(view.Count + " / " + people.Count + L(" people", " คน"));
            GUILayout.EndVertical();

            // --- right: weights + detail
            GUILayout.BeginVertical();
            GUILayout.Label(L("Score weights (0 = ignore)", "น้ำหนักคะแนน (0 = ไม่นับ)"));
            for (int i = 0; i < wts.Length; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(th ? WtTh[i] : WtEn[i], GUILayout.Width(70));
                float nv = Mathf.Round(GUILayout.HorizontalSlider(wts[i], 0f, 5f, GUILayout.Width(120)) * 2f) / 2f;
                GUILayout.Label(nv.ToString("0.#"), GUILayout.Width(30));
                if (!Mathf.Approximately(nv, wts[i])) { wts[i] = nv; finderDirty = true; }
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L("Hire", "จ้างงาน"), GUILayout.Width(75))) SetPreset(1, 1, 1, 1, 1, 0, 0);
            if (GUILayout.Button(L("Business", "ค้าขาย"), GUILayout.Width(75))) SetPreset(0, 0, 3, 0, 1, 0, 0);
            if (GUILayout.Button(L("Scholar", "บัณฑิต"), GUILayout.Width(75))) SetPreset(3, 0, 0, 1, 2, 0, 0);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L("Warrior", "นักรบ"), GUILayout.Width(75))) SetPreset(0, 3, 0, 0, 1, 0, 0);
            if (GUILayout.Button(L("Marriage", "แต่งงาน"), GUILayout.Width(75))) SetPreset(1, 1, 1, 1, 1, 3, 1);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            Person cur = null;
            foreach (var p in view) if (p.Key == selKey) { cur = p; break; }
            if (cur == null)
            {
                GUILayout.Label(L("Click a name to see details", "คลิกชื่อเพื่อดูรายละเอียด"));
            }
            else
            {
                GUILayout.Label(cur.Name + "  (" + (cur.Female ? L("F", "หญิง") : L("M", "ชาย")) + ", " + L("age ", "อายุ ") + cur.Age.ToString("0") + ")");
                string src = SrcName(SrcIndex(cur));
                GUILayout.Label(src + (string.IsNullOrEmpty(cur.ClanLabel) ? "" : " - " + cur.ClanLabel) + "  |  " + ClassName(cur));
                GUILayout.Label(L("Writing ", "อักษร ") + cur.W.ToString("0.0") + "   " + L("Might ", "ยุทธ์ ") + cur.M.ToString("0.0"));
                GUILayout.Label(L("Business ", "การค้า ") + cur.B.ToString("0.0") + "   " + L("Arts ", "ศิลป์ ") + cur.A.ToString("0.0"));
                GUILayout.Label(L("Cunning ", "กลยุทธ์ ") + cur.C.ToString("0.0") + "   " + L("Charisma ", "เสน่ห์ ") + cur.Charisma.ToString("0.0"));
                GUILayout.Label(L("Luck ", "โชค ") + cur.Luck.ToString("0") + "   " + L("Mood ", "อารมณ์ ") + cur.Mood.ToString("0") + "   " + L("Renown ", "ชื่อเสียง ") + cur.Renown.ToString("0"));
                GUILayout.Label(L("Score: ", "คะแนน: ") + cur.Score.ToString("0.0"));
            }
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        static int SrcIndex(Person p)
        {
            switch (p.Src)
            {
                case "city": return 0;
                case "scholar": return 1;
                case "clanman": return 2;
                case "clanwoman": return 3;
                default: return 4;
            }
        }

        string SrcName(int i) { return th ? SrcTh[i] : SrcEn[i]; }

        // ========================================================== names
        static readonly char[] BadChars = { '|', '@', '"', '\\', '~' };

        static bool NameOk(string s)
        {
            return !string.IsNullOrEmpty(s) && s.Trim().Length > 0 && s.IndexOfAny(BadChars) < 0;
        }

        static IEnumerable<List<List<string>>> LineageLists()
        {
            if (Mainload.Member_now != null) yield return Mainload.Member_now;
            if (Mainload.Member_qu != null) yield return Mainload.Member_qu;
            if (Mainload.Member_Ci != null) yield return Mainload.Member_Ci;
        }

        static int ReplaceIn(List<List<string>> list, string oldS, string newS)
        {
            int n = 0;
            foreach (var row in list)
            {
                if (row == null) continue;
                for (int i = 0; i < row.Count; i++)
                    if (row[i] != null && row[i].Contains(oldS)) { row[i] = row[i].Replace(oldS, newS); n++; }
            }
            return n;
        }

        static int ReplaceLineage(string oldS, string newS, bool includeRetainers)
        {
            int n = 0;
            foreach (var l in LineageLists()) n += ReplaceIn(l, oldS, newS);
            if (includeRetainers && Mainload.MenKe_Now != null) n += ReplaceIn(Mainload.MenKe_Now, oldS, newS);
            var mf = Mainload.Member_First;
            if (mf != null)
                for (int i = 0; i < mf.Count; i++)
                    if (mf[i] != null && mf[i].Contains(oldS)) { mf[i] = mf[i].Replace(oldS, newS); n++; }
            return n;
        }

        /// <summary>เปลี่ยนชื่อสมาชิก (ชื่อ + นามสกุลทั้งชื่อ) และชื่อที่ถูกคัดลอกไปเก็บไว้ที่อื่น</summary>
        string RenameMember(Group g, List<string> m, string newName)
        {
            newName = newName.Trim();
            if (!NameOk(newName)) return L("Invalid name (no | @ \" \\ ~)", "ชื่อไม่ถูกต้อง (ห้ามมี | @ \" \\ ~)");
            string old = Name(g, m);
            if (old == newName) return L("Name unchanged", "ชื่อเหมือนเดิม");
            var blk = m[g.NameIdx].Split('|');
            blk[0] = newName;
            m[g.NameIdx] = string.Join("|", blk);
            int n = 0;
            if (old.Length >= 3) n = ReplaceLineage(old, newName, g.Name == "Retainer");
            return old + " -> " + newName + "  (" + n + L(" copies updated", " จุดที่อัปเดตตาม") + ")";
        }

        string RenameFamily(string newFam)
        {
            newFam = newFam.Trim();
            if (!NameOk(newFam)) return L("Invalid name (no | @ \" \\ ~)", "ชื่อไม่ถูกต้อง (ห้ามมี | @ \" \\ ~)");
            string old = Mainload.FamilyData[1];
            if (old == newFam) return L("Name unchanged", "ชื่อเหมือนเดิม");
            Mainload.FamilyData[1] = newFam;
            int n = old.Length >= 2 ? ReplaceLineage(old, newFam, false) : 0;
            return old + " -> " + newFam + "  (" + n + L(" copies updated", " จุดที่อัปเดตตาม") + ")";
        }

        Entry FounderEntry()
        {
            var mf = Mainload.Member_First;
            if (mf == null || mf.Count == 0 || Mainload.Member_now == null) return null;
            foreach (var m in Mainload.Member_now)
                if (m != null && m.Count > Groups[0].Cunning && m[0] == mf[0]) return new Entry { G = Groups[0], M = m };
            return null;
        }
    }
}
