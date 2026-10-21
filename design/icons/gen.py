"""Генерация иконок Workspace 1C (вариант B): SVG -> PNG (Chromium) -> ICO."""
import os, sys, json
from pathlib import Path
from PIL import Image
from playwright.sync_api import sync_playwright

OUT = Path(sys.argv[1])
OUT.mkdir(parents=True, exist_ok=True)

PALETTES = {
    "graphite": dict(bg1="#27272A", bg2="#0C0C0E", a1="#FCD34D", a2="#F59E0B", play="#451A03",
                     tile="#3F3F46", tile_small="#52525B", outline="#71717A", plus="#A1A1AA", glow=0.30, rim="#FFFFFF", rimo=0.10),
    "night":    dict(bg1="#1E293B", bg2="#020617", a1="#FCD34D", a2="#F59E0B", play="#451A03",
                     tile="#334155", outline="#64748B", plus="#94A3B8", glow=0.30, rim="#FFFFFF", rimo=0.10),
    "amber":    dict(bg1="#FCD34D", bg2="#F59E0B", a1="#27272A", a2="#09090B", play="#FBBF24",
                     tile="#FEF3C7", outline="#92400E", plus="#92400E", glow=0.0, rim="#FFFFFF", rimo=0.25),
    "light":    dict(bg1="#FFFFFF", bg2="#E9E9EC", a1="#FCD34D", a2="#F59E0B", play="#451A03",
                     tile="#D4D4D8", outline="#A1A1AA", plus="#71717A", glow=0.18, rim="#000000", rimo=0.10),
}

def full_svg(p, uid="x"):
    """Полная иконка, viewBox 120 (для 40 px и больше)."""
    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 120">
<defs>
<linearGradient id="{uid}bg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="{p['bg1']}"/><stop offset="1" stop-color="{p['bg2']}"/></linearGradient>
<linearGradient id="{uid}am" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="{p['a1']}"/><stop offset="1" stop-color="{p['a2']}"/></linearGradient>
<radialGradient id="{uid}gl" cx="0.34" cy="0.34" r="0.45"><stop offset="0" stop-color="#F59E0B" stop-opacity="{p['glow']}"/><stop offset="1" stop-color="#F59E0B" stop-opacity="0"/></radialGradient>
</defs>
<rect width="120" height="120" rx="27" fill="url(#{uid}bg)"/>
<rect width="120" height="120" rx="27" fill="url(#{uid}gl)"/>
<rect x="0.75" y="0.75" width="118.5" height="118.5" rx="26.25" fill="none" stroke="{p['rim']}" stroke-opacity="{p['rimo']}" stroke-width="1.5"/>
<rect x="25" y="25" width="32" height="32" rx="9" fill="url(#{uid}am)"/>
<path d="M37 33.5 L48.5 41 L37 48.5 Z" fill="{p['play']}" stroke="{p['play']}" stroke-width="1.5" stroke-linejoin="round"/>
<rect x="63" y="25" width="32" height="32" rx="9" fill="{p['tile']}"/>
<rect x="25" y="63" width="32" height="32" rx="9" fill="{p['tile']}"/>
<rect x="64.25" y="64.25" width="29.5" height="29.5" rx="7.75" fill="none" stroke="{p['outline']}" stroke-width="2.5"/>
<path d="M79 72 v14 M72 79 h14" stroke="{p['plus']}" stroke-width="3" stroke-linecap="round"/>
</svg>'''

# Упрощённая иконка на пиксельной сетке: S, отступ m, плитка t, зазор g (m*2 + t*2 + g = S)
SMALL_GRID = {16: (3, 4, 2), 20: (3, 6, 2), 24: (4, 7, 2), 32: (6, 9, 2)}

def small_svg(p, S):
    m, t, g = SMALL_GRID[S]
    assert 2 * m + 2 * t + g == S
    r_bg = round(S * 0.225, 2)
    r_t = max(1.0, round(t * 0.26, 2))
    a, b = m, m + t + g
    ts = p.get("tile_small", p["tile"])
    parts = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {S} {S}" shape-rendering="geometricPrecision">',
             f'<defs><linearGradient id="bg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="{p["bg1"]}"/><stop offset="1" stop-color="{p["bg2"]}"/></linearGradient></defs>',
             f'<rect width="{S}" height="{S}" rx="{r_bg}" fill="url(#bg)"/>',
             f'<rect x="{a}" y="{a}" width="{t}" height="{t}" rx="{r_t}" fill="{p["a2"]}"/>',
             f'<rect x="{b}" y="{a}" width="{t}" height="{t}" rx="{r_t}" fill="{ts}"/>',
             f'<rect x="{a}" y="{b}" width="{t}" height="{t}" rx="{r_t}" fill="{ts}"/>']
    if S >= 24:
        # контур + плюс в правой нижней плитке, треугольник запуска в янтарной
        parts.append(f'<rect x="{b+0.5}" y="{b+0.5}" width="{t-1}" height="{t-1}" rx="{r_t-0.3}" fill="none" stroke="{p["outline"]}" stroke-width="1"/>')
        c = b + t / 2
        h = t * 0.22
        parts.append(f'<path d="M{c} {c-h} V{c+h} M{c-h} {c} H{c+h}" stroke="{p["plus"]}" stroke-width="1.2" stroke-linecap="round"/>')
        tx, ty = a + t * 0.36, a + t * 0.28
        parts.append(f'<path d="M{tx} {ty} L{a + t*0.74} {a + t/2} L{tx} {a + t - t*0.28} Z" fill="{p["play"]}"/>')
    else:
        parts.append(f'<rect x="{b}" y="{b}" width="{t}" height="{t}" rx="{r_t}" fill="{p["outline"]}" fill-opacity="0.75"/>')
    parts.append('</svg>')
    return "".join(parts)

# Значок трея: без подложки, только плитки; читается на тёмной и светлой панели задач
TRAY_GRID = {16: 2, 20: 2, 24: 2, 32: 2, 40: 4, 48: 4, 64: 6}
TRAY = dict(amber="#F59E0B", tile="#71717A", play="#451A03")

def tray_svg(S):
    g = TRAY_GRID[S]
    t = (S - g) / 2
    r = max(1.0, round(t * 0.24, 2))
    b = t + g
    parts = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {S} {S}">',
             f'<rect x="0" y="0" width="{t}" height="{t}" rx="{r}" fill="{TRAY["amber"]}"/>',
             f'<rect x="{b}" y="0" width="{t}" height="{t}" rx="{r}" fill="{TRAY["tile"]}"/>',
             f'<rect x="0" y="{b}" width="{t}" height="{t}" rx="{r}" fill="{TRAY["tile"]}"/>']
    sw = 1 if S < 40 else 2
    parts.append(f'<rect x="{b + sw/2}" y="{b + sw/2}" width="{t - sw}" height="{t - sw}" rx="{max(0.5, r - sw/2)}" fill="none" stroke="{TRAY["tile"]}" stroke-width="{sw}"/>')
    if S >= 24:
        tx, ty = t * 0.34, t * 0.26
        parts.append(f'<path d="M{tx} {ty} L{t*0.76} {t/2} L{tx} {t - ty} Z" fill="{TRAY["play"]}"/>')
    parts.append('</svg>')
    return "".join(parts)

APP_SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
TRAY_SIZES = [16, 20, 24, 32, 40, 48, 64]

def render(page, svg, size, path):
    html = f'<html><body style="margin:0;background:transparent"><div style="width:{size}px;height:{size}px">' \
           + svg.replace('<svg ', f'<svg width="{size}" height="{size}" ', 1) + '</div></body></html>'
    page.set_viewport_size({"width": size, "height": size})
    page.set_content(html)
    page.screenshot(path=str(path), omit_background=True, clip={"x": 0, "y": 0, "width": size, "height": size})

def main():
    svgdir = OUT / "svg"; svgdir.mkdir(exist_ok=True)
    pngdir = OUT / "png"; pngdir.mkdir(exist_ok=True)
    P = PALETTES["graphite"]
    (svgdir / "app_icon.svg").write_text(full_svg(P), encoding="utf-8")
    for S in SMALL_GRID:
        (svgdir / f"app_icon_{S}.svg").write_text(small_svg(P, S), encoding="utf-8")
    (svgdir / "tray_icon.svg").write_text(tray_svg(32), encoding="utf-8")
    for k, pal in PALETTES.items():
        (svgdir / f"palette_{k}.svg").write_text(full_svg(pal), encoding="utf-8")

    with sync_playwright() as pw:
        br = pw.chromium.launch()
        page = br.new_page(device_scale_factor=1)
        for S in APP_SIZES:
            svg = small_svg(P, S) if S in SMALL_GRID else full_svg(P)
            render(page, svg, S, pngdir / f"app_{S}.png")
        for S in TRAY_SIZES:
            render(page, tray_svg(S), S, pngdir / f"tray_{S}.png")
        for k, pal in PALETTES.items():
            render(page, full_svg(pal), 512, pngdir / f"palette_{k}_512.png")
        render(page, full_svg(P), 1024, pngdir / "app_1024.png")
        br.close()

    def build_ico(prefix, sizes, dest):
        imgs = [Image.open(pngdir / f"{prefix}_{s}.png").convert("RGBA") for s in sizes]
        big = imgs[-1]
        big.save(dest, format="ICO", sizes=[(s, s) for s in sizes], append_images=imgs[:-1])

    build_ico("app", APP_SIZES, OUT / "app.ico")
    build_ico("tray", TRAY_SIZES, OUT / "tray.ico")
    # превью, как в репозитории
    Image.open(pngdir / "app_256.png").save(OUT / "app_icon_preview.png")
    Image.open(pngdir / "tray_64.png").save(OUT / "tray_icon_preview.png")

if __name__ == "__main__":
    main()
