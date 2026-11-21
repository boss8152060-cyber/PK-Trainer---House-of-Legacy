using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PKTrainer
{
    // ==========================================================================
    //  วาดรูปตัวละคร/ไอคอนไอเทมของเกมลงในหน้าต่าง IMGUI
    //  ภาพทั้งหมดอยู่ใน Resources ของเกมเป็น prefab ที่มี Image/SpriteRenderer:
    //    ไอเทม   : "AllProp/<id>"
    //    ตัวละคร : "AllLooks/Member_S/<เพศ>/<ช่วงวัย>/{houfa|shen|tou|PX|qianfa}/<รหัส>"  (วางซ้อนกัน 5 ชั้นที่ตำแหน่ง 0,0 เหมือนที่เกมทำ)
    //  เราอ่านสไปรต์ + ตำแหน่ง/ขนาดของแต่ละชั้น แล้ววาดด้วย GUI.DrawTextureWithTexCoords
    // ==========================================================================
    public partial class Plugin
    {
        class Layer { public Sprite S; public float X, Y, W, H; }
        class Art { public List<Layer> Layers = new List<Layer>(); public float X0, Y0, X1, Y1; }

        readonly Dictionary<string, Art> artCache = new Dictionary<string, Art>();
        bool artErrLogged;

        struct Look { public string LiHui, Sex, Px, Guan; public int Age; public bool Ok, Emperor; }

        // ชุดข้าราชการ: ช่อง "5@ยศ@..." (ผู้ใหญ่เท่านั้น) -> A/B = ขุนนางพลเรือน, C/D = ขุนนางทหาร (ตามความคู่คี่ของรหัสลำตัว)
        static string GuanOf(string rankField, string lihui, int age)
        {
            try
            {
                if (age < Mainload.OldFenjie[1] || rankField == null) return null;
                var p = rankField.Split('@');
                if (p[0] != "5") return null;
                bool even = int.Parse(lihui.Split('|')[1]) % 2 == 0;
                string prefix = FormulaData.is_WuGuan(rankField) ? (even ? "C" : "D") : (even ? "A" : "B");
                return prefix + "/" + p[1];
            }
            catch { return null; }
        }

        static Look LookOf(string lihui, string sex, string age, string px)
        {
            var k = new Look { LiHui = lihui, Sex = sex, Px = px };
            int a;
            k.Ok = lihui != null && lihui.Split('|').Length >= 4 && int.TryParse(age, out a) && sex != null && px != null;
            if (k.Ok) { int.TryParse(age, out a); k.Age = a; }
            return k;
        }

        // ตำแหน่งข้อมูลรูปในแต่ละประเภท (อ้างอิง PerLiHuiSmall.initData ของเกม)
        static Look LookOf(Group g, List<string> m)
        {
            try
            {
                switch (g.Name)
                {
                    case "Lineage":
                        {
                            var k = LookOf(m[1], m[4].Split('|')[4], m[6], m[5]);
                            if (k.Ok) k.Guan = GuanOf(m[12].Split('|')[0], k.LiHui, k.Age);
                            return k;
                        }
                    case "Spouse": return LookOf(m[1], m[2].Split('|')[4], m[5], m[2].Split('|')[8]);
                    default: return LookOf(m[1], m[2].Split('|')[4], m[3], m[2].Split('|')[8]);   // ผู้ติดตาม / ตระกูลอื่น / ราชสำนัก / สามัญชน
                }
            }
            catch { return new Look(); }
        }

        // รายการตัวละครที่มี [1]=รหัสรูป [2]=ชื่อ|..|เพศ|..|px [3]=อายุ (Member_other / Member_King / Member_Hanmen / ShuoMei)
        static Look LookOfOther(List<string> t, bool king = false)
        {
            try
            {
                var k = LookOf(t[1], t[2].Split('|')[4], t[3], t[2].Split('|')[8]);
                if (!k.Ok) return k;
                string rank = t.Count > 9 ? t[9].Split('|')[0] : null;
                if (king && rank != null && rank.Split('@').Length > 1 && rank.Split('@')[1] == "5") k.Emperor = true;
                else k.Guan = GuanOf(rank, k.LiHui, k.Age);
                return k;
            }
            catch { return new Look(); }
        }

        static string Stage(int age)
        {
            var f = Mainload.OldFenjie;
            if (age < f[0]) return "0";
            if (age < f[1]) return "1";
            return "2";
        }

        string[] PortraitPaths(Look k)
        {
            var f = Mainload.OldFenjie;
            string st = Stage(k.Age);
            string st5 = k.Age >= f[2] ? "3" : st;          // ผมหน้า/ผมหลังของผู้สูงวัยใช้ชุด 3
            var p = k.LiHui.Split('|');
            string b = "AllLooks/Member_S/" + k.Sex + "/";
            if (k.Emperor)                                   // จักรพรรดิ/จักรพรรดินี ใช้ชุดพิเศษ King_S
            {
                string kb = "AllLooks/King_S/HD/" + k.Sex + "/";
                return new[] { kb + st5 + "/houfa/0", kb + st + "/shen/0", b + st + "/tou/" + p[2], b + st + "/PX/" + k.Px, kb + st5 + "/qianfa/0" };
            }
            return new[]
            {
                b + st5 + "/houfa/" + p[0],
                k.Guan != null ? b + "5/" + k.Guan : b + st + "/shen/" + p[1],
                b + st + "/tou/" + p[2],
                b + st + "/PX/" + k.Px,
                b + st5 + "/qianfa/" + p[3],
            };
        }

        void AddLayers(Art art, string path)
        {
            var go = Resources.Load<GameObject>(path);
            if (go == null) return;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                var img = t.GetComponent<Image>();
                if (img != null && img.sprite != null)
                {
                    var rt = t as RectTransform;
                    if (rt == null) continue;
                    var c = new Vector3[4];
                    rt.GetWorldCorners(c);
                    art.Layers.Add(new Layer { S = img.sprite, X = c[0].x, Y = c[0].y, W = c[2].x - c[0].x, H = c[2].y - c[0].y });
                    continue;
                }
                var sr = t.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite != null)
                {
                    var bnd = sr.sprite.bounds;
                    var m = t.localToWorldMatrix;
                    var a = m.MultiplyPoint3x4(bnd.min);
                    var b = m.MultiplyPoint3x4(bnd.max);
                    art.Layers.Add(new Layer { S = sr.sprite, X = Mathf.Min(a.x, b.x), Y = Mathf.Min(a.y, b.y), W = Mathf.Abs(b.x - a.x), H = Mathf.Abs(b.y - a.y) });
                }
            }
        }

        Art BuildArt(params string[] paths)
        {
            var art = new Art();
            foreach (var p in paths) { try { AddLayers(art, p); } catch (Exception e) { if (!artErrLogged) { artErrLogged = true; Logger.LogWarning("art: " + e.Message); } } }
            if (art.Layers.Count > 0)
            {
                art.X0 = art.Y0 = float.MaxValue; art.X1 = art.Y1 = float.MinValue;
                foreach (var l in art.Layers)
                {
                    art.X0 = Mathf.Min(art.X0, l.X); art.Y0 = Mathf.Min(art.Y0, l.Y);
                    art.X1 = Mathf.Max(art.X1, l.X + l.W); art.Y1 = Mathf.Max(art.Y1, l.Y + l.H);
                }
            }
            return art;
        }

        Art GetArt(string key, Func<string[]> paths)
        {
            Art a;
            if (artCache.TryGetValue(key, out a))
            {
                // สไปรต์ที่ถูกเกมคลายหน่วยความจำไปแล้ว -> สร้างใหม่
                if (a.Layers.Count == 0 || a.Layers[0].S != null) return a;
            }
            a = BuildArt(paths());
            artCache[key] = a;
            return a;
        }

        void DrawArt(Rect box, Art a)
        {
            if (a == null || a.Layers.Count == 0) return;
            float uw = a.X1 - a.X0, uh = a.Y1 - a.Y0;
            if (uw <= 0 || uh <= 0) return;
            float sc = Mathf.Min(box.width / uw, box.height / uh);
            float ox = box.x + (box.width - uw * sc) / 2f, oy = box.y + (box.height - uh * sc) / 2f;
            foreach (var l in a.Layers)
            {
                var s = l.S;
                if (s == null) continue;
                var tex = s.texture;
                if (tex == null) continue;
                var tr = s.textureRect;
                var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
                var r = new Rect(ox + (l.X - a.X0) * sc, oy + (a.Y1 - (l.Y + l.H)) * sc, l.W * sc, l.H * sc);
                GUI.DrawTextureWithTexCoords(r, tex, uv, true);
            }
        }

        // ---- ช่องว่างใน layout + วาดรูป ----
        void Portrait(Look k, float w, float h)
        {
            var r = GUILayoutUtility.GetRect(w, h, GUILayout.Width(w), GUILayout.Height(h));
            if (!k.Ok || Event.current.type != EventType.Repaint) return;
            try
            {
                string key = "P|" + k.Sex + "|" + Stage(k.Age) + "|" + (k.Age >= Mainload.OldFenjie[2] ? "3" : "") + "|" + k.LiHui + "|" + k.Px + "|" + k.Guan + "|" + k.Emperor;
                DrawArt(r, GetArt(key, () => PortraitPaths(k)));
            }
            catch (Exception e) { if (!artErrLogged) { artErrLogged = true; Logger.LogWarning("portrait: " + e.Message); } }
        }

        void ItemIcon(int id, float size)
        {
            var r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            if (Event.current.type != EventType.Repaint) return;
            try { DrawArt(r, GetArt("I|" + id, () => new[] { "AllProp/" + id })); }
            catch (Exception e) { if (!artErrLogged) { artErrLogged = true; Logger.LogWarning("icon: " + e.Message); } }
        }
    }
}
