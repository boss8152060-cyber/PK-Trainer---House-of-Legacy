#!/usr/bin/env python3
"""Palace-style mansion (scene M): outer court / middle court / inner court, ordered by importance.

Unlike mansiongen.py this design does NOT work around the rooms the game refuses to demolish.  It lays the
whole compound out freely and assigns every undemolishable room (read from an export of the current scene) a
*slot* in the plan.  The blueprint (.es3) contains everything except those rooms, and moves.json lists where each
of them has to be moved afterwards (the PK Trainer "move rooms" button applies it).

    python palacegen.py --src current_after_clear.es3 --seed 8 --out palace.es3 --moves moves.json

Axis: the main gate is on the south wall (a = 23, b = 6..8).  Going north:
    outer court  (a 16..22)  gate, guards, stables, warehouses, servants, martial ground
    middle court (a  6..14)  ceremony / learning: ancestral hall, school, library, two reception halls
    inner court  (a -7.. 4)  the family: residences around a private garden with the big pond
"""
import argparse, collections, json, math, os, random, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import layoutgen as L
import mansiongen as MG

N4 = MG.N4
A0, A1, B0, B1 = -8, 23, -8, 22
AXIS = (6, 8)                                   # gate cells b = 6..8
OUTER_A, MID_A = 16, 6                          # first row of outer court / of middle court (partition at their north edge)


def zone(a):
    return "inner" if a < MID_A - 1 else ("middle" if a < OUTER_A - 1 else "outer")


class PM(MG.M):
    """MansionGen M with a few palace helpers"""

    def hwall(self, a_edge, doors):
        """wall along the north edge of row a_edge (whole width), gate on the axis, small doors at b in doors"""
        P = self.P
        gates = set()
        if self.put_corner(self.gate_cls, a_edge, AXIS[0], self.gd, "gate"):
            gates |= {(a_edge, b) for b in range(AXIS[0], AXIS[1] + 1)}
        for b in doors:
            if self.put(86, a_edge, b, "gate"): gates.add((a_edge, b))
        n = 0
        for b in range(self.B0 + 1, self.B1):
            if (a_edge, b) in gates: continue
            n += 1
            P.edge(MG.WALL_LATTICE if n % 4 == 0 else MG.WALL, a_edge, b, 0, "fence")

    def spaced(self, cls, bounds, kind, dirs=(0,), gap=1, near=None, lock=True):
        """footprint must lie inside bounds=(a0,a1,b0,b1) (cells); `gap` empty cells to other buildings"""
        solid = ("house", "work", "fixed", "water")
        a0, a1, b0, b1 = bounds
        if lock and not self.cat.unlocked(cls): return None
        spots = [(a, b, d) for a in range(a0, a1 + 1) for b in range(b0, b1 + 1) for d in dirs]
        if near: spots.sort(key=lambda t: abs(t[0] - near[0]) + abs(t[1] - near[1]))
        for a, b, d in spots:
            aa, bb = self.P.corner(cls, a, b, d)
            cs = [(aa + x, bb + y) for x, y in self.cat.cells(cls, d)]
            if not all(a0 <= c[0] <= a1 and b0 <= c[1] <= b1 and self.free(*c) for c in cs): continue
            ring = {(x + i, y + j) for x, y in cs for i in range(-gap, gap + 1) for j in range(-gap, gap + 1)} - set(cs)
            if any(self.P.occ.get(c) in solid for c in ring): continue
            if self.P.place(cls, aa, bb, d, kind): return (aa, bb, d)
        return None


def load_rooms(path):
    d = json.load(open(path, encoding="utf-8-sig"))
    return [r for r in d["LanTuBuild"]["value"] if r[0] in MG.FIXED_IDS]


def design(cat, src, seed, opts):
    m = PM(cat, src, seed, a0=A0, a1=A1, b0=B0, b1=B1, free_all=True)
    P, rng = m.P, m.rng
    rooms = {r[0]: r for r in load_rooms(src)}
    cls_of = {i: int(r[1]) for i, r in rooms.items()}
    moves = []
    m.walls()
    m.hwall(OUTER_A, doors=(-4, 18))            # outer -> middle
    m.hwall(MID_A - 1, doors=(-4, 18))          # middle -> inner
    CX = 7

    def slot(rid, bounds, near, gap=1):
        r = rooms.get(rid)
        if not r: return
        cls, d = int(r[1]), int(r[6])
        got = m.spaced(cls, bounds, "fixed", dirs=(d,), gap=gap, near=near, lock=False)
        if got:
            moves.append({"id": rid, "cls": cls, "a": got[0], "b": got[1], "d": got[2]})
        else:
            print("!! no slot for", rid, cls, bounds)

    # ---- the pond garden (inner court) ---------------------------------------------------------------
    m.pond(-1.5, 6.5, 3.4, 5.0, island_ok=True)
    water = m.water

    # ---- slots for the rooms that cannot be demolished ---------------------------------------------------
    # middle court: ancestral hall east of the axis, learning (school, library) west of it, two halls by the inner gate
    slot("B1", (7, 13, 12, 20), near=(8, 16))                                   # ancestral hall (shrine, 3x3)
    slot("B390", (7, 13, -7, 3), near=(8, 0))                                    # school
    slot("B662", (7, 14, -7, 3), near=(12, -2))                                  # library
    slot("TMB3", (7, 13, 9, 21), near=(11, 12))                                  # big hall (2x3)
    slot("TMB15", (7, 13, -7, 5), near=(10, 3))                                  # big hall (2x3)
    # outer court: martial ground east, stables west, storehouse east, side hall west
    slot("TMB7", (17, 22, 14, 21), near=(21, 21))                                # storehouse
    slot("B391", (17, 22, 8, 21), near=(19, 14))                                 # martial ground (5x5)
    slot("TMB5", (17, 22, -7, 4), near=(21, -6))                                 # stables
    slot("B583", (17, 22, -7, 4), near=(18, -2))                                 # side hall
    # inner court: the family's own halls (prime positions beside the garden)
    slot("TMB0", (-7, 4, -7, -1), near=(-5, -5))
    slot("TMB1", (-7, 4, -7, -1), near=(-1, -5))
    slot("TMB6", (-7, 4, 13, 21), near=(-5, 19))

    # ---- road network ---------------------------------------------------------------------------------
    m.path(m.line(A1 - 1, CX, -7 + 6, CX))                                       # the axis from the gate to the garden
    for a_ in (16, 14, 6, 4):                                                    # a lane in front of every court
        m.path(m.line(a_, B0 + 1, a_, B1 - 1))
    m.path(m.line(-7, 0, 4, 0)); m.path(m.line(-7, 14, 4, 14))                    # lanes between wings and garden
    m.path(sorted(m.ring_path()))                                                # shore path around the big pond

    # ---- family halls: inner court first, then the middle court wings ---------------------------------------
    cap = lambda c: int(cat.AB[c][3].split("|")[0])
    cap_now = sum(cap(c) for c in cls_of.values() if c in (15, 11, 138, 14, 139))
    target = opts["max_people"] - opts["slack"]
    houses = []
    pool = sorted((c for c in (int(x) for x in MG.HOUSING.split("|")) if cat.unlocked(c) and cap(c) > 0 and cat.dims(c, 0)[0] == 2),
                  key=lambda c: (-cap(c), -cat.lv(c)))
    inner = [(-7, 4, B0 + 1, 21)]
    wings = [(-7, 4, B0 + 1, -1), (-7, 4, 13, B1 - 1), (-7, -5, 0, 12)]            # west wing, east wing, back row (north of the pond)
    middle = [(7, 13, B0 + 1, 3), (7, 13, 13, B1 - 1)]
    fails = 0; i = 0
    for zone_rects in (wings, middle):
        fails = 0; i = 0
        while pool and fails < len(pool) * 2 and cap_now < target:
            cls = pool[i % len(pool)]; i += 1
            if cap_now + cap(cls) > target: fails += 1; continue
            got = None
            for bd in zone_rects:
                got = m.spaced(cls, bd, "house", dirs=(0, 1))
                if got: break
            if got: cap_now += cap(cls); houses.append((cls, got)); fails = 0
            else: fails += 1
    m.cap_total = cap_now

    # ---- outer court: warehouses, servants' quarters ----------------------------------------------------------
    side = 148 if cat.unlocked(148) else 147
    d_side = next(d for d in (0, 1, 2, 3) if cat.dims(side, d) == (3, 1))
    util = []
    for cls in (182, 182, 10, 10, 182, 10):
        got = m.spaced(cls, (17, 22, B0 + 1, B1 - 1), "work", dirs=(0, 1), gap=1, near=(21, 4 if cls != 10 else 18))
        if got: util.append((cls, got))
    n_side = 0
    for b in list(range(-7, 4, 1)) + list(range(14, 22, 1)):
        if n_side >= 14: break
        got = m.spaced(side, (17, 22, B0 + 1, B1 - 1), "house", dirs=(d_side,), gap=0, near=(22, b))
        if got: n_side += 1
    houses.append((side, n_side))

    # ---- garden furniture --------------------------------------------------------------------------------------------
    ga = [(c, 4) for c in (-1, 5, 11)]
    for pa, pb in ((-4, 2), (1, 11)):
        m.fit_in(23, (pa - 1, pa + 1, pb - 1, pb + 1), "decor", dirs=(0,))
    for cls, a_, b_ in ((181, -6, 0), (180, 2, 1), (180, 2, 12), (179, -6, 13), (179, 9, 5), (180, 10, 8), (179, 12, 0)):
        m.fit_in(cls, (a_ - 1, a_ + 1, b_ - 1, b_ + 1), "decor", dirs=(0, 1))
    # lanterns along the axis
    for a_ in range(-4, 22, 3):
        for b_ in (5, 9):
            if m.free(a_, b_): m.put(rng.choice([223, 225]), a_, b_, "decor")

    # ---- walkways: join every building to the road network ----------------------------------------------------------
    blds = []
    for pc in P.pieces:
        if pc["kind"] in ("house", "work", "fixed") and pc["cls"] not in L.EDGE_CLASSES:
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
        if dist[cur] > 16: continue
        while dist.get(cur, 0) > 0:
            if m.free(*cur): m.path([cur]); pathcells.add(cur)
            nxt = [(cur[0] + x, cur[1] + y) for x, y in N4 if dist.get((cur[0] + x, cur[1] + y), 99) == dist[cur] - 1]
            if not nxt: break
            cur = nxt[0]
        linked += 1
    m.linked = linked

    # ---- planting, by court: outer = sparse and formal, middle = flower beds, inner = lush garden ----------------------
    shrubs = [s for s in MG.SHRUBS if cat.unlocked(s)]
    by_zone = {"outer": ([37, 37, 35, 76], 0.22), "middle": ([28, 30, 31, 33, 36], 0.40), "inner": ([36, 80, 27, 28, 76, 30], 0.50)}
    cells = [c for c in sorted(m.limit) if m.free(*c)]
    rng.shuffle(cells)
    def near(kinds, a, b, r=1):
        return any(P.occ.get((a + x, b + y)) in kinds for x in range(-r, r + 1) for y in range(-r, r + 1))
    quota = {z: 0 for z in by_zone}
    budget = {z: int(by_zone[z][1] * sum(1 for c in cells if zone(c[0]) == z)) for z in by_zone}
    for (a, b) in cells:
        z = zone(a)
        if quota[z] >= budget[z] or not m.free(a, b): continue
        if near({"gate"}, a, b, 2) or near({"house", "work", "fixed"}, a, b, 1): continue
        trees, _ = by_zone[z]
        trees = [t for t in trees if cat.unlocked(t)]
        wat = near({"water"}, a, b, 2)
        if near({"tree"}, a, b, 1):
            if rng.random() < 0.5 and not near({"path"}, a, b, 0) and m.put(rng.choice(shrubs), a, b, "flower"): quota[z] += 1
            continue
        if near({"path"}, a, b, 1) and (a * 3 + b * 5) % 9 == 0 and z != "outer":
            if m.put(rng.choice([223, 225]), a, b, "decor"): quota[z] += 1; continue
        pick = (a * 5 + b * 3) % 10
        if pick < 4: cls = rng.choice([27, 36, 80] if wat and z == "inner" else trees)
        elif pick < 8: cls = rng.choice(shrubs)
        else: cls = rng.choice([179, 25]) if (z != "outer" and rng.random() < .3) else rng.choice(shrubs)
        if m.put(cls, a, b, "flower" if cls in MG.SHRUBS else "tree"): quota[z] += 1
    return P, m, houses, util, moves


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default=L.DEFAULT_DATA)
    ap.add_argument("--src", required=True, help="export of the current scene (to learn the rooms that cannot be demolished)")
    ap.add_argument("--seed", type=int, default=8)
    ap.add_argument("--family", type=int, default=12)
    ap.add_argument("--max-people", type=int, default=50)
    ap.add_argument("--slack", type=int, default=4)
    ap.add_argument("--out", default="palace.es3")
    ap.add_argument("--moves", default="moves.json")
    a = ap.parse_args()
    cat = L.Catalog(a.data, a.family, allowed=MG.ALLOWED)
    P, m, houses, util, moves = design(cat, a.src, a.seed, {"max_people": a.max_people, "slack": a.slack})
    bad = L.validate(P)
    st = MG.write_m(P, a.out)
    json.dump(moves, open(a.moves, "w"), indent=1)
    txt = os.path.splitext(a.moves)[0] + ".txt"
    with open(txt, "w", encoding="utf-8") as f:                                 # "id a b dir" per line - what the trainer reads
        for mv in moves: f.write("%s %d %d %d" % (mv["id"], mv["a"], mv["b"], mv["d"]) + chr(10))
    png = os.path.splitext(a.out)[0] + "_iso.png"
    L.KIND_COLOR.update({"gate": "#ee3333", "fence": "#222222"})
    L.render_png(P, png)
    print(MG.ascii_map(P, m))
    print("pieces", st["pieces"], "| cost", format(st["cost"], ","), "coin | validation problems:", len(bad))
    for b in bad[:8]: print("  ", b[0], b[1])
    print("walkways linking", m.linked, "buildings | household capacity", m.cap_total, "/", a.max_people)
    print("moves:", len(moves)); [print("  ", mv) for mv in moves]
    print("houses", houses); print("util", util)
    print(dict(collections.Counter(p["kind"] for p in P.pieces if p["cls"] not in L.EDGE_CLASSES)), "walls", sum(1 for p in P.pieces if p["cls"] in L.EDGE_CLASSES))
    print("wrote", a.out, a.moves, png)


if __name__ == "__main__":
    main()
