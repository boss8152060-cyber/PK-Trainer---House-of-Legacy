using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace PKTrainer
{
    // ==========================================================================
    //  แท็บ "แผนที่": รื้อสิ่งปลูกสร้างทั้งหมดในฉากปัจจุบัน
    //  ใช้ฟังก์ชันรื้อของเกมเอง (PerBuildShow.ChaiBT) ทีละหลัง จึงยึดกฎของเกม
    //  (ห้องที่มีคนอยู่ / คลังที่ยังมีของ ฯลฯ จะถูกข้าม) และเรียงจากท้ายไปหน้า
    //  เพราะเกมลบรายการตามลำดับ sibling index
    // ==========================================================================
    public partial class Plugin
    {
        static readonly MethodInfo ChaiMethod =
            typeof(PerBuildShow).GetMethod("ChaiBT", BindingFlags.Instance | BindingFlags.NonPublic);

        bool clearing, evictFirst = true;
        int evictedCount;
        float confirmUntil, lastCount = -100f;
        string mapMsg = "", countText = "";
        int buildCount;
        // รายการชนิดอาคารในฉาก (class id -> จำนวน) และชนิดที่ติ๊กเลือกไว้
        readonly SortedDictionary<int, int> typeCounts = new SortedDictionary<int, int>();
        readonly HashSet<int> selTypes = new HashSet<int>();
        Vector2 typeScroll;
        string typeFilter = "";

        static int ClassOf(PerBuildShow b)
        {
            int id;
            return int.TryParse(b.buildClassID, out id) ? id : -1;
        }

        string BuildName(int id)
        {
            try
            {
                var n = AllText.Text_AllBuild[id];
                for (int k = 2; k >= 0; k--)
                    if (n.Count > k && !string.IsNullOrEmpty(n[k]) && n[k].Trim().Length > 0) return n[k].Split('|')[0];
            }
            catch { }
            return "#" + id;
        }

        int SelectedCount()
        {
            int n = 0;
            foreach (var kv in typeCounts) if (selTypes.Contains(kv.Key)) n += kv.Value;
            return n;
        }

        List<PerBuildShow> FindBuildings()
        {
            var res = new List<PerBuildShow>();
            foreach (var b in UnityEngine.Object.FindObjectsOfType<PerBuildShow>())
                if (b != null && b.ishave && b.transform.parent != null) res.Add(b);
            return res;
        }

        void RefreshCount()
        {
            var list = FindBuildings();
            buildCount = list.Count;
            var per = new SortedDictionary<string, int>();
            typeCounts.Clear();
            foreach (var b in list)
            {
                int cid = ClassOf(b), cn;
                typeCounts.TryGetValue(cid, out cn);
                typeCounts[cid] = cn + 1;
                int n;
                per.TryGetValue(b.BuildBigClass ?? "?", out n);
                per[b.BuildBigClass ?? "?"] = n + 1;
            }
            var parts = new List<string>();
            foreach (var kv in per) parts.Add(kv.Key + ": " + kv.Value);
            countText = string.Join("   ", parts.ToArray());
            selTypes.RemoveWhere(k => !typeCounts.ContainsKey(k));
            lastCount = Time.unscaledTime;
        }

        void DrawMap()
        {
            if (Event.current.type == EventType.Layout && !clearing && Time.unscaledTime - lastCount > 2f) RefreshCount();

            GUILayout.Space(8);
            GUILayout.Label(L("Current scene: ", "ฉากปัจจุบัน: ") + Mainload.SceneID);
            GUILayout.Label(L("Buildings found: ", "สิ่งปลูกสร้างที่พบ: ") + buildCount + (countText.Length > 0 ? "    (" + countText + ")" : ""));
            GUILayout.Space(6);
            GUILayout.Label(L("Demolishes the ticked building types one by one using the game's own demolish rules.",
                              "รื้อชนิดอาคารที่ติ๊กไว้ทีละหลัง ตามกฎการรื้อของเกมเอง"));
            GUILayout.Label(L("Buildings the game refuses to demolish (occupied rooms, stocked storage...) are skipped.",
                              "หลังที่เกมไม่ให้รื้อ (ห้องที่มีคนอยู่ คลังที่ยังมีของ ฯลฯ) จะถูกข้าม"));
            evictFirst = GUILayout.Toggle(evictFirst, "  " + L("Move out residents / students / retainers first so the building can be demolished",
                                                 "ไล่คนออกก่อน (ผู้อาศัย นักเรียน คนรับใช้/ผู้ติดตาม) เพื่อให้รื้อได้"));
            GUILayout.Label(L("Your save folder is backed up automatically first. Costs/refunds follow the game's rules.",
                              "โปรแกรมสำรองโฟลเดอร์ save ให้อัตโนมัติก่อนเริ่ม ค่าใช้จ่ายและเงินคืนเป็นไปตามกฎของเกม"));
            GUILayout.Space(10);

            // ---- เลือกชนิดอาคารที่จะรื้อ ----
            GUILayout.Label(L("Choose what to clear (tick the building types):", "เลือกชนิดอาคารที่จะเคลียร์ (ติ๊กที่ต้องการ):"));
            GUILayout.BeginHorizontal();
            GUILayout.Label(L("Search:", "ค้นหา:"), GUILayout.Width(60));
            typeFilter = GUILayout.TextField(typeFilter, GUILayout.Width(200));
            if (GUILayout.Button(L("Select all", "เลือกทั้งหมด"), GUILayout.Width(110)))
                foreach (var kv in typeCounts) selTypes.Add(kv.Key);
            if (GUILayout.Button(L("Select none", "ไม่เลือก"), GUILayout.Width(110))) selTypes.Clear();
            GUILayout.EndHorizontal();
            typeScroll = GUILayout.BeginScrollView(typeScroll, GUILayout.Height(230), GUILayout.Width(640));
            string flt = typeFilter.Trim().ToLowerInvariant();
            foreach (var kv in typeCounts)
            {
                string nm = BuildName(kv.Key);
                if (flt.Length > 0 && nm.ToLowerInvariant().IndexOf(flt, StringComparison.Ordinal) < 0 && ("#" + kv.Key) != flt) continue;
                bool on = selTypes.Contains(kv.Key);
                bool now = GUILayout.Toggle(on, "  " + nm + "  (#" + kv.Key + ")   x" + kv.Value);
                if (now != on) { if (now) selTypes.Add(kv.Key); else selTypes.Remove(kv.Key); }
            }
            GUILayout.EndScrollView();
            typeScroll = WheelScroll(typeScroll);
            int selN = SelectedCount();
            GUILayout.Space(6);

            bool armed = Time.unscaledTime < confirmUntil;
            string label = clearing ? L("Working...", "กำลังรื้อ...")
                : armed ? L("Click again to CONFIRM (" + selN + " buildings)", "กดอีกครั้งเพื่อ ยืนยัน (" + selN + " หลัง)")
                : L("Clear selected (" + selN + ")", "เคลียร์ที่เลือก (" + selN + " หลัง)");
            GUI.enabled = !clearing && selN > 0;
            if (GUILayout.Button(label, GUILayout.Height(40), GUILayout.Width(460)))
            {
                if (armed) { confirmUntil = 0; StartCoroutine(ClearRoutine()); }
                else confirmUntil = Time.unscaledTime + 6f;
            }
            GUI.enabled = true;
            GUILayout.Space(6);
            GUILayout.Label(mapMsg);

            DrawBlueprintImport();
            DrawMoveRooms();
            DrawMirror();
            DrawDevExport();
        }

        // ================== นำเข้าผังอาคาร (blueprint .es3) ==================
        // ปุ่มนำเข้าของเกมใช้หน้าต่างเลือกไฟล์ของ Windows ที่เริ่มในโฟลเดอร์ StreamingAssets
        // ที่นี่ส่งพาธไฟล์เข้าเมธอดนำเข้าของเกม (Read_LanTu) ตรงๆ ผ่านหน้าต่างพิมพ์เขียวที่เปิดอยู่
        static readonly Type[] BlueprintPanels = { typeof(NongZLanTuPanelA), typeof(FudiLanTuPanelA), typeof(MudiLanTuPanelA) };
        string bpPath = "", bpMsg = "";
        readonly List<string> bpFiles = new List<string>();
        bool bpScanned;

        void ScanBlueprints()
        {
            bpFiles.Clear();
            var dirs = new List<string>();
            try { dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")); } catch { }
            try { dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)); } catch { }
            try { dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)); } catch { }
            try { dirs.Add(Application.streamingAssetsPath); } catch { }
            foreach (var d in dirs)
            {
                if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) continue;
                string[] files;
                try { files = Directory.GetFiles(d, "*.es3"); } catch { continue; }
                foreach (var f in files)
                {
                    try
                    {
                        if (new FileInfo(f).Length > 8 * 1024 * 1024) continue;
                        if (File.ReadAllText(f).Contains("\"LanTuData\"") && !bpFiles.Contains(f)) bpFiles.Add(f);
                    }
                    catch { }
                }
            }
            if (bpPath.Length == 0 && bpFiles.Count > 0) bpPath = bpFiles[0];
            bpScanned = true;
        }

        MonoBehaviour FindBlueprintPanel()
        {
            foreach (var t in BlueprintPanels)
                foreach (var o in UnityEngine.Object.FindObjectsOfType(t))
                {
                    var mb = o as MonoBehaviour;
                    if (mb != null && mb.isActiveAndEnabled) return mb;
                }
            return null;
        }

        string ImportBlueprint()
        {
            string path = bpPath.Trim().Trim('"');
            if (!File.Exists(path)) return L("File not found.", "ไม่พบไฟล์");
            var panel = FindBlueprintPanel();
            if (panel == null)
                return L("Open the game's blueprint dialog first (the blueprint button on the left panel), then press Import.",
                         "เปิดหน้าต่างพิมพ์เขียวของเกมก่อน (ปุ่ม พิมพ์เขียว ที่แถบซ้าย) แล้วกดนำเข้า");
            var m = panel.GetType().GetMethod("Read_LanTu", BindingFlags.Instance | BindingFlags.NonPublic);
            if (m == null) return "Read_LanTu not found";
            string bk = null;
            try { bk = BackupSaves(); }
            catch (Exception e) { return L("Backup failed - cancelled: ", "สำรองไม่สำเร็จ ยกเลิก: ") + e.Message; }
            m.Invoke(panel, new object[] { path });
            return L("Sent to the game. If nothing happens, the game's own rules blocked it (scene must be empty, plot size, enough coins) - watch for its tip message.",
                     "ส่งให้เกมแล้ว ถ้าไม่มีอะไรเกิดขึ้น แปลว่าติดกฎของเกม (ฉากต้องว่าง ขนาดที่ดิน เหรียญพอ) ดูข้อความแจ้งของเกม")
                   + (bk != null ? L("  Backup: ", "  สำรองไว้ที่: ") + bk : "");
        }

        void DrawBlueprintImport()
        {
            if (!bpScanned && Event.current.type == EventType.Layout) ScanBlueprints();

            GUILayout.Space(14);
            GUILayout.Label(L("Import building layout (blueprint .es3)", "นำเข้าผังอาคาร (ไฟล์พิมพ์เขียว .es3)"));
            GUILayout.BeginHorizontal();
            GUILayout.Label(L("File:", "ไฟล์:"), GUILayout.Width(40));
            bpPath = GUILayout.TextField(bpPath, GUILayout.Width(520));
            if (GUILayout.Button(L("Rescan", "สแกนใหม่"), GUILayout.Width(90))) ScanBlueprints();
            GUILayout.EndHorizontal();

            int shown = Math.Min(bpFiles.Count, 6);
            for (int i = 0; i < shown; i++)
                if (GUILayout.Button(bpFiles[i], GUILayout.Width(640))) bpPath = bpFiles[i];
            if (bpFiles.Count == 0)
                GUILayout.Label(L("No blueprint files found in Downloads / Desktop / Documents. Type the full path above.",
                                  "ไม่พบไฟล์พิมพ์เขียวใน Downloads / Desktop / Documents พิมพ์พาธเต็มด้านบนได้"));

            GUILayout.Label(L("Needs: the game's blueprint dialog open, an empty scene, big enough plot, enough coins.",
                              "ต้องมี: หน้าต่างพิมพ์เขียวของเกมเปิดอยู่ ฉากว่าง ที่ดินใหญ่พอ เหรียญพอ"));
            if (GUILayout.Button(L("Import this layout", "นำเข้าผังนี้"), GUILayout.Height(34), GUILayout.Width(300)))
            {
                try { bpMsg = ImportBlueprint(); }
                catch (Exception e) { Logger.LogError(e); bpMsg = "Error: " + e.Message; }
            }
            GUILayout.Label(bpMsg);
        }


        // ================== ย้ายห้องที่รื้อไม่ได้ไปตำแหน่งตามผัง ==================
        // อ่านไฟล์ข้อความ แต่ละบรรทัด: "รหัสอาคาร a b ทิศ" เช่น TMB0 -5 -5 1
        // แก้ตำแหน่ง (BuildInto_m[i][5]) และทิศ ([i][6]) ของอาคารนั้นในข้อมูลเกม แล้วสั่งโหลดฉากใหม่ (เกมเซฟให้เองตอนโหลดฉาก)
        // คนที่อยู่ในห้องยังอยู่เหมือนเดิม เพราะผูกกับรหัสอาคาร ไม่ใช่ตำแหน่ง
        string movePath = "", moveMsg = "";

        string ApplyMoves()
        {
            string path = movePath.Trim().Trim('"');
            if (!File.Exists(path)) return L("File not found.", "ไม่พบไฟล์");
            if (Mainload.SceneID == null || !Mainload.SceneID.StartsWith("M|"))
                return L("Open the mansion scene first.", "ต้องอยู่ในฉากคฤหาสน์ก่อน");
            string bk = null;
            try { bk = BackupSaves(); }
            catch (Exception e) { return L("Backup failed - cancelled: ", "สำรองไม่สำเร็จ ยกเลิก: ") + e.Message; }
            int moved = 0, missing = 0;
            foreach (var raw in File.ReadAllLines(path))
            {
                var p = raw.Trim().Split(new[] { ' ', '	', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
                int a, b, d;
                if (p.Length < 4 || !int.TryParse(p[1], out a) || !int.TryParse(p[2], out b) || !int.TryParse(p[3], out d)) continue;
                bool hit = false;
                for (int i = 0; i < Mainload.BuildInto_m.Count; i++)
                {
                    var r = Mainload.BuildInto_m[i];
                    if (r[0] != p[0]) continue;
                    r[5] = a + "|" + b;
                    r[6] = d.ToString();
                    hit = true; moved++;
                }
                if (!hit) missing++;
            }
            if (moved > 0) Mainload.isUpdateScene = true;
            return L("Moved " + moved + " rooms" + (missing > 0 ? ", not found " + missing : "") + ". The scene reloads now.",
                     "ย้ายแล้ว " + moved + " ห้อง" + (missing > 0 ? " ไม่พบ " + missing : "") + " ฉากกำลังโหลดใหม่")
                   + (bk != null ? L("  Backup: ", "  สำรองไว้ที่: ") + bk : "");
        }

        void DrawMoveRooms()
        {
            GUILayout.Space(14);
            GUILayout.Label(L("Move rooms that cannot be demolished (list file: id a b dir per line)",
                              "ย้ายห้องที่รื้อไม่ได้ไปตำแหน่งตามผัง (ไฟล์รายการ: รหัส a b ทิศ ต่อบรรทัด)"));
            GUILayout.BeginHorizontal();
            GUILayout.Label(L("File:", "ไฟล์:"), GUILayout.Width(40));
            movePath = GUILayout.TextField(movePath, GUILayout.Width(520));
            GUILayout.EndHorizontal();
            if (GUILayout.Button(L("Move the rooms", "ย้ายห้อง"), GUILayout.Height(34), GUILayout.Width(300)))
            {
                try { moveMsg = ApplyMoves(); }
                catch (Exception e) { Logger.LogError(e); moveMsg = "Error: " + e.Message; }
            }
            GUILayout.Label(moveMsg);
        }

        string BackupSaves()
        {
            string src = Path.Combine(Application.persistentDataPath, "FW");
            if (!Directory.Exists(src)) return null;
            string dst = Path.Combine(Paths.BepInExRootPath, "PKTrainerBackups");
            dst = Path.Combine(dst, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            CopyDir(src, dst);
            return dst;
        }

        static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(src))
                CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }


        // ---- ไล่คนออกจากอาคารก่อนรื้อ: ล้างตัวชี้ "บ้าน/ห้องเรียน" ของคนในข้อมูลเกม เหมือนที่เกมทำเองตอนย้ายออก ----
        static string SetPart(string field, int idx, string val)
        {
            var p = field.Split('|');
            if (idx >= p.Length) return field;
            p[idx] = val;
            return string.Join("|", p);
        }

        int EvictFrom(PerBuildShow b)
        {
            if (b.BuildBigClass != "M") return 0;
            string id = b.buildShiliID;
            if (string.IsNullOrEmpty(id) || id == "null") return 0;
            int type;
            try { type = int.Parse(Mainload.AllBuilddata[ClassOf(b)][5]); } catch { return 0; }
            int n = 0;
            if (type == 8)            // เรือนพัก: ห้องของสมาชิก + คู่สมรส/ผู้ที่รอเข้าตระกูล
            {
                for (int i = 0; i < Mainload.Member_now.Count; i++)
                    if (Mainload.Member_now[i][3].Split('|')[1] == id) { Mainload.Member_now[i][3] = SetPart(Mainload.Member_now[i][3], 1, "null"); n++; }
                for (int j = 0; j < Mainload.Member_qu.Count; j++)
                    if (Mainload.Member_qu[j][4].Split('|')[1] == id) { Mainload.Member_qu[j][4] = SetPart(Mainload.Member_qu[j][4], 1, "null"); n++; }
            }
            if (type == 3)            // โรงเรียน/สำนักศึกษา: นักเรียน + ผู้ติดตามที่ประจำห้อง
            {
                for (int k = 0; k < Mainload.Member_now.Count; k++)
                    if (Mainload.Member_now[k][3].Split('|')[2] == id)
                    {
                        Mainload.Member_now[k][3] = SetPart(Mainload.Member_now[k][3], 2, "null");
                        Mainload.Member_now[k][32] = SetPart(SetPart(SetPart(SetPart(Mainload.Member_now[k][32], 2, "0"), 3, "0"), 4, "0"), 5, "0");
                        n++;
                    }
            }
            if (type == 3 || type == 4)   // ห้องพักของผู้ติดตาม/คนรับใช้ (MenKe)
            {
                for (int m = 0; m < Mainload.MenKe_Now.Count; m++)
                    if (Mainload.MenKe_Now[m][9].Split('|')[1] == id) { Mainload.MenKe_Now[m][9] = SetPart(Mainload.MenKe_Now[m][9], 1, "null"); n++; }
            }
            return n;
        }

        IEnumerator ClearRoutine()
        {
            clearing = true;
            string scene = Mainload.SceneID;
            var list = FindBuildings();
            list.RemoveAll(x => !selTypes.Contains(ClassOf(x)));
            if (list.Count == 0 || ChaiMethod == null)
            {
                mapMsg = L("Nothing to clear.", "ไม่มีอะไรให้เคลียร์");
                clearing = false;
                yield break;
            }

            string bk = null;
            try { bk = BackupSaves(); }
            catch (Exception e)
            {
                Logger.LogError(e);
                mapMsg = L("Backup failed - cancelled: ", "สำรองไม่สำเร็จ ยกเลิก: ") + e.Message;
                clearing = false;
                yield break;
            }

            // เรียงตามคลาส -> คอนเทนเนอร์ -> sibling index จากมากไปน้อย (ลบจากท้าย index ก่อนไม่เลื่อนตัวที่เหลือ)
            list.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(b.BuildBigClass, a.BuildBigClass);
                if (c != 0) return c;
                var pa = a.transform.parent.parent; var pb = b.transform.parent.parent;
                int ia = pa != null ? pa.GetInstanceID() : 0, ib = pb != null ? pb.GetInstanceID() : 0;
                if (ia != ib) return ib.CompareTo(ia);
                return b.transform.parent.GetSiblingIndex().CompareTo(a.transform.parent.GetSiblingIndex());
            });

            evictedCount = 0;
            int total = list.Count, done = 0, errors = 0;
            foreach (var b in list)
            {
                if (Mainload.SceneID != scene) { mapMsg = L("Scene changed - stopped.", "เปลี่ยนฉาก หยุดทำงาน"); break; }
                if (b == null) continue;
                try { if (evictFirst) evictedCount += EvictFrom(b); ChaiMethod.Invoke(b, null); }
                catch (Exception e) { errors++; Logger.LogError(e); }
                done++;
                mapMsg = L("Working... ", "กำลังรื้อ... ") + done + "/" + total;
                yield return new WaitForSecondsRealtime(0.08f);
            }

            yield return new WaitForSecondsRealtime(0.6f);
            int removed = 0;
            foreach (var b in list) if (b == null) removed++;
            RefreshCount();
            mapMsg = L("Done. Removed " + removed + " of " + total + ", skipped " + (total - removed) + (errors > 0 ? ", errors " + errors : "") + (evictedCount > 0 ? ", moved out " + evictedCount + " people" : "") + ".",
                       "เสร็จแล้ว รื้อได้ " + removed + " จาก " + total + " ข้าม " + (total - removed) + (errors > 0 ? " ผิดพลาด " + errors : "") + " หลัง" + (evictedCount > 0 ? " ไล่คนออก " + evictedCount + " คน" : ""))
                     + (bk != null ? L("  Backup: ", "  สำรองไว้ที่: ") + bk : "");
            clearing = false;
        }
    }
}
