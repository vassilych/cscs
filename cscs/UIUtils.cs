using System;
using System.Collections.Generic;

namespace scripting
{
  public class UIUtils
  {
    public static string RemovePrefix(string text)
    {
      string candidate = text.Trim().ToLower();
      if (candidate.Length > 2 && candidate.StartsWith("l'",
                    StringComparison.OrdinalIgnoreCase)) {
        return candidate.Substring(2).Trim();
      }

      int firstSpace = candidate.IndexOf(' ');
      if (firstSpace <= 0) {
        return candidate;
      }

      string prefix = candidate.Substring(0, firstSpace);
      if (prefix.Length == 3 && candidate.Length > 4 &&
         (prefix == "der" || prefix == "die" || prefix == "das" ||
          prefix == "los" || prefix == "las" || prefix == "les")) {
        return candidate.Substring(firstSpace + 1);
      }
      if (prefix.Length == 2 && candidate.Length > 3 &&
         (prefix == "el" || prefix == "la" || prefix == "le" ||
          prefix == "il" || prefix == "lo")) {
        return candidate.Substring(firstSpace + 1);
      }
      return candidate;
    }

    static List<string> reserved = new List<string>() { "case", "catch", "class", "double", "else", "for",
      "if", "int", "internal", "long", "private", "public", "return", "short", "static", "switch", "try",
      "turkey", "while" };
    static public string String2ImageName(string name)
    {
      // Hooks for similar names:
      if (reserved.Contains(name)) {
        return "_" + name; // Only case difference with Turkey the country
      }
      string imagefileName = name.Replace("-", "_").Replace("(", "").
              Replace(")", "_").Replace("'", "_").
              Replace(" ", "_").Replace("é", "e").
              Replace("ñ", "n").Replace("í", "i").
              Replace(",", "" ).Replace("__", "_").
              Replace("\"", "").Replace(".png", "");
      return imagefileName;
    }
  }
}
