#!/usr/bin/env python3
"""Mansion (scene M) blueprint generator for House of Legacy.

Builds a Chinese-garden mansion around the buildings that the game will not let you demolish
(rooms that are somebody's bedroom / study / school / training ground...).  Those rooms are read from
an .es3 you exported from your own mansion with the game's export button and are NOT written to the
blueprint (they already exist); every other piece is new.

    python mansiongen.py --src หกดหกด.es3 --seed 1 --out mansion.es3
"""
import argparse, collections, json, math, os, random, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import layoutgen as L

HOUSING = "11|12|13|14|15|16|137|138|139|140|141|142|143|144|145|146|19|20|21|22|147|148|149|150"
UTILITY = "10|182|9|5|6|8|7|4|81|0|3|2"
DECOR = ("82|83|84|18|87|88|89|218|222|219|220|221|223|224|225|226|174|175|176|177|85|178|86|171|169|170|26|90|23|92|25|179|180|181|"
         "94|95|97|98|1|93|166|17|167|168|27|28|29|30|31|32|33|34|35|36|37|38|76|77|78|79|80|109|110|96|129|130")
ALLOWED = {int(x) for s in (HOUSING, UTILITY, DECOR) for x in s.split("|")}
FIXED_IDS = {"TMB0", "TMB1", "TMB3", "TMB6", "TMB15", "B390", "B391", "B583", "B1", "TMB5", "TMB7", "B629", "B662"}      # protected rooms (see mapm.py)
TREES = [28, 35, 76, 30, 31, 33, 36, 80]          # ginkgo, pines, orchid/osmanthus, peach, plum (no clutter-y fruit trees)
WALLTREES = [37, 37, 35, 76]                    # bamboo + pine along the walls
WATERTREES = [27, 27, 36]                       # willow / peach on the banks
ACCENT = [28, 36, 80]                           # ginkgo / peach / plum beside the avenue
SHRUBS = [77, 38, 78, 79, 80, 109, 110]
WALL, WALL_LATTICE = 18, 88
N4 = ((1, 0), (-1, 0), (0, 1), (0, -1))


def load_fixed_all(path):
    d = json.load(open(path, encoding="utf-8-sig"))
    return [r for r in d["LanTuBuild"]["value"] if int(r[1]) in {int(x) for x in HOUSING.split('|')}]


def load_fixed(path):
    d = json.load(open(path, encoding="utf-8-sig"))
    rows = d["LanTuBuild"]["value"]
    return [r for r in rows if r[0] in FIXED_IDS]


class M:
    def __init__(self, cat, src, seed, a0=-8, a1=23, b0=-8, b1=22, free_all=False):
        self.cat = cat
        self.rng = random.Random(seed)
        self.P = L.Plot(cat, lo=-9, n=36)
        self.A0, self.A1, self.B0, self.B1 = a0, a1, b0, b1
        for r in ([] if free_all else load_fixed(src)):
            ok = self.P.place(int(r[1]), *map(int, r[5].split("|")), int(r[6]), "fixed")
            assert ok, ("fixed room does not fit", r)
        # the walled compound: only interior cells may be used
        self.limit = {(a, b) for a in range(a0, a1 + 1) for b in range(b0, b1 + 1)}
        self.reserve = set()                        # cells kept empty for later development

    # --- helpers -----------------------------------------------------------------------------
    def free(self, a, b): return (a, b) in self.limit and (a, b) not in self.P.occ and (a, b) not in self.reserve
    def put(self, cls, a, b, kind, d=0):
        if cls and self.cat.unlocked(cls) and all(self.free(a + x, b + y) for x, y in self.cat.cells(cls, d)):
            return self.P.place(cls, a, b, d, kind)
        return False
    def put_corner(self, cls, a, b, d, kind):
        if not self.cat.unlocked(cls): return False
        aa, bb = self.P.corner(cls, a, b, d)
        if all(self.free(aa + x, bb + y) for x, y in self.cat.cells(cls, d)):
            return self.P.place(cls, aa, bb, d, kind)
        return False
    def fit_in(self, cls, rect, kind, dirs=(0, 1, 2, 3), step=1, near=None):
        a0, a1, b0, b1 = rect
        spots = [(a, b, d) for a in range(a0, a1 + 1, step) for b in range(b0, b1 + 1, step) for d in dirs]
        if near: spots.sort(key=lambda s: abs(s[0] - near[0]) + abs(s[1] - near[1]))
        for a, b, d in spots:
            if self.put_corner(cls, a, b, d, kind): return (a, b, d)
        return None
    def fit_spaced(self, cls, rects, kind, dirs=(0,), gap=1, near=None):
        """like fit_in, but keeps `gap` empty cells between this building and other buildings (halls read as separate courts)"""
        solid = ("house", "work", "fixed", "water")
        for rect in rects:
            a0, a1, b0, b1 = rect
            spots = [(a, b, d) for a in range(a0, a1 + 1) for b in range(b0, b1 + 1) for d in dirs]
            if near: spots.sort(key=lambda t: abs(t[0] - near[0]) + abs(t[1] - near[1]))
            for a, b, d in spots:
                if not self.cat.unlocked(cls): return None
                aa, bb = self.P.corner(cls, a, b, d)
                cs = [(aa + x, bb + y) for x, y in self.cat.cells(cls, d)]
                if not all(self.free(*c) for c in cs): continue
                ring = {(x + i, y + j) for x, y in cs for i in range(-gap, gap + 1) for j in range(-gap, gap + 1)} - set(cs)
                if any(self.P.occ.get(c) in solid for c in ring): continue
                if self.P.place(cls, aa, bb, d, kind): return (a, b, d)
        return None
    def n4free(self, a, b): return [(a + x, b + y) for x, y in N4 if self.free(a + x, b + y)]

    # --- walls and gates ------------------------------------------------------------------
    def walls(self):
        P, A0, A1, B0, B1 = self.P, self.A0, self.A1, self.B0, self.B1
        sides = [("a0", 0, [(A0, b) for b in range(B0, B1 + 1)]), ("b0", 1, [(a, B0) for a in range(A0, A1 + 1)]),
                 ("a1", 2, [(A1, b) for b in range(B0, B1 + 1)]), ("b1", 3, [(a, B1) for a in range(A0, A1 + 1)])]
        gates = {}                                  # cell -> True where a gate building stands (no wall there)
        mid = (self.B0 + self.B1) // 2
        # main gate (south), small gates west and north
        gd = 0
        gate_cls = 170 if self.cat.unlocked(170) else 169
        for d in (0, 1, 2, 3):
            if self.cat.dims(gate_cls, d) == (1, 3):
                if self.put_corner(gate_cls, A1, 6, d, "gate"):
                    for cell in [(A1, 6), (A1, 7), (A1, 8)]:
                        gates[cell] = True
                    gd = d
                    break
        # two more big gates on the north wall (each adds safety)
        for gb in (-4, 12):
            if self.put_corner(gate_cls, A0, gb, gd, "gate"):
                for cell in [(A0, gb), (A0, gb + 1), (A0, gb + 2)]:
                    gates[cell] = True
        small = self.rng.sample([(10, B0), (2, B0), (16, B0), (20, B0), (4, B1), (14, B1)], 3)
        for (a, b) in small:
            if self.put(86, a, b, "gate"):
                gates[(a, b)] = True
        self.gd, self.gate_cls = gd, gate_cls
        n = 0
        for name, d, cells in sides:
            for (a, b) in cells:
                if (a, b) in gates:
                    continue
                n += 1
                cls = WALL_LATTICE if (n % 4 == 0 and self.cat.unlocked(WALL_LATTICE)) else WALL
                P.edge(cls, a, b, d, "fence")

    # --- inner / outer court partition -----------------------------------------------------------
    def partitions(self, a_in=16, b_in=18):
        """inner court = north-west core (a < a_in, b < b_in); outer court = south band + east service strip.
        two walls with an inner main gate on the central axis and two small service doors on the east wall"""
        P, cat = self.P, self.cat
        gates = set()
        if self.put_corner(self.gate_cls, a_in, 6, self.gd, "gate"):
            gates |= {(a_in, 6), (a_in, 7), (a_in, 8)}
        doors = set()
        for a_ in (3, 11):
            if self.put(86, a_, b_in, "gate"): doors.add((a_, b_in))
        n = 0
        for b in range(self.B0 + 1, b_in + 1):
            if (a_in, b) in gates: continue
            n += 1
            P.edge(WALL_LATTICE if n % 4 == 0 else WALL, a_in, b, 0, "fence")
        for a in range(self.A0 + 1, a_in):
            if (a, b_in) in doors: continue
            n += 1
            P.edge(WALL_LATTICE if n % 4 == 0 else WALL, a, b_in, 1, "fence")

    # --- the pond -------------------------------------------------------------------------
    def pond(self, ca=9.0, cb=10.0, ra=4.2, rb=4.6, island_ok=False):
        rng = self.rng
        cells = []
        ph = [rng.uniform(0, 6.28) for _ in range(3)]
        for a in range(int(ca - ra) - 2, int(ca + ra) + 3):
            for b in range(int(cb - rb) - 2, int(cb + rb) + 3):
                t = math.atan2(a - ca, b - cb)
                wob = 1 + 0.22 * math.sin(2 * t + ph[0]) + 0.12 * math.sin(3 * t + ph[1]) + 0.08 * math.sin(5 * t + ph[2])
                v = ((a - ca) / ra) ** 2 + ((b - cb) / rb) ** 2
                if v < wob * wob * 0.92 and self.free(a, b):
                    cells.append((a, b))
        water = set(cells)
        # drop cells with <2 water neighbours (no spikes)
        for _ in range(2):
            water = {c for c in water if sum((c[0] + x, c[1] + y) in water for x, y in N4) >= 2}
        # a rock island in the middle of the big pond (2x2 'luxury mountain'), kept 2 cells away from the shore
        island = None
        if island_ok and self.cat.unlocked(181):
            inner = [c for c in water if all((c[0] + x, c[1] + y) in water for x in range(-1, 3) for y in range(-1, 3))]
            if inner:
                ia, ib = min(inner, key=lambda c: abs(c[0] - ca - 0.5) + abs(c[1] - cb - 0.5))
                island = {(ia, ib), (ia + 1, ib), (ia, ib + 1), (ia + 1, ib + 1)}
                water -= island
        # shoreline tiles (stone-rimmed, class 90) on the edge, plain water (class 26) inside
        for (a, b) in sorted(water):
            edge = any((a + x, b + y) not in water for x, y in N4)
            self.P.place(90 if edge else 26, a, b, 0, "water")
        if island:
            ia, ib = min(island)
            self.fit_in(181, (ia - 1, ia + 1, ib - 1, ib + 1), "decor", dirs=(0,))
            if self.cat.unlocked(23): pass
        self.water = getattr(self, "water", set()) | water
        # lotus / reeds on part of the water
        lotus = [96, 129, 130]
        for (a, b) in sorted(water):
            if all((a + x, b + y) in water for x, y in N4) and rng.random() < 0.28:
                self.P.stack(rng.choice([c for c in lotus if self.cat.unlocked(c)]), a, b)

    # --- paths --------------------------------------------------------------------------------
    def path(self, cells, cls=None):
        cls = cls or (84 if self.cat.unlocked(84) else 83)
        for (a, b) in cells:
            if self.free(a, b):
                self.P.place(cls, a, b, 0, "path")
    def line(self, a0, b0, a1, b1):
        out = []
        a, b = a0, b0
        while (a, b) != (a1, b1):
            out.append((a, b))
            if a != a1 and (b == b1 or abs(a1 - a) >= abs(b1 - b)): a += 1 if a1 > a else -1
            else: b += 1 if b1 > b else -1
        out.append((a1, b1))
        return out

    def ring_path(self, skip=lambda a, b: False):
        out = set()
        for (a, b) in self.water:
            for x in range(-1, 2):
                for y in range(-1, 2):
                    c = (a + x, b + y)
                    if c not in self.water and self.free(*c) and not skip(*c):
                        out.add(c)
        return out


def design(cat, src, seed, opts):
    m = M(cat, src, seed, free_all=opts.get("free", False))
    P, rng = m.P, m.rng
    m.walls()
    m.partitions()
    m.pond(island_ok=True)
    # a small lotus pond in a quiet corner (first candidate that is mostly free)
    for (ca, cb) in opts.get("pond2", [(4, 14), (14, 14), (15, 2), (3, 3), (18, 2)]):
        if sum(m.free(a_, b_) for a_ in range(ca - 2, ca + 3) for b_ in range(cb - 2, cb + 3)) >= 22:
            m.pond(ca, cb, 2.0, 2.3)
            m.fit_in(23, (ca - 3, ca + 3, cb - 4, cb + 4), "decor", dirs=(0,), near=(ca, cb + 3))
            break
    water = m.water

    # ---- road network (stone path) -----------------------------------------------------------
    shore = m.ring_path(skip=lambda a, b: a < 6)                    # keep the north bank soft (rocks go there)
    # thin the shore ring into two arcs so it does not look like a frame
    arc = {c for c in shore if (c[0] > 9 or c[1] < 9) or c[1] > 14}
    m.path(m.line(m.A1 - 1, 7, 15, 7))                              # gate -> avenue
    m.path(m.line(15, -2, 15, 16))                                  # east-west avenue south of the pond
    m.path(m.line(14, 7, 12, 7))
    m.path(sorted(arc))
    m.path(m.line(2, 4, 14, 4))                                     # along the west halls
    m.path(m.line(2, 4, 2, 16))                                     # along the north halls
    m.path(m.line(2, 16, 22, 16))                                   # east lane in front of the retainers' row
    m.path(m.line(9, -2, 9, 4))                                     # to the west utility court
    m.path(m.line(5, 16, 5, 13)); m.path(m.line(9, 16, 9, 15))      # spurs east

    # ---- room kept free for later development (south-west court) ---------------------------------
    for a_ in range(opts["rsv"][0], opts["rsv"][1] + 1):
        for b_ in range(opts["rsv"][2], opts["rsv"][3] + 1):
            m.reserve.add((a_, b_))
    # ---- homes: add halls until the household capacity reaches the target (rest stays for later) ---
    cap_now = 0 if opts.get("free") else sum(int(cat.AB[int(r[1])][3].split("|")[0]) for r in load_fixed_all(src))
    if opts.get("free"):                              # everything is redesigned: place the 'landmark' rooms first
        m.fit_in(0, (0, 2, 5, 12), "work", dirs=(0,))           # ancestral hall, north-centre
        m.fit_in(6, (15, 20, 11, 15), "work", dirs=(0,))        # martial ground, south-east
        m.fit_in(5, (3, 12, -6, -2), "work", near=(4, -5))      # school, west court
        m.fit_in(8, (3, 14, -6, 0), "work", near=(9, -4))       # book hall
    target = opts["max_people"] - opts["slack"]
    houses = []
    cps = {c: int(cat.AB[c][3].split("|")[0]) for c in (int(x) for x in HOUSING.split("|")) if cat.unlocked(c)}
    pool = sorted((c for c, v in cps.items() if v >= 3 and cat.dims(c, 0)[0] == 2), key=lambda c: (-cps[c], -cat.lv(c)))
    rects = [(0, 1, -5, 14), (5, 13, -5, -1), (5, 12, 13, 14)]
    rng.shuffle(rects)
    i = 0; fails = 0
    while pool and fails < len(pool) * 2 and cap_now < target:
        cls = pool[i % len(pool)]; i += 1
        if cap_now + cps[cls] > target: fails += 1; continue
        r = m.fit_spaced(cls, rects, "house", dirs=(0,))
        if r: cap_now += cps[cls]; houses.append((cls, r)); fails = 0
        else: fails += 1
    pool2 = sorted((c for c, v in cps.items() if v == 2 and cat.dims(c, 0)[0] == 2), key=lambda c: -cat.lv(c))
    full = [(m.A0 + 1, 13, m.B0 + 1, 15)]
    for cls in pool2 * 6:
        cp = cps[cls]
        if cap_now + cp > target: break
        r = m.fit_spaced(cls, full, "house", dirs=(0, 1))
        if r: cap_now += cp; houses.append((cls, r))
    m.cap_total = cap_now
    side = 148 if cat.unlocked(148) else 147
    d_side = next(d for d in (0, 1, 2, 3) if cat.dims(side, d) == (3, 1))
    n_side = 0
    # servants' / retainers' court: two rows of side houses facing a lane in the south-east strip
    m.path(m.line(4, 20, 21, 20))
    for b in (19, 21):
        for a in range(5, 22, 3):
            if m.put_corner(side, a, b, d_side, "house"): n_side += 1
    for b in range(-7, 1, 2):                                        # a few more along the south wall (outer court)
        if n_side >= 14: break
        if m.put_corner(side, 20, b, d_side, "house"): n_side += 1
    houses.append((side, n_side))

    # ---- utility buildings (west court, south court) -----------------------------------------------
    util = []
    has = lambda c: any(p["cls"] == c and p["kind"] == "fixed" for p in P.pieces)
    util.append((182, m.fit_in(182, (3, 14, -6, 0), "work")))        # storehouse
    if not has(8) and not opts.get("free"): util.append((8, m.fit_in(8, (3, 14, -6, 0), "work", near=(6, -4))))   # book hall
    if not has(10): util.append((10, m.fit_in(10, (3, 14, -6, 0), "work", near=(13, -3))))
    if not has(9): util.append((9, m.fit_in(9, (17, 22, 4, 10), "work", near=(20, 5))))   # stables near the gate

    # ---- warehouses: a service lane along the east wall ---------------------------------------------
    n_wh = 0
    for cls in (182, 182, 182, 10, 10, 10):
        r = m.fit_spaced(cls, [(-6, 19, 19, 19)], "work", dirs=(0,), gap=0 if cls == 10 else 1)
        if r: util.append((cls, r)); n_wh += 1
    m.path(m.line(-6, 18, 20, 18))                                   # lane in front of the warehouses

    # ---- garden furniture -------------------------------------------------------------------------
    # rocks on the north bank
    for cls, a, b in ((181, 4, 7), (180, 4, 13), (179, 5, 10), (25, 6, 15)):
        m.put_corner(cls, a, b, 0, "decor") or m.fit_in(cls, (a - 1, a + 1, b - 1, b + 1), "decor", dirs=(0, 1))
    # pavilion on the south bank + one more on the east bank
    pav = m.fit_in(23, (14, 14, 9, 13), "decor", dirs=(0,))
    m.fit_in(23, (7, 11, 15, 15), "decor", dirs=(0,))
    # lanterns beside the avenue
    for a in (16, 18, 20, 22):
        for b in (5, 9):
            m.put(225 if rng.random() < .5 else 223, a, b, "decor")
    shrubs0 = [s for s in SHRUBS if cat.unlocked(s)]
    # bamboo grove in the south-west corner and a flowering grove by the gate
    for a_ in range(19, 23):
        for b_ in range(-6, -2):
            if (a_ + b_) % 2 == 0: m.put(37, a_, b_, "tree")
    for i, (a_, b_) in enumerate(((21, 4), (21, 10), (19, 4), (19, 10), (22, 5), (22, 9))):
        m.put(shrubs0[i % len(shrubs0)], a_, b_, "flower")
    # trees and shrubs: groves in the quiet corners, a few single trees on the lawns
    shrubs = [s for s in SHRUBS if cat.unlocked(s)]
    trees = [t for t in TREES if cat.unlocked(t)]
    k = 0
    for (a, b) in sorted(m.limit):
        if not m.free(a, b): continue
        # soft planting along the walls
        edge = a in (m.A0, m.A1) or b in (m.B0, m.B1)
        near_water = any((a + x, b + y) in water for x in range(-2, 3) for y in range(-2, 3))
        near_path = any(P.occ.get((a + x, b + y)) in ("path",) for x, y in N4)
        near_gate = any(P.occ.get((a + x, b + y)) == "gate" for x in range(-2, 3) for y in range(-2, 3))
        if near_gate: continue
        r = rng.random()
        if edge and r < 0.40: m.put(rng.choice([t for t in WALLTREES if cat.unlocked(t)]), a, b, "tree")
        elif near_water and r < 0.10 and (a + b) % 3 == 0: m.put(rng.choice([t for t in WATERTREES if cat.unlocked(t)]), a, b, "tree")
        elif near_water and r < 0.32: m.put(rng.choice(shrubs), a, b, "flower")
        elif near_path and r < 0.16: m.put(shrubs[k % len(shrubs)], a, b, "flower"); k += 1
        elif r < 0.05: m.put(rng.choice(trees), a, b, "tree")
    # a few accent trees where the avenue meets the pond, and stone clusters on the banks
    for a_, b_ in ((16, 6), (16, 9), (14, 3), (14, 15)):
        m.put(rng.choice([t for t in ACCENT if cat.unlocked(t)]), a_, b_, "tree")
    for cls, a_, b_ in ((181, 12, 4), (180, 4, 9), (179, 14, 17), (25, 16, 14), (25, 3, 15)):
        m.fit_in(cls, (a_ - 1, a_ + 1, b_ - 1, b_ + 1), "decor", dirs=(0, 1))
    # ---- walkways: join every building to the road network ------------------------------------------
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

    # ---- fill the remaining lawn: spaced groves, flower beds, rocks (target density of decorated free cells) ----
    free_cells = [c for c in sorted(m.limit) if m.free(*c)]
    rng.shuffle(free_cells)
    want = int(opts.get("density", 0.30) * len(free_cells))
    placed = 0
    treeset = {"tree"}
    def near(kind_set, a, b, r=1):
        return any(P.occ.get((a + x, b + y)) in kind_set for x in range(-r, r + 1) for y in range(-r, r + 1))
    for (a, b) in free_cells:
        if placed >= want: break
        if not m.free(a, b): continue
        if near({"gate"}, a, b, 2) or near({"house", "work", "fixed"}, a, b, 1): continue     # keep doors / walls of halls clear
        in_rsv = (a, b) in m.reserve
        if near(treeset, a, b, 1): 
            if rng.random() < .55 and not near({"path"}, a, b, 0): 
                if m.put(rng.choice(shrubs), a, b, "flower"): placed += 1
            continue
        if near({"path"}, a, b, 1) and (a * 3 + b * 5) % 9 == 0:
            if m.put(rng.choice([223, 225]), a, b, "decor"): placed += 1; continue
        near_wat = near({"water"}, a, b, 2)
        zone = (a * 5 + b * 3) % 10
        if zone < 4:   cls = rng.choice([t for t in (WATERTREES if near_wat else trees) if cat.unlocked(t)])
        elif zone < 7: cls = rng.choice(shrubs)
        else:          cls = rng.choice([179, 25]) if rng.random() < .3 else rng.choice(shrubs)
        if m.put(cls, a, b, "flower" if cls in SHRUBS else "tree"): placed += 1
    return P, m, houses, util


# ------------------------------------------------------------------------------------------------
def stats(P):
    cat = P.cat
    cost = gold = 0
    new = [p for p in P.pieces if p["kind"] != "fixed"]
    for p in new:
        cost += cat.cost(p["cls"]); gold += cat.gold(p["cls"])
    return {"cost": cost, "gold": gold, "pieces": len(new)}


def write_m(P, path, acres=25):
    st = stats(P)
    rows = []
    for k, p in enumerate([q for q in P.pieces if q["kind"] != "fixed"]):
        d = (p["d"] + 2) % 4 if p["cls"] in L.EDGE_CLASSES else p["d"]
        rows.append(["LTB%d" % k, str(p["cls"]), "1", "0", "1", "%d|%d" % (p["a"], p["b"]), str(d), "0"])
    obj = {"LanTuBuild": {"__type": L.ES3_TYPE_LL, "value": rows},
           "LanTuData": {"__type": L.ES3_TYPE_L, "value": ["M", str(acres), str(st["cost"]), str(st["gold"])]}}
    with open(path, "wb") as f:
        f.write(json.dumps(obj, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))
    return st


def ascii_map(P, m):
    sym = {"fixed": "F", "path": ".", "water": "~", "tree": "t", "flower": "*", "house": "H", "work": "W", "decor": "o", "gate": "G", "lily": "~"}
    occ = {}
    for p in P.pieces:
        if p["cls"] in L.EDGE_CLASSES or p["kind"] == "lily": continue
        for dx, dy in P.cat.cells(p["cls"], p["d"]):
            occ[(p["a"] + dx, p["b"] + dy)] = sym.get(p["kind"], "?")
    wall = {}
    for p in P.pieces:
        if p["cls"] in L.EDGE_CLASSES: wall[(p["a"], p["b"])] = 1
    lines = ["      " + "".join(str(abs(b) % 10) for b in range(m.B0 - 1, m.B1 + 2))]
    for a in range(m.A0 - 1, m.A1 + 2):
        lines.append("%4d  " % a + "".join(occ.get((a, b), "#" if (a, b) in wall else (" " if (a, b) in m.limit else "x")) for b in range(m.B0 - 1, m.B1 + 2)))
    return "\n".join(lines)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default=L.DEFAULT_DATA)
    ap.add_argument("--src", required=True, help="your own mansion export (.es3) - used to find the rooms that cannot be demolished")
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--free", action="store_true", help="redesign everything: no room is treated as undemolishable")
    ap.add_argument("--family", type=int, default=17, help="family level (you already built a level-17 hall)")
    ap.add_argument("--max-people", type=int, default=50, help="household capacity ceiling (sum of hall capacities)")
    ap.add_argument("--density", type=float, default=0.30, help="share of the remaining lawn that gets planted")
    ap.add_argument("--slack", type=int, default=8, help="capacity left unused for later")
    ap.add_argument("--reserve", default="16,22,-6,5", help="a0,a1,b0,b1 cells kept empty for later development")
    ap.add_argument("--out", default="mansion.es3")
    ap.add_argument("--png", default=None)
    a = ap.parse_args()
    cat = L.Catalog(a.data, a.family, allowed=ALLOWED)
    P, m, houses, util = design(cat, a.src, a.seed, {"max_people": a.max_people, "slack": a.slack, "rsv": [int(x) for x in a.reserve.split(",")], "free": a.free, "density": a.density})
    bad = L.validate(P)
    st = write_m(P, a.out)
    png = a.png or os.path.splitext(a.out)[0] + "_iso.png"
    L.KIND_COLOR.update({"gate": "#ee3333", "fence": "#222222"})
    L.render_png(P, png)
    print(ascii_map(P, m))
    print("pieces", st["pieces"], "| cost", format(st["cost"], ","), "coin,", st["gold"], "gold | validation problems:", len(bad))
    for b in bad[:8]: print("  ", b[0], b[1])
    print("walkways linking", getattr(m, "linked", 0), "buildings")
    print("household capacity", m.cap_total, "/", a.max_people, "| reserved cells", len(m.reserve))
    print("houses", houses); print("util", util)
    kinds = collections.Counter(p["kind"] for p in P.pieces if p["cls"] not in L.EDGE_CLASSES)
    print(dict(kinds), "walls", sum(1 for p in P.pieces if p["cls"] in L.EDGE_CLASSES))
    print("wrote", a.out, png)


if __name__ == "__main__":
    main()
