"""Lädt die freien Vorlagen (Poly Haven, CC0) aus vorlagen.json ins Unity-Projekt.

Aufruf (aus dem Repository): python3 -I tools/vorlagen/vorlagen_laden.py
Ziel: Frostline/Assets/Frostline/Vorlagen/ (nicht im Repository, siehe .gitignore).

Texturen werden so benannt, dass „Frostline > 5 · Megascans importieren“ sie erkennt:
<id>_<auflösung>_BaseColor.jpg, _Normal (OpenGL), _Roughness, _AO.
Bereits vorhandene Dateien mit passender Größe werden übersprungen.
"""
import json
import os
import sys
import urllib.request

API = "https://api.polyhaven.com"
HEADERS = {"User-Agent": "Frostline-Vorlagen/1.0"}
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TARGET = os.path.join(ROOT, "Frostline", "Assets", "Frostline", "Vorlagen")
TEXTURE_MAPS = {"Diffuse": "BaseColor", "nor_gl": "Normal", "Rough": "Roughness", "AO": "AO"}


def get_json(path):
    request = urllib.request.Request(API + path, headers=HEADERS)
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.load(response)


def download(url, path, size):
    if os.path.exists(path) and (size is None or os.path.getsize(path) == size):
        return False
    os.makedirs(os.path.dirname(path), exist_ok=True)
    request = urllib.request.Request(url, headers=HEADERS)
    temp = path + ".part"
    with urllib.request.urlopen(request, timeout=300) as response, open(temp, "wb") as out:
        while True:
            chunk = response.read(1 << 20)
            if not chunk:
                break
            out.write(chunk)
    os.replace(temp, path)
    return True


def pick(entry, resolution):
    """Datei in der gewünschten Auflösung, sonst die nächstkleinere."""
    for res in [resolution, "2k", "1k"]:
        if res in entry:
            formats = entry[res]
            for fmt in ("jpg", "png"):
                if fmt in formats:
                    return res, fmt, formats[fmt]
    return None


def load_texture(asset, resolution):
    files = get_json("/files/" + asset)
    folder = os.path.join(TARGET, "Texturen", asset)
    loaded = 0
    for key, role in TEXTURE_MAPS.items():
        if key not in files:
            continue
        found = pick(files[key], resolution)
        if found is None:
            continue
        res, fmt, item = found
        path = os.path.join(folder, f"{asset}_{res}_{role}.{fmt}")
        loaded += download(item["url"], path, item.get("size"))
    info = get_json("/info/" + asset)
    dims = info.get("dimensions", [])
    return {
        "id": asset,
        "breite_m": round(dims[0] / 1000.0, 3) if dims else 0.0,
        "hoehe_m": round(dims[1] / 1000.0, 3) if len(dims) > 1 else 0.0,
        "datei": "",
        "polygone": 0,
    }, loaded


def load_model(asset, resolution):
    files = get_json("/files/" + asset)
    found = None
    for res in [resolution, "1k", "2k"]:
        if res in files.get("gltf", {}):
            found = (res, files["gltf"][res]["gltf"])
            break
    if found is None:
        raise RuntimeError("kein glTF verfügbar")
    res, item = found
    folder = os.path.join(TARGET, "Modelle", asset)
    name = f"{asset}_{res}.gltf"
    loaded = download(item["url"], os.path.join(folder, name), item.get("size"))
    for relative, part in item.get("include", {}).items():
        loaded += download(part["url"], os.path.join(folder, *relative.split("/")), part.get("size"))
    info = get_json("/info/" + asset)
    dims = info.get("dimensions", [])
    return {
        "id": asset,
        "breite_m": round(dims[0] / 1000.0, 3) if dims else 0.0,
        "hoehe_m": round(dims[2] / 1000.0, 3) if len(dims) > 2 else 0.0,
        "datei": f"Modelle/{asset}/{name}",
        "polygone": int(info.get("polycount", 0)),
    }, loaded


def main():
    with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "vorlagen.json"), encoding="utf-8") as f:
        manifest = json.load(f)
    result = {"quelle": manifest["quelle"], "texturen": [], "modelle": []}
    failed = []
    for kind, loader in (("texturen", load_texture), ("modelle", load_model)):
        for asset, resolution in manifest[kind].items():
            try:
                entry, loaded = loader(asset, resolution)
                result[kind].append(entry)
                print(("✓ " if loaded else "· ") + f"{kind[:-2]} {asset}" + (f" ({loaded} Dateien)" if loaded else " (schon da)"))
            except Exception as error:  # noqa: BLE001 - einzelne Vorlage darf fehlen
                failed.append(asset)
                print(f"✗ {asset}: {error}")
    os.makedirs(TARGET, exist_ok=True)
    with open(os.path.join(TARGET, "vorlagen_info.json"), "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)
    print(f"Vorlagen: {len(result['texturen'])} Texturen, {len(result['modelle'])} Modelle in {TARGET}")
    if failed:
        print("Fehlgeschlagen: " + ", ".join(failed))
        sys.exit(1)


if __name__ == "__main__":
    main()
