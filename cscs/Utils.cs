﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace SplitAndMerge
{
    public partial class Utils
    {
        public static Func<string, string> GetFileContentsDelegate { get; set; }

        public static char[] SPACESEP = new char[] { ' ' };

        public static void CheckArgs(int args, int expected, string msg, bool exactMatch = false)
        {
            if (args < expected || (exactMatch && args != expected))
            {
                throw new ArgumentException("Expecting " + expected +
                    " arguments but got " + args + " in " + msg);
            }
        }
        public static void CheckPosInt(Variable variable, ParsingScript script = null)
        {
            CheckInteger(variable, script);
            if (variable.Value <= 0)
            {
                ThrowErrorMsg("Expected a positive integer instead of [" +
                              variable.Value + "].", script, script == null ? "" : script.Current.ToString());
            }
        }

        public static void CheckNonNegativeInt(Variable variable, ParsingScript script = null)
        {
            CheckInteger(variable, script);
            if (variable.Value < 0)
            {
                ThrowErrorMsg("Expected a non-negative integer instead of [" +
                              variable.Value + "].", script, script == null ? "" : script.Current.ToString());
            }
        }
        public static void CheckInteger(Variable variable, ParsingScript script = null)
        {
            CheckNumber(variable, script);
            if (variable.Value % 1 != 0.0)
            {
                ThrowErrorMsg("Expected an integer instead of [" +
                              variable.Value + "].", script, script == null ? "" : script.Current.ToString());
            }
        }
        public static void CheckNumber(Variable variable, ParsingScript script = null)
        {
            if (variable.Type != Variable.VarType.NUMBER)
            {
                ThrowErrorMsg("Expected a number instead of [" +
                              variable.AsString() + "].", script, script == null ? "" : script.Current.ToString());
            }
        }
        public static void CheckArray(Variable variable, string name)
        {
            if (variable.Tuple == null)
            {
                string realName = Constants.GetRealName(name);
                throw new ArgumentException("An array expected for variable [" +
                                               realName + "]");
            }
        }
        public static void CheckNotEmpty(ParsingScript script, string varName, string name)
        {
            if (!script.StillValid() || string.IsNullOrWhiteSpace(varName))
            {
                string realName = Constants.GetRealName(name);
                ThrowErrorMsg("Incomplete arguments for [" + realName + "].", script, name);
            }
        }

        public static void CheckNotNull(object obj, string name, ParsingScript script, int index = -1)
        {
            if (obj == null)
            {
                string indexStr = index >= 0 ? " in position " + (index + 1) : "";
                string realName = Constants.GetRealName(name);
                ThrowErrorMsg("Invalid argument " + indexStr +
                                            " in function [" + realName + "].", script, name);
            }
        }
        public static void CheckNotNull(string name, ParserFunction func, ParsingScript script)
        {
            if (func == null)
            {
                string realName = Constants.GetRealName(name);
                ThrowErrorMsg("Variable or function [" + realName + "] doesn't exist.", script, name);
            }
        }
        public static void CheckNotNull(object obj, string name, ParsingScript script)
        {
            if (obj == null)
            {
                string realName = Constants.GetRealName(name);
                ThrowErrorMsg("Object [" + realName + "] doesn't exist.", script, name);
            }
        }

        public static void CheckNotEnd(ParsingScript script, string name)
        {
            if (!script.StillValid())
            {
                string realName = Constants.GetRealName(name);
                ThrowErrorMsg("Incomplete arguments for [" + realName + "]", script, script.Prev.ToString());
            }
        }
        public static void CheckNotEnd(ParsingScript script)
        {
            if (!script.StillValid())
            {
                ThrowErrorMsg("Incomplete function definition.", script, script.Prev.ToString());
            }
        }

        public static void CheckNotEmpty(string varName, string name)
        {
            if (string.IsNullOrEmpty(varName))
            {
                string realName = Constants.GetRealName(name);
                ThrowErrorMsg("Incomplete arguments for [" + realName + "]", null, name);
            }
        }

        public static void CheckForValidName(string name, ParsingScript script)
        {
            if (string.IsNullOrWhiteSpace(name) || (!Char.IsLetter(name[0]) && name[0] != '_'))
            {
                ThrowErrorMsg("Illegal variable name: [" + name + "]",
                              script, name);
            }

            string illegals = "\"'?!";
            int first = name.IndexOfAny(illegals.ToCharArray());
            if (first >= 0)
            {
                var ind = name.IndexOf('[');
                if (ind < 0 || ind > first)
                {
                    for (int i = 0; i < illegals.Length; i++)
                    {
                        char ch = illegals[i];
                        if (name.Contains(ch))
                        {
                            ThrowErrorMsg("Variable [" + name + "] contains illegal character [" + ch + "]",
                                          script, name);
                        }
                    }
                }
            }
        }

        public static string TrimString1(string text, int maxSize = 15, bool addQoutes = false, bool addDots = true)
        {
            if (text.Length <= maxSize)
            {
                return text;
            }
            var parts = text.Split(SPACESEP, 2);
            var friendlyName = TrimString(parts.First(), maxSize, addQoutes, addDots);
            return friendlyName;
        }

        public static string TrimString(string text, int maxSize = 15, bool addQoutes = false, bool addDots = true)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            text = text.Split('\n')[0].Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }
            if (text.Length > maxSize)
            {
                text = text.Substring(0, maxSize - 3) + (addDots ? "..." : "");
            }
            if (addQoutes)
            {
                text = "\"" + text + "\"";
            }

            return text;
        }

        public static void ThrowErrorMsg(string msg, ParsingScript script, string token)
        {
            string code = script == null || string.IsNullOrWhiteSpace(script.OriginalScript) ? "" : script.OriginalScript;
            int lineNumber = script == null ? 0 : script.OriginalLineNumber;
            string filename = script == null || string.IsNullOrWhiteSpace(script.Filename) ? "" : script.Filename;
            int minLines = script == null || (!string.IsNullOrEmpty(token) &&
                script.OriginalLine.ToLower().Contains(token.ToLower())) ? 1 : 2;

            ThrowErrorMsg(msg, code, lineNumber, filename, minLines);
        }

        static void ThrowErrorMsg(string msg, string script, int lineNumber, string filename = "", int minLines = 1)
        {
            string[] lines = script.Split('\n');
            lineNumber = lines.Length <= lineNumber ? -1 : lineNumber;
            System.Diagnostics.Debug.WriteLine(msg);
            if (lineNumber < 0)
            {
                throw new ParsingException(msg);
            }

            var currentLineNumber = lineNumber;
            var line = lines[lineNumber].Trim();
            var collectMore = line.Length < 3 || minLines > 1;
            var lineContents = line;

            while (collectMore && currentLineNumber > 0)
            {
                line = lines[--currentLineNumber].Trim();
                collectMore = line.Length < 2 || (minLines > lineNumber - currentLineNumber + 1);
                lineContents = line + "  " + lineContents;
            }

            if (lines.Length > 1)
            {
                string lineStr = currentLineNumber == lineNumber ? "Line " + (lineNumber + 1) :
                                 "Lines " + (currentLineNumber + 1) + "-" + (lineNumber + 1);
                msg += " " + lineStr + ": " + lineContents;
            }

            StringBuilder stack = new StringBuilder();
            stack.AppendLine("" + currentLineNumber);
            stack.AppendLine(filename);
            stack.AppendLine(line);

            throw new ParsingException(msg, stack.ToString());
        }

        static void ThrowErrorMsg(string msg, string code, int level, int lineStart, int lineEnd, string filename)
        {
            var lineNumber = level > 0 ? lineStart : lineEnd;
            ThrowErrorMsg(msg, code, lineNumber, filename);
        }

        public static bool CheckLegalName(string name, ParsingScript script = null, bool throwError = true)
        {
            if (string.IsNullOrWhiteSpace(name) || Constants.CheckReserved(name))
            {
                if (!throwError)
                {
                    return false;
                }
                Utils.ThrowErrorMsg(name + " is a reserved name.", script, name);
            }
            if (Char.IsDigit(name[0]) || name[0] == '-')
            {
                if (!throwError)
                {
                    return false;
                }
                Utils.ThrowErrorMsg(name + " has illegal first character " + name[0], null, name);
            }

            return true;
        }

        public static ParsingScript GetTempScript(Interpreter interpreter, string str, ParserFunction.StackLevel stackLevel,
            string name = "",
            ParsingScript script = null, ParsingScript parentScript = null,
            int parentOffset = 0, CSCSClass.ClassInstance instance = null)
        {
            ParsingScript tempScript = new ParsingScript(interpreter, str);
            tempScript.ScriptOffset = parentOffset;
            if (parentScript != null)
            {
                tempScript.Char2Line = parentScript.Char2Line;
                tempScript.Filename = parentScript.Filename;
                tempScript.OriginalScript = parentScript.OriginalScript;
            }
            tempScript.ParentScript = script;
            tempScript.Context = script == null ? null : script.Context;
            tempScript.InTryBlock = script == null ? false : script.InTryBlock;
            tempScript.ClassInstance = instance;
            tempScript.StackLevel = stackLevel;

            return tempScript;
        }

        public static bool ExtractParameterNames(List<Variable> args, string functionName, ParsingScript script)
        {
            CustomFunction custFunc = script.InterpreterInstance.GetFunction(functionName) as CustomFunction;
            if (custFunc == null)
            {
                return false;
            }

            var realArgs = custFunc.RealArgs;
            for (int i = 0; i < args.Count && i < realArgs.Length; i++)
            {
                string name = args[i].CurrentAssign;
                args[i].ParamName = string.IsNullOrWhiteSpace(name) ? realArgs[i] : name;
            }
            return true;
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

        public static void PrintScript(string script, ParsingScript parentScript)
        {
            StringBuilder item = new StringBuilder();

            bool inQuotes = false;

            for (int i = 0; i < script.Length; i++)
            {
                char ch = script[i];
                inQuotes = ch == Constants.QUOTE ? !inQuotes : inQuotes;

                if (inQuotes)
                {
                    parentScript.InterpreterInstance.AppendOutput(ch.ToString());
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
                    parentScript.InterpreterInstance.AppendOutput(token);
                    item.Clear();
                }
                parentScript.InterpreterInstance.AppendOutput(ch.ToString());
            }
        }

        public static string[] GetFileLines(string filename)
        {
            try
            {
                if (GetFileContentsDelegate != null)
                {
                    var contents = GetFileContentsDelegate.Invoke(filename);
                    return contents.Split('\n');
                }

                string[] lines = File.ReadAllLines(filename);
                return lines;
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Couldn't read file [" + filename +
                                            "] from disk: " + ex.Message, ex);
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
                throw new ArgumentException("Couldn't read file from disk: " + ex.Message, ex);
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
                throw new ArgumentException("Couldn't write file to disk: " + ex.Message, ex);
            }
        }

        public static GetVarFunction ExtractArrayElement(Interpreter interpreter, string token)
        {
            if (!token.Contains(Constants.START_ARRAY))
            {
                return null;
            }

            ParsingScript tempScript = new ParsingScript(interpreter, token);
            Variable result = tempScript.Execute();
            return new GetVarFunction(result);
        }

        public static void AppendFileText(string filename, string text)
        {
            try
            {
                File.AppendAllText(filename, text);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Couldn't write file to disk: " + ex.Message, ex);
            }
        }

        public static void ThrowException(ParsingScript script, string excName1,
                                          string errorToken = "", string excName2 = "")
        {
#if UNITY_EDITOR == false && UNITY_STANDALONE == false && __ANDROID__ == false && __IOS__ == false
            string msg = script.InterpreterInstance.Translation.GetErrorString(excName1);
#else
            string msg = excName1;
#endif
            if (!string.IsNullOrWhiteSpace(errorToken))
            {
                msg = string.Format(msg, errorToken);
#if UNITY_EDITOR == false && UNITY_STANDALONE == false && __ANDROID__ == false && __IOS__ == false
                string candidate = script.InterpreterInstance.Translation.TryFindError(errorToken, script);
#else
                string candidate = null;
#endif


                if (!string.IsNullOrWhiteSpace(candidate) &&
                    !string.IsNullOrWhiteSpace(excName2))
                {
#if UNITY_EDITOR == false && UNITY_STANDALONE == false && __ANDROID__ == false && __IOS__ == false
                    string extra = script.InterpreterInstance.Translation.GetErrorString(excName2);
#else
                    string extra = excName2;
#endif
                    msg += " " + string.Format(extra, candidate);
                }
            }

            if (!string.IsNullOrWhiteSpace(script.Filename))
            {
#if UNITY_EDITOR == false && UNITY_STANDALONE == false && __ANDROID__ == false && __IOS__ == false
                string fileMsg = script.InterpreterInstance.Translation.GetErrorString("errorFile");
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
                string lineMsg = script.InterpreterInstance.Translation.GetErrorString("errorLine");
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
                if (!CanConvertToDouble(numberVar.String, out num))
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

        public static string GetSafeToken(List<Variable> args, int index, string defaultValue = "")
        {
            if (args.Count <= index)
            {
                return defaultValue;
            }

            Variable var = args[index];
            string token = var.ParsingToken;

            return token;
        }

        public static Variable GetVariable(string varName, ParsingScript script = null, bool testNull = true)
        {
            if (script == null)
            {
                script = new ParsingScript(script.InterpreterInstance, "");
            }
            varName = varName.ToLower();

            ParserFunction func = script.InterpreterInstance.GetVariable(varName, script);
            if (!testNull && func == null)
            {
                return null;
            }
            Utils.CheckNotNull(varName, func, script);
            Variable varValue = func.GetValue(script);
            Utils.CheckNotNull(varValue, varName, script);
            return varValue;
        }

        public static async Task<Variable> GetVariableAsync(string varName, ParsingScript script, bool testNull = true)
        {
            ParserFunction func = script.InterpreterInstance.GetVariable(varName, script);
            if (!testNull && func == null)
            {
                return null;
            }
            Utils.CheckNotNull(varName, func, script);
            Variable varValue = await func.GetValueAsync(script);
            Utils.CheckNotNull(varValue, varName, script);
            return varValue;
        }

        public static double ConvertToDouble(object obj, ParsingScript script = null)
        {
            string str = obj.ToString().ToLower();
            double num = 0;

            if (!CanConvertToDouble(str, out num) &&
                script != null)
            {
                ProcessErrorMsg(str, script);
            }
            return num;
        }

        public static bool CanConvertToDouble(string str, out double num)
        {
            if (str == "true")
            {
                num = 1.0;
                return true;
            }
            if (str == "false")
            {
                num = 0.0;
                return true;
            }
            return Double.TryParse(str, NumberStyles.Number |
                                        NumberStyles.AllowExponent |
                                        NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out num);
        }

        public static void ProcessErrorMsg(string str, ParsingScript script)
        {
            char ch = script.TryPrev();
            string entity = ch == '(' ? "function" :
                            ch == '[' ? "array" :
                            ch == '{' ? "operand" :
                                        "variable";
            string token = Constants.GetRealName(str);

            string msg = "Couldn't find " + entity + " [" + token + "].";

            ThrowErrorMsg(msg, script, str);
        }

        public static bool ConvertToBool(object obj)
        {
            string str = obj.ToString();
            double dRes = 0;
            if (CanConvertToDouble(str, out dRes))
            {
                return dRes != 0;
            }
            bool res = false;

            Boolean.TryParse(str, out res);
            return res;
        }
        public static int ConvertToInt(object obj, ParsingScript script = null)
        {
            double num = ConvertToDouble(obj, script);
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
            if (string.IsNullOrWhiteSpace(filename))
            {
                return "";
            }
            try
            {
                string[] readText = Utils.GetFileLines(filename);
                return string.Join("\n", readText);
            }
            catch (Exception exc)
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

        public static string GetFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            {
                return path;
            }
            try
            {
                path = Path.GetFullPath(path);
            }
            catch (Exception exc)
            {
                Console.WriteLine("Exception converting path {0}: {1}", path, exc.Message);
            }
            return path;
        }

        public static string GetDirectoryName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return GetCurrentDirectory();
            }
            try
            {
                return Path.GetDirectoryName(path);
            }
            catch (Exception exc)
            {
                Console.WriteLine("Exception getting directory name {0}: {1}", path, exc.Message);
            }
            return GetCurrentDirectory();
        }

        public static string GetCurrentDirectory()
        {
            try
            {
                return Directory.GetCurrentDirectory();
            }
            catch (Exception exc)
            {
                Console.WriteLine("Exception getting current directory: {0}", exc.Message);
            }
            return "";
        }

        public static void ExtendArrayIfNeeded<T>(List<T> array, int count, T defaultValue)
        {
            if (array.Count <= count)
            {
                array.Capacity = count + 1;
                while (array.Count <= count)
                {
                    array.Add(defaultValue);
                }
            }
        }

        static public byte[] TruncateArray(byte[] array, int bytesReceived = 0)
        {
            int i = array.Length - 1;
            while (i >= 0 && array[i] == 0 && i >= bytesReceived)
            {
                --i;
            }
            byte[] newArray = new byte[i + 1];
            Array.Copy(array, newArray, i + 1);
            return newArray;
        }


        static string pass = "mi_900.";
        static string keySalt = "poo12.";
        static string ivSalt = "puu14T";

        static public string EncryptString(string plainText, string password = "")
        {
            if (password == "plain")
            {
                return plainText;
            }
            if (string.IsNullOrEmpty(plainText))
            {
                return "";
            }
            var byteArray =  EncryptStringToBytes(plainText, password);
            var encoded = Convert.ToBase64String(byteArray);
            return encoded;
        }

        static public string DecryptString(string encrypted, string password = "")
        {
            if (password == "plain")
            {
                return encrypted;
            }
            if (string.IsNullOrWhiteSpace(encrypted) || encrypted.Length <= 8 ||
                encrypted.Contains(" "))
            {
                return encrypted;
            }
            try
            {
                var byteArray = Convert.FromBase64String(encrypted);
                string decrypted = encrypted;

                var t = Task.Run(() => {
                    decrypted = DecryptStringFromBytes(byteArray, password);
                });
                //await Task.WhenAny();
                bool completed = t.Wait(4000);

                return decrypted;
            }
            catch (Exception exc)
            {
                return encrypted;
            }
        }

        public static bool CheckIfDecrypted(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }
            for (int i = 0; i < text.Length; i++)
            {
                var ch = (int)text[i];
                if (ch == 65533)
                {
                    return false;
                }
            }
            return true;
        }

        static public byte[] EncryptStringToBytes(string plainText, string password = "")
        {
            string keyStr = keySalt + pass + password;
            string saltStr = ivSalt + pass + password;

            byte[] key = SHA256.Create().ComputeHash(Encoding.Unicode.GetBytes(keyStr));
            byte[] iv = SHA256.Create().ComputeHash(Encoding.Unicode.GetBytes(saltStr)).Take(16).ToArray();

            return EncryptStringToBytes(plainText, key, iv);
        }


        static public string DecryptStringFromBytes(byte[] cipherText, string password = "")
        {
            string keyStr = keySalt + pass + password;
            string saltStr = ivSalt + pass + password;

            byte[] key = SHA256.Create().ComputeHash(Encoding.Unicode.GetBytes(keyStr));
            byte[] iv = SHA256.Create().ComputeHash(Encoding.Unicode.GetBytes(saltStr)).Take(16).ToArray();

            var result = DecryptStringFromBytes(cipherText, key, iv);
            return result.Trim('\0');
        }

        static public byte[] EncryptStringToBytes(string plainText, byte[] Key, byte[] IV)
        {
            // Check arguments.
            if (plainText == null || plainText.Length <= 0)
                throw new ArgumentNullException("plainText");
            if (Key == null || Key.Length <= 0)
                throw new ArgumentNullException("Key");
            if (IV == null || IV.Length <= 0)
                throw new ArgumentNullException("IV");
            byte[] encrypted;
            // Create an RijndaelManaged object
            // with the specified key and IV.
            using (RijndaelManaged rijAlg = new RijndaelManaged())
            {
                rijAlg.Key = Key;
                rijAlg.IV = IV;
                rijAlg.Padding = PaddingMode.Zeros;

                // Create an encryptor to perform the stream transform.
                ICryptoTransform encryptor = rijAlg.CreateEncryptor(rijAlg.Key, rijAlg.IV);

                // Create the streams used for encryption.
                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                        {

                            //Write all data to the stream.
                            swEncrypt.Write(plainText);
                        }
                        encrypted = msEncrypt.ToArray();
                    }
                }
            }

            // Return the encrypted bytes from the memory stream.
            return encrypted;

        }

        static public string DecryptStringFromBytes(byte[] cipherText, byte[] Key, byte[] IV)
        {
            // Check arguments.
            if (cipherText == null || cipherText.Length <= 0)
                throw new ArgumentNullException("cipherText");
            if (Key == null || Key.Length <= 0)
                throw new ArgumentNullException("Key");
            if (IV == null || IV.Length <= 0)
                throw new ArgumentNullException("IV");

            // Declare the string used to hold
            // the decrypted text.
            string plaintext = null;
            try
            {
                // Create an RijndaelManaged object
                // with the specified key and IV.
                using (RijndaelManaged rijAlg = new RijndaelManaged())
                {
                    rijAlg.Key = Key;
                    rijAlg.IV = IV;
                    rijAlg.Padding = PaddingMode.Zeros;

                    // Create a decryptor to perform the stream transform.
                    ICryptoTransform decryptor = rijAlg.CreateDecryptor(rijAlg.Key, rijAlg.IV);

                    // Create the streams used for decryption.
                    using (MemoryStream msDecrypt = new MemoryStream(cipherText))
                    {
                        using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                        {
                            using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                            {
                                // Read the decrypted bytes from the decrypting stream
                                // and place them in a string.
                                plaintext = srDecrypt.ReadToEnd();
                            }
                        }
                    }

                }
            }
            catch (Exception exc)
            {
                plaintext = Encoding.Unicode.GetString(cipherText, 0, Math.Min(cipherText.Length, 255)).Trim();
            }

            return plaintext.Trim();
        }
    }
}
