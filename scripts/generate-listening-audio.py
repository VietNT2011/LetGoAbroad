"""VI: Biên dịch audio seed ngoại tuyến. EN: Offline build only, never invoked by the app.

Requires Python 3 and FFmpeg with the Flite slt voice (Debian libflite1).
Run: python3 scripts/generate-listening-audio.py
The committed bytes/hash are authoritative; a different Flite/FFmpeg build may differ.
"""
import hashlib
import json
import math
from pathlib import Path
import subprocess
import tempfile
import uuid
import wave

ROOT = Path(__file__).resolve().parents[1]
RATE = 44100


def run(*args):
    return subprocess.run(args, check=True, capture_output=True, text=True).stdout


def identifier(kind, slug):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, f"https://englishhub.local/listening/v1/{kind}/{slug}"))


def main():
    source = ROOT / "seed/listening-scripts.json"
    output = ROOT / "seed/audio"
    output.mkdir(exist_ok=True)
    manifest = []
    with tempfile.TemporaryDirectory(prefix="englishhub-listening-") as folder:
        scratch = Path(folder)
        for item in json.loads(source.read_text(encoding="utf-8")):
            segments = []
            frames = bytearray(b"\0\0" * round(RATE * .25))
            for index, sentence in enumerate(item["sentences"]):
                text_file = scratch / f"sentence-{index}.txt"
                wave_file = scratch / f"sentence-{index}.wav"
                text_file.write_text(sentence["en"], encoding="utf-8")
                # VI: Text qua file, không ghép vào shell. EN: Pass trusted text via file and no shell.
                run("ffmpeg", "-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i",
                    f"flite=textfile={text_file.as_posix()}:voice=slt", "-af", "atempo=0.9",
                    "-ar", str(RATE), "-ac", "1", "-c:a", "pcm_s16le", str(wave_file))
                with wave.open(str(wave_file), "rb") as pcm:
                    assert (pcm.getnchannels(), pcm.getsampwidth(), pcm.getframerate()) == (1, 2, RATE)
                    clip = pcm.readframes(pcm.getnframes())
                start = math.floor(len(frames) / 2 / RATE * 1000)
                frames.extend(clip)
                end = math.ceil(len(frames) / 2 / RATE * 1000) + 100
                frames.extend(b"\0\0" * round(RATE * .4))
                segments.append({"startMs": start, "endMs": end, "textEn": sentence["en"],
                                 "textVi": sentence["vi"], "acceptedAnswers": sentence.get("accepted", [sentence["en"]])})
            combined = scratch / "combined.wav"
            with wave.open(str(combined), "wb") as pcm:
                pcm.setnchannels(1)
                pcm.setsampwidth(2)
                pcm.setframerate(RATE)
                pcm.writeframes(frames)
            filename = item["slug"] + ".mp3"
            destination = output / filename
            run("ffmpeg", "-nostdin", "-v", "error", "-y", "-i", str(combined), "-map_metadata", "-1",
                "-c:a", "libmp3lame", "-b:a", "96k", str(destination))
            facts = json.loads(run("ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "json", str(destination)))
            duration = math.ceil(float(facts["format"]["duration"]) * 1000)
            assert duration >= segments[-1]["endMs"]
            blob = destination.read_bytes()
            manifest.append({"slug": item["slug"], "rootId": identifier("root", item["slug"]),
                "versionId": identifier("version", item["slug"]), "assetId": identifier("asset", item["slug"]),
                "title": item["title"], "summary": item["summary"], "body": item["body"], "fileName": filename,
                "bytes": len(blob), "sha256": hashlib.sha256(blob).hexdigest().upper(), "durationMs": duration,
                "segments": segments, "questions": item["questions"]})
    target = ROOT / "seed/listening-curriculum.json"
    target.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"lessons": len(manifest), "segments": sum(len(x["segments"]) for x in manifest),
                      "bytes": sum(x["bytes"] for x in manifest), "generator": "Flite slt / atempo0.9 / MP3 96kbps"}))


if __name__ == "__main__":
    main()
