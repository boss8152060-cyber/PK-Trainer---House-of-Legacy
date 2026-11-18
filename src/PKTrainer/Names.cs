using System;
using System.Collections.Generic;

namespace PKTrainer
{
    // การเปลี่ยนชื่อตระกูล / ผู้ก่อตั้ง / ตัวละคร (พร้อมแก้สำเนาชื่อที่เก็บไว้ที่อื่น)
    public partial class Plugin
    {
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
