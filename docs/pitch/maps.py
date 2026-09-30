"""Chapter-opener backgrounds: one region of the world per chapter, land a shade darker than the page.

Run from docs/pitch: `python maps.py` rewrites art/chapter-1.svg to art/chapter-5.svg. The coastlines and borders
are Natural Earth's 1:50m countries (public domain), fetched once from the world-atlas package into dist/.
"""
import json, math, os, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'art')
DATA = os.path.join(HERE, 'dist', 'countries-50m.json')
if not os.path.exists(DATA):
    os.makedirs(os.path.dirname(DATA), exist_ok=True)
    urllib.request.urlretrieve('https://cdn.jsdelivr.net/npm/world-atlas@2/countries-50m.json', DATA)
topo = json.load(open(DATA, encoding='utf8'))
sx, sy = topo['transform']['scale']
tx, ty = topo['transform']['translate']

arcs = []
for arc in topo['arcs']:
    x = y = 0
    pts = []
    for dx, dy in arc:
        x += dx
        y += dy
        pts.append((x * sx + tx, y * sy + ty))
    arcs.append(pts)


def ring(idx):
    pts = []
    for i in idx:
        a = arcs[i] if i >= 0 else arcs[~i][::-1]
        pts.extend(a if not pts else a[1:])
    return pts


polys = []  # (country name, [rings])
for g in topo['objects']['countries']['geometries']:
    name = g.get('properties', {}).get('name', '')
    if g['type'] == 'Polygon':
        polys.append((name, [ring(r) for r in g['arcs']]))
    elif g['type'] == 'MultiPolygon':
        for p in g['arcs']:
            polys.append((name, [ring(r) for r in p]))

W, H = 1056, 816


def merc(lat):
    lat = max(min(lat, 85), -85)
    return math.degrees(math.log(math.tan(math.pi / 4 + math.radians(lat) / 2)))


# page colour, land colour, centre lon/lat, px per degree, where the centre sits on the page
regions = {
    1: ('#005CB9', '#004A96', (18, 3), 10.2, (700, 395)),     # Africa
    2: ('#00A7E1', '#0094C8', (80, 22), 19.0, (690, 330)),    # India and South Asia
    3: ('#003049', '#001F30', (113, 6), 13.5, (640, 360)),    # Southeast Asia
    4: ('#6CC4EA', '#5AB2D9', (-72, -6), 8.4, (720, 400)),    # Latin America
    5: ('#FF6B00', '#EB6200', (151, -7), 17.0, (640, 330)),   # New Guinea and the western Pacific
}

for n, (bg, land, (lon0, lat0), k, (cx, cy)) in regions.items():
    my0 = merc(lat0)

    def proj(lon, lat):
        dl = lon - lon0
        if dl > 180:
            dl -= 360
        if dl < -180:
            dl += 360
        return cx + dl * k, cy - (merc(lat) - my0) * k

    paths = []
    for name, rings in polys:
        pr = [[proj(lo, la) for lo, la in r] for r in rings]
        xs = [p[0] for r in pr for p in r]
        ys = [p[1] for r in pr for p in r]
        if max(xs) < -20 or min(xs) > W + 20 or max(ys) < -20 or min(ys) > H + 20:
            continue
        # a ring crossing the 180th meridian jumps across the whole map; drop it rather than smear it
        pr = [r for r in pr if all(abs(a[0] - b[0]) < 300 for a, b in zip(r, r[1:]))]
        if not pr:
            continue
        d = []
        for r in pr:
            simplified = [r[0]]
            for p in r[1:]:
                if abs(p[0] - simplified[-1][0]) + abs(p[1] - simplified[-1][1]) >= 0.6:
                    simplified.append(p)
            if len(simplified) < 3:
                continue
            d.append('M' + 'L'.join(f'{x:.1f} {y:.1f}' for x, y in simplified) + 'Z')
        if d:
            paths.append(''.join(d))
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}" aria-hidden="true">'
           f'<rect width="{W}" height="{H}" fill="{bg}"/>'
           f'<g fill="{land}" stroke="{bg}" stroke-width="0.9" stroke-linejoin="round" fill-rule="evenodd">'
           + ''.join(f'<path d="{d}"/>' for d in paths) + '</g></svg>\n')
    open(os.path.join(OUT, f'chapter-{n}.svg'), 'w', encoding='utf8', newline='\n').write(svg)
    print(n, len(paths), 'shapes', len(svg) // 1024, 'KB')
