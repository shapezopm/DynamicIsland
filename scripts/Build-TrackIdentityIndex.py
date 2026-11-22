"""Build local artist and source metadata indices. No playback or credentials required.

MusicBrainz JSON artist dump contains CC0 core names and aliases. Stream the archive;
do not load the complete catalogue into RAM. A failed import leaves the previous DB intact.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import sqlite3
import tarfile
import time
import unicodedata
import urllib.request


def key(value):
    return re.sub(r"\s+", " ", unicodedata.normalize("NFKC", value)).strip().upper()


def source_index(path, destination):
    data = json.loads(Path(path).read_text(encoding="utf-8-sig"))
    tracks = data["tracks"]
    db_path = destination / "tracks.sqlite.building"
    db = sqlite3.connect(db_path)
    db.execute("DROP TABLE IF EXISTS track_hints")
    db.execute("CREATE TABLE track_hints(raw_title TEXT,raw_artist TEXT,duration REAL,title TEXT,artist TEXT,evidence TEXT,source_url TEXT)")
    count = 0
    for track in tracks:
        publisher = (track.get("publisherArtist") or "").strip()
        # Publisher data can itself be noise. An identical title is not an artist credit.
        if not publisher or key(publisher) in (key(track["artist"]), key(track["title"])) or len(publisher) > 256 \
                or re.search(r"https?://|t\.me/|telegram\.me/|vk\.com/|\bprod\.", publisher, re.I):
            continue
        db.execute("INSERT INTO track_hints VALUES(?,?,?,?,?,?,?)", (
            key(track["title"]), key(track["artist"]), track["durationMs"] / 1000,
            track["title"], publisher, "SoundCloudPublisher", track["url"]))
        count += 1
    # Human-confirmed mapping, scoped to this recording rather than the uploader account.
    for track in tracks:
        if track["id"] == 2403298026 and key(track["title"]) == key("доедешь пиши"):
            db.execute("INSERT INTO track_hints VALUES(?,?,?,?,?,?,?)", (
                key(track["title"]), key(track["artist"]), track["durationMs"] / 1000,
                "Доедешь - пиши", "Каспийский Груз", "UserConfirmed", track["url"]))
            count += 1
    db.execute("CREATE INDEX track_identity ON track_hints(raw_title,raw_artist)")
    db.commit()
    db.close()
    os.replace(db_path, destination / "tracks.sqlite")
    print(json.dumps({"source_rows": count, "path": str(destination / "tracks.sqlite")}), flush=True)


def musicbrainz_index(destination):
    root = "https://data.metabrainz.org/pub/musicbrainz/data/json-dumps/"
    latest = urllib.request.urlopen(root + "LATEST", timeout=30).read().decode().strip()
    if not re.fullmatch(r"\d{8}-\d{6}", latest):
        raise ValueError("Unexpected MusicBrainz release identifier")
    release = root + latest + "/"
    sums = urllib.request.urlopen(release + "SHA256SUMS", timeout=30).read().decode()
    match = re.search(r"^([a-fA-F0-9]{64})\s+\*?(?:\./)?artist\.tar\.xz$", sums, re.M)
    if not match:
        raise ValueError("No signed-release checksum entry for artist.tar.xz")
    expected = match.group(1).lower()
    archive = destination / (latest + "-artist.tar.xz")
    offset = archive.stat().st_size if archive.exists() else 0
    request = urllib.request.Request(release + "artist.tar.xz", headers={"Range": f"bytes={offset}-"} if offset else {})
    try:
        response = urllib.request.urlopen(request, timeout=45)
    except urllib.error.HTTPError as error:
        if error.code != 416:
            raise
        response = None
    if response:
        resumed = response.status == 206 and response.headers.get("Content-Range", "").startswith(f"bytes {offset}-")
        with response, archive.open("ab" if resumed else "wb") as output:
            progress = offset if resumed else 0
            last_print = time.monotonic()
            while chunk := response.read(1024 * 1024):
                output.write(chunk)
                progress += len(chunk)
                if time.monotonic() - last_print > 10:
                    print(json.dumps({"stage": "download", "bytes": progress}), flush=True)
                    last_print = time.monotonic()
    with archive.open("rb") as source:
        actual = hashlib.file_digest(source, "sha256").hexdigest()
    if actual != expected:
        raise ValueError("MusicBrainz checksum mismatch; previous index preserved")
    db_path = destination / "artists.sqlite.building"
    db = sqlite3.connect(db_path)
    db.execute("PRAGMA journal_mode=OFF")
    db.execute("DROP TABLE IF EXISTS artist_names")
    db.execute("CREATE TABLE artist_names(key TEXT,artist_id TEXT,name TEXT,is_primary INTEGER)")
    count = 0
    with tarfile.open(archive, "r|xz") as dump:
        for member in dump:
            if not member.isfile() or Path(member.name).name != "artist":
                continue
            for line in dump.extractfile(member):
                artist = json.loads(line)
                artist_id = artist["id"]
                names = [(artist["name"], 1)] + [(alias["name"], 0) for alias in artist.get("aliases", [])]
                seen = set()
                for name, primary in names:
                    normalized = key(name)
                    if not normalized or normalized in seen:
                        continue
                    seen.add(normalized)
                    db.execute("INSERT INTO artist_names VALUES(?,?,?,?)", (normalized, artist_id, name, primary))
                count += 1
                if count % 50000 == 0:
                    db.commit()
                    print(json.dumps({"stage": "index", "artists": count}), flush=True)
    if not count:
        raise ValueError("Artist archive contained no artist records")
    print(json.dumps({"stage": "create_search_indexes", "artists": count}), flush=True)
    db.execute("CREATE INDEX artist_lookup ON artist_names(key)")
    db.execute("CREATE INDEX artist_aliases ON artist_names(artist_id)")
    db.commit()
    db.close()
    os.replace(db_path, destination / "artists.sqlite")
    (destination / "musicbrainz-source.json").write_text(json.dumps({"release": latest, "sha256": expected,
        "artists": count, "license": "CC0", "source": release + "artist.tar.xz"}, indent=2), encoding="utf-8")
    print(json.dumps({"stage": "complete", "artists": count}), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--soundcloud-library")
    parser.add_argument("--musicbrainz", action="store_true")
    parser.add_argument("--destination", default=str(Path(os.environ["LOCALAPPDATA"]) / "DynamicIsland" / "TrackIdentity"))
    args = parser.parse_args()
    destination = Path(args.destination).resolve()
    destination.mkdir(parents=True, exist_ok=True)
    if args.soundcloud_library:
        source_index(args.soundcloud_library, destination)
    if args.musicbrainz:
        musicbrainz_index(destination)
