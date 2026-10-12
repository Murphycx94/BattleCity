"""Generates the built-in map files. Each char is a half tile; see MapParser for the legend."""
import os, sys

OUT = os.path.join(os.path.dirname(__file__), "..", "src", "Tank.Core", "Maps")

class Grid:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.g = [['.'] * w for _ in range(h)]
    def rect(self, x, z, w, h, ch):
        for zz in range(z, z + h):
            for xx in range(x, x + w):
                if 0 <= xx < self.w and 0 <= zz < self.h:
                    self.g[zz][xx] = ch
    def mark(self, x, z, ch):  # 1-tile marker: clear 2x2 then put marker top-left
        self.rect(x, z, 2, 2, '.')
        self.g[z][x] = ch
    def mirror_v(self, swap):
        """Copies the bottom half onto the top half, flipped vertically."""
        for z in range(self.h // 2):
            for x in range(self.w):
                c = self.g[self.h - 1 - z][x]
                self.g[z][x] = swap.get(c, c)
    def text(self, comment):
        return "; " + comment + "\n" + "\n".join("".join(r) for r in self.g) + "\n"

def coop01():
    g = Grid(26, 26)
    for x in (2, 6, 18, 22):
        g.rect(x, 2, 2, 8, '#')
    for x in (10, 14):
        g.rect(x, 2, 2, 6, '#')
    g.rect(12, 8, 2, 2, '@')
    g.rect(0, 13, 4, 1, '#'); g.rect(22, 13, 4, 1, '#')
    g.rect(0, 12, 2, 1, '@'); g.rect(24, 12, 2, 1, '@')
    g.rect(4, 12, 2, 2, '#'); g.rect(20, 12, 2, 2, '#')
    g.rect(8, 12, 2, 2, '%'); g.rect(16, 12, 2, 2, '~')
    g.rect(10, 11, 6, 1, '#'); g.rect(10, 14, 6, 1, '#')
    for x in (2, 6, 18, 22):
        g.rect(x, 16, 2, 6, '#')
    g.rect(10, 16, 2, 4, '#'); g.rect(14, 16, 2, 4, '#'); g.rect(12, 17, 2, 2, '#')
    g.rect(0, 14, 2, 2, '%'); g.rect(24, 14, 2, 2, '%')
    g.rect(8, 19, 2, 2, '-'); g.rect(16, 19, 2, 2, '-')
    g.rect(11, 23, 4, 1, '#'); g.rect(11, 24, 1, 2, '#'); g.rect(14, 24, 1, 2, '#')
    g.mark(12, 24, 'E')
    for x in (8, 16, 4, 20):
        g.mark(x, 24, 'P')
    for x in (0, 12, 24):
        g.mark(x, 0, 'S')
    return g.text("coop-01: classic 13x13 co-op stage, 4 player spawns")

def versus01():
    g = Grid(32, 26)
    # bottom half (team 0); mirrored to the top for team 1
    g.rect(14, 23, 4, 1, '#'); g.rect(14, 24, 1, 2, '#'); g.rect(17, 24, 1, 2, '#')
    g.mark(15, 24, 'E')
    for x in (7, 11, 19, 23):
        g.mark(x, 24, 'P')
    for x in (2, 6, 24, 28):
        g.rect(x, 16, 2, 5, '#')
    g.rect(10, 18, 4, 2, '#'); g.rect(18, 18, 4, 2, '#')
    g.rect(14, 15, 4, 2, '@')
    g.rect(0, 14, 4, 1, '%'); g.rect(28, 14, 4, 1, '%')
    g.mirror_v({'E': '.', 'P': '.'})
    # top markers (team 1) after mirroring (markers live at the top-left of their tile)
    g.mark(15, 0, 'F')
    for x in (7, 11, 19, 23):
        g.mark(x, 0, 'Q')
    g.rect(14, 2, 4, 1, '#'); g.rect(14, 0, 1, 2, '#'); g.rect(17, 0, 1, 2, '#')
    # neutral middle band
    g.rect(4, 12, 4, 2, '~'); g.rect(24, 12, 4, 2, '~')
    g.rect(10, 12, 4, 2, '%'); g.rect(18, 12, 4, 2, '%')
    g.rect(15, 12, 2, 2, '@')
    return g.text("versus-01: team base battle, 4v4, eagles top (team 1) and bottom (team 0)")

def arena01():
    g = Grid(32, 26)
    g.rect(6, 4, 2, 6, '#'); g.rect(24, 4, 2, 6, '#')
    g.rect(6, 16, 2, 6, '#'); g.rect(24, 16, 2, 6, '#')
    g.rect(12, 6, 8, 2, '#'); g.rect(12, 18, 8, 2, '#')
    g.rect(15, 11, 2, 4, '@')
    g.rect(10, 11, 3, 4, '%'); g.rect(19, 11, 3, 4, '%')
    g.rect(0, 12, 4, 2, '~'); g.rect(28, 12, 4, 2, '~')
    g.rect(12, 0, 8, 1, '@'); g.rect(12, 25, 8, 1, '@')
    g.rect(2, 2, 2, 2, '-'); g.rect(28, 22, 2, 2, '-')
    for (x, z) in ((0, 0), (30, 0), (0, 24), (30, 24), (14, 2), (16, 22), (2, 16), (28, 8)):
        g.mark(x, z, 'P')
    return g.text("arena-01: free-for-all deathmatch, 8 spawns")

def main():
    os.makedirs(OUT, exist_ok=True)
    for name, fn in (("coop-01", coop01), ("versus-01", versus01), ("arena-01", arena01)):
        path = os.path.join(OUT, name + ".map")
        with open(path, "w") as f:
            f.write(fn())
        print("wrote", os.path.relpath(path))

if __name__ == "__main__":
    sys.exit(main())
