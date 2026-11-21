using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace PKTrainer
{
    // ================== Mirror (flip) a building while placing it ==================
    // The game only has 4 prefabs per building (dir 0..3 = pure rotations), so an L-shaped building can never be
    // placed as its mirror image. Here a 5th..8th orientation is made at runtime: state = dir + 100 (mirrored).
    //   * Resources.Load("AllBuild/<set>/(Scene|BuildTip)/<id>/<state>") is intercepted for state >= 100 (or the
    //     damaged form <= -101, because the game stores a damaged building as -state-1). The normal prefab
    //     <state-100> is cloned once, reflected left<->right (positions, colliders, sprites, cell names a|b -> b|a)
    //     and cached.
    //   * The state is stored in the save exactly like a normal direction, so mirrored buildings persist.
    //   * Removing the mod while mirrored buildings exist breaks those saves -> use "Restore mirrored buildings".
    public partial class Plugin
    {
        const int MirrorOffset = 100;
        ConfigEntry<KeyCode> cfgMirrorKey;
        static string mirrorStatus = "";
        static bool mirrorOk;

        static readonly Regex MirrorPath = new Regex(@"^AllBuild/([^/]+)/(Scene|BuildTip)/([^/]+)/(-?\d+)$", RegexOptions.Compiled);
        static readonly Regex CellName = new Regex(@"^(-?\d+)\|(-?\d+)$", RegexOptions.Compiled);
        static readonly Dictionary<string, GameObject> mirrorCache = new Dictionary<string, GameObject>();
        static Transform mirrorHolder;
        static BepInEx.Logging.ManualLogSource mlog;

        void InitMirror()
        {
            mlog = Logger;
            cfgMirrorKey = Config.Bind("Build", "MirrorKey", KeyCode.F,
                "Key that flips (mirrors) the building being placed. Not M: the game uses M to open/close the build menu.");
            try
            {
                var h = new Harmony("pk.hol.trainer.mirror");
                var pre = new HarmonyMethod(typeof(Plugin).GetMethod("LoadPrefix", BindingFlags.Static | BindingFlags.NonPublic));
                foreach (var mi in typeof(Resources).GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (mi.Name != "Load" || mi.IsGenericMethodDefinition) continue;
                    var ps = mi.GetParameters();
                    if (ps.Length >= 1 && ps[0].ParameterType == typeof(string) && (ps.Length == 1 || (ps.Length == 2 && ps[1].ParameterType == typeof(Type))))
                        h.Patch(mi, prefix: pre);
                }
                h.Patch(typeof(PerBuildTip).GetMethod("SwitchDir", BindingFlags.Instance | BindingFlags.NonPublic),
                        prefix: new HarmonyMethod(typeof(Plugin).GetMethod("SwitchDirPrefix", BindingFlags.Static | BindingFlags.NonPublic)));
                mirrorOk = true;
                Logger.LogInfo("Mirror ready. Key: " + cfgMirrorKey.Value);
            }
            catch (Exception e) { Logger.LogError("Mirror patch failed: " + e); }
        }

        // ---------- placing: toggle with the key ----------
        void MirrorUpdate()
        {
            if (!mirrorOk || !Mainload.isBuildMode || !Input.GetKeyDown(cfgMirrorKey.Value)) return;
            if (GUIUtility.keyboardControl != 0) return;                       // typing in our own text box
            if (UnityEngine.Object.FindObjectOfType<PerBuildTip>() == null) return; // nothing selected to place
            string id = Mainload.BuildID_Creatselect;
            if ((Mainload.BuildIDBianYuan != null && Mainload.BuildIDBianYuan.Contains(id)) ||
                (Mainload.BuildIDAutoShape != null && Mainload.BuildIDAutoShape.Contains(id)))
                return; // fences / walls / auto-shaped pieces are symmetric or use their own state numbers
            int d;
            if (!int.TryParse(Mainload.BuildDirID_CreatSelect, out d) || d < 0) return;
            Mainload.BuildDirID_CreatSelect = (d >= MirrorOffset ? d - MirrorOffset : d + MirrorOffset).ToString();
        }

        // ---------- rotate keeps the mirror flag ----------
        static bool SwitchDirPrefix()
        {
            if (!Input.GetKeyDown(Mainload.FastKey[2])) return false;
            int d;
            if (!int.TryParse(Mainload.BuildDirID_CreatSelect, out d) || d < 0) d = 0;
            int mir = d >= MirrorOffset ? MirrorOffset : 0;
            int b = d - mir;
            Mainload.BuildDirID_CreatSelect = (mir + (b < 3 ? b + 1 : 0)).ToString();
            return false;
        }

        // ---------- Resources.Load interception ----------
        static bool LoadPrefix(string __0, ref UnityEngine.Object __result)
        {
            try
            {
                if (__0 == null || __0.Length < 12 || __0[0] != 'A') return true;
                var m = MirrorPath.Match(__0);
                if (!m.Success) return true;
                int st = int.Parse(m.Groups[4].Value);
                int baseSt;
                if (st >= MirrorOffset) baseSt = st - MirrorOffset;
                else if (st <= -(MirrorOffset + 1)) baseSt = st + MirrorOffset;
                else return true;
                var g = GetMirrored(__0, "AllBuild/" + m.Groups[1].Value + "/" + m.Groups[2].Value + "/" + m.Groups[3].Value + "/" + baseSt);
                if (g == null) return true;
                __result = g;
                return false;
            }
            catch (Exception e) { mlog.LogError("Mirror load: " + e); return true; }
        }

        static GameObject GetMirrored(string key, string basePath)
        {
            GameObject g;
            if (mirrorCache.TryGetValue(key, out g) && g != null) return g;
            var src = Resources.Load(basePath) as GameObject;
            if (src == null) { mlog.LogWarning("Mirror: base prefab missing: " + basePath); return null; }
            if (mirrorHolder == null)
            {
                var h = new GameObject("PKTrainerMirrorCache");
                h.SetActive(false); // clones below never run Awake/Start; clones made from them are active
                h.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(h);
                mirrorHolder = h.transform;
            }
            g = UnityEngine.Object.Instantiate(src, mirrorHolder, false);
            g.name = key.Substring(key.LastIndexOf('/') + 1);
            MirrorTree(g.transform);
            mirrorCache[key] = g;
            mlog.LogInfo("Mirror: built " + key);
            return g;
        }

        // Reflect every node about the root's vertical axis (screen x -> -x). The game's iso grid has screen x ~ (b - a),
        // so reflecting x is exactly swapping a and b: the cell colliders / drag cells named "a|b" become "b|a".
        static void MirrorTree(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                var rt = t as RectTransform;
                if (rt != null) { var pv = rt.pivot; pv.x = 1f - pv.x; rt.pivot = pv; } // UI rects (the red/green cells) hang off their pivot
                var p = t.localPosition; p.x = -p.x; t.localPosition = p;
                var q = t.localRotation; t.localRotation = new Quaternion(q.x, -q.y, -q.z, q.w);
            }
            foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true)) sr.flipX = !sr.flipX;
            foreach (var c in root.GetComponentsInChildren<Collider2D>(true))
            {
                var o = c.offset; c.offset = new Vector2(-o.x, o.y);
                var poly = c as PolygonCollider2D;
                if (poly != null)
                    for (int i = 0; i < poly.pathCount; i++)
                    {
                        var pts = poly.GetPath(i);
                        for (int k = 0; k < pts.Length; k++) pts[k].x = -pts[k].x;
                        Array.Reverse(pts);
                        poly.SetPath(i, pts);
                    }
                var edge = c as EdgeCollider2D;
                if (edge != null)
                {
                    var pts = edge.points;
                    for (int k = 0; k < pts.Length; k++) pts[k].x = -pts[k].x;
                    edge.points = pts;
                }
                var mm = CellName.Match(c.gameObject.name);
                if (mm.Success) c.gameObject.name = mm.Groups[2].Value + "|" + mm.Groups[1].Value;
            }
        }

        // ---------- safety valve before uninstalling the mod ----------
        static string FixState(string s)
        {
            int v;
            if (!int.TryParse(s, out v)) return s;
            if (v >= MirrorOffset) return (v - MirrorOffset).ToString();
            if (v <= -(MirrorOffset + 1)) return (v + MirrorOffset).ToString();
            return s;
        }

        string RestoreMirrored()
        {
            int n = 0;
            Action<List<List<string>>, int> fix = (list, idx) =>
            {
                if (list == null) return;
                foreach (var r in list)
                {
                    if (r == null || r.Count <= idx) continue;
                    string f = FixState(r[idx]);
                    if (f != r[idx]) { r[idx] = f; n++; }
                }
            };
            fix(Mainload.BuildInto_m, 6);
            fix(Mainload.BuildInto_z, 4);
            fix(Mainload.BuildInto_s, 5);
            fix(Mainload.BuildInto_c, 4);
            fix(Mainload.BuildInto_h, 4);
            fix(Mainload.BuildInto_l, 4);
            if (n > 0) Mainload.isUpdateScene = true;
            return L("Restored " + n + " mirrored buildings to normal. Save the game before removing the mod.",
                     "คืนอาคารที่กลับด้าน " + n + " หลังเป็นปกติแล้ว ให้เซฟเกมก่อนถอด mod");
        }

        void DrawMirror()
        {
            GUILayout.Space(14);
            GUILayout.Label(L("Mirror buildings: press [" + cfgMirrorKey.Value + "] while placing a building to flip it (Space still rotates).",
                              "กลับด้านอาคาร: กด [" + cfgMirrorKey.Value + "] ตอนวางอาคารเพื่อ mirror (Space ยังหมุนเหมือนเดิม)"));
            GUILayout.Label(L("Before REMOVING this mod, press the button below, then save.",
                              "ก่อนถอด mod ออก ให้กดปุ่มด้านล่างแล้วเซฟเกม"));
            if (GUILayout.Button(L("Restore mirrored buildings to normal", "คืนอาคารที่กลับด้านเป็นปกติ"), GUILayout.Height(30), GUILayout.Width(380)))
            {
                try { mirrorStatus = RestoreMirrored(); }
                catch (Exception e) { Logger.LogError(e); mirrorStatus = "Error: " + e.Message; }
            }
            GUILayout.Label(mirrorStatus);
        }
    }
}
