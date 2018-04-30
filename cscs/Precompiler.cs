using System;
using System.Collections.Generic;
using System.Text;

using System.CodeDom;
using System.CodeDom.Compiler;
using System.IO;
using Microsoft.CSharp;
//using Mono.CSharp;
using System.Reflection;
using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;
using static SplitAndMerge.ParserFunction;

namespace SplitAndMerge
{
  public partial class Precompiler
  {
    //static Dictionary<string, Func<ParsingScript, Variable>> m_compiledCode =
    //   new Dictionary<string, Func<ParsingScript, Variable>> ();
    static Dictionary<string, string> m_returnTypes = new Dictionary<string, string> ();

    string m_functionName;
    string m_cscsCode;
    string m_csCode;
    string [] m_defArgs;
    Dictionary<string, Variable> m_argsMap;
    HashSet<string> m_numericVars = new HashSet<string> ();
    Dictionary<string, string> m_paramMap    = new Dictionary<string, string> ();

    HashSet<string> m_newVariables = new HashSet<string>();
    List<string> m_statements;
    Variable.VarType m_returnType;
    int m_statementId;
    int m_tokenId;
    string m_currentStatement;
    string m_nextStatement;
    string m_depth;
    bool m_numericExpression;
    bool m_assigmentExpression;
    bool m_lastStatementReturn;

    Func<List<string>, List<double>, Variable> m_compiledFunc;

    public static void RegisterReturnType (string functionName, string functionType)
    {
      string adjType = Constants.TypeToString (Constants.StringToType(functionType));
      m_returnTypes[functionName] = adjType;
    }
    public static string GetReturnType(string functionName)
    {
      string adjType = "NONE";
      m_returnTypes.TryGetValue(functionName, out adjType);
      return adjType;
    }

    public Precompiler(string functionName, string [] args, Dictionary<string, Variable> argsMap, string cscsCode)
    {
      m_functionName = functionName;
      m_defArgs = args;
      m_argsMap = argsMap;
      m_cscsCode = cscsCode;
      m_returnType = Constants.StringToType(GetReturnType(m_functionName));
    }

    public void Compile()
    {
      m_csCode = ConvertScript();
      CompilerParameters CompilerParams = new CompilerParameters();

      CompilerParams.GenerateInMemory = true;
      CompilerParams.TreatWarningsAsErrors = false;
      CompilerParams.GenerateExecutable = false;
      //CompilerParams.CompilerOptions = "/optimize";

      Assembly [] assemblies = AppDomain.CurrentDomain.GetAssemblies();
      foreach (Assembly asm in assemblies) {
        //FileVersionInfo fvi = FileVersionInfo.GetVersionInfo (asm.Location);
        AssemblyName asmName = asm.GetName();
        if (asmName == null || asmName.CodeBase == null ||
          (!asmName.CodeBase.EndsWith (".exe") && asmName.GetPublicKeyToken().Length == 0)) {
          continue;
        }
        string suffix = asmName.CodeBase.EndsWith(".exe") ? ".exe" : "";
        CompilerParams.ReferencedAssemblies.Add(asmName.Name + suffix);
      }

      CSharpCodeProvider provider = new CSharpCodeProvider();
      CompilerResults compile = provider.CompileAssemblyFromSource(CompilerParams, m_csCode);

      if (compile.Errors.HasErrors) {
        string text = "Compile error: ";
        foreach (CompilerError ce in compile.Errors) {
          text += ce.ToString() + " -- ";
        }

        throw new ArgumentException(text);
      }
      m_compiledFunc = CompileAndCache(compile, m_functionName, m_defArgs, m_argsMap);
    }
    public static Func<List<string>, List<double>, Variable> CompileAndCache(CompilerResults compile, string functionName,
                                       string [] args, Dictionary<string, Variable> argsMap)
    {
      Module module = compile.CompiledAssembly.GetModules()[0];
      Type mt = module.GetType("SplitAndMerge.Precompiler");

      //ParameterExpression param1 = Expression.Parameter (typeof (ParsingScript), "__script");
      ParameterExpression param1 = Expression.Parameter (typeof (List<string>), "__variableStr");
      ParameterExpression param2 = Expression.Parameter (typeof (List<double>), "__variableNum");
      List<ParameterExpression> paramTypes = new List<ParameterExpression> ();
      paramTypes.Add (param1);
      paramTypes.Add (param2);
      List<Type> argTypes = new List<Type> ();
      argTypes.Add (typeof (List<string>));
      argTypes.Add (typeof (List<double>));

      //MethodInfo methodInfo = mt.GetMethod (functionName, new Type [] { typeof (ParsingScript) });
      MethodInfo methodInfo = mt.GetMethod (functionName, argTypes.ToArray());

      MethodCallExpression methodCall = Expression.Call (methodInfo, param1, param2);
      //MethodCallExpression methodCall = Expression.Call (methodInfo, param2);

      //Expression<Func<ParsingScript, List<Variable>, Variable>> lambda =
      //  Expression.Lambda<Func<ParsingScript, List< Variable >, Variable>> (methodCall, paramTypes.ToArray());
      var lambda =
        Expression.Lambda<Func<List<string>, List<double>, Variable>> (methodCall, paramTypes.ToArray());
      var func = lambda.Compile ();

      return func;
    }

    //public static Variable Run (string functionName, ParsingScript script, List<Variable> args, bool throwExc = true)
    public Variable Run(List<string> argsStr, List<double> argsNum, bool throwExc = true)
    {
      if (m_compiledFunc == null) {
        // For "late bindings"...
        Compile();
      }

      Variable result = m_compiledFunc.Invoke(argsStr, argsNum);
      return result;
    }

    public static Variable.VarType GetVariableType(string paramName)
    {
      if (IsNumber(paramName)) {
        return Variable.VarType.NUMBER;
      } else if (IsString(paramName)) {
        return Variable.VarType.STRING;
      }

      if (Constants.RESERVED.Contains(paramName) ||
          Constants.TOKEN_SEPARATION_STR.Contains(paramName)) {
        return Variable.VarType.NONE;
      }

      string functionReturnType = GetReturnType(paramName);
      if (functionReturnType != null) {
        return Constants.StringToType(functionReturnType);
      }

      ParserFunction function = ParserFunction.GetFunction(paramName);
      if (function == null) {
        return Variable.VarType.NONE;
      }

      if (function is INumericFunction) {
        return Variable.VarType.NUMBER;
      } else if (function is IStringFunction) {
        return Variable.VarType.STRING;
      } else if (function is IArrayFunction) {
        return Variable.VarType.ARRAY;
      }

      return Variable.VarType.NONE;
    }

    public string RegisterVariableString(string paramName, string paramValue = "")
    {
      if (string.IsNullOrEmpty(paramValue)) {
        paramValue = paramName;
      }
      return m_depth + "ParserFunction.AddGlobalOrLocalVariable(\"" + paramName +
        "\", new GetVarFunction(new Variable(" + paramValue + ")));\n";
    }

    public string ConvertScript()
    {
      Dictionary<int, int> char2Line = null;
      m_cscsCode = Utils.ConvertToScript(m_cscsCode, out char2Line);

      ParsingScript script = new ParsingScript (m_cscsCode);
      StringBuilder sb = new StringBuilder ();

      //string argStr = ", List<Variable> __variable";
      int numIndex = 0;
      int strIndex = 0;
      for (int i = 0; i < m_defArgs.Length; i++) {
        Variable typeVar = m_argsMap[m_defArgs[i]];
        //argStr += ", " + TypeToCSString (typeVar.Type) + " " + args [i];
        m_paramMap[m_defArgs[i]] = typeVar.Type == Variable.VarType.NUMBER ?
                                     "__variableNum[" + (numIndex++) + "]" :
                                     "__variableStr[" + (strIndex++) + "]";
      }
      //string argStr = args.Length == 0 ? "" : "," + string.Join(",", args);

      sb.AppendLine ("using System; using System.Collections; using System.Collections.Generic;\n\n" +
                     "namespace SplitAndMerge {\n" +
                     "  public partial class Precompiler {\n" +
                     "    public static Variable " + m_functionName + "(List<string> __variableStr, List<double> __variableNum) {");
      m_depth      = "      ";

      m_statements = TokenizeScript(m_cscsCode);
      m_statementId = 0;
      while (m_statementId < m_statements.Count) {
        m_currentStatement = m_statements[m_statementId];
        m_nextStatement = m_statementId < m_statements.Count - 1 ? m_statements [m_statementId+1] : "";
        string converted = ProcessStatement(m_currentStatement, m_nextStatement);
        if (!converted.StartsWith(m_depth)) {
          sb.Append(m_depth);
        }
        sb.Append(converted);
        m_statementId++;
      }

      if (!m_lastStatementReturn) {
        sb.AppendLine(m_depth + "return Variable.EmptyInstance;");
      }

      sb.AppendLine("\n    }\n  }\n}");
      return sb.ToString();
    }

    string ProcessStatement(string statement, string nextStatement, bool addNewVars = true)
    {
      if (statement == ";") {
        // don't need this - will be added from the previous statement.
        return "";
      }
      List<string> tokens = TokenizeStatement(statement);
      List<string> statementVars = new List<string> ();

      string result = "";
      m_lastStatementReturn = ProcessReturnStatement(tokens, statementVars, ref result);
      if (m_lastStatementReturn) {
        return result;
      }
      if (ProcessForStatement(statement, tokens, statementVars, ref result)) {
        return result;
      }

      GetExpressionType(tokens);
      m_tokenId = 0;
      while (m_tokenId < tokens.Count) {
        bool newVarAdded = false;
        string token = tokens[m_tokenId];
        string converted = ProcessToken(tokens, m_tokenId, ref newVarAdded);
        if (addNewVars && newVarAdded) {
          statementVars.Add (token);
          if (m_numericExpression) {
            m_numericVars.Add(token);
          }
        }
        result += converted;
        m_tokenId++;
      }

      if (result == "{") {
        m_depth += "  ";
      } else if (result == "}") {
        if (m_depth.Length <= 4) {
          throw new ArgumentException("Mismatch of { } parentheses in " + m_functionName);
        }
        m_depth = m_depth.Substring(0, m_depth.Length - 2);
      }

      if (statementVars.Count > 0 || (addNewVars &&
                  statement != "}" && statement != "{" && nextStatement != "{")) {
        result += ";\n";
      } else if (addNewVars)  {
        result += "\n";
      }
      for (int i = 0; i < statementVars.Count; i++) {
        result += RegisterVariableString (statementVars [i]);
      }

      return result;
    }

    bool ProcessReturnStatement(List<string> tokens, List<string> statementVars,
                                ref string converted)
    {
      string suffix = "";
      string paramName = GetFunctionName(tokens[0], ref suffix);
      if (paramName != Constants.RETURN) {
        return false;
      }
      if (tokens.Count <= 2) {
        converted = m_depth + "return Variable.EmptyInstance;\n";
        return true;
      }
      if (!string.IsNullOrWhiteSpace(suffix)) {
        // Converting the case "return(..." to the normal case "return (..".
        tokens [2] = suffix + tokens [1] + tokens [2];
      }
      if (tokens.Count == 3) {
        bool newVarAdded = false;
        m_tokenId = 2;
        string token = ProcessToken(tokens, m_tokenId, ref newVarAdded);
        converted += m_depth + "return new Variable(" + token + ");\n";
        return true;
      }

      string remaining = string.Join("", tokens.GetRange (2, tokens.Count - 2));
      string returnToken = ProcessStatement(remaining, "", false);

      converted = m_depth + "return new Variable(" + returnToken + ");\n";

      return true;
    }

    bool ProcessForStatement(string statement, List<string> tokens, List<string> statementVars,
                             ref string converted)
    {
      string suffix = "";
      string functionName = GetFunctionName(statement, ref suffix);
      if (functionName != Constants.FOR) {
        return false;
      }
      if (m_nextStatement != Constants.END_STATEMENT.ToString()) {
        return false;
      }
      if (m_statements.Count <= m_statementId + 4) {
        throw new ArgumentException ("Expecting: for(init; condition; loopStatement)");
      }

      string rest = "";
      string varName = GetFunctionName(suffix.Substring(1), ref rest);

      converted = "";
      if (!m_newVariables.Contains (varName)) {
        m_newVariables.Add(varName);
        converted = m_depth + "var " + varName + rest + ";\n";
      }
      converted += m_depth + statement + "; ";
      m_statementId += 2;
      converted += ProcessStatement (m_statements[m_statementId], "", false) + "; ";
      m_statementId += 2;
      converted += ProcessStatement (m_statements[m_statementId], "", false) + " {\n";
      m_statementId++;

      m_depth += "  ";
      converted += RegisterVariableString(varName);

      return true;
    }
    string ProcessCatch(string exceptionVar)
    {
      string suffix = "";
      string varName = GetFunctionName(exceptionVar.Substring(1), ref suffix);

      string result =  "catch(Exception " + varName + ") {\n";
      result += RegisterVariableString(varName, varName + ".ToString()");
      m_statementId++;
      return result;
    }
    string ProcessElIf(string elif)
    {
      string result = elif.Replace(Constants.ELSE_IF, "else if");
      return result;
    }

    string ProcessToken(List<string> tokens, int id, ref bool newVarAdded)
    {
      string token = tokens[id].Trim();
      string suffix = "";
      string functionName = GetFunctionName(token, ref suffix);
      if (string.IsNullOrEmpty (functionName)) {
        return suffix;
      }

      bool reservedWord = Constants.RESERVED.Contains(functionName);
      if (IsString(functionName) || IsNumber(functionName) || reservedWord) {
        if (functionName == Constants.CATCH) {
          token = ProcessCatch(suffix);
        } else if (functionName == Constants.ELSE_IF) {
          token = ProcessElIf(token);
        }
        return token;
      }
      if (Array.IndexOf (Constants.ACTIONS, token) >= 0) {
        //if (id <= 1 && (token == "++" || token == "--" || Array.IndexOf(Constants.OPER_ACTIONS, token) >= 0)) {
        //  newVarAdded = true;
        //}
        return token;
      }
      if (m_newVariables.Contains(functionName)) {
        if (id == 0) {
          newVarAdded = true;
        }
        return token;
      }

      if (id == 0 && tokens.Count > id + 1 && tokens[id + 1] == "=") {
        m_newVariables.Add(functionName);
        newVarAdded = true;
        return m_depth + "var " + functionName;
      }

      Variable arg;
      if (m_argsMap.TryGetValue(functionName, out arg)) {
        //return (arg.Type == Variable.VarType.NUMBER ? "    Utils.GetDouble(\"" : "    Utils.GetString(\"") + token + "\")";
        string actualName = m_paramMap[functionName];
        return " " + actualName + suffix;
      }
      return ProcessFunction(tokens, id, ref newVarAdded);
    }

    static string GetFunctionName(string token, ref string suffix)
    {
      token = token.Trim();
      int paramStart = token.IndexOf(Constants.START_ARG);
      string functionName = paramStart < 0 ? token : token.Substring(0, paramStart);
      suffix = paramStart < 0 ? "" : token.Substring(paramStart);
      int paramEnd = functionName.LastIndexOf(Constants.END_ARG);
      if (paramEnd < 0) {
        paramEnd = functionName.IndexOf("=");
      }
      if (paramEnd >= 0) {
        suffix = functionName.Substring(paramEnd);
        functionName = functionName.Substring (0, paramEnd);
      }
      return functionName;
    }

    string ProcessFunction(List<string> tokens, int id, ref bool newVarAdded)
    {
      //string restStr = string.Join("", tokens.GetRange(m_tokenId, tokens.Count - m_tokenId).ToArray());
      string restStr = tokens[m_tokenId];
      int paramStart = restStr.IndexOf ("(");
      int paramEnd   =   paramStart < 0 ? restStr.Length : restStr.IndexOf (")", paramStart + 1);
      while (paramEnd < 0 && ++m_tokenId < tokens.Count) {
        paramEnd = tokens[m_tokenId].IndexOf (")");
        restStr += tokens[m_tokenId];
      }
      string functionName = paramStart < 0 ? restStr : restStr.Substring(0, paramStart);
      string argsStr = "";
      if (paramStart >= 0) {
        if (paramEnd <= paramStart) {
          paramEnd = restStr.Length;
        }
        ParsingScript script = new ParsingScript(restStr.Substring(paramStart, paramEnd - paramStart));
        argsStr = Utils.PrepareArgs(Utils.GetBodyBetween(script));
      }

      string conversion = "";
      var type = GetVariableType(functionName);
      if (type != Variable.VarType.NONE) {
        conversion = type == Variable.VarType.NUMBER ? ".AsDouble()" : ".AsString()";
      }

      string result = "Utils.RunCompiled(\"" + functionName + "\", \"" + argsStr + "\")" + conversion;
      return result;
    }

    void GetExpressionType(List<string> tokens)
    {
      m_assigmentExpression = false;
      m_numericExpression   = false;
      Variable arg;
      for (int i = 0; i < tokens.Count; i++) {
        string token = tokens[i];
        if (token[0] == Constants.QUOTE) {
          continue;
        }
        string suffix = "";
        string paramName = GetFunctionName(token, ref suffix);

        var type = GetVariableType(paramName);
        if (type == Variable.VarType.NUMBER) {
          m_numericExpression = true;
        } else if (m_argsMap.TryGetValue(token, out arg)) {
          m_numericExpression = m_numericExpression || arg.Type == Variable.VarType.NUMBER;
        } else if (m_numericVars.Contains (paramName)) {
          m_numericExpression = true;
        } else if (Constants.ARITHMETIC_EXPR.Contains (paramName)) {
          m_numericExpression = true;
        } else if (Constants.RESERVED.Contains (paramName)) {
          continue;
        } else {
          string functionReturnType = GetReturnType (paramName);
          if (functionReturnType != null) {
            m_numericExpression = functionReturnType == "NUMBER";
          }
        }
        if (token.Contains("=")) {
          m_assigmentExpression = true;
        }

      }
    }
    static bool IsNumber (string text)
    {
      double num;
      return Double.TryParse (text, NumberStyles.Number |
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
      for (int i = 0; i < scriptText.Length; i++) {
        char ch = scriptText[i];
        if (Constants.STATEMENT_SEPARATOR.IndexOf(ch) >= 0) {
          if (i > startIndex) {
            string token = scriptText.Substring(startIndex, i - startIndex);
            tokens.Add(token);
          }
          tokens.Add(ch.ToString());
          startIndex = i + 1;
        }
      }
      if (scriptText.Length > startIndex + 1) {
        tokens.Add (scriptText.Substring(startIndex));
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
      while (i < statement.Length) {
        if (statement [i] == Constants.QUOTE && previous != '\\') {
          inQuotes = !inQuotes;
        } else if (inQuotes) {
        } else {
          string candidate = Utils.ValidAction (statement.Substring (i));
          if (candidate == null && (Constants.STATEMENT_TOKENS.IndexOf (statement [i]) >= 0)) {
            candidate = statement[i].ToString ();
          }
          if (candidate != null) {
            if (i > startIndex) {
              string token = statement.Substring(startIndex, i - startIndex);
              tokens.Add (token);
            }
            tokens.Add (candidate);
            previous = statement [i];
            i += candidate.Length;
            startIndex = i;
            continue;
          }
        }
        previous = statement[i];
       i++;
      }

      if (statement.Length > startIndex) {
        tokens.Add (statement.Substring (startIndex));
      }

      return tokens;
    }
    static void ExpoloreAssembly(Assembly assembly)
    {
      Console.WriteLine("Modules in the assembly:");
      foreach (Module m in assembly.GetModules()) {
        Console.WriteLine("{0}", m);

        foreach (Type t in m.GetTypes()) {
          Console.WriteLine("t{0}", t.Name);

          foreach (MethodInfo mi in t.GetMethods()) {
            Console.WriteLine("tt{0}", mi.Name);
          }
        }
      }
    }
    public static string TypeToCSString(Variable.VarType type)
    {
      switch (type) {
      case Variable.VarType.NUMBER:   return "double";
      case Variable.VarType.STRING:   return "string";
      case Variable.VarType.ARRAY:    return "List<Variable>";
      case Variable.VarType.BREAK:    return "break";
      case Variable.VarType.CONTINUE: return "continue";
      default: return "string";
      }
    }
    public static Type CSCSTypeToCSType(Variable.VarType type)
    {
      switch (type) {
      case Variable.VarType.NUMBER: return typeof (double);
      case Variable.VarType.STRING: return typeof (string);
      case Variable.VarType.ARRAY:  return typeof (List<Variable>);
      default: return typeof (string);
      }
    }
  }
}
