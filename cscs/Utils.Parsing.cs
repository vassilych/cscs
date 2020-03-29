using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SplitAndMerge
{
    public partial class Utils
    {
        public static Variable GetItem(ParsingScript script, bool eatLast = true)
        {
            script.MoveForwardIf(Constants.NEXT_ARG, Constants.SPACE);
            Utils.CheckNotEnd(script);

            bool inQuotes = script.Current == Constants.QUOTE;
            bool inQuotes1 = script.Current == Constants.QUOTE1;

            bool isList = script.Current == Constants.START_GROUP || script.Current == Constants.START_ARRAY;
            if (isList)
            {
                return ProcessArrayMap(script);
            }

            var sep = script.ProcessingList ? Constants.NEXT_OR_END_ARRAY_EXT : Constants.NEXT_OR_END_ARRAY;
            // A variable, a function, or a number.
            Variable var = script.Execute(sep);
            //value = var.Clone();

            if (inQuotes)
            {
                script.MoveForwardIf(Constants.QUOTE);
            }
            else if (inQuotes1)
            {
                script.MoveForwardIf(Constants.QUOTE1);
            }
            if (eatLast)
            {
                script.MoveForwardIf(Constants.END_ARG, Constants.SPACE);
            }
            return var;
        }

        public static async Task<Variable> GetItemAsync(ParsingScript script, bool eatLast = true)
        {
            script.MoveForwardIf(Constants.NEXT_ARG, Constants.SPACE);
            Utils.CheckNotEnd(script);

            bool inQuotes  = script.Current == Constants.QUOTE;
            bool inQuotes1 = script.Current == Constants.QUOTE1;

            bool isList = script.Current == Constants.START_GROUP || script.Current == Constants.START_ARRAY;
            if (isList)
            {
                return ProcessArrayMap(script);
            }

            var sep = script.ProcessingList ? Constants.NEXT_OR_END_ARRAY_EXT : Constants.NEXT_OR_END_ARRAY;
            // A variable, a function, or a number.
            Variable var = await script.ExecuteAsync(sep);
            //value = var.Clone();

            if (inQuotes)
            {
                script.MoveForwardIf(Constants.QUOTE);
            }
            else if (inQuotes1)
            {
                script.MoveForwardIf(Constants.QUOTE1);
            }
            if (eatLast)
            {
                script.MoveForwardIf(Constants.END_ARG, Constants.SPACE);
            }
            return var;
        }

        public static Variable ProcessArrayMap(ParsingScript script)
        {
            bool isList = true;
            char start = script.Current;
            char end = script.Current == Constants.START_GROUP ?
                Constants.END_GROUP : Constants.END_ARRAY;
            script.Forward(); // Skip the first brace.
            Variable value = new Variable();
            var processingListBefore = script.ProcessingList;
            script.ProcessingList = true;// script.ProcessingList || script.Current == Constants.START_ARRAY;
            try
            {
                value.Tuple = GetArgs(script, start, end, (outList) => { isList = outList; });
            }
            finally
            {
                script.ProcessingList = processingListBefore;
            }
            return value;
        }

        public static string GetToken(ParsingScript script, char[] to, bool eatLast = false)

        {
            char curr = script.TryCurrent();
            char prev = script.TryPrev();

            if (!to.Contains(Constants.SPACE))
            {
                // Skip a leading space unless we are inside of quotes
                while (curr == Constants.SPACE && prev != Constants.QUOTE && prev != Constants.QUOTE1)
                {
                    script.Forward();
                    curr = script.TryCurrent();
                    prev = script.TryPrev();
                }
            }

            // String in quotes
            bool inQuotes = curr == Constants.QUOTE || curr == Constants.QUOTE1;
            if (inQuotes)
            {
                int qend = script.Find(curr, script.Pointer + 1);
                if (qend == -1)
                {
                    throw new ArgumentException("Unmatched quotes in [" +
                                           script.FromPrev() + "]");
                }
                string result = script.Substr(script.Pointer + 1, qend - script.Pointer - 1);
                script.Pointer = qend + 1;
                return result;
            }

            script.MoveForwardIf(Constants.QUOTE, Constants.QUOTE1);

            int end = script.FindFirstOf(to);
            end = end < 0 ? script.Size() : end;

            // Skip found characters that have a backslash before.
            while (end > 0 && end + 1 < script.Size() &&
                   script.String[end - 1] == '\\')
            {
                end = script.FindFirstOf(to, end + 1);
            }

            end = end < 0 ? script.Size() : end;

            if (script.At(end - 1) == Constants.QUOTE)
            {
                end--;
            }

            string var = script.Substr(script.Pointer, end - script.Pointer);
            // \"yes\" --> "yes"
            var = var.Replace("\\\"", "\"");
            script.Pointer = end;

            script.MoveForwardIf(Constants.QUOTE, Constants.SPACE);

            if (eatLast && !script.MoveForwardIf(Constants.END_ARG, Constants.SPACE))
            {
                script.MoveForwardIf(Constants.NEXT_ARG);
            }

            return var;
        }

        public static string GetNextToken(ParsingScript script, bool eatLast = false)
        {
            if (!script.StillValid())
            {
                return "";
            }
            int end = script.FindFirstOf(Constants.TOKEN_SEPARATION);

            if (end < 0)
            {
                return "";
            }

            string var = script.Substr(script.Pointer, end - script.Pointer);
            script.Pointer = end;
            if (eatLast)
            {
                script.Forward(1);
            }
            return var;
        }

        public static CustomFunction GetFunction(ParsingScript script, string funcName, string token)
        {
            CustomFunction customFunc = null;
            if (token == Constants.FUNCTION && script.Prev == '(')
            {
                string[] args = Utils.GetFunctionSignature(script);
                script.MoveForwardIf('{');
                string body = Utils.GetBodyBetween(script, '{', '}');
                script.MoveForwardIf('}');

                int parentOffset = script.Pointer +
                    (script.CurrentClass != null ? script.CurrentClass.ParentOffset : 0);
                customFunc = new CustomFunction(funcName, body, args, script);
                customFunc.ParentScript = script;
                customFunc.ParentOffset = parentOffset;

            }
            return customFunc;
        }

        static void SetPropertyFromStr(string token, Variable result, ParsingScript script,
            string funcName, CustomFunction customFunc)
        {
            if (string.IsNullOrWhiteSpace(token) || (token[0] == '"' && token[token.Length-1] != '"'))
            {
                Utils.ThrowErrorMsg("Cannot mix value and set/get", script, funcName);
            }
            if (customFunc != null)
            {
                if (funcName == "set")
                {
                    result.CustomFunctionSet = customFunc;
                }
                else
                {
                    result.CustomFunctionGet = customFunc;
                }
                return;
            }
            if (token[0] == '"')
            {
                result.String = token.Substring(1, token.Length - 2);
            }
            else if (CanConvertToDouble(token.ToLower(), out double num))
            {
                result.Value = num;
            }
            else
            {
                if (funcName == "set")
                {
                    result.CustomSet = token;
                }
                else
                {
                    result.CustomGet = token;
                }
            }
        }
        public static Variable GetProperties(ParsingScript script)
        {
            Variable result = new Variable();
            bool valueProvided = false;
            bool setgetProvided = false;
            bool writable = false;
            bool configurable = false;
            bool enumerable = false;
            script.MoveForwardIf('{');
            while (script.StillValid() && script.Current != '}')
            {
                var funcName = Utils.GetNextToken(script, true);
                if (string.IsNullOrWhiteSpace(funcName))
                {
                    break;
                }
                var token = Utils.GetNextToken(script, true);

                CustomFunction customFunc = GetFunction(script, funcName, token);
                script.MoveForwardIf(',');
                string lower = funcName.ToLower();
                switch (lower)
                {
                    case "value":
                        if (setgetProvided)
                        {
                            Utils.ThrowErrorMsg("Cannot mix value and set/get", script, funcName);
                        }
                        valueProvided = true;
                        SetPropertyFromStr(token, result, script, lower, customFunc);
                        break;
                    case "set":
                        if (valueProvided)
                        {
                            Utils.ThrowErrorMsg("Cannot mix value and set/get", script, funcName);
                        }
                        setgetProvided = true;
                        SetPropertyFromStr(token, result, script, lower, customFunc);
                        break;
                    case "get":
                        if (valueProvided)
                        {
                            Utils.ThrowErrorMsg("Cannot mix value and set/get", script, funcName);
                        }
                        setgetProvided = true;
                        SetPropertyFromStr(token, result, script, lower, customFunc);
                        break;
                    case "writable":
                        writable = ConvertToDouble(token.ToLower(), script) > 0;
                        break;
                    case "enumerable":
                        enumerable = ConvertToDouble(token.ToLower(), script) > 0;
                        break;
                    case "configurable":
                        configurable = ConvertToDouble(token.ToLower(), script) > 0;
                        break;
                }
            }
            if (result.Type == Variable.VarType.NONE)
            {
                result.Type = Variable.VarType.CUSTOM;
            }
            result.Writable = writable;
            result.Enumerable = enumerable;
            result.Configurable = configurable;

            return result;
        }

        public static void SkipRestExpr(ParsingScript script, char toChar = Constants.END_STATEMENT)
        {
            int argRead = 0;
            bool inQuotes  = false;
            bool inQuotes1 = false;
            bool inQuotes2 = false;
            char prev = Constants.EMPTY;
            char prevprev = Constants.EMPTY;

            while (script.StillValid())
            {
                char currentChar = script.Current;
                if (inQuotes && currentChar != Constants.QUOTE)
                {
                    script.Forward();
                    continue;
                }
                if (currentChar == toChar)
                {
                    return;
                }

                switch (currentChar)
                {
                    case Constants.QUOTE1:
                        if (!inQuotes2 && (prev != '\\' || prevprev == '\\'))
                        {
                            inQuotes = inQuotes1 = !inQuotes1;
                        }
                        break;
                    case Constants.QUOTE:
                        if (!inQuotes1 && (prev != '\\' || prevprev == '\\'))
                        {
                            inQuotes = inQuotes2 = !inQuotes2;
                        }
                        break;
                    case Constants.START_ARG:
                        argRead++;
                        break;
                    case Constants.END_ARG:
                        argRead--;
                        if (argRead < 0)
                        {
                            return;
                        }
                        break;
                    case Constants.END_STATEMENT:
                        return;
                    case Constants.TERNARY_OPERATOR:
                    case Constants.NEXT_ARG:
                        if (argRead <= 0)
                        {
                            return;
                        }
                        break;
                    default:
                        break;
                }

                script.Forward();
                prevprev = prev;
                prev = currentChar;
            }
        }

        public static bool IsCompareSign(char ch)
        {
            return ch == '<' || ch == '>' || ch == '=';
        }

        public static bool IsAndOrSign(char ch)
        {
            return ch == '&' || ch == '|';
        }

        // Checks whether there is an argument separator (e.g.  ',') before the end of the
        // function call. E.g. returns true for "a,b)" and "a(b,c),d)" and false for "b),c".
        public static bool SeparatorExists(ParsingScript script)
        {
            if (!script.StillValid())
            {
                return false;
            }

            int argumentList = 0;
            for (int i = script.Pointer; i < script.Size(); i++)
            {
                char ch = script.At(i);
                switch (ch)
                {
                    case Constants.NEXT_ARG:
                        return true;
                    case Constants.START_ARG:
                        argumentList++;
                        break;
                    case Constants.END_STATEMENT:
                    case Constants.END_GROUP:
                    case Constants.END_ARG:
                        if (--argumentList < 0)
                        {
                            return false;
                        }
                        break;
                }
            }

            return false;
        }

        public static void GetCompiledArgs(ParsingScript script, out string funcReturn, out string funcName)
        {
            string body = Utils.GetBodyBetween(script, Constants.END_ARG, Constants.START_ARG);
            var parts = body.Split();
            funcReturn = parts.Length > 1 ? parts[0] : "void";
            funcName = parts.Last();
        }

        public static List<string> GetFunctionArgs(ParsingScript script)
        {
            bool isList;
            List<Variable> args = Utils.GetArgs(script,
                Constants.START_ARG, Constants.END_ARG, (outList) => { isList = outList; });

            List<string> result = new List<string>();
            for (int i = 0; i < args.Count; i++)
            {
                result.Add(args[i].AsString());
            }
            return result;
        }
        public static async Task<List<string>> GetFunctionArgsAsync(ParsingScript script)
        {
            bool isList;
            List<Variable> args = await Utils.GetArgsAsync(script,
                Constants.START_ARG, Constants.END_ARG, (outList) => { isList = outList; });

            List<string> result = new List<string>();
            for (int i = 0; i < args.Count; i++)
            {
                result.Add(args[i].AsString());
            }
            return result;
        }

        public static List<Variable> GetArgs(ParsingScript script,
            char start, char end, Action<bool> outList)
        {
            List<Variable> args = new List<Variable>();
            bool isList = script.StillValid() && script.Current == Constants.START_GROUP;

            if (!script.StillValid() || script.Current == Constants.END_STATEMENT)
            {
                return args;
            }

            ParsingScript tempScript = script.GetTempScript(script.String, script.Pointer);

            if (script.Current != start && script.TryPrev() != start &&
               (script.Current == ' ' || script.TryPrev() == ' '))
            { // Allow functions with space separated arguments
                start = ' ';
                end = Constants.END_STATEMENT;
            }

            // ScriptingEngine - body is unsed (used in Debugging) but GetBodyBetween has sideeffects			
#pragma warning disable 219
            string body = Utils.GetBodyBetween(tempScript, start, end);
#pragma warning restore 219
            // After the statement above tempScript.Parent will point to the last
            // character belonging to the body between start and end characters. 

            while (script.Pointer < tempScript.Pointer)
            {
                Variable item = Utils.GetItem(script, false);
                args.Add(item);
                if (script.Pointer < tempScript.Pointer)
                {
                    script.MoveForwardIf(Constants.END_GROUP);
                    script.MoveForwardIf(Constants.NEXT_ARG);
                }
                if (script.Pointer == tempScript.Pointer - 1)
                {
                    script.MoveForwardIf(Constants.END_ARG, Constants.END_GROUP);
                }
            }

            if (script.Pointer <= tempScript.Pointer)
            {
                // Eat closing parenthesis, if there is one, but only if it closes
                // the current argument list, not one after it. 
                script.MoveForwardIf(Constants.END_ARG, end);
            }

            script.MoveForwardIf(Constants.SPACE);
            //script.MoveForwardIf(Constants.SPACE, Constants.END_STATEMENT);
            outList(isList);
            return args;
        }

        public static async Task<List<Variable>> GetArgsAsync(ParsingScript script,
            char start, char end, Action<bool> outList)
        {
            List<Variable> args = new List<Variable>();
            bool isList = script.StillValid() && script.Current == Constants.START_GROUP;

            if (!script.StillValid() || script.Current == Constants.END_STATEMENT)
            {
                return args;
            }

            ParsingScript tempScript = script.GetTempScript(script.String, script.Pointer);

            if (script.Current != start && script.TryPrev() != start &&
               (script.Current == ' ' || script.TryPrev() == ' '))
            { // Allow functions with space separated arguments
                start = ' ';
                end = Constants.END_STATEMENT;
            }

            // ScriptingEngine - body is unsed (used in Debugging) but GetBodyBetween has sideeffects			
#pragma warning disable 219
            string body = Utils.GetBodyBetween(tempScript, start, end);
#pragma warning restore 219
            // After the statement above tempScript.Parent will point to the last
            // character belonging to the body between start and end characters. 

            while (script.Pointer < tempScript.Pointer)
            {
                Variable item = await Utils.GetItemAsync(script, false);
                args.Add(item);
                if (script.Pointer < tempScript.Pointer)
                {
                    script.MoveForwardIf(Constants.END_GROUP);
                    script.MoveForwardIf(Constants.NEXT_ARG);
                }
                if (script.Pointer == tempScript.Pointer - 1)
                {
                    script.MoveForwardIf(Constants.END_ARG, Constants.END_GROUP);
                }
            }

            if (script.Pointer <= tempScript.Pointer)
            {
                // Eat closing parenthesis, if there is one, but only if it closes
                // the current argument list, not one after it. 
                script.MoveForwardIf(Constants.END_ARG, end);
            }

            script.MoveForwardIf(Constants.SPACE);
            //script.MoveForwardIf(Constants.SPACE, Constants.END_STATEMENT);
            outList(isList);
            return args;
        }

        public static List<Variable> GetFunctionArgsAsStrings(ParsingScript script)
        {
            string[] signature = GetFunctionSignature(script);
            List<Variable> args = new List<Variable>(signature.Length);
            for (int i = 0; i < signature.Length; i++)
            {
                args.Add(new Variable(signature[i]));
            }

            return args;
        }
    
        public static string[] GetFunctionSignature(ParsingScript script)
        {
            script.MoveForwardIf(Constants.START_ARG, Constants.SPACE);

            int endArgs = script.FindFirstOf(Constants.END_ARG.ToString());
            if (endArgs < 0)
            {
                endArgs = script.FindFirstOf(Constants.END_STATEMENT.ToString());
            }

            if (endArgs < 0)
            {
                throw new ArgumentException("Couldn't extract function signature");
            }

            string argStr = script.Substr(script.Pointer, endArgs - script.Pointer);
            StringBuilder collect = new StringBuilder(argStr.Length);
            List<string> args = new List<string>();
            int curlyLevel = 0;
            for (int i = 0; i < argStr.Length; i++)
            {
                if (argStr[i] == '{')
                {
                    curlyLevel++;
                }
                else if (argStr[i] == '}')
                {
                    curlyLevel--;
                    if (curlyLevel < 0)
                    {
                        break;
                    }
                }
                else if (argStr[i] == Constants.NEXT_ARG && curlyLevel == 0)
                {
                    string item = collect.ToString().Trim();
                    if (item.Length == 0)
                    {
                        throw new ArgumentException("Empty argument in function signature [" + argStr + "]");
                    }
                    args.Add(item);
                    collect.Clear();
                    continue;
                }
                collect.Append(argStr[i]);
            }

            if (curlyLevel != 0)
            {
                throw new ArgumentException("Unbalanced curly braces in function signature [" + argStr + "]");
            }
            if (collect.Length > 0)
            {
                args.Add(collect.ToString().Trim());
            }

            script.Pointer = endArgs + 1;

            return args.ToArray();
        }

        public static Variable GetVariableFromString(string str, ParsingScript script, int startIndex = 0)
        {
            ParsingScript tempScript = script.GetTempScript(str, startIndex);
            Variable result          = Utils.GetItem(tempScript);
            return result;
        }

        public static string[] GetBaseClasses(ParsingScript script)
        {
            if (script.Current != ':')
            {
                return new string[0];
            }
            script.Forward();

            int endArgs = script.FindFirstOf(Constants.START_GROUP.ToString());
            if (endArgs < 0)
            {
                throw new ArgumentException("Couldn't extract base classes");
            }

            string argStr = script.Substr(script.Pointer, endArgs - script.Pointer);
            string[] args = argStr.Split(Constants.NEXT_ARG_ARRAY, StringSplitOptions.RemoveEmptyEntries);

            args = args.Select(element => Constants.ConvertName(element.Trim())).ToArray();
            script.Pointer = endArgs + 1;

            return args;
        }

        public static string[] GetCompiledFunctionSignature(ParsingScript script, out Dictionary<string, Variable> dict)
        {
            script.MoveForwardIf(Constants.START_ARG, Constants.SPACE);

            int endArgs = script.FindFirstOf(Constants.END_ARG.ToString());
            if (endArgs < 0)
            {
                throw new ArgumentException("Couldn't extract function signature");
            }

            string argStr = script.Substr(script.Pointer, endArgs - script.Pointer);
            List<string> args = GetCompiledArgs(argStr);
            //string[] args = argStr.Split(Constants.NEXT_ARG_ARRAY, StringSplitOptions.RemoveEmptyEntries);

            dict = new Dictionary<string, Variable>(args.Count);
            var sep = new char[] { ' ' };
            for (int i = 0; i < args.Count; i++)
            {
                string[] pair = args[i].ToLower().Trim().Split(sep, StringSplitOptions.RemoveEmptyEntries);
                Variable.VarType type = pair.Length > 1 ? Constants.StringToType(pair[0]) : Variable.VarType.STRING;
                dict.Add(pair[pair.Length - 1], new Variable(type));
                args[i] = pair[pair.Length - 1];
            }

            string[] result = args.Select(element => element.Trim()).ToArray();
            script.Pointer = endArgs + 1;

            return result;
        }

        public static bool EndsWithFunction(string buffer, List<string> functions)
        {
            foreach (string key in functions)
            {
                if (buffer.EndsWith(key, StringComparison.OrdinalIgnoreCase))
                {
                    char prev = key.Length >= buffer.Length ?
                        Constants.END_STATEMENT :
                        buffer[buffer.Length - key.Length - 1];
                    if (Constants.TOKEN_SEPARATION.Contains(prev))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool SpaceNotNeeded(char next)
        {
            return (next == Constants.SPACE || next == Constants.START_ARG ||
                    next == Constants.START_GROUP || next == Constants.START_ARRAY ||
                    next == Constants.EMPTY);
        }

        public static bool KeepSpace(StringBuilder sb, char next)
        {
            if (SpaceNotNeeded(next))
            {
                return false;
            }

            return EndsWithFunction(sb.ToString(), Constants.FUNCT_WITH_SPACE);
        }
        public static bool KeepSpaceOnce(StringBuilder sb, char next)
        {
            if (SpaceNotNeeded(next))
            {
                return false;
            }

            string str = sb.ToString();
            char last = str.Length < 1 ? Constants.EMPTY : str.Last();
            if (char.IsLetterOrDigit(last) && char.IsLetterOrDigit(next))
            {
                return true;
            }
            return EndsWithFunction(str, Constants.FUNCT_WITH_SPACE_ONCE);
        }

        public static List<string> GetCompiledArgs(string source)
        {
            StringBuilder sb = new StringBuilder(source.Length);
            List<string> args = new List<string>();

            bool inQuotes = false;
            char prev = Constants.EMPTY;
            char prevprev = Constants.EMPTY;
            int angleBrackets = 0;

            for (int i = 0; i < source.Length; i++)
            {
                char ch = source[i];
                switch (ch)
                {
                    case '“':
                    case '”':
                    case '„':
                    case '"':
                        ch = '"';
                        if (prev != '\\' || prevprev == '\\')
                        {
                            inQuotes = !inQuotes;
                        }
                        break;
                    case '<':
                        if (!inQuotes) angleBrackets++;
                        break;
                    case '>':
                        if (!inQuotes) angleBrackets--;
                        break;
                    case ',':
                        if (inQuotes || angleBrackets > 0)
                        {
                            break;
                        }
                        args.Add(sb.ToString());
                        sb.Clear();
                        prevprev = prev;
                        prev = ch;
                        continue;
                }
                sb.Append(ch);
                prevprev = prev;
                prev = ch;
            }
            if (sb.Length > 0)
            {
                args.Add(sb.ToString());
            }
            return args;
        }

        public static string ConvertToScript(string source, out Dictionary<int, int> char2Line, string filename = "")
        {
            string curlyErrorMsg   = "Unbalanced curly braces.";
            string bracketErrorMsg = "Unbalanced square brackets.";
            string parenthErrorMsg = "Unbalanced parentheses.";
            string quoteErrorMsg   = "Unbalanced quotes.";

            StringBuilder sb = new StringBuilder(source.Length);
            char2Line = new Dictionary<int, int>();

            bool inQuotes  = false;
            bool inQuotes1 = false;
            bool inQuotes2 = false;
            bool spaceOK = false;
            bool inComments = false;
            bool simpleComments = false;
            char prev = Constants.EMPTY;
            char prevprev = Constants.EMPTY;

            int levelCurly = 0;
            int levelBrackets = 0;
            int levelParentheses = 0;
            int lineNumber = 0;
            int lineNumberCurly = 0;
            int lineNumberBrack = 0;
            int lineNumberPar = 0;
            int lineNumberQuote = 0;

            int lastScriptLength = 0;

            bool precompiledPart = false;
            int  precompiledCounter = 0;
            StringBuilder lastToken = new StringBuilder();

            // Remove these two lines for quality time debugging in case the user has special
            // spaces with code 160. See https://en.wikipedia.org/wiki/Non-breaking_space
            char extraSpace = Convert.ToChar(160);
            source = source.Replace(extraSpace, ' ');

            for (int i = 0; i < source.Length; i++)
            {
                char ch = source[i];
                char next = i + 1 < source.Length ? source[i + 1] : Constants.EMPTY;
                char last = sb.Length > 0  ? sb[sb.Length - 1] : Constants.EMPTY;

                if (string.IsNullOrWhiteSpace(ch.ToString()))
                {
                    precompiledPart = precompiledPart || 
                        lastToken.ToString().Equals(Constants.COMPILED_FUNCTION, StringComparison.OrdinalIgnoreCase);
                    lastToken.Clear();
                }

                if (ch == '\n')
                {
                    if (sb.Length > lastScriptLength)
                    {
                        char2Line[sb.Length - 1] = lineNumber;
                        lastScriptLength = sb.Length;
                    }
                    lineNumber++;
                    if (simpleComments)
                    {
                        inComments = simpleComments = false;
                    }
                    spaceOK = precompiledPart;
                    lastToken.Clear();
                    continue;
                }

                if (inComments && ((simpleComments && ch != '\n') ||
                                  (!simpleComments && ch != '*')))
                {
                    continue;
                }

                switch (ch)
                {
                    case '/':
                        if (!inQuotes && (inComments || next == '/' || next == '*'))
                        {
                            inComments = true;
                            simpleComments = simpleComments || next == '/';
                            continue;
                        }
                        break;
                    case '*':
                        if (!inQuotes && (inComments && next == '/'))
                        {
                            i++; // skip next character
                            inComments = false;
                            continue;
                        }
                        break;
                    case '\'':
                        if (!inComments && !inQuotes2 && (prev != '\\' || prevprev == '\\'))
                        {
                            ch = '"';
                            inQuotes = inQuotes1 = !inQuotes1;
                            if (inQuotes && (prev == '"' && lineNumberQuote == 0))
                            {
                                lineNumberQuote = lineNumber;
                            }
                        }
                        break;
                    case '“':
                    case '”':
                    case '„':
                    case '"':
                        ch = '"';
                        if (!inComments && !inQuotes1 && (prev != '\\' || prevprev == '\\'))
                        {
                            inQuotes = inQuotes2 = !inQuotes2;
                            if (inQuotes && (prev == '"' && lineNumberQuote == 0))
                            {
                                lineNumberQuote = lineNumber;
                            }
                        }
                        else if (inQuotes1)
                        {
                            sb.Append("\\");
                        }
                        break;
                    case ' ':
                        if (inQuotes)
                        {
                            sb.Append(ch);
                        }
                        else
                        {
                            bool keepSpace = KeepSpace(sb, next);
                            bool usedSpace = spaceOK;
                            spaceOK = keepSpace ||
                                 (prev != Constants.EMPTY && prev != Constants.NEXT_ARG && spaceOK);
                            if (spaceOK || KeepSpaceOnce(sb, next))
                            {
                                sb.Append(ch);
                            }
                            spaceOK = spaceOK || (usedSpace && prev == Constants.NEXT_ARG);
                        }
                        continue;
                    case '\t':
                    case '\r':
                        if (inQuotes) sb.Append(ch);
                        continue;
                    case Constants.START_ARG:
                        if (!inQuotes && !inComments)
                        {
                            if (levelParentheses == 0)
                            {
                                lineNumberPar = lineNumber;
                            }
                            levelParentheses++;
                        }
                        break;
                    case Constants.END_ARG:
                        if (!inQuotes && !inComments)
                        {
                            levelParentheses--;
                            spaceOK = false;
                            if (levelParentheses < 0)
                            {
                                ThrowErrorMsg(parenthErrorMsg, source, levelParentheses, lineNumberPar, lineNumber, filename);
                            }
                        }
                        break;
                    case Constants.START_GROUP:
                        if (!inQuotes && !inComments)
                        {
                            if (levelCurly == 0)
                            {
                                lineNumberCurly = lineNumber;
                            }
                            levelCurly++;
                            precompiledCounter = precompiledPart ? precompiledCounter +  1 : 0;
                        }
                        break;
                    case Constants.END_GROUP:
                        if (!inQuotes && !inComments)
                        {
                            levelCurly--;
                            spaceOK = false;
                            if (levelCurly < 0)
                            {
                                ThrowErrorMsg(curlyErrorMsg, source, levelCurly, lineNumberCurly, lineNumber, filename);
                            }
                            if (precompiledPart && --precompiledCounter <= 0)
                            {
                                precompiledPart = false;
                                precompiledCounter = 0;
                            }
                        }
                        break;
                    case Constants.START_ARRAY:
                        if (!inQuotes && !inComments)
                        {
                            if (levelBrackets == 0)
                            {
                                lineNumberBrack = lineNumber;
                            }
                            levelBrackets++;
                        }
                        break;
                    case Constants.END_ARRAY:
                        if (!inQuotes && !inComments)
                        {
                            levelBrackets--;
                            if (levelBrackets < 0)
                            {
                                ThrowErrorMsg(bracketErrorMsg, source, levelBrackets, lineNumberBrack, lineNumber, filename);
                            }
                        }
                        break;
                    case Constants.END_STATEMENT:
                        if (!inQuotes)
                        {
                            spaceOK = false;
                        }
                        break;
                    default:
                        break;
                }
                if (!inComments)
                {
                    sb.Append(ch);
                    lastToken.Append(ch);
                }
                prevprev = prev;
                prev = ch;
            }

            if (sb.Length > lastScriptLength)
            {
                char2Line[sb.Length - 1] = lineNumber;
                lastScriptLength = sb.Length;
                //result += lineNumber + ": " + (sb.Length - 1) + " " +
                //  source.Substring(source.Length - Math.Min(source.Length, 40), Math.Min(source.Length, 40)) + "\n";
            }

            bool error = levelCurly != 0 || levelBrackets != 0 || levelParentheses != 0 || inQuotes;
            if (error)
            {
                if (inQuotes)
                {
                    ThrowErrorMsg(quoteErrorMsg, source, 1, lineNumberQuote, lineNumber, filename);
                }
                else if (levelBrackets != 0)
                {
                    ThrowErrorMsg(bracketErrorMsg, source, levelBrackets, lineNumberBrack, lineNumber, filename);
                }
                else if (levelParentheses != 0)
                {
                    ThrowErrorMsg(parenthErrorMsg, source, levelParentheses, lineNumberPar, lineNumber, filename);
                }
                else if (levelCurly != 0)
                {
                    ThrowErrorMsg(curlyErrorMsg, source, levelCurly, lineNumberCurly, lineNumber, filename);
                }
            }

            return sb.ToString().Trim();
        }

        public static string BeautifyScript(string script, string header)
        {
            StringBuilder result = new StringBuilder();
            char[] extraSpace = ("<>=&|+-*/%").ToCharArray();

            int indent = Constants.INDENT;
            result.AppendLine(header);

            bool inQuotes = false;
            bool lineStart = true;

            for (int i = 0; i < script.Length; i++)
            {
                char ch = script[i];
                inQuotes = ch == Constants.QUOTE ? !inQuotes : inQuotes;

                if (inQuotes)
                {
                    result.Append(ch);
                    continue;
                }

                bool needExtra = extraSpace.Contains(ch) && i > 0 && i < script.Length - 1;
                if (needExtra && !extraSpace.Contains(script[i - 1]))
                {
                    result.Append(" ");
                }

                switch (ch)
                {
                    case Constants.START_GROUP:
                        result.AppendLine(" " + Constants.START_GROUP);
                        indent += Constants.INDENT;
                        lineStart = true;
                        break;
                    case Constants.END_GROUP:
                        indent -= Constants.INDENT;
                        result.AppendLine(new String(' ', indent) + Constants.END_GROUP);
                        lineStart = true;
                        break;
                    case Constants.END_STATEMENT:
                        result.AppendLine(ch.ToString());
                        lineStart = true;
                        break;
                    default:
                        if (lineStart)
                        {
                            result.Append(new String(' ', indent));
                            lineStart = false;
                        }
                        result.Append(ch.ToString());
                        break;
                }
                if (needExtra && !extraSpace.Contains(script[i + 1]))
                {
                    result.Append(" ");
                }
            }

            result.AppendLine(Constants.END_GROUP.ToString());
            return result.ToString();
        }

        public static string ReplaceSpaces(ParsingScript script, char replaceChar = ',', char end = Constants.END_STATEMENT)
        {
            StringBuilder sb = new StringBuilder(); 
            while (script.StillValid() && script.TryCurrent() != end)
            {
                var token = GetBodyBetween(script, '\0', ' ', end);
                sb.Append(token + replaceChar);
            }
            if (sb.Length > 0 && sb[sb.Length - 1] == replaceChar)
            {
                sb.Remove(sb.Length - 1, 1);
            }
            return sb.ToString();
        }

        public static string GetBodySize(ParsingScript script, string endToken1, string endToken2 = null)
        {
            int start = script.Pointer;
            int length = 0;
            int braces = 0;
            bool inQuotes = false;
            bool inQuotes1 = false;
            bool inQuotes2 = false;
            bool checkBraces = true;
            char prev = Constants.EMPTY;
            char prevprev = Constants.EMPTY;

            for (; script.StillValid(); script.Forward())
            {
                if (script.StartsWith(endToken1))
                {
                    script.Forward(endToken1.Length + 1);
                    return endToken1;
                }
                if (script.StartsWith(endToken2))
                {
                    script.Forward(endToken2.Length + 1);
                    return endToken2;
                }

                char ch = script.Current;
                checkBraces = !inQuotes;
                if (ch == Constants.QUOTE && !inQuotes1 && (prev != '\\' || prevprev == '\\'))
                {
                    inQuotes = inQuotes2 = !inQuotes2;
                }
                if (ch == Constants.QUOTE1 && !inQuotes2 && (prev != '\\' || prevprev == '\\'))
                {
                    inQuotes = inQuotes1 = !inQuotes1;
                }
                if (string.IsNullOrWhiteSpace(ch.ToString()) && length == 0)
                {
                    continue;
                }
                else if (checkBraces && ch == '{')
                {
                    braces++;
                }
                else if (checkBraces && ch == '}')
                {
                    braces--;
                }

                length++;
                prevprev = prev;
                prev = ch;
                if (braces < 0)
                {
                    if (ch == '}')
                    {
                        length--;
                    }
                    break;
                }
            }

            return "";
        }

        public static string GetBodyBetween(ParsingScript script, char open = Constants.START_ARG,
                                            char close = Constants.END_ARG, char end = '\0')
        {
            // We are supposed to be one char after the beginning of the string, i.e.
            // we must not have the opening char as the first one.
            StringBuilder sb = new StringBuilder(script.Size());
            int braces = 0;
            bool inQuotes  = false;
            bool inQuotes1 = false;
            bool inQuotes2 = false;
            bool checkBraces = true;
            char prev = Constants.EMPTY;
            char prevprev = Constants.EMPTY;

            for (; script.StillValid(); script.Forward())
            {
                char ch = script.Current;

                if (ch == end && !inQuotes)
                {
                    break;
                }

                if (close != Constants.QUOTE)
                {
                    checkBraces = !inQuotes;
                    if (ch == Constants.QUOTE && !inQuotes1 && (prev != '\\' || prevprev == '\\'))
                    {
                        inQuotes = inQuotes2 = !inQuotes2;
                    }
                    if (ch == Constants.QUOTE1 && !inQuotes2 && (prev != '\\' || prevprev == '\\'))
                    {
                        inQuotes = inQuotes1 = !inQuotes1;
                    }
                }

                if (string.IsNullOrWhiteSpace(ch.ToString()) && sb.Length == 0)
                {
                    continue;
                }
                else if (checkBraces && ch == open)
                {
                    braces++;
                }
                else if (checkBraces && ch == close)
                {
                    braces--;
                }

                sb.Append(ch);
                prevprev = prev;
                prev = ch;
                if (braces < 0)
                {
                    if (ch == close)
                    {
                        sb.Remove(sb.Length - 1, 1);
                    }
                    break;
                }
            }

            return sb.ToString();
        }

        public static string ProtectQuotes(string str)
        {
            StringBuilder sb = new StringBuilder(str.Length);
            char prev        = Constants.EMPTY;
            char prevprev    = Constants.EMPTY;

            for (int i = 0; i < str.Length; i++)
            {
                char ch = str[i];

                if (ch == Constants.QUOTE && (prev != '\\' || prevprev == '\\'))
                {
                    sb.Append('\\');
                }
                sb.Append(ch);
                prevprev = prev;
                prev = ch;
            }

            return sb.ToString();
        }

        public static string IsNotSign(string data)
        {
            //return data.StartsWith(Constants.NOT) ? Constants.NOT : null;
            return data.StartsWith(Constants.NOT) && !data.StartsWith(Constants.NOT_EQUAL) ? Constants.NOT : null;
        }

        public static string ValidAction(string rest)
        {
            string action = Utils.StartsWith(rest, Constants.ACTIONS);
            return action;
        }

        public static bool IsAction(char ch)
        {
            return ch == '+' || ch == '-' || ch == '*' || ch == '/' || ch == '%' || ch == '&' || ch == '|' || ch == '^';
        }

        public static string StartsWith(string data, string[] items)
        {
            foreach (string item in items)
            {
                if (data.StartsWith(item))
                {
                    return item;
                }
            }
            return null;
        }

        public static List<Variable> GetArrayIndices(ParsingScript script, string varName, Action<string> updateVarName)
        {
            int end = 0;
            return GetArrayIndices(script, varName, end, (string str, int i) => { updateVarName(str); end = i; });
        }
        public static async Task<List<Variable>> GetArrayIndicesAsync(ParsingScript script, string varName, Action<string> updateVarName)
        {
            int end = 0;
            return await GetArrayIndicesAsync(script, varName, end, (string str, int i) => { updateVarName(str); end = i; });
        }

        public static List<Variable> GetArrayIndices(ParsingScript script, string varName, int end, Action<string, int> updateVals)
        {
            List<Variable> indices = new List<Variable>();

            int argStart = varName.IndexOf(Constants.START_ARRAY);
            if (argStart < 0)
            {
                return indices;
            }
            int firstIndexStart = argStart;

            while (argStart < varName.Length &&
                   varName[argStart] == Constants.START_ARRAY)
            {
                int argEnd = varName.IndexOf(Constants.END_ARRAY, argStart + 1);
                if (argEnd == -1 || argEnd <= argStart + 1)
                {
                    break;
                }

                ParsingScript tempScript = script.GetTempScript(varName, argStart);
                tempScript.MoveForwardIf(Constants.START_ARG, Constants.START_ARRAY);

                Variable index = tempScript.Execute(Constants.END_ARRAY_ARRAY);

                indices.Add(index);
                argStart = argEnd + 1;
            }

            if (indices.Count > 0)
            {
                varName = varName.Substring(0, firstIndexStart);
                end = argStart - 1;
            }

            updateVals(varName, end);
            return indices;
        }
        public static async Task<List<Variable>> GetArrayIndicesAsync(ParsingScript script, string varName, int end, Action<string, int> updateVals)
        {
            List<Variable> indices = new List<Variable>();

            int argStart = varName.IndexOf(Constants.START_ARRAY);
            if (argStart < 0)
            {
                return indices;
            }
            int firstIndexStart = argStart;

            while (argStart < varName.Length &&
                   varName[argStart] == Constants.START_ARRAY)
            {
                int argEnd = varName.IndexOf(Constants.END_ARRAY, argStart + 1);
                if (argEnd == -1 || argEnd <= argStart + 1)
                {
                    break;
                }

                ParsingScript tempScript = script.GetTempScript(varName, argStart);
                tempScript.MoveForwardIf(Constants.START_ARG, Constants.START_ARRAY);

                Variable index = await tempScript.ExecuteAsync(Constants.END_ARRAY_ARRAY);

                indices.Add(index);
                argStart = argEnd + 1;
            }

            if (indices.Count > 0)
            {
                varName = varName.Substring(0, firstIndexStart);
                end = argStart - 1;
            }

            updateVals(varName, end);
            return indices;
        }

        public static List<string> ExtractTokens(ParsingScript script)
        {
            List<string> tokens = new List<string>(); 
            script.MoveForwardIf(Constants.START_ARG);
            while (script.TryCurrent() != Constants.END_GROUP)
            {
                string propName = Utils.GetToken(script, Constants.TOKEN_SEPARATION);
                script.MoveForwardIf(Constants.NEXT_ARG);
                tokens.Add(propName);
            }
            return tokens;
        }

        public static Variable ExtractArrayElement(Variable array,
                                                   List<Variable> indices,
                                                   ParsingScript script)
        {
            Variable currLevel = array;

            for (int i = 0; i < indices.Count; i++)
            {
                Variable index = indices[i];
                int arrayIndex = currLevel.GetArrayIndex(index);

                int tupleSize = currLevel.Tuple != null ? currLevel.Tuple.Count : 0;
                if (arrayIndex < 0 || arrayIndex >= tupleSize)
                {
                    ThrowErrorMsg("Unknown index [" + index.AsString() +
                                  "] for tuple of size " + tupleSize, script, index.AsString());
                }
                currLevel = currLevel.Tuple[arrayIndex];
            }
            return currLevel;
        }

        public static string GetLinesFromList(ParsingScript script)
        {
            Variable lines = Utils.GetItem(script);
            if (lines.Tuple == null)
            {
                throw new ArgumentException("Expected a list argument");
            }

            StringBuilder sb = new StringBuilder(80 * lines.Tuple.Count);
            foreach (Variable line in lines.Tuple)
            {
                sb.AppendLine(line.String);
            }

            return sb.ToString();
        }

        public static string ProcessString(string text)
        {
            text = text.Replace("\\\"", "\"");
            text = text.Replace("\\t", "\t");
            text = text.Replace("\\n", "\n");

            return text;
        }
        public static async Task<Variable> GetVar(string paramName, ParsingScript script)
        {
            if (script == null)
            {
                script = new ParsingScript("");
            }
            ParserFunction function = ParserFunction.GetVariable(paramName, script);
            if (function == null)
            {
                throw new ArgumentException("Variable [" + paramName + "] not found.");
            }
            Variable result = await function.GetValueAsync(script);
            return result;
        }
        public static async Task<string> GetString(string paramName, ParsingScript script = null)
        {
            Variable result = await GetVar(paramName, script);
            return result.AsString();
        }
        public static async Task<double> GetDouble(string paramName, ParsingScript script = null)
        {
            Variable result = await GetVar(paramName, script);
            return result.AsDouble();
        }
        public static string PrepareArgs(string argsStr, bool validateQuotes = false)
        {
            argsStr = argsStr.Trim();
            if (!string.IsNullOrEmpty(argsStr) && argsStr[0] == Constants.START_ARG)
            {
                argsStr = argsStr.Substring(1);
            }
            if (!string.IsNullOrEmpty(argsStr) && argsStr[argsStr.Length - 1] == Constants.END_ARG)
            {
                argsStr = argsStr.Substring(0, argsStr.Length - 1);
            }
            string src = validateQuotes ? "\\\"" : "\"";
            string dst = validateQuotes ? "\"" : "\\\"";
            argsStr = argsStr.Replace(src, dst);
            return argsStr;
        }

        public static Variable Calculate(string functionName, string argsStr)
        {
            ParsingScript script = new ParsingScript(argsStr);
            string action = "";
            ParserFunction func = new ParserFunction(script, functionName, Constants.EMPTY, ref action);
            Variable current = func.GetValue(script);

            return current;
        }
    }
}
