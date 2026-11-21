using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace PKTrainer
{
    // ส่งออกข้อมูลของเกมสำหรับออกแบบผังอาคาร (อ่านอย่างเดียว ไม่แก้ไขอะไรในเกม):
    //  - รูปทรงพื้นที่ (collider / sprite) ของตัวชี้ตำแหน่งวางอาคารทุกหลัง ทุกทิศ
    //  - ผังตัวอย่างสำเร็จรูปของเกม (Resources/NongZLanTu ฯลฯ)
    // ไฟล์ออกที่ BepInEx\PKTrainerData\
    public partial class Plugin
    {
        bool exporting;
        string exportMsg = "";

        static string Fl(float f) { return f.ToString("0.####", CultureInfo.InvariantCulture); }

        static void AddPt(StringBuilder sb, Vector3 p, ref bool first)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('[').Append(Fl(p.x)).Append(',').Append(Fl(p.y)).Append(']');
        }

        static string Js(string v)
        {
            if (v == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (char c in v)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c < 32) sb.Append(' ');
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static void JsRows(StringBuilder sb, System.Collections.Generic.List<System.Collections.Generic.List<string>> rows)
        {
            sb.Append('[');
            if (rows != null)
                for (int i = 0; i < rows.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('[');
                    var r = rows[i];
                    for (int j = 0; j < r.Count; j++) { if (j > 0) sb.Append(','); sb.Append(Js(r[j])); }
                    sb.Append(']');
                }
            sb.Append(']');
        }

        // ตารางอาคารตัวจริงจากหน่วยความจำของเกม + ชื่อ + ข้อมูลที่ดิน/ตระกูลปัจจุบัน
        void DumpTables(string dir)
        {
            var sb = new StringBuilder();
            sb.Append("{\"allbuild\":"); JsRows(sb, Mainload.AllBuilddata);
            sb.Append(",\"names\":"); JsRows(sb, AllText.Text_AllBuild);
            sb.Append(",\"groups\":"); JsRows(sb, AllText.Text_BuildGroup);
            sb.Append(",\"lang\":").Append(Mainload.SetData != null && Mainload.SetData.Count > 4 ? Mainload.SetData[4] : -1);
            sb.Append(",\"family\":[");
            if (Mainload.FamilyData != null)
                for (int i = 0; i < Mainload.FamilyData.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Js(Mainload.FamilyData[i])); }
            sb.Append("],\"scene\":").Append(Js(Mainload.SceneID));
            sb.Append(",\"plot\":[");
            try
            {
                var parts = Mainload.SceneID.Split('|');
                var row = Mainload.NongZ_now[int.Parse(parts[1])][int.Parse(parts[2])];
                for (int i = 0; i < row.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Js(row[i])); }
            }
            catch { }
            sb.Append("]}");
            File.WriteAllText(Path.Combine(dir, "tables.json"), sb.ToString(), new UTF8Encoding(false));
        }

        static string SafeTag(GameObject go)
        {
            try { return go.tag; } catch { return "?"; }
        }

        // collider -> {"t":ชนิด,"tag":แท็ก,"n":ชื่อ,"r":รัศมี,"p":[[x,y],...]} (พิกัดโลก; ถ้าเป็นวงกลม p = จุดศูนย์กลาง)
        static void AppendShape(StringBuilder sb, Collider2D c)
        {
            sb.Append("{\"t\":\"").Append(c.GetType().Name).Append("\",\"tag\":").Append(Js(SafeTag(c.gameObject)))
              .Append(",\"n\":").Append(Js(c.gameObject.name)).Append(",\"r\":");
            var circ = c as CircleCollider2D;
            var poly = c as PolygonCollider2D;
            var box = c as BoxCollider2D;
            var edge = c as EdgeCollider2D;
            sb.Append(Fl(circ != null ? circ.radius * Mathf.Max(Mathf.Abs(c.transform.lossyScale.x), Mathf.Abs(c.transform.lossyScale.y)) : 0f));
            sb.Append(",\"p\":[");
            bool fp = true;
            if (circ != null) AddPt(sb, c.transform.TransformPoint((Vector3)circ.offset), ref fp);
            else if (poly != null)
            {
                for (int pi = 0; pi < poly.pathCount; pi++)
                    foreach (var pt in poly.GetPath(pi)) AddPt(sb, c.transform.TransformPoint((Vector3)(pt + poly.offset)), ref fp);
            }
            else if (box != null)
            {
                var h = box.size * 0.5f; var o = box.offset;
                AddPt(sb, c.transform.TransformPoint(new Vector3(o.x - h.x, o.y - h.y)), ref fp);
                AddPt(sb, c.transform.TransformPoint(new Vector3(o.x + h.x, o.y - h.y)), ref fp);
                AddPt(sb, c.transform.TransformPoint(new Vector3(o.x + h.x, o.y + h.y)), ref fp);
                AddPt(sb, c.transform.TransformPoint(new Vector3(o.x - h.x, o.y + h.y)), ref fp);
            }
            else if (edge != null)
                foreach (var pt in edge.points) AddPt(sb, c.transform.TransformPoint((Vector3)(pt + edge.offset)), ref fp);
            sb.Append("]}");
        }

        static void JsList(StringBuilder sb, System.Collections.Generic.List<string> l)
        {
            sb.Append('[');
            if (l != null) for (int i = 0; i < l.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Js(l[i])); }
            sb.Append(']');
        }

        // ฉากปัจจุบัน: ขอบเขตที่ดิน/สิ่งกีดขวาง (collider ที่มีแท็กของเกม) + รายการอาคารที่วางซ้อนได้/ติดขอบ
        void DumpScene(string dir)
        {
            var sb = new StringBuilder();
            sb.Append("{\"scene\":").Append(Js(Mainload.SceneID)).Append(",\"bianyuan\":"); JsList(sb, Mainload.BuildIDBianYuan);
            sb.Append(",\"canstack\":"); JsList(sb, Mainload.BuildIDCanDieJiaShang);
            sb.Append(",\"objs\":[");
            bool first = true;
            foreach (var tag in new[] { "DiXingZuDang", "DiXingZuDangB", "DiXingZuDangC", "DiXingZuDangD", "FeiWoRange" })
            {
                GameObject[] objs;
                try { objs = GameObject.FindGameObjectsWithTag(tag); } catch { continue; }
                foreach (var go in objs)
                    foreach (var c in go.GetComponents<Collider2D>())
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        AppendShape(sb, c);
                    }
            }
            sb.Append("]}");
            File.WriteAllText(Path.Combine(dir, "scene.json"), sb.ToString(), new UTF8Encoding(false));
        }

        IEnumerator ExportRoutine()
        {
            exporting = true;
            exportMsg = "Working...";
            string dir = Path.Combine(Paths.BepInExRootPath, "PKTrainerData");
            Directory.CreateDirectory(dir);
            int presets = 0, prefabs = 0;
            try { DumpTables(dir); } catch (Exception e) { Logger.LogError(e); }
            try { DumpScene(dir); } catch (Exception e) { Logger.LogError(e); }
            try
            {
                // 1) ผังตัวอย่างของเกม
                foreach (var folder in new[] { "NongZLanTu", "FudiLanTu", "MudiLanTu", "ZhuangLanTu", "MLanTu", "LanTu" })
                    foreach (var ta in Resources.LoadAll<TextAsset>(folder))
                    {
                        File.WriteAllBytes(Path.Combine(dir, folder + "_" + ta.name + ".es3"), ta.bytes);
                        presets++;
                    }
            }
            catch (Exception e) { Logger.LogError(e); }

            // 2) รูปทรงพื้นที่ของตัวชี้ตำแหน่งวางอาคาร
            string set = string.IsNullOrEmpty(Mainload.BuildTaoZhuangID_SelectNow) ? "0" : Mainload.BuildTaoZhuangID_SelectNow;
            int count = Mainload.AllBuilddata != null ? Mainload.AllBuilddata.Count : 240;
            var sb = new StringBuilder();
            sb.Append("{\"set\":\"").Append(set).Append("\",\"items\":[");
            bool firstItem = true;
            bool loggedTypes = false;
            for (int id = 0; id < count; id++)
            {
                for (int d = 0; d < 4; d++)
                {
                    GameObject go = null;
                    try { go = Resources.Load<GameObject>("AllBuild/" + set + "/BuildTip/" + id + "/" + d); } catch { }
                    if (go == null) continue;
                    prefabs++;
                    try
                    {
                        if (!loggedTypes)
                        {
                            loggedTypes = true;
                            foreach (var c in go.GetComponentsInChildren<Component>(true))
                                Logger.LogInfo("tip prefab component: " + (c == null ? "null" : c.GetType().Name) + " on " + (c == null ? "" : c.gameObject.name));
                        }
                        if (!firstItem) sb.Append(',');
                        firstItem = false;
                        sb.Append("{\"id\":").Append(id).Append(",\"dir\":").Append(d).Append(",\"shapes\":[");
                        bool fs = true;
                        foreach (var c in go.GetComponentsInChildren<Collider2D>(true))
                        {
                            if (!fs) sb.Append(',');
                            fs = false;
                            AppendShape(sb, c);
                        }
                        sb.Append("],\"sprites\":[");
                        bool fr = true;
                        foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
                        {
                            if (sr.sprite == null) continue;
                            if (!fr) sb.Append(',');
                            fr = false;
                            var b = sr.sprite.bounds;
                            var lo = sr.transform.TransformPoint(b.min);
                            var hi = sr.transform.TransformPoint(b.max);
                            sb.Append("{\"n\":\"").Append(sr.gameObject.name.Replace("\"", "")).Append("\",\"b\":[")
                              .Append(Fl(lo.x)).Append(',').Append(Fl(lo.y)).Append(',').Append(Fl(hi.x)).Append(',').Append(Fl(hi.y)).Append("]}");
                        }
                        sb.Append("]}");
                    }
                    catch (Exception e) { Logger.LogError(e); }
                }
                if (id % 10 == 0) { exportMsg = "Working... " + id + "/" + count; yield return null; }
            }
            sb.Append("]}");
            File.WriteAllText(Path.Combine(dir, "footprints.json"), sb.ToString());
            exportMsg = "Done. " + presets + " preset layouts, " + prefabs + " footprint prefabs -> " + dir;
            Logger.LogInfo(exportMsg);
            exporting = false;
        }

        void DrawDevExport()
        {
            GUILayout.Space(14);
            GUILayout.Label(L("Developer: export game data for layout design (read-only)", "สำหรับนักพัฒนา: ส่งออกข้อมูลเกมเพื่อออกแบบผัง (อ่านอย่างเดียว)"));
            GUI.enabled = !exporting;
            if (GUILayout.Button(L("Export building footprints + preset layouts", "ส่งออกขนาดพื้นที่อาคาร + ผังตัวอย่างของเกม"), GUILayout.Width(420)))
                StartCoroutine(ExportRoutine());
            GUI.enabled = true;
            GUILayout.Label(exportMsg);
        }
    }
}
