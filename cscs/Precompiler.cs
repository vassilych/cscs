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
        // Locals assigned a collection literal. Their elements are Variables, so an index
        // read of one needs converting before it can take part in C# arithmetic.
        HashSet<string> m_collectionLocals = new HashSet<string>();
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

        // Compiled code normally mirrors every variable write back into the interpreter so
        // that any CSCS function it calls sees current values (CSCS call arguments are
        // passed as a string and re-parsed by the interpreter, so they resolve by name).
        // That write-back costs far more than the arithmetic around it -- roughly 700x for
        // a tight numeric loop -- and is pure overhead for a function that never calls back
        // into the interpreter. ConvertScript() therefore converts once with the write-back
        // suppressed; if that pass turns out to emit an interpreter call, it converts again
        // with the write-back restored.
        bool m_emitInterpreterSync = true;

        // A CSCS call inside an expression emits *statements*, which cannot sit in the
        // middle of one. They are collected here, emitted before the statement, and the
        // expression keeps a reference to the value they produced.
        string m_statementPrelude = "";
        string m_lastPrelude = "";
        int m_tempVarId;
        bool m_usesInterpreter;

        ParsingScript m_parentScript;
        public string CSharpCode { get; set; }

        Func<Interpreter, List<string>, List<double>, List<int>, List<List<string>>, List<List<double>>, List<List<int>>,
             List<Dictionary<string, string>>, List<Dictionary<string, double>>, List<Variable>, Variable> m_compiledFunc;

        Func<Interpreter, List<string>, List<double>, List<int>, List<List<string>>, List<List<double>>, List<List<int>>,
             List<Dictionary<string, string>>, List<Dictionary<string, double>>, List<Variable>, Task<Variable>> m_compiledFuncAsync;

        static List<string> s_definitions = new List<string>();
        static List<string> s_namespaces = new List<string>();

        public static bool AsyncMode { get; set; } = false;

        /// <summary>
        /// When translation to C# fails, register the cfunction as an ordinary interpreted
        /// function instead of letting the declaration throw.
        ///
        /// The translator covers a subset of CSCS, so without this a script using anything
        /// outside that subset simply dies at the declaration. Falling back means every
        /// cfunction runs -- the unsupported ones merely run at interpreted speed -- and a
        /// gap in the translator degrades performance instead of breaking the script.
        /// Set false to have compile failures throw, which is what the tests want.
        /// </summary>
        public static bool FallbackToInterpreter { get; set; } = true;

        static readonly Dictionary<string, string> s_fallbacks =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Functions that could not be compiled, mapped to why.</summary>
        public static IReadOnlyDictionary<string, string> Fallbacks
        {
            get { lock (s_fallbacks) { return new Dictionary<string, string>(s_fallbacks); } }
        }

        public static bool DidFallBack(string functionName)
        {
            lock (s_fallbacks) { return s_fallbacks.ContainsKey(functionName); }
        }

        public static void ClearFallbacks()
        {
            lock (s_fallbacks) { s_fallbacks.Clear(); }
        }

        internal static void RecordFallback(string functionName, string reason)
        {
            lock (s_fallbacks) { s_fallbacks[functionName] = reason; }
        }

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

        /// <summary>Extra class-level definitions injected into generated code.</summary>
        public static IReadOnlyList<string> Definitions { get { return s_definitions; } }

        /// <summary>Extra namespaces injected into generated code.</summary>
        public static IReadOnlyList<string> Namespaces { get { return s_namespaces; } }

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

            // First pass: assume this function never calls back into the interpreter and
            // leave out the per-assignment write-back.
            m_emitInterpreterSync = false;
            m_usesInterpreter = false;
            CSharpCode = ConvertScript(startClass, finish);

            if (m_usesInterpreter)
            {
                // The assumption was wrong: something in the body resolved to a CSCS
                // function or variable, which reads values by name out of the interpreter.
                // Convert again, this time keeping the write-back.
                m_emitInterpreterSync = true;
                CSharpCode = ConvertScript(startClass, finish);
            }

            return CSharpCode;
        }

        public void Compile(bool scriptInCSharp = false, string outputDLL = "")
        {
            // An implementation compiled ahead of time makes code generation unnecessary --
            // and on AOT targets such as iOS it is the only way this can work at all.
            if (!AsyncMode && string.IsNullOrWhiteSpace(outputDLL) &&
                PrecompiledRegistry.TryGet(m_functionName, out var precompiled))
            {
                m_compiledFunc = (i, s1, n, i2, a1, a2, a3, m1, m2, v) =>
                    precompiled(i, s1, n, i2, a1, a2, a3, m1, m2, v);
                return;
            }

            if (AotGenerator.Collecting)
            {
                // Capture the method on its own (no class wrapper) so several functions can
                // be emitted into one generated file, then fall through and compile as usual.
                AotGenerator.Collect(m_functionName, GetCSharpCode(scriptInCSharp, false, false));
                CSharpCode = null;
            }

            if (string.IsNullOrWhiteSpace(CSharpCode))
            {
                GetCSharpCode(scriptInCSharp);
            }
            var outDll = "";
            if (!string.IsNullOrWhiteSpace(outputDLL))
            {
                if (!outputDLL.ToLower().EndsWith(".dll"))
                {
                    outputDLL += ".dll";
                }
                if (!Path.IsPathRooted(outputDLL))
                {
                    outputDLL = Path.Combine(Directory.GetCurrentDirectory(), outputDLL);
                }
                outDll = OutputDLL = outputDLL;
            }

            // RoslynCompiler appends a hash of the generated source, so the assembly name
            // is stable across runs and the compiled result can be cached.
            Assembly compiledAssembly = RoslynCompiler.Compile(
                CSharpCode, "CscsPrecompiled_" + m_functionName, outDll);

            try
            {
                if (AsyncMode)
                {
                    m_compiledFuncAsync = CompileAndCacheAsync(compiledAssembly, m_functionName);
                }
                else
                {
                    m_compiledFunc = CompileAndCache(compiledAssembly, m_functionName);
                }
            }
            catch (Exception exc)
            {
                throw new ArgumentException("Compile error: " + exc.Message, exc);
            }
        }

        Func<Interpreter, List<string>, List<double>, List<int>, List<List<string>>, List<List<double>>, List<List<int>>,
                           List<Dictionary<string, string>>, List<Dictionary<string, double>>, List<Variable>, Variable>
                            CompileAndCache(Assembly compiledAssembly, string functionName)
        {
            Tuple<MethodCallExpression, List<ParameterExpression>> tuple =
                 CompileBase(compiledAssembly, functionName);

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
                    CompileAndCacheAsync(Assembly compiledAssembly, string functionName)
        {
            Tuple<MethodCallExpression, List<ParameterExpression>> tuple =
                 CompileBase(compiledAssembly, functionName);

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
              CompileBase(Assembly compiledAssembly, string functionName)
        {
            Module module = compiledAssembly.GetModules()[0];
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
            if (!m_emitInterpreterSync)
            {
                return "";
            }
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
            // ConvertScript may run twice (see GetCSharpCode), so every piece of state it
            // accumulates has to start empty.
            m_newVariables.Clear();
            m_collectionLocals.Clear();
            m_definitionsMap.Clear();
            m_paramMap.Clear();
            m_lastStatementReturn = false;
            m_knownExpression = false;
            m_statementPrelude = "";
            m_lastPrelude = "";
            m_tempVarId = 0;

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
                // "x = {}" carries no type, so the C# declaration is deferred until a later
                // statement reveals whether x is a list or a map (see m_definitionsMap).
                // That only ever happens for indexed assignment, so for anything else --
                // x.Add(..), x.Size -- the variable would otherwise never exist at all.
                // Registering an empty CSCS variable here keeps those working through the
                // interpreter; if the deferred declaration does arrive it is inserted above
                // this line.
                string varName = statement.Substring(0, specialIndex);
                m_definitionsMap[varName] = m_converted.Length;
                m_converted.Append(m_depth + "__interpreter.AddGlobalOrLocalVariable(\"" + varName +
                    "\", new GetVarFunction(new Variable(Variable.VarType.ARRAY)));\n");
                m_usesInterpreter = true;
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
            if (string.IsNullOrWhiteSpace(statement))
            {
                return "";
            }

            // Before ProcessSpecialCases, which would swallow "x = {}" and defer the
            // declaration until some later statement reveals a type. Building the collection
            // here instead gives it a real C# local, so .Add and .Size compile against it.
            var emptyLiteral = TryBuildLiteralAssignment(statement, addNewVars);
            if (emptyLiteral != null)
            {
                return emptyLiteral;
            }

            if (ProcessSpecialCases(statement))
            {
                return "";
            }

            // CSCS spells it "elif"; C# needs "else if". Rewrite before dispatching, because
            // the branches below resolve the condition but copy the leading keyword through
            // untouched, which produced "elif(...)" and would not compile.
            // CSCS throws a string; C# needs an Exception. Emitted here rather than in the
            // token loop, where "throw" is a reserved word and gets copied through verbatim.
            if (statement.StartsWith(Constants.THROW, StringComparison.OrdinalIgnoreCase) &&
                (statement.Length == Constants.THROW.Length ||
                 !char.IsLetterOrDigit(statement[Constants.THROW.Length])))
            {
                var thrown = statement.Substring(Constants.THROW.Length).Trim().TrimEnd(';').Trim();
                if (thrown.Length > 1 && thrown[0] == '(' && FindMatchingParen(thrown, 0) == thrown.Length - 1)
                {
                    thrown = thrown.Substring(1, thrown.Length - 2).Trim();
                }
                if (string.IsNullOrWhiteSpace(thrown))
                {
                    return m_depth + "throw new ArgumentException(\"\");\n";
                }
                return m_depth + "throw new ArgumentException(Variable.ConvertToVariable(" +
                    ReplaceArgsInString(thrown) + ").AsString());\n";
            }

            if (statement.StartsWith(Constants.ELSE_IF, StringComparison.OrdinalIgnoreCase) &&
                (statement.Length == Constants.ELSE_IF.Length ||
                 !char.IsLetterOrDigit(statement[Constants.ELSE_IF.Length])))
            {
                statement = "else if" + statement.Substring(Constants.ELSE_IF.Length);
            }
            // "a = {1,2,3}" / "m = {\"k\":\"v\"}": handled here rather than in the token loop,
            // because an assignment does not reach ProcessFunction and the literal would be
            // copied through verbatim as a C# array initialiser, which does not compile.
            var literalAssignment = TryBuildLiteralAssignment(statement, addNewVars);
            if (literalAssignment != null)
            {
                return literalAssignment;
            }

            // "a[1] = 99" / "m[\"k\"] = 1": also an assignment, so it never reaches the
            // token loop's array handling either.
            var indexedAssignment = TryBuildIndexedAssignment(statement, addNewVars);
            if (indexedAssignment != null)
            {
                return indexedAssignment;
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
            if (ProcessSwitchStatement(statement, ref result))
            {
                return result;
            }

            m_knownExpression = IsKnownExpression(tokens);

            if (m_knownExpression && tokens.Count > 1)
            {
                result = m_depth;
                bool isAssignment = IsAssignment(tokens[1]);
                // tokens[0] is only a plain declaration target when this is an assignment to
                // something that isn't a function argument. In every other case (an operator
                // such as +, -, <, or an assignment to an argument) it is part of an
                // expression and has to be resolved like any other token.
                string lhsName = GetFunctionName(tokens[0], out _, out _).Trim();
                bool lhsIsArgument = m_paramMap.ContainsKey(lhsName);

                string lhs = isAssignment ? ConvertTokenIfNeeded(tokens[0]) : ReplaceArgsInString(tokens[0]);
                string rhs = ProcessRHS(tokens);

                if (tokens[1] == "=" && !m_newVariables.Contains(tokens[0]) && !lhsIsArgument)
                {
                    // Declared double rather than var: CSCS has no integer type, so a
                    // variable seeded from a literal like 0 must not turn a later "/" into an
                    // integer division. A comparison or logical expression produces a bool,
                    // though, so those keep var.
                    result += YieldsBool(rhs) ? "var " : "double ";
                    m_newVariables.Add(tokens[0]);
                }
                if (!rhs.Contains(";"))
                {
                    result += lhs + tokens[1] + rhs;
                }
                else
                {
                    result += statement;
                }

                if (nextStatement == ";" || !"(){}[]".Contains(statement.Last()))
                {
                    result += ";\n";
                }
                if (isAssignment)
                {
                    result += RegisterVariableString(tokens[0], lhsIsArgument ? lhs.Trim() : "");
                }

                return result;
            }
            // Only tokens with call parentheses can be math calls. A lone token containing
            // just a comma -- an array literal like {1,2,3} -- is not one.
            if (m_knownExpression && tokens.Count == 1 && tokens[0].Contains('('))
            {
                result = ReplaceMathArgs(tokens[0]);
                return result;
            }

            // ProcessStatement re-enters itself for sub-expressions, so the prelude has to
            // be scoped rather than shared.
            var outerPrelude = m_statementPrelude;
            m_statementPrelude = "";

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

            m_lastPrelude = m_statementPrelude;
            m_statementPrelude = outerPrelude;
            result = m_lastPrelude + result;

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
                string mappedVar;
                result += m_paramMap.TryGetValue(statementVars[i], out mappedVar) &&
                          !string.IsNullOrEmpty(mappedVar) ?
                    RegisterVariableString(statementVars[i], mappedVar) :
                    RegisterVariableString(statementVars[i], statementVars[i]);
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
                // This branch calls ProcessToken directly rather than going through
                // ProcessStatement, so it has to flush the hoisted call statements itself --
                // otherwise the expression references a temp that was never declared.
                var outerPrelude = m_statementPrelude;
                m_statementPrelude = "";
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
                var prelude = m_statementPrelude;
                m_statementPrelude = outerPrelude;
                converted += prelude + result + CreateReturnStatement(token);
                return true;
            }

            // Mirrors the tokens.Count == 3 branch above: ProcessRHS() dispatches on
            // m_knownExpression, so it has to be computed for this statement rather than
            // left over from whichever statement was processed previously.
            m_knownExpression = IsKnownExpression(tokens);
            m_lastPrelude = "";
            string returnToken = ProcessRHS(tokens);

            // A returned expression containing a CSCS call comes back as "<hoisted
            // statements><expression>". Emit the statements first and return the expression,
            // rather than returning the shared temp and discarding the rest of the
            // expression -- which is what "return helper(n) + 1;" used to do to the "+ 1".
            if (!string.IsNullOrEmpty(m_lastPrelude) && returnToken.StartsWith(m_lastPrelude))
            {
                var expression = returnToken.Substring(m_lastPrelude.Length);
                converted = m_lastPrelude + CreateReturnStatement(expression);
                return true;
            }

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
                // ConvertToVariable rather than new Variable(...): it accepts a Variable, a
                // string, a number or a bool, so the returned expression's type no longer has
                // to be guessed. new Variable(...) had no overload for an existing Variable,
                // which is what stopped "return a[1];" from compiling.
                converted = "Variable.ConvertToVariable(" + converted + ")";
            }
            if (!AsyncMode)
            {
                return m_depth + "return " + converted + ";\n";
            }

            string result = m_depth + VARIABLE_TEMP_VAR + " = " + converted + ";\n";
            result += m_depth + "return " + VARIABLE_TEMP_VAR + ";\n";
            return result;
        }

        /// <summary>
        /// Translates a CSCS switch. C# forbids falling through from one non-empty case to
        /// the next, and CSCS requires it -- "case 1:" with no break runs case 2, case 3 and
        /// default in turn -- so a C# switch cannot express this. Instead:
        ///
        ///   do {
        ///     var  __swValN   = &lt;expression&gt;;
        ///     bool __swMatchN = false;
        ///     if (__swMatchN || __swValN == &lt;label&gt;) { __swMatchN = true; ...body... }
        ///     ...
        ///     { ...default body... }
        ///   } while (false);
        ///
        /// The flag carries the fall-through, the do/while gives "break" something to break
        /// out of, and default runs unless an earlier break left the block -- which is what
        /// CSCS does whether it was reached by falling through or by matching nothing.
        /// </summary>
        bool ProcessSwitchStatement(string statement, ref string converted)
        {
            var name = GetFunctionName(statement, out string suffix, out _).Trim();
            if (!name.Equals(Constants.SWITCH, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(suffix) || suffix[0] != Constants.START_ARG)
            {
                return false;
            }
            int exprEnd = FindMatchingParen(suffix, 0);
            if (exprEnd < 0)
            {
                return false;
            }
            var switchExpr = suffix.Substring(1, exprEnd - 1).Trim();

            int id = m_statementId + 1;
            while (id < m_statements.Count && (m_statements[id] == ";" ||
                   string.IsNullOrWhiteSpace(m_statements[id])))
            {
                id++;
            }
            if (id >= m_statements.Count || m_statements[id].Trim() != "{")
            {
                return false;
            }
            id++;

            var body = new List<string>();
            int depth = 1;
            while (id < m_statements.Count)
            {
                var current = m_statements[id].Trim();
                if (current == "{") { depth++; }
                else if (current == "}")
                {
                    depth--;
                    if (depth == 0) { break; }
                }
                body.Add(m_statements[id]);
                id++;
            }
            if (depth != 0)
            {
                return false;
            }

            // "continue" would leave the do/while rather than the enclosing loop, so those
            // switches keep the interpreter instead of being translated wrongly.
            foreach (var line in body)
            {
                if (line.Trim() == Constants.CONTINUE)
                {
                    return false;
                }
            }

            // In CSCS a "break" inside a switch exits the *enclosing loop*, not just the
            // switch: for (i=0;i<4;i++) { switch(i) { case 0: ..; break; } } stops after the
            // first iteration. The do/while below gives break a nearer target, which would
            // quietly change that, so a breaking switch inside a function that loops is left
            // to the interpreter. With no loop around it, both readings agree.
            if (ContainsWord(string.Join(";", body), Constants.BREAK) &&
                (m_cscsCode.Contains(Constants.FOR + "(") ||
                 m_cscsCode.Contains(Constants.WHILE + "(")))
            {
                return false;
            }

            // Group the statements into clauses, in order.
            var labels = new List<string>();          // null marks the default clause
            var bodies = new List<List<string>>();
            foreach (var line in body)
            {
                var trimmed = line.Trim();
                if (trimmed == ";" || string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }
                string label;
                string rest;
                if (TrySplitClause(trimmed, out label, out rest))
                {
                    labels.Add(label);
                    bodies.Add(new List<string>());
                    if (!string.IsNullOrWhiteSpace(rest))
                    {
                        bodies[bodies.Count - 1].Add(rest);
                    }
                    continue;
                }
                if (bodies.Count == 0)
                {
                    return false;      // a statement before any case label
                }
                bodies[bodies.Count - 1].Add(line);
            }
            if (labels.Count == 0)
            {
                return false;
            }
            // default is emitted last and unconditionally, so it has to be last in the source.
            for (int i = 0; i < labels.Count - 1; i++)
            {
                if (labels[i] == null)
                {
                    return false;
                }
            }

            var id2 = ++m_tempVarId;
            var valueVar = "__swVal" + id2;
            var matchVar = "__swMatch" + id2;
            var sb = new StringBuilder();
            var outer = m_depth;

            sb.Append(outer + "do {\n");
            sb.Append(outer + "  var " + valueVar + " = " + ReplaceArgsInString(switchExpr) + ";\n");
            sb.Append(outer + "  bool " + matchVar + " = false;\n");

            for (int i = 0; i < labels.Count; i++)
            {
                if (labels[i] == null)
                {
                    sb.Append(outer + "  {\n");
                }
                else
                {
                    sb.Append(outer + "  if (" + matchVar + " || " + valueVar + " == " +
                        ReplaceArgsInString(labels[i]) + ") { " + matchVar + " = true;\n");
                }

                m_depth = outer + "    ";
                for (int j = 0; j < bodies[i].Count; j++)
                {
                    var next = j + 1 < bodies[i].Count ? bodies[i][j + 1] : "";
                    sb.Append(ProcessStatement(bodies[i][j], next));
                }
                m_depth = outer;
                sb.Append(outer + "  }\n");
            }

            sb.Append(outer + "} while (false);\n");

            m_statementId = id;        // the main loop advances past the closing brace
            converted = sb.ToString();
            return true;
        }

        /// <summary>
        /// Splits "case 1: rest" or "default: rest" into its label and whatever follows on the
        /// same statement. The label is null for default. Returns false when the statement is
        /// not a clause header.
        /// </summary>
        /// <summary>Whether the word appears in the text as a whole word.</summary>

        static bool ContainsWord(string text, string word)
        {
            int from = 0;
            while (true)
            {
                int at = text.IndexOf(word, from, StringComparison.Ordinal);
                if (at < 0)
                {
                    return false;
                }
                bool leftOk = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
                int after = at + word.Length;
                bool rightOk = after >= text.Length || !char.IsLetterOrDigit(text[after]);
                if (leftOk && rightOk)
                {
                    return true;
                }
                from = at + 1;
            }
        }

        static bool TrySplitClause(string statement, out string label, out string rest)
        {
            label = null;
            rest = "";
            var trimmed = statement.TrimStart();
            bool isCase = trimmed.StartsWith(Constants.CASE, StringComparison.OrdinalIgnoreCase) &&
                          trimmed.Length > Constants.CASE.Length &&
                          !char.IsLetterOrDigit(trimmed[Constants.CASE.Length]);
            bool isDefault = trimmed.StartsWith(Constants.DEFAULT, StringComparison.OrdinalIgnoreCase) &&
                             (trimmed.Length == Constants.DEFAULT.Length ||
                              !char.IsLetterOrDigit(trimmed[Constants.DEFAULT.Length]));
            if (!isCase && !isDefault)
            {
                return false;
            }

            int colon = -1;
            bool inQuotes = false;
            for (int i = 0; i < trimmed.Length; i++)
            {
                var ch = trimmed[i];
                if (ch == '"' && (i == 0 || trimmed[i - 1] != '\\')) { inQuotes = !inQuotes; continue; }
                if (!inQuotes && ch == ':') { colon = i; break; }
            }
            if (colon < 0)
            {
                return false;
            }

            rest = trimmed.Substring(colon + 1).Trim();
            if (isDefault)
            {
                return true;           // label stays null
            }
            label = trimmed.Substring(Constants.CASE.Length, colon - Constants.CASE.Length).Trim();
            return !string.IsNullOrWhiteSpace(label);
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
                converted = m_depth + "double " + varName + rest + ";\n";
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
            // The interpreter binds the caught variable to the thrown string, so use
            // Message rather than ToString() -- the latter yields
            // "System.ArgumentException: boom" plus a stack trace and would silently
            // differ from an interpreted run.
            result += RegisterVariableString(varName, "new Variable(" + varName + ".Message)");
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
                // Assigning to an argument has to write the argument slot. Declaring "var s"
                // created a shadowing local while every read of s still resolved to
                // __varStr[0], so "s = \"x\"; return s;" silently returned the original
                // argument, and "s = s + \"!\";" would not even compile.
                string mapped;
                if (m_paramMap.TryGetValue(functionName, out mapped) && !string.IsNullOrEmpty(mapped))
                {
                    return m_depth + mapped;
                }
                // Re-assigning a local declares it only the first time.
                if (m_newVariables.Contains(functionName))
                {
                    return m_depth + functionName;
                }
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
            if (token == "?" || token == ":")
            {
                // Ternary punctuation is already valid C#. Without this it resolves to no
                // known function and becomes an interpreter callback in mid-expression,
                // which is what stopped a ternary with string branches from compiling.
                result += token;
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
                else if (!string.IsNullOrEmpty(suffix) && suffix[0] == Constants.START_ARG &&
                         (functionName == Constants.IF || functionName == Constants.WHILE))
                {
                    // A condition containing a string literal is not a "known expression" --
                    // IsKnownExpression bails on strings -- so it lands here, where the token
                    // used to be copied through verbatim and its arguments were never
                    // resolved: if (s == "ab") emitted a bare "s". Numeric conditions take the
                    // known-expression path above and never reach this.
                    token = functionName + ReplaceArgsInString(suffix);
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
            // An argument is never a plain local: it lives in its slot, so it must fall
            // through to the argument handling below. Without this guard, once a name reached
            // m_newVariables it was emitted bare -- "s = s + \"!\"" produced "__varStr[0]=s+..."
            // referring to an "s" the generated method does not have.
            string mappedArgument;
            bool isMappedArgument = m_paramMap.TryGetValue(functionName, out mappedArgument) &&
                                    !string.IsNullOrEmpty(mappedArgument);

            if (m_newVariables.Contains(functionName) && !isMappedArgument)
            {
                if (id == 0)
                {
                    newVarAdded = !isArray;
                }
                // A CSCS string member on a local: .Upper has no C# equivalent by that name,
                // and emitting it verbatim does not compile. Mapping is safe even though the
                // local's type is not tracked -- a non-string simply fails to compile and the
                // function falls back.
                string mappedMember = null;
                if (!isArray && suffix.StartsWith(".") &&
                    MapStringMember(functionName, suffix.Substring(1), ref mappedMember))
                {
                    result += mappedMember;
                    return;
                }

                // An index expression is ordinary code and needs resolving. Emitting the
                // whole token verbatim left argument names undeclared, so "a[n]" referred to
                // an "n" that does not exist in the generated method.
                result += isArray && !string.IsNullOrEmpty(suffix) ?
                    functionName + ReplaceArgsInString(suffix) : token;
                return;
            }

            if (id == 0 && tokens.Count > id + 2 && tokens[id + 1] == "=" && !token.Contains('.'))
            {
                // Registered after the call: GetExpressionType needs to tell a first
                // assignment (which declares) from a later one (which does not).
                string expr = GetExpressionType(tokens, functionName, ref newVarAdded);
                if (!isMappedArgument)
                {
                    m_newVariables.Add(functionName);   // arguments are already declared
                }
                newVarAdded = true;
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

            // The index expression is part of the surrounding code and needs resolving too:
            // returning "[n]" verbatim left the argument name undeclared in "a[n]".
            if (!string.IsNullOrWhiteSpace(arrayArg))
            {
                arrayArg = ReplaceArgsInString(arrayArg);
            }

            if (m_paramMap.TryGetValue(token, out replacement))
            {
                return replacement + arrayArg;
            }

            // "a[i]" on a collection local yields a Variable, which has no arithmetic
            // operators. Inside a known expression the result is numeric by definition --
            // IsKnownExpression rejects anything string-typed -- so AsDouble() is safe here
            // and nowhere else.
            if (m_knownExpression && !string.IsNullOrEmpty(arrayArg) &&
                m_collectionLocals.Contains(token))
            {
                return token + arrayArg + ".AsDouble()";
            }

            resolved = !string.IsNullOrWhiteSpace(arrayArg) ||
                        m_newVariables.Contains(token);
            return token + arrayArg;
        }

        static bool IsTokenSeparator(char ch)
        {
            // Comparison and logical characters end a token just like the arithmetic ones.
            // Leaving them out meant "n>1&&n<100" was split only at '&', so everything after
            // the first comparison stayed one unresolved blob and argument names in it were
            // never replaced.
            return (ch == ',' || ch == '+' || ch == '-' || ch == '(' || ch == ')' || ch == '[' || ch == ']' ||
                    ch == '%' || ch == '*' || ch == '/' || ch == '&' || ch == '|' || ch == '^' || ch == '?' ||
                    ch == '<' || ch == '>' || ch == '=' || ch == '!' || ch == ':');
        }

        string ReplaceArgsInString(string argStr)
        {
            StringBuilder sb = new StringBuilder();
            bool inQuotes = false;
            string token = "";
            int backSlashes = 0;
            char prevSeparator = '\0';
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
                else if (ch == '[' && m_knownExpression && m_collectionLocals.Contains(token))
                {
                    // "a[i]" on a collection local reads a Variable, which has no arithmetic
                    // operators. '[' is itself a token separator, so the index has to be
                    // consumed here as one unit rather than resolved as a bare "a". Inside a
                    // known expression the result is numeric by definition -- IsKnownExpression
                    // rejects anything string-typed -- so AsDouble() is safe here and nowhere
                    // else.
                    int close = FindMatchingBracket(argStr, i);
                    if (close < 0)
                    {
                        token += ch;
                        continue;
                    }
                    var index = argStr.Substring(i + 1, close - i - 1);
                    sb.Append(token).Append('[').Append(ReplaceArgsInString(index)).Append("].AsDouble()");
                    i = close;
                    prevSeparator = ']';
                    token = "";
                }
                else if (IsTokenSeparator(ch))
                {
                    string arguments = i + 1 < argStr.Length ? argStr.Substring(i + 1) : "";
                    sb.Append(AsDoubleNextToDivision(ResolveToken(token, out _, arguments), token, prevSeparator, ch));
                    sb.Append(ch);
                    prevSeparator = ch;
                    token = "";
                }
                else
                { // We are collecting the chars
                    token += ch;
                }
            }

            sb.Append(AsDoubleNextToDivision(ResolveToken(token, out _), token, prevSeparator, '\0'));
            return sb.ToString();
        }

        /// <summary>
        /// Emits an integer literal that sits next to a '/' as a double.
        ///
        /// CSCS numbers are all doubles, so 3/2 is 1.5. Copied into C# verbatim it becomes
        /// integer division and yields 1 -- code that compiles cleanly and quietly returns
        /// a different answer. Only literals touching a division are widened, because an
        /// integer literal elsewhere may be picking a C# overload (Math.Round(x, 3) takes
        /// an int) that a double would no longer match.
        /// </summary>
        static string AsDoubleNextToDivision(string resolved, string original, char before, char after)
        {
            if (before != '/' && after != '/')
            {
                return resolved;
            }
            // An int-typed argument is a C# int, so int/int would truncate here too.
            if (resolved.StartsWith(INT_VAR_ARG, StringComparison.InvariantCulture) ||
                resolved.StartsWith(INT_ARRAY_ARG, StringComparison.InvariantCulture))
            {
                return "(double)(" + resolved + ")";
            }
            if (resolved != original || !IsIntegerLiteral(original))
            {
                return resolved;
            }
            return original + ".0";
        }

        /// <summary>
        /// True when the expression is a comparison or logical test, so its C# type is bool
        /// rather than a number. Quoted text is skipped so a '&gt;' inside a string literal
        /// does not count.
        /// </summary>
        static bool YieldsBool(string expression)
        {
            bool inQuotes = false;
            for (int i = 0; i < expression.Length; i++)
            {
                var ch = expression[i];
                if (ch == '"' && (i == 0 || expression[i - 1] != '\\'))
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (inQuotes)
                {
                    continue;
                }
                if (ch == '<' || ch == '>' || ch == '!')
                {
                    return true;
                }
                if ((ch == '=' || ch == '&' || ch == '|') && i + 1 < expression.Length &&
                    expression[i + 1] == ch)
                {
                    return true;
                }
            }
            return false;
        }

        static bool IsIntegerLiteral(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            foreach (var ch in text)
            {
                if (!char.IsDigit(ch))
                {
                    return false;
                }
            }
            return true;
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
            if (paramStart <= 0)
            {
                // Never silently return "": the caller uses the result as the whole
                // translated statement, so an empty string would drop it and change what the
                // function computes. Failing here makes the function fall back instead.
                throw new ArgumentException("Not a math call: " + token);
            }
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

            // Pull in tokens until the call's parentheses balance. The old loop tested a
            // paramEnd it never recomputed, so it consumed every remaining token: it turned
            // "helper(n*2) + 1" into an argument string of "n*2)+1" and dropped the "+ 1"
            // from the result entirely.
            int callEnd = FindMatchingParen(restStr, paramStart);
            while (paramStart >= 0 && callEnd < 0 && m_tokenId + 1 < tokens.Count)
            {
                restStr += tokens[++m_tokenId];
                callEnd = FindMatchingParen(restStr, paramStart);
            }

            string functionName = paramStart < 0 ? restStr : restStr.Substring(0, paramStart);
            string argsStr = "";
            string trailing = "";
            if (paramStart >= 0)
            {
                var callText = callEnd >= 0 ? restStr.Substring(paramStart, callEnd - paramStart + 1)
                                            : restStr.Substring(paramStart);
                if (callEnd >= 0 && callEnd + 1 < restStr.Length)
                {
                    // Whatever follows the call is still part of the expression.
                    trailing = restStr.Substring(callEnd + 1);
                }
                ParsingScript tmpScript = new ParsingScript(m_parentScript.InterpreterInstance, callText);
                argsStr = Utils.PrepareArgs(Utils.GetBodyBetween(tmpScript));
            }

            string token = "";
            if (ProcessArray(argsStr, functionName, ref token))
            {
                result += token;
                return;
            }

            // "a.Add(x)" on a collection built in this function: AddVariable is what the
            // interpreter itself calls, and this is usually inside a loop, so the callback it
            // replaces was being paid on every iteration.
            int addDot = functionName.IndexOf('.');
            // A quoted argument reaches here already escaped for embedding in a C# string
            // literal, and re-running that through ReplaceArgsInString mangles the
            // backslashes, so those keep the interpreter.
            if (addDot > 0 && !string.IsNullOrEmpty(argsStr) && !argsStr.Contains('"') &&
                m_collectionLocals.Contains(functionName.Substring(0, addDot)) &&
                functionName.Substring(addDot + 1).Trim().ToLower() == "add")
            {
                result += functionName.Substring(0, addDot) + ".AddVariable(Variable.ConvertToVariable(" +
                    ReplaceArgsInString(argsStr) + "))";
                return;
            }

            // "s.Upper" arrives here as a function name with no arguments; mapping it to a
            // direct C# call avoids a round trip through the interpreter for every use.
            int memberDot = functionName.IndexOf('.');
            if (memberDot > 0 && string.IsNullOrEmpty(argsStr))
            {
                var owner = functionName.Substring(0, memberDot);
                var member = functionName.Substring(memberDot + 1);
                // Arguments first, where the type is declared; otherwise a local, which is
                // attempted on the same reasoning as MapStringMember: a non-string local
                // simply fails to compile and the function falls back.
                if (ProcessStringMember(owner, member, ref token) ||
                    (m_newVariables.Contains(owner) && !m_collectionLocals.Contains(owner) &&
                     MapStringMember(owner, member, ref token)))
                {
                    result += token;
                    return;
                }
            }

            var tryCSharp = GetCSharpFunction(functionName, argsStr);
            if (!string.IsNullOrEmpty(tryCSharp))
            {
                result += tryCSharp + "\n";
                return;
            }

            StringBuilder sb = new StringBuilder();

            if (functionName.StartsWith("{"))
            {
                // An array literal. Build the Variable directly: the interpreter cannot
                // evaluate a bare "{1,2,3}" fragment on its own ("Couldn't find operand []"),
                // and constructing it here keeps the literal out of the interpreter entirely.
                // A map literal (key:value) is left to fall back for now.
                var inner = functionName.Substring(1, functionName.Length - 2).Trim();
                var elements = SplitTopLevel(inner, ',');

                // "key : value" entries make this a map rather than an array.
                bool isMap = false;
                foreach (var element in elements)
                {
                    if (SplitTopLevel(element, ':').Count > 1)
                    {
                        isMap = true;
                        break;
                    }
                }

                if (isMap)
                {
                    // SetHashVariable is what the interpreter itself uses for map assignment,
                    // so keys and values end up laid out exactly as a script expects.
                    sb.AppendLine(m_depth + VARIABLE_TEMP_VAR + " = new Variable(Variable.VarType.ARRAY);");
                    foreach (var element in elements)
                    {
                        if (string.IsNullOrWhiteSpace(element))
                        {
                            continue;
                        }
                        var pair = SplitTopLevel(element, ':');
                        if (pair.Count != 2)
                        {
                            throw new ArgumentException("Malformed map entry: " + element);
                        }
                        sb.AppendLine(m_depth + VARIABLE_TEMP_VAR + ".SetHashVariable(" +
                            "Variable.ConvertToVariable(" + ReplaceArgsInString(pair[0].Trim()) + ").AsString(), " +
                            "Variable.ConvertToVariable(" + ReplaceArgsInString(pair[1].Trim()) + "));");
                    }
                }
                else
                {
                    var items = new List<string>();
                    foreach (var element in elements)
                    {
                        if (string.IsNullOrWhiteSpace(element))
                        {
                            continue;
                        }
                        items.Add("Variable.ConvertToVariable(" + ReplaceArgsInString(element.Trim()) + ")");
                    }
                    sb.AppendLine(m_depth + VARIABLE_TEMP_VAR + " = new Variable(new List<Variable> { " +
                        string.Join(", ", items) + " });");
                }
                EmitCallResult(tokens, sb.ToString(), trailing, ref result, ref newVarAdded);
                return;
            }

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
            EmitCallResult(tokens, token, trailing, ref result, ref newVarAdded);
        }

        /// <summary>
        /// Places a call's generated statements and its resulting value into the statement
        /// being built: either as the right-hand side of an assignment, as the whole
        /// expression, or hoisted ahead of a larger expression.
        /// </summary>
        void EmitCallResult(List<string> tokens, string token, string trailing,
                            ref string result, ref bool newVarAdded)
        {
            if (tokens.Count >= 3 && tokens[1] == "=" && !string.IsNullOrWhiteSpace(result))
            {
                var type = GetTokenType(tokens);
                var last = type == "string" ? VARIABLE_TEMP_VAR + ".AsString()" : VARIABLE_TEMP_VAR;
                result = m_depth + token + result + last +
                    (string.IsNullOrEmpty(trailing) ? "" : ReplaceArgsInString(trailing)) + ";\n";
                newVarAdded = true;
            }
            else if (string.IsNullOrEmpty(trailing) && m_tokenId >= tokens.Count - 1 &&
                     string.IsNullOrWhiteSpace(result))
            {
                // The call is the whole expression -- nothing before it and nothing after --
                // so its statements can simply be emitted: the caller reads the shared temp
                // as a Variable and no type has to be guessed. This is the long-standing path
                // and stays as it was. "return n * rec(n-1)" does not qualify: the call is the
                // last token, but "n *" is already in result and would be orphaned.
                result += token;
            }
            else
            {
                // The call is part of a larger expression, so hoist its statements ahead of
                // the statement and leave a value behind. Splicing them inline produced
                //   __varTempVar = ...GetValue(...);  +1  return __varTempVar;
                // where the rest of the expression ("+ 1") was a syntax error and, worse,
                // silently dropped from the result.
                var tempName = VARIABLE_TEMP_VAR + (++m_tempVarId);
                m_statementPrelude += token + m_depth + "Variable " + tempName + " = " +
                    VARIABLE_TEMP_VAR + ";\n";
                // Each call gets its own temp: the shared one would be overwritten by a
                // later call before the expression read it.
                result += GetTokenType(tokens) == "string" ?
                    tempName + ".AsString()" : tempName + ".AsDouble()";
                if (!string.IsNullOrEmpty(trailing))
                {
                    result += ReplaceArgsInString(trailing);
                }
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
            m_usesInterpreter = true;
            string result = m_depth + VARIABLE_TEMP_VAR + " = __interpreter.GetVariableValue(\"" + functionName + "\");\n";
            return result;
        }

        string GetCSCSFunction(string argsStr, string functionName, char ch = '(')
        {
            m_usesInterpreter = true;
            StringBuilder sb = new StringBuilder();

            // Re-register the function's arguments before every call. Arguments are
            // registered once on entry, but a called CSCS function pops a stack level when it
            // returns and takes them with it -- so a second call in the same expression could
            // no longer resolve them ("Couldn't find variable [n]").
            foreach (var param in m_paramMap)
            {
                if (string.IsNullOrEmpty(param.Value))
                {
                    continue;
                }
                sb.AppendLine(m_depth + "__interpreter.AddGlobalOrLocalVariable(\"" + param.Key +
                    "\", new GetVarFunction(Variable.ConvertToVariable(" + param.Value + ")));");
            }
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
            if (ProcessStringMember(arrayName, methodName, ref result))
            {
                return true;
            }
            return ProcessArray(arrayName, methodName, ref result);
        }

        /// <summary>
        /// Maps CSCS string members onto their C# equivalents so they compile to a direct
        /// call instead of an interpreter callback.
        ///
        /// Deliberately short. Most other members cannot be mapped without changing results:
        /// .Size is 0 on a string in CSCS (not its length), and .Contains/.StartsWith yield
        /// 1/0 rather than C#'s True/False, so a mapped version would differ once the value
        /// reaches a string. .Length, .Substring, .IndexOf and .Replace need no mapping --
        /// a string argument is a real C# string, so those already compile natively.
        /// </summary>
        bool ProcessStringMember(string name, string member, ref string result)
        {
            Variable arg;
            if (!m_argsMap.TryGetValue(name, out arg) || arg.Type != Variable.VarType.STRING ||
                !m_paramMap.ContainsKey(name))
            {
                return false;
            }

            var target = m_paramMap[name];
            return MapStringMember(target, member, ref result);
        }

        /// <summary>
        /// Maps a CSCS string member onto its C# equivalent for any expression known to be a
        /// C# string. Safe to attempt on a local whose type is not tracked: if it turns out
        /// not to be a string, the generated call does not compile and the function falls
        /// back -- it cannot produce a wrong answer.
        /// </summary>
        static bool MapStringMember(string target, string member, ref string result)
        {
            switch (member.Trim().ToLower())
            {
                case "upper": result = target + ".ToUpper()"; return true;
                case "lower": result = target + ".ToLower()"; return true;
            }
            return false;
        }

        bool ProcessArray(string arrayName, string methodName, ref string result)
        {
            // A collection built in this function is a Variable, whose Size property already
            // means the element count. Without this, "a.Size" inside a larger expression --
            // "a[a.Size-1]", say -- resolves to nothing and becomes an interpreter callback
            // in a position where a value is required.
            if (m_collectionLocals.Contains(arrayName) && methodName.Trim().ToLower() == "size")
            {
                result = arrayName + ".Size";
                return true;
            }

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
                if (token == "?" || token == ":")
                {
                    // Ternary punctuation: neither an operand nor an arithmetic operator, so
                    // it must not make the expression look unknown.
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

                // Strip grouping punctuation the surrounding expression left on the token.
                // "!(n>100)" tokenizes with a trailing "100))", and GetFunctionName only
                // trims back to the last ')', leaving "100)" -- which is not a number, so the
                // whole condition looked unknown and stopped resolving its arguments.
                paramName = paramName.Trim('(', ')', '!');

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

        /// <summary>
        /// Index of the '}' matching the '{' at <paramref name="openIndex"/>, or -1. Counts
        /// nesting and ignores braces inside string literals.
        /// </summary>
        /// <summary>
        /// Splits on <paramref name="separator"/> at nesting depth zero, ignoring separators
        /// inside brackets, braces, parentheses or string literals. string.Split cannot be
        /// used for a literal's elements because "{1,{2,3}}" and "f(a,b)" both nest.
        /// </summary>
        /// <summary>
        /// Translates "name = {...}" into code that builds the collection, or null when the
        /// statement is not a literal assignment.
        ///
        /// Collections are built as a Variable using the interpreter's own operations
        /// (SetHashVariable for maps), so a compiled literal is laid out exactly as an
        /// interpreted one.
        /// </summary>
        string TryBuildLiteralAssignment(string statement, bool addNewVars)
        {
            statement = statement.TrimEnd().TrimEnd(';').TrimEnd();
            int assign = statement.IndexOf('=');
            if (assign <= 0 || assign + 1 >= statement.Length || statement[assign + 1] != '{' ||
                !statement.EndsWith("}"))
            {
                return null;
            }

            var name = statement.Substring(0, assign).Trim();
            foreach (var ch in name)
            {
                if (!char.IsLetterOrDigit(ch) && ch != '_')
                {
                    return null;   // compound assignment, indexed target, comparison, ...
                }
            }

            var literal = statement.Substring(assign + 1).Trim();
            if (FindMatchingBrace(literal, 0) != literal.Length - 1)
            {
                return null;       // the braces are not one balanced literal
            }

            var inner = literal.Substring(1, literal.Length - 2).Trim();
            var elements = SplitTopLevel(inner, ',');
            bool isMap = false;
            foreach (var element in elements)
            {
                if (SplitTopLevel(element, ':').Count > 1)
                {
                    isMap = true;
                    break;
                }
            }

            var declaration = m_newVariables.Contains(name) ? "" : "var ";
            m_newVariables.Add(name);
            m_collectionLocals.Add(name);

            var sb = new StringBuilder();
            if (isMap)
            {
                sb.Append(m_depth + declaration + name + " = new Variable(Variable.VarType.ARRAY);\n");
                foreach (var element in elements)
                {
                    if (string.IsNullOrWhiteSpace(element))
                    {
                        continue;
                    }
                    var pair = SplitTopLevel(element, ':');
                    if (pair.Count != 2)
                    {
                        return null;
                    }
                    sb.Append(m_depth + name + ".SetHashVariable(Variable.ConvertToVariable(" +
                        ReplaceArgsInString(pair[0].Trim()) + ").AsString(), Variable.ConvertToVariable(" +
                        ReplaceArgsInString(pair[1].Trim()) + "));\n");
                }
            }
            else
            {
                var items = new List<string>();
                foreach (var element in elements)
                {
                    if (string.IsNullOrWhiteSpace(element))
                    {
                        continue;
                    }
                    items.Add("Variable.ConvertToVariable(" + ReplaceArgsInString(element.Trim()) + ")");
                }
                sb.Append(m_depth + declaration + name + " = new Variable(new List<Variable> { " +
                    string.Join(", ", items) + " });\n");
            }

            if (addNewVars)
            {
                sb.Append(RegisterVariableString(name));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Translates "name[index] = value" into a SetVariable call, or null when the
        /// statement is not an indexed assignment. SetVariable is the interpreter's own
        /// assignment logic, so map keys, array growth and numeric indices all behave
        /// identically to an interpreted run.
        /// </summary>
        string TryBuildIndexedAssignment(string statement, bool addNewVars)
        {
            int bracket = statement.IndexOf('[');
            if (bracket <= 0)
            {
                return null;
            }

            var name = statement.Substring(0, bracket).Trim();
            foreach (var ch in name)
            {
                if (!char.IsLetterOrDigit(ch) && ch != '_')
                {
                    return null;
                }
            }
            if (!m_newVariables.Contains(name) && !m_paramMap.ContainsKey(name))
            {
                return null;   // not a collection this function knows about
            }

            int depth = 0;
            int close = -1;
            bool inQuotes = false;
            for (int i = bracket; i < statement.Length; i++)
            {
                var ch = statement[i];
                if (ch == '"' && (i == 0 || statement[i - 1] != '\\'))
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (inQuotes)
                {
                    continue;
                }
                if (ch == '[') depth++;
                else if (ch == ']')
                {
                    depth--;
                    if (depth == 0) { close = i; break; }
                }
            }
            if (close < 0 || close + 1 >= statement.Length || statement[close + 1] != '=')
            {
                return null;   // not a plain assignment (could be "a[i] == b" or a read)
            }
            if (close + 2 < statement.Length && statement[close + 2] == '=')
            {
                return null;   // "==" comparison
            }

            var indexExpr = statement.Substring(bracket + 1, close - bracket - 1).Trim();
            var valueExpr = statement.Substring(close + 2).Trim().TrimEnd(';');
            if (string.IsNullOrWhiteSpace(indexExpr) || string.IsNullOrWhiteSpace(valueExpr))
            {
                return null;
            }

            var target = m_paramMap.ContainsKey(name) ? m_paramMap[name] : name;
            var code = m_depth + target + ".SetVariable(Variable.ConvertToVariable(" +
                ReplaceArgsInString(indexExpr) + "), Variable.ConvertToVariable(" +
                ReplaceArgsInString(valueExpr) + "));\n";
            if (addNewVars)
            {
                code += RegisterVariableString(name, target);
            }
            return code;
        }

        /// <summary>Index of the ']' matching the '[' at <paramref name="start"/>, or -1.</summary>
        static int FindMatchingBracket(string text, int start)
        {
            int depth = 0;
            bool inQuotes = false;
            for (int i = start; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == '"' && (i == 0 || text[i - 1] != '\\'))
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (inQuotes)
                {
                    continue;
                }
                if (ch == '[') depth++;
                else if (ch == ']' && --depth == 0) return i;
            }
            return -1;
        }

        static List<string> SplitTopLevel(string text, char separator)
        {
            var parts = new List<string>();
            if (text == null)
            {
                return parts;
            }

            int depth = 0;
            bool inQuotes = false;
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char current = text[i];
                if (current == '"' && (i == 0 || text[i - 1] != '\\'))
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (inQuotes)
                {
                    continue;
                }
                if (current == '(' || current == '[' || current == '{')
                {
                    depth++;
                }
                else if (current == ')' || current == ']' || current == '}')
                {
                    depth--;
                }
                else if (current == separator && depth == 0)
                {
                    parts.Add(text.Substring(start, i - start));
                    start = i + 1;
                }
            }
            parts.Add(text.Substring(start));
            return parts;
        }

        static int FindMatchingBrace(string text, int openIndex)
        {
            int depth = 0;
            bool inQuotes = false;
            for (int i = openIndex; i < text.Length; i++)
            {
                char current = text[i];
                if (current == '"' && (i == 0 || text[i - 1] != '\\'))
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (inQuotes)
                {
                    continue;
                }
                if (current == '{')
                {
                    depth++;
                }
                else if (current == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }
            return -1;
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
                        if (token.EndsWith("=") && ch == '{')
                        {
                            // An array or map literal in value position. Braces normally end a
                            // statement, which shredded "a = {1,2,3}" into "a=", "{", "1,2,3"
                            // and "}" before translation ever saw it. Keep it in one piece.
                            int literalEnd = FindMatchingBrace(scriptText, i);
                            if (literalEnd > 0)
                            {
                                tokens.Add(token + scriptText.Substring(i, literalEnd - i + 1));
                                i = literalEnd + 1;
                                startIndex = i;
                                continue;
                            }
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
            int bracketDepth = 0;
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
                    if (ch == '[')
                    {
                        bracketDepth++;
                    }
                    else if (ch == ']' && bracketDepth > 0)
                    {
                        bracketDepth--;
                    }

                    // Operators inside an index belong to the index expression, not to the
                    // statement. Splitting on them tore "a[a.Size-1]" into "a[a.Size", "-",
                    // "1]", which no later stage could put back together.
                    string candidate = bracketDepth > 0 ? null :
                        Utils.ValidAction(statement.Substring(i));
                    // '?' and ':' are not "valid actions", so without this a ternary came
                    // through as one glued token ("5?100") that resolved to nothing and got
                    // turned into an interpreter callback in the middle of the expression.
                    if (candidate == null && bracketDepth == 0 &&
                        (Constants.STATEMENT_TOKENS.IndexOf(statement[i]) >= 0 ||
                                             ch == '?' || ch == ':' ||
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
            if (string.IsNullOrEmpty(name))
            {
                // ReplaceMathArgs hands over an empty name for any token without '(' -- an
                // array literal such as {1,2,3}, for instance -- and name[0] below then threw
                // IndexOutOfRange out of the middle of translation.
                return false;
            }
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

        /// <summary>
        /// Index of the ')' matching the '(' at <paramref name="openIndex"/>, or -1 if the
        /// text does not contain it. Counts nesting and ignores parentheses inside strings,
        /// neither of which FindChar does.
        /// </summary>
        static int FindMatchingParen(string text, int openIndex)
        {
            if (openIndex < 0 || openIndex >= text.Length)
            {
                return -1;
            }

            int depth = 0;
            bool inQuotes = false;
            for (int i = openIndex; i < text.Length; i++)
            {
                char current = text[i];
                if (current == '"' && (i == 0 || text[i - 1] != '\\'))
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (inQuotes)
                {
                    continue;
                }
                if (current == '(')
                {
                    depth++;
                }
                else if (current == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }
            return -1;
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
