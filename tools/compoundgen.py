#!/usr/bin/env python3
"""Compact, symmetrical East-Asian family compound (scene M) - Chinese siheyuan / Korean hanok / Japanese yashiki feel.

Everything is laid out freely (no rooms are reserved for undemolishable buildings).  Fewer, better placed buildings:
~11 big halls, no small storehouses, two big storehouses, a walled box of 26 x 25 cells.

South -> north along one axis (b = 6):
    south gate -> OUTER court   gate house/servants, stables, study wing (school + library), martial ground, ancestral hall
    inner gate -> INNER court   front halls, courtyard garden with the pond, wing halls east and west, main hall at the back

    python compoundgen.py --seed 8 --out compound.es3
"""
import argparse, collections, json, math, os, random, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import layoutgen as L
import mansiongen as MG
import palacegen as PG

N4 = MG.N4
A0, A1, B0, B1 = -6, 19, -6, 18
AX = 6                                           # axis column; gates cover b = 5..7
INNER_FRONT = 8                                  # first row of the outer court (inner wall = north edge of this row)


def zone(a):
    return "inner" if a < INNER_FRONT else "outer"


def design(cat, seed, opts):
    m = PG.PM(cat, None, seed, a0=A0, a1=A1, b0=B0, b1=B1, free_all=True)
    P, rng = m.P, m.rng
    gate_cls = 170 if cat.unlocked(170) else 169
    gd = next(d for d in (0, 1, 2, 3) if cat.dims(gate_cls, d) == (1, 3))
    m.gate_cls, m.gd = gate_cls, gd
    houses, util = [], []

    # ---- walls and gates -------------------------------------------------------------------------------------
    gates = set()
    def big_gate(a):
        if m.put_corner(gate_cls, a, AX - 1, gd, "gate"):
            gates.update({(a, AX - 1), (a, AX), (a, AX + 1)})
    def door(a, b):
        if m.put(86, a, b, "gate"): gates.add((a, b))
    big_gate(A1)                                  # main gate, south
    door(A0, AX)                                  # back door, north
    door(10, B0); door(10, B1)                    # side doors (outer court)
    door(2, B0); door(2, B1)                      # side doors (inner court)
    n = 0
    for name, d, cells in (("n", 0, [(A0, b) for b in range(B0, B1 + 1)]), ("w", 1, [(a, B0) for a in range(A0, A1 + 1)]),
                           ("s", 2, [(A1, b) for b in range(B0, B1 + 1)]), ("e", 3, [(a, B1) for a in range(A0, A1 + 1)])):
        for (a, b) in cells:
            if (a, b) in gates: continue
            n += 1
            P.edge(MG.WALL_LATTICE if n % 4 == 0 else MG.WALL, a, b, d, "fence")
    # inner wall between the two courts: gate on the axis, a small door on each side
    inner_gates = set()
    if m.put_corner(gate_cls, INNER_FRONT, AX - 1, gd, "gate"):
        inner_gates |= {(INNER_FRONT, AX - 1), (INNER_FRONT, AX), (INNER_FRONT, AX + 1)}
    for b in (-2, 14):
        if m.put(86, INNER_FRONT, b, "gate"): inner_gates.add((INNER_FRONT, b))
    n = 0
    for b in range(B0 + 1, B1):
        if (INNER_FRONT, b) in inner_gates: continue
        n += 1
        P.edge(MG.WALL_LATTICE if n % 4 == 0 else MG.WALL, INNER_FRONT, b, 0, "fence")

    # ---- exact placement helper ---------------------------------------------------------------------------------------
    def put(cls, a, b, kind, d=0, lock=True):
        dims = cat.dims(cls, d)
        got = m.spaced(cls, (a, a + dims[0] - 1, b, b + dims[1] - 1), kind, dirs=(d,), gap=0, lock=lock)
        return got

    # ---- inner court: main hall at the back, front halls, wings, courtyard garden --------------------------------------------
    cap = lambda c: int(cat.AB[c][3].split("|")[0])
    big = [139, 138, 137]                          # 2x3, three people each (best levels first)
    small = [16, 15, 14, 13, 12, 11]               # 2x2, two people each
    pick = lambda pool, i: next(c for c in pool[i % len(pool):] + pool[:i % len(pool)] if cat.unlocked(c))
    # main hall on the axis, two companions beside it (symmetrical)
    for k, (b, c) in enumerate(((AX - 1, big[0]), (1, big[1]), (9, big[1]))):
        got = put(c, -5, b, "house")
        if got: houses.append((c, got))
    # front halls flanking the way to the inner gate
    for b in (1, 9):
        got = put(big[2], 5, b, "house")
        if got: houses.append((big[2], got))
    # wing halls, three per side
    for k, a in enumerate((-2, 1, 4)):
        for side_b in (-5, 16):
            c = small[(k + (0 if side_b < 0 else 1)) % len(small)]
            got = put(c, a, side_b, "house")
            if got: houses.append((c, got))
    # the courtyard pond (small) with a rock island, pavilion on the shore
    m.pond(1.5, 6.0, 2.7, 3.7, island_ok=True)

    # ---- outer court ------------------------------------------------------------------------------------------------------
    study = []                                     # school + library west, ancestral hall east, martial ground centre-east
    for cls, bounds, near, kind in ((5, (9, 14, -5, -2), (10, -5), "work"), (8, (12, 17, -5, -2), (13, -5), "work")):
        got = m.spaced(cls, bounds, kind, dirs=(0,), gap=1, near=near)
        if got: study.append((cls, got))
    got = m.spaced(0, (9, 12, 14, 17), "work", dirs=(0,), gap=1, near=(10, 15), lock=False)   # ancestral hall
    if got: study.append((0, got))
    got = m.spaced(6, (11, 17, 9, 13), "work", dirs=(0,), gap=1, near=(14, 11))              # martial ground
    if got: study.append((6, got))
    for cls, bounds, near in ((182, (16, 17, -5, -3), (16, -5)), (182, (14, 15, 15, 17), (14, 16)), (9, (18, 18, -2, 3), (18, -2))):
        got = m.spaced(cls, bounds, "work", dirs=(0, 1), gap=1, near=near)                 # storehouses, stables
        if got: study.append((cls, got))
    side = 148 if cat.unlocked(148) else 147
    d_side = next(d for d in (0, 1, 2, 3) if cat.dims(side, d) == (3, 1))
    n_side = 0
    for bounds, lim in (((15, 18, -1, 3), 2), ((16, 18, 11, 17), 1)):                         # servants' quarters beside the gate (3 in all)
        k = 0
        while k < lim and m.spaced(side, bounds, "house", dirs=(d_side,), gap=0, near=(bounds[0], bounds[2])):
            k += 1; n_side += 1
    houses.append((side, n_side))
    m.cap_total = sum(cap(c) for c, _ in houses if c in big + small)

    # ---- roads ------------------------------------------------------------------------------------------------------------------
    m.path(m.line(A1 - 1, AX, -4, AX))                                         # the axis, gate to main hall
    for a_ in (7, -3, 9, 17):                                                  # lanes across each court
        m.path(m.line(a_, B0 + 1, a_, B1 - 1))
    for b_ in (-3, 15):                                                        # lanes along the wings
        m.path(m.line(-4, b_, 7, b_))
    m.path(sorted(m.ring_path()))                                              # pond shore walk

    # ---- walkways: every building gets a link to the roads ----------------------------------------------------------------
    blds = []
    for pc in P.pieces:
        if pc["kind"] in ("house", "work") and pc["cls"] not in L.EDGE_CLASSES:
            blds.append([(pc["a"] + dx, pc["b"] + dy) for dx, dy in cat.cells(pc["cls"], pc["d"])])
    pathcells = {c for c, k in P.occ.items() if k == "path"}
    dist = {c: 0 for c in pathcells}
    dq = collections.deque(pathcells)
    while dq:
        ca_, cb_ = dq.popleft()
        for x, y in N4:
            c = (ca_ + x, cb_ + y)
            if c not in dist and m.free(*c):
                dist[c] = dist[(ca_, cb_)] + 1; dq.append(c)
    linked = 0
    for cells in blds:
        cs = set(cells)
        nb = [(a_ + x, b_ + y) for a_, b_ in cells for x, y in N4 if (a_ + x, b_ + y) not in cs]
        if any(c in pathcells for c in nb): continue
        cand = [c for c in nb if c in dist]
        if not cand: continue
        cur = min(cand, key=lambda c: dist[c])
        if dist[cur] > 14: continue
        while dist.get(cur, 0) > 0:
            if m.free(*cur): m.path([cur]); pathcells.add(cur)
            nxt = [(cur[0] + x, cur[1] + y) for x, y in N4 if dist.get((cur[0] + x, cur[1] + y), 99) == dist[cur] - 1]
            if not nxt: break
            cur = nxt[0]
        linked += 1
    m.linked = linked

    # ---- garden furniture: pavilion, rocks (symmetrical pairs), lanterns along the axis -------------------------------------------
    m.fit_in(23, (2, 4, 10, 12), "decor", dirs=(0,))                           # pavilion on the east shore
    for cls, a_, b_ in ((181, 3, 0), (181, 3, 12), (180, 10, 3), (180, 10, 9), (179, 14, 6)):
        m.fit_in(cls, (a_ - 1, a_ + 1, b_ - 1, b_ + 1), "decor", dirs=(0, 1))
    for a_ in range(-2, 18, 3):
        for b_ in (AX - 2, AX + 2):
            if m.free(a_, b_): m.put(rng.choice([223, 225]), a_, b_, "decor")

    # ---- planting by court (inner = lush garden, outer = sparse and formal) ---------------------------------------------------------
    shrubs = [s for s in MG.SHRUBS if cat.unlocked(s)]
    by_zone = {"outer": ([37, 37, 35, 76], 0.18), "inner": ([36, 80, 27, 28, 76, 30], 0.42)}
    cells = [c for c in sorted(m.limit) if m.free(*c)]
    rng.shuffle(cells)
    def near(kinds, a, b, r=1):
        return any(P.occ.get((a + x, b + y)) in kinds for x in range(-r, r + 1) for y in range(-r, r + 1))
    quota = {z: 0 for z in by_zone}
    budget = {z: int(by_zone[z][1] * sum(1 for c in cells if zone(c[0]) == z)) for z in by_zone}
    for (a, b) in cells:
        z = zone(a)
        if quota[z] >= budget[z] or not m.free(a, b): continue
        if near({"gate"}, a, b, 2) or near({"house", "work"}, a, b, 1): continue
        trees = [t for t in by_zone[z][0] if cat.unlocked(t)]
        wat = near({"water"}, a, b, 2)
        if near({"tree"}, a, b, 1):
            if rng.random() < 0.5 and not near({"path"}, a, b, 0) and m.put(rng.choice(shrubs), a, b, "flower"): quota[z] += 1
            continue
        pick_ = (a * 5 + b * 3) % 10
        if pick_ < 4: cls = rng.choice([27, 36, 80] if wat and z == "inner" else trees)
        elif pick_ < 8: cls = rng.choice(shrubs)
        else: cls = rng.choice([179, 25]) if (z == "inner" and rng.random() < .3) else rng.choice(shrubs)
        if m.put(cls, a, b, "flower" if cls in MG.SHRUBS else "tree"): quota[z] += 1
    return P, m, houses, study


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default=L.DEFAULT_DATA)
    ap.add_argument("--seed", type=int, default=8)
    ap.add_argument("--family", type=int, default=12)
    ap.add_argument("--out", default="compound.es3")
    a = ap.parse_args()
    cat = L.Catalog(a.data, a.family, allowed=MG.ALLOWED | {0})
    P, m, houses, study = design(cat, a.seed, {})
    bad = L.validate(P)
    st = MG.write_m(P, a.out)
    png = os.path.splitext(a.out)[0] + "_iso.png"
    L.KIND_COLOR.update({"gate": "#ee3333", "fence": "#222222"})
    L.render_png(P, png)
    print(MG.ascii_map(P, m))
    print("pieces", st["pieces"], "| cost", format(st["cost"], ","), "coin,", st["gold"], "gold | validation problems:", len(bad))
    for b in bad[:8]: print("  ", b[0], b[1])
    cnt = collections.Counter(p["cls"] for p in P.pieces if p["kind"] in ("house", "work"))
    print("buildings:", {cat.name(c) + "#" + str(c): n for c, n in sorted(cnt.items())}, "| big halls", sum(n for c, n in cnt.items() if c in (11,12,13,14,15,16,137,138,139)))
    print("walkways linking", m.linked, "| household capacity", m.cap_total)
    print("wrote", a.out, png)


if __name__ == "__main__":
    main()
