using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PKTrainer
{
    // ==========================================================================
    //  แท็บ "แต่งงาน": (1) สั่งแต่งงาน  (2) เพิ่มความสัมพันธ์กับตระกูล/บุคคล
    //  แต่งงาน: เรียก FormulaData.MemberMarry_act ของเกมเอง (ตัวเดียวกับตอนสู่ขอสำเร็จ)
    //    host = คนที่อยู่ในตระกูลต่อ, joiner = คนที่ย้ายเข้าไปอยู่
    //    โหมด 0: คนของเราเป็น host (อีกฝ่ายย้ายเข้าตระกูลเรา)  -> MemberMarry_act(-1, เรา, ตระกูลเขา, เขา, เอก/รอง)
    //    โหมด 1: คนของเราเป็น joiner (ไปอยู่ตระกูลเขา)         -> MemberMarry_act(ตระกูลเขา, เขา, -1, เรา, เอก/รอง)
    //  เอก = Member_Qu_Nv 0, รอง = 1   sex: "1" = ชาย, "0" = หญิง   ShijiaIndex: -1 เรา, -2 ราชสำนัก, >=0 ตระกูลอื่น
    // ==========================================================================
    public partial class Plugin
    {
        int mPage, mHost;                 // mHost: 0 = คนของเราเป็น host, 1 = คนของเราไปอยู่ตระกูลเขา
        int mSel = -1, mSrc = -3, mTar = -1;
        Vector2 mScrA, mScrB, mScrC;
                string mMsg = "", relAmt = "10", mFilter = "";

        static bool Married(string st) { return st == "1" || st == "3"; }
        string SexTxt(string s) { return s == "1" ? L("M", "ช") : L("F", "ญ"); }

        // แหล่งสามัญชน: -10 = ชาวบ้านทั่วแคว้น (Member_Hanmen), -11 = ชาวบ้านที่เดินอยู่ในเมืองนี้ (HanMen_City), -12 = คนที่แม่สื่อแนะนำ
        static bool IsCommoner(int src) { return src <= -10; }

        static List<List<string>> CommonerList(int src)
        {
            switch (src)
            {
                case -10: return Mainload.Member_Hanmen;
                case -11: return Mainload.HanMen_City;
                case -12: return Mainload.HanMenData_ShuoMei;
            }
            return null;
        }

        string SrcName(int src)
        {
            if (src == -10) return L("Commoners (world)", "สามัญชน (ทั่วแคว้น)");
            if (src == -11) return L("Commoners in this city", "สามัญชนในเมืองนี้");
            if (src == -12) return L("Matchmaker's list", "คนที่แม่สื่อแนะนำ");
            if (src == -2) return L("Royal court", "ราชสำนัก");
            try { return Mainload.ShiJia_Now[src][1]; } catch { return "#" + src; }
        }

        // ===== กฎการสมรสของเกม (สรุปจาก Act_MemberOtherOrQuA/B และ FormuAct.Ti_Qin) =====
        //  - ต้องเป็นผู้ใหญ่ (อายุ >= OldFenjie[1])
        //  - ฝั่งที่ "ย้ายไปอยู่บ้านอีกฝ่าย" ต้องโสด และ ห้ามมียศ/ตำแหน่ง (เกมให้โอกาส -9,000,000 = เป็นไปไม่ได้ และถ้าฝืนจะหลุดตำแหน่ง)
        //    ฝั่งเรา: ห้ามมียศขุนนาง ([12] = "5@...") และต้องว่าง ([15] = "0")
        //  - ฝั่งเจ้าบ้านต้องโสด หรือเป็นผู้ที่ยังรับคู่เพิ่มได้ (FormulaData.isCanNaQie)
        static bool HasRank(string field)
        {
            try { return field != null && field.Split('|')[0].Split('@')[0] == "5"; } catch { return false; }
        }

        static bool IsAdult(string age)
        {
            int a;
            return int.TryParse(age, out a) && a >= Mainload.OldFenjie[1];
        }

        // joiner = คนที่ย้ายไปอยู่บ้านอีกฝ่าย ; host = คนที่อยู่บ้านเดิม
        bool OursOK(int k, bool asHost, out string why)
        {
            why = null;
            var m = Mainload.Member_now[k];
            if (!IsAdult(m[6])) { why = L("not an adult", "ยังไม่โตพอ"); return false; }
            if (m[15] != "0" && !(asHost && m[15] == "16")) { why = L("busy / not available", "ติดภารกิจ/ไม่ว่าง"); return false; }
            if (asHost) { if (Married(m[26]) && !FormulaData.isCanNaQie(-1, k)) { why = L("cannot take more spouses", "รับคู่เพิ่มไม่ได้"); return false; } return true; }
            if (Married(m[26])) { why = L("already married", "แต่งแล้ว"); return false; }
            if (HasRank(m[12])) { why = L("holds an official rank", "มียศขุนนาง"); return false; }
            return true;
        }

        bool TargetOK(int src, int i, bool targetIsHost, out string why)
        {
            why = null;
            var t = TargetData(src, i);
            string nm, sx, ag, rl; bool mar;
            if (t == null || !TargetInfo(src, i, out nm, out sx, out ag, out mar, out rl)) { why = "?"; return false; }
            if (!IsAdult(ag)) { why = L("not an adult", "ยังไม่โตพอ"); return false; }
            if (targetIsHost)
            {
                if (mar)
                {
                    if (IsCommoner(src)) { why = L("already married", "แต่งแล้ว"); return false; }
                    bool can = src == -2 || FormulaData.isCanNaQie(src, i);
                    if (!can) { why = L("cannot take more spouses", "รับคู่เพิ่มไม่ได้"); return false; }
                }
                return true;
            }
            // เป้าหมายย้ายเข้าตระกูลเรา
            if (mar) { why = L("already married", "แต่งแล้ว"); return false; }
            string rank = src == -2 ? t[9] : (t.Count > 9 ? t[9] : null);
            if (HasRank(rank)) { why = L("holds an official rank (would lose it)", "มียศขุนนาง (จะหลุดตำแหน่ง)"); return false; }
            if (src == -2) { if (t.Count > 10 && t[10] != "0|0") { why = L("holds a noble title", "มีบรรดาศักดิ์"); return false; } }
            else if (!IsCommoner(src) && t.Count > 11 && t[11] != "0|0") { why = L("holds a noble title (would lose it)", "มีบรรดาศักดิ์ (จะหลุดตำแหน่ง)"); return false; }
            return true;
        }

        bool TargetInfo(int src, int i, out string name, out string sex, out string age, out bool married, out string rel)
        {
            name = sex = age = rel = ""; married = false;
            try
            {
                if (IsCommoner(src))
                {
                    var t = CommonerList(src)[i];
                    var p = t[2].Split('|');
                    name = p[0]; sex = p[4]; age = t[3]; married = Married(t[21]); rel = "";
                }
                else if (src == -2)
                {
                    var t = Mainload.Member_King[i];
                    var p = t[2].Split('|');
                    name = p[0]; sex = p[4]; age = t[3]; married = t[20] != "0"; rel = t[14];
                }
                else
                {
                    var t = Mainload.Member_other[src][i];
                    var p = t[2].Split('|');
                    name = p[0]; sex = p[4]; age = t[3]; married = Married(t[21]); rel = t[15];
                }
                return true;
            }
            catch { return false; }
        }

        List<string> TargetData(int src, int i)
        {
            try
            {
                if (IsCommoner(src)) return CommonerList(src)[i];
                if (src == -2) return Mainload.Member_King[i];
                return Mainload.Member_other[src][i];
            }
            catch { return null; }
        }

        int TargetCount(int src)
        {
            if (IsCommoner(src)) { var l = CommonerList(src); return l != null ? l.Count : 0; }
            if (src == -2) return Mainload.Member_King != null ? Mainload.Member_King.Count : 0;
            if (src >= 0 && Mainload.Member_other != null && src < Mainload.Member_other.Count) return Mainload.Member_other[src].Count;
            return 0;
        }

        void DrawMarriage()
        {
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(mPage == 0, L("Marriage", "สั่งแต่งงาน"), "Button", GUILayout.Width(150))) mPage = 0;
            if (GUILayout.Toggle(mPage == 1, L("Relationship", "เพิ่มความสัมพันธ์"), "Button", GUILayout.Width(170))) mPage = 1;
            GUILayout.EndHorizontal();
            if (mPage == 0) DrawMarryPage(); else DrawRelPage();
        }

        // ---- คอลัมน์ตระกูล/ราชสำนัก (ใช้ร่วมสองหน้า)
        void DrawSourceColumn(int width, int height)
        {
            GUILayout.BeginVertical(GUILayout.Width(width));
            GUILayout.Label(L("Family / court", "ตระกูล/ราชสำนัก"));
            mScrB = GUILayout.BeginScrollView(mScrB, GUILayout.Height(height));
            if (GUILayout.Toggle(mSrc == -2, SrcName(-2), "Button") && mSrc != -2) { mSrc = -2; mTar = -1; }
            if (mPage == 0)
                for (int c = -10; c >= -12; c--)
                    if (GUILayout.Toggle(mSrc == c, SrcName(c), "Button") && mSrc != c) { mSrc = c; mTar = -1; mHost = 0; }
            if (Mainload.ShiJia_Now != null)
                for (int s = 0; s < Mainload.ShiJia_Now.Count; s++)
                    if (GUILayout.Toggle(mSrc == s, SrcName(s), "Button") && mSrc != s) { mSrc = s; mTar = -1; }
            GUILayout.EndScrollView();
            mScrB = WheelScroll(mScrB);
            GUILayout.EndVertical();
        }

        void DrawMarryPage()
        {
            var now = Mainload.Member_now;
            GUILayout.Label(L("Marry one of your people to someone from another family or the royal court (ignores the game's request rules).",
                              "ให้คนในตระกูลเราแต่งงานกับคนจากตระกูลอื่น/ราชสำนัก (ข้ามเงื่อนไขการสู่ขอของเกม)"));
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(mHost == 0, L("They join MY family", "เขาย้ายเข้าตระกูลเรา (รับเข้าเรือน)"), "Button", GUILayout.Width(330)) && mHost != 0) { mHost = 0; mTar = -1; }
            if (GUILayout.Toggle(mHost == 1, L("I join THEIR family", "คนของเราไปอยู่ตระกูลเขา (ออกเรือน)"), "Button", GUILayout.Width(330)) && mHost != 1 && !IsCommoner(mSrc)) { mHost = 1; mTar = -1; }
            GUILayout.EndHorizontal();
            GUILayout.Label(L("Only people the game allows are listed: adults; the one who moves house must be single and hold no rank or title.",
                              "แสดงเฉพาะคนที่เกมอนุญาต: ต้องเป็นผู้ใหญ่ และฝั่งที่ย้ายบ้านต้องโสด ไม่มียศ/บรรดาศักดิ์ (ไม่งั้นจะหลุดตำแหน่ง)"), stCap);
            bool oursHost = mHost == 0;
            int hiddenOurs = 0, hiddenTar = 0;

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(270));
            GUILayout.Label(L("1) Your family", "1) คนในตระกูลเรา"));
            mScrA = GUILayout.BeginScrollView(mScrA, GUILayout.Height(340));
            for (int k = 0; k < now.Count; k++)
            {
                var m = now[k];
                if (m.Count < 40) continue;
                string why;
                if (!OursOK(k, oursHost, out why)) { hiddenOurs++; continue; }
                string label = m[4].Split('|')[0] + "  (" + SexTxt(m[4].Split('|')[4]) + ", " + m[6] + (Married(m[26]) ? L(", married", ", แต่งแล้ว") : "") + ")";
                GUILayout.BeginHorizontal();
                Portrait(LookOf(Groups[0], m), 30, 45);
                if (GUILayout.Toggle(mSel == k, label, "Button", GUILayout.Height(45)) && mSel != k) { mSel = k; mTar = -1; }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            if (hiddenOurs > 0) GUILayout.Label(L("hidden by game rules: ", "ซ่อนเพราะติดกฎเกม: ") + hiddenOurs, stCap);
            mScrA = WheelScroll(mScrA);
            GUILayout.EndVertical();

            DrawSourceColumn(170, 340);

            GUILayout.BeginVertical(GUILayout.Width(280));
            GUILayout.BeginHorizontal();
            GUILayout.Label(L("3) Spouse", "3) คู่สมรส"), GUILayout.Width(90));
            mFilter = GUILayout.TextField(mFilter, GUILayout.Width(150));
            GUILayout.EndHorizontal();
            mScrC = GUILayout.BeginScrollView(mScrC, GUILayout.Height(315));
            string ourSex = mSel >= 0 && mSel < now.Count ? now[mSel][4].Split('|')[4] : null;
            if (ourSex != null && mSrc != -3)
            {
                int n = TargetCount(mSrc), shown = 0;
                string flt = mFilter.Trim().ToLowerInvariant();
                for (int i = 0; i < n && shown < 250; i++)
                {
                    string nm, sx, ag, rl; bool mar;
                    if (!TargetInfo(mSrc, i, out nm, out sx, out ag, out mar, out rl)) continue;
                    if (sx == ourSex) continue;
                    if (flt.Length > 0 && nm.ToLowerInvariant().IndexOf(flt, StringComparison.Ordinal) < 0) continue;
                    string why;
                    if (!TargetOK(mSrc, i, !oursHost, out why)) { hiddenTar++; continue; }
                    shown++;
                    string label = nm + "  (" + SexTxt(sx) + ", " + ag + (mar ? L(", married", ", แต่งแล้ว") : "") + ")";
                    GUILayout.BeginHorizontal();
                    var td = TargetData(mSrc, i);
                    Portrait(td != null ? LookOfOther(td, mSrc == -2) : new Look(), 30, 45);
                    if (GUILayout.Toggle(mTar == i, label, "Button", GUILayout.Height(45)) && mTar != i) mTar = i;
                    GUILayout.EndHorizontal();
                }
            }
            else GUILayout.Label(L("Pick your person and a family first", "เลือกคนของเราและตระกูลก่อน"));
            GUILayout.EndScrollView();
            if (hiddenTar > 0) GUILayout.Label(L("hidden by game rules: ", "ซ่อนเพราะติดกฎเกม: ") + hiddenTar, stCap);
            mScrC = WheelScroll(mScrC);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            string tn = "", ts = "", ta = "", tr = ""; bool tm = false;
            bool ready = mSel >= 0 && mSel < now.Count && mSrc != -3 && mTar >= 0 && TargetInfo(mSrc, mTar, out tn, out ts, out ta, out tm, out tr);
            if (ready)
            {
                string on = now[mSel][4].Split('|')[0];
                bool oursMale = now[mSel][4].Split('|')[4] == "1";
                bool hostMarried = oursHost ? Married(now[mSel][26]) : tm;
                GUILayout.Label(on + "  +  " + tn + "  (" + SrcName(mSrc) + ")");
                string pre;
                if (IsCommoner(mSrc)) pre = L("Take commoner into the family", "รับสามัญชนเข้าตระกูล");
                else if (oursHost) pre = oursMale ? L("Take in as wife", "รับเข้าตระกูล") : L("Husband joins our house", "บุรุษแต่งเข้าเรือน");
                else if (mSrc == -2) pre = L("Marry into the court", "อภิเษก");
                else pre = oursMale ? L("Join their house", "แต่งเข้าตระกูลเขา") : L("Marry out", "ออกเรือน");
                GUILayout.BeginHorizontal();
                if (IsCommoner(mSrc))
                {
                    if (GUILayout.Button(pre + (hostMarried ? L(" (secondary - you already have a spouse)", " (เป็นรอง เพราะมีคู่สมรสแล้ว)") : L(" (principal)", " (เอก)")),
                                         GUILayout.Height(34), GUILayout.Width(460)))
                        DoCommoner(mSel, mSrc, mTar);
                    GUILayout.EndHorizontal();
                    GUILayout.Label(L("Commoners can only join your family (the game has no 'marry out' for them).", "สามัญชนทำได้เฉพาะย้ายเข้าตระกูลเรา (เกมไม่มีให้ไปอยู่ตระกูลเขา)"));
                    GUILayout.Label(mMsg);
                    return;
                }
                GUI.enabled = !hostMarried;
                if (GUILayout.Button(pre + L(" - principal", " - เอก"), GUILayout.Height(34), GUILayout.Width(330))) DoMarry(mSel, mSrc, mTar, 0, oursHost);
                GUI.enabled = !(mSrc == -2 && !oursHost);          // ราชสำนัก: ออกเรือนได้เฉพาะเอก
                if (GUILayout.Button(pre + L(" - secondary", " - รอง"), GUILayout.Height(34), GUILayout.Width(330))) DoMarry(mSel, mSrc, mTar, 1, oursHost);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                if (hostMarried) GUILayout.Label(L("The host already has a spouse, so only 'secondary' is available.", "ฝั่งเจ้าบ้านมีคู่สมรสแล้ว จึงเลือกได้เฉพาะ 'รอง'"));
            }
            GUILayout.Label(mMsg);
        }

        void DoMarry(int ours, int src, int tar, int qu, bool oursHost)
        {
            try
            {
                string on = Mainload.Member_now[ours][4].Split('|')[0];
                string tn, ts, ta, tr; bool tm;
                TargetInfo(src, tar, out tn, out ts, out ta, out tm, out tr);
                if (oursHost)
                    FormulaData.MemberMarry_act(-1, ours, src, tar, qu, "null", 0, false);
                else if (src == -2)
                    FormulaData.MemberMarry_act(-2, tar, -1, ours, 0, FormulaData.GetShenfen_KingQuQin(tar, false), 0, false);
                else
                    FormulaData.MemberMarry_act(src, tar, -1, ours, qu, "null", 0, false);
                mMsg = L("Done: ", "สำเร็จ: ") + on + " + " + tn;
                status = mMsg;
                mSel = -1; mTar = -1;
            }
            catch (Exception e)
            {
                Logger.LogError(e);
                mMsg = "Error: " + e.Message;
            }
        }

        // สามัญชน: เกมใช้ FormuAct.TiQin_HanMen (ต้องทอยลุ้นตามของหมั้น) จึงส่ง "ของหมั้นปลอม" มูลค่ามหาศาลให้ทอยผ่านแน่นอน
        // แล้วคืนค่าที่ฟังก์ชันหักไว้ (พลังกาย, ตัวนับของหมั้น, วันเวลา) ของหมั้นปลอมเป็นไอเทมที่เราไม่มี จึงไม่หักของจริง
        void DoCommoner(int ours, int src, int tar)
        {
            try
            {
                var list = CommonerList(src);
                var data = list[tar];
                string on = Mainload.Member_now[ours][4].Split('|')[0], tn = data[2].Split('|')[0];

                int pid = -1, price = 0;
                for (int i = 0; i < Mainload.AllPropdata.Count; i++)
                {
                    int pr;
                    if (!int.TryParse(Mainload.AllPropdata[i][0], out pr) || pr <= price) continue;
                    bool owned = false;
                    foreach (var h in Mainload.Prop_have) if (h[0] == i.ToString()) { owned = true; break; }
                    if (!owned) { pid = i; price = pr; }
                }
                if (pid < 0 || price <= 0) { mMsg = L("No usable gift item found.", "ไม่พบไอเทมสำหรับของหมั้น"); return; }
                int cnt = (int)Math.Min(1000000L, 2000000L / price + 1);

                string stam = Mainload.Member_now[ours][30], fam5 = Mainload.FamilyData[5];
                bool day = Mainload.isTimeGoDay;
                int before = Mainload.Member_qu.Count;
                FormuAct.TiQin_HanMen(ours, new List<List<int>> { new List<int> { pid, cnt } }, data);
                Mainload.Member_now[ours][30] = stam;
                Mainload.FamilyData[5] = fam5;
                Mainload.isTimeGoDay = day;

                if (Mainload.Member_qu.Count > before)
                {
                    if (src == -11) Mainload.QMemberID_Remove.Add(data[0]);   // ให้ตัวละครที่เดินอยู่ในฉากหายไป
                    list.Remove(data);
                    mMsg = L("Done: ", "สำเร็จ: ") + on + " + " + tn;
                    status = mMsg;
                    mSel = -1; mTar = -1;
                }
                else mMsg = L("The game refused (no change).", "เกมไม่ยอมรับ (ไม่มีการเปลี่ยนแปลง)");
            }
            catch (Exception e) { Logger.LogError(e); mMsg = "Error: " + e.Message; }
        }

        // ================== เพิ่มความสัมพันธ์ ==================
        // ตระกูล: ShiJia_Now[i][3] (ราชสำนัก: ShiJia_king[2])   บุคคล: Member_other[i][j][15] (ราชสำนัก: Member_King[j][14])   ช่วง -100..100
        string FamRel(int src)
        {
            try { return src == -2 ? Mainload.ShiJia_king[2] : Mainload.ShiJia_Now[src][3]; } catch { return "?"; }
        }

        void AddRel(int src, int person, float amt, bool toMax)
        {
            try
            {
                if (person < 0)
                {
                    if (src == -2) Mainload.ShiJia_king[2] = toMax ? "100" : FormulaData.YiBaiNumChange(Mainload.ShiJia_king[2], amt.ToString(CultureInfo.InvariantCulture), true);
                    else Mainload.ShiJia_Now[src][3] = toMax ? "100" : FormulaData.YiBaiNumChange(Mainload.ShiJia_Now[src][3], amt.ToString(CultureInfo.InvariantCulture), true);
                }
                else if (src == -2)
                    Mainload.Member_King[person][14] = toMax ? "100" : FormulaData.YiBaiNumChange(Mainload.Member_King[person][14], amt.ToString(CultureInfo.InvariantCulture), true);
                else
                    Mainload.Member_other[src][person][15] = toMax ? "100" : FormulaData.YiBaiNumChange(Mainload.Member_other[src][person][15], amt.ToString(CultureInfo.InvariantCulture), true);
                mMsg = L("Relationship updated.", "เพิ่มความสัมพันธ์แล้ว");
                status = mMsg;
            }
            catch (Exception e) { Logger.LogError(e); mMsg = "Error: " + e.Message; }
        }

        void DrawRelPage()
        {
            GUILayout.Label(L("Raise the relationship with a whole family, or with one person in it (range -100..100).",
                              "เพิ่มความสัมพันธ์กับทั้งตระกูล หรือกับบุคคลคนเดียวในตระกูล (ช่วง -100 ถึง 100)"));
            if (IsCommoner(mSrc)) { mSrc = -3; mTar = -1; }
            GUILayout.BeginHorizontal();
            DrawSourceColumn(200, 340);

            GUILayout.BeginVertical(GUILayout.Width(440));
            GUILayout.Label(L("Person (optional - leave unselected to target the whole family)", "บุคคล (ไม่เลือก = เพิ่มให้ทั้งตระกูล)"));
            mScrC = GUILayout.BeginScrollView(mScrC, GUILayout.Height(340));
            if (mSrc != -3)
            {
                int n = TargetCount(mSrc);
                for (int i = 0; i < n; i++)
                {
                    string nm, sx, ag, rl; bool mar;
                    if (!TargetInfo(mSrc, i, out nm, out sx, out ag, out mar, out rl)) continue;
                    string label = nm + "  (" + SexTxt(sx) + ", " + ag + ")   " + L("rel ", "ความสัมพันธ์ ") + rl;
                    bool on = mTar == i;
                    if (GUILayout.Toggle(on, label, "Button")) { if (!on) mTar = i; }
                    else if (on) mTar = -1;                       // กดซ้ำเพื่อยกเลิกการเลือก
                }
            }
            else GUILayout.Label(L("Pick a family first", "เลือกตระกูลก่อน"));
            GUILayout.EndScrollView();
            mScrC = WheelScroll(mScrC);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            if (mSrc != -3)
            {
                string target = mTar >= 0 ? PersonName(mSrc, mTar) : L("whole family ", "ทั้งตระกูล ") + SrcName(mSrc);
                GUILayout.Label(L("Target: ", "เป้าหมาย: ") + target + "     " + L("family relation now: ", "ความสัมพันธ์ตระกูลตอนนี้: ") + FamRel(mSrc));
                GUILayout.BeginHorizontal();
                GUILayout.Label(L("Amount", "จำนวน"), GUILayout.Width(70));
                relAmt = GUILayout.TextField(relAmt, GUILayout.Width(80));
                float v;
                bool ok = float.TryParse(relAmt, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
                if (GUILayout.Button(L("+ Add", "+ เพิ่ม"), GUILayout.Width(100)) && ok) AddRel(mSrc, mTar, v, false);
                if (GUILayout.Button(L("- Sub", "- ลด"), GUILayout.Width(100)) && ok) AddRel(mSrc, mTar, -v, false);
                if (GUILayout.Button(L("Max (100)", "สูงสุด (100)"), GUILayout.Width(130))) AddRel(mSrc, mTar, 0, true);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(6);
            bool armedAll = Time.unscaledTime < relAllConfirm;
            if (GUILayout.Button(armedAll ? L("Click again to CONFIRM", "กดอีกครั้งเพื่อยืนยัน")
                                          : L("Max relationship with ALL families and the court", "ความสัมพันธ์ทุกตระกูลและราชสำนัก = สูงสุด"), GUILayout.Width(460), GUILayout.Height(30)))
            {
                if (armedAll)
                {
                    relAllConfirm = 0;
                    try
                    {
                        for (int s = 0; s < Mainload.ShiJia_Now.Count; s++) Mainload.ShiJia_Now[s][3] = "100";
                        Mainload.ShiJia_king[2] = "100";
                        mMsg = L("All family relations set to 100.", "ตั้งความสัมพันธ์ทุกตระกูลเป็น 100 แล้ว");
                        status = mMsg;
                    }
                    catch (Exception e) { Logger.LogError(e); mMsg = "Error: " + e.Message; }
                }
                else relAllConfirm = Time.unscaledTime + 6f;
            }
            GUILayout.Label(mMsg);
        }

        float relAllConfirm;

        string PersonName(int src, int i)
        {
            string nm, sx, ag, rl; bool mar;
            return TargetInfo(src, i, out nm, out sx, out ag, out mar, out rl) ? nm : "#" + i;
        }
    }
}
