#!/usr/bin/env python3
"""
Hunspell Kazakh dictionary expander.
Generates all inflected word forms from kk_KZ.dic + kk_KZ.aff
and filters out proper nouns, abbreviations, and invalid content.

Source: https://github.com/kergalym/myspell-kk
License: MPL 1.1 (chosen from triple GPL/LGPL/MPL license)
"""

import re
import sys
import os

# Kazakh Cyrillic alphabet (lowercase): standard Russian + Kazakh-specific letters
# ә, і, ң, ғ, ү, ұ, қ, ө, һ are specific to Kazakh
KAZAKH_ALPHABET = set(
    'абвгғдеёжзийкқлмнңоөпрстуұүфхһцчшщъыьэюяәі'
)

# Kazakh vowels (for word validity check)
KAZAKH_VOWELS = set('аеёиоуыэюяәіөүұ')


def parse_aff(aff_path):
    """Parse Hunspell .aff file and return suffix/prefix rules."""
    sfx_rules = {}  # flag -> list of (strip, add, condition_regex)
    pfx_rules = {}  # flag -> list of (strip, add, condition_regex)
    current_sfx = None
    current_pfx = None

    with open(aff_path, 'r', encoding='utf-8-sig') as f:
        lines = f.readlines()

    i = 0
    while i < len(lines):
        line = lines[i].strip()
        i += 1

        if not line or line.startswith('#'):
            continue

        parts = line.split()
        if not parts:
            continue

        directive = parts[0]

        # Handle SFX rules
        if directive == 'SFX' and len(parts) >= 3:
            flag = parts[1]
            if len(parts) == 4:  # Header line: SFX flag Y/N count
                sfx_rules[flag] = []
                current_sfx = flag
                current_pfx = None
            elif current_sfx == flag and len(parts) >= 5:
                strip_str = parts[2] if parts[2] != '0' else ''
                add_str = parts[3] if parts[3] != '0' else ''
                # Remove any flags from the add string (e.g., "ів/A" -> "ів")
                add_str = add_str.split('/')[0]
                condition_str = parts[4] if len(parts) > 4 else '.'
                try:
                    if condition_str == '.':
                        condition_re = None
                    else:
                        condition_re = re.compile(condition_str + '$')
                    sfx_rules[flag].append((strip_str, add_str, condition_re))
                except re.error:
                    pass
            continue

        # Handle PFX rules
        if directive == 'PFX' and len(parts) >= 3:
            flag = parts[1]
            if len(parts) == 4:  # Header line
                pfx_rules[flag] = []
                current_pfx = flag
                current_sfx = None
            elif current_pfx == flag and len(parts) >= 5:
                strip_str = parts[2] if parts[2] != '0' else ''
                add_str = parts[3] if parts[3] != '0' else ''
                add_str = add_str.split('/')[0]
                condition_str = parts[4] if len(parts) > 4 else '.'
                try:
                    if condition_str == '.':
                        condition_re = None
                    else:
                        condition_re = re.compile('^' + condition_str)
                    pfx_rules[flag].append((strip_str, add_str, condition_re))
                except re.error:
                    pass
            continue

    return sfx_rules, pfx_rules


def expand_word(stem, flags, sfx_rules, pfx_rules):
    """Generate all word forms from a stem + set of flags."""
    forms = set()
    forms.add(stem)

    for flag in flags:
        if flag in sfx_rules:
            for (strip_str, add_str, condition_re) in sfx_rules[flag]:
                if condition_re is not None:
                    if not condition_re.search(stem):
                        continue
                if strip_str:
                    if stem.endswith(strip_str):
                        new_stem = stem[:-len(strip_str)]
                    else:
                        continue
                else:
                    new_stem = stem
                new_form = new_stem + add_str
                if new_form:
                    forms.add(new_form)

        if flag in pfx_rules:
            for (strip_str, add_str, condition_re) in pfx_rules[flag]:
                if condition_re is not None:
                    if not condition_re.search(stem):
                        continue
                if strip_str:
                    if stem.startswith(strip_str):
                        new_stem = stem[len(strip_str):]
                    else:
                        continue
                else:
                    new_stem = stem
                new_form = add_str + new_stem
                if new_form:
                    forms.add(new_form)

    return forms


def is_valid_kazakh_word(word):
    """Check if a word is a valid Kazakh word for use in a word game."""
    if not word:
        return False
    if len(word) < 2:
        return False
    if '-' in word or ' ' in word:
        return False

    # Must consist only of Kazakh Cyrillic characters
    for ch in word:
        if ch.lower() not in KAZAKH_ALPHABET:
            return False

    # Must contain at least one vowel
    if not any(c.lower() in KAZAKH_VOWELS for c in word):
        return False

    return True


def is_proper_noun(word):
    """Check if a word is a proper noun (starts with uppercase)."""
    if not word:
        return True
    return word[0].isupper()


def is_abbreviation(word):
    """Check if a word looks like an abbreviation or acronym (all uppercase, length > 1)."""
    if not word:
        return True
    if len(word) > 1 and word.isupper():
        return True
    return False


def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    aff_path = os.path.join(script_dir, 'kk_KZ.aff')
    dic_path = os.path.join(script_dir, 'kk_KZ.dic')
    os.makedirs(os.path.join(script_dir, 'output'), exist_ok=True)
    output_path = os.path.join(script_dir, 'output', 'kazakh_kk_kz_cyrillic.txt')

    print("Parsing .aff file...", flush=True)
    sfx_rules, pfx_rules = parse_aff(aff_path)
    print(f"  Found {len(sfx_rules)} suffix flag rules, {len(pfx_rules)} prefix flag rules", flush=True)

    all_words = set()
    skipped_proper = 0
    skipped_invalid = 0
    skipped_abbrev = 0
    total_stems = 0

    print("Processing .dic file...", flush=True)

    with open(dic_path, 'r', encoding='utf-8-sig') as f:
        # Skip the first line (word count)
        first_line = f.readline().strip()
        print(f"  Dictionary declares {first_line} entries", flush=True)

        for line_num, line in enumerate(f, start=2):
            line = line.strip()
            if not line or line.startswith('#'):
                continue

            # Split stem from flags: format is stem/flags or stem
            if '/' in line:
                slash_pos = line.index('/')
                stem = line[:slash_pos]
                rest = line[slash_pos + 1:]
                flag_str = rest.split()[0] if rest else ''
                flags = set(flag_str) if flag_str else set()
            else:
                stem = line.split()[0]
                flags = set()

            stem = stem.strip()
            if not stem:
                continue

            total_stems += 1

            # Skip proper nouns (starts with uppercase — covers the 395 Kazakh names)
            if is_proper_noun(stem):
                skipped_proper += 1
                continue

            # Skip all-uppercase abbreviations/acronyms
            if is_abbreviation(stem):
                skipped_abbrev += 1
                continue

            # Expand all word forms using the affix rules
            forms = expand_word(stem, flags, sfx_rules, pfx_rules)

            for form in forms:
                form_lower = form.lower()
                if not is_proper_noun(form) and is_valid_kazakh_word(form_lower):
                    all_words.add(form_lower)
                else:
                    skipped_invalid += 1

            if total_stems % 10000 == 0:
                print(f"  Processed {total_stems} stems, generated {len(all_words)} forms so far...", flush=True)

    print(f"\nTotal stems processed: {total_stems}", flush=True)
    print(f"Skipped proper nouns (including names): {skipped_proper}", flush=True)
    print(f"Skipped abbreviations: {skipped_abbrev}", flush=True)
    print(f"Skipped invalid forms: {skipped_invalid}", flush=True)
    print(f"Total unique word forms generated: {len(all_words)}", flush=True)

    print("Sorting...", flush=True)
    sorted_words = sorted(all_words)

    print(f"Writing to {output_path}...", flush=True)
    with open(output_path, 'w', encoding='utf-8') as f:
        f.write('\n'.join(sorted_words))
        f.write('\n')

    print(f"Done! Wrote {len(sorted_words)} words to {output_path}", flush=True)

    print("\nSample words (around index 1000):", flush=True)
    for word in sorted_words[1000:1015]:
        print(f"  {word}")

    print("\nLongest words:", flush=True)
    for word in sorted(all_words, key=len, reverse=True)[:10]:
        print(f"  {word} ({len(word)} chars)")


if __name__ == '__main__':
    main()
