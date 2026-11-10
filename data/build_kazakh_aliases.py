"""Build offline Russian-letter forms from the Kazakh CEFR and MySpell dictionaries."""

from collections import defaultdict
import gzip
from itertools import product
import json
from pathlib import Path
import re


DATA = Path(__file__).resolve().parent
SOURCE = DATA.parent / "downloads" / "kazakh_cefr_lexicon.json"
KAZAKH_LETTERS = "әғіқңөұүһ"
PLAIN_LETTERS = str.maketrans("әғқңөұүһ", "агкноуух")
# Treat и and ы as equivalent for the collision check, since both can stand for і.
SIGNATURE = str.maketrans(KAZAKH_LETTERS + "ы", "агикноуухи")
WORD = re.compile(r"[а-яёәғіқңөұүһ]+(?:-[а-яёәғіқңөұүһ]+)*")


def russian_spellings(word):
    parts = word.translate(PLAIN_LETTERS).split("і")
    for letters in product("иы", repeat=len(parts) - 1):
        yield parts[0] + "".join(letter + part for letter, part in zip(letters, parts[1:]))


def dictionary_words():
    with (DATA / "kk_KZ.dic").open(encoding="utf-8-sig") as stream:
        next(stream)  # Hunspell entry count.
        words = (line.split()[0].split("/", 1)[0] for line in stream if line.strip())
        return {word for word in words if 2 <= len(word) <= 48 and WORD.fullmatch(word)
                and any(letter in word for letter in KAZAKH_LETTERS)}


def unique_pairs(words, matches, russian):
    return {source: word for word in words if matches[word.translate(SIGNATURE)] == {word}
            for source in russian_spellings(word) if source not in russian}


def write_pairs(name, pairs):
    payload = "".join(f"{source}\t{pairs[source]}\n" for source in sorted(pairs)).encode("utf-8")
    (DATA / f"kk-russian-letters-{name}.gz").write_bytes(gzip.compress(payload, mtime=0))
    spellings = {word for word in pairs.values() if word}
    blocked = sum(not word for word in pairs.values())
    print(f"{name}: {len(pairs) - blocked} aliases for {len(spellings)} Kazakh spellings, {blocked} ambiguous inputs")


def main():
    rows = json.loads(SOURCE.read_text(encoding="utf-8-sig"))
    cefr = {row["lemma"] for row in rows
            if row["cefr"] in {"A1", "A2", "B1", "B2", "C1"} and 4 <= len(row["lemma"]) <= 24
            and WORD.fullmatch(row["lemma"]) and any(letter in row["lemma"] for letter in KAZAKH_LETTERS)}
    myspell = dictionary_words()
    candidates = cefr | myspell
    signatures = {word.translate(SIGNATURE) for word in candidates}
    matches = defaultdict(set)
    confirmed = set()
    with gzip.open(DATA / "kk.gz", "rt", encoding="utf-8") as stream:
        for line in stream:
            word = line.rstrip("\n").lower()
            signature = word.translate(SIGNATURE)
            if signature in signatures:
                matches[signature].add(word)
            if word in candidates:
                confirmed.add(word)
    for row in rows:
        word = row["lemma"].lower().rstrip(".,!?;:")
        signature = word.translate(SIGNATURE)
        if signature in signatures:
            matches[signature].add(word)
    with gzip.open(DATA / "ru.gz", "rt", encoding="utf-8") as stream:
        russian = {line.split(" ", 1)[0].lower() for line in stream}
    for name in ("ru-typos-kartaslov", "ru-typos-ruspellru"):
        with gzip.open(DATA / f"{name}.gz", "rt", encoding="utf-8") as stream:
            russian.update(line.split("\t", 1)[0] for line in stream)

    cefr_pairs = unique_pairs(confirmed & cefr, matches, russian)
    # An empty value protects an ambiguous input from the approximate spelling pass.
    myspell_pairs = {source: word if matches[word.translate(SIGNATURE)] == {word} else ""
                     for word in confirmed & myspell for source in russian_spellings(word)
                     if source not in russian and source not in cefr_pairs}
    write_pairs("cefr", cefr_pairs)
    write_pairs("myspell", myspell_pairs)


if __name__ == "__main__":
    main()
