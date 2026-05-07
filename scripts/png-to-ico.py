#!/usr/bin/env python3
"""
Simple converter: wraps a PNG into an ICO container (single-image PNG-based ICO).
Usage: png-to-ico.py input.png output.ico

This produces a valid .ico that contains the PNG image (Windows Vista+ supports PNG in ICO).
"""
import sys
import os


def png_to_ico(png_path: str, ico_path: str) -> None:
    if not os.path.exists(png_path):
        raise FileNotFoundError(f"Input PNG not found: {png_path}")
    with open(png_path, "rb") as f:
        png_data = f.read()
    png_len = len(png_data)
    # ICO header: reserved(2), type(2), count(2)
    header = bytearray()
    header += (0).to_bytes(2, "little")
    header += (1).to_bytes(2, "little")  # 1 = icon
    header += (1).to_bytes(2, "little")  # number of images

    # Directory entry (16 bytes)
    # width (1), height (1), color count (1), reserved (1)
    # planes (2), bitcount (2), bytes in res (4), image offset (4)
    # For PNG inside ICO, width/height = 0 means 256.
    width = 0
    height = 0
    color_count = 0
    reserved = 0
    planes = 1
    bit_count = 32
    image_offset = 6 + 16  # header + one dir entry

    dir_entry = bytearray()
    dir_entry += (width).to_bytes(1, "little")
    dir_entry += (height).to_bytes(1, "little")
    dir_entry += (color_count).to_bytes(1, "little")
    dir_entry += (reserved).to_bytes(1, "little")
    dir_entry += (planes).to_bytes(2, "little")
    dir_entry += (bit_count).to_bytes(2, "little")
    dir_entry += (png_len).to_bytes(4, "little")
    dir_entry += (image_offset).to_bytes(4, "little")

    os.makedirs(os.path.dirname(ico_path), exist_ok=True)
    with open(ico_path, "wb") as out:
        out.write(header)
        out.write(dir_entry)
        out.write(png_data)


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print("Usage: png-to-ico.py input.png output.ico")
        sys.exit(2)
    inp = sys.argv[1]
    outp = sys.argv[2]
    try:
        png_to_ico(inp, outp)
        print(f"Wrote: {outp}")
    except Exception as e:
        print("Error:", e)
        sys.exit(1)
