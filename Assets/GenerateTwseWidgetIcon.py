from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter


SIZE = 256
OUTPUT = Path(__file__).with_name("TwseWidget.ico")


def create_icon() -> Image.Image:
    icon = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))

    shadow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    shadow_draw = ImageDraw.Draw(shadow)
    shadow_draw.rounded_rectangle((12, 14, 244, 246), radius=48, fill=(0, 0, 0, 180))
    icon.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(9)))

    background = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    background_draw = ImageDraw.Draw(background)
    for y in range(12, 245):
        blend = (y - 12) / 233
        color = (
            int(8 + 5 * blend),
            int(19 + 15 * blend),
            int(34 + 25 * blend),
            255,
        )
        background_draw.line((12, y, 244, y), fill=color)

    mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(mask).rounded_rectangle((12, 12, 244, 244), radius=48, fill=255)
    background.putalpha(mask)
    icon.alpha_composite(background)

    frame = ImageDraw.Draw(icon)
    frame.rounded_rectangle((12, 12, 244, 244), radius=48, outline=(48, 129, 166, 255), width=3)
    frame.rounded_rectangle((19, 19, 237, 237), radius=40, outline=(23, 63, 91, 220), width=2)

    grid = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    grid_draw = ImageDraw.Draw(grid)
    for x in range(45, 225, 30):
        grid_draw.line((x, 56, x, 210), fill=(67, 139, 174, 42), width=1)
    for y in range(70, 211, 28):
        grid_draw.line((35, y, 225, y), fill=(67, 139, 174, 42), width=1)
    grid.putalpha(Image.composite(grid.getchannel("A"), Image.new("L", (SIZE, SIZE), 0), mask))
    icon.alpha_composite(grid)

    bars = ImageDraw.Draw(icon)
    for x, top, bottom, color in (
        (42, 151, 203, (38, 117, 157, 150)),
        (65, 133, 203, (38, 117, 157, 170)),
        (88, 164, 203, (38, 117, 157, 135)),
        (111, 116, 203, (38, 117, 157, 170)),
        (134, 145, 203, (38, 117, 157, 150)),
        (157, 96, 203, (38, 117, 157, 180)),
        (180, 123, 203, (38, 117, 157, 155)),
        (203, 78, 203, (38, 117, 157, 190)),
    ):
        bars.rounded_rectangle((x, top, x + 11, bottom), radius=4, fill=color)

    points = [(36, 171), (68, 143), (98, 155), (130, 112), (158, 128), (190, 80), (220, 98)]
    glow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    glow_draw = ImageDraw.Draw(glow)
    glow_draw.line(points, fill=(54, 217, 255, 230), width=13, joint="curve")
    icon.alpha_composite(glow.filter(ImageFilter.GaussianBlur(10)))

    chart = ImageDraw.Draw(icon)
    chart.line(points, fill=(105, 234, 255, 255), width=5, joint="curve")
    chart.line(points[-3:], fill=(169, 132, 255, 255), width=6, joint="curve")

    last_x, last_y = points[-1]
    dot_glow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    dot_glow_draw = ImageDraw.Draw(dot_glow)
    dot_glow_draw.ellipse((last_x - 17, last_y - 17, last_x + 17, last_y + 17), fill=(255, 72, 96, 220))
    icon.alpha_composite(dot_glow.filter(ImageFilter.GaussianBlur(9)))
    chart = ImageDraw.Draw(icon)
    chart.ellipse((last_x - 9, last_y - 9, last_x + 9, last_y + 9), fill=(255, 84, 100, 255), outline=(255, 220, 224, 255), width=3)

    # Compact signal mark in the upper-left corner: three bright bars echo the widget's market pulse.
    mark = ImageDraw.Draw(icon)
    mark.rounded_rectangle((38, 36, 45, 51), radius=3, fill=(76, 217, 255, 255))
    mark.rounded_rectangle((50, 30, 57, 51), radius=3, fill=(57, 242, 183, 255))
    mark.rounded_rectangle((62, 24, 69, 51), radius=3, fill=(169, 135, 255, 255))

    return icon


if __name__ == "__main__":
    create_icon().save(
        OUTPUT,
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )
    print(f"Generated {OUTPUT}")
