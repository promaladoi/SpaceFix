"""Build the offline typo dictionaries from their pinned source files."""

from collections import defaultdict
import csv
from difflib import SequenceMatcher
import gzip
import json
from pathlib import Path
import re


DATA = Path(__file__).resolve().parent
RAW = DATA.parent / "downloads" / "typos"
RUSSIAN = re.compile(r"[а-яё]{4,24}")
ENGLISH = re.compile(r"[a-z]{4,24}")
KAZAKH = re.compile(r"[а-яёәіңғүұқөһ]{4,24}")
TOKEN = re.compile(r"\w+(?:[-'’]\w+)*")


def edit_distance(source, target):
    previous = list(range(len(target) + 1))
    earlier = previous
    for i, first in enumerate(source, 1):
        row = [i]
        for j, second in enumerate(target, 1):
            cost = min(row[-1] + 1, previous[j] + 1, previous[j - 1] + (first != second))
            if i > 1 and j > 1 and first == target[j - 2] and second == source[i - 2]:
                cost = min(cost, earlier[j - 2] + 1)
            row.append(cost)
        earlier, previous = previous, row
    return previous[-1]


def eligible(mistake, correction, known, alphabet=RUSSIAN):
    if (mistake == correction or mistake in known or correction not in known
            or not alphabet.fullmatch(mistake) or not alphabet.fullmatch(correction)):
        return False
    max_edits = 1 if min(len(mistake), len(correction)) < 6 else 2
    return abs(len(mistake) - len(correction)) <= max_edits and edit_distance(mistake, correction) <= max_edits


def kartaslov(known):
    pairs = defaultdict(set)
    with (RAW / "kartaslov.csv").open(encoding="utf-8-sig", newline="") as stream:
        for row in csv.DictReader(stream, delimiter=";"):
            mistake, correction = row["MISTAKE"].strip().lower(), row["CORRECT"].strip().lower()
            if float(row["WEIGHT"]) >= 0.025 and eligible(mistake, correction, known):
                pairs[mistake].add(correction)
    return pairs


def ruspellru(known):
    pairs = defaultdict(set)
    for split in ("train", "test"):
        rows = (RAW / f"{split}.json").read_text(encoding="utf-8-sig").splitlines()
        for row in map(json.loads, filter(str.strip, rows)):
            for mistake, correction in changed_words(row["source"], row["correction"]):
                if eligible(mistake, correction, known):
                    pairs[mistake].add(correction)
    return pairs


def changed_words(before, after):
    source = TOKEN.findall(before.lower())
    target = TOKEN.findall(after.lower())
    matcher = SequenceMatcher(None, source, target, autojunk=False)
    for operation, a, b, c, d in matcher.get_opcodes():
        if operation == "replace" and b - a == d - c == 1:
            yield source[a], target[c]


def toefl(known):
    pairs = defaultdict(set)
    with (RAW / "toefl.tsv").open(encoding="utf-8-sig", newline="") as stream:
        for row in csv.DictReader(stream, delimiter="\t"):
            mistake, correction = row["Misspelling"].strip().lower(), row["Correction"].strip().lower()
            if row["Type"] == "M" and eligible(mistake, correction, known, ENGLISH):
                pairs[mistake].add(correction)
    return pairs


def sauatai(known, russian):
    pairs = defaultdict(set)
    for split in ("train", "val", "test"):
        with (RAW / f"sauatai-{split}.csv").open(encoding="utf-8-sig", newline="") as stream:
            for row in csv.DictReader(stream):
                if row["label"] != "0":
                    continue
                for mistake, correction in changed_words(row["corrupted_sentence"], row["correct_sentence"]):
                    if mistake not in russian and eligible(mistake, correction, known, KAZAKH):
                        pairs[mistake].add(correction)
    return pairs


def frequency_words(language):
    with (DATA / f"{language}.txt").open(encoding="utf-8-sig") as source:
        return {word.lower() for word, count in (line.rstrip("\n").rsplit(" ", 1) for line in source)
                if int(count) >= 5}


def write_dictionaries(language, dictionaries):
    combined = defaultdict(set)
    for pairs in dictionaries.values():
        for mistake, corrections in pairs.items():
            combined[mistake].update(corrections)

    for name, pairs in dictionaries.items():
        chosen = sorted((mistake, next(iter(corrections))) for mistake, corrections in pairs.items()
                        if len(combined[mistake]) == 1)
        payload = "".join(f"{mistake}\t{correction}\n" for mistake, correction in chosen).encode("utf-8")
        (DATA / f"{language}-typos-{name}.gz").write_bytes(gzip.compress(payload, mtime=0))
        print(f"{language} {name}: {len(chosen):,} pairs")
    print(f"{language} combined: {sum(len(corrections) == 1 for corrections in combined.values()):,} distinct corrections")


def main():
    russian = frequency_words("ru")
    english = frequency_words("en")
    with gzip.open(DATA / "kk.gz", "rt", encoding="utf-8") as stream:
        kazakh = set(stream.read().splitlines())
    write_dictionaries("ru", {"kartaslov": kartaslov(russian), "ruspellru": ruspellru(russian)})
    write_dictionaries("en", {"toefl": toefl(english)})
    write_dictionaries("kk", {"sauatai": sauatai(kazakh, russian)})


if __name__ == "__main__":
    main()
