"""Grid-symmetric 4-acre farm blueprint (32x32), four-fold symmetric like a chessboard of walled blocks.

  python gridgen.py --out C:/Users/boss8/Desktop/PKLayouts/grid_sym.es3 [--reserve-works] [--no-orchard-half]

Layout (local quadrant = top-left 15x15, mirrored left/right and top/bottom):
  corner block   : farmhouse + workshop slot + garden
  side block A   : crop plots inside a fenced yard (gate to the main road)
  side block B   : animal pens inside a fenced yard (gate to the main road)
  centre block   : fruit orchard, every species in every quadrant
  centre         : pond of 4 fish ponds with lilies; two-wide main roads N-S and E-W, one-wide cross roads
Only buildings of the farm menu are used.
"""
import argparse, collections, os, sys, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import layoutgen as L

N = 32


def build(cat, args):
    """One mirror axis (left/right of the N-S spine road at i=15,16); five fenced bands, no cross roads:
         j 0-5  crop plots + fish ponds        j 13-19 farmhouses (centre) + workshops
         j 7-11 animal pens                    j 21-27 orchard      j 29-31 free ground for later
       hedge rows (shrubs, no road) separate the bands."""
    P = L.Plot(cat, -9, N)
    A = P.A
    ok = cat.unlocked
    houses = [c for c in (100, 99, 42, 41) if ok(c)]
    house = args.house if args.house else houses[0]
    crops = [67, 133, 50]
    pen_kinds = [44, 46, 66, 46]
    fruit = [69, 74, 71, 72, 70, 75, 73, 68]
    shrubs = [77, 38, 78, 79, 80, 109, 110]
    fence_out = 106 if ok(106) else 105
    fence_pen, fence_field = 103, 105
    gate = 108 if ok(108) else 107
    quads = [(0, 0), (1, 0)]

    def mir(i, j, w, h, mi, mj=0):
        return (N - 1 - (i + w - 1) if mi else i), j

    def side(s, mi):
        return 2 - s if (mi and s in (0, 2)) else s

    def put(cls, i, j, d, kind, only=None):
        n = 0
        w, h = cat.dims(cls, d)
        for (mi, mj) in quads:
            if only and (mi, mj) not in only: continue
            ii, jj = mir(i, j, w, h, mi)
            dd = (d + 2 * mi) % 4 if kind in ("house", "work") else d
            if P.place_corner(cls, A(ii), A(jj), dd, kind):
                n += 1
        return n

    def reserve(i, j, w, h):
        for (mi, mj) in quads:
            ii, jj = mir(i, j, w, h, mi)
            for da in range(w):
                for db in range(h):
                    P.occ[(A(ii) + da, A(jj) + db)] = "reserve"; P.reserved.add((A(ii) + da, A(jj) + db))

    # ---- outer fence, gates on the spine at the north and south ------------------------------------------
    for b in range(P.lo, P.hi + 1):
        P.edge(fence_out, P.lo, b, 0, "fence"); P.edge(fence_out, P.hi, b, 2, "fence")
    for a_ in range(P.lo, P.hi + 1):
        P.edge(fence_out, a_, P.lo, 1, "fence"); P.edge(fence_out, a_, P.hi, 3, "fence")
    for r_ in (5, 21, 27):                                       # side gates for the lane roads
        for (s, a_, b) in ((0, P.lo, A(r_)), (2, P.hi, A(r_))):
            P.remove_edge_cell(a_, b, s); P.edge(gate, a_, b, s, "gate")
    for cc in (15, 16):
        for (s, a_, b) in ((1, A(cc), P.lo), (3, A(cc), P.hi), (0, P.lo, A(cc)), (2, P.hi, A(cc))):
            P.remove_edge_cell(a_, b, s); P.edge(gate, a_, b, s, "gate")

    # ---- roads: the spine (north and south of the centre) + one roundabout ring around the central pond ---------
    island = {6, 7, 28, 29}                                      # rows of the two small roundabout islands
    for k in list(range(0, 11)) + list(range(21, N)):
        for c in (15, 16):
            if k not in island:
                P.place(82, A(c), A(k), 0, "path")
    for (r0, r1) in ((5, 8), (27, 30)):                          # small roundabouts on the spine
        for i in range(14, 18):
            for j in range(r0, r1 + 1):
                if i in (14, 17) or j in (r0, r1):
                    P.place(82, A(i), A(j), 0, "path")
    for (j, i1) in ((5, 13), (27, 13), (21, 14), (11, 10)):      # one lane road per band, both sides of the spine
        for i in range(0, i1 + 1):
            for (mi, mj) in quads:
                a_, b = mir(i, j, 1, 1, mi); P.place(82, A(a_), A(b), 0, "path")
    for k in list(range(0, 11)) + list(range(21, N)):          # second axis: east-west road, 2 wide
        for c in (15, 16):
            P.place(82, A(k), A(c), 0, "path")
    for i in range(11, 21):
        for j in range(11, 21):
            if i in (11, 20) or j in (11, 20):
                P.place(82, A(i), A(j), 0, "path")

    # ---- centre garden inside the ring: 4 fish ponds with lilies, pavilions at the corners, flower frame -------------
    for jj in (14, 16):
        put(51, 14, jj, 0, "water")
        for (mi, mj) in quads:
            for (li, lj, c) in ((14, jj, 130), (15, jj + 1, 96), (15, jj, 129), (14, jj + 1, 129)):
                a_, b = mir(li, lj, 1, 1, mi); P.stack(c, A(a_), A(b))
    for (i, j) in ((12, 12), (12, 19)):
        put(24, i, j, 0, "decor")                          # straw pavilions at the four corners of the island
    for n_, i in enumerate((13, 14, 15)):
        put(shrubs[n_ % 7], i, 12, 0, "flower"); put(shrubs[(n_ + 3) % 7], i, 19, 0, "flower")
    for n_, j in enumerate((14, 15, 16, 17)):
        put(shrubs[(n_ + 1) % 7], 12, j, 0, "flower")
    put(31, 13, 13, 0, "tree"); put(31, 13, 18, 0, "tree")
    put(30, 12, 13, 0, "flower") if False else None

    # island gardens of the two small roundabouts (each island is itself symmetric)
    put(35, 15, 6, 0, "tree"); put(80, 15, 7, 0, "flower")
    put(28, 15, 28, 0, "tree"); put(78, 15, 29, 0, "flower")

    # ---- band 1: crop plots + fish ponds (j 0-5) ------------------------------------------------------------
    k = 0
    for j in (0, 3):
        for i in (0, 3, 6, 9, 12):
            if i == 12:
                put(51, i, j, 0, "water")
                for (mi, mj) in quads:
                    for (li, lj, c) in ((i, j, 130), (i + 1, j + 1, 96), (i + 1, j, 129), (i, j + 1, 129)):
                        a_, b = mir(li, lj, 1, 1, mi); P.stack(c, A(a_), A(b))
            else:
                put(crops[k % 3], i, j, 0, "field"); k += 1

    # ---- band 2: animal pens (j 6-10): two rows of three 3-long pens + one 2-long pen ----------------------------
    short = {44: 43, 46: 45, 66: 65}
    for r, j in enumerate((7, 9)):
        for q, i in enumerate((0, 4, 8)):
            put(pen_kinds[(q + r) % 3], i, j, 1, "pen")
        put(short[pen_kinds[(r + 1) % 3]], 12, j, 1, "pen")

    # ---- band 3: the four farmhouses face the roundabout; workshop + free slot behind them (j 12-19) -------------
    put(house, 8, 13, 1, "house")
    put(house, 8, 17, 1, "house")
    ws = [c for c in (156, 155) if ok(c)]
    if args.reserve_works:
        reserve(3, 13, 2, 2); reserve(3, 17, 2, 2)
    else:
        put(ws[0], 3, 13, 0, "work")
        reserve(3, 17, 2, 2)

    # ---- band 4: orchard (j 22-26) ---------------------------------------------------------------------------
    for r, j in enumerate((22, 24, 26)):
        for i in range(15):
            put(fruit[((i // 2) + 3 * r) % 8], i, j, 0, "tree")

    # ---- band 5: free ground behind the lower roundabout (j 28-31) ---------------------------------------------
    reserve(0, 28, 14, 4)

    # ---- fences with a gate toward the nearest road -----------------------------------------------------------
    def fence_rect(rect, cls, gate_j, gate_side=2, gate_i=None):
        i0, i1, j0, j1 = rect
        for (mi, mj) in quads:
            for i in range(i0, i1 + 1):
                for (j, s) in ((j0, 1), (j1, 3)):
                    a_, b = mir(i, j, 1, 1, mi); P.edge(cls, A(a_), A(b), s, "fence")
            for j in range(j0, j1 + 1):
                for (i, s) in ((i0, 0), (i1, 2)):
                    a_, b = mir(i, j, 1, 1, mi); P.edge(cls, A(a_), A(b), side(s, mi), "fence")
            gi = i1 if gate_side == 2 else gate_i
            gj = gate_j if gate_side == 2 else (j1 if gate_side == 3 else j0)
            a_, b = mir(gi, gj, 1, 1, mi); s = side(gate_side, mi)
            P.remove_edge_cell(A(a_), A(b), s); P.edge(gate, A(a_), A(b), s, "gate")

    fence_rect((0, 14, 0, 4), fence_field, 0, 3, 7)          # plots, gate onto the lane at j=5
    fence_rect((0, 13, 6, 10), fence_pen, 0, 1, 7)           # pens, gate onto the same lane
    fence_rect((0, 10, 12, 14), fence_pen, 0, 3, 5)      # north house yard, gate onto the east-west road
    fence_rect((0, 10, 17, 19), fence_pen, 0, 1, 5)      # south house yard, gate onto the east-west road
    fence_rect((0, 14, 22, 26), fence_field, 0, 3, 7)        # orchard, gate onto the lane at j=27

    # ---- decoration (mirrored): hedge rows, house gardens, pines at the ring, willows by the spine gates -------------
    for n_, j in enumerate((6, 10, 12)):
        for i in range(0, 14, 2):
            put(shrubs[(i // 2 + n_) % 7], i, j, 0, "flower")
    for n_, i in enumerate((6, 7, 8, 9)):
        put(shrubs[(n_ + 1) % 7], i, 12, 0, "flower"); put(shrubs[(n_ + 4) % 7], i, 19, 0, "flower")
    for n_, j in enumerate((13, 14, 15, 17, 18, 19)):
        put(shrubs[(j + 2) % 7], 10, j, 0, "flower") if j not in (16,) else None
    put(31, 6, 15, 0, "tree"); put(30, 6, 17, 0, "tree"); put(28, 1, 15, 0, "tree"); put(28, 1, 18, 0, "tree")
    put(31, 1, 12, 0, "tree"); put(30, 1, 19, 0, "tree")
    for (i, j) in ((10, 10), (10, 21), (13, 4), (13, 9), (13, 26), (13, 31)):
        put(35, i, j, 0, "tree")                       # pines around the roundabout and where hedges meet the spine
    for (i, j) in ((0, 0), (0, 31)):
        put(35, i, j, 0, "tree")
    for j in (4, 9):
        put(27, 14, j, 0, "tree")                      # willows beside the spine gates
    return P


def need_exact(cat, P):
    prod = collections.defaultdict(float)
    for p in P.pieces:
        if p["cls"] in L.EDGE_CLASSES or p["kind"] == "lily":
            continue
        t = cat.AB[p["cls"]][3].split("|")
        if len(t) < 4 or "@" not in t[3]:
            continue
        k, req = t[3].split("@")[:2]
        k = int(k); req = float(req)
        if req <= 0:
            continue
        for q in t[2].split("@"):
            if "~" not in q: continue
            pid = int(q.split("~")[0])
            if pid >= 10000 or (k == 1 and pid in (1, 284)): continue
            prod[(k, pid)] += req
    need = collections.Counter()
    for (k, pid), v in prod.items():
        need[k] += v
    return need


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="grid_sym.es3")
    ap.add_argument("--data", default=L.DEFAULT_DATA)
    ap.add_argument("--family", type=int, default=12)
    ap.add_argument("--house", type=int, default=None, help="farmhouse class id (default: best unlocked 2x4 house)")
    ap.add_argument("--reserve-works", action="store_true", help="keep all four workshop slots free")
    a = ap.parse_args()
    cat = L.Catalog(a.data, a.family)
    P = build(cat, a)
    bad = L.validate(P)
    st = L.write_es3(P, a.out)
    png = os.path.splitext(a.out)[0] + "_iso.png"
    L.render_png(P, png)
    kinds = collections.Counter(p["kind"] for p in P.pieces if p["cls"] not in L.EDGE_CLASSES)
    cnt = collections.Counter(p["cls"] for p in P.pieces if p["kind"] in ("house", "field", "pen", "work", "water", "tree"))
    nd = need_exact(cat, P)
    names = {0: "plant", 1: "husbandry", 2: "craft"}
    off = [c for c in set(p["cls"] for p in P.pieces) if c not in L.FARM_CLASSES]
    print("pieces", st["pieces"], "| overlaps/problems", len(bad), "| not in farm menu:", off or "none", "| locked:", sum(st["locked"].values()))
    print("farmers max %d | env %d | safety %d | convenience %d | cost %s" % (st["farmers_max"], st["env"], st["saf"], st["conv"], format(st["cost"], ",")))
    print("need", {names[k]: round(v, 2) for k, v in sorted(nd.items())}, "total", round(sum(nd.values()), 2))
    print("contents", dict(kinds), "| reserved cells", len(P.reserved))
    print("wrote", a.out, png)


if __name__ == "__main__":
    main()
