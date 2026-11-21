using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PKTrainer
{
    // ==========================================================================
    //  ม้า: Mainload.Horse_Have[i] = [ชนิด(0..AllHorseShowNum-1), อายุ, อายุขัย, กำลัง, ความเร็ว, ปัญญา, รหัสเจ้าของ|null]
    //  ที่ว่างในคอกม้า = FamilyData[6] (เกมหัก 1 ต่อม้า 1 ตัว)   ไอคอน = Resources "AllHorse/Icon/<ชนิด>"
    // ==========================================================================
    public partial class Plugin
    {
        int hType;
        string hAge = "3", hStr = "50", hSpd = "50", hInt = "50", hCount = "1";
        Vector2 hScroll;
        string horseMsg = "";

        static int HorseTypes() { return Math.Max(1, Mainload.AllHorseShowNum); }

        void HorseIcon(int type, float size)
        {
            var r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            if (Event.current.type != EventType.Repaint) return;
            try { DrawArt(r, GetArt("H|" + type, () => new[] { "AllHorse/Icon/" + type })); } catch { }
        }

        static int ParseInt(string s, int def, int lo, int hi)
        {
            int v;
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) v = def;
            return Math.Max(lo, Math.Min(hi, v));
        }

        void SpawnHorses()
        {
            try
            {
                int n = ParseInt(hCount, 1, 1, 200);
                int free = int.Parse(Mainload.FamilyData[6]);
                if (n > free) n = free;
                if (n <= 0) { horseMsg = L("Stable is full - add stable slots below.", "คอกม้าเต็ม - เพิ่มช่องคอกม้าด้านล่างก่อน"); return; }
                for (int i = 0; i < n; i++)
                    Mainload.Horse_Have.Add(new List<string>
                    {
                        hType.ToString(),
                        ParseInt(hAge, 3, 1, 30).ToString(),
                        (18 + ParseInt(hAge, 3, 1, 30)).ToString(),
                        ParseInt(hStr, 50, 1, 999).ToString(),
                        ParseInt(hSpd, 50, 1, 999).ToString(),
                        ParseInt(hInt, 50, 1, 999).ToString(),
                        "null"
                    });
                Mainload.FamilyData[6] = (free - n).ToString();
                horseMsg = L("Added horses: ", "เพิ่มม้าแล้ว: ") + n;
                status = horseMsg;
            }
            catch (Exception e) { Logger.LogError(e); horseMsg = "Error: " + e.Message; }
        }

        void DrawHorses()
        {
            GUILayout.Space(6);
            GUILayout.Label(L("Stable slots free: ", "ที่ว่างในคอกม้า: ") + Mainload.FamilyData[6]
                            + "     " + L("horses owned: ", "ม้าที่มี: ") + Mainload.Horse_Have.Count);

            GUILayout.Label(L("Add horses", "เพิ่มม้า"), stSec);
            GUILayout.BeginHorizontal();
            for (int t = 0; t < HorseTypes(); t++)
            {
                GUILayout.BeginVertical(GUILayout.Width(66));
                HorseIcon(t, 56);
                if (GUILayout.Toggle(hType == t, "#" + t, "Button")) hType = t;
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(L("Age", "อายุ"), GUILayout.Width(40)); hAge = GUILayout.TextField(hAge, GUILayout.Width(40));
            GUILayout.Label(L("Strength", "กำลัง"), GUILayout.Width(60)); hStr = GUILayout.TextField(hStr, GUILayout.Width(50));
            GUILayout.Label(L("Speed", "ความเร็ว"), GUILayout.Width(70)); hSpd = GUILayout.TextField(hSpd, GUILayout.Width(50));
            GUILayout.Label(L("Wit", "ปัญญา"), GUILayout.Width(60)); hInt = GUILayout.TextField(hInt, GUILayout.Width(50));
            GUILayout.Label(L("Count", "จำนวน"), GUILayout.Width(60)); hCount = GUILayout.TextField(hCount, GUILayout.Width(40));
            if (GUILayout.Button(L("+ Add", "+ เพิ่มม้า"), GUILayout.Width(110), GUILayout.Height(28))) SpawnHorses();
            GUILayout.EndHorizontal();
            if (GUILayout.Button(L("Add 10 stable slots", "เพิ่มที่ว่างคอกม้า 10 ช่อง"), GUILayout.Width(260)))
            {
                try { Mainload.FamilyData[6] = (int.Parse(Mainload.FamilyData[6]) + 10).ToString(); horseMsg = L("Stable +10", "คอกม้า +10"); } catch { }
            }
            GUILayout.Label(horseMsg, stCap);

            GUILayout.Space(6);
            GUILayout.Label(L("Your horses", "ม้าของคุณ"), stSec);
            hScroll = GUILayout.BeginScrollView(hScroll, GUILayout.Height(230));
            for (int i = 0; i < Mainload.Horse_Have.Count; i++)
            {
                var h = Mainload.Horse_Have[i];
                if (h.Count < 7) continue;
                GUILayout.BeginHorizontal();
                HorseIcon(ParseInt(h[0], 0, 0, 999), 48);
                GUILayout.Label(L("Horse #", "ม้า #") + h[0] + "   " + L("age ", "อายุ ") + h[1]
                                + "\n" + L("Str ", "กำลัง ") + h[3] + "   " + L("Spd ", "ความเร็ว ") + h[4] + "   " + L("Wit ", "ปัญญา ") + h[5], GUILayout.Width(360));
                if (GUILayout.Button(L("Max stats (99)", "ค่าสูงสุด (99)"), GUILayout.Width(150), GUILayout.Height(40)))
                { h[3] = "99"; h[4] = "99"; h[5] = "99"; }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            hScroll = WheelScroll(hScroll);
        }
    }
}
