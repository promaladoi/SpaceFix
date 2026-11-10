using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace LayoutPilot
{
    enum Language { English, Russian, Kazakh }

    sealed class KeyboardMap
    {
        public readonly IntPtr Handle;
        public readonly Language Language;
        private readonly char[] letters = new char[512];
        private static readonly Dictionary<IntPtr, KeyboardMap> Cache = new Dictionary<IntPtr, KeyboardMap>();

        public KeyboardMap(IntPtr handle, Language language)
        {
            Handle = handle;
            Language = language;
            var state = new byte[256];
            var buffer = new char[8];
            for (uint scan = 0x02; scan <= 0x56; scan++)
            {
                if (scan > 0x35 && scan != 0x56) continue;
                uint key = Native.MapVirtualKeyEx(scan, 3, handle);
                if (key == 0) continue;
                for (int modifier = 0; modifier < 4; modifier++)
                {
                    state[0x10] = (modifier & 1) != 0 ? (byte)128 : (byte)0;
                    state[0x14] = (modifier & 2) != 0 ? (byte)1 : (byte)0;
                    // Flag 4 asks Windows to leave the keyboard's dead-key state alone.
                    if (Native.ToUnicodeEx(key, scan, state, buffer, buffer.Length, 4, handle) == 1 && buffer[0] > ' ')
                        letters[scan * 4 + modifier] = buffer[0];
                }
            }
        }

        public char At(int stroke) { return stroke >= 0 && stroke < letters.Length ? letters[stroke] : '\0'; }

        public static KeyboardMap From(IntPtr handle)
        {
            KeyboardMap map;
            if (Cache.TryGetValue(handle, out map)) return map;
            Language language;
            switch (handle.ToInt64() & 0xffff)
            {
                case 0x0409: language = Language.English; break;
                case 0x0419: language = Language.Russian; break;
                case 0x043f: language = Language.Kazakh; break;
                default: return null;
            }
            map = new KeyboardMap(handle, language);
            // Match the standard layouts even if a different variant shares the language ID.
            bool standard = language == Language.English
                ? map.At(0x10 * 4) == 'q' && map.At(0x28 * 4) == '\''
                : map.At(0x10 * 4) == 'й' && (language == Language.Kazakh || map.At(0x03 * 4) == '2');
            Cache[handle] = standard ? map : null;
            return Cache[handle];
        }

        public static KeyboardMap[] Installed()
        {
            var handles = new IntPtr[Native.GetKeyboardLayoutList(0, null)];
            int count = Native.GetKeyboardLayoutList(handles.Length, handles);
            return handles.Take(count).Select(From).Where(map => map != null)
                .GroupBy(map => map.Language).Select(group => group.First()).ToArray();
        }

        public static string Label(Language language)
        {
            if (language == Language.English) return "English";
            if (language == Language.Russian) return "Русский";
            return "Қазақша";
        }
    }

    sealed class Candidate
    {
        public readonly string Text;
        public readonly KeyboardMap Map;
        public readonly int Rank;

        public Candidate(string text, KeyboardMap map, int rank)
        {
            Text = text;
            Map = map;
            Rank = rank;
        }
    }

    sealed class Recognition
    {
        private static readonly Regex EnglishWord = new Regex("^[a-z]+(?:['-][a-z]+)*$", RegexOptions.CultureInvariant);
        private static readonly Regex CyrillicWord = new Regex("^[а-яёәіңғүұқөһ]+(?:-[а-яёәіңғүұқөһ]+)*$", RegexOptions.CultureInvariant);
        private static readonly Regex Address = new Regex(
            @"(?:https?://|www\.|@|\.(?:com|net|org|ru|kz|io|exe|txt|pdf|docx|xlsx|zip)(?:[/\\?#]|$)|^[a-z]:[\\/])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly char[] Punctuation = { '.', ',', '!', '?', ':', ';' };
        private const string KazakhLetters = "әіңғүұқөһ";
        private const string RussianLetters = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
        private const string EnglishLetters = "abcdefghijklmnopqrstuvwxyz";
        private const int ShortWordLength = 3;
        private const int CommonShortWordRank = 2000;
        private const int RarePenalty = 100000;
        // The Kazakh dictionary has no frequency ranks.
        private static readonly HashSet<string> CommonShortKazakh = new HashSet<string>(
            ("ал бар бер бір бұл деп дәл екі еді ел ең жоқ кім күн көп мен не ол өзі " +
             "өте қой сен сол сөз тек тіл үш үй").Split(' '));
        private static readonly HashSet<string> CommonKazakh = new HashSet<string>(
            ("сәлем сәлеметсіз сәлеметсіздер рахмет рақмет қазақ қазақша қазақстан " +
             "қайырлы күніңіз қалай қалайсыз жақсы керемет бүгін ертең әдемі өтінемін көмектесіңіз " +
             "кездескенше сау болыңыз жарайды жазамын жазып тұрмын тіліндегі жұмысыңыз").Split(' '));
        private static readonly Dictionary<string, string> KazakhWrittenWithRussianLetters =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "салем", "сәлем" },
                { "салеметсин", "сәлеметсің" },
                { "салеметсиз", "сәлеметсіз" },
                { "салеметсиздер", "сәлеметсіздер" },
                { "салеметсизбе", "сәлеметсіз бе" },
                { "казакша", "қазақша" },
                { "казакстан", "қазақстан" },
                { "калай", "қалай" },
                { "калайсын", "қалайсың" },
                { "калайсындар", "қалайсыңдар" },
                { "калайсыз", "қалайсыз" },
                { "калайсыздар", "қалайсыздар" },
                { "кайда", "қайда" },
                { "кайырлы", "қайырлы" },
                { "жаксы", "жақсы" },
                { "жаксымын", "жақсымын" },
                { "жаксылык", "жақсылық" },
                { "бугин", "бүгін" },
                { "бугын", "бүгін" },
                { "ертен", "ертең" },
                { "танертен", "таңертең" },
                { "кунин", "күнің" },
                { "куниниз", "күніңіз" },
                { "кун", "күн" },
                { "кутты", "құтты" },
                { "адеми", "әдемі" },
                { "отиниш", "өтініш" },
                { "отинемин", "өтінемін" },
                { "комек", "көмек" },
                { "комектесиниз", "көмектесіңіз" },
                { "кешир", "кешір" },
                { "кешириниз", "кешіріңіз" },
                { "журек", "жүрек" },
                { "журегим", "жүрегім" },
                { "соган", "соған" },
                { "саган", "саған" },
                { "маган", "маған" },
                { "оте", "өте" },
                { "озин", "өзің" },
                { "озим", "өзім" },
                { "соз", "сөз" },
                { "кош", "қош" },
                { "куаныш", "қуаныш" },
                { "казир", "қазір" },
                { "кызык", "қызық" },
                { "билмеймин", "білмеймін" },
                { "тусиндим", "түсіндім" },
                { "тусинбедим", "түсінбедім" },
                { "келесин", "келесің" },
                { "коремин", "көремін" },
                { "сойлесемиз", "сөйлесеміз" },
                { "жумыс", "жұмыс" },
                { "жумысыныз", "жұмысыңыз" }
            };
        private readonly Dictionary<string, int> english = LoadFrequencies("en");
        private readonly Dictionary<string, int> russian = LoadFrequencies("ru");
        private readonly HashSet<string> kazakh = LoadKazakh();
        private readonly Dictionary<string, string> kazakhLexicalAliases =
            LoadTypos("kk-russian-letters-myspell", "kk-russian-letters-cefr");
        private readonly PersonalDictionary personal;
        private readonly Dictionary<Language, Dictionary<string, string>> typos =
            new Dictionary<Language, Dictionary<string, string>>
            {
                { Language.English, LoadTypos("en-typos-toefl") },
                { Language.Russian, LoadTypos("ru-typos-kartaslov", "ru-typos-ruspellru") },
                { Language.Kazakh, LoadTypos("kk-typos-sauatai") }
            };

        public Recognition(PersonalDictionary personal = null) { this.personal = personal; }

        public Candidate Find(string original, IList<int> strokes, KeyboardMap current, KeyboardMap[] layouts)
        {
            if (Address.IsMatch(original)) return null;
            string bare = original.TrimEnd(Punctuation);
            string punctuation = original.Substring(bare.Length);
            string replacement;
            if (personal != null && personal.TryGet(bare, out replacement))
                return replacement == null || replacement == bare ? null : new Candidate(replacement + punctuation, current, -1);
            if (original.Length < 2 || !original.Any(char.IsLetter)) return null;

            string lower = bare.ToLowerInvariant();
            string kazakhSpelling;
            if (TryKazakhSpelling(bare, lower, current.Language, out kazakhSpelling))
            {
                KeyboardMap kazakhLayout = current.Language == Language.Kazakh ? current
                    : layouts.FirstOrDefault(map => map.Language == Language.Kazakh);
                if (kazakhLayout != null)
                    return kazakhSpelling == null ? null : new Candidate(kazakhSpelling + punctuation, kazakhLayout, -1);
            }
            if (Known(lower, current.Language) || (current.Language != Language.English
                && (Known(lower, Language.Russian) || Known(lower, Language.Kazakh))))
                return null;

            string listed = ListedSpelling(bare, current.Language);
            if (listed != null) return new Candidate(listed + punctuation, current, -1);

            var choices = new List<Candidate>();
            foreach (KeyboardMap map in layouts)
            {
                if (map.Language == current.Language) continue;
                string converted = Convert(strokes, strokes.Count, map);
                if (AddIfKnown(original, converted, "", map, choices)) continue;
                if (punctuation.Length > 0)
                    AddIfKnown(original, Convert(strokes, bare.Length, map), punctuation, map, choices);
            }
            if (bare.Length <= ShortWordLength)
            {
                var confident = choices.Where(choice => IsConfidentLayoutWord(
                    choice.Text.TrimEnd(Punctuation).ToLowerInvariant(), choice.Map.Language)).ToList();
                return confident.Select(choice => choice.Text).Distinct().Count() == 1 ? BestChoice(confident) : null;
            }
            if (choices.Count > 0) return BestChoice(choices);

            AddNearChoice(bare, punctuation, current, choices);
            foreach (KeyboardMap map in layouts)
            {
                if (map.Language == current.Language) continue;
                string converted = Convert(strokes, bare.Length, map);
                AddNearChoice(converted, punctuation, map, choices);
            }
            return BestChoice(choices);
        }

        private static Candidate BestChoice(List<Candidate> choices)
        {
            var ordered = choices.OrderBy(choice => choice.Rank).ToArray();
            if (ordered.Length == 0) return null;
            if (ordered.Length > 1 && ordered[0].Text != ordered[1].Text
                && (ordered[0].Rank == ordered[1].Rank || ordered[0].Rank >= RarePenalty))
                return null;
            return ordered[0];
        }

        public string ConvertContext(string original, IList<int> strokes, KeyboardMap current, KeyboardMap target)
        {
            if (original == null) return null;
            string bare = original.TrimEnd(Punctuation);
            string unused;
            if (personal != null && personal.TryGet(bare, out unused)) return null;
            string restored;
            if (TryKazakhSpelling(bare, bare.ToLowerInvariant(), current.Language, out restored))
                return target.Language == Language.Kazakh && restored != null ? restored + original.Substring(bare.Length) : null;
            string converted = Convert(strokes, strokes.Count, target);
            if (converted == null || converted == original) return null;
            string lower = converted.ToLowerInvariant();
            if (original.Length == 1)
            {
                string singleWords = target.Language == Language.English ? "ai"
                    : target.Language == Language.Russian ? "авикосуя" : "аое";
                return char.IsLetter(original[0]) && singleWords.IndexOf(lower, StringComparison.Ordinal) >= 0
                    ? converted : null;
            }
            if (Address.IsMatch(original) || ListedSpelling(bare, current.Language) != null)
                return null;
            string originalLower = original.ToLowerInvariant();
            if (Known(originalLower, current.Language) || (current.Language != Language.English
                && (Known(originalLower, Language.Russian) || Known(originalLower, Language.Kazakh)))) return null;
            if (Known(lower, target.Language))
                return IsConfidentLayoutWord(lower, target.Language) ? converted : null;
            var choices = new List<Candidate>();
            AddNearChoice(converted, "", target, choices);
            return choices.Count == 0 ? null : choices[0].Text;
        }

        private bool IsConfidentLayoutWord(string lower, Language language)
        {
            return lower.Length > ShortWordLength || (language == Language.Kazakh
                ? CommonShortKazakh.Contains(lower) : DictionaryRank(lower, language) <= CommonShortWordRank);
        }

        private bool TryKazakhSpelling(string word, string lower, Language language, out string restored)
        {
            restored = null;
            if (language == Language.English) return false;
            string spelling;
            bool curated = KazakhWrittenWithRussianLetters.TryGetValue(lower, out spelling);
            if (!curated && !kazakhLexicalAliases.TryGetValue(lower, out spelling)) return false;
            // The common Kazakh greeting is also present in the Russian corpus as a name.
            if ((lower != "салем" && Known(lower, Language.Russian)) || ListedSpelling(word, language) != null)
                return false;
            if (spelling.Length > 0 && (curated || IsConfidentLayoutWord(spelling, Language.Kazakh)))
                restored = MatchCase(word, spelling);
            return true;
        }

        private string ListedSpelling(string word, Language language)
        {
            string lower = word.ToLowerInvariant();
            if (language != Language.English && lower == "еше") return MatchCase(word, "ещё");
            if (language == Language.Russian && lower == "переименуи") return MatchCase(word, "переименуй");
            string correction;
            return typos[language].TryGetValue(lower, out correction) ? MatchCase(word, correction) : null;
        }

        private void AddNearChoice(string word, string punctuation, KeyboardMap map, List<Candidate> choices)
        {
            if (word == null || word.Length <= ShortWordLength || word.Length > 24) return;
            Language language = map.Language;
            string listed = ListedSpelling(word, language);
            if (listed != null)
            {
                choices.Add(new Candidate(listed + punctuation, map, -1));
                return;
            }
            string lower = word.ToLowerInvariant();
            string alphabet = language == Language.English ? EnglishLetters
                : language == Language.Russian ? RussianLetters : RussianLetters + KazakhLetters;
            if (lower.Any(letter => alphabet.IndexOf(letter) < 0)) return;

            string best = null;
            int bestRank = int.MaxValue;
            bool tied = false;
            string rare = null;
            int rareRank = 0;
            bool rareAmbiguous = false;
            int frequentLimit = language == Language.English ? 8000 : 12000;
            Action<string, int> consider = (candidate, editCost) =>
            {
                int rank = DictionaryRank(candidate, language);
                if (rank == int.MaxValue) return;
                if (rank > frequentLimit)
                {
                    if (rare == null) { rare = candidate; rareRank = rank; }
                    else if (rare != candidate) rareAmbiguous = true;
                    return;
                }
                rank += editCost * 30000;
                if (rank < bestRank)
                {
                    best = candidate;
                    bestRank = rank;
                    tied = false;
                }
                else if (rank == bestRank && candidate != best) tied = true;
            };
            for (int index = 0; index <= lower.Length; index++)
            {
                string head = lower.Substring(0, index);
                string tail = lower.Substring(index);
                if (tail.Length > 0)
                {
                    string remaining = tail.Substring(1);
                    bool repeated = (index > 0 && lower[index - 1] == tail[0])
                        || (tail.Length > 1 && tail[0] == tail[1]);
                    consider(head + remaining, repeated ? 0 : 2);
                    foreach (char letter in alphabet)
                        if (letter != tail[0]) consider(head + letter + remaining, 2);
                    if (tail.Length > 1 && tail[0] != tail[1])
                        consider(head + tail[1] + tail[0] + tail.Substring(2), 0);
                }
                foreach (char letter in alphabet)
                    consider(head + letter + tail, (index > 0 && letter == lower[index - 1])
                        || (tail.Length > 0 && letter == tail[0]) ? 1 : 2);
            }
            // Use the full dictionary for a longer token only if there is a single rare match.
            if (best == null && lower.Length >= 5 && rare != null && !rareAmbiguous)
            {
                best = rare;
                bestRank = RarePenalty + rareRank;
            }
            if (best == null || tied) return;
            choices.Add(new Candidate(MatchCase(word, best) + punctuation, map, bestRank));
        }

        private static string MatchCase(string word, string replacement)
        {
            if (word.All(char.IsUpper)) return replacement.ToUpperInvariant();
            if (char.IsUpper(word[0])) return char.ToUpperInvariant(replacement[0]) + replacement.Substring(1);
            return replacement;
        }

        private int DictionaryRank(string word, Language language)
        {
            int rank;
            if (language == Language.English)
                return english.TryGetValue(word, out rank) ? rank : int.MaxValue;
            if (language == Language.Russian)
                return russian.TryGetValue(word, out rank) ? rank : int.MaxValue;
            if (CommonKazakh.Contains(word)) return 0;
            return word.Any(letter => KazakhLetters.IndexOf(letter) >= 0) && kazakh.Contains(word) ? 1 : int.MaxValue;
        }

        private bool AddIfKnown(string original, string converted, string punctuation, KeyboardMap map, List<Candidate> choices)
        {
            if (converted == null || converted + punctuation == original) return false;
            string lower = converted.ToLowerInvariant();
            if (!Known(lower, map.Language)) return false;

            int rank;
            switch (map.Language)
            {
                case Language.English:
                    rank = english[lower];
                    break;
                case Language.Russian:
                    rank = russian[lower];
                    break;
                default:
                    rank = lower.Any(letter => KazakhLetters.IndexOf(letter) >= 0) || CommonKazakh.Contains(lower)
                        ? 0 : 50000;
                    break;
            }
            choices.Add(new Candidate(converted + punctuation, map, rank));
            return true;
        }

        private bool Known(string word, Language language)
        {
            switch (language)
            {
                case Language.English: return EnglishWord.IsMatch(word) && english.ContainsKey(word);
                case Language.Russian: return CyrillicWord.IsMatch(word) && russian.ContainsKey(word);
                default: return CyrillicWord.IsMatch(word) && kazakh.Contains(word);
            }
        }

        internal static string Convert(IList<int> strokes, int count, KeyboardMap map)
        {
            var result = new char[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = map.At(strokes[i]);
                if (result[i] == '\0') return null;
            }
            return new string(result);
        }

        private static StreamReader ReadDictionary(string code)
        {
            var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("words." + code + ".gz");
            return new StreamReader(new GZipStream(resource, CompressionMode.Decompress));
        }

        private static Dictionary<string, string> LoadTypos(params string[] dictionaries)
        {
            var corrections = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string source in dictionaries)
            {
                using (var reader = ReadDictionary(source))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        int separator = line.IndexOf('\t');
                        corrections[line.Substring(0, separator)] = line.Substring(separator + 1);
                    }
                }
            }
            return corrections;
        }

        private static Dictionary<string, int> LoadFrequencies(string code)
        {
            var words = new Dictionary<string, int>(StringComparer.Ordinal);
            using (var reader = ReadDictionary(code))
            {
                int rank = 0;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    rank++;
                    int separator = line.IndexOf(' ');
                    int count;
                    if (separator > 0 && int.TryParse(line.Substring(separator + 1), NumberStyles.None,
                        CultureInfo.InvariantCulture, out count) && count >= 5)
                    {
                        string word = line.Substring(0, separator).ToLowerInvariant();
                        if (!words.ContainsKey(word)) words.Add(word, rank);
                    }
                }
            }
            if (code == "ru")
            {
                foreach (string word in new[] { "хром", "хрома", "хроме", "хрому", "хромом", "переименуй" })
                    if (!words.ContainsKey(word)) words.Add(word, 25000);
            }
            return words;
        }

        private static HashSet<string> LoadKazakh()
        {
            var words = new HashSet<string>(StringComparer.Ordinal);
            using (var reader = ReadDictionary("kk"))
            {
                string line;
                while ((line = reader.ReadLine()) != null) words.Add(line);
            }
            // Common conversational forms and inflections also needed by the built-in demo.
            words.UnionWith(CommonKazakh);
            return words;
        }
    }
}
