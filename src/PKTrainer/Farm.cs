using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace PKTrainer
{
    // ==========================================================================
    //  ฟาร์ม: เก็บผลผลิตอัตโนมัติ + ผลผลิตออกทันที (ใช้ได้เมื่ออยู่ในฉากฟาร์ม "Z|..")
    //  อาคารฟาร์มทุกหลังมีคอมโพเนนต์ในฉาก (ลูกของ PerBuildScene) ข้อมูลอยู่ที่ Mainload.BuildInto_z[ลำดับพี่น้องของพ่อ]:
    //    [5] ปัจจัย  [6] เวลาปลูก "ปี|เดือน"  [7] ผลผลิตที่สะสมรอเก็บ  [8] เวลาคิดผลผลิตล่าสุด  [11] ผลผลิตชนิดที่สองของสัตว์
    //  - เก็บ: เรียกเมธอดเก็บของเกมเอง (GetPropBT / GetPropBTA / GetPropBTB) จึงเข้าคลังและนับพื้นที่คลังตามกฎเดิม
    //  - ผลผลิตทันที: พืช/ต้นไม้ = บวกผลผลิตหนึ่งรอบด้วยสูตรเดียวกับเกม (FormulaData.NongZZhongZhiOut)
    //                  สัตว์ = เลื่อนเวลา [8] ถอยหลัง 12 เดือน/รอบ แล้วเรียก UpdateShow ของเกมให้คิดผลผลิตเอง
    //                  ต้นไม้ = ย้อนเวลาปลูกให้โตเต็มวัยก่อน
    // ==========================================================================
    public partial class Plugin
    {
        const BindingFlags NP = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        bool farmAuto, farmAutoProduce;
        string farmCycles = "1", farmEvery = "3";
        float farmNext;
        string farmMsg = "";
        int fCrops, fTrees, fAnimals;

        static bool InFarm() { return Mainload.SceneID != null && Mainload.SceneID.StartsWith("Z|"); }

        static object Call(object o, string name)
        {
            var m = o.GetType().GetMethod(name, NP);
            return m != null ? m.Invoke(o, null) : null;
        }

        static int ZIndex(Component c) { return c.transform.parent.GetSiblingIndex(); }

        static int NowMonths() { return Mainload.Time_now[0] * 12 + (Mainload.Time_now[1] - 1); }

        static string MonthsToStr(int total)
        {
            if (total < 12) total = 12;
            return (total / 12) + "|" + (total % 12 + 1);
        }

        // อัปเดตตัวแปรภายใน "Prop_Out" ของคอมโพเนนต์ให้ตรงกับข้อมูลที่เพิ่งเปลี่ยน (ปุ่มเก็บของเกมอ่านค่าจากตัวนี้)
        static void SyncOut(Component c, int zi)
        {
            var f = c.GetType().GetField("Prop_Out", NP);
            if (f != null) { var l = f.GetValue(c) as List<string>; if (l != null && l.Count > 1) l[1] = Mainload.BuildInto_z[zi][7]; }
            var fa = c.GetType().GetField("Prop_OutA", NP);
            if (fa != null) { var l = fa.GetValue(c) as List<string>; if (l != null && l.Count > 1) l[1] = Mainload.BuildInto_z[zi][7]; }
            var fb = c.GetType().GetField("Prop_OutB", NP);
            if (fb != null) { var l = fb.GetValue(c) as List<string>; if (l != null && l.Count > 1) l[1] = Mainload.BuildInto_z[zi][11]; }
        }

        static int CropYield(int zi)
        {
            var z = Mainload.BuildInto_z[zi];
            int cls = int.Parse(z[1]);
            string si = Mainload.SceneID;
            int scene = int.Parse(si.Split('|')[1]), nz = int.Parse(si.Split('|')[2]);
            int outInit = int.Parse(Mainload.AllBuilddata[cls][3].Split('|')[2].Split('@')[0].Split('~')[1]);
            return FormulaData.NongZZhongZhiOut(outInit, float.Parse(z[5]), int.Parse(Mainload.NongZ_now[scene][nz][3]));
        }

        // ---------------- ผลผลิตออกทันที ----------------
        int ProduceCrops()
        {
            int n = 0;
            var comps = new List<Component>();
            comps.AddRange(UnityEngine.Object.FindObjectsOfType<CaiTianShow>());   // ผัก
            comps.AddRange(UnityEngine.Object.FindObjectsOfType<TianShow>());      // นาข้าว ฝ้าย
            foreach (var c in comps)
            {
                try
                {
                    int zi = ZIndex(c);
                    var z = Mainload.BuildInto_z[zi];
                    z[7] = (int.Parse(z[7]) + CropYield(zi)).ToString();      // เวลานับรอบ [8] ไม่แตะ: รอบปกติของเกมเดินต่อเหมือนเดิม
                    SyncOut(c, zi);
                    Call(c, "UpdateShow");
                    n++;
                }
                catch (Exception e) { Logger.LogWarning("crop: " + e.Message); }
            }
            return n;
        }

        int ProduceTrees()
        {
            int n = 0;
            foreach (var c in UnityEngine.Object.FindObjectsOfType<FruitStateShow>())
            {
                try
                {
                    int zi = ZIndex(c);
                    var z = Mainload.BuildInto_z[zi];
                    int cls = int.Parse(z[1]);
                    int grow = int.Parse(Mainload.AllBuilddata[cls][3].Split('|')[0]);
                    // โตเต็มวัยทันที: ย้อนเวลาปลูกถอยไป ZhangChengTime เดือน
                    string[] pt = z[6].Split('|');
                    int planted = int.Parse(pt[0]) * 12 + (int.Parse(pt[1]) - 1);
                    if (NowMonths() - planted < grow)
                    {
                        z[6] = MonthsToStr(NowMonths() - grow);
                        var f = c.GetType().GetField("PlantTime", NP);
                        if (f != null) f.SetValue(c, z[6].Split('|'));
                    }
                    z[7] = (int.Parse(z[7]) + CropYield(zi)).ToString();      // เวลานับรอบ [8] ไม่แตะ: รอบปกติของเกมเดินต่อเหมือนเดิม
                    SyncOut(c, zi);
                    Call(c, "UpdateShow");
                    n++;
                }
                catch (Exception e) { Logger.LogWarning("tree: " + e.Message); }
            }
            return n;
        }

        static int OutInit(int cls, int which)
        {
            var parts = Mainload.AllBuilddata[cls][3].Split('|')[2].Split('@');
            return int.Parse(parts[Math.Min(which, parts.Length - 1)].Split('~')[1]);
        }

        // สัตว์/บ่อปลา/โรงงาน: บวกผลผลิตด้วยสูตรของเกมโดยตรง (UpdateShow ของเกมไม่คิดผลผลิตให้ที่ดินที่ซื้อมายังไม่ครบปี)
        int ProduceAnimals(int cycles)
        {
            int n = 0;
            string si = Mainload.SceneID;
            int scene = int.Parse(si.Split('|')[1]), nz = int.Parse(si.Split('|')[2]);
            int dizu = int.Parse(Mainload.NongZ_now[scene][nz][3]);
            foreach (var c in UnityEngine.Object.FindObjectsOfType<AnimalRoomShow>())
            {
                try
                {
                    int zi = ZIndex(c);
                    var z = Mainload.BuildInto_z[zi];
                    int cls = int.Parse(z[1]), lv = int.Parse(z[2]);
                    z[7] = (int.Parse(z[7]) + cycles * FormulaData.NongZAniamalOut(OutInit(cls, 0), dizu, lv)).ToString();
                    z[11] = (int.Parse(z[11]) + cycles * FormulaData.NongZAniamalOut(OutInit(cls, 1), dizu, lv)).ToString();
                    SyncOut(c, zi);
                    n++;
                }
                catch (Exception e) { Logger.LogWarning("animal: " + e.Message); }
            }
            foreach (var c in UnityEngine.Object.FindObjectsOfType<YuTangShow>())
            {
                try
                {
                    int zi = ZIndex(c);
                    var z = Mainload.BuildInto_z[zi];
                    int cls = int.Parse(z[1]), lv = int.Parse(z[2]);
                    z[7] = (int.Parse(z[7]) + cycles * FormulaData.NongZAniamalOut(OutInit(cls, 0), dizu, lv)).ToString();
                    SyncOut(c, zi);
                    n++;
                }
                catch (Exception e) { Logger.LogWarning("fish: " + e.Message); }
            }
            foreach (var c in UnityEngine.Object.FindObjectsOfType<ZuoFangShow>())
            {
                try
                {
                    int zi = ZIndex(c);
                    var z = Mainload.BuildInto_z[zi];
                    int cls = int.Parse(z[1]), lv = int.Parse(z[2]);
                    z[7] = (int.Parse(z[7]) + cycles * FormulaData.NongZZuoFangOut(OutInit(cls, 0), dizu, lv)).ToString();
                    SyncOut(c, zi);
                    n++;
                }
                catch (Exception e) { Logger.LogWarning("workshop: " + e.Message); }
            }
            return n;
        }

        // ---------------- เก็บผลผลิต ----------------
        int CollectAll()
        {
            int n = 0;
            var singles = new List<Component>();
            singles.AddRange(UnityEngine.Object.FindObjectsOfType<CaiTianShow>());
            singles.AddRange(UnityEngine.Object.FindObjectsOfType<TianShow>());
            singles.AddRange(UnityEngine.Object.FindObjectsOfType<FruitStateShow>());
            singles.AddRange(UnityEngine.Object.FindObjectsOfType<YuTangShow>());
            singles.AddRange(UnityEngine.Object.FindObjectsOfType<ZuoFangShow>());
            foreach (var c in singles)
                try { int zi = ZIndex(c); if (int.Parse(Mainload.BuildInto_z[zi][7]) > 0) { SyncOut(c, zi); Call(c, "GetPropBT"); n++; } } catch { }
            foreach (var c in UnityEngine.Object.FindObjectsOfType<AnimalRoomShow>())
                try
                {
                    int zi = ZIndex(c);
                    SyncOut(c, zi);
                    if (int.Parse(Mainload.BuildInto_z[zi][7]) > 0) { Call(c, "GetPropBTA"); n++; }
                    if (int.Parse(Mainload.BuildInto_z[zi][11]) > 0) { Call(c, "GetPropBTB"); n++; }
                }
                catch { }
            return n;
        }

        void CountFarm()
        {
            fCrops = UnityEngine.Object.FindObjectsOfType<CaiTianShow>().Length + UnityEngine.Object.FindObjectsOfType<TianShow>().Length;
            fTrees = UnityEngine.Object.FindObjectsOfType<FruitStateShow>().Length;
            fAnimals = UnityEngine.Object.FindObjectsOfType<AnimalRoomShow>().Length + UnityEngine.Object.FindObjectsOfType<YuTangShow>().Length + UnityEngine.Object.FindObjectsOfType<ZuoFangShow>().Length;
        }

        // เรียกจาก Update(): โหมดอัตโนมัติ
        void FarmTick()
        {
            if (!(farmAuto || farmAutoProduce) || !InFarm() || Mainload.BuildInto_z == null) return;
            if (Time.unscaledTime < farmNext) return;
            float every;
            if (!float.TryParse(farmEvery, NumberStyles.Float, CultureInfo.InvariantCulture, out every)) every = 3f;
            farmNext = Time.unscaledTime + Mathf.Max(1f, every);
            try
            {
                if (farmAutoProduce)
                {
                    int cyc; if (!int.TryParse(farmCycles, out cyc)) cyc = 1;
                    ProduceCrops(); ProduceTrees(); ProduceAnimals(Math.Max(1, Math.Min(10, cyc)));
                }
                CollectAll();
            }
            catch (Exception e) { Logger.LogWarning("farm tick: " + e.Message); }
        }

        void DrawFarm()
        {
            GUILayout.Space(6);
            if (!InFarm())
            {
                GUILayout.Label(L("Go into one of your farms first (this page works only inside a farm map).", "ต้องเข้าไปในแผนที่ฟาร์มก่อน (หน้านี้ใช้ได้เฉพาะในฉากฟาร์ม)"));
                GUILayout.Label(L("Current scene: ", "ฉากปัจจุบัน: ") + Mainload.SceneID, stCap);
                return;
            }
            if (Event.current.type == EventType.Layout && Time.frameCount % 60 == 0) CountFarm();
            GUILayout.Label(L("In this farm: crops ", "ในฟาร์มนี้: แปลงพืช ") + fCrops + L(", trees ", "  ต้นไม้ ") + fTrees + L(", animal pens ", "  คอกสัตว์ ") + fAnimals);

            GUILayout.BeginHorizontal();
            GUILayout.Label(L("Animal cycles per press", "รอบผลผลิตสัตว์ต่อการกด"), GUILayout.Width(230));
            farmCycles = GUILayout.TextField(farmCycles, GUILayout.Width(40));
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.Label(L("Press once - produce now", "กดครั้งเดียว ผลผลิตเสร็จทันที"), stSec);
            int cyc2; if (!int.TryParse(farmCycles, out cyc2)) cyc2 = 1;
            cyc2 = Math.Max(1, Math.Min(10, cyc2));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L("Crops (rice, cotton, veg): now", "พืช (ข้าว ฝ้าย ผัก): ออกผลทันที"), GUILayout.Height(34))) { farmMsg = L("Crops: ", "พืช: ") + ProduceCrops(); status = farmMsg; }
            if (GUILayout.Button(L("Trees: grow + fruit now", "ต้นไม้: โต+ออกผลทันที"), GUILayout.Height(34))) { farmMsg = L("Trees: ", "ต้นไม้: ") + ProduceTrees(); status = farmMsg; }
            if (GUILayout.Button(L("Animals, fish, workshops: now", "สัตว์/บ่อปลา/โรงงาน: ออกผลทันที"), GUILayout.Height(34))) { farmMsg = L("Animal pens: ", "คอกสัตว์: ") + ProduceAnimals(cyc2); status = farmMsg; }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L("Produce EVERYTHING now", "ออกผลผลิตทั้งหมดทันที"), GUILayout.Height(34)))
            { int a = ProduceCrops(), b = ProduceTrees(), d = ProduceAnimals(cyc2); farmMsg = L("Done: ", "สำเร็จ: ") + (a + b + d); status = farmMsg; }
            if (GUILayout.Button(L("Collect everything now", "เก็บทั้งหมดเข้าคลังตอนนี้"), GUILayout.Height(34)))
            { farmMsg = L("Collected ", "เก็บแล้ว ") + CollectAll() + L(" batches", " ชุด"); status = farmMsg; }
            GUILayout.EndHorizontal();
            if (GUILayout.Button(L("Produce everything AND collect it into the storehouse", "ออกผลผลิตทั้งหมด + เก็บเข้าคลังในปุ่มเดียว"), GUILayout.Height(40)))
            { int a = ProduceCrops(), b = ProduceTrees(), d = ProduceAnimals(cyc2); int k = CollectAll(); farmMsg = L("Produced ", "ผลิต ") + (a + b + d) + L(", collected ", " เก็บ ") + k; status = farmMsg; }
            GUILayout.Label(L("The game's normal harvest timing is left untouched, so it keeps counting as before after you collect.",
                              "เวลานับรอบปกติของเกมไม่ถูกแตะ หลังเก็บแล้วเกมจะนับรอบผลผลิตต่อเหมือนเดิม"), stCap);
            GUILayout.Label(farmMsg, stCap);
        }
    }
}
