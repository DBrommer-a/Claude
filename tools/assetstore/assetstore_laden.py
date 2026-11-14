"""Entpackt ausgewählte Teile heruntergeladener Asset-Store-Pakete ins Unity-Projekt.

Aufruf (aus dem Repository): python3 -I tools/assetstore/assetstore_laden.py
Liest tools/assetstore/auswahl.json. Pro Paket werden die gewählten Dateien und alles, worauf sie
per GUID verweisen (Meshes, Materialien, Texturen, Shader Graphs …), samt .meta entpackt – genau so,
wie Unitys eigener Import sie ablegen würde, nur ohne den Rest des Pakets (spart Gigabytes).

Ein .unitypackage ist ein gzip-Tar mit <guid>/pathname, <guid>/asset und <guid>/asset.meta.
Fehlt ein Paket im Asset-Store-Cache, wird es übersprungen (z. B. in der Cloud).
"""
import fnmatch
import json
import os
import re
import sys
import tarfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
PROJECT = os.path.join(REPO, "Frostline")
GUID = re.compile(rb"guid\W{1,4}([0-9a-f]{32})")


def safe(pathname):
    """Nur Pfade unter Assets/ oder Packages/, ohne Ausbruch aus dem Projekt."""
    if not (pathname.startswith("Assets/") or pathname.startswith("Packages/")):
        return False
    return ".." not in pathname.split("/") and not os.path.isabs(pathname)


def scan(package):
    """Durchgang 1: Pfade, Verweise aus Text-Assets und .meta-Dateien."""
    paths, refs, sizes = {}, {}, {}
    # "r:gz" statt Stream-Modus: Python 3.9 scheitert im Stream-Modus am gzip-Zusatzfeld der Pakete.
    # Gelesen wird trotzdem der Reihe nach, also ohne teures Zurückspulen.
    with tarfile.open(package, "r:gz") as tar:
        for member in tar:
            parts = member.name.split("/")
            if len(parts) != 2 or not member.isfile():
                continue
            guid, kind = parts
            if kind == "pathname":
                paths[guid] = tar.extractfile(member).read().decode("utf-8", "replace").splitlines()[0].strip()
            elif kind in ("asset", "asset.meta"):
                if kind == "asset":
                    sizes[guid] = member.size
                data = tar.extractfile(member).read() if member.size < 8_000_000 else b""
                if kind == "asset.meta" or data[:5] == b"%YAML" or data[:1] == b"{":
                    refs.setdefault(guid, set()).update(g.decode() for g in GUID.findall(data))
    return paths, refs, sizes


# Nie mitnehmen, auch wenn ein Prefab oder Material darauf verweist: Projekt-/Paketeinstellungen
# (überschreiben sonst HDRP und Paketliste) sowie Code und Shader anderer Render-Pipelines
# (kompilieren in HDRP nicht und schicken Unity in den Safe Mode).
IMMER_OHNE = ["Packages/*", "ProjectSettings/*", "*.cs", "*.asmdef", "*.asmref", "*.dll", "*.js",
              "*.shader", "*.shadergraph", "*.shadersubgraph", "*.hlsl", "*.cginc", "*.compute"]


def excluded(pathname, patterns):
    return any(fnmatch.fnmatch(pathname, pattern) for pattern in patterns)


def closure(selected, refs, paths, without=()):
    todo, done = list(selected), set()
    while todo:
        guid = todo.pop()
        if guid in done or guid not in paths or excluded(paths[guid], without):
            continue
        done.add(guid)
        todo.extend(refs.get(guid, ()))
    return done


def extract(package, wanted, paths):
    """Durchgang 2: gewählte Assets und .meta-Dateien schreiben (vorhandene gleich große überspringen)."""
    written, skipped, size = 0, 0, 0
    with tarfile.open(package, "r:gz") as tar:
        for member in tar:
            parts = member.name.split("/")
            if len(parts) != 2 or not member.isfile():
                continue
            guid, kind = parts
            if guid not in wanted or kind not in ("asset", "asset.meta"):
                continue
            pathname = paths.get(guid)
            if not pathname or not safe(pathname):
                continue
            target = os.path.join(PROJECT, *pathname.split("/")) + (".meta" if kind == "asset.meta" else "")
            # Vorhandene Dateien nie überschreiben: Unity passt sie nach dem Import an (HDRP-Umstellung,
            # Kachelgrößen). Für eine neue Paketversion den Ordner löschen und neu entpacken.
            if os.path.exists(target):
                skipped += 1
                continue
            os.makedirs(os.path.dirname(target), exist_ok=True)
            with open(target + ".part", "wb") as out:
                out.write(tar.extractfile(member).read())
            os.replace(target + ".part", target)
            written += 1
            size += member.size
    return written, skipped, size


def main():
    with open(os.path.join(HERE, "auswahl.json"), encoding="utf-8") as f:
        config = json.load(f)
    cache = os.path.expanduser(config["cache"])
    for entry in config["pakete"]:
        package = os.path.join(cache, entry["datei"])
        if not os.path.exists(package):
            print(f"· {entry['name']}: nicht heruntergeladen, übersprungen")
            continue
        paths, refs, sizes = scan(package)
        selected = [g for g, p in paths.items() if any(fnmatch.fnmatch(p, pattern) for pattern in entry["auswahl"])]
        without = IMMER_OHNE + entry.get("ohne", [])
        wanted = closure(selected, refs, paths, without)
        total = sum(sizes.get(g, 0) for g in wanted)
        written, skipped, size = extract(package, wanted, paths)
        print(f"✓ {entry['name']}: {len(selected)} gewählt, {len(wanted)} mit Abhängigkeiten "
              f"({total / 1e6:.0f} MB), {written} Dateien geschrieben ({size / 1e6:.0f} MB), {skipped} schon da")
    return 0


if __name__ == "__main__":
    sys.exit(main())
