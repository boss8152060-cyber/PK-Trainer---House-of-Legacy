using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PKTrainer
{
    // ==========================================================================
    //  ศูนย์ค้นหาคนเก่ง / จ้างที่ปรึกษา (門客 = MenKe_Now)
    //  - ค้นหาจากสามัญชน (ทั่วแคว้น / ในเมืองนี้ / แม่สื่อ) และสมาชิกตระกูลอื่น พร้อมคะแนนความเก่ง
    //  - จ้างเป็นที่ปรึกษาได้เฉพาะสามัญชน (เหมือนในเกม: เกมให้จ้างได้เฉพาะ HanMen) โดยคัดลอกฟิลด์แบบเดียวกับ MemberHanMenPanelC.QiangQinBT
    //  ฟิลด์ (สามัญชน/ตระกูลอื่น): [3]=อายุ [4..7]=อักษร,ยุทธ์,การค้า,ศิลป์  [22]=กลยุทธ์  [2]=ชื่อ|..|เพศ(4)|..
    // ==========================================================================
    public partial class Plugin
    {
        class Cand
        {
            public int Src;                 // -10 ทั่วแคว้น, -11 ในเมือง, -12 แม่สื่อ, >=0 ตระกูลอื่น
            public List<string> Data;
            public string Name, Sex;
            public int Age;
            public float[] Sk = new float[5];   // อักษร, ยุทธ์, การค้า, ศิลป์, กลยุทธ์
            public float Score;
            public int Salary;
            public string Family;
        }

        bool[] tSrc = { true, true, true, false };      // ทั่วแคว้น, ในเมือง, แม่สื่อ, ตระกูลอื่น
        int tSort, tSex;                                // tSort: 0 รวม 1-5 ทักษะ 6 อายุ ; tSex: 0 ทั้งหมด 1 ชาย 2 หญิง
        string tMinAge = "0", tMaxAge = "150", tMinScore = "0", tName = "";
        bool tFree = true, tIgnoreLimit, tDirty = true;
        float tLast = -100f;
        readonly List<Cand> tRes = new List<Cand>();
        int tTotal;
        Vector2 tScroll;
        string tMsg = "";

        static readonly string[] SkEn = { "Writing", "Might", "Business", "Arts", "Cunning" };
        static readonly string[] SkTh = { "อักษร", "ยุทธ์", "การค้า", "ศิลป์", "กลยุทธ์" };

        static float F(List<string> t, int i)
        {
            float v;
            return i < t.Count && float.TryParse(t[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0f;
        }

        Cand MakeCand(int src, List<string> t, string fam)
        {
            try
            {
                var p = t[2].Split('|');
                var c = new Cand { Src = src, Data = t, Name = p[0], Sex = p[4], Family = fam };
                int a; int.TryParse(t[3], out a); c.Age = a;
                c.Sk[0] = F(t, 4); c.Sk[1] = F(t, 5); c.Sk[2] = F(t, 6); c.Sk[3] = F(t, 7); c.Sk[4] = F(t, 22);
                c.Salary = Mathf.CeilToInt((c.Sk[0] + c.Sk[1] + c.Sk[2] + c.Sk[3]) * 20f);
                return c;
            }
            catch { return null; }
        }

        static float PF(string s, float def)
        {
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        void Search()
        {
            tRes.Clear();
            tTotal = 0;
            float minAge = PF(tMinAge, 0), maxAge = PF(tMaxAge, 150), minScore = PF(tMinScore, 0);
            string flt = tName.Trim().ToLowerInvariant();
            Action<int, List<List<string>>, string> scan = (src, list, fam) =>
            {
                if (list == null) return;
                foreach (var t in list)
                {
                    if (t == null || t.Count < 24) continue;
                    var c = MakeCand(src, t, fam);
                    if (c == null) continue;
                    tTotal++;
                    if (tSex == 1 && c.Sex != "1") continue;
                    if (tSex == 2 && c.Sex != "0") continue;
                    if (c.Age < minAge || c.Age > maxAge) continue;
                    if (flt.Length > 0 && c.Name.ToLowerInvariant().IndexOf(flt, StringComparison.Ordinal) < 0) continue;
                    c.Score = tSort == 0 ? c.Sk[0] + c.Sk[1] + c.Sk[2] + c.Sk[3] + c.Sk[4] : tSort <= 5 ? c.Sk[tSort - 1] : c.Age;
                    if (tSort <= 5 && c.Score < minScore) continue;
                    tRes.Add(c);
                }
            };
            if (tSrc[0]) scan(-10, Mainload.Member_Hanmen, SrcName(-10));
            if (tSrc[1]) scan(-11, Mainload.HanMen_City, SrcName(-11));
            if (tSrc[2]) scan(-12, Mainload.HanMenData_ShuoMei, SrcName(-12));
            if (tSrc[3] && Mainload.Member_other != null)
                for (int s = 0; s < Mainload.Member_other.Count; s++) scan(s, Mainload.Member_other[s], SrcName(s));
            if (tSort == 6) tRes.Sort((a, b) => a.Age.CompareTo(b.Age));
            else tRes.Sort((a, b) => b.Score.CompareTo(a.Score));
            if (tRes.Count > 150) tRes.RemoveRange(150, tRes.Count - 150);
            tLast = Time.unscaledTime; tDirty = false;
        }

        void Hire(Cand c)
        {
            try
            {
                if (c.Src >= 0) { tMsg = L("The game only lets you hire commoners.", "เกมให้จ้างได้เฉพาะสามัญชน"); return; }
                int max = FormulaData.MenkeNumMax();
                if (!tIgnoreLimit && Mainload.MenKe_Now.Count >= max)
                { tMsg = L("Advisor limit reached (", "ที่ปรึกษาเต็มแล้ว (") + Mainload.MenKe_Now.Count + "/" + max + L("). Tick 'ignore limit' to go over.", ") ติ๊ก 'ข้ามขีดจำกัด' เพื่อจ้างเกิน"); return; }
                if (!tFree)
                {
                    if (FormulaData.GetCoinsNum() < c.Salary) { tMsg = L("Not enough coins for the salary.", "เหรียญไม่พอจ่ายค่าจ้าง"); return; }
                    FormulaData.ChangeCoins(-c.Salary);
                }
                var h = c.Data;
                Mainload.MenKe_Now.Add(new List<string>
                {
                    h[0], h[1], h[2], h[3], h[4], h[5], h[6], h[7], h[8], "0|null|null",
                    h[16], h[17], h[18], h[19], h[20], h[22], h[24], "-1", c.Salary.ToString(),
                    FormulaData.GetTiliMax(int.Parse(h[3])).ToString(), "0", h[26]
                });
                FormulaData.TaskOrder_AddNum(5, 1);
                if (c.Src == -11) Mainload.QMemberID_Remove.Add(h[0]);
                var src = CommonerList(c.Src);
                if (src != null) src.Remove(h);
                tMsg = L("Hired: ", "จ้างแล้ว: ") + c.Name;
                status = tMsg;
                tDirty = true;
            }
            catch (Exception e) { Logger.LogError(e); tMsg = "Error: " + e.Message; }
        }

        void DrawTalent()
        {
            GUILayout.BeginHorizontal();
            string[] sn = { SrcName(-10), SrcName(-11), SrcName(-12), L("Other families (view only)", "ตระกูลอื่น (ดูอย่างเดียว)") };
            for (int i = 0; i < 4; i++)
            {
                bool n = GUILayout.Toggle(tSrc[i], sn[i], "Button", GUILayout.Width(i == 3 ? 200 : 170));
                if (n != tSrc[i]) { tSrc[i] = n; tDirty = true; }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(L("Rank by", "จัดอันดับตาม"), GUILayout.Width(100));
            var sortNames = new[] { L("Total", "รวม"), L(SkEn[0], SkTh[0]), L(SkEn[1], SkTh[1]), L(SkEn[2], SkTh[2]), L(SkEn[3], SkTh[3]), L(SkEn[4], SkTh[4]), L("Youngest", "อายุน้อย") };
            int ns = GUILayout.SelectionGrid(tSort, sortNames, 7, GUILayout.Width(640));
            if (ns != tSort) { tSort = ns; tDirty = true; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(L("Gender", "เพศ"), GUILayout.Width(50));
            int nx = GUILayout.SelectionGrid(tSex, new[] { L("All", "ทั้งหมด"), L("Male", "ชาย"), L("Female", "หญิง") }, 3, GUILayout.Width(210));
            if (nx != tSex) { tSex = nx; tDirty = true; }
            GUILayout.Label(L("Age", "อายุ"), GUILayout.Width(40));
            string a1 = GUILayout.TextField(tMinAge, GUILayout.Width(40)); GUILayout.Label("-", GUILayout.Width(10));
            string a2 = GUILayout.TextField(tMaxAge, GUILayout.Width(40));
            GUILayout.Label(L("Min score", "คะแนนขั้นต่ำ"), GUILayout.Width(100));
            string a3 = GUILayout.TextField(tMinScore, GUILayout.Width(50));
            GUILayout.Label(L("Name", "ชื่อ"), GUILayout.Width(40));
            string a4 = GUILayout.TextField(tName, GUILayout.Width(110));
            if (a1 != tMinAge || a2 != tMaxAge || a3 != tMinScore || a4 != tName) { tMinAge = a1; tMaxAge = a2; tMinScore = a3; tName = a4; tDirty = true; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            tFree = GUILayout.Toggle(tFree, L("Hire for free", "จ้างฟรี (ไม่จ่ายค่าจ้าง)"), GUILayout.Width(230));
            tIgnoreLimit = GUILayout.Toggle(tIgnoreLimit, L("Ignore advisor limit", "ข้ามขีดจำกัดจำนวนที่ปรึกษา"), GUILayout.Width(280));
            GUILayout.Label(L("Advisors: ", "ที่ปรึกษา: ") + Mainload.MenKe_Now.Count + "/" + FormulaData.MenkeNumMax());
            GUILayout.EndHorizontal();

            if (Event.current.type == EventType.Layout && (tDirty || Time.unscaledTime - tLast > 4f)) Search();
            GUILayout.Label(L("Showing ", "แสดง ") + tRes.Count + L(" of ", " จาก ") + tTotal + L(" people", " คน") + "   " + tMsg, stCap);

            tScroll = GUILayout.BeginScrollView(tScroll, GUILayout.Height(330));
            for (int i = 0; i < tRes.Count; i++)
            {
                var c = tRes[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label("#" + (i + 1), GUILayout.Width(34));
                Portrait(LookOfOther(c.Data), 38, 57);
                GUILayout.BeginVertical(GUILayout.Width(380));
                GUILayout.Label(c.Name + "  (" + (c.Sex == "1" ? L("M", "ช") : L("F", "ญ")) + ", " + c.Age + ")   " + c.Family);
                GUILayout.Label(L(SkEn[0], SkTh[0]) + " " + c.Sk[0] + "  " + L(SkEn[1], SkTh[1]) + " " + c.Sk[1] + "  " + L(SkEn[2], SkTh[2]) + " " + c.Sk[2]
                                + "  " + L(SkEn[3], SkTh[3]) + " " + c.Sk[3] + "  " + L(SkEn[4], SkTh[4]) + " " + c.Sk[4], stCap);
                GUILayout.EndVertical();
                GUILayout.Label((tSort == 6 ? L("age ", "อายุ ") : L("score ", "คะแนน ")) + c.Score + "\n" + L("salary ", "ค่าจ้าง ") + c.Salary.ToString("N0"), GUILayout.Width(120));
                GUI.enabled = c.Src < 0;
                if (GUILayout.Button(L("Hire", "จ้าง"), GUILayout.Width(80), GUILayout.Height(44))) { Hire(c); }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            if (tRes.Count == 0) GUILayout.Label(L("Nobody matches.", "ไม่พบใครตามเงื่อนไข"));
            GUILayout.EndScrollView();
            tScroll = WheelScroll(tScroll);
        }
    }
}
