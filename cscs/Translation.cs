using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;


namespace SplitAndMerge
{
    public class Translation
    {
        static string[] latUp = { "Shch", "_MZn", "_TZn", "Ts", "Ch", "Sh", "Yu", "Ya", "Ye", "Yo", "Zh", "X", "Q", "W", "A", "B", "V", "G", "D", "Z", "I", "J", "K", "C", "L", "M", "N", "O", "P", "R", "S", "T", "U", "F", "H", "Y", "E" };
        static string[] latLo = { "shch", "_mzh", "_tzn", "ts", "ch", "sh", "yu", "ya", "ye", "yo", "zh", "x", "q", "w", "a", "b", "v", "g", "d", "z", "i", "j", "k", "c", "l", "m", "n", "o", "p", "r", "s", "t", "u", "f", "h", "y", "e" };
        static string[] rusUp = { "Щ", "Ъ", "Ь", "Ц", "Ч", "Ш", "Ю", "Я", "Е", "Ё", "Ж", "Кс", "Кю", "Уи", "А", "Б", "В", "Г", "Д", "З", "И", "Й", "К", "К", "Л", "М", "Н", "О", "П", "Р", "С", "Т", "У", "Ф", "Х", "Ы", "Э" };
        static string[] rusLo = { "щ", "ъ", "ь", "ц", "ч", "ш", "ю", "я", "е", "ё", "ж", "кс", "кю", "уи", "а", "б", "в", "г", "д", "з", "и", "й", "к", "к", "л", "м", "н", "о", "п", "р", "с", "т", "у", "ф", "х", "ы", "э" };

        private static HashSet<string> s_nativeWords = new HashSet<string>();
        private static HashSet<string> s_tempWords = new HashSet<string>();

        private static Dictionary<string, string> s_spellErrors =
            new Dictionary<string, string>();

        private static Dictionary<string, Dictionary<string, string>> s_keywords =
            new Dictionary<string, Dictionary<string, string>>();

        private static Dictionary<string, Dictionary<string, string>> s_dictionaries =
            new Dictionary<string, Dictionary<string, string>>();

        private static Dictionary<string, Dictionary<string, string>> s_errors =
            new Dictionary<string, Dictionary<string, string>>();

        // The default user language. Can be changed in settings.
        private static string s_language = Constants.ENGLISH;
        public static string Language { set { s_language = value; } }

        public static void AddNativeKeyword(string word)
        {
            s_nativeWords.Add(word);
            AddSpellError(word);
        }
        public static void AddTempKeyword(string word)
        {
            s_tempWords.Add(word);
            AddSpellError(word);
        }
        public static void AddSpellError(string word)
        {
            if (word.Length > 2)
            {
                s_spellErrors[word.Substring(0, word.Length - 1)] = word;
                s_spellErrors[word.Substring(1)] = word;
            }
        }

        public static Dictionary<string, string> KeywordsDictionary(string fromLang, string toLang)
        {
            return GetDictionary(fromLang, toLang, s_keywords);
        }
        public static Dictionary<string, string> TranslationDictionary(string fromLang, string toLang)
        {
            return GetDictionary(fromLang, toLang, s_dictionaries);
        }
        public static Dictionary<string, string> ErrorDictionary(string lang)
        {
            return GetDictionary(lang, s_dictionaries);
        }

        static Dictionary<string, string> GetDictionary(string fromLang, string toLang,
                             Dictionary<string, Dictionary<string, string>> dictionaries)
        {
            string key = fromLang + "-->" + toLang;
            return GetDictionary(key, dictionaries);
        }

        static Dictionary<string, string> GetDictionary(string key,
                             Dictionary<string, Dictionary<string, string>> dictionaries)
        {
            Dictionary<string, string> result;
            if (!dictionaries.TryGetValue(key, out result))
            {
                result = new Dictionary<string, string>();
                dictionaries[key] = result;
            }
            return result;
        }

        public static void TryLoadDictionary(string dirname, string fromLang, string toLang)
        {
            if (String.IsNullOrEmpty(dirname) || !Directory.Exists(dirname))
            {
                return;
            }
            string filename = Path.Combine(dirname, fromLang + "_" + toLang + ".txt");
            if (File.Exists(filename))
            {
                LoadDictionary(filename, fromLang, toLang);
            }
            filename = Path.Combine(dirname, toLang + "_" + fromLang + ".txt");
            if (File.Exists(filename))
            {
                LoadDictionary(filename, toLang, fromLang);
            }
        }

        public static void LoadDictionary(string filename, string fromLang, string toLang)
        {
            Dictionary<string, string> dict1 = TranslationDictionary(fromLang, toLang);
            Dictionary<string, string> dict2 = TranslationDictionary(toLang, fromLang);

            string[] lines = Utils.GetFileLines(filename);
            foreach (string line in lines)
            {
                string[] tokens = line.Split(" ".ToCharArray(),
                                              StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 2 || tokens[0].StartsWith("#"))
                {
                    continue;
                }
                string word1 = tokens[0].Trim();
                string word2 = tokens[1].Trim();
                dict1[word1] = word2;
                dict2[word2] = word1;
            }
        }

        public static void LoadErrors(string filename)
        {
            if (!File.Exists(filename))
            {
                return;
            }
            Dictionary<string, string> dict = GetDictionary(Constants.ENGLISH, s_errors);
            string[] lines = Utils.GetFileLines(filename);
            foreach (string line in lines)
            {
                string[] tokens = line.Split("=".ToCharArray(),
                                         StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 1 || tokens[0].StartsWith("#"))
                {
                    continue;
                }
                if (tokens.Length == 1)
                {
                    dict = GetDictionary(tokens[0], s_errors);
                    continue;
                }

                dict[tokens[0].Trim()] = tokens[1].Trim();
            }
        }

        public static string TryTranslit(string fromLang, string toLang,
                                         string str)
        {
            if (fromLang == Constants.RUSSIAN)
            {
                str = Transliterate(rusUp, latUp, rusLo, latLo, str);
            }
            else if (toLang == Constants.RUSSIAN)
            {
                str = Transliterate(latUp, rusUp, latLo, rusLo, str);
            }

            return str;
        }
        private static string Transliterate(string[] up1, string[] up2,
                                            string[] lo1, string[] lo2,
                                            string str)
        {
            for (int i = 0; i < up1.Length; i++)
            {
                str = str.Replace(up1[i], up2[i]);
                str = str.Replace(lo1[i], lo2[i]);
            }
            return str;
        }

        public static string GetErrorString(string key)
        {
            string result = null;
            Dictionary<string, string> dict = GetDictionary(s_language, s_errors);
            if (dict.TryGetValue(key, out result))
            {
                return result;
            }

            if (s_language != Constants.ENGLISH)
            {
                dict = GetDictionary(Constants.ENGLISH, s_errors);
                if (dict.TryGetValue(key, out result))
                {
                    return result;
                }
            }
            return key;
        }

        public static void Add(NameValueCollection langDictionary, string origName,
                               Dictionary<string, string> translations1,
                               Dictionary<string, string> translations2)
        {
            AddNativeKeyword(origName);

            string translation = langDictionary[origName];
            if (string.IsNullOrWhiteSpace(translation))
            {
                // The translation is not provided for this function.
                translations1[origName] = origName;
                translations2[origName] = origName;
                return;
            }

            AddNativeKeyword(translation);

            translations1[origName] = translation;
            translations2[translation] = origName;

            if (translation.IndexOfAny((" \t\r\n").ToCharArray()) >= 0)
            {
                throw new ArgumentException("Translation of [" + translation + "] contains white spaces");
            }

            ParserFunction origFunction = ParserFunction.GetFunction(origName, null);
            Utils.CheckNotNull(origName, origFunction);
            ParserFunction.RegisterFunction(translation, origFunction);

            // Also add the translation to the list of functions after which there
            // can be a space (besides a parenthesis).
            if (Constants.FUNCT_WITH_SPACE.Contains(origName))
            {
                Constants.FUNCT_WITH_SPACE.Add(translation);
            }
            if (Constants.FUNCT_WITH_SPACE_ONCE.Contains(origName))
            {
                Constants.FUNCT_WITH_SPACE_ONCE.Add(translation);
            }
        }

        public static void AddSubstatement(NameValueCollection langDictionary,
                                           string origName,
                                           List<string> keywordsArray,
                                           Dictionary<string, string> translations1,
                                           Dictionary<string, string> translations2)
        {
            string translation = langDictionary[origName];
            if (string.IsNullOrWhiteSpace(translation))
            {
                // The translation is not provided for this sub statement.
                translations1[origName] = origName;
                translations2[origName] = origName;
                return;
            }

            translations1[origName] = translation;
            translations2[translation] = origName;

            if (translation.IndexOfAny((" \t\r\n").ToCharArray()) >= 0)
            {
                throw new ArgumentException("Translation of [" + translation + "] contains white spaces");
            }

            keywordsArray.Add(translation);
            s_nativeWords.Add(origName);
            s_nativeWords.Add(translation);
        }

        public static void PrintScript(string script, ParsingScript parentSript)
        {
            StringBuilder item = new StringBuilder();

            bool inQuotes = false;

            for (int i = 0; i < script.Length; i++)
            {
                char ch = script[i];
                inQuotes = ch == Constants.QUOTE ? !inQuotes : inQuotes;

                if (inQuotes)
                {
                    Interpreter.Instance.AppendOutput(ch.ToString());
                    continue;
                }
                if (!Constants.TOKEN_SEPARATION.Contains(ch))
                {
                    item.Append(ch);
                    continue;
                }
                if (item.Length > 0)
                {
                    string token = item.ToString();
                    ParserFunction func = ParserFunction.GetFunction(token, parentSript);
                    bool isNative = s_nativeWords.Contains(token);
                    if (func != null || isNative)
                    {
                        ConsoleColor col = isNative || func.isNative ? ConsoleColor.Green :
                                                       func.isGlobal ? ConsoleColor.Magenta :
                                                           ConsoleColor.Gray;
                        Utils.PrintColor(token, col);
                    }
                    else
                    {
                        Interpreter.Instance.AppendOutput(token);
                    }
                    item.Clear();
                }
                Interpreter.Instance.AppendOutput(ch.ToString());
            }
        }

        public static void TranslateScript(string[] args)
        {
            if (args.Length < 3)
            {
                return;
            }
            string fromLang = args[0];
            string toLang = args[1];
            string script = Utils.GetFileText(args[2]);

            ParsingScript parentScript = new ParsingScript(script);
            parentScript.Filename = args[2];

            string result = TranslateScript(script, fromLang, toLang, parentScript);
            Console.WriteLine(result);
        }

        public static string TranslateScript(string script, string toLang, ParsingScript parentScript)
        {
            string tempScript = TranslateScript(script, Constants.ENGLISH, Constants.ENGLISH, parentScript);
            if (toLang == Constants.ENGLISH)
            {
                return tempScript;
            }

            string result = TranslateScript(tempScript, Constants.ENGLISH, toLang, parentScript);
            return result;
        }

        static string TranslateScript(string script, string fromLang, string toLang, ParsingScript parentScript)
        {
            StringBuilder result = new StringBuilder();
            StringBuilder item = new StringBuilder();

            Dictionary<string, string> keywordsDict = KeywordsDictionary(fromLang, toLang);
            Dictionary<string, string> transDict = TranslationDictionary(fromLang, toLang);
            bool inQuotes = false;

            for (int i = 0; i < script.Length; i++)
            {
                char ch = script[i];
                inQuotes = ch == Constants.QUOTE ? !inQuotes : inQuotes;

                if (inQuotes)
                {
                    result.Append(ch);
                    continue;
                }
                if (!Constants.TOKEN_SEPARATION.Contains(ch))
                {
                    item.Append(ch);
                    continue;
                }
                if (item.Length > 0)
                {
                    string token = item.ToString();
                    string translation = string.Empty;
                    if (toLang == Constants.ENGLISH)
                    {
                        ParserFunction func = ParserFunction.GetFunction(token, parentScript);
                        if (func != null)
                        {
                            translation = func.Name;
                        }
                    }
                    if (string.IsNullOrEmpty(translation) &&
                        !keywordsDict.TryGetValue(token, out translation) &&
                        !transDict.TryGetValue(token, out translation))
                    {
                        translation = token;//TryTranslit (fromLang, toLang, token);
                    }
                    result.Append(translation);
                    item.Clear();
                }
                result.Append(ch);
            }

            return result.ToString();
        }

        public static string TryFindError(string item, ParsingScript script)
        {
            string candidate = null;
            int minSize = Math.Max(2, item.Length - 1);

            for (int i = item.Length - 1; i >= minSize; i--)
            {
                candidate = item.Substring(0, i);
                if (s_nativeWords.Contains(candidate))
                {
                    return candidate + " " + Constants.START_ARG;
                }
                if (s_tempWords.Contains(candidate))
                {
                    return candidate;
                }
            }

            if (s_spellErrors.TryGetValue(item, out candidate))
            {
                return candidate;
            }

            return null;
        }
    }

}
