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
        string[] m_defaultArgs;
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
        public string CSharpCode { get; set; }

        Func<Interpreter, List<string>, List<double>, List<int>, List<List<string>>, List<List<double>>, List<List<int>>,
             List<Dictionary<string, string>>, List<Dictionary<string, double>>, List<Variable>, Variable> m_compiledFunc;

        Func<Interpreter, List<string>, List<double>, List<int>, List<List<string>>, List<List<double>>, List<List<int>>,
             List<Dictionary<string, string>>, List<Dictionary<string, double>>, List<Variable>, Task<Variable>> m_compiledFuncAsync;

        static List<string> s_definitions = new List<string>();
        static List<string> s_namespaces = new List<string>();

        public static bool AsyncMode { get; set; } = false;

        public string OutputDLL { get; private set; } = "";
        public string Name { get; set; } = "";

        public string ClassHeader { get; set; } = "  public partial class Precompiler {";
        public string ClassName { get; set; } = "Precompiler";
        public bool IsStatic { get; set; } = true;

        static string STRING_VAR_ARG = "__varStr";
        static string NUMERIC_VAR_ARG = "__varNum";
        static string INT_VAR_ARG = "__varInt";
        static string STRING_ARRAY_ARG = "__varArrStr";
        static string NUMERIC_ARRAY_ARG = "__varArrNum";
        static string INT_ARRAY_ARG = "__varArrInt";
        static string STRING_MAP_ARG = "__varMapStr";
        static string NUMERIC_MAP_ARG = "__varMapNum";
        static string CSCS_VAR_ARG = "__varVar";

        static string ARGS_TEMP_VAR = "__argsTempStr";
        static string SCRIPT_TEMP_VAR = "__scriptTempVar";
        static string PARSER_TEMP_VAR = "__funcTempVar";
        static string ACTION_TEMP_VAR = "__actionTempVar";
        static string VARIABLE_TEMP_VAR = "__varTempVar";
        static string GETVAR_TEMP_VAR = "__varTempGetVar";
        static string BOOL_TEMP_VAR = "__boolTempVar";

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
            if (!s_definitions.Contains(def))
            {
                s_definitions.Add(def);
            }
        }
        public static void AddNamespace(string ns)
        {
            if (!s_namespaces.Contains(ns))
            {
                s_namespaces.Add(ns);
            }
        }
        public static void ClearDefinitions()
        {
            s_definitions.Clear();
        }
        public static void ClearNamespaces()
        {
            s_namespaces.Clear();
        }

        public Precompiler(string functionName)
        {
            m_functionName = functionName;
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

            ProcessDefaultArgs();
        }

        void ProcessDefaultArgs()
        {
            m_defaultArgs = new string[m_actualArgs.Length];
            for (int i = 0; i < m_actualArgs.Length; i++)
            {
                var arg = m_actualArgs[i];
                var parts = arg.Split('=');
                if (parts.Length < 2)
                {
                    continue;
                }
                var realArg = parts[0];
                var defValue = parts[1];
                if (defValue.StartsWith("\"") && defValue.EndsWith("\"") && defValue.Length >= 2)
                {
                    defValue = defValue.Substring(1, defValue.Length - 2);
                }
                m_defaultArgs[i] = defValue;
                m_actualArgs[i] = realArg;
            }
        }

        public string GetCSharpCode(bool scriptInCSharp = false, bool startClass = true, bool finish = true)
        {
            m_scriptInCSharp = scriptInCSharp;

            m_cscsCode = Utils.ConvertToScript(m_parentScript.InterpreterInstance, m_originalCode, out _);
            RemoveIrrelevant(m_cscsCode);

            CSharpCode = ConvertScript(startClass, finish);
            return CSharpCode;
        }

        public void Compile(bool scriptInCSharp = false, string outputDLL = "")
        {
            if (string.IsNullOrWhiteSpace(CSharpCode))
            {
                GetCSharpCode(scriptInCSharp);
            }
            var compilerParams = new CompilerParameters();

            compilerParams.TreatWarningsAsErrors = false;
            compilerParams.CompilerOptions = "/optimize";
            //compilerParams.CompilerOptions = "/debug";
            //compilerParams.IncludeDebugInformation = true;
            compilerParams.GenerateExecutable = false;
            compilerParams.GenerateInMemory = !string.IsNullOrWhiteSpace(outputDLL);
            if (!string.IsNullOrWhiteSpace(outputDLL))
            {
                if (!outputDLL.ToLower().EndsWith(".dll"))
                {
                    outputDLL += ".dll";
                }
                var absolute = Path.IsPathRooted(outputDLL);
                if (!absolute)
                {
                    var pwd = Directory.GetCurrentDirectory();
                    outputDLL = Path.Combine(pwd, outputDLL);
                }
                compilerParams.OutputAssembly = OutputDLL = outputDLL;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly asm in assemblies)
            {
                AssemblyName asmName = asm.GetName();
                if (asmName == null || string.IsNullOrWhiteSpace(asmName.CodeBase))
                {
                    continue;
                }

                var uri = new Uri(asmName.CodeBase);
                if (uri != null && File.Exists(uri.LocalPath) && !uri.LocalPath.Contains("mscorlib"))
                {
                    compilerParams.ReferencedAssemblies.Add(uri.LocalPath);
                }
            }

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
                throw new ArgumentException("Compile error: " + exc.Message, exc);
            }
        }

        Func<Interpreter, List<string>, List<double>, List<int>, List<List<string>>, List<List<double>>, List<List<int>>,
                           List<Dictionary<string, string>>, List<Dictionary<string, double>>, List<Variable>, Variable>
                            CompileAndCache(CompilerResults compile, string functionName)
        {
            Tuple<MethodCallExpression, List<ParameterExpression>> tuple =
                 CompileBase(compile, functionName);

            MethodCallExpression methodCall = tuple.Item1;
            List<ParameterExpression> paramTypes = tuple.Item2;

            var lambda =
              Expression.Lambda<Func<Interpreter, List<string>, List<double>, List<int>,
            List<List<string>>, List<List<double>>, List<List<int>>,
            List<Dictionary<string, string>>, List<Dictionary<string, double>>, List<Variable>, Variable>>(
                methodCall, paramTypes.ToArray());
            var func = lambda.Compile();

            return func;
        }

        Func<Interpreter, List<string>, List<double>, List<int>, List<List<string>>, List<List<double>>, List<List<int>>,
                   List<Dictionary<string, string>>, List<Dictionary<string, double>>, List<Variable>, Task<Variable>>
                    CompileAndCacheAsync(CompilerResults compile, string functionName)
        {
            Tuple<MethodCallExpression, List<ParameterExpression>> tuple =
                 CompileBase(compile, functionName);

            MethodCallExpression methodCall = tuple.Item1;
            List<ParameterExpression> paramTypes = tuple.Item2;

            var lambda =
              Expression.Lambda<Func<Interpreter, List<string>, List<double>, List<int>, List<List<string>>, List<List<double>>, List<List<int>>,
                                     List<Dictionary<string, string>>, List<Dictionary<string, double>>, List<Variable>, Task<Variable>>>(
                methodCall, paramTypes.ToArray());
            var func = lambda.Compile();

            return func;
        }

        Tuple<MethodCallExpression, List<ParameterExpression>>
              CompileBase(CompilerResults compile, string functionName)
        {
            Module module = compile.CompiledAssembly.GetModules()[0];
            Type mt = module.GetType("SplitAndMerge." + ClassName);

            List<ParameterExpression> paramTypes = new List<ParameterExpression>();
            paramTypes.Add(Expression.Parameter(typeof(Interpreter), "__interpreter"));
            paramTypes.Add(Expression.Parameter(typeof(List<string>), STRING_VAR_ARG));
            paramTypes.Add(Expression.Parameter(typeof(List<double>), NUMERIC_VAR_ARG));
            paramTypes.Add(Expression.Parameter(typeof(List<int>), INT_VAR_ARG));
            paramTypes.Add(Expression.Parameter(typeof(List<List<string>>), STRING_ARRAY_ARG));
            paramTypes.Add(Expression.Parameter(typeof(List<List<double>>), NUMERIC_ARRAY_ARG));
            paramTypes.Add(Expression.Parameter(typeof(List<List<int>>), INT_ARRAY_ARG));
            paramTypes.Add(Expression.Parameter(typeof(List<Dictionary<string, string>>), STRING_MAP_ARG));
            paramTypes.Add(Expression.Parameter(typeof(List<Dictionary<string, double>>), NUMERIC_MAP_ARG));
            paramTypes.Add(Expression.Parameter(typeof(List<Variable>), CSCS_VAR_ARG));
            List<Type> argTypes = new List<Type>();
            for (int i = 0; i < paramTypes.Count; i++)
            {
                argTypes.Add(paramTypes[i].Type);
            }

            MethodInfo methodInfo = mt.GetMethod(functionName, argTypes.ToArray());
            MethodCallExpression methodCall = methodInfo.IsStatic ?
                Expression.Call(methodInfo, paramTypes) :
                Expression.Call(Expression.New(mt), methodInfo, paramTypes);

            return new Tuple<MethodCallExpression, List<ParameterExpression>>(methodCall, paramTypes);
        }

        public Variable Run(Interpreter interpreter, List<string> argsStr, List<double> argsNum, List<int> argsInt,
            List<List<string>> argsArrStr, List<List<double>> argsArrNum, List<List<int>> argsArrInt,
            List<Dictionary<string, string>> argsMapStr,
            List<Dictionary<string, double>> argsMapNum, List<Variable> argsVar, bool throwExc = true)
        {
            if (m_compiledFunc == null)
            {
                // For "late bindings"...
                Compile();
            }

            Variable result = m_compiledFunc.Invoke(interpreter, argsStr, argsNum, argsInt, argsArrStr, argsArrNum, argsArrInt,
                argsMapStr, argsMapNum, argsVar);
            return result;
        }
        public Variable RunAsync(Interpreter interpreter, List<string> argsStr, List<double> argsNum, List<int> argsInt,
            List<List<string>> argsArrStr, List<List<double>> argsArrNum, List<List<int>> argsArrInt,
            List<Dictionary<string, string>> argsMapStr,
            List<Dictionary<string, double>> argsMapNum, List<Variable> argsVar, bool throwExc = true)
        {
            if (m_compiledFuncAsync == null)
            {
                // For "late bindings"...
                Compile();
            }

            var task = m_compiledFuncAsync.Invoke(interpreter, argsStr, argsNum, argsInt, argsArrStr, argsArrNum, argsArrInt,
                argsMapStr, argsMapNum, argsVar);
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
            if (resolved.StartsWith(INT_VAR_ARG, StringComparison.InvariantCulture))
            {
                return Variable.VarType.INT;
            }
            if (resolved.StartsWith(NUMERIC_ARRAY_ARG, StringComparison.InvariantCulture) &&
                paramName.Count(x => x == '[') >= 2)
            {
                return Variable.VarType.NUMBER;
            }
            if (resolved.StartsWith(INT_ARRAY_ARG, StringComparison.InvariantCulture) &&
                paramName.Count(x => x == '[') >= 2)
            {
                return Variable.VarType.INT;
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
            return m_depth + "__interpreter.AddGlobalOrLocalVariable(\"" + paramName +
                     "\", new GetVarFunction(Variable.ConvertToVariable(" + paramValue + ")));\n";
        }

        string ConvertScript(bool startClass = true, bool finish = true)
        {
            m_converted.Clear();

            int strIndex = 0;
            int numIndex = 0;
            int intIndex = 0;
            int arrStrIndex = 0;
            int arrNumIndex = 0;
            int arrIntIndex = 0;
            int mapStrIndex = 0;
            int mapNumIndex = 0;
            int varIndex = 0;
            // Create a mapping from the original function argument to the element array it is in.
            for (int i = 0; i < m_actualArgs.Length; i++)
            {
                string argName = m_actualArgs[i];
                Variable typeVar = m_argsMap[argName];
                m_paramMap[argName] =
                    typeVar.Type == Variable.VarType.STRING ? STRING_VAR_ARG + "[" + (strIndex++) + "]" :
                    typeVar.Type == Variable.VarType.NUMBER ? NUMERIC_VAR_ARG + "[" + (numIndex++) + "]" :
                    typeVar.Type == Variable.VarType.INT ? INT_VAR_ARG + "[" + (intIndex++) + "]" :
                    typeVar.Type == Variable.VarType.ARRAY_STR ? STRING_ARRAY_ARG + "[" + (arrStrIndex++) + "]" :
                    typeVar.Type == Variable.VarType.ARRAY_NUM ? NUMERIC_ARRAY_ARG + "[" + (arrNumIndex++) + "]" :
                    typeVar.Type == Variable.VarType.ARRAY_INT ? INT_ARRAY_ARG + "[" + (arrIntIndex++) + "]" :
                    typeVar.Type == Variable.VarType.MAP_STR ? STRING_MAP_ARG + "[" + (mapStrIndex++) + "]" :
                    typeVar.Type == Variable.VarType.MAP_NUM ? NUMERIC_MAP_ARG + "[" + (mapNumIndex++) + "]" :
                    typeVar.Type == Variable.VarType.VARIABLE ? CSCS_VAR_ARG + "[" + (varIndex++) + "]" :
                            "";
            }

            if (startClass)
            {
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
                m_converted.AppendLine("namespace SplitAndMerge {\n" + ClassHeader);

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
            }
            if (AsyncMode)
            {
                m_converted.AppendLine("    public " + (IsStatic ? "static " : "") + "async Task<Variable> " + m_functionName);
            }
            else
            {
                m_converted.AppendLine("    public " + (IsStatic ? "static " : "") + "Variable " + m_functionName);
            }

            m_converted.AppendLine(
                           "(Interpreter __interpreter,\n" +
                           " List<string> " + STRING_VAR_ARG + ",\n" +
                           " List<double> " + NUMERIC_VAR_ARG + ",\n" +
                           " List<int> " + INT_VAR_ARG + ",\n" +
                           " List<List<string>> " + STRING_ARRAY_ARG + ",\n" +
                           " List<List<double>> " + NUMERIC_ARRAY_ARG + ",\n" +
                           " List<List<int>> " + INT_ARRAY_ARG + ",\n" +
                           " List<Dictionary<string, string>> " + STRING_MAP_ARG + ",\n" +
                           " List<Dictionary<string, double>> " + NUMERIC_MAP_ARG + ",\n" +
                           " List<Variable> " + CSCS_VAR_ARG + ") {\n");
            m_depth = "      ";

            m_converted.AppendLine("     string " + ARGS_TEMP_VAR + "= \"\";");
            m_converted.AppendLine("     string " + ACTION_TEMP_VAR + " = \"\";");
            m_converted.AppendLine("     ParsingScript " + SCRIPT_TEMP_VAR + " = null;");
            m_converted.AppendLine("     ParserFunction " + PARSER_TEMP_VAR + " = null;");
            m_converted.AppendLine("     GetVarFunction " + GETVAR_TEMP_VAR + " = null;");
            m_converted.AppendLine("     Variable " + VARIABLE_TEMP_VAR + " = null;");
            m_converted.AppendLine("     bool " + BOOL_TEMP_VAR + " = false;");

            m_newVariables.Add(ARGS_TEMP_VAR);
            m_newVariables.Add(ACTION_TEMP_VAR);
            m_newVariables.Add(SCRIPT_TEMP_VAR);
            m_newVariables.Add(PARSER_TEMP_VAR);
            m_newVariables.Add(GETVAR_TEMP_VAR);
            m_newVariables.Add(VARIABLE_TEMP_VAR);
            m_newVariables.Add(BOOL_TEMP_VAR);

            m_statements = TokenizeScript(m_cscsCode);
            m_statementId = 0;
            while (m_statementId < m_statements.Count)
            {
                m_currentStatement = m_statements[m_statementId];
                m_nextStatement = m_statementId < m_statements.Count - 1 ? m_statements[m_statementId + 1] : "";
                var current = m_converted.ToString();
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

            m_converted.AppendLine("\n    }");
            if (finish)
            {
                m_converted.AppendLine("}}");
            }
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

        string ConvertTokenIfNeeded(string token, string first = "")
        {
            string result = token;
            string functionName = GetFunctionName(token, out string suffix, out bool isArray).ToLower();
            if (!suffix.Contains('.') && m_argsMap.TryGetValue(functionName, out _))
            {
                string actualName = m_paramMap[functionName];
                result = " " + actualName + ReplaceArgsInString(suffix);
                if (first == "int" || first == "long")
                {
                    result = "(" + first + ")" + result;
                }
            }
            return result;
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
            m_tokenId = 0;
            while (m_tokenId < tokens.Count)
            {
                string token = tokens[m_tokenId];
                var converted = ConvertTokenIfNeeded(token);
                result += converted;
                m_tokenId++;
            }
            if (!result.EndsWith("{") && (!nextStatement.StartsWith("{") || !result.EndsWith(")")))
            {
                result += ";\n";
            }

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
            string defaultReturn = VARIABLE_TEMP_VAR;
            string suffix = "";

            string paramName = tokens.Count > 0 ? GetFunctionName(tokens[0], out suffix, out bool isArray).Trim() : "";
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

            var converted = ConvertTokenIfNeeded(toReturn);

            if (!converted.Contains("Variable.EmptyInstance"))
            {
                converted = "new Variable(" + converted + ")";
            }
            if (!AsyncMode)
            {
                return m_depth + "return " + converted + ";\n";
            }

            string result = m_depth + VARIABLE_TEMP_VAR + " = " + converted + ";\n";
            result += m_depth + "return " + VARIABLE_TEMP_VAR + ";\n";
            return result;
        }

        bool ProcessForStatement(string statement, List<string> tokens, ref string converted)
        {
            string functionName = GetFunctionName(statement, out string suffix, out bool isArray).Trim();
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

            string varName = GetFunctionName(suffix.Substring(1), out string rest, out isArray);

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
            string varName = GetFunctionName(exceptionVar.Substring(1), out string suffix, out bool isArray);

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

        string TryProcessArrayToken(string token, ref string extra)
        {
            var start = token.IndexOf("[");
            if (start <= 0)
            {
                return token;
            }
            var arrayName = token.Substring(0, start);

            var end = token.LastIndexOf("]");
            var arrayPart = token.Substring(start + 1, end - start - 1);

            var arrayIndex = EvaluateToken(arrayPart);
            string result = "Utils.ExtendArrayIfNeeded(" + arrayName + ", (int)" + arrayIndex + ", Variable.EmptyInstance); \n";
            result += m_depth + token.Substring(0, start + 1) + "(int)" + arrayIndex + token.Substring(end, token.Length - end);

            extra += m_depth + "if (" + BOOL_TEMP_VAR + ") " + "__interpreter.AddGlobalOrLocalVariable(\"" + arrayName +
                     "\", new GetVarFunction(Variable.ConvertToVariable(__varTempVar)));\n"; ;
            extra += m_depth + "else __interpreter.AddGlobalOrLocalVariable(\"" + arrayName +
                     "\", new GetVarFunction(Variable.ConvertToVariable(" + arrayName + ")));\n";
            return result;
        }

        string EvaluateToken(string token)
        {
            int tokenId = 0;
            string result = "";
            bool newVarAdded = false;
            var subtokens = Utils.SplitToken(token);
            int counter = 0;
            foreach (var sub in subtokens)
            {
                List<string> tokens = new List<string>() { sub };
                if (++counter % 2 == 1)
                {
                    ProcessToken(tokens, ref tokenId, ref result, ref newVarAdded);
                }
                else
                {
                    result += sub;
                }
            }
            return result;
        }

        public string GetExpressionType(List<string> tokens, string functionName, ref bool addNewVarDef)
        {
            if (!tokens[0].Contains("["))
            {
                return m_depth + "var " + functionName;
            }
            bool firstString = tokens[0].Contains("\"");
            string expr = m_depth + BOOL_TEMP_VAR + " = false;\n";

            string param = GetTokenType(tokens);
            if (functionName != tokens[0])
            {
                expr += GetCSCSVariable(functionName);
                expr += m_depth + "List<Variable> " + functionName + " = null;\n";
                expr += m_depth + "if (__varTempVar != null && __varTempVar.Tuple != null) {\n" + m_depth + m_depth +
                    functionName + "= __varTempVar.Tuple; " + BOOL_TEMP_VAR + " = true;\n" + m_depth +
                    "} else " + functionName + " = new List<Variable> ();\n";
            }
            else if (!firstString)
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
            string extra = "";
            expr += m_depth + TryProcessArrayToken(tokens[m_tokenId], ref extra);
            while (++m_tokenId < tokens.Count)
            {
                ProcessToken(tokens, ref m_tokenId, ref expr, ref addNewVarDef);
            }
            expr += extra;

            m_tokenId = tokens.Count;
            addNewVarDef = false;
            return expr;
        }

        int DecisionToken(List<string> tokens)
        {
            for (int i = 0; i < tokens.Count - 1; i++)
            {
                if (tokens[i] == "=")
                {
                    return i + 1;
                }
            }
            return -1;
        }

        string GetTokenType(List<string> tokens)
        {
            int desTokenId = DecisionToken(tokens);
            if (desTokenId < 0)
            {
                return "double";
            }

            var lastToken = tokens[desTokenId].ToUpper();
            bool needsConvert = lastToken.StartsWith("STRING(");
            if (lastToken.StartsWith("\"") || needsConvert || lastToken.Contains(".TOSTRING("))
            {
                return "string";
            }
            return "double";
        }

        void ProcessToken(List<string> tokens, ref int id, ref string result, ref bool newVarAdded)
        {
            string token = tokens[id].Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            string functionName = GetFunctionName(token, out string suffix, out bool isArray);
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

        string GetFunctionName(string token, out string suffix, out bool isArray)
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
            string restStr = m_tokenId < tokens.Count ? tokens[m_tokenId] : "";
            int paramStart = restStr.IndexOf('(');
            int paramEnd = FindChar(restStr, paramStart, ')');
            while (paramEnd < 0 && m_tokenId < tokens.Count && ++m_tokenId < tokens.Count)
            {
                restStr += tokens[m_tokenId];
            }
            paramEnd = paramStart < 0 ? restStr.Length : paramStart;

            string functionName = paramStart < 0 ? restStr : restStr.Substring(0, paramStart);
            string argsStr = "";
            if (paramStart >= 0)
            {
                paramEnd = paramEnd <= paramStart ? restStr.Length : paramEnd;
                ParsingScript tmpScript = new ParsingScript(m_parentScript.InterpreterInstance, restStr.Substring(paramStart, restStr.Length - paramStart));
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

            sb.AppendLine(GetCSCSFunction(argsStr, functionName, ch));

            token = sb.ToString();
            if (tokens.Count >= 3 && tokens[1] == "=" && !string.IsNullOrWhiteSpace(result))
            {
                var type = GetTokenType(tokens);
                var last = type == "string" ? VARIABLE_TEMP_VAR + ".AsString()" : VARIABLE_TEMP_VAR;
                result = m_depth + token + result + last + ";\n";
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
            else if (functionName == "string" && !string.IsNullOrWhiteSpace(arguments))
            {
                var eval = EvaluateToken(arguments);
                return "new Variable(Convert.ToString(" + eval + (eval.EndsWith(")") ? "" : "))") + ";";
            }
            else if (functionName == "int" && !string.IsNullOrWhiteSpace(arguments))
            {
                var eval = EvaluateToken(arguments);
                return "new Variable(Convert.ToInt32(" + eval + (eval.EndsWith(")") ? "" : "))") + ";";
            }
            else if (functionName == "long" && !string.IsNullOrWhiteSpace(arguments))
            {
                var eval = EvaluateToken(arguments);
                return "new Variable(Convert.ToInt64(" + eval + (eval.EndsWith(")") ? "" : "))") + ";";
            }
            else if (functionName == "bool" && !string.IsNullOrWhiteSpace(arguments))
            {
                var eval = EvaluateToken(arguments);
                return "new Variable(Convert.ToBoolean(" + eval + (eval.EndsWith(")") ? "" : "))") + ";";
            }
            else if (functionName == "double" && !string.IsNullOrWhiteSpace(arguments))
            {
                var eval = EvaluateToken(arguments);
                return "new Variable(Convert.ToDouble(" + eval + (eval.EndsWith(")") ? "" : "))") + ";";
            }
            return "";
        }

        //expr += m_depth + VARIABLE_TEMP_VAR + " = ParserFunction.GetVariable(\"" + functionName + "\");\n";
        string GetCSCSVariable(string functionName, string argsStr = "", char ch = '(')
        {
            string result = m_depth + VARIABLE_TEMP_VAR + " = __interpreter.GetVariableValue(\"" + functionName + "\");\n";
            return result;
        }

        string GetCSCSFunction(string argsStr, string functionName, char ch = '(')
        {
            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(argsStr) && argsStr.Last() == '"' && argsStr.First() == '"')
            {
                argsStr = "\\\"" + argsStr.Substring(1, argsStr.Length - 2) + "\\\"";
            }

            sb.AppendLine(m_depth + ARGS_TEMP_VAR + " =\"" + argsStr + "\";");
            sb.AppendLine(m_depth + SCRIPT_TEMP_VAR + " = new ParsingScript(__interpreter, " + ARGS_TEMP_VAR + ", true);");
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

                string paramName = GetFunctionName(token, out string suffix, out bool isArray);

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
                previous = i > 0 ? scriptText[i - 1] : previous;

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
            if (name.StartsWith("Math."))
            {
                return true;
            }

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

        static string GetMainDLLFunction(ParsingScript script, int id, out string funcName, bool scriptInCSharp = true)
        {
            Utils.GetCompiledArgs(script, out string funcReturn, out funcName);
            Precompiler.RegisterReturnType(funcName, funcReturn);

            script.MoveForwardIf(Constants.START_ARG, Constants.SPACE);

            int endArgs = script.FindFirstOf(Constants.END_ARG.ToString());
            if (endArgs < 0)
            {
                throw new ArgumentException("Couldn't extract function signature");
            }

            string argStr = script.Substr(script.Pointer, endArgs - script.Pointer);
            var args = ImplementArgExtractFunction(argStr, DLLConst.GetArgsMethod(id), out Dictionary<string, Variable> dict, out string funcBody);

            string[] funcArgs = args.Select(element => element.Trim()).ToArray();
            script.Pointer = endArgs + 1;

            script.MoveForwardIf(Constants.START_GROUP, Constants.SPACE);
            script.ParentOffset = script.Pointer;

            string body = Utils.GetBodyBetween(script, Constants.START_GROUP, Constants.END_GROUP);

            Precompiler precompiler = new Precompiler(DLLConst.GetWorkerMethod(id), funcArgs, dict, body, script);
            precompiler.ClassName = DLLConst.ClassName;
            precompiler.ClassHeader = DLLConst.ClassHeader;
            precompiler.IsStatic = false;
            var cscode = precompiler.GetCSharpCode(scriptInCSharp, id == 1, false);

            return cscode + funcBody;
        }

        static string CreateAuxFunctions(Dictionary<string, string> workDir, Dictionary<string, string> argDict)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("\n\n  public int NumberWorkMethods() {\n" +
                      "    return " + workDir.Count + ";}\n\n");

            sb.Append("  public List<string> MethodNames() {\n");
            sb.Append("    var res = new List<string>();\n");
            foreach (var entry in workDir)
            {
                var funcName = entry.Key;
                var dllFuncName = entry.Value;
                sb.Append("    res.Add(\"" + funcName + "\");\n");
            }
            sb.Append("    return res;\n }\n\n");

            sb.Append("  public Func<Interpreter, List<string>, List<double>, List<int>, List<List<string>>, List<List<double>>, List<List<int>>," +
            "List<Dictionary<string, string>>, List<Dictionary<string, double>>, List<Variable>, Variable> GetWorkFunction(string name) {\n");
            foreach (var entry in workDir)
            {
                var funcName = entry.Key;
                var dllFuncName = entry.Value;
                sb.Append("    if (name == \"" + funcName + "\") { return " + dllFuncName + ";}\n");
            }
            sb.Append("    return null;\n }\n\n");

            sb.Append("  public Func<int, ArgData> GetArgFunction(string name) {\n");
            foreach (var entry in argDict)
            {
                var funcName = entry.Key;
                var dllFuncName = entry.Value;
                sb.Append("    if (name == \"" + funcName + "\") { return " + dllFuncName + ";}\n");
            }
            sb.Append("    return null;\n }\n\n");

            // Implement not needed:
            for (int i = workDir.Count; i < DLLConst.MaxWorkMethods; i++)
            {
                sb.Append("  public Variable " + DLLConst.GetWorkerMethod(i+1) + "(Interpreter __interpreter, List<string> __varStr,List<double> __varNum," +
                    " List<int> __varInt,List<List<string>> __varArrStr,List<List<double>> __varArrNum,List<List<int>> __varArrInt,"+
                    " List<Dictionary<string, string>> __varMapStr,List<Dictionary<string, double>> __varMapNum,List<Variable> __varVar) {\n" +
                    "    return null;}\n");
                sb.Append("  public ArgData " + DLLConst.GetArgsMethod(i+1) + "(int id) {\n" +
                    "    return null;}\n");
            }

            return sb.ToString();
        }

        public static Precompiler ImplementCustomDLL(ParsingScript script, bool scriptInCSharp = true, bool createDLL = true)
        {
            var workDict = new Dictionary<string, string>();
            var argDict = new Dictionary<string, string>();

            string dllname = "";
            string cscode = "";
            for (int id = 1; id < DLLConst.MaxWorkMethods + 1; id++)
            {
                cscode += GetMainDLLFunction(script, id, out string funcName, scriptInCSharp) + "\n";
                dllname = string.IsNullOrWhiteSpace(dllname) ? funcName : dllname;
                workDict[funcName] = DLLConst.GetWorkerMethod(id);
                argDict[funcName] = DLLConst.GetArgsMethod(id);

                int pos = script.Pointer;
                script.GoToNextStatement();
                if (script.Rest.StartsWith(Constants.DLL_FUNCTION + " "))
                {
                    script.Pointer += Constants.DLL_FUNCTION.Length + 1;
                }
                else
                {
                    script.Pointer = pos;
                    break;
                }
            }

            Precompiler precompiler = new Precompiler(DLLConst.GetWorkerMethod());
            precompiler.ClassName = DLLConst.ClassName;
            precompiler.CSharpCode = cscode;

            while (true)
            {
                int pos = script.Pointer;
                script.GoToNextStatement();
                if (script.Rest.StartsWith(Constants.DLL_SUB + " "))
                {
                    script.Pointer += Constants.DLL_SUB.Length + 1;
                    var body2 = Utils.GetBodyBetween(script, Constants.START_GROUP, Constants.END_GROUP, '\0', true);
                    if (body2.EndsWith(";"))
                    {
                        body2 = body2.Substring(0, body2.Length - 1);
                    }
                    precompiler.CSharpCode += "\n  " + body2 + "\n\n";
                }
                else
                {
                    script.Pointer = pos;
                    break;
                }
            }

            var auxFuncs = CreateAuxFunctions(workDict, argDict);
            precompiler.CSharpCode += auxFuncs + "\n  }\n}";

            precompiler.Compile(scriptInCSharp, createDLL ? dllname : "");

            return precompiler;
        }

        static List<string> ImplementArgExtractFunction(string argStr, string funcName, out Dictionary<string, Variable> dict, out string funcBody)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("  public ArgData " + funcName + "(int id) {\n" +
                      "    ArgData arg = new ArgData();\n");

            List<string> args = Utils.GetCompiledArgs(argStr);

            dict = new Dictionary<string, Variable>(args.Count);
            var sep = new char[] { ' ' };
            for (int i = 0; i < args.Count; i++)
            {
                var arg1 = args[i].ToLower().Trim();
                string[] pair = arg1.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                var argType = pair[0];
                var fullArgName = pair[pair.Length - 1];
                var argParts = fullArgName.Split('=');
                var argName = argParts[0];
                Variable.VarType type = pair.Length > 1 ? Constants.StringToType(argType) : Variable.VarType.STRING;
                var defPart = "";
                if (argParts.Length >= 2)
                {
                    var defValue = argParts[1];
                    if (defValue != "null")
                    {
                        defPart = "arg.defValue = new Variable(" + defValue + "); ";
                    }
                    else
                    {
                        defPart = "arg.defValue = new Variable(type); ";
                        if (type == Variable.VarType.STRING)
                        {
                            defPart += "arg.defValue.String = null; ";
                        }
                    }
                }
                sb.Append("      if (id == " + i + ") {\n arg.exists = true; arg.name = \"" + argName +
                    "\"; arg.type = Variable.VarType." + type.ToString() + ";\n  " +
                    defPart + "\n }\n");
                dict.Add(argName, new Variable(type));
                args[i] = fullArgName;
            }
            sb.Append("return arg;\n    }");
            funcBody = sb.ToString();

            return args;
        }

        public static void ExtractArgsFromDLL(ICustomDLL dll, ref ImportDLLFunction.DLLData data)
        {
            data.functionMap = new Dictionary<string, ImportDLLFunction.DLLFunctionData>();
            var names = dll.MethodNames();
            foreach(var name in names)
            {
                var fd = new ImportDLLFunction.DLLFunctionData();
                fd.name = name;
                fd.workMethod = dll.GetWorkFunction(name);
                var argMethod = dll.GetArgFunction(name);

                var argsList = new List<string>();
                var defArgsList = new List<Variable>();
                var argsMap = new Dictionary<string, Variable>();
                for (int id = 0; ; id++)
                {
                    var argData = argMethod(id);
                    if (!argData.exists)
                    {
                        break;
                    }
                    argsList.Add(argData.name);
                    defArgsList.Add(argData.defValue);
                    argsMap[argData.name] = new Variable(argData.type);
                }
                fd.args = argsList.ToArray();
                fd.defArgs = defArgsList.ToArray();
                fd.argsMap = argsMap;
                data.functionMap[name.ToLower()] = fd;
            }
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
