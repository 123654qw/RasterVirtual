"""生成 Raster Virtual 的应用图标（纯标准库，无需 Pillow）。

设计：炭黑圆角底 + 暖橙屏幕边框 + 内部像素方块阵列（呼应 "Raster"）。
输出 ICO，内含 16/32/48/64/128/256 六种尺寸的 32bpp BGRA 位图。
"""
import struct
import os

BG = (0x1C, 0x1F, 0x24)          # 底色
BORDER = (0x45, 0x4C, 0x57)      # 外框
ACCENT = (0xE5, 0x84, 0x3C)      # 暖橙
ACCENT_DIM = (0x8A, 0x52, 0x28)  # 暗橙

SS = 4  # 超采样倍数


def rounded_rect(x, y, w, h, r):
    """返回一个判定函数：点是否落在圆角矩形内。"""
    def inside(px, py):
        if px < x or py < y or px >= x + w or py >= y + h:
            return False
        cx = min(max(px, x + r), x + w - r)
        cy = min(max(py, y + r), y + h - r)
        dx = px - cx
        dy = py - cy
        return dx * dx + dy * dy <= r * r
    return inside


def render(size):
    """渲染一张 size x size 的 RGBA 图（列表，行优先，自上而下）。"""
    n = size * SS
    buf = [[(0, 0, 0, 0)] * n for _ in range(n)]

    outer = rounded_rect(0.0, 0.0, n, n, n * 0.22)
    inner_border = rounded_rect(n * 0.055, n * 0.055, n * 0.89, n * 0.89, n * 0.18)

    # 屏幕框
    screen_out = rounded_rect(n * 0.17, n * 0.20, n * 0.66, n * 0.52, n * 0.07)
    screen_in = rounded_rect(n * 0.225, n * 0.255, n * 0.55, n * 0.41, n * 0.045)

    # 底座
    stand = rounded_rect(n * 0.40, n * 0.72, n * 0.20, n * 0.055, n * 0.02)
    base = rounded_rect(n * 0.29, n * 0.775, n * 0.42, n * 0.06, n * 0.028)

    # 像素方块阵列（3 列 x 2 行）
    blocks = []
    bw = n * 0.125
    bh = n * 0.125
    gap = n * 0.045
    total_w = bw * 3 + gap * 2
    start_x = (n - total_w) / 2
    start_y = n * 0.305
    pattern = [
        [1, 1, 0],
        [1, 0, 1],
    ]
    for row in range(2):
        for col in range(3):
            bx = start_x + col * (bw + gap)
            by = start_y + row * (bh + gap * 0.6)
            blocks.append((rounded_rect(bx, by, bw, bh, n * 0.018), pattern[row][col]))

    for py in range(n):
        fy = py + 0.5
        for px in range(n):
            fx = px + 0.5
            if not outer(fx, fy):
                continue

            color = BORDER
            if inner_border(fx, fy):
                color = BG

                if screen_out(fx, fy):
                    color = ACCENT if not screen_in(fx, fy) else BG

                    if screen_in(fx, fy):
                        for hit, filled in blocks:
                            if hit(fx, fy):
                                color = ACCENT if filled else ACCENT_DIM
                                break

                elif stand(fx, fy) or base(fx, fy):
                    color = ACCENT

            buf[py][px] = (color[0], color[1], color[2], 255)

    # 下采样
    out = []
    for y in range(size):
        row = []
        for x in range(size):
            r = g = b = a = 0
            for dy in range(SS):
                for dx in range(SS):
                    pr, pg, pb, pa = buf[y * SS + dy][x * SS + dx]
                    r += pr * pa
                    g += pg * pa
                    b += pb * pa
                    a += pa
            total = SS * SS
            if a == 0:
                row.append((0, 0, 0, 0))
            else:
                row.append((r // a, g // a, b // a, a // total))
        out.append(row)
    return out


def to_dib(image, size):
    """打包成 ICO 内使用的 BITMAPINFOHEADER + BGRA 像素 + AND 掩码。"""
    header = struct.pack(
        "<IiiHHIIiiII",
        40,            # biSize
        size,          # biWidth
        size * 2,      # biHeight（含掩码，需两倍）
        1,             # biPlanes
        32,            # biBitCount
        0,             # biCompression
        size * size * 4,
        2835, 2835, 0, 0,
    )

    pixels = bytearray()
    for y in range(size - 1, -1, -1):   # DIB 自下而上
        for x in range(size):
            r, g, b, a = image[y][x]
            pixels += bytes((b, g, r, a))

    # AND 掩码：每行按 4 字节对齐
    row_bytes = ((size + 31) // 32) * 4
    mask = bytearray(row_bytes * size)

    return header + bytes(pixels) + bytes(mask)


def build_ico(path, sizes):
    images = []
    for s in sizes:
        img = render(s)
        images.append((s, to_dib(img, s)))

    out = bytearray()
    out += struct.pack("<HHH", 0, 1, len(images))

    offset = 6 + 16 * len(images)
    for s, data in images:
        out += struct.pack(
            "<BBBBHHII",
            0 if s >= 256 else s,
            0 if s >= 256 else s,
            0, 0, 1, 32,
            len(data), offset,
        )
        offset += len(data)

    for _, data in images:
        out += data

    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(bytes(out))

    print(f"已生成 {path}（{len(out)} 字节，含 {len(images)} 种尺寸）")


if __name__ == "__main__":
    target = os.path.join(
        os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
        "src", "RasterVirtual", "Assets", "app.ico",
    )
    build_ico(target, [16, 32, 48, 64, 128, 256])
