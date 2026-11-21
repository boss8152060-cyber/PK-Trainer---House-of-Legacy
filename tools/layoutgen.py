#!/usr/bin/env python3
"""PK Trainer - farm blueprint generator for House of Legacy (4-acre farm).

Reads the data exported by the in-game trainer (BepInEx/PKTrainerData: footprints.json, tables.json),
designs a layout, validates it, and writes a blueprint .es3 that the game's import button accepts.

  python layoutgen.py --style garden --seed 3 --out "C:/Users/me/Desktop/farm.es3"

Layout rules learned from the game's own preset blueprints (verified: 0 overlaps on every preset):
  * plot = square of cells a,b in [lo, lo+31]; footprints = collider names "da|db" of the tip prefab
  * "edge" classes (fences, walls, gates ...) sit on a cell side: dir 0 = a-min side, 1 = b-min, 2 = a-max, 3 = b-max
    and may share a cell with other buildings
  * water lilies / lotus / reeds (96,129,130) can be stacked on water
"""
import argparse, collections, json, math, os, random, re, sys

DEFAULT_DATA = "C:/Program Files (x86)/Steam/steamapps/common/吾今有世家/BepInEx/PKTrainerData"
ES3_TYPE_LL = ("System.Collections.Generic.List`1[[System.Collections.Generic.List`1[[System.String, mscorlib, "
               "Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]], mscorlib, Version=4.0.0.0, "
               "Culture=neutral, PublicKeyToken=b77a5c561934e089]],mscorlib")
ES3_TYPE_L = ("System.Collections.Generic.List`1[[System.String, mscorlib, Version=4.0.0.0, Culture=neutral, "
              "PublicKeyToken=b77a5c561934e089]],mscorlib")

# ---- class groups (ids from the game's building table) -----------------------------------------
HOUSES = [39, 40, 41, 42, 99, 100, 101, 102]
FIELDS = {"rice": 67, "veg": 133, "cotton": 50, "dry": 49}
PENS = {"poultry": [43, 44], "sheep": [45, 46], "pig": [65, 66], "cattle": [47, 48]}
FISH = 51
WORKSHOPS = [155, 156, 157, 158, 159, 160, 162]
ORCHARD = [69, 74, 71, 72, 70, 75, 73, 68]
# Decoration tab of the FARM only offers: dirt path (82), 4 fences, 3 gates (107,108,165), straw pavilion (24),
# 7 trees + bamboo (37), 7 flowers, and 3 water plants that sit on fish ponds (51). The game's own farm presets
# (NongZLanTu_*.es3) use exactly this set, so nothing else (stone/flower paths, rocks, ponds 26/90, moon gate,
# square pavilion, peach/pomegranate/crabapple trees ...) is generated.
WATER = [51]                       # fish pond, 2x2 - the only water on a farm
WATER_STACK = [96, 129, 130]
TREES = [33, 31, 30, 29, 27, 35, 28]
FLOWERS = [77, 38, 78, 79, 80, 109, 110]
PATHS = {"dirt": 82, "stone": 82, "flower": 82}
FENCES = [103, 104, 105, 106]
GATE_SMALL = [107, 108]
GATE_BIG = [165]
PAVILION = [24]
ROCK = None                        # rocks are not available on a farm
EDGE_CLASSES = {1, 17, 18, 85, 87, 88, 89, 93, 94, 95, 97, 98, 103, 104, 105, 106, 107, 108, 165, 166, 167, 168, 171, 174, 175, 176, 177, 178}


# Buildings in the FARM build menu = the game's own table Mainload.BuildGroupdata, entries "Z":
#   housing   39|40|41|42|99|100|101|102
#   production 49|133|67|50|51|43|44|45|46|65|66|47|48|69|74|71|72|70|75|73|68|155-160|162|163|164
#   decoration 82|103|104|105|106|107|108|165|24|37|33|38|31|29|30|35|27|28|77|78|79|80|109|110|96|129|130
# (confirmed against the player's screenshots of the farm menu). Anything else - stone/flower paths,
# decorative ponds, rocks, the four-corner pavilion ... - is NOT available in the farm.
FARM_CLASSES = {24, 27, 28, 29, 30, 31, 33, 35, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 65, 66, 67, 68, 69, 70,
                71, 72, 73, 74, 75, 77, 78, 79, 80, 82, 96, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 129, 130,
                133, 155, 156, 157, 158, 159, 160, 162, 163, 164, 165}


class Catalog:
    def __init__(self, data_dir, family_level, allowed=None):
        self.fam = family_level
        self.allowed = set(allowed) if allowed is not None else FARM_CLASSES     # buildings of the scene being designed
        fp = json.load(open(os.path.join(data_dir, "footprints.json"), encoding="utf-8"))
        tb = json.load(open(os.path.join(data_dir, "tables.json"), encoding="utf-8"))
        self.AB = tb["allbuild"]
        self.NM = tb["names"]
        pat = re.compile(r"^(-?\d+)\|(-?\d+)$")
        self.foot = {}
        for it in fp["items"]:
            cells = set()
            for s in it["shapes"]:
                if s["tag"] == "BuildTipDiXing":
                    m = pat.match(s["n"])
                    if m:
                        cells.add((int(m.group(1)), int(m.group(2))))
            self.foot[(it["id"], it["dir"])] = sorted(cells or {(0, 0)})

    def lv(self, c): return int(self.AB[c][4])
    def cost(self, c): return int(self.AB[c][1].split("|")[0])
    def gold(self, c): return int(self.AB[c][1].split("|")[1])
    def in_farm(self, c): return c in self.allowed
    def unlocked(self, c): return c in self.allowed and self.lv(c) <= self.fam        # usable right now in this scene
    def locked_ok(self, c): return c in self.allowed and self.lv(c) > self.fam         # in the menu but level too high
    def name(self, c):
        n = self.NM[c] if c < len(self.NM) else []
        for k in (2, 1, 0):
            if len(n) > k and n[k].strip():
                return n[k].split("|")[0]
        return str(c)
    def stats(self, c):
        a = self.AB[c][7].split("|")
        return {"cap": int(a[18]), "env": int(a[12]), "saf": int(a[13]), "conv": int(a[14]), "ren": int(a[11])}
    def cells(self, c, d): return self.foot[(c, d % 4)]
    def dims(self, c, d):
        cs = self.cells(c, d)
        A = [x[0] for x in cs]; B = [x[1] for x in cs]
        return max(A) - min(A) + 1, max(B) - min(B) + 1


class Plot:
    def __init__(self, cat, lo=-9, n=32):
        self.cat, self.lo, self.n, self.hi = cat, lo, n, lo + n - 1
        self.occ = {}              # cell -> kind
        self.pieces = []           # dict(cls,a,b,d,kind)
        self.edges = set()         # (a,b,d,cls) already used
        self.stacked = set()
        self.nextid = 0
        self.gates = []            # (side, anchor_a, anchor_b, cls)
        self.pond = set()
        self.used = collections.Counter()   # how many plots / fish ponds / pens / fruit trees placed so far
        self.budget = {}                    # optional caps: plots, fish, pens, fruit
        self.reserved = set()               # cells deliberately left free for later development

    def left(self, key):
        b = self.budget.get(key)
        return 10 ** 9 if b is None else max(0, b - self.used[key])


    # coordinates: i,j in [0,n) <-> a,b
    def A(self, i): return self.lo + i
    def inb(self, a, b): return self.lo <= a <= self.hi and self.lo <= b <= self.hi
    def free(self, a, b): return self.inb(a, b) and (a, b) not in self.occ

    def fits(self, cls, a, b, d):
        return all(self.free(a + da, b + db) for da, db in self.cat.cells(cls, d))

    def place(self, cls, a, b, d, kind):
        if cls is None or not self.fits(cls, a, b, d):
            return False
        for da, db in self.cat.cells(cls, d):
            self.occ[(a + da, b + db)] = kind
        self.pieces.append({"cls": cls, "a": a, "b": b, "d": d, "kind": kind})
        return True

    def corner(self, cls, a, b, d):
        """anchor that puts the footprint's min corner at cell (a,b)"""
        cs = self.cat.cells(cls, d)
        return a - min(c[0] for c in cs), b - min(c[1] for c in cs)

    def fits_corner(self, cls, a, b, d):
        aa, bb = self.corner(cls, a, b, d)
        return self.fits(cls, aa, bb, d)

    def place_corner(self, cls, a, b, d, kind):
        aa, bb = self.corner(cls, a, b, d)
        return self.place(cls, aa, bb, d, kind)

    def place_any_dir(self, cls, a, b, dirs, kind):
        for d in dirs:
            if self.place(cls, a, b, d, kind):
                return True
        return False

    def edge(self, cls, a, b, d, kind="fence"):
        key = (a, b, d)
        if key in self.edges or not self.inb(a, b):
            return False
        self.edges.add(key)
        self.pieces.append({"cls": cls, "a": a, "b": b, "d": d, "kind": kind})
        return True

    def stack(self, cls, a, b):
        if self.occ.get((a, b)) != "water" or (a, b) in self.stacked:
            return False
        self.stacked.add((a, b))
        self.pieces.append({"cls": cls, "a": a, "b": b, "d": 0, "kind": "lily"})
        return True

    def remove_edge_cell(self, a, b, d):
        self.edges.discard((a, b, d))
        self.pieces = [p for p in self.pieces if not (p["cls"] in EDGE_CLASSES and (p["a"], p["b"], p["d"]) == (a, b, d))]

    def rect_cells(self, r):
        i0, i1, j0, j1 = r
        return [(self.A(i), self.A(j)) for i in range(i0, i1 + 1) for j in range(j0, j1 + 1)]


# ---- dihedral transforms of a square plot (to vary the arrangement) --------------------------
def transform_rect(r, t, n):
    i0, i1, j0, j1 = r
    def f(i, j):
        if t & 1: i = n - 1 - i
        if t & 2: j = n - 1 - j
        if t & 4: i, j = j, i
        return i, j
    pts = [f(i0, j0), f(i1, j1)]
    return (min(p[0] for p in pts), max(p[0] for p in pts), min(p[1] for p in pts), max(p[1] for p in pts))


def usable(cat, classes, allow_locked, rng, locked_share=0.12):
    """filter a class list: unlocked ones; optionally sprinkle locked ones"""
    ok = [c for c in classes if cat.unlocked(c)]
    lk = [c for c in classes if cat.locked_ok(c)]
    return ok, (lk if allow_locked else [])


def pick(rng, ok, locked, locked_share):
    if locked and rng.random() < locked_share:
        return rng.choice(locked)
    return rng.choice(ok) if ok else (rng.choice(locked) if locked else None)


# ============================== zone fillers ====================================================
def fill_pond(P, rect, rng, cfg):
    """irregular pond with lotus, reeds, pavilion, rocks and willows; returns water cell set"""
    cat = P.cat
    i0, i1, j0, j1 = rect
    ci, cj = (i0 + i1) / 2.0, (j0 + j1) / 2.0
    ri, rj = (i1 - i0) / 2.0 - 0.8, (j1 - j0) / 2.0 - 0.8
    ph = [rng.uniform(0, 6.28) for _ in range(3)]
    organic = cfg.get("center") == "organic"
    th = rng.uniform(0, math.pi)
    lobe = (0.30 * math.cos(th), 0.30 * math.sin(th))
    water = set()
    wc = [c for c in WATER if cat.unlocked(c)] or WATER
    for i in range(i0, i1 + 1):
        for j in range(j0, j1 + 1):
            x, y = (i - ci) / max(ri, 1), (j - cj) / max(rj, 1)
            ang = math.atan2(y, x)
            wob = 1 + 0.22 * math.sin(2 * ang + ph[0]) + 0.14 * math.sin(3 * ang + ph[1]) + 0.08 * math.sin(5 * ang + ph[2])
            if organic:
                r2 = (0.80 * wob) ** 2                       # two overlapping lobes -> kidney shape
                inside = (x - lobe[0]) ** 2 + (y - lobe[1]) ** 2 <= r2 or (x + lobe[0]) ** 2 + (y + lobe[1]) ** 2 <= r2
            else:
                inside = x * x + y * y <= wob * wob
            if inside:
                water.add((P.A(i), P.A(j)))
    # keep only cells that are free
    mask = {c for c in water if P.free(*c)}
    water = set()
    for (a, b) in sorted(mask):                      # fish ponds are 2x2: snap the pond shape to a 2-cell grid
        if (a - P.lo) % 2 or (b - P.lo) % 2:
            continue
        blk = [(a, b), (a + 1, b), (a, b + 1), (a + 1, b + 1)]
        if all(c in mask for c in blk) and P.place(rng.choice(wc), a, b, 0, "water"):
            water.update(blk)
    # lotus / lily / reed stacked on water
    sc = [x for x in WATER_STACK if cat.unlocked(x)]
    for c in sorted(water):
        if rng.random() < cfg.get("lily_density", 0.3):
            P.stack(rng.choice(sc), *c)
    P.pond = set(water)
    if cfg.get("decor") == "refined":
        return water                      # shore is dressed by refined_decor()
    # shoreline ring
    shore = []
    for (a, b) in sorted(water):
        for da, db in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            n = (a + da, b + db)
            if P.free(*n) and n not in shore:
                shore.append(n)
    rng.shuffle(shore)
    pav = [c for c in PAVILION if cat.unlocked(c)] or PAVILION
    placed_pav = 0
    for c in shore:
        if placed_pav < cfg.get("pavilions", 1) and P.free(*c):
            if P.place(rng.choice(pav), c[0], c[1], 0, "decor"):
                placed_pav += 1
    okt, lkt = usable(cat, [27, 33, 31, 35], cfg["locked"], rng)
    rocks = 0
    for c in shore:
        if not P.free(*c):
            continue
        r = rng.random()
        if r < 0.22:
            P.place(27 if cat.unlocked(27) else rng.choice(okt), c[0], c[1], 0, "tree")      # willow
        elif r < 0.34 and rocks < 6:
            if P.place(ROCK, c[0], c[1], 0, "decor"):
                rocks += 1
        elif r < 0.60:
            fl = [f for f in FLOWERS if cat.unlocked(f)]
            P.place(rng.choice(fl), c[0], c[1], 0, "flower")
    return water


def fill_pond_fish(P, rect, rng, cfg):
    """the farm's only 'pond' is the fish pond (2x2). Build a kidney-shaped pond from 2x2 blocks and stack
    lotus / water lilies / reeds on it (exactly what the game's own presets do)."""
    cat = P.cat
    i0, i1, j0, j1 = rect
    bi = (i1 - i0 + 1) // 2; bj = (j1 - j0 + 1) // 2
    ci, cj = (bi - 1) / 2.0, (bj - 1) / 2.0
    ri, rj = max(bi / 2.0, 1.0), max(bj / 2.0, 1.0)
    th = rng.uniform(0, math.pi)
    lobe = (0.30 * math.cos(th), 0.30 * math.sin(th))
    ph = [rng.uniform(0, 6.28) for _ in range(3)]
    water = set()
    fish = 51
    blocks = []
    for x in range(bi):
        for y in range(bj):
            nx, ny = (x - ci) / ri, (y - cj) / rj
            ang = math.atan2(ny, nx)
            wob = 1 + 0.20 * math.sin(2 * ang + ph[0]) + 0.12 * math.sin(3 * ang + ph[1])
            r2 = (0.85 * wob) ** 2
            d1 = (nx - lobe[0]) ** 2 + (ny - lobe[1]) ** 2
            d2 = (nx + lobe[0]) ** 2 + (ny + lobe[1]) ** 2
            if d1 <= r2 or d2 <= r2:
                blocks.append((min(d1, d2) / r2, x, y))
    blocks.sort()                                    # core of the pond first, so a small budget still gives a compact pond
    for (_, x, y) in blocks:
        if P.left("fish") <= 0:
            break
        if P.place_corner(fish, P.A(i0 + 2 * x), P.A(j0 + 2 * y), 0, "water"):
            P.used["fish"] += 1
            for da in (0, 1):
                for db in (0, 1):
                    water.add((P.A(i0 + 2 * x) + da, P.A(j0 + 2 * y) + db))
    P.pond = set(water)
    sc = [x for x in WATER_STACK if cat.unlocked(x)]
    cells = sorted(water)
    for c in cells:
        if sc and rng.random() < cfg.get("lily_density", 0.22):
            P.stack(rng.choice(sc), *c)
    return water


def fill_flower_path_loop(P, rect, rng, cfg):
    """ring path (stone/flower) around the rectangle"""
    i0, i1, j0, j1 = rect
    pc = PATHS["flower"] if P.cat.unlocked(PATHS["flower"]) else PATHS["stone"] if P.cat.unlocked(PATHS["stone"]) else PATHS["dirt"]
    for i in range(i0, i1 + 1):
        for j in range(j0, j1 + 1):
            if i in (i0, i1) or j in (j0, j1):
                P.place(pc, P.A(i), P.A(j), 0, "path")


def fill_houses(P, rect, rng, cfg):
    """rows of farmhouses; cfg['houses'] limits the count (the freed rows are returned via P.house_end)"""
    cat = P.cat
    i0, i1, j0, j1 = rect
    ok, lk = usable(cat, HOUSES, cfg["locked"], rng)
    ok = sorted(ok, key=cat.lv, reverse=True)           # best unlocked houses first
    limit = cfg.get("houses")
    count = 0
    last = j0 - 1
    j = j0
    while j <= j1 and (not limit or count < limit):
        i = i0
        rowh = 0
        while i <= i1 and (not limit or count < limit):
            cls = pick(rng, ok[:2] if limit else ok[:4], lk, 0.12)
            placed = False
            for d in rng.sample([0, 1], 2):
                w, h = cat.dims(cls, d)
                if i + w - 1 <= i1 and j + h - 1 <= j1 and P.fits_corner(cls, P.A(i), P.A(j), d):
                    P.place_corner(cls, P.A(i), P.A(j), d, "house")
                    i += w + 1; rowh = max(rowh, h); count += 1; placed = True
                    break
            if not placed:
                i += 1
        if rowh:
            last = j + rowh - 1
        j += max(rowh, 2) + 1
    P.house_end = last
    return count


def fill_fields(P, rect, rng, cfg, fish=0.0):
    cat = P.cat
    i0, i1, j0, j1 = rect
    crops = [c for c in (cfg.get("crops") or FIELDS.values()) if cat.unlocked(c)]
    n = 0
    j = j0
    stripe = rng.choice(crops)
    while j + 1 <= j1:
        i = i0
        stripe = crops[P.used["rowgroups"] % len(crops)]   # rotate through the chosen crops, one per row group
        P.used["rowgroups"] += 1
        while i + 1 <= i1:
            use = FISH if (fish and cat.unlocked(FISH) and rng.random() < fish and P.left("fish") > 0) else stripe
            kind = "fish" if use == FISH else "plots"
            if P.left(kind) > 0 and P.place_corner(use, P.A(i), P.A(j), 0, "field"):
                n += 1
                P.used[kind] += 1
            i += 2
        j += 2
        if (j - j0) % 6 == 4 and j + 1 <= j1:   # footpath between field groups
            for ii in range(i0, i1 + 1):
                P.place(PATHS["dirt"], P.A(ii), P.A(j), 0, "path")
            j += 1
    return n


def fill_pens(P, rect, rng, cfg):
    cat = P.cat
    i0, i1, j0, j1 = rect
    allowed_pens = cfg.get("pens")
    pens = {k: [c for c in v if (not allowed_pens or c in allowed_pens)] for k, v in PENS.items()}
    kinds = [k for k in pens if any(cat.unlocked(c) or cfg["locked"] for c in pens[k])]
    n = 0
    j = j0
    order = list(kinds); rng.shuffle(order)       # rows cycle through every chosen animal so none is missed
    row = 0
    while j <= j1:
        kind = order[row % len(order)]; row += 1
        ok, lk = usable(cat, pens[kind], cfg["locked"], rng)
        i = i0
        while i <= i1:
            if P.left("pens") <= 0:
                return n
            cls = pick(rng, sorted(ok, key=cat.lv, reverse=True), lk, 0.25 if kind == "cattle" else 0.0)
            if cls is None:
                break
            placed = False
            d = 1                                   # long side along i
            w, h = cat.dims(cls, d)
            if i + w - 1 <= i1 and j + h - 1 <= j1 and P.place_corner(cls, P.A(i), P.A(j), d, "pen"):
                i += w; n += 1; placed = True; P.used["pens"] += 1
            if not placed:
                i += 1
        j += 2
    return n


def fill_orchard(P, rect, rng, cfg):
    cat = P.cat
    i0, i1, j0, j1 = rect
    ok, lk = usable(cat, cfg.get("orchard") or ORCHARD, cfg["locked"], rng)
    fl = [f for f in FLOWERS if cat.unlocked(f)]
    species_rows = {}
    cycle = list(ok); rng.shuffle(cycle)
    ncyc = [0]
    n = 0
    for i in range(i0, i1 + 1):
        for j in range(j0, j1 + 1):
            a, b = P.A(i), P.A(j)
            if not P.free(a, b):
                continue
            dense = bool(cfg.get("prod"))
            if (i % 2 == 0) if dense else (i % 2 == 0 and j % 2 == 0):
                if P.left("fruit") <= 0:
                    continue
                key = (i // 2, j // 5)            # short segments so every chosen species shows up
                if key not in species_rows:
                    species_rows[key] = cycle[ncyc[0] % len(cycle)]; ncyc[0] += 1
                if P.place(species_rows[key], a, b, 0, "tree"):
                    n += 1; P.used["fruit"] += 1
            elif dense:
                if i % 2 == 1 and j % 4 == 1 and rng.random() < 0.5:
                    P.place(rng.choice(fl), a, b, 0, "flower")
            elif i % 2 == 1 and j % 2 == 1 and rng.random() < 0.55:
                P.place(rng.choice(fl), a, b, 0, "flower")
    return n


def fill_leftover(P, rng, cfg):
    """production in the empty space: every free 2x2 block becomes a crop plot (or fish pond), every free single
    cell a fruit tree. Cells right next to a road are kept for fruit trees so plots stay reachable."""
    cat = P.cat
    crops = [c for c in (cfg.get("crops") or FIELDS.values()) if cat.unlocked(c)]
    fruit = [c for c in (cfg.get("orchard") or ORCHARD) if cat.unlocked(c)]
    if not crops:
        return 0
    fish = cfg.get("fish", 0.3)
    n = 0
    km = cfg.get("keep_margin", 3)
    keep = set()                                    # ground kept for the garden: around the pond and by the gates
    for (a, b) in P.pond:
        for da in range(-km, km + 1):
            for db in range(-km, km + 1):
                keep.add((a + da, b + db))
    for (side, aa, bb, cls, cov) in P.gates:
        for (a, b) in cov:
            for da in range(-km, km + 1):
                for db in range(-km, km + 1):
                    keep.add((a + da, b + db))
    for a in range(P.lo, P.hi, 2):
        stripe = rng.choice(crops)
        for b in range(P.lo, P.hi, 2):
            if any(c in keep for c in ((a, b), (a + 1, b), (a, b + 1), (a + 1, b + 1))):
                continue
            if P.free(a, b) and P.free(a + 1, b) and P.free(a, b + 1) and P.free(a + 1, b + 1):
                use = FISH if (fish and cat.unlocked(FISH) and rng.random() < fish * 0.5) else stripe
                if P.place(use, a, b, 0, "field"):
                    n += 1
    if cfg.get("leftover_trees") and fruit:            # single leftover cells -> fruit trees, up to the requested total
        have = sum(1 for q in P.pieces if q["cls"] in ORCHARD)
        room = max(0, cfg.get("fruit_total", 10 ** 6) - have)
        cells = [(a, b) for a in range(P.lo, P.hi + 1) for b in range(P.lo, P.hi + 1) if P.free(a, b)]
        rng.shuffle(cells)
        for (a, b) in cells:
            if room <= 0:
                break
            if P.free(a, b) and P.place(rng.choice(fruit), a, b, 0, "tree"):
                n += 1; room -= 1
    return n


def fill_workshops(P, rect, rng, cfg):
    cat = P.cat
    i0, i1, j0, j1 = rect
    ok, lk = usable(cat, WORKSHOPS, cfg["locked"], rng)
    cands = sorted(ok, key=cat.lv, reverse=True)[:2] + (lk[:2] if cfg["locked"] else [])
    n = 0
    for j in range(j0, j1 + 1, 3):
        for i in range(i0, i1 + 1, 3):
            if n >= cfg.get("workshops", 4):
                return n
            cls = cands[n % len(cands)] if cands else None
            if cls is not None and any(P.place_corner(cls, P.A(i), P.A(j), d, "work") for d in (0, 1)):
                n += 1
    return n


def lay_paths(P, rows, cols, kind="stone"):
    """straight paths: rows = [(i, j0, j1)], cols = [(j, i0, i1)]"""
    cat = P.cat
    pc = PATHS[kind] if cat.unlocked(PATHS[kind]) else PATHS["dirt"]
    for (i, j0, j1) in rows:
        for j in range(j0, j1 + 1):
            P.place(pc, P.A(i), P.A(j), 0, "path")
    for (j, i0, i1) in cols:
        for i in range(i0, i1 + 1):
            P.place(pc, P.A(i), P.A(j), 0, "path")


def structured_decor(P, rng, cfg):
    """tidy decoration: bamboo hedge inside the fence, tree/flower avenues along roads, flower beds, groves"""
    cat = P.cat
    okT, lkT = usable(cat, TREES, cfg["locked"], rng)
    okF, lkF = usable(cat, FLOWERS, cfg["locked"], rng)
    bamboo = 37 if cat.unlocked(37) else None
    N4 = ((1, 0), (-1, 0), (0, 1), (0, -1))
    lo, hi = P.lo, P.hi
    level = cfg.get("decor", "rich")            # rich | moderate | light
    k_bamboo = {"rich": 0.8, "moderate": 0.35, "light": 0.0}[level]
    k_avenue = {"rich": 4, "moderate": 6, "light": 8}[level]
    n_beds = {"rich": 40, "moderate": 10, "light": 4}[level]
    n_groves = {"rich": 10, "moderate": 3, "light": 1}[level]
    k_rest = {"rich": 0.30, "moderate": 0.04, "light": 0.0}[level]

    # (1) bamboo / hedge ring just inside the fence
    if bamboo:
        for a in range(lo, hi + 1):
            for b in range(lo, hi + 1):
                if (a in (lo, hi) or b in (lo, hi)) and P.free(a, b) and rng.random() < k_bamboo:
                    P.place(bamboo, a, b, 0, "tree")

    # (2) avenues: trees and flowers alternating beside every road
    avenue_tree = rng.choice(okT)
    for a in range(lo, hi + 1):
        for b in range(lo, hi + 1):
            if not P.free(a, b):
                continue
            if any(P.occ.get((a + da, b + db)) == "path" for da, db in N4):
                if (a + b) % k_avenue == 0:
                    P.place(avenue_tree, a, b, 0, "tree")
                elif (a + b) % k_avenue == k_avenue // 2 and rng.random() < 0.8:
                    P.place(rng.choice(okF), a, b, 0, "flower")

    # (3) flower beds: free rectangles filled with a single species
    for _ in range(n_beds * 3):
        if sum(1 for q in P.pieces if q['kind'] == 'flower') > 0 and level != 'rich' and _ >= n_beds * 3:
            break
        w, h = rng.choice([(2, 2), (2, 3), (3, 2), (3, 3), (2, 4)])
        a0 = rng.randint(lo, hi - w + 1); b0 = rng.randint(lo, hi - h + 1)
        cells = [(a0 + x, b0 + y) for x in range(w) for y in range(h)]
        if all(P.free(*c) for c in cells):
            fl = pick(rng, okF, lkF, 0.08)
            for c in cells:
                P.place(fl, c[0], c[1], 0, "flower")

    # (4) groves of one tree species, with a rock or pavilion in the middle
    pav = [c for c in PAVILION if cat.unlocked(c)]
    for _ in range(n_groves):
        ca = rng.randint(lo + 2, hi - 2); cb = rng.randint(lo + 2, hi - 2)
        sp = pick(rng, okT, lkT, 0.12)
        cells = [(ca + x, cb + y) for x in range(-2, 3) for y in range(-2, 3) if x * x + y * y <= 5]
        if sum(1 for c in cells if P.free(*c)) < 14:
            continue
        for c in cells:
            if P.free(*c) and rng.random() < 0.8:
                P.place(sp, c[0], c[1], 0, "tree")
        if P.free(ca, cb):
            spot = pav[0] if pav else (ROCK if cat.in_farm(ROCK) else None)
            if spot: P.place(spot, ca, cb, 0, "decor")

    # (5) sparse remainder
    cells = [(a, b) for a in range(lo, hi + 1) for b in range(lo, hi + 1) if P.free(a, b)]
    rng.shuffle(cells)
    for (a, b) in cells[: int(len(cells) * k_rest)]:
        if P.free(a, b):
            if rng.random() < 0.5:
                P.place(pick(rng, okT, lkT, 0.08), a, b, 0, "tree")
            else:
                P.place(pick(rng, okF, lkF, 0.06), a, b, 0, "flower")


def refined_decor(P, rng, cfg):
    """deliberate garden dressing instead of random scatter; uses all the small flowering shrubs"""
    cat = P.cat
    un = cat.unlocked
    lo, hi = P.lo, P.hi
    pick1 = lambda lst: next((c for c in lst if un(c)), None)
    pine = pick1([35, 33, 31]); willow = pick1([27, 33]); gink = pick1([28, 31, 33]); osm = pick1([31, 33]); bamboo = pick1([37])
    shrubs = [f for f in (77, 38, 78, 79, 80, 109, 110) if un(f)]       # small flowering bushes
    rng.shuffle(shrubs)
    N4 = ((1, 0), (-1, 0), (0, 1), (0, -1))
    inward = {0: (1, 0), 1: (0, 1), 2: (-1, 0), 3: (0, -1)}
    cyc = [0]
    sc = cfg.get("decor_scale", 1.0)

    def shrub(k=None):
        if not shrubs:
            return None
        if k is None:
            cyc[0] += 1; k = cyc[0]
        return shrubs[k % len(shrubs)]

    def put(cls, a, b, kind):
        if cls and P.free(a, b):
            return P.place(cls, a, b, 0, kind)
        return False

    # 1) pine groups in the four corners, softened with a shrub in front of each
    for (ca, cb, da, db) in ((lo, lo, 1, 1), (lo, hi, 1, -1), (hi, lo, -1, 1), (hi, hi, -1, -1)):
        for (x, y) in ((1, 1), (1, 2), (2, 1)):
            put(pine, ca + da * x, cb + db * y, "tree")
        put(shrub(), ca + da * 2, cb + db * 2, "flower")

    # 2) paired feature trees flanking every gate, with shrubs between them and the wall
    for (side, aa, bb, cls, cov) in P.gates:
        ia, ib = inward[side]
        ends = (min(cov), max(cov))
        along = (0, 1) if side in (0, 2) else (1, 0)
        for k, c0 in enumerate(ends):
            sgn = -1 if k == 0 else 1
            put(gink, c0[0] + ia + along[0] * sgn, c0[1] + ib + along[1] * sgn, "tree")
            put(pine, c0[0] + 2 * ia + along[0] * sgn, c0[1] + 2 * ib + along[1] * sgn, "tree")
            put(shrub(k), c0[0] + ia + 2 * along[0] * sgn, c0[1] + ib + 2 * along[1] * sgn, "flower")

    # 3) pond: willows at the four extremes, pavilion facing the main gate, two rock groups, shrub ring
    water = sorted(P.pond)
    if water:
        shore = []
        for (a, b) in water:
            for da, db in N4:
                n = (a + da, b + db)
                if P.free(*n) and n not in shore:
                    shore.append(n)
        for key in (lambda c: c[0], lambda c: -c[0], lambda c: c[1], lambda c: -c[1]):
            c = min(shore, key=key) if shore else None
            if c: put(willow, c[0], c[1], "tree")
        gx = P.gates[0] if P.gates else None
        pav = pick1([24])
        if gx and pav and shore:
            gcell = gx[4][len(gx[4]) // 2]
            c = min([x for x in shore if P.free(*x)], key=lambda x: abs(x[0] - gcell[0]) + abs(x[1] - gcell[1]))
            put(pav, c[0], c[1], "decor")
        far = []
        for c in shore:
            if P.free(*c) and (not far or abs(c[0] - far[0][0]) + abs(c[1] - far[0][1]) > 9):
                far.append(c)
            if len(far) == 2:
                break
        if cat.in_farm(ROCK):
            for c in far:
                put(ROCK, c[0], c[1], "decor")
                for da, db in N4:
                    if rng.random() < 0.5 and put(ROCK, c[0] + da, c[1] + db, "decor"):
                        break
        # shrub ring: clumps of three cells of one species around the shore
        order = sorted(shore, key=lambda c: math.atan2(c[1] - sum(w[1] for w in water) / len(water), c[0] - sum(w[0] for w in water) / len(water)))
        for idx, c in enumerate(order):
            if P.free(*c) and rng.random() < 0.85 * sc:
                put(shrub(idx // 3), c[0], c[1], "flower")

    # 4) road borders: shrub clumps of two (species changes clump by clump) with a tree every 6 cells
    pool = [t for t in (28, 29, 30, 33, 31) if un(t)] or [gink or osm]
    road_tree = gink or osm
    for a in range(lo, hi + 1):
        for b in range(lo, hi + 1):
            if not P.free(a, b):
                continue
            if any(P.occ.get((a + da, b + db)) == "path" for da, db in N4):
                per = max(6, int(round(6 / sc)))
                m = (a + b) % per
                if m in (0, 1): put(shrub((a + b) // per), a, b, "flower")
                elif m == per // 2: put(pool[((a + b) // per) % len(pool)], a, b, "tree")

    # 5) rectangular flower beds, next to roads: every bed a different species or two alternating rows
    beds = 0
    cand = [(a, b) for a in range(lo + 2, hi - 2) for b in range(lo + 2, hi - 2)]
    rng.shuffle(cand)
    for (a0, b0) in cand:
        if beds >= int(round(12 * sc)):
            break
        w, h = rng.choice([(3, 2), (2, 3), (3, 3), (2, 2)])
        cells = [(a0 + x, b0 + y) for x in range(w) for y in range(h)]
        if not all(P.free(*c) for c in cells):
            continue
        ring = [(a0 + x, b0 + y) for x in range(-1, w + 1) for y in range(-1, h + 1)]
        if not any(P.occ.get(c) == "path" for c in ring):
            continue
        sa, sb = shrub(beds), shrub(beds + 3)
        for x in range(w):
            for y in range(h):
                put(sa if (x % 2 == 0 or beds % 2 == 0) else sb, a0 + x, b0 + y, "flower")
        beds += 1

    # 6) continuous bamboo hedge on the side opposite the main gate
    if bamboo and P.gates:
        side = {0: 2, 1: 3, 2: 0, 3: 1}[P.gates[0][0]]
        wall = lo if side in (0, 1) else hi
        step = 1 if side in (0, 1) else -1
        for k in range(lo, hi + 1):
            a, b = (wall + step, k) if side in (0, 2) else (k, wall + step)
            put(bamboo, a, b, "tree")

    # 7) every house: a yard tree and foundation shrubs on its free sides
    for p in list(P.pieces):
        if p["kind"] == "house":
            cells = [(p["a"] + da, p["b"] + db) for (da, db) in cat.cells(p["cls"], p["d"])]
            sides = [(a + da, b + db) for (a, b) in cells for da, db in N4 if (a + da, b + db) not in cells]
            sides = sorted(set(sides))
            rng.shuffle(sides)
            tree_done = False
            for c in sides:
                if not tree_done and put(rng.choice(pool), c[0], c[1], "tree"):
                    tree_done = True
                elif rng.random() < 0.5 * sc:
                    put(shrub(), c[0], c[1], "flower")


# ============================== perimeter fence + gates ========================================
def perimeter(P, rng, cfg, gates):
    """fence ring: dir0 on a=lo, dir1 on b=lo, dir2 on a=hi, dir3 on b=hi.
    gates = [(side, center_coordinate, kind)]; the gate replaces the fence pieces under its footprint."""
    cat = P.cat
    fcls = [c for c in FENCES if cat.unlocked(c)]
    style = cfg.get("fence_class") or rng.choice(fcls)
    lo, hi = P.lo, P.hi
    for b in range(lo, hi + 1):
        P.edge(style, lo, b, 0, "fence"); P.edge(style, hi, b, 2, "fence")
    for a in range(lo, hi + 1):
        P.edge(style, a, lo, 1, "fence"); P.edge(style, a, hi, 3, "fence")
    small = [c for c in GATE_SMALL if cat.unlocked(c)] or GATE_SMALL
    big = [c for c in GATE_BIG if cat.unlocked(c)] or GATE_BIG
    for (side, center, kind) in gates:
        cls = big[0] if kind == "big" else rng.choice(small)
        cs = cat.cells(cls, side)
        wall = lo if side in (0, 1) else hi
        if side in (0, 2):                       # wall runs along b
            vals = [db for (da, db) in cs]
            b_anchor = center - (min(vals) + max(vals)) // 2
            a_anchor = wall - cs[0][0]
            anchor = (a_anchor, b_anchor)
        else:                                    # wall runs along a
            vals = [da for (da, db) in cs]
            a_anchor = center - (min(vals) + max(vals)) // 2
            b_anchor = wall - cs[0][1]
            anchor = (a_anchor, b_anchor)
        for (da, db) in cs:
            P.remove_edge_cell(anchor[0] + da, anchor[1] + db, side)
        P.edge(cls, anchor[0], anchor[1], side, "gate")
        P.gates.append((side, anchor[0], anchor[1], cls, [(anchor[0] + da, anchor[1] + db) for (da, db) in cs]))
    return style


def fence_zone(P, rect, cls, rng=None, gate=True):
    """fence pieces on the outer border of a zone rectangle (i0,i1,j0,j1) with one gate facing the plot centre"""
    i0, i1, j0, j1 = rect
    for i in range(i0, i1 + 1):
        P.edge(cls, P.A(i), P.A(j0), 1, "fence")
        P.edge(cls, P.A(i), P.A(j1), 3, "fence")
    for j in range(j0, j1 + 1):
        P.edge(cls, P.A(i0), P.A(j), 0, "fence")
        P.edge(cls, P.A(i1), P.A(j), 2, "fence")
    if gate:
        ci, cj = (i0 + i1) / 2.0, (j0 + j1) / 2.0
        mid = (P.n - 1) / 2.0
        gcls = [c for c in GATE_SMALL if P.cat.unlocked(c)][-1]
        # choose the wall closest to the plot centre
        opts = [(abs(i0 - mid), 0, P.A(i0), P.A(int(cj))), (abs(i1 - mid), 2, P.A(i1), P.A(int(cj))),
                (abs(j0 - mid), 1, P.A(int(ci)), P.A(j0)), (abs(j1 - mid), 3, P.A(int(ci)), P.A(j1))]
        opts.sort()
        _, side, aa, bb = opts[0]
        P.remove_edge_cell(aa, bb, side)
        P.edge(gcls, aa, bb, side, "gate")


# ============================== templates =======================================================
def tpoint(i, j, t, n):
    if t & 1: i = n - 1 - i
    if t & 2: j = n - 1 - j
    if t & 4: i, j = j, i
    return i, j


def gate_for(i, j, t, n):
    """template-space point on the wall -> (side, absolute coordinate along the wall) after transform t"""
    ti, tj = tpoint(i, j, t, n)
    if ti == n - 1: return 2, tj
    if ti == 0: return 0, tj
    if tj == n - 1: return 3, ti
    return 1, ti


def path_cells(rows, cols, t, n):
    cells = set()
    for (i, j0, j1) in rows:
        for j in range(j0, j1 + 1): cells.add(tpoint(i, j, t, n))
    for (j, i0, i1) in cols:
        for i in range(i0, i1 + 1): cells.add(tpoint(i, j, t, n))
    return cells


def organic_ring(P, rng, cfg):
    """two winding flower-path arcs on opposite sides of the pond (not a closed frame), joined to the roads by spokes"""
    water = P.pond
    if not water:
        return
    cat = P.cat
    arc_cls = PATHS["flower"] if cat.unlocked(PATHS["flower"]) else PATHS["stone"] if cat.unlocked(PATHS["stone"]) else PATHS["dirt"]
    road_cls = PATHS["stone"] if cat.unlocked(PATHS["stone"]) else PATHS["dirt"]
    cells = [(a, b) for a in range(P.lo, P.hi + 1) for b in range(P.lo, P.hi + 1)]
    cx = sum(w[0] for w in water) / len(water); cy = sum(w[1] for w in water) / len(water)
    N4 = ((1, 0), (-1, 0), (0, 1), (0, -1))
    ring = []
    for c in cells:
        if not P.free(*c):
            continue
        d = min(max(abs(c[0] - w[0]), abs(c[1] - w[1])) for w in water)
        if d == 2 and not any(P.occ.get((c[0] + da, c[1] + db)) == "path" for da, db in N4):
            ring.append(c)
    ring.sort(key=lambda c: math.atan2(c[1] - cy, c[0] - cx))
    if not ring:
        return
    n = len(ring)
    s0 = rng.random()
    keep = []
    for k, c in enumerate(ring):
        f = (k / n - s0) % 1.0
        if f < 0.34 or 0.5 <= f < 0.84:          # two arcs of about a third of the shore each
            keep.append(c)
    for c in keep:
        P.place(arc_cls, c[0], c[1], 0, "path")
    # spokes from the arc ends straight away from the pond until a road is met
    ends = [c for k, c in enumerate(ring) if ((k / n - s0) % 1.0) < 0.02 or 0.5 <= ((k / n - s0) % 1.0) < 0.52
            or 0.32 <= ((k / n - s0) % 1.0) < 0.34 or 0.82 <= ((k / n - s0) % 1.0) < 0.84]
    for c in ends:
        da = 1 if c[0] > cx else -1
        db = 1 if c[1] > cy else -1
        best = None
        for (sa, sb) in ((da, 0), (0, db)):
            a, b = c
            path = []
            hit = False
            for _ in range(14):
                a += sa; b += sb
                if not P.inb(a, b):
                    break
                if P.occ.get((a, b)) == "path":
                    hit = True
                    break
                if not P.free(a, b):
                    break
                path.append((a, b))
            if hit and path and (best is None or len(path) < len(best)):
                best = path
        if best:
            for (a2, b2) in best:
                P.place(road_cls, a2, b2, 0, "path")


def meander_cells(i, j0, j1, amp, period, phase):
    """a gently winding, 4-connected east-west road (template space)"""
    cells = set()
    prev = None
    for j in range(j0, j1 + 1):
        ii = i + int(round(amp * math.sin(j / period + phase)))
        if prev is not None and prev != ii:
            step = 1 if ii > prev else -1
            for k in range(prev, ii + step, step):
                cells.add((k, j))
        cells.add((ii, j))
        prev = ii
    return cells


def template_garden(n, rng, cfg=None):
    """central garden pond with ring path; houses/fields behind, orchard / workshops / animals in front"""
    t = rng.randrange(8)
    R = lambda r: transform_rect(r, t, n)
    zones = {"houses": R((1, 8, 1, 13)), "fields": R((1, 8, 18, 30)), "pond": R((12, 20, 8, 23)),
             "orchard": R((24, 30, 1, 13)), "pens": R((24, 30, 17, 30)), "works": R((24, 30, 14, 17))}
    ring = R((10, 22, 6, 25))
    if cfg and cfg.get("prod"):
        # lean towards income: crop strips on both sides of the pond
        zones["fields2"] = R((11, 21, 1, 4))
        zones["fields3"] = R((11, 21, 27, 30))
        zones["works"] = R((24, 30, 9, 18))
        if cfg.get("pond") == "small":
            # smaller pond: the freed ground makes the crop strips three plots wide
            zones["pond"] = R((12, 20, 10, 21))
            ring = R((10, 22, 8, 23))
            zones["fields2"] = R((11, 21, 1, 6))
            zones["fields3"] = R((11, 21, 25, 30))
    rows = [(9, 0, 31), (22, 0, 31)]
    cols = [(15, 0, 9), (15, 22, 31), (16, 22, 31), (14, 0, 9)]
    gates = [(31, 15, "big"), (15, 31, "small"), (15, 0, "small")]
    if cfg and cfg.get("center") == "organic":
        zones["pond"] = R((13, 19, 9, 22))          # long kidney pond, room for the winding path around it
        if cfg.get("tight"):
            zones["pond"] = R((14, 18, 11, 20))     # compact pond: more ground for crops
        if (cfg.get("budget") or {}).get("fish", 99) <= 6:
            zones["pond"] = R((14, 19, 12, 21))     # few fish ponds: keep the pond a compact blob, not a thin canal
        ph = [rng.uniform(0, 6.28) for _ in range(2)]
        mc = set()
        for idx, i in enumerate((9, 22)):
            mc |= meander_cells(i, 0, 31, 1.2, 4.2 + idx * 1.3, ph[idx])
        pc = {tpoint(i, j, t, n) for (i, j) in mc} | path_cells([], cols, t, n)
        return t, zones, pc, None, gates
    return t, zones, path_cells(rows, cols, t, n), ring, gates


def template_courtyard(n, rng, cfg=None):
    """houses around a central yard; pond garden on one side, fields and orchard on the outside"""
    t = rng.randrange(8)
    R = lambda r: transform_rect(r, t, n)
    zones = {"houses": R((12, 19, 10, 22)), "pond": R((3, 9, 9, 22)), "fields": R((23, 30, 1, 14)),
             "orchard": R((23, 30, 17, 30)), "pens": R((1, 9, 1, 7)), "works": R((1, 9, 25, 30))}
    rows = [(10, 0, 31), (21, 0, 31), (11, 0, 31)]
    cols = [(8, 10, 21), (23, 10, 21), (15, 22, 31)]
    ring = None
    gates = [(31, 15, "big"), (15, 31, "small"), (15, 0, "small")]
    return t, zones, path_cells(rows, cols, t, n), ring, gates


def template_harvest(n, rng, cfg=None):
    """production focus: houses + workshops in the back, big fields / fish ponds in the middle,
    orchard + small koi garden + animal pens in front. Light decoration."""
    t = rng.randrange(8)
    R = lambda r: transform_rect(r, t, n)
    zones = {"houses": R((1, 9, 1, 13)), "works": R((1, 9, 17, 30)),
             "fields": R((11, 21, 1, 14)), "fields2": R((11, 21, 17, 30)),
             "orchard": R((23, 30, 1, 8)), "pond": R((23, 30, 9, 14)), "pens": R((23, 30, 17, 30))}
    if cfg and cfg.get("pond") == "big":
        # a real garden pond: take more of the front-left area, orchard shrinks to a strip
        zones["orchard"] = R((23, 30, 1, 4)); zones["pond"] = R((23, 30, 5, 14))
    rows = [(10, 0, 31), (22, 0, 31)]
    cols = [(15, 0, 31), (16, 0, 31)]
    gates = [(31, 15, "big"), (15, 31, "small"), (15, 0, "small")]
    return t, zones, path_cells(rows, cols, t, n), None, gates


TEMPLATES = {"garden": template_garden, "courtyard": template_courtyard, "harvest": template_harvest}


def design(cat, style="garden", seed=1, locked=True, lo=-9, n=32, fence_class=None, decor=None, pond=None, prod=0, opts=None):
    rng = random.Random(seed)
    P = Plot(cat, lo, n)
    cfg = {"locked": locked, "fence_class": fence_class, "workshops": 3, "pavilions": 2, "lily_density": 0.28, "decor": "rich"}
    tname = style if style in TEMPLATES else rng.choice(sorted(TEMPLATES))
    if decor: cfg["decor"] = decor
    if prod: cfg["prod"] = prod; cfg["workshops"] = 5
    if opts: cfg.update(opts)
    if pond: cfg["pond"] = pond
    if tname == "harvest":
        cfg.update({"workshops": 9, "pavilions": 1, "lily_density": 0.2, "decor": decor or "moderate", "works_orchard": True})
    t, zones, pcells, ring, gates = TEMPLATES[tname](n, rng, cfg)
    P.budget = dict(cfg.get("budget") or {})
    reserve = list(cfg.get("reserve") or [])

    # 1) perimeter fence + gates
    gl = []
    for (gi, gj, kind) in gates:
        side, coord = gate_for(gi, gj, t, n)
        gl.append((side, P.A(coord), kind))
    perimeter(P, rng, cfg, gl)

    # 2) roads
    pc = PATHS["stone"] if cat.unlocked(PATHS["stone"]) else PATHS["dirt"]
    for (i, j) in sorted(pcells):
        P.place(pc, P.A(i), P.A(j), 0, "path")
    if ring:
        fill_flower_path_loop(P, ring, rng, cfg)

    # 2b) ground reserved for later development (kept empty: no buildings, no decoration)
    for key in reserve:
        if key in zones:
            for (a, b) in P.rect_cells(zones[key]):
                if P.free(a, b):
                    P.occ[(a, b)] = "reserve"; P.reserved.add((a, b))

    # 3) zones
    if cat.in_farm(51) and cat.unlocked(51):
        fill_pond_fish(P, zones["pond"], rng, cfg)
    else:
        fill_pond(P, zones["pond"], rng, cfg)
    if cfg.get("center") == "organic":
        organic_ring(P, rng, cfg)
    fill_houses(P, zones["houses"], rng, cfg)
    if cfg.get("houses") and getattr(P, "house_end", None) is not None:
        hz = zones["houses"]
        rem = (hz[0], hz[1], P.house_end + 3, hz[3])         # keep a lane below the houses, then crops
        if rem[3] - rem[2] >= 1:
            fill_fields(P, rem, rng, cfg, fish=cfg.get("fish", 0.3))
            fence_zone(P, rem, 105 if cat.unlocked(105) else [c for c in FENCES if cat.unlocked(c)][-1])
            fill_orchard(P, (hz[0], hz[1], P.house_end + 1, rem[3]), rng, cfg)   # fruit rows on any ground still free
    fill_fields(P, zones["fields"], rng, cfg)
    for k in ("fields2", "fields3"):
        if k in zones:
            fill_fields(P, zones[k], rng, cfg, fish=cfg.get("fish", 0.3))
    fill_pens(P, zones["pens"], rng, cfg)
    if cfg.get("prod"):
        fill_workshops(P, zones["works"], rng, cfg)       # workshops first, orchard fills what is left
        fill_orchard(P, zones["orchard"], rng, cfg)
        if cfg.get("workshops", 1) == 0 or cfg.get("works_orchard"):
            fill_orchard(P, zones["works"], rng, cfg)     # no workshops requested: use that ground for fruit trees
    else:
        fill_orchard(P, zones["orchard"], rng, cfg)
        fill_workshops(P, zones["works"], rng, cfg)
    if cfg.get("works_orchard"):
        fill_orchard(P, zones["works"], rng, cfg)       # use the free ground beside the workshops for fruit trees

    # 4) fences around animals + fields
    fcls = cfg["fence_class"] or [c for c in FENCES if cat.unlocked(c)][-1]
    f_pen = 103 if cat.unlocked(103) else fcls                    # a different fence style for each kind of zone
    f_field = 105 if cat.unlocked(105) else fcls
    if "pens" not in reserve:
        fence_zone(P, zones["pens"], f_pen)
    if "fields" not in reserve:
        fence_zone(P, zones["fields"], f_field)
    for k in ("fields2", "fields3"):
        if k in zones and k not in reserve:
            fence_zone(P, zones[k], f_field)
    if tname == "harvest":
        fence_zone(P, zones["orchard"], fcls)
        fence_zone(P, zones["works"], fcls)

    # 5) decoration first (garden look), then production in the space that is really still empty
    if cfg.get("decor") == "refined":
        refined_decor(P, rng, cfg)
    else:
        structured_decor(P, rng, cfg)
    if cfg.get("prod") and not cfg.get("no_leftover"):
        fill_leftover(P, rng, cfg)
    return P, tname, t


# ============================== reports, validation, output ====================================
def stats(P, baseline_env=28):
    cat = P.cat
    tot = collections.Counter()
    cost = gold = 0
    locked_pieces = collections.Counter()
    for p in P.pieces:
        c = p["cls"]
        cost += cat.cost(c); gold += cat.gold(c)
        if not cat.unlocked(c):
            locked_pieces[c] += 1
            continue
        s = cat.stats(c)
        for k, v in s.items():
            tot[k] += v
    return {"farmers_max": tot["cap"] * 5, "env": tot["env"] + baseline_env, "saf": tot["saf"], "conv": tot["conv"],
            "renown_cost": tot["ren"], "cost": cost, "gold": gold, "locked": locked_pieces, "pieces": len(P.pieces)}


def validate(P):
    cat = P.cat
    occ = {}
    bad = []
    for p in P.pieces:
        c, a, b, d = p["cls"], p["a"], p["b"], p["d"]
        if c in EDGE_CLASSES:
            for (da, db) in cat.cells(c, d):
                if not P.inb(a + da, b + db): bad.append(("edge out of plot", p))
            continue
        if p["kind"] == "lily":
            continue
        for (da, db) in cat.cells(c, d):
            cell = (a + da, b + db)
            if not P.inb(*cell): bad.append(("out of plot", p))
            if cell in occ: bad.append(("overlap", p, occ[cell]))
            occ[cell] = p
    return bad


def write_es3(P, path, acres=4):
    cat = P.cat
    s = stats(P)
    rows = []
    for k, p in enumerate(P.pieces):
        rows.append(["LTB%d" % k, str(p["cls"]), "1", "%d|%d" % (p["a"], p["b"]), str(p["d"]), str(acres), "1|1", "0", "1|1", "0", "0", "0"])
    data = ["Z", str(acres), str(s["cost"]), str(s["gold"])]
    obj = {"LanTuBuild": {"__type": ES3_TYPE_LL, "value": rows}, "LanTuData": {"__type": ES3_TYPE_L, "value": data}}
    with open(path, "wb") as f:
        f.write(json.dumps(obj, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))
    return s


KIND_COLOR = {"fixed": "#ff9800", "path": "#b8b4a8", "house": "#a0522d", "field": "#c9b037", "pen": "#d9822b", "water": "#4aa3d8", "tree": "#2e8b57",
              "flower": "#e0609a", "decor": "#8a7f9a", "work": "#5b4a7a", "lily": "#7ad7a1", "fence": "#222", "gate": "#e33"}


def preview_html(P, path, title, st, tname, t, seed):
    cat = P.cat
    cell = 18
    N = P.n
    out = []
    out.append("<!doctype html><meta charset='utf-8'><title>%s</title><body style='font:14px system-ui;background:#fafafa;margin:16px'>" % title)
    out.append("<h3>%s</h3>" % title)
    out.append("<p>template <b>%s</b> t=%d seed=%d | pieces %d | farmers max %d | env %d | safety %d | convenience %d | cost %s | locked pieces %d</p>" % (
        tname, t, seed, st["pieces"], st["farmers_max"], st["env"], st["saf"], st["conv"], format(st["cost"], ","), sum(st["locked"].values())))
    out.append("<svg width='%d' height='%d' style='background:#eef3e6;border:1px solid #999'>" % (N * cell, N * cell))
    for p in P.pieces:
        c, a, b, d = p["cls"], p["a"], p["b"], p["d"]
        locked = not cat.unlocked(c)
        if c in EDGE_CLASSES:
            x = (b - P.lo) * cell; y = (a - P.lo) * cell
            col = KIND_COLOR["gate" if p["kind"] == "gate" else "fence"]
            if d == 0: out.append("<rect x='%d' y='%d' width='%d' height='3' fill='%s'/>" % (x, y, cell, col))
            if d == 2: out.append("<rect x='%d' y='%d' width='%d' height='3' fill='%s'/>" % (x, y + cell - 3, cell, col))
            if d == 1: out.append("<rect x='%d' y='%d' width='3' height='%d' fill='%s'/>" % (x, y, cell, col))
            if d == 3: out.append("<rect x='%d' y='%d' width='3' height='%d' fill='%s'/>" % (x + cell - 3, y, cell, col))
            continue
        col = KIND_COLOR.get(p["kind"], "#999")
        for (da, db) in cat.cells(c, d):
            x = (b + db - P.lo) * cell; y = (a + da - P.lo) * cell
            if p["kind"] == "lily":
                out.append("<circle cx='%d' cy='%d' r='4' fill='%s'/>" % (x + cell // 2, y + cell // 2, col))
            else:
                out.append("<rect x='%d' y='%d' width='%d' height='%d' fill='%s' stroke='%s' stroke-width='1' opacity='%s'/>" % (
                    x + 1, y + 1, cell - 2, cell - 2, col, "#d00" if locked else "#0003", "0.55" if locked else "1"))
    out.append("</svg>")
    out.append("<p style='color:#666'>แกนตั้ง = a (บนลงล่าง), แกนนอน = b | เส้นแดงรอบช่อง = อาคารที่ยังติดล็อก | ประตู=แดง รั้ว=ดำ</p>")
    open(path, "w", encoding="utf-8").write("\n".join(out))


def render_png(P, path, scale=22):
    """isometric preview that matches the in-game view (a grows down-left, b grows down-right)"""
    from PIL import Image, ImageDraw
    cat = P.cat
    N = P.n
    W = N * scale + 80
    H = int(N * scale / 2) + 110
    img = Image.new("RGB", (W, H), (238, 243, 230))
    dr = ImageDraw.Draw(img)
    ox, oy = W / 2, 40
    hw, hh = scale / 2, scale / 4

    def c2(a, b):
        return ox + (b - a) * hw, oy + (a + b - 2 * P.lo) * hh

    def diamond(a, b, shrink=0.0):
        x, y = c2(a, b)
        k = 1 - shrink
        return [(x, y - hh * k), (x + hw * k, y), (x, y + hh * k), (x - hw * k, y)]

    # plot ground
    for a in range(P.lo, P.hi + 1):
        for b in range(P.lo, P.hi + 1):
            dr.polygon(diamond(a, b), fill=(214, 226, 196) if (a + b) % 2 else (208, 221, 190))
    for (a, b) in sorted(P.reserved):
        dr.polygon(diamond(a, b, 0.04), fill=(222, 214, 240), outline=(170, 150, 210))
    order = sorted(P.pieces, key=lambda p: (p["a"] + p["b"], p["a"]))
    for p in order:
        c, a, b, d, kind = p["cls"], p["a"], p["b"], p["d"], p["kind"]
        locked = not cat.unlocked(c)
        if c in EDGE_CLASSES:
            continue
        col = tuple(int(KIND_COLOR.get(kind, "#999999")[i:i + 2], 16) for i in (1, 3, 5))
        for (da, db) in cat.cells(c, d):
            if kind == "lily":
                x, y = c2(a + da, b + db)
                dr.ellipse([x - 4, y - 3, x + 4, y + 3], fill=col)
                continue
            poly = diamond(a + da, b + db, 0.08)
            dr.polygon(poly, fill=col, outline=(255, 40, 40) if locked else (60, 60, 60))
            if kind in ("tree", "house", "work", "pen"):
                x, y = c2(a + da, b + db)
                dr.ellipse([x - 3, y - 7, x + 3, y - 1], fill=tuple(max(0, v - 40) for v in col))
    for p in P.pieces:
        c, a, b, d = p["cls"], p["a"], p["b"], p["d"]
        if c not in EDGE_CLASSES:
            continue
        for (da, db) in cat.cells(c, d):
            x, y = c2(a + da, b + db)
            T, R_, B, L = (x, y - hh), (x + hw, y), (x, y + hh), (x - hw, y)
            seg = {0: (T, R_), 1: (L, T), 2: (L, B), 3: (B, R_)}[d]
            dr.line([seg[0], seg[1]], fill=(230, 40, 40) if p["kind"] == "gate" else (50, 40, 30), width=3 if p["kind"] == "gate" else 2)
    img.save(path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default=DEFAULT_DATA)
    ap.add_argument("--style", default="garden", help="garden | courtyard | random")
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--family", type=int, default=12, help="your family level (buildings above it are 'locked')")
    ap.add_argument("--no-locked", action="store_true", help="do not place buildings above your family level")
    ap.add_argument("--out", default="farm.es3")
    ap.add_argument("--decor", default=None, choices=["rich", "moderate", "light", "refined"], help="decoration level")
    ap.add_argument("--pond", default=None, choices=["normal", "big", "small"])
    ap.add_argument("--center", default=None, choices=["frame", "organic"], help="organic = kidney pond, winding paths (no rectangular frame)")
    ap.add_argument("--prod", type=int, default=0, help="1 = lean towards income (more crops/fish ponds/workshops)")
    ap.add_argument("--crops", default=None, help="field class ids, e.g. 67,133,50")
    ap.add_argument("--pens", default=None, help="pen class ids, e.g. 43,44,45,46,65,66")
    ap.add_argument("--orchard", default=None, help="fruit tree class ids")
    ap.add_argument("--workshops", type=int, default=None)
    ap.add_argument("--max-plots", type=int, default=None, help="cap on crop plots")
    ap.add_argument("--max-fish", type=int, default=None, help="cap on fish ponds (central pond included)")
    ap.add_argument("--max-pens", type=int, default=None, help="cap on animal pens")
    ap.add_argument("--reserve", default=None, help="comma list of zones to keep empty for later: fields,pens,orchard,works,houses,fields2,fields3")
    ap.add_argument("--decor-scale", type=float, default=None, help="refined decoration amount, 1.0 = full, 0.4 = light")
    ap.add_argument("--no-leftover", action="store_true", help="do not fill free blocks with extra production")
    ap.add_argument("--fruit", type=int, default=None, help="total number of fruit trees to aim for")
    ap.add_argument("--tight", action="store_true", help="denser production: compact pond, light decoration, fill every free block")
    ap.add_argument("--houses", type=int, default=None, help="limit the number of farmhouses (freed space becomes crop plots)")
    ap.add_argument("--fish", type=float, default=None, help="share of crop plots turned into fish ponds (0..1)")
    ap.add_argument("--preview", default=None)
    ap.add_argument("--png", default=None, help="isometric preview image")
    a = ap.parse_args()
    cat = Catalog(a.data, a.family)
    ids = lambda v: [int(x) for x in v.split(",")] if v else None
    opts = {}
    if a.crops: opts["crops"] = ids(a.crops)
    if a.pens: opts["pens"] = ids(a.pens)
    if a.orchard: opts["orchard"] = ids(a.orchard)
    if a.workshops is not None: opts["workshops"] = a.workshops
    if a.fish is not None: opts["fish"] = a.fish
    if a.houses is not None: opts["houses"] = a.houses
    budget = {}
    if a.max_plots is not None: budget["plots"] = a.max_plots
    if a.max_fish is not None: budget["fish"] = a.max_fish
    if a.max_pens is not None: budget["pens"] = a.max_pens
    if a.fruit is not None:
        opts["fruit_total"] = a.fruit; budget["fruit"] = a.fruit
    if budget: opts["budget"] = budget
    if a.reserve: opts["reserve"] = [x.strip() for x in a.reserve.split(",") if x.strip()]
    if a.no_leftover: opts["no_leftover"] = True
    if a.decor_scale is not None: opts["decor_scale"] = a.decor_scale
    if a.tight: opts.update({"tight": True, "decor_scale": 0.4, "keep_margin": 1, "leftover_trees": True})
    if a.center: opts["center"] = a.center
    P, tname, t = design(cat, a.style, a.seed, locked=not a.no_locked, decor=a.decor, pond=a.pond, prod=a.prod, opts=opts)
    bad = validate(P)
    st = write_es3(P, a.out)
    prev = a.preview or os.path.splitext(a.out)[0] + "_preview.html"
    preview_html(P, prev, os.path.basename(a.out), st, tname, t, a.seed)
    png = a.png or os.path.splitext(a.out)[0] + "_iso.png"
    render_png(P, png)
    print("template", tname, "transform", t, "| pieces", st["pieces"], "| validation problems:", len(bad))
    for b in bad[:5]: print("   ", b[0], b[1])
    print("farmers max %d | env %d | safety %d | convenience %d | renown cost %d" % (st["farmers_max"], st["env"], st["saf"], st["conv"], st["renown_cost"]))
    print("cost %s coin, %s gold | locked pieces %d (%s)" % (format(st["cost"], ","), st["gold"], sum(st["locked"].values()),
          ", ".join("%s x%d" % (cat.name(c), n) for c, n in st["locked"].most_common(8))))
    kinds = collections.Counter(p["kind"] for p in P.pieces if p["cls"] not in EDGE_CLASSES)
    major = collections.Counter(p["cls"] for p in P.pieces if p["kind"] in ("house", "field", "pen", "work"))
    print("contents:", dict(kinds), "| fences/gates:", sum(1 for p in P.pieces if p["cls"] in EDGE_CLASSES))
    print("buildings:", ", ".join("%s x%d" % (cat.name(c), n) for c, n in sorted(major.items())))
    print("wrote", a.out, "and", prev, "and", png)


if __name__ == "__main__":
    main()
