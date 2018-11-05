using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SplitAndMerge
{
    public partial class Utils
    {
        public static void CheckArgs(int args, int expected, string msg, bool exactMatch = false)
        {
            if (args < expected || (exactMatch && args != expected))
            {
                throw new ArgumentException("Expecting " + expected +
                    " arguments but got " + args + " in " + msg);
            }
        }
        public static void CheckPosInt(Variable variable)
        {
            CheckInteger(variable);
            if (variable.Value <= 0)
            {
                throw new ArgumentException("Expected a positive integer instead of [" +
                                               variable.Value + "]");
            }
        }
        public static void CheckPosInt(int number, string name)
        {
            if (number < 0)
            {
                throw new ArgumentException("Expected a positive integer instead of [" +
                                               number + "] in [" + name + "]");
            }
        }
        public static void CheckNonNegativeInt(Variable variable)
        {
            CheckInteger(variable);
            if (variable.Value < 0)
            {
                throw new ArgumentException("Expected a non-negative integer instead of [" +
                                               variable.Value + "]");
            }
        }
        public static void CheckInteger(Variable variable)
        {
            CheckNumber(variable);
            if (variable.Value % 1 != 0.0)
            {
                throw new ArgumentException("Expected an integer instead of [" +
                                               variable.Value + "]");
            }
        }
        public static void CheckNumber(Variable variable)
        {
            if (variable.Type != Variable.VarType.NUMBER)
            {
                throw new ArgumentException("Expected a number instead of [" +
                                               variable.AsString() + "]");
            }
        }
        public static void CheckArray(Variable variable, string name)
        {
            if (variable.Tuple == null)
            {
                throw new ArgumentException("An array expected for variable [" +
                                               name + "]");
            }
        }
        public static void CheckNotEmpty(ParsingScript script, string varName, string name)
        {
            if (!script.StillValid() || string.IsNullOrWhiteSpace(varName))
            {
                throw new ArgumentException("Incomplete arguments for [" + name + "]");
            }
        }
        public static void CheckNotEnd(ParsingScript script, string name)
        {
            if (!script.StillValid())
            {
                throw new ArgumentException("Incomplete arguments for [" + name + "]");
            }
        }
        public static void CheckNotNull(object obj, string name, int index = -1)
        {
            if (obj == null)
            {
                string indexStr = index >= 0 ? " in position " + (index + 1) : "";
                throw new ArgumentException("Invalid argument " + indexStr +
                                            " in function [" + name + "]");
            }
        }
        public static void CheckNotEnd(ParsingScript script)
        {
            if (!script.StillValid())
            {
                throw new ArgumentException("Incomplete function definition.");
            }
        }
        public static void CheckNotEmpty(string varName, string name)
        {
            if (string.IsNullOrEmpty(varName))
            {
                throw new ArgumentException("Incomplete arguments for [" + name + "]");
            }
        }
        public static void CheckNotNull(string name, ParserFunction func)
        {
            if (func == null)
            {
                throw new ArgumentException("Variable or function [" + name + "] doesn't exist");
            }
        }
 
        public static string GetLine(int chars = 40)
        {
            return string.Format("-").PadRight(chars, '-');
        }

        public static string GetFileText(string filename)
        {
            string fileContents = string.Empty;
            if (File.Exists(filename))
            {
                fileContents = File.ReadAllText(filename);
            }
            else
            {
                throw new ArgumentException("Couldn't read file [" + filename +
                                            "] from disk.");
            }
            return fileContents;
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
                    Interpreter.Instance.AppendOutput(token);
                    item.Clear();
                }
                Interpreter.Instance.AppendOutput(ch.ToString());
            }
        }

        public static string[] GetFileLines(string filename)
        {
            try
            {
                string[] lines = File.ReadAllLines(filename);
                return lines;
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Couldn't read file [" + filename +
                                            "] from disk: " + ex.Message);
            }
        }

        public static string[] GetFileLines(string filename, int from, int count)
        {
            try
            {
                var allLines = File.ReadLines(filename).ToArray();
                if (allLines.Length <= count)
                {
                    return allLines;
                }

                if (from < 0)
                {
                    // last n lines
                    from = allLines.Length - count;
                }

                string[] lines = allLines.Skip(from).Take(count).ToArray();
                return lines;
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Couldn't read file from disk: " + ex.Message);
            }
        }

        public static void WriteFileText(string filename, string text)
        {
            try
            {
                File.WriteAllText(filename, text);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Couldn't write file to disk: " + ex.Message);
            }
        }

        public static void AppendFileText(string filename, string text)
        {
            try
            {
                File.AppendAllText(filename, text);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Couldn't write file to disk: " + ex.Message);
            }
        }

        public static void ThrowException(ParsingScript script, string excName1,
                                          string errorToken = "", string excName2 = "")
        {
#if UNITY_EDITOR == false && UNITY_STANDALONE == false && __ANDROID__ == false && __IOS__ == false
            string msg = Translation.GetErrorString(excName1);
#else
            string msg = excName1;
#endif
            if (!string.IsNullOrWhiteSpace(errorToken))
            {
                msg = string.Format(msg, errorToken);
#if UNITY_EDITOR == false && UNITY_STANDALONE == false && __ANDROID__ == false && __IOS__ == false
                string candidate = Translation.TryFindError(errorToken, script);
#else
                string candidate = null;
#endif


                if (!string.IsNullOrWhiteSpace(candidate) &&
                    !string.IsNullOrWhiteSpace(excName2))
                {
#if UNITY_EDITOR == false && UNITY_STANDALONE == false && __ANDROID__ == false && __IOS__ == false
                    string extra = Translation.GetErrorString(excName2);
#else
                    string extra = excName2;
#endif
                    msg += " " + string.Format(extra, candidate);
                }
            }

            if (!string.IsNullOrWhiteSpace(script.Filename))
            {
#if UNITY_EDITOR == false && UNITY_STANDALONE == false && __ANDROID__ == false && __IOS__ == false
                string fileMsg = Translation.GetErrorString("errorFile");
#else
                string fileMsg = "File: {0}.";
#endif
                msg += Environment.NewLine + string.Format(fileMsg, script.Filename);
            }

            int lineNumber = -1;
            string line = script.GetOriginalLine(out lineNumber);
            if (lineNumber >= 0)
            {
#if UNITY_EDITOR == false && UNITY_STANDALONE == false && __ANDROID__ == false && __IOS__ == false
                string lineMsg = Translation.GetErrorString("errorLine");
#else
                string lineMsg = "Line {0}: [{1}]";
#endif
                msg += string.IsNullOrWhiteSpace(script.Filename) ? Environment.NewLine : " ";
                msg += string.Format(lineMsg, lineNumber + 1, line.Trim());
            }
            throw new ArgumentException(msg);
        }

        public static void PrintList(List<Variable> list, int from)
        {
            Console.Write("Merging list:");
            for (int i = from; i < list.Count; i++)
            {
                Console.Write(" ({0}, '{1}')", list[i].Value, list[i].Action);
            }
            Console.WriteLine();
        }

        public static int GetSafeInt(List<Variable> args, int index, int defaultValue = 0)
        {
            if (args.Count <= index)
            {
                return defaultValue;
            }
            Variable numberVar = args[index];
            if (numberVar.Type != Variable.VarType.NUMBER)
            {
                if (string.IsNullOrWhiteSpace(numberVar.String))
                {
                    return defaultValue;
                }
                int num;
                if (!Int32.TryParse(numberVar.String, NumberStyles.Number,
                                     CultureInfo.InvariantCulture, out num))
                {
                    throw new ArgumentException("Expected an integer instead of [" + numberVar.AsString() + "]");
                }
                return num;
            }
            return numberVar.AsInt();
        }
        public static double GetSafeDouble(List<Variable> args, int index, double defaultValue = 0.0)
        {
            if (args.Count <= index)
            {
                return defaultValue;
            }

            Variable numberVar = args[index];
            if (numberVar.Type != Variable.VarType.NUMBER)
            {
                double num;
                if (!Double.TryParse(numberVar.String, NumberStyles.Number |
                                   NumberStyles.AllowExponent |
                                   NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out num))
                {
                    throw new ArgumentException("Expected a double instead of [" + numberVar.AsString() + "]");
                }
                return num;
            }
            return numberVar.AsDouble();
        }
        public static string GetSafeString(List<Variable> args, int index, string defaultValue = "")
        {
            if (args.Count <= index)
            {
                return defaultValue;
            }
            return args[index].AsString();
        }
        public static Variable GetSafeVariable(List<Variable> args, int index, Variable defaultValue = null)
        {
            if (args.Count <= index)
            {
                return defaultValue;
            }
            return args[index];
        }

        public static Variable GetVariable(string varName, ParsingScript script)
        {
            ParserFunction func = ParserFunction.GetFunction(varName, script);
            Utils.CheckNotNull(varName, func);
            Variable varValue = func.GetValue(script);
            Utils.CheckNotNull(varValue, varName);
            return varValue;
        }

        public static double ConvertToDouble(object obj, string errorOrigin = "")
        {
            string str = obj.ToString();
            double num = 0;

            if (!Double.TryParse(str, NumberStyles.Number |
                                 NumberStyles.AllowExponent |
                                 NumberStyles.Float,
                                 CultureInfo.InvariantCulture, out num) &&
                !string.IsNullOrWhiteSpace(errorOrigin))
            {
                throw new ArgumentException("Couldn't parse [" + str + "] in " + errorOrigin);
            }
            return num;
        }
        public static bool ConvertToBool(object obj)
        {
            string str = obj.ToString();
            double dRes = 0;
            if (Double.TryParse(str, NumberStyles.Number | NumberStyles.AllowExponent,
                                CultureInfo.InvariantCulture, out dRes))
            {
                return dRes != 0;
            }
            bool res = false;

            Boolean.TryParse(str, out res);
            return res;
        }
        public static int ConvertToInt(object obj, string errorOrigin = "")
        {
            double num = ConvertToDouble(obj, errorOrigin);
            return (int)num;
        }
        public static void Extract(string data, ref string str1, ref string str2,
                                   ref string str3, ref string str4, ref string str5)
        {
            string[] vals = data.Split(new char[] { ',', ':' });
            str1 = vals[0];
            if (vals.Length > 1)
            {
                str2 = vals[1];
                if (vals.Length > 2)
                {
                    str3 = vals[2];
                    if (vals.Length > 3)
                    {
                        str4 = vals[3];
                        if (vals.Length > 4)
                        {
                            str5 = vals[4];
                        }
                    }
                }
            }
        }
        public static int GetNumberOfDigits(string data, int itemNumber = -1)
        {
            if (itemNumber >= 0)
            {
                string[] vals = data.Split(new char[] { ',', ':' });
                if (vals.Length <= itemNumber)
                {
                    return 0;
                }
                int min = 0;
                for (int i = 0; i < vals.Length; i++)
                {
                    min = Math.Max(min, GetNumberOfDigits(vals[i]));
                }
                return min;
            }

            int index = data.IndexOf(".");
            if (index < 0 || index >= data.Length - 1)
            {
                return 0;
            }
            return data.Length - index - 1;
        }
        public static void Extract(string data, ref double val1, ref double val2,
                                                ref double val3, ref double val4)
        {
            string[] vals = data.Split(new char[] { ',', ':' });
            val1 = ConvertToDouble(vals[0].Trim());

            if (vals.Length > 1)
            {
                val2 = ConvertToDouble(vals[1].Trim());
                if (vals.Length > 2)
                {
                    val3 = ConvertToDouble(vals[2].Trim());
                }
                if (vals.Length > 3)
                {
                    val4 = ConvertToDouble(vals[3].Trim());
                }
            }
            else
            {
                val3 = val2 = val1;
            }
        }
        public static string GetFileContents(string filename)
        {
            try
            {
                string[] readText = Utils.GetFileLines(filename);
                return string.Join("\n", readText);
            }
            catch (ArgumentException exc)
            {
                Console.WriteLine(exc.Message);
                return "";
            }
        }

        public static string RemovePrefix(string text)
        {
            string candidate = text.Trim().ToLower();
            if (candidate.Length > 2 && candidate.StartsWith("l'",
                          StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Substring(2).Trim();
            }

            int firstSpace = candidate.IndexOf(' ');
            if (firstSpace <= 0)
            {
                return candidate;
            }

            string prefix = candidate.Substring(0, firstSpace);
            if (prefix.Length == 3 && candidate.Length > 4 &&
               (prefix == "der" || prefix == "die" || prefix == "das" ||
                prefix == "los" || prefix == "las" || prefix == "les"))
            {
                return candidate.Substring(firstSpace + 1);
            }
            if (prefix.Length == 2 && candidate.Length > 3 &&
               (prefix == "el" || prefix == "la" || prefix == "le" ||
                prefix == "il" || prefix == "lo"))
            {
                return candidate.Substring(firstSpace + 1);
            }
            return candidate;
        }
    }
}
