using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PKTrainer
{
    // ==========================================================================
    //  แท็บ "ของ": เสกไอเทมเข้าคลังตระกูล (Mainload.Prop_have = [id, จำนวน])
    //  พื้นที่คลังที่เหลืออยู่คือ FamilyData[5] (เกมหักตัวนี้ทุกครั้งที่ของเข้าคลัง) จึงหักตามจำนวนที่เสก
    //  รายการ: Mainload.AllPropdata[id] = [ราคา, หมวด, ...]   ชื่อ: AllText.Text_AllProp[id][ภาษา]   หมวด: AllText.Text_AllPropClass[หมวด]
    // ==========================================================================
    public partial class Plugin
    {
        int iCat = -1;
        string iSearch = "", iAmt = "100", iCoin = "1000000", iGold = "1000";
        Vector2 iScroll;
        readonly Dictionary<int, bool> prefabOk = new Dictionary<int, bool>();
        string itemMsg = "";

        static int Lang() { try { return Mainload.SetData != null ? int.Parse(Mainload.SetData[4].ToString()) : 1; } catch { return 1; } }

        static string PropName(int id)
        {
            try { var n = AllText.Text_AllProp[id]; int l = Lang(); return n[Math.Min(l, n.Count - 1)]; } catch { return "#" + id; }
        }

        static string ClassName(int c)
        {
            try { var n = AllText.Text_AllPropClass[c]; int l = Lang(); return n[Math.Min(l, n.Count - 1)]; } catch { return "#" + c; }
        }

        // ไอเทมที่ไม่มีไอคอนในเกมอาจทำให้หน้าคลังพัง จึงแสดงเฉพาะที่มีไฟล์ไอคอน
        bool HasPrefab(int id)
        {
            bool ok;
            if (prefabOk.TryGetValue(id, out ok)) return ok;
            try { ok = Resources.Load("AllProp/" + id) != null; } catch { ok = false; }
            prefabOk[id] = ok;
            return ok;
        }

        // สรรพคุณ: ประกอบข้อความด้วยแม่แบบของเกมเอง (เหมือนหน้าข้อมูลไอเทมในเกม)
        readonly Dictionary<int, string> effCache = new Dictionary<int, string>();

        string PropEffect(int id)
        {
            string r;
            if (effCache.TryGetValue(id, out r)) return r;
            var sb = new System.Text.StringBuilder();
            try
            {
                string[] a = Mainload.AllPropdata[id][2].Split('|');
                int l = Mainload.SetData[4];
                Func<int, int> v = i => { int x; return i < a.Length && int.TryParse(a[i], out x) ? x : 0; };
                for (int i = 0; i < 4; i++) if (v(i) > 0) sb.Append(AllText.Text_UIA[1123 + i][l].Replace("@", v(i).ToString())).Append("  ");
                for (int i = 4; i < 8; i++)
                {
                    int x = v(i);
                    if (x != 0) sb.Append(AllText.Text_UIA[1123 + i][l].Replace("@", x > 0 ? "+" + x : x.ToString())).Append("  ");
                }
                if (v(8) > 0) sb.Append(AllText.Text_UIA[1131][l]).Append("  ");
                if (v(9) > 0) sb.Append(AllText.Text_UIA[1075][l].Replace("@", "+" + v(9))).Append("  ");
                for (int i = 10; i < 16; i++)
                    if (v(i) > 0) sb.Append(AllText.Text_AllMemberSkill[i - 9][l]).Append(" +").Append(v(i)).Append("  ");
            }
            catch { }
            r = sb.ToString().Replace("\n", " ").Trim();
            effCache[id] = r;
            return r;
        }

        int Owned(int id)
        {
            foreach (var h in Mainload.Prop_have)
                if (h[0] == id.ToString()) { int n; return int.TryParse(h[1], out n) ? n : 0; }
            return 0;
        }

        long UsedSpace()
        {
            long t = 0;
            foreach (var h in Mainload.Prop_have) { int n; if (int.TryParse(h[1], out n)) t += n; }
            return t;
        }

        void AddProp(int id, long n)
        {
            try
            {
                long free = long.Parse(Mainload.FamilyData[5]);
                if (n > free) n = free;
                if (n <= 0) { itemMsg = L("Storehouse is full - raise the capacity below.", "คลังเต็ม - เพิ่มความจุคลังด้านล่างก่อน"); return; }
                bool found = false;
                foreach (var h in Mainload.Prop_have)
                    if (h[0] == id.ToString()) { h[1] = (long.Parse(h[1]) + n).ToString(); found = true; break; }
                if (!found) Mainload.Prop_have.Add(new List<string> { id.ToString(), n.ToString() });
                Mainload.FamilyData[5] = (free - n).ToString();
                itemMsg = PropName(id) + " +" + n.ToString("N0");
                status = itemMsg;
            }
            catch (Exception e) { Logger.LogError(e); itemMsg = "Error: " + e.Message; }
        }

        int iPage;
        float catConfirmUntil;
        Vector2 invScroll;

        void AddCategory(int cat, long amt)
        {
            int n = 0;
            for (int id = 0; id < Mainload.AllPropdata.Count; id++)
            {
                int c; if (!int.TryParse(Mainload.AllPropdata[id][1], out c) || c != cat || !HasPrefab(id)) continue;
                long free = long.Parse(Mainload.FamilyData[5]);
                if (free <= 0) break;
                AddProp(id, amt);
                n++;
            }
            itemMsg = L("Added ", "เสกแล้ว ") + n + L(" kinds from ", " ชนิด จากหมวด ") + ClassName(cat);
            status = itemMsg;
        }

        // ตั้งจำนวนของในคลัง: ปรับพื้นที่ว่างให้สัมพันธ์กัน (ลดของ = พื้นที่ว่างเพิ่ม)
        void SetPropCount(List<string> h, long newN)
        {
            try
            {
                long old = long.Parse(h[1]), free = long.Parse(Mainload.FamilyData[5]);
                if (newN < 0) newN = 0;
                long delta = newN - old;
                if (delta > free) { delta = free; newN = old + free; }
                if (newN <= 0) Mainload.Prop_have.Remove(h); else h[1] = newN.ToString();
                Mainload.FamilyData[5] = (free - delta).ToString();
                itemMsg = PropName(int.Parse(h[0])) + " -> " + newN.ToString("N0");
                status = itemMsg;
            }
            catch (Exception e) { Logger.LogError(e); itemMsg = "Error: " + e.Message; }
        }

        void DrawInventory()
        {
            GUILayout.Space(6);
            long free = 0; long.TryParse(Mainload.FamilyData[5], out free);
            GUILayout.Label(L("Storehouse: ", "คลังตระกูล: ") + UsedSpace().ToString("N0") + " / " + (UsedSpace() + free).ToString("N0")
                            + "     " + L("free ", "ว่าง ") + free.ToString("N0"));
            GUILayout.BeginHorizontal();
            GUILayout.Label(L("Search:", "ค้นหา:"), GUILayout.Width(60));
            iSearch = GUILayout.TextField(iSearch, GUILayout.Width(150));
            GUILayout.Label(L("Amount:", "จำนวน:"), GUILayout.Width(70));
            iAmt = GUILayout.TextField(iAmt, GUILayout.Width(90));
            GUILayout.EndHorizontal();
            long amt;
            bool ok = long.TryParse(iAmt.Replace(",", "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out amt) && amt >= 0;
            string flt = iSearch.Trim().ToLowerInvariant();

            invScroll = GUILayout.BeginScrollView(invScroll, GUILayout.Height(390));
            var snapshot = new List<List<string>>(Mainload.Prop_have);
            int shown = 0;
            foreach (var h in snapshot)
            {
                int id; if (!int.TryParse(h[0], out id)) continue;
                string nm = PropName(id);
                if (flt.Length > 0 && nm.ToLowerInvariant().IndexOf(flt, StringComparison.Ordinal) < 0) continue;
                shown++;
                GUILayout.BeginHorizontal();
                ItemIcon(id, 48);
                GUILayout.BeginVertical(GUILayout.Width(260));
                GUILayout.Label(nm + "  x" + h[1]);
                string eff = PropEffect(id);
                GUILayout.Label(eff.Length > 0 ? eff : L("(no special effect)", "(ไม่มีสรรพคุณพิเศษ)"), stCap);
                GUILayout.EndVertical();
                if (GUILayout.Button(L("Set to ", "ตั้งเป็น ") + amt.ToString("N0"), GUILayout.Width(120), GUILayout.Height(36)) && ok) SetPropCount(h, amt);
                if (GUILayout.Button("- " + amt.ToString("N0"), GUILayout.Width(90), GUILayout.Height(36)) && ok) SetPropCount(h, long.Parse(h[1]) - amt);
                if (GUILayout.Button("+ " + amt.ToString("N0"), GUILayout.Width(90), GUILayout.Height(36)) && ok) SetPropCount(h, long.Parse(h[1]) + amt);
                if (GUILayout.Button(L("Remove", "ลบทิ้ง"), GUILayout.Width(80), GUILayout.Height(36))) SetPropCount(h, 0);
                GUILayout.EndHorizontal();
            }
            if (shown == 0) GUILayout.Label(L("Nothing here.", "ไม่มีของ"));
            GUILayout.EndScrollView();
            invScroll = WheelScroll(invScroll);
            GUILayout.Label(itemMsg, stCap);
        }

        void DrawItems()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(iPage == 0, L("Items", "ไอเทม"), "Button", GUILayout.Width(140))) iPage = 0;
            if (GUILayout.Toggle(iPage == 1, L("Horses", "ม้า"), "Button", GUILayout.Width(140))) iPage = 1;
            if (GUILayout.Toggle(iPage == 2, L("My storehouse", "คลังของฉัน"), "Button", GUILayout.Width(160))) iPage = 2;
            GUILayout.EndHorizontal();
            if (iPage == 1) { DrawHorses(); return; }
            if (iPage == 2) { DrawInventory(); return; }
            GUILayout.Space(6);
            Row(L("Coin", "เหรียญ"), Coins().ToString("N0"), ref iCoin, v => SetCoins(v), v => SetCoins(Coins() + v));
            Row(L("Gold", "ทอง"), Mainload.CGNum[1], ref iGold,
                v => Mainload.CGNum[1] = Math.Max(0, Math.Min(2000000000L, v)).ToString(),
                v => Mainload.CGNum[1] = Math.Max(0, Math.Min(2000000000L, long.Parse(Mainload.CGNum[1]) + v)).ToString());
            GUILayout.Space(6);
            long free = 0; long.TryParse(Mainload.FamilyData[5], out free);
            long used = UsedSpace();
            GUILayout.Label(L("Storehouse: ", "คลังตระกูล: ") + used.ToString("N0") + " / " + (used + free).ToString("N0")
                            + "     " + L("free ", "ว่าง ") + free.ToString("N0"));

            // หมวด
            var cats = new List<int>();
            var seen = new HashSet<int>();
            for (int i = 0; i < Mainload.AllPropdata.Count; i++)
            {
                int c; if (int.TryParse(Mainload.AllPropdata[i][1], out c) && seen.Add(c)) cats.Add(c);
            }
            cats.Sort();
            var names = new string[cats.Count + 1];
            names[0] = L("All", "ทั้งหมด");
            for (int i = 0; i < cats.Count; i++) names[i + 1] = ClassName(cats[i]);
            int cur = iCat < 0 ? 0 : cats.IndexOf(iCat) + 1;
            int pick = GUILayout.SelectionGrid(cur, names, 4, GUILayout.Width(740));
            iCat = pick <= 0 ? -1 : cats[pick - 1];

            GUILayout.BeginHorizontal();
            GUILayout.Label(L("Search:", "ค้นหา:"), GUILayout.Width(60));
            iSearch = GUILayout.TextField(iSearch, GUILayout.Width(150));
            GUILayout.Label(L("Amount:", "จำนวน:"), GUILayout.Width(70));
            iAmt = GUILayout.TextField(iAmt, GUILayout.Width(80));
            foreach (var q in new[] { 1, 10, 100, 1000, 10000 })
                if (GUILayout.Button(q.ToString("N0"), GUILayout.Width(54))) iAmt = q.ToString();
            GUILayout.EndHorizontal();

            long amt;
            bool ok = long.TryParse(iAmt.Replace(",", "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out amt) && amt > 0;
            string flt = iSearch.Trim().ToLowerInvariant();

            iScroll = GUILayout.BeginScrollView(iScroll, GUILayout.Height(330));
            int shown = 0;
            for (int id = 0; id < Mainload.AllPropdata.Count && shown < 200; id++)
            {
                int c; if (!int.TryParse(Mainload.AllPropdata[id][1], out c)) continue;
                if (iCat >= 0 && c != iCat) continue;
                string nm = PropName(id);
                if (flt.Length > 0 && nm.ToLowerInvariant().IndexOf(flt, StringComparison.Ordinal) < 0 && ("#" + id) != flt) continue;
                if (!HasPrefab(id)) continue;
                shown++;
                GUILayout.BeginHorizontal();
                ItemIcon(id, 52);
                GUILayout.BeginVertical(GUILayout.Width(360));
                GUILayout.Label(nm + "  (#" + id + ")");
                string eff = PropEffect(id);
                GUILayout.Label(eff.Length > 0 ? eff : L("(no special effect)", "(ไม่มีสรรพคุณพิเศษ)"), stCap);
                GUILayout.EndVertical();
                GUILayout.Label(L("price ", "ราคา ") + Mainload.AllPropdata[id][0] + "\n" + L("have ", "มี ") + Owned(id).ToString("N0"), GUILayout.Width(110));
                if (GUILayout.Button(L("+ Add", "+ เสก"), GUILayout.Width(90), GUILayout.Height(40)) && ok) AddProp(id, amt);
                GUILayout.EndHorizontal();
                GUILayout.Space(2);
            }
            if (shown == 0) GUILayout.Label(L("No items match.", "ไม่พบไอเทม"));
            GUILayout.EndScrollView();
            iScroll = WheelScroll(iScroll);

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            bool armedCat = Time.unscaledTime < catConfirmUntil;
            GUI.enabled = iCat >= 0 && ok;
            if (GUILayout.Button(armedCat ? L("Click again to CONFIRM", "กดอีกครั้งเพื่อยืนยัน")
                                          : L("Add the amount of EVERY item in this category", "เสกทุกชิ้นในหมวดที่เลือก (ตามจำนวน)"), GUILayout.Width(380)))
            {
                if (armedCat) { catConfirmUntil = 0; AddCategory(iCat, amt); }
                else catConfirmUntil = Time.unscaledTime + 6f;
            }
            GUI.enabled = true;
            if (GUILayout.Button(L("Raise capacity by the amount", "เพิ่มความจุคลังตามจำนวน"), GUILayout.Width(300)) && ok)
            {
                Mainload.FamilyData[5] = (free + amt).ToString();
                itemMsg = L("Storehouse capacity +", "ความจุคลัง +") + amt.ToString("N0");
                status = itemMsg;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(itemMsg);
        }
    }
}
