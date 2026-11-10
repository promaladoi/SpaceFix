using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LayoutPilot
{
    sealed class PersonalDictionary
    {
        public const int MaxWordLength = 48;
        public const int MaxReplacementLength = 200;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly Regex Word = new Regex(
            @"\A[a-zа-яёәіңғүұқөһ0-9]+(?:['-][a-zа-яёәіңғүұқөһ0-9]+)*\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private readonly string path;
        // A null value protects the word from both spelling and layout corrections.
        private Dictionary<string, string> entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public PersonalDictionary(string path)
        {
            this.path = path;
            try
            {
                var saved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in File.ReadAllLines(path, Utf8))
                {
                    int separator = line.IndexOf('\t');
                    if (separator <= 0) throw new InvalidDataException();
                    string word = line.Substring(0, separator);
                    string value = line.Substring(separator + 1);
                    if (!IsValidWord(word) || (value.Length > 0 && !IsValidReplacement(value)) || saved.ContainsKey(word))
                        throw new InvalidDataException();
                    saved.Add(word, value.Length == 0 ? null : value);
                }
                SetEntries(saved);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (Exception error)
            {
                if (!(error is IOException || error is UnauthorizedAccessException ||
                    error is InvalidDataException || error is DecoderFallbackException)) throw;
                LoadFailed = true;
            }
        }

        public bool LoadFailed { get; private set; }
        public bool HasSingleLetterRules { get; private set; }

        public IEnumerable<KeyValuePair<string, string>> Entries
        {
            get { return entries.OrderBy(entry => entry.Key, StringComparer.CurrentCultureIgnoreCase); }
        }

        public bool TryGet(string word, out string replacement) { return entries.TryGetValue(word, out replacement); }

        public static bool IsValidWord(string word)
        {
            return word != null && word.Length <= MaxWordLength && Word.IsMatch(word) && word.Any(char.IsLetter);
        }

        public static bool IsValidReplacement(string replacement)
        {
            return !string.IsNullOrWhiteSpace(replacement) && replacement.Length <= MaxReplacementLength &&
                !replacement.Any(letter => char.IsControl(letter) || letter == '\u2028' || letter == '\u2029');
        }

        public void Set(string word, string replacement)
        {
            if (!IsValidWord(word) || (replacement != null && !IsValidReplacement(replacement)))
                throw new ArgumentException("Invalid personal dictionary entry.");
            var updated = new Dictionary<string, string>(entries, entries.Comparer);
            updated[word] = replacement;
            Save(updated);
        }

        public void Remove(string word)
        {
            var updated = new Dictionary<string, string>(entries, entries.Comparer);
            if (updated.Remove(word)) Save(updated);
        }

        private void Save(Dictionary<string, string> updated)
        {
            if (LoadFailed) throw new IOException("The personal dictionary could not be read.");
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllLines(temporary, updated.Select(entry => entry.Key + "\t" + entry.Value), Utf8);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                SetEntries(updated);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private void SetEntries(Dictionary<string, string> updated)
        {
            entries = updated;
            HasSingleLetterRules = entries.Keys.Any(word => word.Length == 1);
        }
    }
}
