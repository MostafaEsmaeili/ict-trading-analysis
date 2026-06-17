#!/usr/bin/env python3
"""Convert YouTube auto-caption VTT files into clean text transcripts.

For each video: writes one cleaned .txt (de-duplicated, timestamps stripped).
For each playlist: writes one combined .txt with all episodes in order.

Usage: python build_transcripts.py <raw_dir> <out_dir> "<Playlist Title>"
"""
import sys, os, re, html, textwrap, glob

TAG_RE = re.compile(r"<[^>]+>")


def clean_vtt(path):
    """Return cleaned plain text from a YouTube auto-caption VTT file."""
    with open(path, encoding="utf-8") as f:
        raw = f.read().splitlines()

    lines = []
    for line in raw:
        s = line.strip()
        if not s:
            continue
        if s.startswith(("WEBVTT", "Kind:", "Language:", "NOTE")):
            continue
        if "-->" in s:
            continue
        s = TAG_RE.sub("", s)          # drop <00:00:..> and <c> inline tags
        s = html.unescape(s)
        s = s.strip()
        if not s:
            continue
        # rolling captions repeat the previous line; skip consecutive dups
        if lines and lines[-1] == s:
            continue
        lines.append(s)

    text = " ".join(lines)
    text = re.sub(r"\s+", " ", text).strip()
    return text


def parse_base(fname):
    """From 'NN - Title.en-orig.vtt' -> (index_int, 'NN - Title', 'Title')."""
    base = re.sub(r"\.(en-orig|en)\.vtt$", "", fname)
    m = re.match(r"^(\d+)\s*-\s*(.*)$", base)
    if m:
        return int(m.group(1)), base, m.group(2)
    return 0, base, base


def main():
    raw_dir, out_dir, title = sys.argv[1], sys.argv[2], sys.argv[3]
    os.makedirs(out_dir, exist_ok=True)

    # group vtt files by base name, preferring en-orig over en
    resolved = {}  # base -> path
    for path in glob.glob(os.path.join(raw_dir, "*.vtt")):
        fname = os.path.basename(path)
        base = re.sub(r"\.(en-orig|en)\.vtt$", "", fname)
        if base not in resolved:
            resolved[base] = path
        if ".en-orig.vtt" in fname:
            resolved[base] = path

    entries = []
    for base, path in resolved.items():
        idx, base_name, vid_title = parse_base(os.path.basename(path))
        text = clean_vtt(path)
        entries.append((idx, base_name, vid_title, text))

    entries.sort(key=lambda e: (e[0], e[1]))

    # write per-video transcripts
    for idx, base_name, vid_title, text in entries:
        wrapped = textwrap.fill(text, width=100) if text else "(no captions found)"
        out_path = os.path.join(out_dir, base_name + ".txt")
        with open(out_path, "w", encoding="utf-8") as f:
            f.write(f"{vid_title}\n")
            f.write("=" * len(vid_title) + "\n\n")
            f.write(wrapped + "\n")

    # write combined playlist transcript
    combined_path = os.path.join(out_dir, f"_{title} - FULL PLAYLIST.txt")
    with open(combined_path, "w", encoding="utf-8") as f:
        header = f"{title} — Full Playlist Transcript"
        f.write("=" * 78 + "\n")
        f.write(header + "\n")
        f.write(f"{len(entries)} videos\n")
        f.write("=" * 78 + "\n\n")
        for idx, base_name, vid_title, text in entries:
            f.write("\n" + "#" * 78 + "\n")
            f.write(f"# {base_name}\n")
            f.write("#" * 78 + "\n\n")
            f.write((textwrap.fill(text, width=100) if text else "(no captions found)") + "\n")

    words = sum(len(e[3].split()) for e in entries)
    print(f"{title}: {len(entries)} transcripts, ~{words:,} words -> {out_dir}")
    for idx, base_name, vid_title, text in entries:
        if not text:
            print(f"  WARNING empty: {base_name}")


if __name__ == "__main__":
    main()
