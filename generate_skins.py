#!/usr/bin/env python3
"""
绘梨衣桌宠 — 一键换色皮肤生成器
从原版 sprite 生成 5 套主题配色
"""

from PIL import Image, ImageEnhance
import colorsys
import os
import shutil

SRC_DIR = "assets/sprites"
THEMES = {
    "sakura": {
        "name": "🌸 樱花粉",
        "hue_shift": 0.04,      # 微偏粉
        "sat_scale": 1.15,       # 饱和度 +15%
        "val_scale": 1.05,       # 明度 +5%
        "skin_keep": True,       # 保留肤色
    },
    "moonlight": {
        "name": "🌙 月光蓝",
        "hue_shift": -0.35,      # 偏向蓝色
        "sat_scale": 0.90,
        "val_scale": 0.95,
        "skin_keep": True,
    },
    "lavender": {
        "name": "💜 薰衣草紫",
        "hue_shift": -0.15,      # 偏紫
        "sat_scale": 1.05,
        "val_scale": 1.00,
        "skin_keep": True,
    },
    "noir": {
        "name": "🖤 暗夜黑",
        "hue_shift": 0.0,
        "sat_scale": 0.20,       # 大幅降饱和度
        "val_scale": 0.65,       # 变暗
        "tint_rgb": (30, 30, 40),# 加一层蓝紫暗色
        "tint_alpha": 0.25,
        "skin_keep": True,
    },
    "wafuu": {
        "name": "🍵 和风",
        "hue_shift": 0.02,
        "sat_scale": 0.75,       # 低饱和，素雅
        "val_scale": 0.92,
        "skin_keep": True,
    },
}


def is_skin_tone(r, g, b):
    """判断像素是否为肤色（保留不处理）"""
    # 肤色在 HSV 中 H≈10-35, S≈15-60, V≈60-95
    h, s, v = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
    # 粉色系肤色（含腮红）
    if 0.01 < h < 0.12 and 0.10 < s < 0.70 and v > 0.50:
        return True
    # 浅肤色（高亮区域）
    if s < 0.15 and v > 0.75:
        return True
    return False


def apply_theme(img: Image.Image, theme: dict) -> Image.Image:
    """对一张图应用主题配色"""
    pixels = img.convert("RGBA")
    w, h = pixels.size
    result = Image.new("RGBA", (w, h))

    for y in range(h):
        for x in range(w):
            r, g, b, a = pixels.getpixel((x, y))
            if a == 0:
                result.putpixel((x, y), (0, 0, 0, 0))
                continue

            # 保留肤色
            if theme.get("skin_keep") and is_skin_tone(r, g, b):
                result.putpixel((x, y), (r, g, b, a))
                continue

            # 转 HSV
            h, s, v = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)

            # 应用色相偏移
            h = (h + theme["hue_shift"]) % 1.0

            # 应用饱和度和明度缩放
            s = min(1.0, max(0.0, s * theme["sat_scale"]))
            v = min(1.0, max(0.0, v * theme["val_scale"]))

            # 转回 RGB
            nr, ng, nb = colorsys.hsv_to_rgb(h, s, v)
            nr = int(nr * 255)
            ng = int(ng * 255)
            nb = int(nb * 255)

            # 叠加 tint 层
            tint = theme.get("tint_rgb")
            alpha_t = theme.get("tint_alpha", 0)
            if tint and alpha_t > 0:
                nr = int(nr * (1 - alpha_t) + tint[0] * alpha_t)
                ng = int(ng * (1 - alpha_t) + tint[1] * alpha_t)
                nb = int(nb * (1 - alpha_t) + tint[2] * alpha_t)

            result.putpixel((x, y), (nr, ng, nb, a))

    return result


def main():
    import sys
    sys.stdout.reconfigure(encoding='utf-8')
    base = os.path.dirname(os.path.abspath(__file__))
    src = os.path.join(base, SRC_DIR)
    if not os.path.isdir(src):
        print(f"❌ 找不到 {src}，请在项目根目录运行此脚本")
        return

    sprites = sorted([f for f in os.listdir(src) if f.endswith(".png")])
    if not sprites:
        print("❌ assets/sprites/ 中没有 PNG 文件")
        return

    print(f"🎨 找到 {len(sprites)} 张原版精灵")
    print(f"   将生成 {len(THEMES)} 套换色皮肤\n")

    for key, theme in THEMES.items():
        out_dir = os.path.join(src, key)
        os.makedirs(out_dir, exist_ok=True)

        print(f"  {theme['name']} ({key}/) ...", end=" ")

        for fn in sprites:
            img = Image.open(os.path.join(src, fn))
            themed = apply_theme(img, theme)
            themed.save(os.path.join(out_dir, fn))

        print(f"✓ {len(sprites)} 张完成")

    # 创建皮肤配置文件
    import json
    skin_config = [{"id": key, "name": t["name"]} for key, t in THEMES.items()]
    skin_config.insert(0, {"id": "default", "name": "🎀 原版"})
    with open(os.path.join(src, "skins.json"), "w", encoding="utf-8") as f:
        json.dump(skin_config, f, ensure_ascii=False, indent=2)

    print(f"\n✅ 完成！")
    print(f"   皮肤目录: {src}/")
    print(f"   可用皮肤: 原版 + {', '.join(t['name'] for t in THEMES.values())}")


if __name__ == "__main__":
    main()
