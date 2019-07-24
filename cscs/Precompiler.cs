using System;
using System.Collections.Generic;
using System.Text;

using System.CodeDom;
using System.CodeDom.Compiler;
using System.IO;
using Microsoft.CSharp;
using System.Reflection;
using System.Diagnostics;
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
        string[] m_defArgs;
        StringBuilder m_converted = new StringBuilder();
        Dictionary<string, Variable> m_argsMap;
        HashSet<string> m_numericVars = new HashSet<string>();
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
        bool m_numericExpression;
        //bool m_assigmentExpression;
        bool m_lastStatementReturn;

        ParsingScript m_parentScript;
        public string CSCode { get; private set; }

        Func<List<string>, List<double>, List<List<string>>, List<List<double>>,
             List<Dictionary<string, string>>, List<Dictionary<string, double>>, List<Variable>, Variable> m_compiledFunc;

        Func<List<string>, List<double>, List<List<string>>, List<List<double>>,
             List<Dictionary<string, string>>, List<Dictionary<string, double>>, List<Variable>, Task<Variable>> m_compiledFuncAsync;

        static List<string> s_namespaces = new List<string>();

        public static bool AsyncMode { get; set; } = true;

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

        public static void AddNamespace(string ns)
        {
            s_namespaces.Add(ns);
        }

        public Precompiler(string functionName, string[] args, Dictionary<string, Variable> argsMap,
                           string cscsCode, ParsingScript parentScript)
        {
            m_functionName = functionName;
            m_defArgs = args;
            m_argsMap = argsMap;
            m_originalCode = cscsCode;
            m_returnType = GetReturnType(m_functionName);
            m_parentScript = parentScript;
        }

        public void Compile()
        {
            var CompilerParams = new CompilerParameters();

            CompilerParams.GenerateInMemory = true;
            CompilerParams.TreatWarningsAsErrors = false;
            CompilerParams.GenerateExecutable = false;
            CompilerParams.CompilerOptions = "/optimize";

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly asm in assemblies)
            {
                AssemblyName asmName = asm.GetName();
                if (asmName == null || string.IsNullOrWhiteSpace(asmName.CodeBase))
                {
                    continue;
                }

                var uri = new Uri(asmName.CodeBase);
                if (uri == null || string.IsNullOrWhiteSpace(uri.LocalPath) || !File.Exists(uri.LocalPath))
                {
                    continue;
                }

                CompilerParams.ReferencedAssemblies.Add(uri.LocalPath);
            }

            var provider = new CSharpCodeProvider();

            /*CSCode = ConvertScript(false);
            var compile = provider.CompileAssemblyFromSource(CompilerParams, CSCode);
            if (compile.Errors.HasErrors)
            {
                CSCode = ConvertScript();
                compile = provider.CompileAssemblyFromSource(CompilerParams, CSCode);
            }*/

            CSCode = ConvertScript();
            var compile = provider.CompileAssemblyFromSource(CompilerParams, CSCode);
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
            paramTypes.Add(Expression.Parameter(typeof(List<string>), "__varStr"));
            paramTypes.Add(Expression.Parameter(typeof(List<double>), "__varNum"));
            paramTypes.Add(Expression.Parameter(typeof(List<List<string>>), "__varArrStr"));
            paramTypes.Add(Expression.Parameter(typeof(List<List<double>>), "__varArrNum"));
            paramTypes.Add(Expression.Parameter(typeof(List<Dictionary<string, string>>), "__varMapStr"));
            paramTypes.Add(Expression.Parameter(typeof(List<Dictionary<string, double>>), "__varMapNum"));
            paramTypes.Add(Expression.Parameter(typeof(List<Variable>), "__varVar"));
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

            var functionReturnType = GetReturnType(paramName);
            if (functionReturnType != Variable.VarType.NONE)
            {
                return functionReturnType;
            }

            Variable arg;
            if (m_argsMap.TryGetValue(paramName, out arg))
            {
                return arg.Type;
            }

            if (IsMathFunction(paramName))
            {
                return Variable.VarType.NUMBER;
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
            }

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

        string ConvertScript(bool cscsStyle = true)
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
            for (int i = 0; i < m_defArgs.Length; i++)
            {
                Variable typeVar = m_argsMap[m_defArgs[i]];
                m_paramMap[m_defArgs[i]] =
                  typeVar.Type == Variable.VarType.STRING ? "__varStr[" + (strIndex++) + "]" :
                  typeVar.Type == Variable.VarType.NUMBER ? "__varNum[" + (numIndex++) + "]" :
                  typeVar.Type == Variable.VarType.ARRAY_STR ? "__varArrStr[" + (arrStrIndex++) + "]" :
                  typeVar.Type == Variable.VarType.ARRAY_NUM ? "__varArrNum[" + (arrNumIndex++) + "]" :
                  typeVar.Type == Variable.VarType.MAP_STR ? "__varMapStr[" + (mapStrIndex++) + "]" :
                  typeVar.Type == Variable.VarType.MAP_NUM ? "__varMapNum[" + (mapNumIndex++) + "]" :
                  typeVar.Type == Variable.VarType.VARIABLE ? "__varVar[" + (varIndex++) + "]" :
                            "";
            }

            m_converted.AppendLine("using System; using System.Collections; using System.Collections.Generic; using System.Collections.Specialized; " +
                                   "using System.Globalization; using System.Linq; using System.Linq.Expressions; using System.Reflection; " +
                                   "using System.Text; using System.Threading; using System.Threading.Tasks; using static System.Math;");
            for (int i = 0; i < s_namespaces.Count; i++)
            {
                m_converted.AppendLine(s_namespaces[i]);
            }
            m_converted.AppendLine("namespace SplitAndMerge {\n" +
                                   "  public partial class Precompiler {");

            if (AsyncMode)
            {
                m_converted.AppendLine("    public static async Task<Variable> " + m_functionName);
            }
            else
            {
                m_converted.AppendLine("    public static Variable " + m_functionName);
            }
            m_converted.AppendLine(
                           "(List<string> __varStr,\n" +
                           " List<double> __varNum,\n" +
                           " List<List<string>> __varArrStr,\n" +
                           " List<List<double>> __varArrNum,\n" +
                           " List<Dictionary<string, string>> __varMapStr,\n" +
                           " List<Dictionary<string, double>> __varMapNum,\n" +
                           " List<Variable> __varVar) {\n");
            m_depth = "      ";

            m_converted.AppendLine("     string __argsStr = \"\";");
            m_converted.AppendLine("     string __action = \"\";");
            m_converted.AppendLine("     ParsingScript __script = null;");
            m_converted.AppendLine("     ParserFunction __func = null;");
            m_converted.AppendLine("     Variable __tempVar = null;");
            m_newVariables.Add("__argsStr");
            m_newVariables.Add("__action");
            m_newVariables.Add("__script");
            m_newVariables.Add("__func");
            m_newVariables.Add("__tempVar");

            if (!cscsStyle)
            {
                //m_converted.AppendLine(m_originalCode);
                m_cscsCode = m_originalCode;
            }
            else
            {
                m_cscsCode = Utils.ConvertToScript(m_originalCode, out _);
            }
            ParsingScript script = new ParsingScript(m_cscsCode);

            m_cscsCode = m_cscsCode.Trim();
            if (m_cscsCode.Length > 0 &&  m_cscsCode.First() == '{')
            {
                m_cscsCode = m_cscsCode.Remove(0, 1);
                while (m_cscsCode.Length > 0 && m_cscsCode.Last() == ';')
                {
                    m_cscsCode = m_cscsCode.Remove(m_cscsCode.Length - 1, 1);
                }
                if (m_cscsCode.Length > 0 && m_cscsCode.Last() == '}')
                {
                    m_cscsCode = m_cscsCode.Remove(m_cscsCode.Length - 1, 1 );
                }
            }

            m_statements = TokenizeScript(m_cscsCode);
            m_statementId = 0;
            while (m_statementId < m_statements.Count)
            {
                m_currentStatement = m_statements[m_statementId];
                m_nextStatement = m_statementId < m_statements.Count - 1 ? m_statements[m_statementId + 1] : "";
                string converted = ProcessStatement(m_currentStatement, m_nextStatement, true, cscsStyle);
                if (!string.IsNullOrWhiteSpace(converted) && !converted.StartsWith(m_depth))
                {
                    m_converted.Append(m_depth);
                }
                m_converted.Append(converted);
                m_statementId++;
            }

            if (!m_lastStatementReturn)
            {
                m_converted.AppendLine(CreateReturnStatement("Variable.EmptyInstance"));
            }

            m_converted.AppendLine("\n    }\n    }\n}");
            return m_converted.ToString();
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

        string ProcessStatement(string statement, string nextStatement, bool addNewVars = true, bool cscsStyle = true)
        {
            if (string.IsNullOrWhiteSpace(statement) || ProcessSpecialCases(statement))
            {
                return "";
            }
            List<string> tokens = TokenizeStatement(statement);
            List<string> statementVars = new List<string>();

            string result = m_depth;
            m_lastStatementReturn = ProcessReturnStatement(tokens, cscsStyle, ref result);
            if (m_lastStatementReturn)
            {
                return result;
            }
            if (ProcessForStatement(statement, tokens, cscsStyle, ref result))
            {
                return result;
            }

            GetExpressionType(tokens);
            if (!cscsStyle)
            {
                result = ReplaceArgsInTokens(tokens);
                char last = result.Length < 1 ? Constants.EMPTY : result.First();
                char first = nextStatement.Length < 1 ? Constants.EMPTY : nextStatement.Last();
                if (last != ';' && (first == ';' || (char.IsLetterOrDigit(last) && char.IsLetterOrDigit(first))))
                {
                    result += ";";
                }
                return result;
            }
            if (m_numericExpression && (tokens.Count > 1 && tokens[1] == "="))
            {
                result = m_depth;
                if (cscsStyle && !m_newVariables.Contains(tokens[0]))
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
                result += RegisterVariableString(tokens[0]);

                return result;
            }
            if (m_numericExpression && tokens.Count == 1 &&
                (tokens[0].Contains(Constants.START_ARG) ||
                 tokens[0].Contains(Constants.NEXT_ARG)))
            {
                result = ReplaceArgs(tokens[0]);
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
                    if (m_numericExpression)
                    {
                        m_numericVars.Add(token);
                    }
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
                result += ";\n";
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

        bool ProcessReturnStatement(List<string> tokens, bool cscsStyle,
                                    ref string converted)
        {
            string suffix = "";
            string defaultReturn = "__tempVar";
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

                string token = cscsStyle ? defaultReturn : tokens[m_tokenId];
                if (cscsStyle)
                {
                    GetExpressionType(tokens);
                    if (m_numericExpression)
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
            string returnToken = ProcessStatement(remaining, "", false);
            return returnToken;
        }

        string CreateReturnStatement(string toReturn)
        {
            if (toReturn == "__tempVar")
            {
                return m_depth + "return __tempVar;\n";
            }
            if (!toReturn.Contains("Variable.EmptyInstance"))
            {
                toReturn = "new Variable(" + toReturn + ")";
            }
            if (!AsyncMode)
            {
                return m_depth + "return " + toReturn + ";\n"; 
            }

            string result = m_depth + "__tempVar = " + toReturn + ";\n";
            result       += m_depth + "return __tempVar;\n";
            //result += "  return Task.FromResult(__tempVar);\n";
            return result;
        }

        bool ProcessForStatement(string statement, List<string> tokens, bool cscsStyle,
                                 ref string converted)
        {
            string suffix = "";
            bool isArray = false;
            string functionName = GetFunctionName(statement, ref suffix, ref isArray).Trim();
            if (functionName != Constants.FOR)
            {
                return false;
            }
            if (m_nextStatement != Constants.END_STATEMENT.ToString())
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
            if (cscsStyle && !m_newVariables.Contains(varName))
            {
                m_newVariables.Add(varName);
                converted = m_depth + "var " + varName + rest + ";\n";
            }
            converted += m_depth + statement;
            converted += converted.EndsWith(";") ? "" : ";";
            m_statementId += 2;
            converted += ProcessStatement(m_statements[m_statementId], m_statements[m_statementId + 1], false, cscsStyle);
            converted += converted.EndsWith(";") ? "" : ";";
            m_statementId += 2;
            converted += ProcessStatement(m_statements[m_statementId], m_statements[m_statementId + 1], false, cscsStyle) + " {\n";
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

        string ProcessToken(List<string> tokens, ref int id, ref string result, ref bool newVarAdded)
        {
            string token = tokens[id].Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                return "";
            }

            string suffix = "";
            bool isArray = false;
            string functionName = GetFunctionName(token, ref suffix, ref isArray);
            if (string.IsNullOrEmpty(functionName))
            {
                result += suffix;
                return suffix;
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
                    result = tempFunc + result + " __tempVar;";
                }
                else
                {
                    result += token;
                }
                return token;
            }
            if (Array.IndexOf(Constants.ACTIONS, token) >= 0)
            {
                //if (id <= 1 && (token == "++" || token == "--" || Array.IndexOf(Constants.OPER_ACTIONS, token) >= 0)) {
                //  newVarAdded = true;
                //}
                result += token;
                return token;
            }
            if (m_newVariables.Contains(functionName))
            {
                if (id == 0)
                {
                    newVarAdded = !isArray;
                }
                result += token;
                return token;
            }

            if (id == 0 && tokens.Count > id + 2 && tokens[id + 1] == "=" && !token.Contains('.'))
            {
                m_newVariables.Add(functionName);
                newVarAdded = true;
                string expr = GetExpressionType(tokens, functionName, ref newVarAdded);
                result += expr;
                return expr;
            }

            Variable arg;
            if (m_argsMap.TryGetValue(functionName, out arg))
            {
                //return (arg.Type == Variable.VarType.NUMBER ? "    Utils.GetDouble(\"" : "    Utils.GetString(\"") + token + "\")";
                string actualName = m_paramMap[functionName];
                token = " " + actualName + suffix;
                result += token;
                return token;
            }
            return ProcessFunction(tokens, ref id, ref result, ref newVarAdded);
        }

        string ReplaceArgsInTokens(List<string> tokens)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];
                string replacement;
                if (!string.IsNullOrWhiteSpace(token) && token[0] != Constants.QUOTE &&
                     m_paramMap.TryGetValue(token, out replacement))
                {
                    token = replacement;
                }
                sb.Append(token);
            }
            return sb.ToString();
        }

        string GetFunctionName(string token, ref string suffix, ref bool isArray)
        {
            token = token.Trim();
            int paramStart = token.IndexOf(Constants.START_ARG);
            string functionName = paramStart < 0 ? token : token.Substring(0, paramStart);
            suffix = paramStart < 0 ? "" : token.Substring(paramStart);
            int paramEnd = functionName.LastIndexOf(Constants.END_ARG);
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
            isArray = IsArray(functionName, out arrayName, out arrayArg);
            if (isArray)
            {
                functionName = arrayName;
                suffix = arrayArg;
            }

            return functionName;
        }

        string ReplaceArgs(string token)
        {
            int paramStart = token.IndexOf(Constants.START_ARG) + 1;
            string result = "";
            string rest = token.Substring(paramStart).Trim();

            var funcName = token.Substring(0, paramStart);
            funcName = funcName[0].ToString().ToUpperInvariant() + funcName.Substring(1).ToLower();
            if (rest.EndsWith(";"))
            {
                rest = rest.Substring(0, rest.Length - 1);
            }
            rest = rest.Substring(0, rest.Length - 1);

            var tokens = rest.Split(Constants.NEXT_ARG);
            int count = 0;
            foreach (var item in tokens)
            {
                result += ProcessStatement(item, "", false);
                count++;
                result += count != tokens.Length ? Constants.NEXT_ARG : Constants.END_ARG;
            }
            if (paramStart > 0)
            {
                result = funcName + result;
            }
            return result;
        }

        string ProcessFunction(List<string> tokens, ref int id, ref string result, ref bool newVarAdded)
        {
            //string restStr = string.Join("", tokens.GetRange(m_tokenId, tokens.Count - m_tokenId).ToArray());
            string restStr = tokens[m_tokenId];
            int paramStart = restStr.IndexOf('(');
            int paramEnd = paramStart < 0 ? restStr.Length : restStr.IndexOf(')', paramStart + 1);
            while (paramEnd < 0 && ++m_tokenId < tokens.Count)
            {
                paramEnd = tokens[m_tokenId].IndexOf(')');
                restStr += tokens[m_tokenId];
            }
            string functionName = paramStart < 0 ? restStr : restStr.Substring(0, paramStart);
            string argsStr = "";
            if (paramStart >= 0)
            {
                if (paramEnd <= paramStart)
                {
                    paramEnd = restStr.Length;
                }
                ParsingScript tmpScript = new ParsingScript(restStr.Substring(paramStart, restStr.Length - paramStart));
                argsStr = Utils.PrepareArgs(Utils.GetBodyBetween(tmpScript));
            }

            string token = "";
            if (ProcessArray(argsStr, functionName, ref token))
            {
                result += token;
                return token;
            }

            string conversion = "";
            var type = GetVariableType(functionName);
            if (type != Variable.VarType.NONE)
            {
                conversion = type == Variable.VarType.NUMBER ? ".AsDouble()" : ".AsString()";
            }

            StringBuilder sb = new StringBuilder();
            char ch = '(';

            int index = functionName.IndexOf('.');
            if (index > 0 && id < tokens.Count - 2 && tokens[id + 1] == "=")
            {
                //functionName = "SetProperty";
                argsStr = tokens[id + 2];
                sb.AppendLine(m_depth + "__action =\"=\";");
                ch = '=';
                id = tokens.Count;
            }
            else
            {
                sb.AppendLine(m_depth + "__action =\"\";");
            }

            sb.AppendLine(GetCSCSFunction(argsStr,  functionName, ch));

            token = sb.ToString();
            if (tokens.Count >= 3 && tokens[1] == "=" && !string.IsNullOrWhiteSpace(result))
            {
                result = m_depth + token + result + " __tempVar;\n";
                newVarAdded = true;
            }
            else
            {
                result += token;
            }

            return token;
        }

        string GetCSCSFunction(string argsStr, string functionName, char ch = '(')
        {
            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(argsStr) && argsStr.Last() == '"' && argsStr.First() == '"')
            {
                argsStr = "\\\"" + argsStr.Substring(1, argsStr.Length - 2) + "\\\"";
            }

            sb.AppendLine(m_depth + "__argsStr =\"" + argsStr + "\";");
            sb.AppendLine(m_depth + "__script = new ParsingScript(__argsStr);");
            sb.AppendLine(m_depth + "__func = new ParserFunction(__script, \"" + functionName + "\", '" + ch + "', ref __action);");

            if (AsyncMode)
            {
                sb.AppendLine(m_depth + "__tempVar = await __func.GetValueAsync(__script);");
            }
            else
            {
                sb.AppendLine(m_depth + "__tempVar = __func.GetValue(__script);");
            }
            return sb.ToString();
        }

        bool ProcessArray(string paramName, string functionName, ref string result)
        {
            string arrayName, arrayArg, mappingName;
            if (!IsDefinedAsArray(paramName, out arrayName, out arrayArg, out mappingName))
            {
                return false;
            }
            if (functionName.ToLower() == "size")
            {
                result = mappingName + ".Count";
                return true;
            }
            return false;
        }
        bool IsArray(string paramName, out string arrayName, out string arrayArg)
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
        void GetExpressionType(List<string> tokens)
        {
            //m_assigmentExpression = false;
            m_numericExpression = false;
            Variable arg;
            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];
                if (string.IsNullOrWhiteSpace(token) || token[0] == Constants.QUOTE || token == Constants.RETURN)
                {
                    continue;
                }
                string suffix = "";
                bool isArray = false;
                string paramName = GetFunctionName(token, ref suffix, ref isArray);

                var type = GetVariableType(paramName);
                if (type == Variable.VarType.NUMBER)
                {
                    m_numericExpression = true;
                }
                else if (m_argsMap.TryGetValue(token, out arg))
                {
                    m_numericExpression = m_numericExpression || arg.Type == Variable.VarType.NUMBER;
                }
                else if (m_numericVars.Contains(paramName))
                {
                    m_numericExpression = true;
                }
                else if (Constants.ARITHMETIC_EXPR.Contains(paramName))
                {
                    m_numericExpression = true;
                }
                else if (Constants.RESERVED.Contains(paramName))
                {
                    continue;
                }
                else
                {
                    var functionReturnType = GetReturnType(paramName);
                    if (functionReturnType != Variable.VarType.NONE)
                    {
                        m_numericExpression = functionReturnType == Variable.VarType.NUMBER;
                    }
                }
                //if (token.Contains("=")) {
                //  m_assigmentExpression = true;
                //}

            }
        }
        static bool IsNumber(string text)
        {
            double num;
            return Double.TryParse(text, NumberStyles.Number |
                                         NumberStyles.AllowExponent |
                                         NumberStyles.Float,
                                         CultureInfo.InvariantCulture, out num);
        }
        static bool IsString(string text)
        {
            return string.IsNullOrWhiteSpace(text) || text[0] == Constants.QUOTE;
        }

        public static List<string> TokenizeScript(string scriptText)
        {
            List<string> tokens = new List<string>();

            int startIndex = 0;
            int i = 0;
            while (i < scriptText.Length)
            {
                char ch = scriptText[i];
                if (Constants.STATEMENT_SEPARATOR.IndexOf(ch) >= 0)
                {
                    if (i > startIndex)
                    {
                        string token = scriptText.Substring(startIndex, i - startIndex);
                        if (token.EndsWith("=") && scriptText.Substring(i).StartsWith("{};"))
                        {
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
        public static List<string> TokenizeStatement(string statement)
        {
            List<string> tokens = new List<string>();

            int startIndex = 0;
            int i = 0;
            bool inQuotes = false;
            char previous = Constants.EMPTY;
            while (i < statement.Length)
            {
                if (statement[i] == Constants.QUOTE && previous != '\\')
                {
                    inQuotes = !inQuotes;
                }
                else if (inQuotes)
                {
                }
                else
                {
                    string candidate = Utils.ValidAction(statement.Substring(i));
                    if (candidate == null && (Constants.STATEMENT_TOKENS.IndexOf(statement[i]) >= 0))
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
                previous = statement[i];
                i++;
            }

            //tokens = tokens.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

            if (statement.Length > startIndex)
            {
                tokens.Add(statement.Substring(startIndex));
            }

            return tokens;
        }

        public static bool IsMathFunction(string name)
        {
            name = name[0].ToString().ToUpperInvariant() + name.Substring(1).ToLower();

            Type mathType = typeof(System.Math);
            try
            {
                MethodInfo myMethod = mathType.GetMethod(name);
                return myMethod != null;
            }
            catch (AmbiguousMatchException)
            {
                return true;
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
