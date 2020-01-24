using System;
using System.Collections.Generic;
using System.Text;

using System.CodeDom.Compiler;
using System.IO;
using Microsoft.CSharp;
using System.Reflection;
using System.Globalization;
using System.Linq.Expressions;
using System.Linq;
using System.Threading.Tasks;

namespace SplitAndMerge
{
    public class Precompiler
    {
#if __ANDROID__ == false && __IOS__ == false
        static Dictionary<string, Variable.VarType> m_returnTypes = new Dictionary<string, Variable.VarType>();

        string m_functionName;
        string m_originalCode;
        string m_cscsCode;
        string[] m_actualArgs;
        StringBuilder m_converted = new StringBuilder();
        Dictionary<string, Variable> m_argsMap;
        Dictionary<string, string> m_paramMap = new Dictionary<string, string>();
        Dictionary<string, int> m_definitionsMap = new Dictionary<string, int>();

        HashSet<string> m_newVariables = new HashSet<string>();
        List<string> m_statements;
        Variable.VarType m_returnType;
        int m_statementId;
        int m_tokenId;
        string m_currentStatement;
        string m_nextStatement;
        string m_depth;
        bool m_knownExpression;
        //bool m_assigmentExpression;
        bool m_lastStatementReturn;

        bool m_scriptInCSharp;

        ParsingScript m_parentScript;
        public string CSharpCode { get; private set; }

        Func<List<string>, List<double>, List<List<string>>, List<List<double>>,
             List<Dictionary<string, string>>, List<Dictionary<string, double>>, List<Variable>, Variable> m_compiledFunc;

        Func<List<string>, List<double>, List<List<string>>, List<List<double>>,
             List<Dictionary<string, string>>, List<Dictionary<string, double>>, List<Variable>, Task<Variable>> m_compiledFuncAsync;

        static List<string> s_definitions = new List<string>();
        static List<string> s_namespaces  = new List<string>();

        public static bool AsyncMode { get; set; } = true;

        static string NUMERIC_VAR_ARG   = "__varNum";
        static string STRING_VAR_ARG    = "__varStr";
        static string NUMERIC_ARRAY_ARG = "__varArrNum";
        static string STRING_ARRAY_ARG  = "__varArrStr";
        static string NUMERIC_MAP_ARG   = "__varMapNum";
        static string STRING_MAP_ARG    = "__varMapStr";
        static string CSCS_VAR_ARG      = "__varVar";

        static string ARGS_TEMP_VAR     = "__argsTempStr";
        static string SCRIPT_TEMP_VAR   = "__scriptTempVar";
        static string PARSER_TEMP_VAR   = "__funcTempVar";
        static string ACTION_TEMP_VAR   = "__actionTempVar";
        static string VARIABLE_TEMP_VAR = "__varTempVar";

        public static void RegisterReturnType(string functionName, string functionType)
        {
            m_returnTypes[functionName] = Constants.StringToType(functionType);
        }

        public static Variable.VarType GetReturnType(string functionName)
        {
            Variable.VarType retType = Variable.VarType.NONE;
            m_returnTypes.TryGetValue(functionName, out retType);
            return retType;
        }

        public static void AddDefinition(string def)
        {
            s_definitions.Add(def);
        }
        public static void AddNamespace(string ns)
        {
            s_namespaces.Add(ns);
        }
        public static void ClearDefinitions()
        {
            s_definitions.Clear();
        }
        public static void ClearNamespaces()
        {
            s_namespaces.Clear();
        }

        public Precompiler(string functionName, string[] args, Dictionary<string, Variable> argsMap,
                           string cscsCode, ParsingScript parentScript)
        {
            m_functionName = functionName;
            m_actualArgs = args;
            m_argsMap = argsMap;
            m_originalCode = cscsCode;
            m_returnType = GetReturnType(m_functionName);
            m_parentScript = parentScript;
        }

        public void Compile(bool scriptInCSharp = false)
        {
            m_scriptInCSharp = scriptInCSharp;

            var compilerParams = new CompilerParameters();

            compilerParams.GenerateInMemory = true;
            compilerParams.TreatWarningsAsErrors = false;
            compilerParams.GenerateExecutable = false;
            compilerParams.CompilerOptions = "/optimize";

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly asm in assemblies)
            {
                AssemblyName asmName = asm.GetName();
                if (asmName == null || string.IsNullOrWhiteSpace(asmName.CodeBase))
                {
                    continue;
                }

                var uri = new Uri(asmName.CodeBase);
                if (uri != null && File.Exists(uri.LocalPath))
                {
                    compilerParams.ReferencedAssemblies.Add(uri.LocalPath);
                }
            }

            m_cscsCode = Utils.ConvertToScript(m_originalCode, out _);
            RemoveIrrelevant(m_cscsCode);

            CSharpCode = ConvertScript();

            var provider = new CSharpCodeProvider();
            CompilerResults compile = provider.CompileAssemblyFromSource(compilerParams, CSharpCode);

            if (compile.Errors.HasErrors)
            {
                string text = "Compile error: ";
                foreach (var ce in compile.Errors)
                {
                    text += ce.ToString() + " -- ";
                }

                throw new ArgumentException(text);
            }

            try
            {
                if (AsyncMode)
                {
                    m_compiledFuncAsync = CompileAndCacheAsync(compile, m_functionName);
                }
                else
                {
                    m_compiledFunc = CompileAndCache(compile, m_functionName);
                }
            }
            catch (Exception exc)
            {
                throw new ArgumentException("Compile error: " + exc.Message);
            }
        }

        static Func<List<string>, List<double>, List<List<string>>, List<List<double>>,
                           List<Dictionary<string, string>>, List<Dictionary<string, double>>, List<Variable>, Variable>
                            CompileAndCache(CompilerResults compile, string functionName)
        {
            Tuple<MethodCallExpression, List<ParameterExpression>> tuple =
                 CompileBase(compile, functionName);

            MethodCallExpression methodCall = tuple.Item1;
            List<ParameterExpression> paramTypes = tuple.Item2;

            var lambda =
              Expression.Lambda<Func<List<string>, List<double>, List<List<string>>, List<List<double>>,
                                     List<Dictionary<string, string>>, List<Dictionary<string, double>>, List<Variable>, Variable>>(
                methodCall, paramTypes.ToArray());
            var func = lambda.Compile();

            return func;
        }

        static Func<List<string>, List<double>, List<List<string>>, List<List<double>>,
                   List<Dictionary<string, string>>, List<Dictionary<string, double>>, List<Variable>, Task<Variable>>
                    CompileAndCacheAsync(CompilerResults compile, string functionName)
        {
            Tuple<MethodCallExpression, List<ParameterExpression>> tuple =
                 CompileBase(compile, functionName);

            MethodCallExpression methodCall = tuple.Item1;
            List<ParameterExpression> paramTypes = tuple.Item2;

            var lambda =
              Expression.Lambda<Func<List<string>, List<double>, List<List<string>>, List<List<double>>,
                                     List<Dictionary<string, string>>, List<Dictionary<string, double>>, List<Variable>, Task<Variable>>>(
                methodCall, paramTypes.ToArray());
            var func = lambda.Compile();

            return func;
        }

        static Tuple<MethodCallExpression, List<ParameterExpression>>
              CompileBase(CompilerResults compile, string functionName)
        {
            Module module = compile.CompiledAssembly.GetModules()[0];
            Type mt = module.GetType("SplitAndMerge.Precompiler");

            List<ParameterExpression> paramTypes = new List<ParameterExpression>();
            paramTypes.Add(Expression.Parameter(typeof(List<string>), STRING_VAR_ARG));
            paramTypes.Add(Expression.Parameter(typeof(List<double>), NUMERIC_VAR_ARG));
            paramTypes.Add(Expression.Parameter(typeof(List<List<string>>), STRING_ARRAY_ARG));
            paramTypes.Add(Expression.Parameter(typeof(List<List<double>>), NUMERIC_ARRAY_ARG));
            paramTypes.Add(Expression.Parameter(typeof(List<Dictionary<string, string>>), STRING_MAP_ARG));
            paramTypes.Add(Expression.Parameter(typeof(List<Dictionary<string, double>>), NUMERIC_MAP_ARG));
            paramTypes.Add(Expression.Parameter(typeof(List<Variable>), CSCS_VAR_ARG));
            List<Type> argTypes = new List<Type>();
            for (int i = 0; i < paramTypes.Count; i++)
            {
                argTypes.Add(paramTypes[i].Type);
            }

            MethodInfo methodInfo = mt.GetMethod(functionName, argTypes.ToArray());
            MethodCallExpression methodCall = Expression.Call(methodInfo, paramTypes);

            return new Tuple<MethodCallExpression, List<ParameterExpression>>(methodCall, paramTypes);
        }

        public Variable Run(List<string> argsStr, List<double> argsNum, List<List<string>> argsArrStr,
                            List<List<double>> argsArrNum, List<Dictionary<string, string>> argsMapStr,
                            List<Dictionary<string, double>> argsMapNum, List<Variable> argsVar, bool throwExc = true)
        {
            if (m_compiledFunc == null)
            {
                // For "late bindings"...
                Compile();
            }

            Variable result = m_compiledFunc.Invoke(argsStr, argsNum, argsArrStr, argsArrNum, argsMapStr, argsMapNum, argsVar);
            return result;
        }
        public Variable RunAsync(List<string> argsStr, List<double> argsNum, List<List<string>> argsArrStr,
                            List<List<double>> argsArrNum, List<Dictionary<string, string>> argsMapStr,
                            List<Dictionary<string, double>> argsMapNum, List<Variable> argsVar, bool throwExc = true)
        {
            if (m_compiledFuncAsync == null)
            {
                // For "late bindings"...
                Compile();
            }

            var task = m_compiledFuncAsync.Invoke(argsStr, argsNum, argsArrStr, argsArrNum, argsMapStr, argsMapNum, argsVar);
            Variable result = task.Result;
            return result;
        }

        Variable.VarType GetVariableType(string paramName)
        {
            if (IsNumber(paramName))
            {
                return Variable.VarType.NUMBER;
            }
            else if (IsString(paramName))
            {
                return Variable.VarType.STRING;
            }

            if (Constants.RESERVED.Contains(paramName) ||
                Constants.TOKEN_SEPARATION_STR.Contains(paramName))
            {
                return Variable.VarType.NONE;
            }

            if (IsMathFunction(paramName, out paramName))
            {
                return Variable.VarType.NUMBER;
            }

            string resolved = ResolveToken(paramName, out _);
            if (resolved.StartsWith(NUMERIC_VAR_ARG, StringComparison.InvariantCulture))
            {
                return Variable.VarType.NUMBER;
            }
            if (resolved.StartsWith(NUMERIC_ARRAY_ARG, StringComparison.InvariantCulture) &&
                paramName.Count(x => x == '[') >= 2)
            {
                return Variable.VarType.NUMBER;
            }

            Variable arg;
            if (m_argsMap.TryGetValue(paramName, out arg))
            {
                return arg.Type;
            }

            /*var functionReturnType = GetReturnType(paramName);
            if (functionReturnType != Variable.VarType.NONE)
            {
                return functionReturnType;
            }

            ParserFunction function = ParserFunction.GetFunction(paramName, m_parentScript);
            if (function == null)
            {
                return Variable.VarType.NONE;
            }
            if (function is INumericFunction)
            {
                return Variable.VarType.NUMBER;
            }
            else if (function is IStringFunction)
            {
                return Variable.VarType.STRING;
            }
            else if (function is IArrayFunction)
            {
                return Variable.VarType.ARRAY;
            }*/

            return Variable.VarType.NONE;
        }

        public string RegisterVariableString(string paramName, string paramValue = "")
        {
            if (string.IsNullOrWhiteSpace(paramValue))
            {
                paramValue = paramName;
            }
            return m_depth + "ParserFunction.AddGlobalOrLocalVariable(\"" + paramName +
                     "\", new GetVarFunction(Variable.ConvertToVariable(" + paramValue + ")));\n";
        }

        string ConvertScript()
        {
            m_converted.Clear();

            int numIndex = 0;
            int strIndex = 0;
            int arrNumIndex = 0;
            int arrStrIndex = 0;
            int mapNumIndex = 0;
            int mapStrIndex = 0;
            int varIndex = 0;
            // Create a mapping from the original function argument to the element array it is in.
            for (int i = 0; i < m_actualArgs.Length; i++)
            {
                Variable typeVar = m_argsMap[m_actualArgs[i]];
                m_paramMap[m_actualArgs[i]] =
                    typeVar.Type == Variable.VarType.STRING ? STRING_VAR_ARG + "[" + (strIndex++) + "]" :
                    typeVar.Type == Variable.VarType.NUMBER ? NUMERIC_VAR_ARG + "[" + (numIndex++) + "]" :
                    typeVar.Type == Variable.VarType.ARRAY_STR ? STRING_ARRAY_ARG + "[" + (arrStrIndex++) + "]" :
                    typeVar.Type == Variable.VarType.ARRAY_NUM ? NUMERIC_ARRAY_ARG + "[" + (arrNumIndex++) + "]" :
                    typeVar.Type == Variable.VarType.MAP_STR ? STRING_MAP_ARG + "[" + (mapStrIndex++) + "]" :
                    typeVar.Type == Variable.VarType.MAP_NUM ? NUMERIC_MAP_ARG + "[" + (mapNumIndex++) + "]" :
                    typeVar.Type == Variable.VarType.VARIABLE ? CSCS_VAR_ARG + "[" + (varIndex++) + "]" :
                            "";
            }

            m_converted.AppendLine("using System; using System.Collections; using System.Collections.Generic; using System.Collections.Specialized; " +
                                   "using System.Globalization; using System.Linq; using System.Linq.Expressions; using System.Reflection; " +
                                   "using System.Text; using System.Threading; using System.Threading.Tasks;");// using static System.Math;");
            for (int i = 0; i < s_namespaces.Count; i++)
            {
                var ns = s_namespaces[i].Trim();
                if (!ns.StartsWith("using "))
                {
                    ns = "using " + ns;
                }
                if (!ns.EndsWith(";"))
                {
                    ns += ";";
                }

                m_converted.AppendLine(ns);
            }
            m_converted.AppendLine("namespace SplitAndMerge {\n" +
                                   "  public partial class Precompiler {");

            for (int i = 0; i < s_definitions.Count; i++)
            {
                var def = s_definitions[i].Trim();
                if (!def.StartsWith("static ") && !def.Contains(" static "))
                {
                    def = "static " + def;
                }
                if (!def.EndsWith(";"))
                {
                    def += ";";
                }

                m_converted.AppendLine(def);
            }
            if (AsyncMode)
            {
                m_converted.AppendLine("    public static async Task<Variable> " + m_functionName);
            }
            else
            {
                m_converted.AppendLine("    public static Variable " + m_functionName);
            }
            m_converted.AppendLine(
                           "(List<string> " + STRING_VAR_ARG + ",\n" +
                           " List<double> " + NUMERIC_VAR_ARG + ",\n" +
                           " List<List<string>> " + STRING_ARRAY_ARG + ",\n" +
                           " List<List<double>> " + NUMERIC_ARRAY_ARG + ",\n" +
                           " List<Dictionary<string, string>> " + STRING_MAP_ARG + ",\n" +
                           " List<Dictionary<string, double>> " + NUMERIC_MAP_ARG + ",\n" +
                           " List<Variable> " + CSCS_VAR_ARG + ") {\n");
            m_depth = "      ";

            m_converted.AppendLine("     string " + ARGS_TEMP_VAR + "= \"\";");
            m_converted.AppendLine("     string " + ACTION_TEMP_VAR + " = \"\";");
            m_converted.AppendLine("     ParsingScript " + SCRIPT_TEMP_VAR + " = null;");
            m_converted.AppendLine("     ParserFunction " + PARSER_TEMP_VAR + " = null;");
            m_converted.AppendLine("     Variable " + VARIABLE_TEMP_VAR + " = null;");
            m_newVariables.Add(ARGS_TEMP_VAR);
            m_newVariables.Add(ACTION_TEMP_VAR);
            m_newVariables.Add(SCRIPT_TEMP_VAR);
            m_newVariables.Add(PARSER_TEMP_VAR);
            m_newVariables.Add(VARIABLE_TEMP_VAR);

            m_statements = TokenizeScript(m_cscsCode);
            m_statementId = 0;
            while (m_statementId < m_statements.Count)
            {
                m_currentStatement = m_statements[m_statementId];
                m_nextStatement = m_statementId < m_statements.Count - 1 ? m_statements[m_statementId + 1] : "";
                string converted = m_scriptInCSharp ? ProcessCSStatement(m_currentStatement, m_nextStatement, true) :
                                   ProcessStatement(m_currentStatement, m_nextStatement, true);
                if (!string.IsNullOrWhiteSpace(converted))
                {
                    m_converted.Append(m_depth + converted);
                }
                m_statementId++;
            }

            if (!m_lastStatementReturn)
            {
                m_converted.AppendLine(CreateReturnStatement("Variable.EmptyInstance"));
            }

            m_converted.AppendLine("\n    }\n    }\n}");
            return m_converted.ToString();
        }

        static void RemoveIrrelevant(string code)
        {
            if (code.Length > 0 && code.First() == '{')
            { // Remove redundant braces and semi-colons.
                code = code.Remove(0, 1);
                while (code.Length > 0 && code.Last() == ';')
                {
                    code = code.Remove(code.Length - 1, 1);
                }
                if (code.Length > 0 && code.Last() == '}')
                {
                    code = code.Remove(code.Length - 1, 1);
                }
            }
        }

        bool ProcessSpecialCases(string statement)
        {
            if (statement == ";")
            {
                // don't need end of statement - will be added from the previous statement.
                return true;
            }
            int specialIndex = statement.IndexOf("={};");
            if (specialIndex > 0)
            {
                string varName = statement.Substring(0, specialIndex);
                m_definitionsMap[varName] = m_converted.Length;
                return true;
            }
            return false;
        }

        string ProcessCSStatement(string statement, string nextStatement, bool addNewVars = true)
        {
            if (string.IsNullOrWhiteSpace(statement) || ProcessSpecialCases(statement))
            {
                return "";
            }
            List<string> tokens = TokenizeStatement(statement, true);
            var first = tokens[0];

            m_lastStatementReturn = first == "return";
            if (m_lastStatementReturn)
            {
                if (tokens.Count <= 2)
                {
                    return CreateReturnStatement("Variable.EmptyInstance");
                }
                return CreateReturnStatement(statement.Substring(7));
            }

            string result = "";
            string suffix = "";
            bool isArray = false;

            m_tokenId = 0;
            while (m_tokenId < tokens.Count)
            {
                string token = tokens[m_tokenId];
                string functionName = GetFunctionName(token, ref suffix, ref isArray);

                if (!suffix.Contains('.') && m_argsMap.TryGetValue(functionName, out _))
                {
                    string actualName = m_paramMap[functionName];
                    token = " " + actualName + ReplaceArgsInString(suffix);
                    if (first == "int" || first == "long")
                    {
                        token = "(" + first + ")" + token;
                    }
                }
                result += token;
                m_tokenId++;
            }
            result += ";\n";

            return result;
        }

        string ProcessStatement(string statement, string nextStatement, bool addNewVars = true)
        {
            if (string.IsNullOrWhiteSpace(statement) || ProcessSpecialCases(statement))
            {
                return "";
            }
            List<string> tokens = TokenizeStatement(statement, false);
            List<string> statementVars = new List<string>();

            string result = "";
            m_lastStatementReturn = ProcessReturnStatement(tokens, ref result);
            if (m_lastStatementReturn)
            {
                return result;
            }
            if (ProcessForStatement(statement, tokens, ref result))
            {
                return result;
            }

            m_knownExpression = IsKnownExpression(tokens);

            if (m_knownExpression && tokens.Count > 1)
            {
                result = m_depth;
                if (tokens[1] == "=" && !m_newVariables.Contains(tokens[0]))
                {
                    result += "var ";
                    m_newVariables.Add(tokens[0]);
                }

                string rhs = ProcessRHS(tokens);
                if (!rhs.Contains(";"))
                {
                    result += tokens[0] + tokens[1] + rhs;
                }
                else
                {
                    result += statement;
                }

                if (nextStatement == ";" || !"(){}[]".Contains(statement.Last()))
                {
                    result += ";\n";
                }
                if (IsAssignment(tokens[1]))
                {
                    result += RegisterVariableString(tokens[0]);
                }

                return result;
            }
            if (m_knownExpression && tokens.Count == 1 &&
                (tokens[0].Contains('(') ||
                 tokens[0].Contains(',')))
            {
                result = ReplaceMathArgs(tokens[0]);
                return result;
            }

            m_tokenId = 0;
            while (m_tokenId < tokens.Count)
            {
                bool newVarAdded = false;
                string token = tokens[m_tokenId];
                ProcessToken(tokens, ref m_tokenId, ref result, ref newVarAdded);
                if (m_tokenId == 0 && addNewVars && newVarAdded)
                {
                    statementVars.Add(token);
                }
                m_tokenId++;
            }

            if (result == "{")
            {
                m_depth += "  ";
            }
            else if (result == "}")
            {
                if (m_depth.Length <= 4)
                {
                    throw new ArgumentException("Mismatch of { } parentheses in " + m_functionName);
                }
                m_depth = m_depth.Substring(0, m_depth.Length - 2);
            }

            if (statementVars.Count > 0 || (addNewVars &&
                        statement != "}" && statement != "{" && nextStatement != "{"))
            {
                if (!result.Trim().EndsWith(";"))
                {
                    result += ";\n";
                }
            }
            else if (addNewVars)
            {
                result += "\n";
            }
            for (int i = 0; i < statementVars.Count; i++)
            {
                result += RegisterVariableString(statementVars[i], statementVars[i]);
            }

            return result;
        }

        bool ProcessReturnStatement(List<string> tokens, ref string converted)
        {
            string suffix = "";
            string defaultReturn = VARIABLE_TEMP_VAR;
            bool isArray = false;

            string paramName = tokens.Count > 0 ? GetFunctionName(tokens[0], ref suffix, ref isArray).Trim() : "";
            if (paramName != Constants.RETURN)
            {
                return false;
            }
            if (tokens.Count <= 2)
            {
                converted = CreateReturnStatement("Variable.EmptyInstance");
                return true;
            }
            if (!string.IsNullOrWhiteSpace(suffix))
            {
                // Converting the case "return(..." to the normal case "return (..".
                tokens[2] = suffix + tokens[1] + tokens[2];
            }
            if (tokens.Count == 3)
            {
                bool newVarAdded = false;
                m_tokenId = 2;
                string result = "";

                string token = defaultReturn;

                m_knownExpression = IsKnownExpression(tokens);
                if (m_knownExpression)
                {
                    token = ProcessRHS(tokens);
                }
                else
                {
                    ProcessToken(tokens, ref m_tokenId, ref result, ref newVarAdded);
                    if (!result.Contains(";"))
                    {
                        token = result;
                        result = "";
                    }
                }
                converted += result + CreateReturnStatement(token);
                return true;
            }

            string returnToken = ProcessRHS(tokens);
            if (!returnToken.Contains(";"))
            {
                converted = CreateReturnStatement(returnToken);
            }
            else
            {
                converted = returnToken;
                converted += CreateReturnStatement(defaultReturn);
            }

            return true;
        }

        string ProcessRHS(List<string> tokens, int from = 2)
        {
            string remaining = string.Join("", tokens.GetRange(from, tokens.Count - from));
            if (m_knownExpression)
            {
                return ReplaceArgsInString(remaining);
            }
            string returnToken = ProcessStatement(remaining, "", false);
            return returnToken;
        }

        string CreateReturnStatement(string toReturn)
        {
            if (toReturn == VARIABLE_TEMP_VAR)
            {
                return m_depth + "return " + VARIABLE_TEMP_VAR + ";\n";
            }
            if (!toReturn.Contains("Variable.EmptyInstance"))
            {
                toReturn = "new Variable(" + toReturn + ")";
            }
            if (!AsyncMode)
            {
                return m_depth + "return " + toReturn + ";\n"; 
            }

            string result = m_depth + VARIABLE_TEMP_VAR + " = " + toReturn + ";\n";
            result       += m_depth + "return " + VARIABLE_TEMP_VAR + ";\n";
            return result;
        }

        bool ProcessForStatement(string statement, List<string> tokens, ref string converted)
        {
            string suffix = "";
            bool isArray = false;
            string functionName = GetFunctionName(statement, ref suffix, ref isArray).Trim();
            if (functionName != Constants.FOR)
            {
                return false;
            }
            if (m_nextStatement != ";")
            {
                return false;
            }
            if (m_statements.Count <= m_statementId + 5)
            {
                throw new ArgumentException("Expecting: for(init; condition; loopStatement)");
            }

            string rest = "";
            string varName = GetFunctionName(suffix.Substring(1), ref rest, ref isArray);

            converted = "";
            if (!m_newVariables.Contains(varName))
            {
                m_newVariables.Add(varName);
                converted = m_depth + "var " + varName + rest + ";\n";
            }
            converted += m_depth + statement;
            converted += converted.EndsWith(";") ? "" : ";";
            m_statementId += 2;
            converted += ProcessStatement(m_statements[m_statementId], m_statements[m_statementId + 1], false).Trim();
            converted += converted.EndsWith(";") ? "" : ";";
            m_statementId += 2;
            converted += ProcessStatement(m_statements[m_statementId], m_statements[m_statementId + 1], false).Trim() + " {\n";
            m_statementId++;

            m_depth += "  ";
            converted += RegisterVariableString(varName);

            return true;
        }
        string ProcessCatch(string exceptionVar)
        {
            string suffix = "";
            bool isArray = false;
            string varName = GetFunctionName(exceptionVar.Substring(1), ref suffix, ref isArray);

            string result = "catch(Exception " + varName + ") {\n";
            result += RegisterVariableString(varName, "new Variable(" + varName + ".ToString())");
            m_statementId++;
            return result;
        }
        string ProcessElIf(string elif)
        {
            string result = elif.Replace(Constants.ELSE_IF, "else if");
            return result;
        }

        public string GetExpressionType(List<string> tokens, string functionName, ref bool addNewVarDef)
        {
            if (!tokens[0].Contains("["))
            {
                return m_depth + "var " + functionName;
            }
            bool firstString = tokens[0].Contains("\"");
            string expr = m_depth;

            string param = firstString ? "string" : "double";
            if (!firstString)
            {
                expr += "List<" + param + "> " + functionName + " = new List<" + param + "> ();\n";
            }
            else
            {
                expr += "Dictionary<string," + param + "> " + functionName + " = new Dictionary<string," + param + "> ();\n";
            }

            int position = -1;
            if (m_definitionsMap.TryGetValue(functionName, out position))
            {
                // There was a definition like m={}; sometime before. Now we know what it meant,
                // so we can insert it:
                m_converted.Insert(position, expr);
                expr = "";
            }
            expr += m_depth + tokens[m_tokenId];
            while (++m_tokenId < tokens.Count)
            {
                ProcessToken(tokens, ref m_tokenId, ref expr, ref addNewVarDef);
            }
            m_tokenId = tokens.Count;
            addNewVarDef = false;
            return expr;
        }

        void ProcessToken(List<string> tokens, ref int id, ref string result, ref bool newVarAdded)
        {
            string token = tokens[id].Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            string suffix = "";
            bool isArray = false;
            string functionName = GetFunctionName(token, ref suffix, ref isArray);
            if (string.IsNullOrEmpty(functionName))
            {
                result += suffix;
                return;
            }

            bool reservedWord = Constants.RESERVED.Contains(functionName);
            if (IsString(functionName) || IsNumber(functionName) || reservedWord)
            {
                if (functionName == Constants.CATCH)
                {
                    token = ProcessCatch(suffix);
                }
                else if (functionName == Constants.ELSE_IF)
                {
                    token = ProcessElIf(token);
                }

                if (token == "new")
                {
                    var argsStr = "";
                    for (int i = id + 1; i < tokens.Count; i++)
                    {
                        argsStr += tokens[i].Trim().Replace("\"", "\\\"");
                    }
                    string tempFunc = GetCSCSFunction(argsStr, token);
                    id = tokens.Count - 1;
                    result = tempFunc + result + " " + VARIABLE_TEMP_VAR + ";";
                }
                else
                {
                    result += token;
                }
                return;
            }
            if (Array.IndexOf(Constants.ACTIONS, token) >= 0)
            {
                result += token;
                return;
            }
            if (m_newVariables.Contains(functionName))
            {
                if (id == 0)
                {
                    newVarAdded = !isArray;
                }
                result += token;
                return;
            }

            if (id == 0 && tokens.Count > id + 2 && tokens[id + 1] == "=" && !token.Contains('.'))
            {
                m_newVariables.Add(functionName);
                newVarAdded = true;
                string expr = GetExpressionType(tokens, functionName, ref newVarAdded);
                result += expr;
                return;
            }

            if (!suffix.Contains('.') && m_argsMap.TryGetValue(functionName, out _))
            {
                string actualName = m_paramMap[functionName];
                token = " " + actualName + ReplaceArgsInString(suffix);
                result += token;
                return;
            }
            ProcessFunction(tokens, ref id, ref result, ref newVarAdded);
        }

        string ResolveToken(string token, out bool resolved, string arguments = "")
        {
            resolved = true;
            if (IsString(token) || IsNumber(token))
            {
                return token;
            }

            string replacement;
            if (IsMathFunction(token, out replacement))
            {
                return replacement;
            }

            replacement = GetCSharpFunction(token, arguments);
            if (!string.IsNullOrEmpty(replacement))
            {
                return replacement;
            }

            if (ProcessArray(token, ref replacement))
            {
                return replacement;
            }

            string arrayName, arrayArg;
            if (IsArrayElement(token, out arrayName, out arrayArg))
            {
                token = arrayName;
            }

            if (m_paramMap.TryGetValue(token, out replacement))
            {
                return replacement + arrayArg;
            }

            resolved = !string.IsNullOrWhiteSpace(arrayArg) ||
                        m_newVariables.Contains(token);
            return token + arrayArg;
        }

        static bool IsTokenSeparator(char ch)
        {
            return (ch == ',' || ch == '+' || ch == '-' || ch == '(' || ch == ')' || ch == '[' || ch == ']' ||
                    ch == '%' || ch == '*' || ch == '/' || ch == '&' || ch == '|' || ch == '^' || ch == '?');
        }

        string ReplaceArgsInString(string argStr)
        {
            StringBuilder sb = new StringBuilder();
            bool inQuotes = false;
            string token = "";
            int backSlashes = 0;
            for (int i = 0; i < argStr.Length; i++)
            {
                char ch = argStr[i];
                if (ch == '\\')
                {
                    backSlashes++;
                    continue;
                }

                if (backSlashes % 2 == 0)
                {
                    sb.Append(new string('\\', backSlashes / 2));
                    backSlashes = 0;
                }
                else if (backSlashes >= 1)
                { // Odd number of \ means that this char value is irrelevant
                    if (backSlashes > 1)
                    {
                        sb.Append(new string('\\', (backSlashes - 1) / 2));
                    }
                    sb.Append("'\\'" + ch);
                    backSlashes = 0;
                    continue;
                }

                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    sb.Append(ch);
                }
                else if (inQuotes)
                { // part of a string - just add it
                    sb.Append(ch);
                }
                else if (IsTokenSeparator(ch))
                {
                    string arguments = i + 1 < argStr.Length ? argStr.Substring(i + 1) : "";
                    sb.Append(ResolveToken(token, out _, arguments));
                    sb.Append(ch);
                    token = "";
                }
                else
                { // We are collecting the chars
                    token += ch;
                }
            }

            sb.Append(ResolveToken(token, out _));
            return sb.ToString();
        }

        string GetFunctionName(string token, ref string suffix, ref bool isArray)
        {
            token = token.Trim();
            int paramStart = token.IndexOf('(');
            if (paramStart < 0)
            {
                paramStart = token.IndexOf('.');
            }

            string functionName = paramStart < 0 ? token : token.Substring(0, paramStart);
            suffix = paramStart < 0 ? "" : token.Substring(paramStart);
            int paramEnd = functionName.LastIndexOf(')');
            if (paramEnd < 0)
            {
                paramEnd = functionName.IndexOf('=');
            }
            if (paramEnd >= 0)
            {
                suffix = functionName.Substring(paramEnd);
                functionName = functionName.Substring(0, paramEnd);
            }

            string arrayName, arrayArg;
            isArray = IsArrayElement(functionName, out arrayName, out arrayArg);
            if (isArray)
            {
                functionName = arrayName;
                suffix = arrayArg;
            }

            return functionName;
        }

        string ReplaceMathArgs(string token)
        {
            int paramStart = token.IndexOf('(') + 1;
            string result = "";
            string rest = token.Substring(paramStart).Trim();

            string funcName = token.Substring(0, paramStart);
            IsMathFunction(funcName, out funcName);
            if (rest.EndsWith(";"))
            {
                rest = rest.Substring(0, rest.Length - 1);
            }
            rest = rest.Substring(0, rest.Length - 1);

            var tokens = rest.Split(',');
            int count = 0;
            foreach (var item in tokens)
            {
                result += ProcessStatement(item, "", false);
                count++;
                result += count != tokens.Length ? ',' : ')';
            }
            if (paramStart > 0)
            {
                result = funcName + result;
            }
            return result;
        }

        void ProcessFunction(List<string> tokens, ref int id, ref string result, ref bool newVarAdded)
        {
            //string restStr = string.Join("", tokens.GetRange(m_tokenId, tokens.Count - m_tokenId).ToArray());
            string restStr = tokens[m_tokenId];
            int paramStart = restStr.IndexOf('(');
            int paramEnd = FindChar(restStr, paramStart, ')');
            while (paramEnd < 0 && ++m_tokenId < tokens.Count)
            {
                restStr += tokens[m_tokenId];
            }
            paramEnd = paramStart < 0 ? restStr.Length : paramStart;

            string functionName = paramStart < 0 ? restStr : restStr.Substring(0, paramStart);
            string argsStr = "";
            if (paramStart >= 0)
            {
                paramEnd = paramEnd <= paramStart ? restStr.Length : paramEnd;
                ParsingScript tmpScript = new ParsingScript(restStr.Substring(paramStart, restStr.Length - paramStart));
                argsStr = Utils.PrepareArgs(Utils.GetBodyBetween(tmpScript));
            }

            string token = "";
            if (ProcessArray(argsStr, functionName, ref token))
            {
                result += token;
                return;
            }

            var tryCSharp = GetCSharpFunction(functionName, argsStr);
            if (!string.IsNullOrEmpty(tryCSharp))
            {
                result += tryCSharp + "\n";
                return;
            }

            StringBuilder sb = new StringBuilder();
            char ch = '(';

            int index = functionName.IndexOf('.');
            if (index > 0 && id < tokens.Count - 2 && tokens[id + 1] == "=")
            {
                argsStr = tokens[id + 2];
                sb.AppendLine(m_depth + ACTION_TEMP_VAR + " =\"=\";");
                ch = '=';
                id = tokens.Count;
            }
            else
            {
                sb.AppendLine(m_depth + ACTION_TEMP_VAR + " =\"\";");
            }

            sb.AppendLine(GetCSCSFunction(argsStr,  functionName, ch));

            token = sb.ToString();
            if (tokens.Count >= 3 && tokens[1] == "=" && !string.IsNullOrWhiteSpace(result))
            {
                result = m_depth + token + result + VARIABLE_TEMP_VAR + ";\n";
                newVarAdded = true;
            }
            else
            {
                result += token;
            }
        }

        string GetCSharpFunction(string functionName, string arguments = "")
        {
            if (functionName == "printc")
            {
                arguments = ReplaceArgsInString(arguments.Replace("\\\"", "\""));
                return "Console.WriteLine(" + arguments + ");";
            }
            return "";
        }

        string GetCSCSFunction(string argsStr, string functionName, char ch = '(')
        {
            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(argsStr) && argsStr.Last() == '"' && argsStr.First() == '"')
            {
                argsStr = "\\\"" + argsStr.Substring(1, argsStr.Length - 2) + "\\\"";
            }

            sb.AppendLine(m_depth + ARGS_TEMP_VAR + " =\"" + argsStr + "\";");
            sb.AppendLine(m_depth + SCRIPT_TEMP_VAR + " = new ParsingScript(" + ARGS_TEMP_VAR + ");");
            sb.AppendLine(m_depth + PARSER_TEMP_VAR + " = new ParserFunction(" + SCRIPT_TEMP_VAR + ", \"" + functionName +
                "\", '" + ch + "', ref " + ACTION_TEMP_VAR + ");");

            if (AsyncMode)
            {
                sb.AppendLine(m_depth + VARIABLE_TEMP_VAR + " = await " + PARSER_TEMP_VAR +
                    ".GetValueAsync(" + SCRIPT_TEMP_VAR + ");");
            }
            else
            {
                sb.AppendLine(m_depth + VARIABLE_TEMP_VAR + " = " + PARSER_TEMP_VAR +
                    ".GetValue(" + SCRIPT_TEMP_VAR + ");");
            }
            return sb.ToString();
        }

        bool ProcessArray(string argStr, ref string result)
        {
            int index = argStr.IndexOf('.');
            if (index <= 0)
            {
                return false;
            }
            string arrayName = argStr.Substring(0, index);
            string methodName = argStr.Substring(index + 1);
            return ProcessArray(arrayName, methodName, ref result);
        }

        bool ProcessArray(string arrayName, string methodName, ref string result)
        {
            string mappingName;
            if (!IsDefinedAsArray(arrayName, out _, out _, out mappingName))
            {
                return false;
            }
            if (methodName.ToLower() == "size")
            {
                result = mappingName + ".Count";
                return true;
            }
            return false;
        }

        bool IsArrayElement(string paramName, out string arrayName, out string arrayArg)
        {
            arrayName = paramName;
            arrayArg = "";
            int paramStart = paramName.IndexOf('[');
            if (paramStart > 0)
            {
                arrayName = paramName.Substring(0, paramStart);
                arrayArg = paramName.Substring(paramStart);
                return true;
            }
            return false;
        }
        bool IsDefinedAsArray(string paramName, out string arrayName, out string arrayArg, out string mappingName)
        {
            arrayName = mappingName = paramName;
            arrayArg = "";
            int paramStart = paramName.IndexOf('[');
            if (paramStart > 0)
            {
                arrayName = paramName.Substring(0, paramStart);
                arrayArg = paramName.Substring(paramStart);
            }
            Variable arg;
            if (!m_argsMap.TryGetValue(arrayName, out arg))
            {
                return false;
            }
            mappingName = m_paramMap[arrayName];
            return arg.Type == Variable.VarType.ARRAY ||
                   arg.Type == Variable.VarType.ARRAY_STR ||
                   arg.Type == Variable.VarType.ARRAY_NUM ||
                   arg.Type == Variable.VarType.MAP_STR ||
                   arg.Type == Variable.VarType.MAP_NUM;
        }

        bool IsKnownExpression(List<string> tokens)
        {
            bool numericCandidate = false;
            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];
                string next = i >= tokens.Count - 1 ? "" : tokens[i + 1];
                if (i == 0 && next == "=")
                {
                    continue;
                }
                if (string.IsNullOrWhiteSpace(token) || token == Constants.RETURN)
                {
                    continue;
                }
                if (IsString(token))
                {
                    return false;
                }
                if (Constants.ARITHMETIC_EXPR.Contains(token))
                {
                    numericCandidate = true;
                    continue;
                }

                string suffix = "";
                bool isArray = false;
                string paramName = GetFunctionName(token, ref suffix, ref isArray);

                if (string.IsNullOrWhiteSpace(paramName) || Constants.RESERVED.Contains(paramName))
                {
                    continue;
                }

                var type = GetVariableType(paramName);
                if (type == Variable.VarType.NUMBER)
                {
                    numericCandidate = true;
                    continue;
                }
                if (type == Variable.VarType.STRING)
                {
                    return false;
                }

                bool alreadyDefined;
                ResolveToken(paramName, out alreadyDefined);
                if (!alreadyDefined)
                {
                    return false;
                }
            }
            return numericCandidate;
        }

        static bool IsNumber(string text)
        {
            return Double.TryParse(text, NumberStyles.Number |
                                         NumberStyles.AllowExponent |
                                         NumberStyles.Float,
                                         CultureInfo.InvariantCulture, out _);
        }
        static bool IsString(string text)
        {
            return string.IsNullOrWhiteSpace(text) || text.Contains('"');
        }

        public static List<string> TokenizeScript(string scriptText)
        {
            List<string> tokens = new List<string>();

            int startIndex = 0;
            int i = 0;
            bool inQuotes = false;
            char previous = Constants.EMPTY;

            while (i < scriptText.Length)
            {
                char ch = scriptText[i];
                previous = i> 0 ? scriptText[i - 1] : previous;
                
                if (ch == '"' && previous != '\\')
                {
                    inQuotes = !inQuotes;
                }
                else if (inQuotes)
                {
                }
                else if (Constants.STATEMENT_SEPARATOR.IndexOf(ch) >= 0)
                {
                    if (i > startIndex)
                    {
                        string token = scriptText.Substring(startIndex, i - startIndex);
                        if (token.EndsWith("=") && scriptText.Substring(i).StartsWith("{};"))
                        { // Special degenerate case.
                            tokens.Add(token + "{};");
                            i += 3;
                            startIndex = i;
                            continue;
                        }
                        tokens.Add(token.Trim());
                    }
                    tokens.Add(ch.ToString().Trim());
                    startIndex = i + 1;
                }
                i++;
            }
            if (scriptText.Length > startIndex + 1)
            {
                tokens.Add(scriptText.Substring(startIndex).Trim());
            }
            return tokens;
        }

        public static List<string> TokenizeStatement(string statement, bool scriptInCSharp)
        {
            List<string> tokens = new List<string>();

            int startIndex = 0;
            int i = 0;
            bool inQuotes = false;
            char previous = Constants.EMPTY;
            while (i < statement.Length)
            {
                var ch = statement[i];
                if (ch == '"' && previous != '\\')
                {
                    inQuotes = !inQuotes;
                }
                else if (inQuotes)
                {
                }
                else
                {
                    string candidate = Utils.ValidAction(statement.Substring(i));
                    if (candidate == null && (Constants.STATEMENT_TOKENS.IndexOf(statement[i]) >= 0 ||
                                             (scriptInCSharp && (ch == '(' || ch == ')' || ch == ','))))
                    {
                        candidate = statement[i].ToString();
                    }
                    if (candidate != null)
                    {
                        if (i > startIndex)
                        {
                            string token = statement.Substring(startIndex, i - startIndex);
                            tokens.Add(token);
                        }
                        tokens.Add(candidate);
                        previous = statement[i];
                        i += candidate.Length;
                        startIndex = i;
                        continue;
                    }
                }
                previous = ch;
                i++;
            }

            //tokens = tokens.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

            if (statement.Length > startIndex)
            {
                tokens.Add(statement.Substring(startIndex));
            }

            return tokens;
        }

        public static bool IsMathFunction(string name, out string corrected)
        {
            corrected = name;
            string candidate = name[0].ToString().ToUpperInvariant() + name.Substring(1).ToLower();
            if (candidate == "Pi")
            {
                corrected = "Math.PI";
                return true;
            }

            Type mathType = typeof(System.Math);
            try
            {
                MethodInfo myMethod = mathType.GetMethod(candidate);
                if (myMethod != null)
                {
                    corrected = "Math." + candidate;
                    return true;
                }
                return false;
            }
            catch (AmbiguousMatchException)
            {
                corrected = "Math." + candidate;
                return true;
            }
        }

        static bool IsAssignment(string token)
        {
            return token == "=" || Constants.OPER_ACTIONS.Contains(token);
        }

        static int FindChar(string token, int paramStart, char ch)
        {
            if (paramStart < 0)
            {
                return token.Length;
            }

            bool inQuotes = false;
            char prev = Constants.EMPTY;
            for (int i = paramStart + 1; i < token.Length; i++)
            {
                char current = token[i];
                if (current == ch && !inQuotes)
                {
                    return i;
                }
                if (current == '"' && prev != '\\')
                {
                    inQuotes = !inQuotes;
                }
                prev = ch; 
            }

            return -1;
        }

        public static string TypeToCSString(Variable.VarType type)
        {
            switch (type)
            {
                case Variable.VarType.NUMBER: return "double";
                case Variable.VarType.STRING: return "string";
                case Variable.VarType.ARRAY: return "List<Variable>";
                case Variable.VarType.BREAK: return "break";
                case Variable.VarType.CONTINUE: return "continue";
                default: return "string";
            }
        }
        public static Type CSCSTypeToCSType(Variable.VarType type)
        {
            switch (type)
            {
                case Variable.VarType.NUMBER: return typeof(double);
                case Variable.VarType.STRING: return typeof(string);
                case Variable.VarType.ARRAY: return typeof(List<Variable>);
                default: return typeof(string);
            }
        }
#endif
    }
}
