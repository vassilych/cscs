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
        // The argument types as declared. m_argsMap is the translator's own copy, with the
        // collection arguments taken out -- the original is also what the runtime prepares
        // the arguments from, so it is never changed.
        Dictionary<string, Variable> m_declaredArgsMap;
        HashSet<string> m_collectionArgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Loop counters declared "int" rather than "double" -- see FindIntCounters.
        HashSet<string> m_intCounters = new HashSet<string>();
        // Int arguments the body assigns to. They become double locals: see ConvertScript.
        HashSet<string> m_widenedIntArgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> m_widenedIntSlots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] m_defaultArgs;
        StringBuilder m_converted = new StringBuilder();
        Dictionary<string, Variable> m_argsMap;
        Dictionary<string, string> m_paramMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
        // Locals that hold a Variable rather than a double or a string, because somewhere in
        // the function they are assigned an element read out of a collection. Collected before
        // any statement is translated: the declaration comes from the first assignment, which
        // may be "v = 0" long before "v = m[key]" reveals what v really has to hold.
        HashSet<string> m_variableLocals = new HashSet<string>();
        // Locals first assigned at the function's own top level, so declared in the scope that
        // every later statement sees -- see DeclareBlockCrossingLocals.
        HashSet<string> m_topLevelAssigned = new HashSet<string>();
        // Names the function declares with "var": locals even where the interpreter holds a
        // global of that name, so no global read or write-back may reach them.
        HashSet<string> m_varLocals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Every name the body assigns, steps, loops over or catches into: a local, wherever in the
        // body it is first read. Only a name outside this set can be read late (CscsLate).
        HashSet<string> m_assignedAnywhere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> m_readFirst = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // The read-first names whose first write reads them: "x = !x" (ReadBeforeAssigned).
        HashSet<string> m_selfReadFirst = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // ProcessFunction is handling a name with no parentheses of its own (GetCSCSFunction).
        bool m_nameWithoutCall;
        // Locals every one of whose assignments is a string. Only those can take part in a
        // rewritten string comparison: a local that is ever given a number has to keep the
        // numeric comparison, which orders by value rather than by text.
        HashSet<string> m_stringLocals = new HashSet<string>();
        // The C# type every assignment to a local agrees on -- "double", "string", "bool" or
        // "Variable" -- for the locals where they do agree. Only used to answer ".Type", which
        // the interpreter answers with the name of the CSCS type: a C# double, string or bool
        // has no such member, so without this the function fell back. A local whose
        // assignments disagree is absent here and keeps falling back, rather than being given
        // a guessed answer.
        Dictionary<string, string> m_localTypes = new Dictionary<string, string>();
        // Whether the statement being translated compares with "<" or ">". A condition is
        // tokenized on its operator, so each side reaches the resolver on its own and cannot
        // see the comparison it belongs to -- the flag carries that across.
        bool m_statementRelational;
        // Whether the statement accumulates into a Variable local with "+=". The element on
        // the right has to stay a Variable then, so that Variable's own "+" picks between
        // concatenation and addition the way the interpreter does.
        bool m_statementVariableAccum;
        // Whether the statement combines bits. C# needs whole numbers for that, and an
        // element arrives as a double, so those operands take a cast the others do not.
        bool m_statementBitwise;
        // Whether the statement compares through Variable.SameValue. Its operands keep their
        // runtime type -- that is the whole point of calling it -- so an element in one is not
        // converted to a number the way an ordinary argument is.
        bool m_statementSameValue;
        // Whether a script call in the statement has to stay where it is, as an expression:
        // inside "&&", "||" or "?:", or in a loop's condition. See CscsCalls.
        bool m_statementInlineCalls;
        bool m_forceInlineCalls;
        // Whether the statement has a string literal anywhere. A condition arrives in pieces,
        // split on its comparison, so the piece holding a call cannot see the "\"t\"" it is
        // compared with.
        bool m_statementHasString;
        // Statement positions holding the "while" that closes a do-loop, so that it is
        // emitted as the loop's tail rather than as a new loop. A set rather than one
        // value, so that nested do-loops each keep their own.
        HashSet<int> m_doWhileTails = new HashSet<int>();
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
        // Enums declared inside the function, with the member names each one was given.
        Dictionary<string, List<string>> m_enumLocals = new Dictionary<string, List<string>>();
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

        /// <summary>The C# types of the typed entry point ("int", "double", "string",
        /// "Variable"), or null when the function has none (MakeDirectSelfCalls).</summary>
        public List<string> DirectTypes { get; private set; }

        /// <summary>Whether compiled functions call each other and themselves through their typed
        /// entry points (MakeDirectSelfCalls, CscsDirect). On by default; off, every call goes
        /// through the interpreter's call as before.</summary>
        public static bool DirectCalls { get; set; } = true;

        // The code without typed returns, kept while CSharpCode holds the variant with them: that
        // variant is proved by compiling it, and when it does not compile this one is used.
        string m_untypedCSharpCode;
        internal string UntypedCSharpCode { get { return m_untypedCSharpCode; } }

        /// <summary>The typed entry point, NAME__direct, as a Func of those types, once compiled.
        /// A compiled caller runs it in place of the interpreter's call (CscsDirect.Call).</summary>
        public Delegate Direct { get; private set; }

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
            // A copy: ProcessDefaultArgs strips the default off each argument ("n = 5" ->
            // "n"), and the caller hands the same array to CustomCompiledFunction afterwards,
            // which reads the defaults from it. Stripping it in place left a compiled function
            // with no defaults, so "f()" on "cfunction f(int n = 5)" was an argument mismatch.
            m_actualArgs = (string[])args.Clone();
            m_argsMap = argsMap;
            m_declaredArgsMap = argsMap;
            m_originalCode = cscsCode;
            m_returnType = GetReturnType(m_functionName);
            m_parentScript = parentScript;

            ProcessDefaultArgs();
        }

        /// <summary>
        /// Stops the translation of anything whose C# spelling would not mean what the
        /// interpreter means by it. Throwing leaves the whole function to the interpreter,
        /// which is the granularity the fallback works at.
        ///   "iff(c, a, b)": a statement in the interpreter, which needs the script around it.
        ///     Called back with only its arguments it threw "Couldn't skip expression".
        /// </summary>
        static void RefuseUntranslatable(string statement)
        {
            bool inQuotes = false;
            for (int i = 0; i < statement.Length; i++)
            {
                var ch = statement[i];
                if (ch == '"' && !IsEscapedQuote(statement, i))
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (inQuotes)
                {
                    continue;
                }
                if ((ch == 'i' || ch == 'I') && (i == 0 || !char.IsLetterOrDigit(statement[i - 1])) &&
                    i + 4 <= statement.Length && statement.Substring(i, 4).ToLower() == "iff(")
                {
                    throw new ArgumentException("iff() needs the interpreter: " + statement);
                }
                // "e.Stack" on a catch variable is the interpreter's call chain at the throw
                // (Interpreter.CreateExceptionStack), which compiled code has no record of.
                if (ch == '.' && i + 6 <= statement.Length &&
                    string.Equals(statement.Substring(i + 1, 5), "stack", StringComparison.OrdinalIgnoreCase) &&
                    (i + 6 == statement.Length || !char.IsLetterOrDigit(statement[i + 6]) && statement[i + 6] != '_'))
                {
                    throw new ArgumentException("A caught exception's Stack needs the interpreter: " + statement);
                }
            }
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
                var realArg = parts[0].Trim();
                var defValue = parts[1].Trim();
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
            s_scriptFunctions = ScriptFunctionsShadowingMath();
            if (!scriptInCSharp)
            {
                m_cscsCode = RewriteNestedNew(RewriteIfAssignment(RewriteTernaryNew(RewriteComparisonsAsNumbers(
                    RewriteChainedComparisons(RewriteIff(RewriteTextTimesNumber(RewriteBraceLiterals(RewriteVariableMemberCalls(RewriteVarDeclarations(RewriteCaseBlocks(RewriteSizeCalls(RewriteForEachSeparator(RewriteBracketLiterals(RewriteTypeCalls(RewritePrefixStepInIf(RewriteReturnStep(RewriteForEachLiterals(RewriteBracelessBodies(TerminateLiteralStatements(RewriteNamedArgumentCalls(NormalizeArgSpelling(m_cscsCode))))))))))))))))))))));
            }
            RemoveIrrelevant(m_cscsCode);
            m_assignedAnywhere = AssignedAnywhere(m_cscsCode);
            m_selfReadFirst = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            m_readFirst = ReadBeforeAssigned(m_cscsCode, m_assignedAnywhere, m_selfReadFirst);
            var callText = WithoutStringContents(m_cscsCode ?? "");
            s_localNames = new HashSet<string>(m_assignedAnywhere.Concat(m_declaredArgsMap.Keys)
                .Where(local => !System.Text.RegularExpressions.Regex.IsMatch(callText,
                    @"(?<![\w.])" + System.Text.RegularExpressions.Regex.Escape(local) + @"\s*\(",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
                StringComparer.OrdinalIgnoreCase);

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

            if (!scriptInCSharp && m_usesInterpreter)
            {
                // Where it can call back, a local standing for an interpreter variable is read
                // from the interpreter (ReadGlobalsThroughInterpreter).
                var bound = GlobalBoundNames();
                CSharpCode = RoslynCompiler.ReadGlobalsThroughInterpreter(CSharpCode, bound);
                if (m_untypedCSharpCode != null)
                {
                    m_untypedCSharpCode = RoslynCompiler.ReadGlobalsThroughInterpreter(m_untypedCSharpCode, bound);
                }
            }
            if (!scriptInCSharp)
            {
                CSharpCode = TruthTestLogicalOperands(CSharpCode);
                CSharpCode = ScriptMembersAsFields(CSharpCode);
                CSharpCode = MakeDirectSelfCalls(CSharpCode);
                // "return x++" came back as "__rsN = x; x++; return __rsN;" (RewriteReturnStep): the
                // interpreter's postfix step returns the number underneath the old value, which for
                // text is not the text.
                CSharpCode = StepValueReturns(CSharpCode);
                if (m_untypedCSharpCode != null)
                {
                    m_untypedCSharpCode = StepValueReturns(m_untypedCSharpCode);
                }
                CSharpCode = RoslynCompiler.InterpreterCompoundsAndElements(RoslynCompiler.NumbersForBoolsInText(
                    RoslynCompiler.FixVariableEquality(RoslynCompiler.WidenIntArithmetic(CSharpCode))));
                if (m_untypedCSharpCode != null)
                {
                    m_untypedCSharpCode = RoslynCompiler.InterpreterCompoundsAndElements(RoslynCompiler.NumbersForBoolsInText(
                        RoslynCompiler.FixVariableEquality(RoslynCompiler.WidenIntArithmetic(m_untypedCSharpCode))));
                }
            }
            return CSharpCode;
        }

        /// <summary>A "for" counter the interpreter would write as a global: one exists under the
        /// name, and nothing in the function (an argument, a "var") hides it.</summary>
        bool IsGlobalCounter(string name)
        {
            var interpreter = m_parentScript?.InterpreterInstance;
            return !m_scriptInCSharp && interpreter != null && IsPlainName(name) && !m_varLocals.Contains(name) &&
                   !m_declaredArgsMap.Keys.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) &&
                   interpreter.GetVariable(name, m_parentScript) is GetVarFunction;
        }

        /// <summary>
        /// The names this function assigns that are the interpreter's rather than its own: a
        /// global when the function was translated -- an assignment inside a function writes an
        /// existing global -- or a name read before it is assigned (ReadBeforeAssigned). Not an
        /// argument and not a "var" local, which hide a global of their name.
        /// </summary>
        List<string> GlobalBoundNames()
        {
            var interpreter = m_parentScript?.InterpreterInstance;
            return m_assignedAnywhere.Where(name =>
                    !name.StartsWith("__", StringComparison.Ordinal) && !m_varLocals.Contains(name) &&
                    !m_declaredArgsMap.Keys.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) &&
                    (m_readFirst.Contains(name) ||
                     (interpreter != null && interpreter.GetVariable(name, m_parentScript) is GetVarFunction)))
                .ToList();
        }

        /// <summary>
        /// C#'s Variable has members of its own that scripts cannot mean -- Count, Value, String,
        /// Tuple, Object -- and a script's "v.Count" copied into the C# bound to them: 2 for a
        /// list, where the interpreter looks the property up and does not find it. On a local
        /// holding a Variable or a "variable" argument, such a member (not called) is read as the
        /// interpreter reads a property (ReadField). Generated code never uses them on those names.
        /// </summary>
        string ScriptMembersAsFields(string code)
        {
            var holders = new HashSet<string>(m_variableLocals.Concat(m_collectionLocals), StringComparer.Ordinal);
            return System.Text.RegularExpressions.Regex.Replace(code,
                @"(?<![\w.""])([A-Za-z_]\w*|__varVar\[\d+\])\.(Count|Value|String|Tuple|Object|ObjectType)\b(?!\s*\()",
                m =>
                {
                    var owner = m.Groups[1].Value;
                    bool scriptHolder = owner.StartsWith(CSCS_VAR_ARG + "[", StringComparison.Ordinal) ||
                                        (holders.Contains(owner) && !owner.StartsWith("__", StringComparison.Ordinal));
                    return scriptHolder && !InsideQuotes(code, m.Index) ?
                        owner + ".ReadField(\"" + m.Groups[2].Value + "\")" : m.Value;
                });
        }

        /// <summary>
        /// Recursion without the interpreter. A function whose body needs nothing of the
        /// interpreter's except calls to itself -- recursive fib, gcd, a power by squaring -- and
        /// whose arguments are all scalars is split in three: the entry point the interpreter
        /// calls keeps its signature and only unpacks the argument lists into typed values;
        /// "NAME__body" is the body with those values as parameters; "NAME__direct" counts the
        /// depth (CscsDirect) and calls the body. Each call to itself goes to NAME__direct with
        /// the arguments converted as PrepareArgs converts them, instead of CscsCalls.Call's
        /// lookup, argument frame and nine lists. Nothing observes the frames it skips: the body
        /// reads no variable, calls no function and publishes no local through the interpreter.
        /// The first call, from the interpreter, still pushes its level, so the depth counted is
        /// the same as before and MaxCallDepth stops at the same call. Anything that does not fit
        /// leaves the code as it was.
        /// </summary>
        string MakeDirectSelfCalls(string code)
        {
            DirectTypes = null;
            m_untypedCSharpCode = null;
            if (!DirectCalls || AsyncMode || !IsStatic || m_scriptInCSharp || m_usesInterpreter || m_collectionArgs.Count > 0 ||
                string.IsNullOrEmpty(m_functionName))
            {
                return code;
            }
            var selfCall = new System.Text.RegularExpressions.Regex(
                @"CscsCalls\.Call\(__interpreter,\s*""" + System.Text.RegularExpressions.Regex.Escape(m_functionName) + @"""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var types = new List<string>();
            var slots = new List<string>();
            foreach (var arg in m_actualArgs)
            {
                if (!m_declaredArgsMap.TryGetValue(arg, out var declared))
                {
                    return code;
                }
                string slot;
                if (!m_paramMap.TryGetValue(arg, out slot) && !m_widenedIntSlots.TryGetValue(arg, out slot))
                {
                    return code;
                }
                switch (declared.Type)
                {
                    case Variable.VarType.INT: types.Add("int"); break;
                    case Variable.VarType.NUMBER: types.Add("double"); break;
                    case Variable.VarType.STRING: types.Add("string"); break;
                    case Variable.VarType.VARIABLE: types.Add("Variable"); break;
                    default: return code;
                }
                slots.Add(slot);
            }

            var header = System.Text.RegularExpressions.Regex.Match(code,
                @"public static Variable " + System.Text.RegularExpressions.Regex.Escape(m_functionName) +
                @"\s*\(Interpreter __interpreter,[^)]*List<Variable> " + CSCS_VAR_ARG + @"\)\s*\{");
            if (!header.Success)
            {
                return code;
            }
            int open = header.Index + header.Length - 1;
            int close = MatchingBraceInCSharp(code, open);
            if (close < 0)
            {
                return code;
            }
            var body = code.Substring(open + 1, close - open - 1);
            var parameters = new List<string>();
            for (int i = 0; i < slots.Count; i++)
            {
                body = body.Replace(slots[i], "__p" + i);
                parameters.Add(types[i] + " __p" + i);
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(body,
                    @"__var(Str|Num|Int|ArrStr|ArrNum|ArrInt|MapStr|MapNum|Var)\b"))
            {
                return code;
            }
            body = DirectSelfCalls(body, selfCall, types);
            if (body == null)
            {
                return code;
            }

            var name = m_functionName;
            var typedParams = string.Concat(parameters.Select(p => ", " + p));
            var names = string.Concat(Enumerable.Range(0, slots.Count).Select(i => ", __p" + i));
            var nl = Environment.NewLine;
            var entry = "{" + nl +
                        "        return " + name + "__body(__interpreter" + string.Concat(slots.Select(s => ", " + s)) + ");" + nl +
                        "    }" + nl;
            var bodyMethod = "    public static Variable " + name + "__body(Interpreter __interpreter" + typedParams + ") {" +
                             body + "}" + nl;
            var directMethod = "    public static Variable " + name + "__direct(Interpreter __interpreter" + typedParams + ") {" + nl +
                               "        CscsDirect.Enter(__interpreter);" + nl +
                               "        try { return " + name + "__body(__interpreter" + names + "); }" + nl +
                               "        finally { CscsDirect.Leave(); }" + nl +
                               "    }" + nl;
            DirectTypes = types;
            var untyped = code.Substring(0, open) + entry + bodyMethod + directMethod + code.Substring(close + 1);
            var numBody = TypedReturnBody(body);
            if (numBody == null)
            {
                return untyped;
            }
            // Typed returns: the body again, returning a double, its calls to itself taking and
            // giving doubles (NAME__numdirect). The entry points build their Variable from it,
            // which for a number is the Variable the untyped body returned.
            var slotArgs = string.Concat(slots.Select(s => ", " + s));
            var typedEntry = "{" + nl +
                             "        return Variable.ConvertToVariable(" + name + "__numbody(__interpreter" + slotArgs + "));" + nl +
                             "    }" + nl;
            var numMethod = "    public static double " + name + "__numbody(Interpreter __interpreter" + typedParams + ") {" +
                            numBody + "}" + nl;
            var numDirect = "    public static double " + name + "__numdirect(Interpreter __interpreter" + typedParams + ") {" + nl +
                            "        CscsDirect.Enter(__interpreter);" + nl +
                            "        try { return " + name + "__numbody(__interpreter" + names + "); }" + nl +
                            "        finally { CscsDirect.Leave(); }" + nl +
                            "    }" + nl;
            var typedDirect = "    public static Variable " + name + "__direct(Interpreter __interpreter" + typedParams + ") {" + nl +
                              "        CscsDirect.Enter(__interpreter);" + nl +
                              "        try { return Variable.ConvertToVariable(" + name + "__numbody(__interpreter" + names + ")); }" + nl +
                              "        finally { CscsDirect.Leave(); }" + nl +
                              "    }" + nl;
            var typed = code.Substring(0, open) + typedEntry + numMethod + numDirect + typedDirect + code.Substring(close + 1);
            // A double where a Variable was is the same number, but C# writes it into text its
            // own way: "r + \"x\"" would no longer be the interpreter's "3x" for every r.
            if (RoslynCompiler.JoinsNumberToText(typed, name + "__numbody"))
            {
                return untyped;
            }
            m_untypedCSharpCode = untyped;
            return typed;
        }

        /// <summary>
        /// The body of a function that calls itself, rewritten to return a double: a call to itself
        /// held in a temporary -- "__varTempVar = CscsDirect.Result(NAME__direct(..)); Variable
        /// __varTempVarK = __varTempVar;" -- becomes "double __varTempVarK = NAME__numdirect(..);",
        /// one returned at once becomes "return NAME__numdirect(..);", and every
        /// "return Variable.ConvertToVariable(E);" becomes "return CscsDirect.Number(E);". Number
        /// takes a double or an int only, so if any value returned is text, a truth value or a
        /// Variable, the result does not compile, and the untyped code is used instead
        /// (Precompiler.Compile): the C# compiler is the proof that every return is a number. Null
        /// when the body has no such call to itself, where typed returns would gain nothing.
        /// </summary>
        string TypedReturnBody(string body)
        {
            var name = m_functionName;
            var callHead = "__varTempVar = CscsDirect.Result(" + name + "__direct(";
            if (body.IndexOf(callHead, StringComparison.Ordinal) < 0)
            {
                return null;
            }
            var sb = new StringBuilder(body.Length);
            int at = 0;
            bool rewrote = false;
            while (true)
            {
                int found = body.IndexOf(callHead, at, StringComparison.Ordinal);
                if (found < 0)
                {
                    sb.Append(body, at, body.Length - at);
                    break;
                }
                int argsOpen = found + callHead.Length - 1;
                int argsClose = MatchingParenInCSharp(body, argsOpen);
                int resultClose = argsClose < 0 ? -1 : MatchingParenInCSharp(body, body.IndexOf('(', found));
                if (resultClose < 0 || resultClose + 1 >= body.Length || body[resultClose + 1] != ';')
                {
                    sb.Append(body, at, found + callHead.Length - at);
                    at = found + callHead.Length;
                    continue;
                }
                var args = body.Substring(argsOpen + 1, argsClose - argsOpen - 1);
                var after = System.Text.RegularExpressions.Regex.Match(body.Substring(resultClose + 2),
                    @"^\s*(?:Variable ([A-Za-z_]\w*) = __varTempVar;|return __varTempVar;)");
                if (!after.Success)
                {
                    sb.Append(body, at, found + callHead.Length - at);
                    at = found + callHead.Length;
                    continue;
                }
                sb.Append(body, at, found - at);
                sb.Append(after.Groups[1].Success ?
                    "double " + after.Groups[1].Value + " = " + name + "__numdirect(" + args + ");" :
                    "return " + name + "__numdirect(" + args + ");");
                at = resultClose + 2 + after.Length;
                rewrote = true;
            }
            if (!rewrote)
            {
                return null;
            }
            var text = sb.ToString();
            const string returnHead = "return Variable.ConvertToVariable(";
            sb.Clear();
            at = 0;
            while (true)
            {
                int found = text.IndexOf(returnHead, at, StringComparison.Ordinal);
                if (found < 0)
                {
                    sb.Append(text, at, text.Length - at);
                    break;
                }
                int open = found + returnHead.Length - 1;
                int close = MatchingParenInCSharp(text, open);
                if (close < 0)
                {
                    return null;
                }
                sb.Append(text, at, found - at).Append("return CscsDirect.Number(")
                  .Append(text, open + 1, close - open - 1).Append(")");
                at = close + 1;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Every "CscsCalls.Call(__interpreter, "NAME", ..)" in the text as a call to NAME__direct,
        /// the arguments converted to the declared types; null when a call has another number of
        /// arguments than declared (a default or a named one -- the interpreter binds those).
        /// </summary>
        string DirectSelfCalls(string text, System.Text.RegularExpressions.Regex selfCall, List<string> types)
        {
            var sb = new StringBuilder(text.Length);
            int at = 0;
            while (true)
            {
                var m = selfCall.Match(text, at);
                if (!m.Success)
                {
                    sb.Append(text, at, text.Length - at);
                    return sb.ToString();
                }
                int open = text.IndexOf('(', m.Index);
                int close = MatchingParenInCSharp(text, open);
                if (close < 0)
                {
                    return null;
                }
                var args = SplitCSharpArguments(text.Substring(open + 1, close - open - 1));
                if (args.Count != types.Count + 2)
                {
                    return null;
                }
                var call = new StringBuilder("CscsDirect.Result(" + m_functionName + "__direct(__interpreter");
                for (int i = 0; i < types.Count; i++)
                {
                    var value = DirectSelfCalls(args[i + 2], selfCall, types);
                    if (value == null)
                    {
                        return null;
                    }
                    var convert = types[i] == "int" ? "Int" : types[i] == "double" ? "Num" :
                                  types[i] == "string" ? "Text" : "Var";
                    call.Append(", CscsDirect.").Append(convert).Append("(").Append(value.Trim()).Append(")");
                }
                call.Append("))");
                sb.Append(text, at, m.Index - at).Append(call);
                at = close + 1;
            }
        }

        /// <summary>The '}' closing the '{' at <paramref name="open"/> in generated C#, skipping string
        /// and character literals; -1 if there is none.</summary>
        static int MatchingBraceInCSharp(string code, int open)
        {
            return MatchingInCSharp(code, open, '{', '}');
        }

        static int MatchingParenInCSharp(string code, int open)
        {
            return MatchingInCSharp(code, open, '(', ')');
        }

        static int MatchingInCSharp(string code, int open, char opener, char closer)
        {
            int depth = 0;
            for (int i = open; i < code.Length; i++)
            {
                char c = code[i];
                if (c == '"' || c == '\'')
                {
                    i = SkipCSharpLiteral(code, i);
                    continue;
                }
                if (c == opener) { depth++; }
                else if (c == closer && --depth == 0) { return i; }
            }
            return -1;
        }

        static int SkipCSharpLiteral(string code, int start)
        {
            char quote = code[start];
            for (int i = start + 1; i < code.Length; i++)
            {
                if (code[i] == '\\') { i++; continue; }
                if (code[i] == quote) { return i; }
            }
            return code.Length;
        }

        /// <summary>The top-level, comma-separated arguments of a C# argument list.</summary>
        static List<string> SplitCSharpArguments(string text)
        {
            var result = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"' || c == '\'') { i = SkipCSharpLiteral(text, i); continue; }
                if ("([{".IndexOf(c) >= 0) { depth++; }
                else if (")]}".IndexOf(c) >= 0) { depth--; }
                else if (c == ',' && depth == 0)
                {
                    result.Add(text.Substring(start, i - start));
                    start = i + 1;
                }
            }
            result.Add(text.Substring(start));
            return result;
        }

        /// <summary>
        /// In every generated "if (...)" and "while (...)" condition, a side of "&&" or "||" that
        /// is a script call's result -- "CscsCalls.Call(...)" made in place, or a hoisted one read
        /// as "__varTempVarN.AsDouble()" -- is tested as CSCS tests a value: CscsConvert.IsTrue
        /// (IsFalse under "!"). C# has no truth value for a Variable or a double (CS0019), and
        /// AsDouble would parse the text "5" to 5 and call it true where the interpreter says
        /// false. Each builder that emits a condition would otherwise need this separately; done
        /// on the finished C#, it cannot touch code that compiled, since C# rejects both shapes.
        /// </summary>
        static string TruthTestLogicalOperands(string csharp)
        {
            var sb = new StringBuilder(csharp.Length);
            int i = 0;
            while (i < csharp.Length)
            {
                int at = IndexOfConditionKeyword(csharp, i, out int open);
                if (at < 0)
                {
                    sb.Append(csharp, i, csharp.Length - i);
                    break;
                }
                int close = MatchingParen(csharp, open);
                if (close < 0)
                {
                    sb.Append(csharp, i, csharp.Length - i);
                    break;
                }
                sb.Append(csharp, i, open + 1 - i);
                var inner = csharp.Substring(open + 1, close - open - 1);
                // A returned or stored value is only a condition when it joins terms with
                // "&&" or "||": "return f(n) && n > 1" -- anything else is left as it is.
                bool isValue = string.CompareOrdinal(csharp, at, "Variable.ConvertToVariable", 0, 26) == 0;
                bool logical = SplitTopLevelOn(inner, "&&").Count > 1 || SplitTopLevelOn(inner, "||").Count > 1;
                sb.Append(isValue && !logical ? TruthTestLogicalOperands(inner) : TruthTestTerms(inner));
                sb.Append(')');
                i = close + 1;
            }
            return sb.ToString();
        }

        /// <summary>The next "if(" or "while(" (spaces allowed before the parenthesis) outside
        /// string literals, and the position of its parenthesis.</summary>
        static int IndexOfConditionKeyword(string text, int from, out int open)
        {
            open = -1;
            bool inString = false;
            for (int i = from; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (c == '\\') { i++; }
                    else if (c == '"') { inString = false; }
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                foreach (var keyword in new[] { "if", "while", "Variable.ConvertToVariable" })
                {
                    if (string.CompareOrdinal(text, i, keyword, 0, keyword.Length) != 0 ||
                        (i > 0 && (IsNameChar(text[i - 1]) || text[i - 1] == '.')))
                    {
                        continue;
                    }
                    int j = i + keyword.Length;
                    while (j < text.Length && text[j] == ' ') { j++; }
                    if (j < text.Length && text[j] == '(')
                    {
                        open = j;
                        return i;
                    }
                }
            }
            return -1;
        }

        static string TruthTestTerms(string condition)
        {
            foreach (var connective in new[] { "||", "&&" })
            {
                var parts = SplitTopLevelOn(condition, connective);
                if (parts.Count > 1)
                {
                    return string.Join(connective, parts.Select(TruthTestTerms));
                }
            }
            var term = condition.Trim();
            var lead = condition.Substring(0, condition.Length - condition.TrimStart().Length);
            var trail = condition.Substring(condition.TrimEnd().Length);
            bool negated = term.StartsWith("!") && !term.StartsWith("!=");
            var core = negated ? term.Substring(1).Trim() : term;
            if (core.Length > 1 && core[0] == '(' && FindMatchingParen(core, 0) == core.Length - 1)
            {
                var innerTerm = core.Substring(1, core.Length - 2);
                var rewritten = TruthTestTerms(innerTerm);
                return rewritten == innerTerm ? condition :
                    lead + (negated ? "!" : "") + "(" + rewritten + ")" + trail;
            }
            string value = null;
            if (core.StartsWith("CscsCalls.Call(", StringComparison.Ordinal) &&
                FindMatchingParen(core, core.IndexOf('(')) == core.Length - 1)
            {
                value = core;
            }
            else
            {
                var m = System.Text.RegularExpressions.Regex.Match(core, @"^(__varTempVar\d*)\.AsDouble\(\)$");
                if (m.Success)
                {
                    value = m.Groups[1].Value;
                }
            }
            if (value == null)
            {
                return condition;
            }
            return lead + "CscsConvert." + (negated ? "IsFalse(" : "IsTrue(") + value + ")" + trail;
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
                // Never the variant with typed returns: it is proved only by compiling it, which
                // does not happen here.
                var collected = GetCSharpCode(scriptInCSharp, false, false);
                AotGenerator.Collect(m_functionName, m_untypedCSharpCode ?? collected);
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
            Assembly compiledAssembly;
            try
            {
                compiledAssembly = CompileRepairing(outDll);
            }
            catch (ArgumentException) when (m_untypedCSharpCode != null)
            {
                // The variant with typed returns did not compile -- something returned is not a
                // number -- so the one without them is used.
                CSharpCode = m_untypedCSharpCode;
                m_untypedCSharpCode = null;
                compiledAssembly = CompileRepairing(outDll);
            }

            try
            {
                if (AsyncMode)
                {
                    m_compiledFuncAsync = CompileAndCacheAsync(compiledAssembly, m_functionName);
                }
                else
                {
                    m_compiledFunc = CompileAndCache(compiledAssembly, m_functionName);
                    Direct = DirectDelegate(compiledAssembly);
                }
            }
            catch (Exception exc)
            {
                throw new ArgumentException("Compile error: " + exc.Message, exc);
            }
        }

        /// <summary>
        /// Compiles CSharpCode; on an error the interpreter can stand in for -- a comparison or an
        /// operator C# has none of between these operands -- repairs it (RoslynCompiler.RepairOperators)
        /// and compiles again.
        /// </summary>
        Assembly CompileRepairing(string outDll)
        {
            try
            {
                return RoslynCompiler.Compile(CSharpCode, "CscsPrecompiled_" + m_functionName, outDll);
            }
            catch (ArgumentException exc) when (RoslynCompiler.IsRepairable(exc.Message))
            {
                var interpreter = m_parentScript?.InterpreterInstance;
                var repaired = RoslynCompiler.RepairOperators(CSharpCode,
                    name => interpreter != null && interpreter.GetFunction(name) != null);
                if (repaired == CSharpCode)
                {
                    throw;
                }
                CSharpCode = repaired;
                return RoslynCompiler.Compile(CSharpCode, "CscsPrecompiled_" + m_functionName, outDll);
            }
        }

        /// <summary>NAME__direct as a Func of its typed parameters, or null when there is none.</summary>
        Delegate DirectDelegate(Assembly compiledAssembly)
        {
            if (DirectTypes == null || DirectTypes.Count > CscsDirect.MaxArguments)
            {
                return null;
            }
            var types = new List<Type> { typeof(Interpreter) };
            foreach (var name in DirectTypes)
            {
                types.Add(name == "int" ? typeof(int) : name == "double" ? typeof(double) :
                          name == "string" ? typeof(string) : typeof(Variable));
            }
            var method = compiledAssembly.GetModules()[0].GetType("SplitAndMerge." + ClassName)?
                .GetMethod(m_functionName + "__direct", types.ToArray());
            if (method == null)
            {
                return null;
            }
            types.Add(typeof(Variable));
            return method.CreateDelegate(Expression.GetFuncType(types.ToArray()));
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

        /// <summary>
        /// C# keywords a CSCS script may perfectly well use as a variable name -- "out" and
        /// "base" among them.
        ///
        /// Deliberately *not* every C# keyword: the type names and modifiers are left out
        /// because the generated code writes them itself. Escaping by name across the finished
        /// text turned the "int" of a parameter declaration into "@int" and nothing compiled,
        /// so this holds only words that never appear in generated code for any other reason.
        /// </summary>
        // Every reserved word of C#, for text that would be written into C# as an identifier.
        static readonly HashSet<string> s_allCsKeywords = new HashSet<string>
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
            "void", "volatile", "while"
        };

        static readonly HashSet<string> s_csKeywords = new HashSet<string>
        {
            "abstract", "as", "base", "checked", "const", "delegate", "enum", "event",
            "explicit", "extern", "fixed", "goto", "implicit", "interface", "internal", "is",
            "lock", "operator", "out", "override", "params", "protected", "readonly", "ref",
            "sealed", "sizeof", "stackalloc", "struct", "unchecked", "unsafe", "virtual",
            "volatile"
        };


        /// <summary>
        /// Emits the write-back that lets the interpreter see a local of this function, which
        /// its callbacks resolve by name. AddCompiledLocalVariable rather than
        /// AddGlobalOrLocalVariable: called with no script, as generated code calls it, the
        /// latter wrote every one of them as a global. A compiled function's locals were
        /// published for good -- one sharing a name with a global overwrote it, and a later
        /// interpreted function assigning to that name then wrote the global too, which broke
        /// its own recursion. An assignment to a global that already exists still reaches it.
        /// </summary>
        public string RegisterVariableString(string paramName, string paramValue = "",
                                            bool mayBeGlobal = true)
        {
            if (!m_emitInterpreterSync)
            {
                return "";
            }
            if (string.IsNullOrWhiteSpace(paramValue))
            {
                paramValue = paramName;
            }
            // Only a plain name can be registered. An indexed target such as m["a"] reached
            // here as the "name" and produced AddGlobalOrLocalVariable("m["a"]", ...), whose
            // quotes closed the C# string literal. The collection it belongs to is registered
            // under its own name and the assignment mutates it in place, so there is nothing
            // to register here.
            if (!IsPlainName(paramName))
            {
                return "";
            }
            // A "var" local hides a global of its name, so it is published at the function's
            // own level: through AddCompiledLocalVariable it overwrote the global.
            if (m_varLocals.Contains(paramName))
            {
                mayBeGlobal = false;
            }
            // The name stays as the script spelt it; only the value, which is C#, is mapped.
            var setter = mayBeGlobal ? "AddCompiledLocalVariable" : "AddCompiledLocalOnlyVariable";
            return m_depth + "__interpreter." + setter + "(\"" + paramName +
                     "\", new GetVarFunction(Variable.ConvertToVariable(" + paramValue +
                     ")));\n";
        }

        string ConvertScript(bool startClass = true, bool finish = true)
        {
            m_converted.Clear();
            // ConvertScript may run twice (see GetCSharpCode), so every piece of state it
            // accumulates has to start empty.
            m_newVariables.Clear();
            m_openCatches.Clear();
            m_collectionLocals.Clear();
            m_definitionsMap.Clear();
            m_paramMap.Clear();
            m_collectionArgs.Clear();
            m_widenedIntArgs.Clear();
            m_localTypes.Clear();
            m_argsMap = new Dictionary<string, Variable>(m_declaredArgsMap, StringComparer.OrdinalIgnoreCase);
            // An int argument the body assigns to holds whatever the interpreter would put
            // there, which need not be an int: "n = n / 2" is 13.5 for 27, and "n = 3 * n + 1"
            // outgrows an int long before a double. Such an argument becomes a double local
            // started from its slot; an argument that is only read keeps the slot.
            // Case-blind: the signature arrives lowercased ('initvalue') while the body keeps
            // the script's spelling ('initValue'), and the interpreter treats them as one name.
            var assignedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var statement in TokenizeScript(m_cscsCode))
            {
                var assigned = AssignedName(statement);
                var stepped = (statement ?? "").Trim().TrimEnd(';').Trim();
                if (assigned == null && (stepped.EndsWith("++") || stepped.EndsWith("--") ||
                                         stepped.StartsWith("++") || stepped.StartsWith("--")))
                {
                    assigned = stepped.Trim('+', '-').Trim();
                }
                if (assigned != null)
                {
                    assignedNames.Add(assigned);
                }
            }
            // An int argument keeps its int slot only while nothing assigns to it (above), so
            // only those can seed or bound an int counter.
            var readOnlyIntArgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            readOnlyIntArgs.UnionWith(m_declaredArgsMap
                .Where(arg => arg.Value.Type == Variable.VarType.INT && !assignedNames.Contains(arg.Key))
                .Select(arg => arg.Key));
            m_intCounters = FindIntCounters(m_cscsCode, readOnlyIntArgs,
                new HashSet<string>(m_declaredArgsMap.Keys, StringComparer.OrdinalIgnoreCase));
            m_lastStatementReturn = false;
            m_knownExpression = false;
            m_statementPrelude = "";
            m_enumLocals.Clear();
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
                // A list or map argument is the caller's own Variable, fetched at the start
                // and used as any collection local is. The typed copy the runtime also hands
                // over was converted to an opaque object whenever it met the interpreter --
                // "return a" gave "System.Collections.Generic.List`1[...]" and "a[1].Upper" the
                // whole list -- and most of what a script does with one did not compile. The
                // Variable is the one the interpreter passes, so changes reach the caller as
                // they do there. The slot is still counted, keeping the others' positions.
                if (typeVar.Type == Variable.VarType.ARRAY_INT || typeVar.Type == Variable.VarType.ARRAY_NUM ||
                    typeVar.Type == Variable.VarType.ARRAY_STR || typeVar.Type == Variable.VarType.MAP_NUM ||
                    typeVar.Type == Variable.VarType.MAP_STR)
                {
                    m_collectionArgs.Add(argName);
                    m_argsMap.Remove(argName);
                    if (typeVar.Type == Variable.VarType.ARRAY_INT) arrIntIndex++;
                    else if (typeVar.Type == Variable.VarType.ARRAY_NUM) arrNumIndex++;
                    else if (typeVar.Type == Variable.VarType.ARRAY_STR) arrStrIndex++;
                    else if (typeVar.Type == Variable.VarType.MAP_NUM) mapNumIndex++;
                    else mapStrIndex++;
                    continue;
                }
                if (typeVar.Type == Variable.VarType.INT && assignedNames.Contains(argName))
                {
                    m_widenedIntArgs.Add(argName);
                    m_argsMap.Remove(argName);
                    m_widenedIntSlots[argName] = INT_VAR_ARG + "[" + (intIndex++) + "]";
                    continue;
                }
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

            foreach (var intArg in m_widenedIntArgs)
            {
                m_converted.AppendLine("     double " + intArg + " = " + m_widenedIntSlots[intArg] + ";");
                m_newVariables.Add(intArg);
            }
            foreach (var collectionArg in m_collectionArgs)
            {
                m_converted.AppendLine("     Variable " + collectionArg +
                                       " = __interpreter.GetVariableValue(\"" + collectionArg + "\");");
                m_newVariables.Add(collectionArg);
                m_collectionLocals.Add(collectionArg);
                m_usesInterpreter = true;
            }

            m_statements = TokenizeScript(m_cscsCode);
            CollectVariableLocals(m_statements);
            DeclareBlockCrossingLocals(m_statements);
            CollectLocalTypes(m_statements);
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
            return KeepInterpreterEscapes(CastRoundDigits(NumberMathArgs(MapChainedStringMembers(
                EscapeKeywordNames(m_converted.ToString())))));
        }

        /// <summary>
        /// Wraps a Math argument that is not a C# number in CscsConvert.ToNumber.
        ///
        /// A script call resolves to CscsCalls.Call and a global read to GetVariableValue, both
        /// of which yield a Variable; a comparison yields a C# bool. C# then finds no Math
        /// overload at all and reports the first one it has, which is why the diagnostics named
        /// "byte", "short" and "decimal" for Max, Abs and Round. ToNumber unwraps a Variable
        /// through AsDouble and converts anything else.
        ///
        /// Done over the finished code, like CastRoundDigits and MapChainedStringMembers: the
        /// argument is emitted by the general operand path, which has no idea it is inside a
        /// Math call -- and ReplaceMathArgs, the one place that does know, is never reached for
        /// a "return Math.Max(..)" statement (ProcessReturnStatement returns first; traced).
        ///
        /// Only arguments carrying one of those markers are touched, so the shapes that already
        /// compile -- an element, which is read as a number, a scalar argument, a literal, an
        /// arithmetic expression, a mapped string member and a nested Math call -- are left
        /// exactly as they were. Runs before CastRoundDigits and skips an argument already cast
        /// or converted, so the two passes cannot fight; and for a two-argument Math.Round only
        /// the value is wrapped, never the digit count, which has to stay an int.
        /// </summary>
        string NumberMathArgs(string code)
        {
            const string prefix = "Math.";
            if (code == null || code.IndexOf(prefix, StringComparison.Ordinal) < 0)
            {
                return code;
            }
            var sb = new StringBuilder(code.Length + 32);
            bool inQuotes = false;
            int i = 0;
            while (i < code.Length)
            {
                char ch = code[i];
                if (ch == '"' && !IsEscapedQuote(code, i))
                {
                    inQuotes = !inQuotes;
                }
                if (inQuotes || string.CompareOrdinal(code, i, prefix, 0, prefix.Length) != 0)
                {
                    sb.Append(ch);
                    i++;
                    continue;
                }
                // The call's name, then its opening parenthesis. "Math.PI" has none.
                int nameEnd = i + prefix.Length;
                while (nameEnd < code.Length &&
                       (char.IsLetterOrDigit(code[nameEnd]) || code[nameEnd] == '_'))
                {
                    nameEnd++;
                }
                if (nameEnd >= code.Length || code[nameEnd] != '(')
                {
                    sb.Append(code, i, nameEnd - i);
                    i = nameEnd;
                    continue;
                }
                int close = FindCallEnd(code, nameEnd);
                if (close < 0)
                {
                    sb.Append(ch);
                    i++;
                    continue;
                }
                var name = code.Substring(i, nameEnd - i);
                var args = SplitTopLevel(code.Substring(nameEnd + 1, close - nameEnd - 1), ',');
                bool isRound = name == "Math.Round";
                bool any = false;
                for (int a = 0; a < args.Count; a++)
                {
                    // Round's digit count stays an int: CastRoundDigits casts it, and a double
                    // there is the very overload failure that pass exists to prevent.
                    if (isRound && args.Count == 2 && a == 1)
                    {
                        continue;
                    }
                    if (!NeedsNumericArgument(args[a]))
                    {
                        continue;
                    }
                    args[a] = "CscsConvert.ToNumber(" + args[a].Trim() + ")";
                    any = true;
                }
                if (!any)
                {
                    // Nothing to change: copy the call through untouched, but keep scanning
                    // inside it -- a nested call may still have an argument that needs it.
                    sb.Append(code, i, nameEnd + 1 - i);
                    i = nameEnd + 1;
                    continue;
                }
                sb.Append(name).Append('(').Append(string.Join(",", args)).Append(')');
                i = close + 1;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Whether this already-generated argument yields something a C# Math parameter cannot
        /// take: a Variable, from an interpreter callback or a global read, or a bool, from a
        /// comparison. An argument that is already a number, cast or converted is left alone.
        /// </summary>
        bool NeedsNumericArgument(string argument)
        {
            var text = (argument ?? "").Trim();
            if (text.Length == 0 || text.Contains(".AsDouble()") ||
                text.Contains("CscsConvert.ToNumber(") ||
                text.StartsWith("(int)", StringComparison.Ordinal))
            {
                return false;
            }
            return text.Contains("CscsCalls.Call(") ||
                   text.Contains("__interpreter.GetVariableValue(") ||
                   text.Contains(VARIABLE_TEMP_VAR) ||
                   YieldsBool(text) ||
                   // A truth value reaches the argument as the bare name of a bool local --
                   // "Math.Max(b, 5)" with "var b=__varInt[0]>1;" above it -- so the text alone
                   // cannot tell. m_localTypes recorded "bool" for it, which is what IsBoolLocal
                   // reads; that is why this pass is an instance method rather than static.
                   IsBoolLocal(text);
        }

        /// <summary>
        /// Casts the digit count of a two-argument Math.Round to int.
        ///
        /// CSCS numbers are all doubles, and C# has no Round(double, double): a literal picks
        /// Round(double, int) by itself, but anything computed -- "Math.Round(x, d)" or
        /// "Math.Round(x, k + 1)" -- arrives as a double, and the compiler then reaches for
        /// Round(decimal, int) and refuses both arguments. Done over the finished code, like
        /// MapChainedStringMembers: the argument is emitted by the general operand path, which
        /// has no idea it is inside a Round.
        ///
        /// Round only, and only with two arguments: Pow, Max, Min and Atan2 take two doubles
        /// and casting theirs would break them, and Round(x) has no digit count to cast.
        /// </summary>
        static string CastRoundDigits(string code)
        {
            const string call = "Math.Round(";
            if (code == null || code.IndexOf(call, StringComparison.Ordinal) < 0)
            {
                return code;
            }
            var sb = new StringBuilder(code.Length + 16);
            bool inQuotes = false;
            int i = 0;
            while (i < code.Length)
            {
                char ch = code[i];
                if (ch == '"' && !IsEscapedQuote(code, i))
                {
                    inQuotes = !inQuotes;
                }
                if (inQuotes || string.CompareOrdinal(code, i, call, 0, call.Length) != 0)
                {
                    sb.Append(ch);
                    i++;
                    continue;
                }
                int open = i + call.Length - 1;
                int close = FindCallEnd(code, open);
                if (close < 0)
                {
                    sb.Append(ch);
                    i++;
                    continue;
                }
                var args = SplitTopLevel(code.Substring(open + 1, close - open - 1), ',');
                if (args.Count != 2 || IsIntegerLiteral(args[1].Trim()) ||
                    args[1].Trim().StartsWith("(int)", StringComparison.Ordinal))
                {
                    sb.Append(code, i, close + 1 - i);
                    i = close + 1;
                    continue;
                }
                sb.Append(call).Append(args[0]).Append(",(int)(").Append(args[1]).Append("))");
                i = close + 1;
            }
            return sb.ToString();
        }

        // Generated calls that are certain to return a C# string, so whatever member follows
        // one is a string member.
        static readonly string[] s_stringResults =
            { ".SubstringCscs(", ".AtCscs(", ".ToUpper(", ".ToLower(", ".Replace(", ".Trim(",
              // Variable's own: its Substring returns a string too, and so do these two
              // properties, which have no call to find the end of.
              ".Substring(", ".Upper", ".Lower" };

        /// <summary>
        /// Maps a CSCS member that follows a call returning a string: "s.Substring(0, 2).Upper"
        /// came out as "SubstringCscs(0,2).Upper", since the expression path resolves the call
        /// and its arguments as separate tokens and copies the ".Upper" after them verbatim.
        /// Done over the finished code, where the receiver's type is not in doubt. Members
        /// MapStringMember does not know -- the C# ones among them -- are left as they are.
        /// </summary>
        static string MapChainedStringMembers(string code)
        {
            if (!s_stringResults.Any(code.Contains))
            {
                return code;
            }
            var sb = new StringBuilder(code.Length + 16);
            bool inQuotes = false;
            int i = 0;
            while (i < code.Length)
            {
                char ch = code[i];
                if (ch == '"' && !IsEscapedQuote(code, i))
                {
                    inQuotes = !inQuotes;
                }
                string call = inQuotes ? null :
                    s_stringResults.FirstOrDefault(c => string.CompareOrdinal(code, i, c, 0, c.Length) == 0);
                int end = call == null ? -1 :
                    call.EndsWith("(") ? FindCallEnd(code, i + call.Length - 1) :
                    // A property: only where the name really ends there.
                    i + call.Length < code.Length && (char.IsLetterOrDigit(code[i + call.Length]) ||
                        code[i + call.Length] == '_') ? -1 : i + call.Length - 1;
                if (end < 0)
                {
                    sb.Append(ch);
                    i++;
                    continue;
                }
                sb.Append(code, i, end + 1 - i);
                i = end + 1;
                // Each member mapped onto one that returns a string can be followed by another.
                while (i < code.Length && code[i] == '.')
                {
                    int nameEnd = i + 1;
                    while (nameEnd < code.Length && (char.IsLetterOrDigit(code[nameEnd]) || code[nameEnd] == '_'))
                    {
                        nameEnd++;
                    }
                    int memberEnd = nameEnd < code.Length && code[nameEnd] == '(' ?
                        FindCallEnd(code, nameEnd) : nameEnd - 1;
                    if (memberEnd < 0 || nameEnd == i + 1)
                    {
                        break;
                    }
                    string member = code.Substring(i + 1, memberEnd - i);
                    string mapped = null;
                    if (!MapStringMember("", member, ref mapped) || mapped == "." + member)
                    {
                        break;
                    }
                    sb.Append(mapped);
                    i = memberEnd + 1;
                    if (!s_stringResults.Any(c => mapped.StartsWith(c, StringComparison.Ordinal)))
                    {
                        break;
                    }
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Keeps string literals meaning what they mean to the interpreter. Its literal parser
        /// turns only \\, \", \' and \n into characters; anything else -- "\t" above all --
        /// stays a backslash and a letter (Print is what shows "\t" as a tab). Copied into C#
        /// unchanged, "a\tb" became three characters where the interpreter has four, so
        /// Length, comparisons and Substring all answered differently. Every other escape gets
        /// its backslash doubled, which keeps it two characters in C# as well.
        /// </summary>
        static string KeepInterpreterEscapes(string code)
        {
            if (code.IndexOf('\\') < 0)
            {
                return code;
            }
            var sb = new StringBuilder(code.Length + 16);
            bool inQuotes = false;
            for (int i = 0; i < code.Length; i++)
            {
                var ch = code[i];
                if (!inQuotes)
                {
                    inQuotes = ch == '"';
                    sb.Append(ch);
                    continue;
                }
                if (ch == '"')
                {
                    inQuotes = false;
                    sb.Append(ch);
                    continue;
                }
                if (ch != '\\' || i + 1 >= code.Length)
                {
                    sb.Append(ch);
                    continue;
                }
                var next = code[i + 1];
                if (next != '\\' && next != '"' && next != '\'' && next != 'n')
                {
                    sb.Append('\\');
                }
                sb.Append(ch).Append(next);
                i++;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Writes an "@" in front of every local whose name is a C# keyword -- a script may
        /// perfectly well call a variable "out". Done once over the finished code rather than
        /// at each of the dozen places a name is emitted, and skipping string literals, which
        /// is where the *script's* names live: the interpreter is told about "out", and only
        /// the C# identifier becomes "@out".
        /// </summary>
        string EscapeKeywordNames(string code)
        {
            // "object" too -- a script's "object = GetObject(...)" -- though generated code writes it
            // itself, always as the cast "(object)(": that shape is left alone below.
            bool Escapable(string name) => s_csKeywords.Contains(name) || name == "object";
            var names = new HashSet<string>();
            foreach (var name in m_newVariables)
            {
                if (Escapable(name))
                {
                    names.Add(name);
                }
            }
            foreach (var name in m_paramMap.Keys)
            {
                if (Escapable(name))
                {
                    names.Add(name);
                }
            }
            if (names.Count == 0)
            {
                return code;
            }

            var sb = new StringBuilder(code.Length + 16);
            bool inQuotes = false;
            for (int i = 0; i < code.Length; i++)
            {
                var ch = code[i];
                if (ch == '"' && !IsEscapedQuote(code, i))
                {
                    inQuotes = !inQuotes;
                    sb.Append(ch);
                    continue;
                }
                if (inQuotes || !(char.IsLetter(ch) || ch == '_'))
                {
                    sb.Append(ch);
                    continue;
                }
                int end = i;
                while (end < code.Length && (char.IsLetterOrDigit(code[end]) || code[end] == '_'))
                {
                    end++;
                }
                var word = code.Substring(i, end - i);
                // Not one already written with an "@", and not a member: ".out" would be a
                // field of something else, not the local.
                bool escaped = i > 0 && (code[i - 1] == '@' || code[i - 1] == '.');
                if (word == "object" && i > 0 && code[i - 1] == '(' &&
                    string.CompareOrdinal(code, end, ")(", 0, 2) == 0)
                {
                    escaped = true;
                }
                sb.Append(!escaped && names.Contains(word) ? "@" + word : word);
                i = end - 1;
            }
            return sb.ToString();
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
                if (!IsPlainName(varName))
                {
                    // An indexed target -- "m[\"a\"] = {}" -- is not a variable to register:
                    // the name went into the C# string literal with its quotes and closed it
                    // early. The collection it belongs to is already registered, and the
                    // assignment below mutates it in place.
                    return false;
                }
                m_definitionsMap[varName] = m_converted.Length;
                m_converted.Append(m_depth + "__interpreter.AddCompiledLocalVariable(\"" + varName +
                    "\", new GetVarFunction(new Variable(Variable.VarType.ARRAY)));\n");
                m_usesInterpreter = true;
                return true;
            }
            return false;
        }

        string ConvertTokenIfNeeded(string token, string first = "")
        {
            // A name that is a C# keyword takes an "@": a script may call a variable "out",
            // and the generated code has to spell it in a way C# accepts.
            string result = token;
            // ".Type" on a local reaches C# through here on the return path -- "return
            // x.Type;" -- where nothing else resolves a member, so it used to be emitted
            // verbatim and failed to compile on a double, a string or a bool. It only looked
            // as though members worked here: "m.Type" on a collection compiled because that
            // spelling is already valid C# on a Variable.
            int typeDot = token.IndexOf('.');
            if (typeDot > 0 && !token.Contains("(") && !token.Contains("[") &&
                TryMapTypeMember(token.Substring(0, typeDot), token.Substring(typeDot + 1), out var typeText))
            {
                return typeText;
            }
            string functionName = GetFunctionName(token, out string suffix, out bool isArray).ToLower();
            if (!suffix.Contains('.') && !suffix.TrimStart().StartsWith("(") && m_argsMap.TryGetValue(functionName, out _))
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

        /// <summary>
        /// Moves a "new" that sits inside a larger statement into a statement of its own, so
        /// that the one form the translator does handle -- "&lt;name&gt; = new X(...)" -- is the
        /// one it sees. The temporary is a real local and is registered with the interpreter
        /// like any other, which is what lets a method called on it resolve its own fields;
        /// building the instance without a name was tried instead and every method call on it
        /// threw. Returns null when there is nothing to move.
        /// </summary>
        string TryHoistNewInstance(string statement, string nextStatement, bool addNewVars)
        {
            var trimmed = statement.Trim();
            int at = IndexOfTopLevelNew(trimmed, 0);
            if (at < 0)
            {
                return null;
            }

            // Already the whole right-hand side of an assignment to a plain name: that is the
            // form the rest of the pipeline builds, so leave it be.
            var halves = SplitTopLevelOn(trimmed, "=");
            if (halves.Count == 2 && IsPlainName(halves[0].Trim()) &&
                halves[1].Trim().TrimEnd(';').Trim() == trimmed.Substring(at).TrimEnd(';').Trim())
            {
                // Unless another "new" sits in its arguments. Left alone that reaches the
                // interpreter with its whitespace stripped and looks for a function called
                // "newPoint", which throws at run time instead of falling back. Moving the
                // inner one out does not help either: the temporary is registered with the
                // interpreter, but the sub-script that builds the outer instance does not see
                // it, so the method call on it resolves a field of the wrong class. The whole
                // function goes to the interpreter, which is right every time.
                if (IndexOfTopLevelNew(trimmed, at + 3) >= 0)
                {
                    throw new ArgumentException(
                        "A class instance built inside another one's arguments is interpreted.");
                }
                return null;
            }

            // Not out of a ternary branch: only one of the two runs, and building both ahead
            // of the test would run a constructor the script never asked for.
            int question = IndexOfTopLevelChar(trimmed, '?');
            if (question >= 0 && at > question)
            {
                return null;
            }

            int close = FindNewInstanceEnd(trimmed, at);
            if (close < 0)
            {
                return null;
            }

            var instance = trimmed.Substring(at, close - at);
            // Numbered across the whole run, not within this function: the temporary is
            // registered with the interpreter, and two functions that both started at 1 left
            // one holding the other's instance -- a method call on it then looked for a field
            // of the wrong class.
            var tempName = "__newInst" +
                System.Threading.Interlocked.Increment(ref s_newInstanceId);
            // The pre-pass that decides which locals hold a Variable ran over the statements
            // as written, so it has never seen this one. An instance is a Variable, and
            // without saying so the name resolved as an interpreter variable instead of a
            // local and the assignment came out as a call to it.
            m_variableLocals.Add(tempName);
            var rest = trimmed.Substring(0, at) + tempName + trimmed.Substring(close);
            // The temporary is built first, then the statement that uses it. Both go back
            // through the pipeline, so a statement holding two of them resolves one per pass.
            // No spaces around the "=" and no trailing ";": statements reach the translator
            // with their whitespace stripped and their separator as a statement of its own,
            // and either one made the tokenizer read the name as a call instead of a target.
            return ProcessStatement(tempName + "=" + instance, ";", addNewVars) +
                   ProcessStatement(rest, nextStatement, addNewVars);
        }

        /// <summary>
        /// Position of a "new" used as a whole word outside any string literal, at or after
        /// <paramref name="from"/>.
        /// </summary>
        static int s_newInstanceId;

        static int IndexOfTopLevelNew(string text, int from)
        {
            bool inQuotes = false;
            for (int i = 0; i < from && i < text.Length; i++)
            {
                if (text[i] == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                }
            }
            for (int i = from; i < text.Length; i++)
            {
                if (text[i] == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (inQuotes || text[i] != 'n' ||
                    string.CompareOrdinal(text, i, "new", 0, 3) != 0)
                {
                    continue;
                }
                if ((i > 0 && (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] == '_' ||
                               text[i - 1] == '.')) ||
                    i + 3 >= text.Length || char.IsLetterOrDigit(text[i + 3]) ||
                    text[i + 3] == '_')
                {
                    continue;
                }
                return i;
            }
            return -1;
        }

        /// <summary>
        /// One past the end of the "new X(...)" beginning at <paramref name="at"/>, or -1 when
        /// it is not one -- "new" without a call after it is not an instance.
        /// </summary>
        static int FindNewInstanceEnd(string text, int at)
        {
            int i = at + 3;
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }
            while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
            {
                i++;
            }
            if (i >= text.Length || text[i] != '(')
            {
                return -1;
            }
            int close = FindMatchingParen(text, i);
            return close < 0 ? -1 : close + 1;
        }

        string ProcessStatement(string statement, string nextStatement, bool addNewVars = true)
        {
            if (string.IsNullOrWhiteSpace(statement))
            {
                return "";
            }

            RefuseUntranslatable(statement);

            // "finally" is not one of Constants.RESERVED, so it never reached the clause
            // handling that "catch" goes through: it was resolved as a name, became a
            // callback to a function called "finally", and threw at run time. C# spells it
            // the same way, and the "{" after it arrives as its own statement, as it does
            // for a catch.
            if (statement.Trim() == Constants.FINALLY)
            {
                m_depth += "  ";
                m_statementId++;
                return Constants.FINALLY + " {\n";
            }

            var localEnum = TryBuildLocalEnum(statement, addNewVars);
            if (localEnum != null)
            {
                return localEnum;
            }

            // Before ProcessSpecialCases, which would swallow "x = {}" and defer the
            // declaration until some later statement reveals a type. Building the collection
            // here instead gives it a real C# local, so .Add and .Size compile against it.
            // Before anything else looks at the statement: "a ** b" is not C#, and the
            // Math.Pow form it becomes is one the rest of the pipeline already compiles.
            // Rewriting later left earlier stages having seen the "**" text and taking a
            // different path through the return handling, which stopped resolving arguments.
            var power = TryRewritePower(statement);
            if (power != null)
            {
                statement = power;
            }

            // "a.Add(new Point(5, 6))" and "p.kid = new Named(6, \"b\")": a class instance
            // built anywhere but as the whole right-hand side of an assignment to a plain
            // name. Only that one form is translated, and it is the interpreter that builds
            // the instance, under the name being assigned -- which is how a method of the
            // class later resolves its own fields. Elsewhere the "new" reached C# verbatim, as
            // "new Named(...)", naming a type the generated code does not have.
            var hoistedNew = TryHoistNewInstance(statement, nextStatement, addNewVars);
            if (hoistedNew != null)
            {
                return hoistedNew;
            }

            // "return (b = n > 2) + b": the group is 1 or 0 in CSCS but a C# bool, and "bool + int"
            // does not compile. Hoisted into a statement of its own, which is the order CSCS
            // evaluates in anyway, leaving a plain name the bool-to-number rule already handles.
            // Ahead of the compound builders: they copy a group through as it stands, and
            // "r += (c = n > 5) * 10" then multiplied a C# bool (CS0019).
            var boolGroup = TryHoistBoolGroupAssignment(statement, nextStatement, addNewVars);
            if (boolGroup != null)
            {
                return boolGroup;
            }

            // "r += x" on a local that holds a Variable goes through the interpreter's own
            // compound operator rather than C#'s "+=", which would use Variable's "+".
            var variableCompound = TryBuildVariableCompound(statement);
            if (variableCompound != null)
            {
                return variableCompound;
            }

            // "p.x += 5" and "p.x++" on a local that holds a Variable: read the field, apply the
            // interpreter's own compound operator, set it back -- C# has neither the "+=" nor the
            // "++" for a member of a Variable (CS1061/CS1059).
            var memberCompound = TryBuildMemberCompound(statement);
            if (memberCompound != null)
            {
                return memberCompound;
            }

            // "a[f(0)]" -- a call inside a subscript. The resolver builds the index itself
            // and cannot produce the statements a call needs, so it is worked out first.
            var hoistedIndex = TryHoistSubscriptCall(statement, nextStatement, addNewVars);
            if (hoistedIndex != null)
            {
                return hoistedIndex;
            }

            // "a = b = 3" assigns right to left, so it stands for two statements.
            var chained = TryBuildChainedAssignment(statement, nextStatement, addNewVars);
            if (chained != null)
            {
                return chained;
            }

            // "garr[0] = 9" on a global. Reading one already works; writing one was read as a
            // declaration of a local called garr and emitted "double garr[0]=9".
            var globalElement = TryBuildGlobalElementAssignment(statement);
            if (globalElement != null)
            {
                return globalElement;
            }

            var emptyLiteral = TryBuildLiteralAssignment(statement, addNewVars);
            if (emptyLiteral != null)
            {
                return emptyLiteral;
            }

            // "p.v = 9" on a local holding a class instance: a field is written through the
            // same property setter the interpreter uses.
            var fieldWrite = TryBuildFieldAssignment(statement);
            if (fieldWrite != null)
            {
                return fieldWrite;
            }

            // A local that holds a collection element somewhere in this function is a
            // Variable throughout, so every assignment to it goes through one builder: the
            // seed ("v = 0") and the element ("v = m[key]") have to fit the same declaration.
            var variableLocal = TryBuildVariableLocalAssignment(statement, addNewVars);
            if (variableLocal != null)
            {
                return variableLocal;
            }

            // "a = cond ? {1,2,3} : {4}". A literal in a branch has to be built; emitting its
            // braces verbatim is not an expression C# can parse.
            var ternaryLiteral = TryBuildTernaryLiteral(statement, addNewVars);
            if (ternaryLiteral != null)
            {
                return ternaryLiteral;
            }

            if (ProcessSpecialCases(statement))
            {
                return "";
            }

            // C# has no "<" or ">" on strings, so a comparison between them becomes the
            // string.Compare form the interpreter uses. It has to happen on the whole
            // statement: the tokenizer splits on the relational operator, so by the time the
            // condition reaches the token loop its two sides are already in different tokens.
            // A no-op unless one side is provably a string, so numeric comparisons are
            // untouched.
            var stringCompare = TryRewriteStatementComparison(statement);
            if (stringCompare != null)
            {
                statement = stringCompare;
            }

            // "do { ... } while (cond);" maps onto the C# loop of the same shape; only the
            // trailing "while" has to be recognised as the loop's tail rather than a new loop.
            var doLoop = TryBuildDoLoop(statement);
            if (doLoop != null)
            {
                return doLoop;
            }

            // A call in a "while" condition has to run on every iteration, so the loop is
            // turned inside out rather than the call being hoisted above it.
            var whileWithCall = TryBuildWhileWithCall(statement);
            if (whileWithCall != null)
            {
                return whileWithCall;
            }

            // "--a[1]" and "g += 5" are rewritten into forms the rest of this already handles:
            // the postfix step, and the read-modify-write spelled out.
            var stepRewrite = TryRewriteStep(statement);
            if (stepRewrite != null)
            {
                statement = stepRewrite;
            }

            var globalCompound = TryBuildGlobalCompound(statement);
            if (globalCompound != null)
            {
                return globalCompound;
            }

            // "y &= 10" on a local: locals are declared double so that "/" is real division,
            // and C# has no bitwise compound assignment on a double. Emitted here as the
            // read-modify-write it stands for, truncating with (int) exactly as
            // Parser.MergeNumbers does for the binary form.
            var bitwiseCompound = TryBuildBitwiseCompound(statement);
            if (bitwiseCompound != null)
            {
                return bitwiseCompound;
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
            m_statementRelational = HasPlainRelational(string.Join("", tokens));
            m_statementVariableAccum = IsVariableAccumulation(string.Join("", tokens));
            m_statementBitwise = HasPlainBitwise(string.Join("", tokens));
            m_statementSameValue = string.Join("", tokens).Contains(SAME_VALUE_CALL);
            m_statementInlineCalls = m_forceInlineCalls || NeedsInlineCalls(string.Join("", tokens));
            m_statementHasString = string.Join("", tokens).Contains("\"");

            if (m_knownExpression && tokens.Count > 1)
            {
                // This branch returns on its own, so anything hoisted while resolving the
                // statement has to be emitted here -- the shared flush further down never
                // runs for it.
                var preludeBefore = m_statementPrelude;
                m_statementPrelude = "";
                bool isAssignment = IsAssignment(tokens[1]);
                // tokens[0] is only a plain declaration target when this is an assignment to
                // something that isn't a function argument. In every other case (an operator
                // such as +, -, <, or an assignment to an argument) it is part of an
                // expression and has to be resolved like any other token.
                string lhsName = GetFunctionName(tokens[0], out _, out _).Trim();
                bool lhsIsArgument = m_paramMap.ContainsKey(lhsName);
                // Assigning to a name the interpreter holds has to reach it, as it does when
                // the function is interpreted. The write-back at the end of this branch is
                // only emitted once something asks for the interpreter, and a statement like
                // "g = n * 3" asks for nothing else -- so the global kept its old value.
                if (isAssignment && !lhsIsArgument && IsPlainName(lhsName) &&
                    IsInterpreterVariable(lhsName))
                {
                    m_usesInterpreter = true;
                }

                // A numeric "if" condition lands here rather than in the token loop, and this
                // path resolves argument names without converting calls -- so
                // "if (helper(n) > 5)" emitted a bare "helper" that does not compile.
                var lhsText = !isAssignment && StartsWithKeyword(statement, Constants.IF) ?
                    HoistConditionCalls(tokens[0], statement) : tokens[0];
                string lhs = isAssignment ? ConvertTokenIfNeeded(tokens[0]) : ReplaceArgsInString(lhsText);
                string rhs = ProcessRHS(tokens);

                result = m_statementPrelude + m_depth;
                m_statementPrelude = preludeBefore;

                // Not a declaration when the statement is a keyword carrying an assignment
                // inside its condition: "while ((x = n - t) > 2)" arrives as one token, so
                // tokens[0] is the whole "while (...)" text and tokens[1] is the "=" of the
                // inner assignment. The prefix was glued to the keyword -- "var while((x=..)>2)"
                // -- which is "CS1002: ; expected" and "The name 'var' does not exist". The
                // condition's own assignment declares nothing here; the variable is registered
                // by the statement that follows.
                bool keywordStatement = StartsWithKeyword(tokens[0], Constants.IF) ||
                                        StartsWithKeyword(tokens[0], Constants.WHILE) ||
                                        StartsWithKeyword(tokens[0], Constants.FOR) ||
                                        StartsWithKeyword(tokens[0], Constants.ELSE_IF) ||
                                        StartsWithKeyword(tokens[0], Constants.SWITCH) ||
                                        StartsWithKeyword(tokens[0], Constants.RETURN);
                if (tokens[1] == "=" && !m_newVariables.Contains(tokens[0]) && !lhsIsArgument &&
                    !keywordStatement)
                {
                    // Declared double rather than var: CSCS has no integer type, so a
                    // variable seeded from a literal like 0 must not turn a later "/" into an
                    // integer division. A comparison or logical expression produces a bool,
                    // though, so those keep var.
                    // "var" too when the value comes from the interpreter: that yields a
                    // Variable, which does not fit a double.
                    // "var" too when the value joins collections: that yields their text --
                    // "[1, 2][3]", whose Type is STRING and Size 0 -- which a double cannot
                    // hold. The recorded type is corrected to match in CollectLocalTypes;
                    // declaring var alone left ".Type" answering NUMBER at translation time.
                    // "var" too when the value is a generated call certain to return a string --
                    // "t = s.Substring(n - 1)", "t = s.At(n - 1)". An operator in the argument makes
                    // the statement a known expression, which declared the local a double, and the
                    // string could not be stored in it (CS0029).
                    result += YieldsBool(rhs) || rhs.Contains("__interpreter.GetVariableValue") ||
                        JoinsCollections(rhs) || s_stringResults.Any(marker => rhs.Contains(marker)) ?
                        "var " : "double ";
                    m_newVariables.Add(tokens[0]);
                }
                if (!WithoutStringContents(rhs).Contains(";"))
                {
                    // CSCS has no boolean type: a truth value is the number 1 or 0. A local C#
                    // declared "bool" therefore cannot take part in "t += b" (double += bool)
                    // or "b == 1" (bool == int), and this is the only place the operator and
                    // both sides are visible together -- the right-hand side reaches the
                    // operand funnel as a bare token with no neighbouring separator, so the
                    // conversion there cannot see the context.
                    result += AsCscsNumberBeside(lhs, tokens[1], rhs, out var convertedRhs) +
                              tokens[1] + convertedRhs;
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
            // "return (n);" arrives as the single token "return(n)", the value glued on through
            // the parenthesis. Counted as a bare "return;" it returned nothing at all -- no
            // compile error, just an empty result -- whenever the group had no operator in it
            // to split it into more tokens: "return (n);", "return (f(x));".
            if (tokens.Count <= 2 && !string.IsNullOrWhiteSpace(suffix))
            {
                var value = suffix + (tokens.Count == 2 && tokens[1].Trim() != ";" ? tokens[1] : "");
                tokens = new List<string> { Constants.RETURN, " ", value };
                suffix = "";
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
            m_statementRelational = HasPlainRelational(string.Join("", tokens));
            m_statementVariableAccum = IsVariableAccumulation(string.Join("", tokens));
            m_statementBitwise = HasPlainBitwise(string.Join("", tokens));
            m_statementSameValue = string.Join("", tokens).Contains(SAME_VALUE_CALL);
            m_statementInlineCalls = m_forceInlineCalls || NeedsInlineCalls(string.Join("", tokens));
            m_statementHasString = string.Join("", tokens).Contains("\"");
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
                    if (!WithoutStringContents(result).Contains(";"))
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
            m_statementRelational = HasPlainRelational(string.Join("", tokens));
            m_statementVariableAccum = IsVariableAccumulation(string.Join("", tokens));
            m_statementBitwise = HasPlainBitwise(string.Join("", tokens));
            m_statementSameValue = string.Join("", tokens).Contains(SAME_VALUE_CALL);
            m_statementInlineCalls = m_forceInlineCalls || NeedsInlineCalls(string.Join("", tokens));
            m_statementHasString = string.Join("", tokens).Contains("\"");
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

            // A ";" in text -- "return t + \";\"" -- is not a statement the expression carries.
            if (!WithoutStringContents(returnToken).Contains(";"))
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
                // Rewriting the right-hand side here was tried, to compile
                // "acc += \"ab\" * 2". It cannot reach that statement at all: the tokenizer
                // hands it over as one token, and IsKnownExpression answers false for any
                // token holding a quote -- so a compound assignment of text never takes this
                // branch. What it did do was rewrite right-hand sides that already compiled,
                // costing four constructs in the coverage fixture. The "*"-on-text form in a
                // compound assignment falls back instead, with the interpreter's value.
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
        /// out of -- the switch itself, never the loop around it -- and default runs unless an
        /// earlier break left the block, which is what CSCS does whether it was reached by
        /// falling through or by matching nothing.
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

            // "continue" would leave the do/while rather than the enclosing loop. It is
            // redirected instead (see RedirectContinue): it sets a flag and breaks out of the
            // do/while, and the flag continues the loop right after it. Matched as a word
            // anywhere in the statement, not as the whole of it: a clause carries whatever
            // follows its label, so "case 1: continue;" is a single statement.
            bool hasContinue = body.Any(line => ContainsWord(line, Constants.CONTINUE));

            // Group the statements into clauses, in order.
            var labels = new List<string>();          // null marks the default clause
            var bodies = new List<List<string>>();
            // Labels count only at the switch's own level: a nested switch's "case" lines
            // belong to the clause that holds it, and TranslateClause translates that switch.
            int nesting = 0;
            foreach (var line in body)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }
                // Kept inside a clause: a "for" header is three statements joined by these,
                // and its builder reads them from the statement list (see TranslateClause).
                if (trimmed == ";")
                {
                    if (bodies.Count > 0)
                    {
                        bodies[bodies.Count - 1].Add(line);
                    }
                    continue;
                }
                string label;
                string rest;
                if (nesting == 0 && TrySplitClause(trimmed, out label, out rest))
                {
                    labels.Add(label);
                    bodies.Add(new List<string>());
                    // "case 1: case 2: return 12;" arrives as one line, so the second label was
                    // left in the first clause's body and went out as a C# "case" in the middle
                    // of an if (CS1003). Each label starts its own clause with an empty body,
                    // which is what fall-through already is here: the first sets the match flag
                    // and the next clause's body runs.
                    while (!string.IsNullOrWhiteSpace(rest) &&
                           TrySplitClause(rest.Trim(), out var nextLabel, out var nextRest))
                    {
                        labels.Add(nextLabel);
                        bodies.Add(new List<string>());
                        rest = nextRest;
                    }
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
                if (trimmed == "{") { nesting++; }
                else if (trimmed == "}") { nesting--; }
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
            var continueVar = "__swCont" + id2;

            // A "break" inside a switch ends the switch and nothing else, as it does in C#
            // and JavaScript. The do/while(false) wrapper is what gives it somewhere to go,
            // so it is always emitted -- inside a loop as much as outside one, since without
            // it the break would reach the real loop and turn the switch into a single pass
            // through it. "continue" still belongs to the enclosing loop, and the wrapper
            // would capture that, which is why a body containing one is redirected.
            if (hasContinue)
            {
                sb.Append(outer + "bool " + continueVar + " = false;\n");
            }
            sb.Append(outer + "do {\n");
            var switchValue = ReplaceArgsInString(switchExpr);
            sb.Append(outer + "  var " + valueVar + " = " + switchValue + ";\n");
            sb.Append(outer + "  bool " + matchVar + " = false;\n");

            var clauses = new StringBuilder();
            for (int i = 0; i < labels.Count; i++)
            {
                if (labels[i] == null)
                {
                    clauses.Append(outer + "  {\n");
                }
                else
                {
                    // The interpreter's rule (ProcessSwitch): the same type, and then equal --
                    // "3" is not case 3 there, where "==" (loose, as the interpreter compares
                    // elsewhere) and SameValue matched it.
                    var labelTest = "CscsOps.CaseMatches((object)(" + valueVar + "), (object)(" +
                        ReplaceArgsInString(labels[i]) + "))";
                    clauses.Append(outer + "  if (" + matchVar + " || " + labelTest + ") { " +
                        matchVar + " = true;\n");
                }

                m_depth = outer + "    ";
                clauses.Append(TranslateClause(bodies[i]));
                m_depth = outer;
                clauses.Append(outer + "  }\n");
            }

            sb.Append(hasContinue ?
                RedirectContinue(clauses.ToString(), "{ " + continueVar + " = true; break; }") :
                clauses.ToString());
            sb.Append(outer + "} while (false);\n");
            if (hasContinue)
            {
                // Outside any loop this is a C# error (CS0139), so the function falls back --
                // as it should: a continue with no loop around it means nothing.
                sb.Append(outer + "if (" + continueVar + ") { continue; }\n");
            }

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

        /// <summary>
        /// Translates a clause's statements with the main loop's own machinery. The builders
        /// for "for" and a nested "switch" read ahead in m_statements by index -- the header's
        /// condition and step, the block after the switch -- so a clause handed over one
        /// statement at a time left them reading the function's statements instead: a loop in a
        /// case came out as "for(j=0; __actionTempVar = ..." (CS1003) and a nested switch was
        /// copied through as a C# switch (CS8070). The clause becomes the statement list for
        /// the duration, exactly as the function body is.
        /// </summary>
        string TranslateClause(List<string> statements)
        {
            var savedStatements = m_statements;
            var savedId = m_statementId;
            var savedCurrent = m_currentStatement;
            var savedNext = m_nextStatement;
            var sb = new StringBuilder();
            try
            {
                m_statements = statements;
                m_statementId = 0;
                while (m_statementId < m_statements.Count)
                {
                    m_currentStatement = m_statements[m_statementId];
                    m_nextStatement = m_statementId < m_statements.Count - 1 ? m_statements[m_statementId + 1] : "";
                    var converted = ProcessStatement(m_currentStatement, m_nextStatement);
                    if (!string.IsNullOrWhiteSpace(converted))
                    {
                        sb.Append(converted);
                    }
                    m_statementId++;
                }
            }
            finally
            {
                m_statements = savedStatements;
                m_statementId = savedId;
                m_currentStatement = savedCurrent;
                m_nextStatement = savedNext;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Replaces each "continue;" in generated C# that belongs to the loop around a switch --
        /// one not inside a for, foreach, while or do of its own within the text -- with the
        /// replacement. A nested switch is a do/while too, so its own continues are skipped
        /// here; it redirected them itself, and the "if (flag) continue;" it left after its
        /// block sits at this level and is redirected in turn.
        /// </summary>
        static string RedirectContinue(string csharp, string replacement)
        {
            // Which characters are inside string or char literals, so neither a brace nor the
            // word in text is taken for code.
            var inLiteral = new bool[csharp.Length];
            for (int i = 0; i < csharp.Length; i++)
            {
                char c = csharp[i];
                bool verbatim = c == '@' && i + 1 < csharp.Length && csharp[i + 1] == '"';
                if (c != '"' && c != '\'' && !verbatim)
                {
                    continue;
                }
                int j = verbatim ? i + 2 : i + 1;
                char quote = verbatim ? '"' : c;
                while (j < csharp.Length)
                {
                    if (!verbatim && csharp[j] == '\\') { j += 2; continue; }
                    if (csharp[j] == quote)
                    {
                        if (verbatim && j + 1 < csharp.Length && csharp[j + 1] == '"') { j += 2; continue; }
                        break;
                    }
                    j++;
                }
                for (int k = i; k <= j && k < csharp.Length; k++) { inLiteral[k] = true; }
                i = j;
            }

            var sb = new StringBuilder(csharp.Length);
            var loops = new Stack<bool>();
            int loopDepth = 0;
            for (int i = 0; i < csharp.Length; i++)
            {
                char c = csharp[i];
                if (inLiteral[i]) { sb.Append(c); continue; }
                if (c == '{')
                {
                    bool isLoop = OpensLoop(csharp, i, inLiteral);
                    loops.Push(isLoop);
                    if (isLoop) { loopDepth++; }
                }
                else if (c == '}' && loops.Count > 0)
                {
                    if (loops.Pop()) { loopDepth--; }
                }
                else if (loopDepth == 0 && c == 'c' && (i == 0 || !IsNameChar(csharp[i - 1])) &&
                         string.CompareOrdinal(csharp, i, "continue", 0, 8) == 0)
                {
                    int after = i + 8;
                    while (after < csharp.Length && char.IsWhiteSpace(csharp[after])) { after++; }
                    if (after < csharp.Length && csharp[after] == ';' &&
                        (i + 8 >= csharp.Length || !IsNameChar(csharp[i + 8])))
                    {
                        sb.Append(replacement);
                        i = after;
                        continue;
                    }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Whether the "{" at this position opens the body of a for, foreach, while
        /// or do.</summary>
        static bool OpensLoop(string csharp, int brace, bool[] inLiteral)
        {
            int i = brace - 1;
            while (i >= 0 && char.IsWhiteSpace(csharp[i])) { i--; }
            if (i >= 0 && csharp[i] == ')' && !inLiteral[i])
            {
                int depth = 0;
                for (; i >= 0; i--)
                {
                    if (inLiteral[i]) { continue; }
                    if (csharp[i] == ')') { depth++; }
                    else if (csharp[i] == '(' && --depth == 0) { break; }
                }
                i--;
                while (i >= 0 && char.IsWhiteSpace(csharp[i])) { i--; }
            }
            int end = i;
            while (i >= 0 && IsNameChar(csharp[i])) { i--; }
            var word = end > i ? csharp.Substring(i + 1, end - i) : "";
            return word == "for" || word == "foreach" || word == "while" || word == "do";
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
                if (ch == '"' && !IsEscapedQuote(trimmed, i)) { inQuotes = !inQuotes; continue; }
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

        /// <summary>
        /// The loop counters that can be declared "int" without changing an answer.
        ///
        /// A counter is otherwise declared "double", because CSCS numbers are doubles and an
        /// int differs from one in exactly two ways a script can see: "/" between two ints
        /// truncates ("(i + 1) / 2" is 1 for i = 2, where CSCS says 1.5), and "*" overflows
        /// ("i * i" wraps past i = 46340). An int is also faster, and it is what an index or an
        /// overload such as Math.Round(x, i) wants. So a counter is an int only when all of
        /// this holds for every loop that uses the name, and it stays a double otherwise:
        ///   - the header is "name = start; name OP bound; step", where start is an integer
        ///     literal or an int argument nothing assigns to, OP is &lt; &lt;= &gt; or &gt;=, bound
        ///     is such a literal or argument or a ".Size"/".Length"/".Count" (with an optional
        ///     "+ k"/"- k"), and step is ++, --, "+= k" or "-= k" for a small literal k -- so the
        ///     counter holds whole numbers well inside int range;
        ///   - nothing else in the function assigns to it (including ++/--);
        ///   - no statement using it outside an index contains "*" outside an index, and no
        ///     statement using it at all contains "/";
        ///   - it is not copied into a new local ("x = i + 1" would declare x from an int);
        ///   - nothing reads a member off it or calls it.
        /// Uses inside [...] are exempt from the "*" and copy rules: an index is an int anyway.
        /// </summary>
        static HashSet<string> FindIntCounters(string code, HashSet<string> intArgs, HashSet<string> argNames)
        {
            var text = StripSpacesOutsideStrings(code ?? "");
            var qualifying = new HashSet<string>();
            var rejected = new HashSet<string>();
            var blanked = text.ToCharArray();

            // Pass 1: every "for(" header. A qualifying one is blanked out, so the scan below
            // sees only the other uses of its counter.
            bool inString = false;
            char quote = '\0';
            for (int pos = 0; pos < text.Length; pos++)
            {
                char c = text[pos];
                if (inString)
                {
                    if (c == '\\') { pos++; }
                    else if (c == quote) { inString = false; }
                    continue;
                }
                if (c == '"' || c == '\'') { inString = true; quote = c; continue; }
                if (c != 'f' || string.CompareOrdinal(text, pos, "for(", 0, 4) != 0 ||
                    (pos > 0 && IsNameChar(text[pos - 1])))
                {
                    continue;
                }
                int close = MatchingParen(text, pos + 3);
                if (close < 0)
                {
                    continue;
                }
                var header = text.Substring(pos + 4, close - pos - 4);
                var parts = SplitTopLevelSemicolons(header);
                if (parts.Count != 3)
                {
                    // "for (v in a)": its variable is an element, never a counter.
                    var name = LeadingName(header);
                    if (name != null) { rejected.Add(name); }
                    continue;
                }
                var init = System.Text.RegularExpressions.Regex.Match(parts[0], @"^([A-Za-z_]\w*)=(?!=)(.+)$");
                if (!init.Success)
                {
                    continue;
                }
                var counter = init.Groups[1].Value;
                var escaped = System.Text.RegularExpressions.Regex.Escape(counter);
                bool ok = !argNames.Contains(counter) && IsIntAtom(init.Groups[2].Value, intArgs, false) &&
                    System.Text.RegularExpressions.Regex.IsMatch(parts[1], "^" + escaped + @"(<=|>=|<|>)(.+)$") &&
                    IsIntAtom(System.Text.RegularExpressions.Regex.Match(parts[1], "^" + escaped + @"(<=|>=|<|>)(.+)$").Groups[2].Value, intArgs, true) &&
                    System.Text.RegularExpressions.Regex.IsMatch(parts[2],
                        "^(" + escaped + @"\+\+|\+\+" + escaped + "|" + escaped + "--|--" + escaped +
                        "|" + escaped + @"[+-]=\d{1,4})$");
                if (!ok)
                {
                    rejected.Add(counter);
                    continue;
                }
                qualifying.Add(counter);
                for (int k = pos + 4; k < close; k++) { blanked[k] = ' '; }
                pos = close;
            }
            qualifying.ExceptWith(rejected);
            if (qualifying.Count == 0)
            {
                return qualifying;
            }

            // Pass 2: every other use of each candidate, statement by statement.
            var scan = new string(blanked);
            foreach (var segment in Statements(scan))
            {
                var body = scan.Substring(segment.Item1, segment.Item2 - segment.Item1);
                var shape = Shape(body);   // strings removed, index contents marked
                bool hasDivision = shape.Contains('/');
                bool hasTopLevelProduct = TopLevel(shape).Contains('*');
                var assignment = System.Text.RegularExpressions.Regex.Match(shape, @"^([A-Za-z_]\w*)=(?!=)");
                foreach (var counter in qualifying.ToList())
                {
                    foreach (var at in NameOccurrences(shape, counter))
                    {
                        bool inIndex = IndexDepth(shape, at) > 0;
                        var after = shape.Substring(at + counter.Length);
                        var before = shape.Substring(0, at);
                        bool assigned = (after.StartsWith("=") && !after.StartsWith("==")) ||
                            after.StartsWith("+=") || after.StartsWith("-=") || after.StartsWith("*=") ||
                            after.StartsWith("/=") || after.StartsWith("%=") ||
                            after.StartsWith("++") || after.StartsWith("--") ||
                            before.EndsWith("++") || before.EndsWith("--");
                        bool member = after.StartsWith(".") || after.StartsWith("(");
                        bool copied = !inIndex && assignment.Success && assignment.Groups[1].Value != counter;
                        if (assigned || member || hasDivision || copied || (!inIndex && hasTopLevelProduct))
                        {
                            qualifying.Remove(counter);
                            break;
                        }
                    }
                }
            }
            return qualifying;
        }

        static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        /// <summary>
        /// The bare Math names -- "abs", "max", "round" -- that the interpreter resolves to a
        /// script function: one defined already, or the function being translated.
        /// </summary>
        HashSet<string> ScriptFunctionsShadowingMath()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var interpreter = m_parentScript?.InterpreterInstance;
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(m_cscsCode ?? "", @"(?<![\w.])([A-Za-z_]\w*)\s*\("))
            {
                var name = m.Groups[1].Value;
                if (string.Equals(name, m_functionName, StringComparison.OrdinalIgnoreCase) ||
                    (interpreter != null && interpreter.GetFunction(name) is CustomFunction))
                {
                    names.Add(name);
                }
            }
            return names;
        }

        /// <summary>
        /// A list literal in square brackets -- ["a", "b"], [[1, 2], [3]] -- as the same literal in
        /// braces, which the interpreter builds identically (checked: text, Size, Type, elements,
        /// nesting, empty) and the translator knows how to build. A "[" is a literal where the
        /// interpreter reads one -- after "=", "(", ",", ":", "?", "{", or inside another literal
        /// (not after "return", where it does not) -- and a subscript after a name, "]" or ")".
        /// </summary>
        static string RewriteBracketLiterals(string code)
        {
            var text = code ?? "";
            if (text.IndexOf('[') < 0)
            {
                return text;
            }
            var sb = new StringBuilder(text);
            var literal = new Stack<bool>();
            // Whether the literal open at each level sits inside a brace literal, where the
            // interpreter reads a bracket literal without the function's locals.
            var inBraceLiteral = new Stack<bool>();
            bool inQuotes = false;
            for (int i = 0; i < sb.Length; i++)
            {
                char c = sb[i];
                if (c == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (inQuotes)
                {
                    continue;
                }
                if (c == '[')
                {
                    int k = i - 1;
                    while (k >= 0 && char.IsWhiteSpace(text[k])) { k--; }
                    char before = k >= 0 ? text[k] : ';';
                    // Not after "return": the interpreter does not read a bracket literal there
                    // ("Couldn't find variable [...]"), and compiled code must not either.
                    bool isLiteral = "=(,:?{".IndexOf(before) >= 0 ||
                                     (before == '[' && literal.Count > 0 && literal.Peek());
                    // Inside a brace literal -- {"k": [1, 2, n]} -- the interpreter reads the
                    // bracket literal without the function's locals ("Couldn't find variable [n]"),
                    // so there only a literal of constants is the same both ways.
                    bool braced = before == ':' || before == '{' ||
                                  (before == '[' && inBraceLiteral.Count > 0 && inBraceLiteral.Peek());
                    if (isLiteral && braced && !ConstantsOnly(text, i))
                    {
                        isLiteral = false;
                    }
                    literal.Push(isLiteral);
                    inBraceLiteral.Push(braced);
                    if (isLiteral)
                    {
                        sb[i] = '{';
                    }
                }
                else if (c == ']' && literal.Count > 0)
                {
                    inBraceLiteral.Pop();
                    if (literal.Pop())
                    {
                        sb[i] = '}';
                    }
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// "for (x : a)" and "for (x of a)" as "for (x in a)": the interpreter reads all three the
        /// same way (Interpreter.ProcessArrayFor), and the translator knows only " in ".
        /// </summary>
        /// <summary>
        /// "for (v : {x, !x})": the literal a for-each walks is built in a statement of its own
        /// first -- "__forLitN = {x, !x}; for (v : __forLitN)". Built in the header, a literal
        /// holding a Variable did not compile, while the same literal held in a local did. The
        /// interpreter builds it once, before the loop, as this does.
        /// </summary>
        static string RewriteForEachLiterals(string code)
        {
            var text = code ?? "";
            if (text.IndexOf("for", StringComparison.Ordinal) < 0 || text.IndexOf('{') < 0)
            {
                return text;
            }
            int temp = 0;
            var pattern = new System.Text.RegularExpressions.Regex(
                @"(?<=^|[;{}])(\s*)for\s*\(\s*([A-Za-z_]\w*)\s*(:|\bin\b|\bof\b)\s*\{");
            int from = 0;
            while (true)
            {
                var m = pattern.Match(text, from);
                if (!m.Success)
                {
                    return text;
                }
                from = m.Index + m.Length;
                if (InsideQuotes(text, m.Index))
                {
                    continue;
                }
                int open = m.Index + m.Length - 1;
                int depth = 0;
                int close = -1;
                bool quoted = false;
                for (int i = open; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c == '"' && !IsEscapedQuote(text, i))
                    {
                        quoted = !quoted;
                    }
                    else if (!quoted && c == '{')
                    {
                        depth++;
                    }
                    else if (!quoted && c == '}' && --depth == 0)
                    {
                        close = i;
                        break;
                    }
                }
                if (close < 0)
                {
                    continue;
                }
                int paren = SkipSpaces(text, close + 1);
                if (paren >= text.Length || text[paren] != ')')
                {
                    continue;
                }
                var name = "__forLit" + (++temp);
                var literal = text.Substring(open, close - open + 1);
                var replacement = m.Groups[1].Value + name + "=" + literal + ";for(" + m.Groups[2].Value +
                                  (m.Groups[3].Value == ":" ? " : " : " " + m.Groups[3].Value + " ") + name + ")";
                text = text.Substring(0, m.Index) + replacement + text.Substring(paren + 1);
                from = m.Index + replacement.Length;
            }
        }

        static string RewriteForEachSeparator(string code)
        {
            var text = code ?? "";
            text = System.Text.RegularExpressions.Regex.Replace(text,
                @"(?<![\w.])for\s*\(\s*([A-Za-z_]\w*)\s*:\s*", m => InsideQuotes(text, m.Index) ? m.Value : "for(" + m.Groups[1].Value + " in ");
            return System.Text.RegularExpressions.Regex.Replace(text,
                @"(?<![\w.])for\s*\(\s*([A-Za-z_]\w*)\s+of\s+", m => InsideQuotes(text, m.Index) ? m.Value : "for(" + m.Groups[1].Value + " in ");
        }

        /// <summary>
        /// "Size(x)" as "x.Size" in a loop header. The built-in takes a variable's name -- it reads the token, not a
        /// value -- and answers GetSize() of its value: the count of an array, 0 for anything else.
        /// "x.Size" is Variable.Size, the same count, and compiles wherever an expression does,
        /// where the call went out as statements -- inside a for header, "for (i = 1; i <
        /// Size(rows); i++)", that was a syntax error. Only while Size is the built-in and x is a
        /// name, possibly subscripted, that this function or the interpreter already holds: a name
        /// defined later keeps the call, which the interpreter resolves when it runs.
        /// </summary>
        string RewriteSizeCalls(string code)
        {
            var text = code ?? "";
            var interpreter = m_parentScript?.InterpreterInstance;
            if (text.IndexOf("ize", StringComparison.OrdinalIgnoreCase) < 0 || interpreter == null ||
                interpreter.GetFunction(Constants.SIZE) is CustomFunction ||
                interpreter.GetFunction(Constants.SIZE) == null)
            {
                return text;
            }
            var assigned = AssignedAnywhere(text);
            return System.Text.RegularExpressions.Regex.Replace(text,
                // A subscript may hold a quoted key -- "size(json[\"Lines\"])" -- without brackets in it.
                @"(?<![\w.""])size\s*\(\s*([A-Za-z_]\w*)((?:\[(?:[^\[\]""]|""[^""\[\]]*"")*\])*)\s*\)",
                m =>
                {
                    var root = m.Groups[1].Value;
                    bool known = assigned.Contains(root) ||
                                 m_declaredArgsMap.Keys.Any(arg => string.Equals(arg, root, StringComparison.OrdinalIgnoreCase)) ||
                                 interpreter.GetVariable(root, m_parentScript) is GetVarFunction;
                    // In a loop header whatever the name is: the call route cannot go there at all,
                    // and a name defined later is read late (ResolveToken's member branch).
                    // Anywhere for a name already known: "x.Size" is the same value (GetSize, the
                    // length of text too), and the call route failed inside a joined text --
                    // "\"<\" + (Size(x) > 1) + \">\"".
                    return !InsideQuotes(text, m.Index) &&
                           (known || InLoopHeader(text, m.Index) || InShortCircuitStatement(text, m.Index) ||
                            InEqualityStatement(text, m.Index)) ?
                        root + m.Groups[2].Value + ".Size" : m.Value;
                },
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        /// <summary>Whether the position is inside the parentheses of a "for" or "while" header --
        /// the only place the call route cannot go, since it emits statements.</summary>
        static bool InLoopHeader(string text, int at)
        {
            int depth = 0;
            for (int i = at - 1; i >= 0; i--)
            {
                char c = text[i];
                if (c == ')') { depth++; }
                else if (c == '(')
                {
                    if (depth == 0)
                    {
                        var before = text.Substring(0, i).TrimEnd();
                        return before.EndsWith("for", StringComparison.Ordinal) ||
                               before.EndsWith("while", StringComparison.Ordinal);
                    }
                    depth--;
                }
                else if (c == ';' && depth == 0 || c == '{' || c == '}')
                {
                    // A for header holds ';' of its own; keep looking only past those.
                    if (c != ';') { return false; }
                }
            }
            return false;
        }

        /// <summary>Whether the quote at the position is escaped: an odd number of backslashes
        /// right before it. "a\\" ends with an escaped backslash and the quote closes the text; a test
        /// of the one character before it took the quote for escaped and ran the string on.</summary>
        static bool IsEscapedQuote(string text, int at)
        {
            int count = 0;
            for (int k = at - 1; k >= 0 && text[k] == '\\'; k--)
            {
                count++;
            }
            return count % 2 == 1;
        }

        /// <summary>The text with the contents of every string literal replaced by spaces, the
        /// quotes and every position kept.</summary>
        static string WithoutStringContents(string text)
        {
            var sb = new StringBuilder(text);
            bool inQuotes = false;
            for (int i = 0; i < sb.Length; i++)
            {
                char c = sb[i];
                if (c == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                }
                else if (inQuotes)
                {
                    sb[i] = ' ';
                }
            }
            return sb.ToString();
        }

        /// <summary>Whether the statement around the position compares with "==" or "!=": the
        /// comparison is rewritten to Variable.SameValue, whose operands are written as C#, and the
        /// Size call route cannot go there -- "if (Size(numbers) != 3)" came out as a bare Size().</summary>
        static bool InEqualityStatement(string text, int at)
        {
            int start = at, end = at;
            while (start > 0 && ";{}".IndexOf(text[start - 1]) < 0) { start--; }
            while (end < text.Length && ";{}".IndexOf(text[end]) < 0) { end++; }
            var statement = WithoutStringContents(text.Substring(start, end - start));
            return statement.Contains("==") || statement.Contains("!=");
        }

        /// <summary>Whether the statement around the position has "&&", "||" or "?": there a call
        /// has to stay inline, which the call route of a built-in like Size cannot.</summary>
        static bool InShortCircuitStatement(string text, int at)
        {
            int start = at, end = at, depth = 0;
            while (start > 0)
            {
                char c = text[start - 1];
                if (c == ')') { depth++; }
                else if (c == '(') { depth--; }
                else if (depth <= 0 && (c == ';' || c == '{' || c == '}')) { break; }
                start--;
            }
            depth = 0;
            while (end < text.Length)
            {
                char c = text[end];
                if (c == '(') { depth++; }
                else if (c == ')') { depth--; }
                else if (depth <= 0 && (c == ';' || c == '{' || c == '}')) { break; }
                end++;
            }
            var statement = WithoutStringContents(text.Substring(start, end - start));
            return statement.Contains("&&") || statement.Contains("||") || statement.IndexOf('?') >= 0;
        }

        /// <summary>Whether the bracket opened at <paramref name="open"/> holds only constants --
        /// strings, numbers, nested brackets -- and no name.</summary>
        static bool ConstantsOnly(string text, int open)
        {
            int depth = 0;
            bool inQuotes = false;
            for (int i = open; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (inQuotes)
                {
                    continue;
                }
                if (c == '[') { depth++; }
                else if (c == ']' && --depth == 0) { return true; }
                else if (char.IsLetter(c) || c == '_') { return false; }
            }
            return false;
        }

        static bool InsideQuotes(string text, int at)
        {
            bool inQuotes = false;
            for (int i = 0; i < at && i < text.Length; i++)
            {
                if (text[i] == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                }
            }
            return inQuotes;
        }

        static string StepValueReturns(string code)
        {
            return string.IsNullOrEmpty(code) || code.IndexOf("__rs", StringComparison.Ordinal) < 0 ? code :
                System.Text.RegularExpressions.Regex.Replace(code,
                    @"Variable\.ConvertToVariable\(\s*(__rs\d+)\s*\)", "CscsOps.StepValue($1)");
        }

        /// <summary>
        /// "return ++x;" and "return x++;": the step as a statement of its own, and the value
        /// returned -- "x++; return x;" and "__rsN = x; x++; return __rsN;". The return path
        /// handed "++x" to the statement translation, which spells a step as the postfix one (right
        /// for a statement, not for a value): "return ++x" gave 5 for x = 5, and on a global the
        /// value was lost altogether.
        /// </summary>
        static string RewriteReturnStep(string code)
        {
            var text = code ?? "";
            if (text.IndexOf("++", StringComparison.Ordinal) < 0 && text.IndexOf("--", StringComparison.Ordinal) < 0)
            {
                return text;
            }
            int temp = 0;
            var target = @"[A-Za-z_]\w*(?:\.[A-Za-z_]\w*|\[[^\[\];""]*\])*";
            text = System.Text.RegularExpressions.Regex.Replace(text,
                @"(?<=^|[;{}])(\s*)return\s*(\+\+|--)\s*(" + target + @")\s*;",
                m => InsideQuotes(text, m.Index) ? m.Value :
                    m.Groups[1].Value + m.Groups[3].Value + m.Groups[2].Value + ";return " + m.Groups[3].Value + ";");
            return System.Text.RegularExpressions.Regex.Replace(text,
                @"(?<=^|[;{}])(\s*)return\s*(" + target + @")\s*(\+\+|--)\s*;",
                m =>
                {
                    if (InsideQuotes(text, m.Index))
                    {
                        return m.Value;
                    }
                    var keep = "__rs" + (++temp);
                    return m.Groups[1].Value + keep + "=" + m.Groups[2].Value + ";" + m.Groups[2].Value +
                           m.Groups[3].Value + ";return " + keep + ";";
                });
        }

        /// <summary>
        /// "if (++x >= n)": the step taken out ahead of the statement -- "x++; if (x >= n)"; for
        /// "if (x++ % 2 == 0)" the value before it too -- "__ps1=x; x++; if (__ps1 % 2 == 0)". The
        /// interpreter runs an "if" condition once, the step first, so that is the same; C# had
        /// "++" on a value it cannot step (CS1059, a global read by name). Only a statement that
        /// is an "if" of its own (not "else if", not a loop), one prefix step in the condition, and
        /// no "&&", "||" or "?" there, which may skip it.
        /// </summary>
        /// <summary>
        /// A call to a script function with named arguments -- "tDef(s = \"q\", n = n + 1)" -- as
        /// the positional call it stands for: "tDef(n + 1, \"q\")". The statement builders read
        /// "s = " inside the parentheses as an assignment. Only where that is certainly the same
        /// call: a named value reaches the callee as a copy (AssignFunction) and a positional one
        /// as itself, so a value that may be a collection -- a bare name, element or member not
        /// known to be a number or text -- keeps the call with the interpreter; so does a skipped
        /// parameter without a default the call can spell (a number or text), and more than one
        /// argument with a call in it, whose order the rewrite could change.
        /// </summary>
        string RewriteNamedArgumentCalls(string code)
        {
            var text = code ?? "";
            var interpreter = m_parentScript?.InterpreterInstance;
            if (interpreter == null || text.IndexOf('=') < 0)
            {
                return text;
            }
            var callPattern = new System.Text.RegularExpressions.Regex(@"(?<![\w.])([A-Za-z_]\w*)\s*\(");
            int from = 0;
            while (true)
            {
                var m = callPattern.Match(text, from);
                if (!m.Success)
                {
                    return text;
                }
                from = m.Index + m.Length;
                if (InsideQuotes(text, m.Index))
                {
                    continue;
                }
                var callee = interpreter.GetFunction(m.Groups[1].Value) as CustomFunction;
                if (callee == null || callee.GetType() != typeof(CustomFunction) || callee.RealArgs == null)
                {
                    continue;
                }
                int open = m.Index + m.Length - 1;
                int close = FindMatchingParen(text, open);
                if (close < 0)
                {
                    continue;
                }
                var positional = PositionalArguments(callee, SplitTopLevelOn(text.Substring(open + 1, close - open - 1), ","));
                if (positional == null)
                {
                    continue;
                }
                text = text.Substring(0, open + 1) + positional + text.Substring(close);
                from = open + 1;
            }
        }

        string PositionalArguments(CustomFunction callee, List<string> args)
        {
            var names = callee.RealArgs;
            var slots = new string[names.Length];
            bool namedSeen = false;
            int withCalls = 0;
            for (int i = 0; i < args.Count; i++)
            {
                var arg = args[i].Trim();
                if (arg.Length == 0)
                {
                    return null;
                }
                if (arg.IndexOf('(') >= 0)
                {
                    withCalls++;
                }
                var named = System.Text.RegularExpressions.Regex.Match(arg, @"^([A-Za-z_]\w*)\s*=(?!=)\s*(.+)$",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                int index = named.Success ?
                    Array.FindIndex(names, n => string.Equals(n, named.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) : -1;
                if (index >= 0)
                {
                    var value = named.Groups[2].Value.Trim();
                    if (slots[index] != null || !IsNewValue(value))
                    {
                        return null;
                    }
                    slots[index] = value;
                    namedSeen = true;
                }
                else
                {
                    // A positional argument after a named one is the interpreter's error.
                    if (namedSeen || named.Success || i >= slots.Length)
                    {
                        return null;
                    }
                    slots[i] = arg;
                }
            }
            if (!namedSeen || withCalls > 1)
            {
                return null;
            }
            int last = Array.FindLastIndex(slots, s => s != null);
            for (int i = 0; i <= last; i++)
            {
                if (slots[i] != null)
                {
                    continue;
                }
                var byDefault = callee.DefaultArgument(i);
                if (byDefault == null)
                {
                    return null;
                }
                if (byDefault.Type == Variable.VarType.NUMBER)
                {
                    slots[i] = byDefault.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (byDefault.Type == Variable.VarType.STRING && byDefault.String != null)
                {
                    slots[i] = "\"" + byDefault.String.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                }
                else
                {
                    return null;
                }
            }
            return string.Join(",", slots.Take(last + 1));
        }

        static int FindMatchingBracketOrBrace(string text, int open)
        {
            int depth = 0;
            bool quoted = false;
            for (int i = open; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"' && !IsEscapedQuote(text, i)) { quoted = !quoted; }
                else if (!quoted && (c == '{' || c == '[')) { depth++; }
                else if (!quoted && (c == '}' || c == ']') && --depth == 0) { return i; }
            }
            return -1;
        }

        // A value that is not a reference to something else: a literal, a number or text argument,
        // or the result of an operator.
        bool IsNewValue(string value)
        {
            var text = value.Trim();
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^-?\d+(\.\d+)?$") ||
                (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"' && text.IndexOf('"', 1) == text.Length - 1))
            {
                return true;
            }
            // A collection literal builds a new collection: nothing else holds it.
            if (text.Length >= 2 && (text[0] == '{' || text[0] == '[') &&
                FindMatchingBracketOrBrace(text, 0) == text.Length - 1)
            {
                return true;
            }
            if (IsPlainName(text) && m_argsMap.TryGetValue(text, out var arg) &&
                (arg.Type == Variable.VarType.INT || arg.Type == Variable.VarType.NUMBER || arg.Type == Variable.VarType.STRING))
            {
                return true;
            }
            var plain = WithoutStringContents(text);
            int depth = 0;
            for (int i = 1; i < plain.Length; i++)
            {
                char c = plain[i];
                if (c == '(' || c == '[' || c == '{') { depth++; }
                else if (c == ')' || c == ']' || c == '}') { depth--; }
                else if (depth == 0 && "+-*/%<>=!&|".IndexOf(c) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Braces around a body of one statement -- "if (x) return 1; else return 2;",
        /// "for (i = 0; i &lt; n; i++) r += i;", "while (k &lt; 3) k++;" -- which the interpreter runs
        /// as the one statement (Interpreter.ProcessIf, ProcessLoopBody). The statement
        /// builders expect blocks: without braces a "return" or "throw" in the body was not seen
        /// as one (a string returned where a Variable is, CS0029), and a loop body was glued to
        /// its header. An "if" body takes its "else" chain along, a loop body its own body; a
        /// body whose end is not certain is left as it is.
        /// </summary>
        static string RewriteBracelessBodies(string code)
        {
            var text = code ?? "";
            if (!System.Text.RegularExpressions.Regex.IsMatch(text, @"(?<![\w.])(if|for|while|else)\b"))
            {
                return text;
            }
            int from = 0;
            for (int guard = 0; guard < 1000; guard++)
            {
                int bodyStart = -1, bodyEnd = -1;
                bool quoted = false;
                for (int i = from; i < text.Length && bodyStart < 0; i++)
                {
                    char c = text[i];
                    if (c == '"' && !IsEscapedQuote(text, i))
                    {
                        quoted = !quoted;
                        continue;
                    }
                    if (quoted || !char.IsLetter(c) || (i > 0 && (char.IsLetterOrDigit(text[i - 1]) ||
                        text[i - 1] == '_' || text[i - 1] == '.')))
                    {
                        continue;
                    }
                    int b = BracelessBodyAt(text, i);
                    if (b >= 0)
                    {
                        int e = SingleStatementEnd(text, b);
                        if (e > b)
                        {
                            bodyStart = b;
                            bodyEnd = e;
                        }
                    }
                }
                if (bodyStart < 0)
                {
                    return text;
                }
                text = text.Substring(0, bodyStart) + "{" + text.Substring(bodyStart, bodyEnd - bodyStart) + "}" +
                       text.Substring(bodyEnd);
                from = bodyStart + 1;
            }
            return text;
        }

        // The start of the body of the if, for, while or else at "at", when that body is not a
        // block; -1 otherwise ("else if" too: the "if" is taken on its own).
        static int BracelessBodyAt(string text, int at)
        {
            string word = WordAt(text, at);
            int after = at + word.Length;
            if (word == "else")
            {
                int b = SkipSpaces(text, after);
                if (b >= text.Length || text[b] == '{' || WordAt(text, b) == "if")
                {
                    return -1;
                }
                return b;
            }
            if (word != "if" && word != "for" && word != "while")
            {
                return -1;
            }
            int open = SkipSpaces(text, after);
            if (open >= text.Length || text[open] != '(')
            {
                return -1;
            }
            int close = FindMatchingParen(text, open);
            if (close < 0)
            {
                return -1;
            }
            int body = SkipSpaces(text, close + 1);
            // "while (...);" ending a do-while is no body.
            if (body >= text.Length || text[body] == '{' || text[body] == ';' || text[body] == '}')
            {
                return -1;
            }
            return body;
        }

        // Where the statement starting at "at" ends (just past it), or -1 when that is not certain.
        static int SingleStatementEnd(string text, int at)
        {
            at = SkipSpaces(text, at);
            string word = WordAt(text, at);
            if (word == "if" || word == "for" || word == "while")
            {
                int open = SkipSpaces(text, at + word.Length);
                if (open >= text.Length || text[open] != '(')
                {
                    return -1;
                }
                int close = FindMatchingParen(text, open);
                if (close < 0)
                {
                    return -1;
                }
                int end = BodyEnd(text, close + 1);
                if (end < 0 || word != "if")
                {
                    return end;
                }
                int next = SkipSpaces(text, end);
                if (WordAt(text, next) == "else")
                {
                    return BodyEnd(text, next + 4);
                }
                return end;
            }
            if (word == "do" || word == "try" || word == "switch" || word == "else")
            {
                return -1;
            }
            int depth = 0;
            bool quoted = false;
            for (int i = at; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"' && !IsEscapedQuote(text, i))
                {
                    quoted = !quoted;
                }
                else if (quoted)
                {
                    continue;
                }
                else if (c == '(' || c == '[' || c == '{')
                {
                    depth++;
                }
                else if (c == ')' || c == ']' || c == '}')
                {
                    if (--depth < 0)
                    {
                        return -1;
                    }
                }
                else if (c == ';' && depth == 0)
                {
                    return i + 1;
                }
            }
            return -1;
        }

        // The end of a body starting at "at": a block through its "}", or one statement.
        static int BodyEnd(string text, int at)
        {
            int b = SkipSpaces(text, at);
            if (b < text.Length && text[b] == '{')
            {
                int depth = 0;
                bool quoted = false;
                for (int i = b; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c == '"' && !IsEscapedQuote(text, i))
                    {
                        quoted = !quoted;
                    }
                    else if (!quoted && c == '{')
                    {
                        depth++;
                    }
                    else if (!quoted && c == '}' && --depth == 0)
                    {
                        return i + 1;
                    }
                }
                return -1;
            }
            return SingleStatementEnd(text, b);
        }

        static string WordAt(string text, int at)
        {
            int end = at;
            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
            {
                end++;
            }
            return text.Substring(at, end - at);
        }

        static int SkipSpaces(string text, int at)
        {
            while (at < text.Length && char.IsWhiteSpace(text[at]))
            {
                at++;
            }
            return at;
        }

        static string RewritePrefixStepInIf(string code)
        {
            var text = code ?? "";
            if (text.IndexOf("++", StringComparison.Ordinal) < 0 && text.IndexOf("--", StringComparison.Ordinal) < 0)
            {
                return text;
            }
            var sb = new StringBuilder(text.Length + 16);
            int copied = 0;
            int temp = 0;
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(text, @"(?<=^|[;{}])\s*if\s*\("))
            {
                if (m.Index < copied || InsideQuotes(text, m.Index))
                {
                    continue;
                }
                int open = text.IndexOf('(', m.Index);
                int close = FindMatchingParen(text, open);
                if (close < 0)
                {
                    continue;
                }
                var condition = text.Substring(open + 1, close - open - 1);
                var plain = WithoutStringContents(condition);
                if (NeedsInlineCalls(condition))
                {
                    continue;
                }
                if (System.Text.RegularExpressions.Regex.Matches(plain, @"\+\+|--").Count != 1)
                {
                    continue;
                }
                var steps = System.Text.RegularExpressions.Regex.Matches(plain, @"(?<![\w)\]+-])(\+\+|--)\s*([A-Za-z_]\w*)(?![\w(\[.])");
                // A postfix step -- "if (counter++ % 2 == 0)" -- tests the value from before it:
                // that is kept in a temporary ahead of the step.
                var postfix = System.Text.RegularExpressions.Regex.Matches(plain, @"(?<![\w.)\]])([A-Za-z_]\w*)\s*(\+\+|--)(?![\w(\[.+-])");
                if (steps.Count + postfix.Count != 1)
                {
                    continue;
                }
                bool isPostfix = postfix.Count == 1;
                var step = isPostfix ? postfix[0] : steps[0];
                var name = isPostfix ? step.Groups[1].Value : step.Groups[2].Value;
                var op = isPostfix ? step.Groups[2].Value : step.Groups[1].Value;
                var before = isPostfix ? "__ps" + (++temp) : name;
                var rewritten = condition.Substring(0, step.Index) + before + condition.Substring(step.Index + step.Length);
                int statementStart = m.Index + (m.Value.Length - m.Value.TrimStart().Length);
                sb.Append(text, copied, statementStart - copied);
                if (isPostfix)
                {
                    sb.Append(before).Append('=').Append(name).Append(';');
                }
                sb.Append(name).Append(op).Append(';');
                sb.Append(text, statementStart, open + 1 - statementStart);
                sb.Append(rewritten);
                copied = close;
            }
            sb.Append(text, copied, text.Length - copied);
            return sb.ToString();
        }

        /// <summary>
        /// Ends a statement at the collection literal it assigns when the script left out the
        /// ";" -- "object = {\"success\": true}  jsonString = SerializeJson(object);". The
        /// interpreter's statement ends with the literal; to the translator the next name ran on
        /// into it. Only after "=" and only where a name follows straight away.
        /// </summary>
        static string TerminateLiteralStatements(string code)
        {
            var text = code ?? "";
            if (text.IndexOf("{", StringComparison.Ordinal) < 0)
            {
                return TerminateBeforeReturn(text);
            }
            var sb = new StringBuilder(text.Length + 8);
            bool inString = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                sb.Append(c);
                if (inString)
                {
                    if (c == '\\' && i + 1 < text.Length) { sb.Append(text[++i]); }
                    else if (c == '"') { inString = false; }
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c != '{')
                {
                    continue;
                }
                int k = i - 1;
                while (k >= 0 && char.IsWhiteSpace(text[k])) { k--; }
                if (k < 0 || text[k] != '=' || (k > 0 && "=!<>".IndexOf(text[k - 1]) >= 0))
                {
                    continue;
                }
                int close = MatchingBrace(text, i);
                if (close < 0)
                {
                    continue;
                }
                int next = close + 1;
                while (next < text.Length && char.IsWhiteSpace(text[next])) { next++; }
                sb.Append(text, i + 1, close - i);
                if (next < text.Length && (char.IsLetter(text[next]) || text[next] == '_'))
                {
                    sb.Append(';');
                }
                i = close;
            }
            return TerminateBeforeReturn(sb.ToString());
        }

        /// <summary>
        /// "json = SerializeJson(o)   return json;", "print(x)   Test(a, b);": a call's ")" straight
        /// before a name ends the statement for the interpreter; the translator read the next
        /// statement into it. Not the ")" of an "if", "while", "for" or the like, whose body
        /// follows it.
        /// </summary>
        static string TerminateBeforeReturn(string text)
        {
            var sb = new StringBuilder(text.Length + 4);
            int copied = 0;
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(text, @"\)(\s*)(?=[A-Za-z_])"))
            {
                if (InsideQuotes(text, m.Index))
                {
                    continue;
                }
                int open = MatchingOpenParen(text, m.Index);
                if (open < 0)
                {
                    continue;
                }
                int k = open - 1;
                while (k >= 0 && char.IsWhiteSpace(text[k])) { k--; }
                int end = k + 1;
                while (k >= 0 && (char.IsLetterOrDigit(text[k]) || text[k] == '_')) { k--; }
                var word = text.Substring(k + 1, end - k - 1);
                if (word == "if" || word == "elif" || word == "while" || word == "for" || word == "foreach" ||
                    word == "switch" || word == "catch" || word == "else")
                {
                    continue;
                }
                sb.Append(text, copied, m.Index + 1 - copied).Append(';');
                copied = m.Index + 1;
            }
            sb.Append(text, copied, text.Length - copied);
            return sb.ToString();
        }

        // The "(" matching the ")" at the position, outside string literals; -1 if none.
        static int MatchingOpenParen(string text, int close)
        {
            int depth = 0;
            for (int i = close; i >= 0; i--)
            {
                if (text[i] == '"' && !IsEscapedQuote(text, i))
                {
                    // Skip back over a string literal.
                    int j = i - 1;
                    while (j >= 0 && !(text[j] == '"' && !IsEscapedQuote(text, j))) { j--; }
                    i = j;
                    continue;
                }
                if (text[i] == ')') { depth++; }
                else if (text[i] == '(' && --depth == 0) { return i; }
            }
            return -1;
        }

        /// <summary>
        /// "type(x)" -- the interpreter's TypeFunction -- is spelt "Type(x)". The lower-case name is
        /// one of Constants.RESERVED, which the translator checks case-sensitively and treats as
        /// a keyword, so the call went out as C# (CS0103); to the interpreter, whose names are
        /// case-blind, both spellings are the same function. A member (".type") and text are left.
        /// </summary>
        string RewriteTypeCalls(string code)
        {
            var text = code ?? "";
            if (text.IndexOf("type", StringComparison.Ordinal) < 0 ||
                !(m_parentScript?.InterpreterInstance?.GetFunction(Constants.TYPE) is TypeFunction))
            {
                return text;
            }
            return System.Text.RegularExpressions.Regex.Replace(text, @"(?<![\w.])type(?=\s*\()",
                m => InsideQuotes(text, m.Index) ? m.Value : "Type");
        }

        /// <summary>
        /// Spells every mention of an argument the way the signature does. The signature
        /// parser lowercases the names ("stepby") while the body keeps the script's spelling
        /// ("stepBy", "StepBy"); the interpreter treats all of them as one name, and a C# local
        /// has one spelling. Strings and members (".stepBy") are left alone.
        /// </summary>
        string NormalizeArgSpelling(string code)
        {
            var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in m_declaredArgsMap.Keys)
            {
                args[name] = name;
            }
            var text = code ?? "";
            if (args.Count == 0 || text.Length == 0)
            {
                return text;
            }
            var sb = new StringBuilder(text.Length);
            bool inQuotes = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                }
                if (inQuotes || !(char.IsLetter(ch) || ch == '_') ||
                    (i > 0 && (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] == '_')))
                {
                    sb.Append(ch);
                    continue;
                }
                int end = i;
                while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
                {
                    end++;
                }
                var word = text.Substring(i, end - i);
                bool member = i > 0 && text[i - 1] == '.';
                // Nor a call: "ShowView(v, showView)" with an argument "showView" calls the
                // function, to the interpreter as here.
                int after = end;
                while (after < text.Length && char.IsWhiteSpace(text[after])) { after++; }
                bool call = after < text.Length && text[after] == '(';
                sb.Append(!member && !call && args.TryGetValue(word, out var spelled) ? spelled : word);
                i = end - 1;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Moves a member call on a "variable" argument -- "s.At(1)", "s.Equals(\"x\")",
        /// "w.Trim()" -- into a temporary ahead of the
        /// statement: "__mcN=s.At(1);". The assignment is a shape the translator runs through
        /// the interpreter (IsUnmappedMemberCallOnVariable), which is exact, where the call in
        /// the middle of an expression or a condition went out as C# the Variable does not have.
        /// Only where running it first changes nothing: a plain statement, a "return" or an
        /// "if", with nothing ahead of the call that calls, groups, steps or short-circuits.
        /// </summary>
        string RewriteVariableMemberCalls(string code)
        {
            var variableArgs = VariableParamNames();
            var text = code ?? "";
            if (text.IndexOf('(') < 0)
            {
                return text;
            }
            var sb = new StringBuilder(text.Length + 32);
            int start = 0;
            int depth = 0;
            int temp = 0;
            bool inString = false;
            char quote = '\0';
            for (int i = 0; i <= text.Length; i++)
            {
                char c = i < text.Length ? text[i] : ';';
                if (inString)
                {
                    if (c == '\\') { i++; }
                    else if (c == quote) { inString = false; }
                    continue;
                }
                if (c == '"' || c == '\'') { inString = true; quote = c; continue; }
                if (c == '(' || c == '[') { depth++; continue; }
                if (c == ')' || c == ']') { depth--; continue; }
                if (c == '{' && OpensBraceLiteral(text, i))
                {
                    int close = MatchingBrace(text, i);
                    if (close > i)
                    {
                        i = close;
                        continue;
                    }
                }
                if (depth != 0 || (c != ';' && c != '{' && c != '}'))
                {
                    continue;
                }
                var statement = text.Substring(start, Math.Min(i, text.Length) - start);
                sb.Append(MemberCallsHoisted(statement, variableArgs, ref temp) ?? statement);
                if (i < text.Length) { sb.Append(c); }
                start = i + 1;
            }
            return sb.ToString();
        }

        /// <summary>Whether every call in the text is a Math call whose own arguments are
        /// whole operands or Math calls in turn.</summary>
        static bool OnlyMathCalls(string text)
        {
            foreach (System.Text.RegularExpressions.Match call in
                     System.Text.RegularExpressions.Regex.Matches(text ?? "", @"([A-Za-z_][\w.]*)\s*\("))
            {
                if (!call.Groups[1].Value.StartsWith("Math.", StringComparison.Ordinal))
                {
                    return false;
                }
            }
            // A bare parenthesis (grouping) is not a call; the builder takes only whole operands.
            int calls = System.Text.RegularExpressions.Regex.Matches(text ?? "", @"[A-Za-z_][\w.]*\s*\(").Count;
            return calls == (text ?? "").Count(ch => ch == '(');
        }

        /// <summary>Whether the name is a function the script defines, or the one being translated.</summary>
        bool IsScriptFunctionName(string name)
        {
            return string.Equals(name, m_functionName, StringComparison.OrdinalIgnoreCase) ||
                   m_parentScript?.InterpreterInstance?.GetFunction(name) is CustomFunction ||
                   IsLateFunctionName(name);
        }

        string MemberCallsHoisted(string statement, HashSet<string> variableArgs, ref int temp)
        {
            var trimmed = statement.Trim();
            if (trimmed.Length == 0 || trimmed.Contains("&&") || trimmed.Contains("||") ||
                trimmed.Contains("++") || trimmed.Contains("--") || IndexOfTopLevelChar(trimmed, '?') >= 0)
            {
                return null;
            }
            int bodyStart = 0;
            if (StartsWithKeyword(trimmed, Constants.IF))
            {
                bodyStart = trimmed.IndexOf('(');
                if (bodyStart < 0 || FindMatchingParen(trimmed, bodyStart) != trimmed.Length - 1)
                {
                    return null;
                }
                bodyStart++;
            }
            else if (StartsWithKeyword(trimmed, Constants.RETURN))
            {
                bodyStart = Constants.RETURN.Length;
            }
            else
            {
                foreach (var keyword in new[] { Constants.WHILE, Constants.FOR, Constants.ELSE_IF, Constants.ELSE,
                                                Constants.SWITCH, Constants.CASE, Constants.DEFAULT, Constants.CATCH,
                                                Constants.THROW, Constants.DO, Constants.FUNCTION, Constants.CLASS,
                                                Constants.TRY, "finally", "elif", "var" })
                {
                    if (StartsWithKeyword(trimmed, keyword))
                    {
                        return null;
                    }
                }
            }
            var hoisted = new StringBuilder();
            var rest = new StringBuilder(trimmed.Substring(0, bodyStart));
            int i = bodyStart;
            bool changed = false;
            bool inString = false;
            while (i < trimmed.Length)
            {
                char c = trimmed[i];
                if (c == '"' && !IsEscapedQuote(trimmed, i))
                {
                    inString = !inString;
                }
                if (inString || !(char.IsLetter(c) || c == '_') ||
                    (i > 0 && (char.IsLetterOrDigit(trimmed[i - 1]) || trimmed[i - 1] == '_' || trimmed[i - 1] == '.')))
                {
                    // Anything ahead of a hoisted call that could run code stops the rewrite.
                    if (!inString && c == '(')
                    {
                        break;
                    }
                    rest.Append(c);
                    i++;
                    continue;
                }
                var m = System.Text.RegularExpressions.Regex.Match(trimmed.Substring(i), @"^([A-Za-z_]\w*)\.([A-Za-z_]\w*)\(");
                // A call to a script function -- or to this one -- first in an "if" condition, with
                // arithmetic in its arguments: the condition reaches the translator cut at the
                // operator ("if(f(n"), so "if (f(n - 1) > 4)" went out as a C# call to f.
                if (!m.Success && bodyStart > 0 && StartsWithKeyword(trimmed, Constants.IF))
                {
                    var scriptCall = System.Text.RegularExpressions.Regex.Match(trimmed.Substring(i), @"^([A-Za-z_]\w*)\s*\(");
                    if (scriptCall.Success && IsScriptFunctionName(scriptCall.Groups[1].Value))
                    {
                        int callClose = FindMatchingParen(trimmed, i + scriptCall.Length - 1);
                        var callArgs = callClose > 0 ? trimmed.Substring(i + scriptCall.Length, callClose - i - scriptCall.Length) : "";
                        if (callClose > 0 && callArgs.IndexOf('"') < 0 && callArgs.IndexOf('(') < 0 &&
                            callArgs.IndexOfAny(new[] { '+', '-', '*', '/', '%' }) >= 0)
                        {
                            var callTemp = "__mc" + (++temp);
                            hoisted.Append(callTemp).Append('=').Append(trimmed, i, callClose - i + 1).Append(';');
                            rest.Append(callTemp);
                            i = callClose + 1;
                            changed = true;
                            continue;
                        }
                    }
                }
                // "Math.Sin(x)" over a "variable" argument is hoisted too: its assignment is built
                // as a call the interpreter runs (TryBuildMathOnVariable).
                bool isMath = m.Success && m.Groups[1].Value == "Math";
                if (isMath)
                {
                    int mathClose = FindMatchingParen(trimmed, i + m.Length - 1);
                    var mathArgs = mathClose > 0 ? trimmed.Substring(i + m.Length, mathClose - i - m.Length) : "";
                    // Flat arguments only, as TryBuildMathOnVariable takes them: a nested call keeps
                    // the path it had ("return Math.Max(Math.Abs(n - 10), 5)" went to the
                    // interpreter whole).
                    isMath = mathClose > 0 && OnlyMathCalls(mathArgs) && mathArgs.IndexOf('"') < 0 &&
                             MentionsAny(mathArgs, variableArgs);
                }
                if (!m.Success || (!isMath && !variableArgs.Contains(m.Groups[1].Value)))
                {
                    int end = i;
                    while (end < trimmed.Length && (char.IsLetterOrDigit(trimmed[end]) || trimmed[end] == '_')) { end++; }
                    rest.Append(trimmed, i, end - i);
                    i = end;
                    continue;
                }
                int open = i + m.Length - 1;
                int close = FindMatchingParen(trimmed, open);
                if (close < 0)
                {
                    return null;
                }
                var call = trimmed.Substring(i, close - i + 1);
                var member = m.Groups[2].Value;
                var args = trimmed.Substring(open + 1, close - open - 1);
                bool needs = isMath || (!IsVariableMember(member) && !IsCollectionMethod(member));
                // "x = s.At(1)" and "return s.At(1)" already take the interpreter's path whole.
                var before = rest.ToString().Trim();
                bool wholeValue = close == trimmed.Length - 1 &&
                    ((before == Constants.RETURN && !isMath) || (before.EndsWith("=") && IsPlainName(before.TrimEnd('=').Trim()) &&
                                                    !before.EndsWith("==")));
                if (!needs || wholeValue)
                {
                    // The whole value already goes to the interpreter; nothing follows it.
                    if (needs)
                    {
                        break;
                    }
                    rest.Append(call);
                    i = close + 1;
                    continue;
                }
                var name = "__mc" + (++temp);
                hoisted.Append(name).Append('=').Append(call).Append(';');
                rest.Append(name);
                i = close + 1;
                changed = true;
            }
            if (!changed)
            {
                return null;
            }
            rest.Append(trimmed, i, trimmed.Length - i);
            int lead = statement.Length - statement.TrimStart().Length;
            return statement.Substring(0, lead) + hoisted + rest;
        }

        /// <summary>
        /// Turns "var x = v" into "x = v" and records x as a local. In the interpreter "var"
        /// declares a local that hides a global of the same name ("var g = 7" leaves the
        /// global g alone), which is what a translator local already is once the global
        /// paths are told to leave the name alone (IsInterpreterVariable). Only when the
        /// declaration is the name's first mention -- a use before it would read or write the
        /// global -- and never for "var E = Enum", which TryBuildLocalEnum builds.
        /// </summary>
        string RewriteVarDeclarations(string code)
        {
            m_varLocals.Clear();
            var text = code ?? "";
            var sb = new StringBuilder(text.Length);
            bool inQuotes = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                }
                bool statementStart = i == 0 || ";{}".IndexOf(text[i - 1]) >= 0;
                if (!inQuotes && statementStart && string.CompareOrdinal(text, i, "var ", 0, 4) == 0)
                {
                    int nameStart = i + 4;
                    while (nameStart < text.Length && text[nameStart] == ' ')
                    {
                        nameStart++;
                    }
                    int nameEnd = nameStart;
                    while (nameEnd < text.Length && (char.IsLetterOrDigit(text[nameEnd]) || text[nameEnd] == '_'))
                    {
                        nameEnd++;
                    }
                    var name = text.Substring(nameStart, nameEnd - nameStart);
                    int eq = nameEnd;
                    while (eq < text.Length && text[eq] == ' ')
                    {
                        eq++;
                    }
                    int valueStart = eq + 1;
                    while (valueStart < text.Length && text[valueStart] == ' ')
                    {
                        valueStart++;
                    }
                    bool assigns = IsPlainName(name) && eq < text.Length && text[eq] == '=' &&
                                   !(eq + 1 < text.Length && text[eq + 1] == '=');
                    bool isEnum = assigns && string.CompareOrdinal(text, valueStart, "Enum", 0, 4) == 0 &&
                                  (valueStart + 4 >= text.Length || !char.IsLetterOrDigit(text[valueStart + 4]));
                    if (assigns && !isEnum && !m_declaredArgsMap.Keys.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) &&
                        !MentionsAny(text.Substring(0, i), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name }))
                    {
                        m_varLocals.Add(name);
                        i = nameStart - 1;      // drop "var ", keep the name and the rest
                        continue;
                    }
                }
                sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>
        /// "iff(c, a, b)" as "((c)?(a):(b))". The interpreter's iff evaluates the condition and
        /// then only the branch it picks, which is what a ternary does in both CSCS and C#, so
        /// the two mean the same; the ternary compiles and iff (a statement that needs the
        /// script around it) cannot. Anything but three arguments is left for the interpreter.
        /// </summary>
        static string RewriteIff(string code)
        {
            var sb = new StringBuilder(code.Length);
            bool inString = false;
            char quote = '\0';
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < code.Length) { sb.Append(code[++i]); }
                    else if (c == quote) { inString = false; }
                    continue;
                }
                if (c == '"' || c == '\'') { inString = true; quote = c; sb.Append(c); continue; }
                if ((c == 'i' || c == 'I') && (i == 0 || (!IsNameChar(code[i - 1]) && code[i - 1] != '.')) &&
                    string.Compare(code, i, "iff(", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    int close = MatchingParen(code, i + 3);
                    if (close > 0)
                    {
                        var args = SplitTopLevel(code.Substring(i + 4, close - i - 4), ',');
                        if (args.Count == 3 && args.All(a => !string.IsNullOrWhiteSpace(a)))
                        {
                            sb.Append("((").Append(RewriteIff(args[0])).Append(")?(")
                              .Append(RewriteIff(args[1])).Append("):(")
                              .Append(RewriteIff(args[2])).Append("))");
                            i = close;
                            continue;
                        }
                    }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Chained comparisons the way CSCS evaluates them. CSCS ranks the operators as C# does
        /// (&lt; &lt;= &gt; &gt;= above == !=) and applies each level left to right, but every
        /// comparison yields the number 1 or 0, which the next one compares -- so "1 &lt; n &lt; 10"
        /// is "(1&lt;n?1:0)&lt;10" and "1 &lt; n == 1" is "(1&lt;n?1:0)==1". Copied through, both are
        /// C#'s "bool against int" (CS0019) and fell back. Only a run of plain operands (names,
        /// numbers, members, subscripts, arithmetic) joined by two or more comparisons is
        /// rewritten; a parenthesised operand leaves the run as it was.
        /// </summary>
        static string RewriteChainedComparisons(string code)
        {
            var sb = new StringBuilder(code.Length);
            bool inString = false;
            char quote = '\0';
            int i = 0;
            while (i < code.Length)
            {
                char c = code[i];
                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < code.Length) { sb.Append(code[++i]); }
                    else if (c == quote) { inString = false; }
                    i++;
                    continue;
                }
                if (c == '"' || c == '\'') { inString = true; quote = c; sb.Append(c); i++; continue; }
                if (ComparisonRunStep(code, i) == 0)
                {
                    sb.Append(c);
                    i++;
                    continue;
                }
                int start = i;
                for (int step; i < code.Length && (step = ComparisonRunStep(code, i)) > 0; i += step) { }
                sb.Append(RewriteComparisonRun(code.Substring(start, i - start)));
            }
            return sb.ToString();
        }

        /// <summary>
        /// "(n > 2) * 10" as "(n>2?1:0)*10": a parenthesised comparison next to arithmetic is
        /// the number CSCS makes of it, 1 or 0, where C# has a bool (CS0019: bool * int). The
        /// ternary's condition is the same comparison, so it keeps every rule the translator
        /// already applies to one. Only a group holding exactly
        /// one comparison and nothing looser ("&&", "||", "?", ",", an assignment), not a call's
        /// parentheses, and with +, -, *, / or % (or a compound "+=" and the like) right beside it.
        /// </summary>
        static string RewriteComparisonsAsNumbers(string code)
        {
            var sb = new StringBuilder(code.Length);
            bool inString = false;
            char quote = '\0';
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < code.Length) { sb.Append(code[++i]); }
                    else if (c == quote) { inString = false; }
                    continue;
                }
                if (c == '"' || c == '\'') { inString = true; quote = c; sb.Append(c); continue; }
                if (c != '(')
                {
                    sb.Append(c);
                    continue;
                }
                int close = MatchingParen(code, i);
                int prev = i - 1;
                while (prev >= 0 && code[prev] == ' ') { prev--; }
                int next = close + 1;
                while (close > 0 && next < code.Length && code[next] == ' ') { next++; }
                bool isCall = prev >= 0 && (IsNameChar(code[prev]) || code[prev] == ']' || code[prev] == ')');
                if (isCall && IsNameChar(code[prev]))
                {
                    // Whitespace is stripped by now, so "return (n > 2) * 10" reads "return(n>2)*10":
                    // a keyword before the group is not a function being called.
                    int wordStart = prev;
                    while (wordStart > 0 && IsNameChar(code[wordStart - 1])) { wordStart--; }
                    var word = code.Substring(wordStart, prev - wordStart + 1);
                    if (word == Constants.RETURN || word == "throw")
                    {
                        isCall = false;
                    }
                }
                bool arithmeticBefore = prev >= 0 && ("+-*/%".IndexOf(code[prev]) >= 0 ||
                    (code[prev] == '=' && prev > 0 && "+-*/%".IndexOf(code[prev - 1]) >= 0));
                bool arithmeticAfter = close > 0 && next < code.Length && "+-*/%".IndexOf(code[next]) >= 0 &&
                    !(next + 1 < code.Length && code[next + 1] == '=');
                var inner = close > 0 ? code.Substring(i + 1, close - i - 1) : null;
                if (close < 0 || isCall || !(arithmeticBefore || arithmeticAfter) || !IsSingleComparison(inner))
                {
                    sb.Append(c);
                    continue;
                }
                sb.Append('(').Append(RewriteComparisonsAsNumbers(inner)).Append("?1:0)");
                i = close;
            }
            return sb.ToString();
        }

        /// <summary>Exactly one of == != &lt; &gt; &lt;= &gt;= at the top level, and no "&&", "||", "?",
        /// ",", "===", or plain "=".</summary>
        static bool IsSingleComparison(string text)
        {
            int depth = 0, comparisons = 0;
            bool inString = false;
            char quote = '\0';
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (c == '\\') { i++; }
                    else if (c == quote) { inString = false; }
                    continue;
                }
                if (c == '"' || c == '\'') { inString = true; quote = c; continue; }
                if (c == '(' || c == '[' || c == '{') { depth++; continue; }
                if (c == ')' || c == ']' || c == '}') { depth--; continue; }
                if (depth != 0)
                {
                    continue;
                }
                char next = i + 1 < text.Length ? text[i + 1] : '\0';
                char after = i + 2 < text.Length ? text[i + 2] : '\0';
                if (c == '?' || c == ',' || c == ':' || (c == '&' && next == '&') || (c == '|' && next == '|'))
                {
                    return false;
                }
                if ((c == '=' || c == '!') && next == '=')
                {
                    if (after == '=') { return false; }          // === and !==
                    comparisons++; i++; continue;
                }
                if (c == '=')
                {
                    return false;                               // an assignment
                }
                if (c == '<' || c == '>')
                {
                    if (next == c) { return false; }            // a shift
                    comparisons++;
                    if (next == '=') { i++; }
                }
            }
            // Not against null: in a ternary's condition "== null" is not routed through the
            // rule that compares it as the empty text (TryRewriteStringComparison), so C# tested
            // a reference and "(m[\"a\"] == null) * 10" answered 0 where the interpreter says 10.
            return comparisons == 1 &&
                   !System.Text.RegularExpressions.Regex.IsMatch(text, @"(?<![\w.])null(?![\w])");
        }

        /// <summary>
        /// "\"ab\" * 2" as "\"ab2\"": with text on either side, CSCS's "*" joins the two trimmed
        /// texts (Parser.MergeStrings), so a text literal times an integer literal is a constant --
        /// "ab2", or "2ab" the other way round. C# has no "*" on a string (CS0019), and inside
        /// "s += ..." the translator's own rewrite for it was not reached. Only a plain integer
        /// (whose text is its digits) and text without escapes, and only where no "*", "/", "%"
        /// or "**" beside the pair could regroup it.
        /// </summary>
        static string RewriteTextTimesNumber(string code)
        {
            var pattern = new System.Text.RegularExpressions.Regex(
                "(?<![*/%\\w.\\])\"])(?:\"([^\"\\\\]*)\"\\*(0|[1-9]\\d{0,8})|(0|[1-9]\\d{0,8})\\*\"([^\"\\\\]*)\")(?![*\\w.(\\[])");
            for (int round = 0; round < 8; round++)
            {
                var next = pattern.Replace(code, m => m.Groups[2].Success ?
                    "\"" + m.Groups[1].Value.Trim() + m.Groups[2].Value + "\"" :
                    "\"" + m.Groups[3].Value + m.Groups[4].Value.Trim() + "\"");
                if (next == code)
                {
                    break;
                }
                code = next;
            }
            return code;
        }

        static bool IsOperandChar(char c) =>
            IsNameChar(c) || c == '.' || c == '[' || c == ']' ||
            c == '+' || c == '-' || c == '*' || c == '/' || c == '%';

        /// <summary>How many characters at this position belong to a comparison run: an
        /// operand character, or a comparison operator; 0 ends the run. "===" (strict
        /// equality) and a lone "=" (assignment) are never part of one.</summary>
        static int ComparisonRunStep(string code, int i)
        {
            char c = code[i];
            char next = i + 1 < code.Length ? code[i + 1] : '\0';
            char third = i + 2 < code.Length ? code[i + 2] : '\0';
            if ((c == '<' || c == '>') && next == '=') { return 2; }
            if ((c == '=' || c == '!') && next == '=' && third != '=') { return 2; }
            if (c == '<' || c == '>' || IsOperandChar(c)) { return 1; }
            return 0;
        }

        static string RewriteComparisonRun(string run)
        {
            var operands = new List<string>();
            var operators = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < run.Length; i++)
            {
                char c = run[i];
                if (c == '[') { depth++; continue; }
                if (c == ']') { depth--; continue; }
                if (depth != 0)
                {
                    continue;
                }
                string op = null;
                char next = i + 1 < run.Length ? run[i + 1] : '\0';
                if ((c == '<' || c == '>') && next == '=') { op = c + "="; }
                else if ((c == '=' || c == '!') && next == '=') { op = c + "="; }
                else if (c == '<' || c == '>') { op = c.ToString(); }
                if (op == null)
                {
                    continue;
                }
                operands.Add(run.Substring(start, i - start));
                operators.Add(op);
                i += op.Length - 1;
                start = i + 1;
            }
            operands.Add(run.Substring(start));
            if (operators.Count < 2 || depth != 0 || operands.Any(string.IsNullOrEmpty))
            {
                return run;
            }

            // Split at == / != into groups of relational comparisons.
            var groups = new List<Tuple<List<string>, List<string>>>();
            var equalities = new List<string>();
            var current = Tuple.Create(new List<string> { operands[0] }, new List<string>());
            for (int k = 0; k < operators.Count; k++)
            {
                if (operators[k] == "==" || operators[k] == "!=")
                {
                    groups.Add(current);
                    equalities.Add(operators[k]);
                    current = Tuple.Create(new List<string> { operands[k + 1] }, new List<string>());
                }
                else
                {
                    current.Item1.Add(operands[k + 1]);
                    current.Item2.Add(operators[k]);
                }
            }
            groups.Add(current);

            if (equalities.Count == 0)
            {
                return RelationalChain(groups[0]);
            }
            var expr = AsNumber(groups[0]);
            for (int k = 0; k < equalities.Count; k++)
            {
                var step = expr + equalities[k] + AsNumber(groups[k + 1]);
                expr = k < equalities.Count - 1 ? "(" + step + "?1:0)" : step;
            }
            return expr;
        }

        /// <summary>A left-to-right chain of relational comparisons, as a C# bool.</summary>
        static string RelationalChain(Tuple<List<string>, List<string>> group)
        {
            var expr = group.Item1[0];
            var ops = group.Item2;
            for (int k = 0; k < ops.Count; k++)
            {
                var step = expr + ops[k] + group.Item1[k + 1];
                expr = k < ops.Count - 1 ? "(" + step + "?1:0)" : step;
            }
            return expr;
        }

        /// <summary>
        /// "case 1: { t = 5; break; }" as "case 1: t = 5; break;". A case body in braces is a
        /// block and nothing more -- a CSCS local belongs to the function, not the block -- but
        /// the switch builder groups a case's statements up to the next label and read the
        /// brace as a statement of its own. Only a block that runs up to the next label or to
        /// the end of the switch.
        /// </summary>
        static string RewriteCaseBlocks(string code)
        {
            var text = code ?? "";
            if (text.IndexOf(Constants.CASE, StringComparison.Ordinal) < 0 &&
                text.IndexOf(Constants.DEFAULT, StringComparison.Ordinal) < 0)
            {
                return text;
            }
            var sb = new StringBuilder(text);
            bool inString = false;
            char quote = '\0';
            for (int i = 0; i < sb.Length; i++)
            {
                char c = sb[i];
                if (inString)
                {
                    if (c == '\\') { i++; }
                    else if (c == quote) { inString = false; }
                    continue;
                }
                if (c == '"' || c == '\'') { inString = true; quote = c; continue; }
                if (c != '{')
                {
                    continue;
                }
                int k = i - 1;
                while (k >= 0 && char.IsWhiteSpace(sb[k])) { k--; }
                if (k < 0 || sb[k] != ':')
                {
                    continue;
                }
                int s = k - 1;
                while (s >= 0 && ";{}".IndexOf(sb[s]) < 0) { s--; }
                var head = sb.ToString(s + 1, k - s - 1).TrimStart();
                if (!StartsWithKeyword(head, Constants.CASE) && !StartsWithKeyword(head, Constants.DEFAULT))
                {
                    continue;
                }
                var current = sb.ToString();
                int close = MatchingBrace(current, i);
                if (close < 0)
                {
                    continue;
                }
                int after = close + 1;
                while (after < current.Length && char.IsWhiteSpace(current[after])) { after++; }
                var next = current.Substring(after);
                if (!(next.StartsWith("}") || StartsWithKeyword(next, Constants.CASE) ||
                      StartsWithKeyword(next, Constants.DEFAULT)))
                {
                    continue;
                }
                // The closing brace goes first, so the opening one's index still holds. The
                // body's own last statement keeps its ";"; one is added when it had none.
                var body = current.Substring(i + 1, close - i - 1).TrimEnd();
                sb.Remove(close, 1);
                if (body.Length > 0 && !body.EndsWith(";") && !body.EndsWith("}"))
                {
                    sb.Insert(close, ";");
                }
                sb[i] = ' ';
            }
            return sb.ToString();
        }

        /// <summary>
        /// A collection literal where the statement builders do not take one. The tokenizer cuts
        /// a statement at every brace, so "return {7, 8}" reached C# as a return followed by a
        /// block holding "7,8;", and "a = c ? {1} : {2}" as a statement ending at the "?". Both
        /// are rewritten into the shape that compiles -- an assignment whose whole value is the
        /// literal: "return {..}" as "__litN={..};return __litN", and the ternary as the if/else
        /// that evaluates only the branch it picks. A brace opens a literal after "=", "?", ":",
        /// "(", ",", "[" or "return"; any other brace is a block.
        /// </summary>
        static string RewriteBraceLiterals(string code)
        {
            var text = code ?? "";
            if (text.IndexOf('{') < 0)
            {
                return text;
            }
            var sb = new StringBuilder(text.Length + 32);
            int start = 0;
            int depth = 0;
            int temp = 0;
            bool inString = false;
            char quote = '\0';
            for (int i = 0; i <= text.Length; i++)
            {
                char c = i < text.Length ? text[i] : ';';
                if (inString)
                {
                    if (c == '\\') { i++; }
                    else if (c == quote) { inString = false; }
                    continue;
                }
                if (c == '"' || c == '\'') { inString = true; quote = c; continue; }
                if (c == '(' || c == '[') { depth++; continue; }
                if (c == ')' || c == ']') { depth--; continue; }
                if (c == '{' && OpensBraceLiteral(text, i))
                {
                    int close = MatchingBrace(text, i);
                    if (close > i)
                    {
                        i = close;
                        continue;
                    }
                }
                if (depth != 0 || (c != ';' && c != '{' && c != '}'))
                {
                    continue;
                }
                var statement = text.Substring(start, Math.Min(i, text.Length) - start);
                sb.Append(BraceLiteralRewritten(statement, ref temp) ?? statement);
                if (i < text.Length) { sb.Append(c); }
                start = i + 1;
            }
            return sb.ToString();
        }

        static bool OpensBraceLiteral(string text, int at)
        {
            int k = at - 1;
            while (k >= 0 && char.IsWhiteSpace(text[k])) { k--; }
            if (k < 0)
            {
                return false;
            }
            if (text[k] == ':')
            {
                // "case 1: {" and "default: {" open a block.
                int s = k - 1;
                while (s >= 0 && ";{}".IndexOf(text[s]) < 0) { s--; }
                var head = text.Substring(s + 1, k - s - 1).TrimStart();
                if (StartsWithKeyword(head, Constants.CASE) || StartsWithKeyword(head, Constants.DEFAULT))
                {
                    return false;
                }
            }
            if ("=?:(,[".IndexOf(text[k]) >= 0)
            {
                // Not "==" or the like: a comparison is never followed by a literal here, and
                // "=>" does not exist in CSCS, so any "=" is an assignment's.
                return true;
            }
            int end = k + 1;
            while (k >= 0 && (char.IsLetterOrDigit(text[k]) || text[k] == '_')) { k--; }
            var word = text.Substring(k + 1, end - k - 1);
            return word == Constants.RETURN && (k < 0 || !(char.IsLetterOrDigit(text[k]) || text[k] == '_' || text[k] == '.'));
        }

        static int MatchingBrace(string text, int open)
        {
            int depth = 0;
            bool inString = false;
            char quote = '\0';
            for (int i = open; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (c == '\\') { i++; }
                    else if (c == quote) { inString = false; }
                    continue;
                }
                if (c == '"' || c == '\'') { inString = true; quote = c; continue; }
                if (c == '{') { depth++; }
                else if (c == '}' && --depth == 0) { return i; }
            }
            return -1;
        }

        static string BraceLiteralRewritten(string statement, ref int temp)
        {
            var trimmed = statement.Trim();
            if (trimmed.IndexOf('{') < 0)
            {
                return null;
            }
            int lead = statement.Length - statement.TrimStart().Length;
            var indent = statement.Substring(0, lead);
            // "return {..}": the whole value is one literal.
            if (trimmed.StartsWith(Constants.RETURN, StringComparison.Ordinal))
            {
                var value = trimmed.Substring(Constants.RETURN.Length).Trim();
                if (value.StartsWith("{") && MatchingBrace(value, 0) == value.Length - 1)
                {
                    var name = "__lit" + (++temp);
                    return indent + name + "=" + value + ";" + Constants.RETURN + " " + name;
                }
                return null;
            }
            // "v = c ? {..} : x" and its mirror: an if/else, each branch a plain assignment.
            int eq = trimmed.IndexOf('=');
            if (eq <= 0 || (eq + 1 < trimmed.Length && trimmed[eq + 1] == '=') ||
                !IsPlainName(trimmed.Substring(0, eq).Trim()))
            {
                return null;
            }
            var target = trimmed.Substring(0, eq).Trim();
            var rhs = trimmed.Substring(eq + 1).Trim();
            int q = TopLevelIndexOutsideBraces(rhs, '?', 0);
            if (q <= 0)
            {
                return null;
            }
            int colon = TopLevelIndexOutsideBraces(rhs, ':', q + 1);
            if (colon < 0 || TopLevelIndexOutsideBraces(rhs, '?', q + 1) >= 0)
            {
                return null;
            }
            var condition = rhs.Substring(0, q).Trim();
            var whenTrue = rhs.Substring(q + 1, colon - q - 1).Trim();
            var whenFalse = rhs.Substring(colon + 1).Trim();
            bool trueLiteral = whenTrue.StartsWith("{") && MatchingBrace(whenTrue, 0) == whenTrue.Length - 1;
            bool falseLiteral = whenFalse.StartsWith("{") && MatchingBrace(whenFalse, 0) == whenFalse.Length - 1;
            if (!(trueLiteral || falseLiteral) || condition.Length == 0 || whenTrue.Length == 0 ||
                whenFalse.Length == 0 || condition.IndexOf('{') >= 0 ||
                (!trueLiteral && whenTrue.IndexOf('{') >= 0) || (!falseLiteral && whenFalse.IndexOf('{') >= 0))
            {
                return null;
            }
            return indent + "if(" + condition + "){" + target + "=" + whenTrue + ";}else{" +
                   target + "=" + whenFalse + ";}";
        }

        /// <summary>The first top-level <paramref name="ch"/> from <paramref name="from"/>, outside
        /// strings, parentheses, subscripts and braces; -1 when there is none.</summary>
        static int TopLevelIndexOutsideBraces(string text, char ch, int from)
        {
            int depth = 0;
            bool inString = false;
            char quote = '\0';
            for (int i = from; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (c == '\\') { i++; }
                    else if (c == quote) { inString = false; }
                    continue;
                }
                if (c == '"' || c == '\'') { inString = true; quote = c; continue; }
                if ("([{".IndexOf(c) >= 0) { depth++; continue; }
                if (")]}".IndexOf(c) >= 0) { depth--; continue; }
                if (depth == 0 && c == ch) { return i; }
            }
            return -1;
        }

        /// <summary>
        /// "p = c ? new A(..) : new B(..);" as "if(c){p=new A(..);}else{p=new B(..);}". The
        /// translator builds an instance only as the whole right-hand side of an assignment, and
        /// handed the ternary it passed "Point(1,2):new Point(3,4)" to the interpreter's "new"
        /// (CS1003). A ternary evaluates only the branch it picks, as the if/else does, so the
        /// two mean the same. Only a statement that is exactly such an assignment, with one
        /// top-level "?" and a branch starting with "new", is rewritten.
        /// </summary>
        static string RewriteTernaryNew(string code)
        {
            var sb = new StringBuilder(code.Length);
            int start = 0;
            bool inString = false;
            char quote = '\0';
            int depth = 0;
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                if (inString)
                {
                    if (c == '\\') { i++; }
                    else if (c == quote) { inString = false; }
                    continue;
                }
                if (c == '"' || c == '\'') { inString = true; quote = c; continue; }
                if (c == '(' || c == '[') { depth++; continue; }
                if (c == ')' || c == ']') { depth--; continue; }
                if (depth != 0 || (c != ';' && c != '{' && c != '}'))
                {
                    continue;
                }
                var statement = code.Substring(start, i - start);
                sb.Append(c == ';' ? TernaryNewAsIfElse(statement) ?? statement : statement);
                sb.Append(c);
                start = i + 1;
            }
            sb.Append(code.Substring(start));
            return sb.ToString();
        }

        /// <summary>
        /// "if((v=X)>5){" as "v=X;if((v)>5){". An if evaluates its condition once, and an
        /// assignment grouped as the condition's first operand runs before anything else in it,
        /// so the two mean the same -- and the second is an ordinary assignment, which the
        /// local-type analysis sees. Seen only inside the condition, a number assigned there
        /// and text assigned in the block ("cond_asg_conflict") declared the local a string
        /// (CS0029). Only a statement that starts with "if(" -- never "else if", whose
        /// condition may not run, nor "while", whose condition runs on every pass.
        /// </summary>
        static string RewriteIfAssignment(string code)
        {
            var sb = new StringBuilder(code.Length);
            int start = 0;
            bool inString = false;
            char quote = '\0';
            int depth = 0;
            for (int i = 0; i <= code.Length; i++)
            {
                char c = i < code.Length ? code[i] : ';';
                if (inString)
                {
                    if (c == '\\') { i++; }
                    else if (c == quote) { inString = false; }
                    continue;
                }
                if (c == '"' || c == '\'') { inString = true; quote = c; continue; }
                if (c == '(' || c == '[') { depth++; continue; }
                if (c == ')' || c == ']') { depth--; continue; }
                if (depth != 0 || (c != ';' && c != '{' && c != '}'))
                {
                    continue;
                }
                var statement = code.Substring(start, Math.Min(i, code.Length) - start);
                sb.Append(IfAssignmentHoisted(statement) ?? LeadingGroupsHoisted(statement) ?? statement);
                if (i < code.Length) { sb.Append(c); }
                start = i + 1;
            }
            return sb.ToString();
        }

        /// <summary>
        /// "p=new A(new B(1,2).Sum(),\"z\")" as "__newArg1=new B(1,2);p=new A(__newArg1.Sum(),\"z\")".
        /// The translator builds an instance only as the whole right-hand side of an
        /// assignment, so one built inside another's arguments was refused. Hoisting it is
        /// exact when it is the first thing the statement evaluates: only arguments that are
        /// literals or plain names may come before it, and the statement must not be a
        /// condition or a loop header (which may run it more than once, or not at all).
        /// </summary>
        static string RewriteNestedNew(string code)
        {
            var sb = new StringBuilder(code.Length);
            int start = 0, depth = 0, counter = 0;
            bool inString = false;
            char quote = '\0';
            for (int i = 0; i <= code.Length; i++)
            {
                char c = i < code.Length ? code[i] : ';';
                if (inString)
                {
                    if (c == '\\') { i++; }
                    else if (c == quote) { inString = false; }
                    continue;
                }
                if (c == '"' || c == '\'') { inString = true; quote = c; continue; }
                if (c == '(' || c == '[') { depth++; continue; }
                if (c == ')' || c == ']') { depth--; continue; }
                if (depth != 0 || (c != ';' && c != '{' && c != '}'))
                {
                    continue;
                }
                var statement = code.Substring(start, Math.Min(i, code.Length) - start);
                sb.Append(c == ';' || i == code.Length ? NestedNewHoisted(statement, ref counter) ?? statement : statement);
                if (i < code.Length) { sb.Append(c); }
                start = i + 1;
            }
            return sb.ToString();
        }

        static string NestedNewHoisted(string statement, ref int counter)
        {
            foreach (var keyword in new[] { "if(", "while(", "for(", "elif(", "else", "switch(", "case", "default" })
            {
                if (statement.StartsWith(keyword, StringComparison.Ordinal))
                {
                    return null;
                }
            }
            int outer = IndexOfNew(statement, 0);
            if (outer < 0)
            {
                return null;
            }
            int open = statement.IndexOf('(', outer);
            int close = open < 0 ? -1 : MatchingParen(statement, open);
            if (close < 0)
            {
                return null;
            }
            // Everything the statement evaluates before the outer constructor must be free of
            // calls: an assignment target, or "return".
            var before = statement.Substring(0, outer);
            if (before.IndexOf('(') >= 0)
            {
                return null;
            }
            var args = SplitTopLevel(statement.Substring(open + 1, close - open - 1), ',');
            int offset = open + 1;
            foreach (var arg in args)
            {
                int innerNew = arg.StartsWith("new ", StringComparison.Ordinal) ? 0 : -1;
                if (innerNew == 0)
                {
                    int innerOpen = arg.IndexOf('(');
                    int innerClose = innerOpen < 0 ? -1 : MatchingParen(arg, innerOpen);
                    if (innerClose < 0 || IndexOfNew(arg, 4) >= 0)
                    {
                        return null;
                    }
                    var temp = "__newArg" + (++counter);
                    return temp + "=" + arg.Substring(0, innerClose + 1) + ";" +
                           statement.Substring(0, offset) + temp + statement.Substring(offset + innerClose + 1);
                }
                // An argument ahead of it must not run anything: a literal or a plain name.
                if (!System.Text.RegularExpressions.Regex.IsMatch(arg, @"^(-?[\w.]+|""[^""\\]*"")$"))
                {
                    return null;
                }
                offset += arg.Length + 1;
            }
            return null;
        }

        /// <summary>Where "new " starts as a word, at or after the position; -1 if nowhere.</summary>
        static int IndexOfNew(string text, int from)
        {
            for (int at = text.IndexOf("new ", from, StringComparison.Ordinal); at >= 0;
                 at = text.IndexOf("new ", at + 1, StringComparison.Ordinal))
            {
                if (at == 0 || !IsNameChar(text[at - 1]))
                {
                    return at;
                }
            }
            return -1;
        }

        /// <summary>
        /// "return helper((q=n*2))+q" as "q=n*2;return helper(q)+q", and "return(b=n>2)+(c=n>1)+b+c"
        /// as "b=n>2;c=n>1;return b+c+b+c": a grouped assignment in a plain or return statement
        /// becomes a statement of its own when nothing before it can tell the difference. CSCS
        /// evaluates left to right, so it is exact when everything ahead of the group is free of
        /// effects and does not read the name: no ")" there (a completed call or group), no "?",
        /// "&&" or "||" (which may skip it), no "++" or "--", and no mention of the name.
        /// Inside a call's arguments the translator did not carry such an assignment out
        /// ("Math.Max((q = n * 2), 5) + q" answered 5, not 9, when q was declared for it), and
        /// two of them in one statement put a C# bool beside "+".
        /// </summary>
        static string LeadingGroupsHoisted(string statement)
        {
            var body = statement;
            foreach (var keyword in new[] { "if", "while", "for", "elif", "else", "switch", "case", "default",
                                            "catch", "throw", "do", "function", "class", "try", "finally" })
            {
                if (body.StartsWith(keyword, StringComparison.Ordinal) &&
                    (body.Length == keyword.Length || !IsNameChar(body[keyword.Length])))
                {
                    return null;
                }
            }
            var hoisted = new StringBuilder();
            while (true)
            {
                int at = -1;
                bool inString = false;
                char quote = '\0';
                for (int i = 0; i < body.Length && at < 0; i++)
                {
                    char c = body[i];
                    if (inString)
                    {
                        if (c == '\\') { i++; }
                        else if (c == quote) { inString = false; }
                        continue;
                    }
                    if (c == '"' || c == '\'') { inString = true; quote = c; continue; }
                    if (c == '(' && System.Text.RegularExpressions.Regex.IsMatch(
                            body.Substring(i + 1), @"^[A-Za-z_]\w*=(?!=)"))
                    {
                        at = i;
                    }
                }
                if (at < 0)
                {
                    break;
                }
                var before = body.Substring(0, at);
                var m = System.Text.RegularExpressions.Regex.Match(body.Substring(at + 1), @"^([A-Za-z_]\w*)=(?!=)");
                var name = m.Groups[1].Value;
                int close = MatchingParen(body, at);
                if (close < 0 || before.IndexOfAny(new[] { ')', '?' }) >= 0 || before.Contains("&&") ||
                    before.Contains("||") || before.Contains("++") || before.Contains("--") ||
                    System.Text.RegularExpressions.Regex.IsMatch(before, @"(?<![\w.])" + name + @"(?!\w)"))
                {
                    break;
                }
                var value = body.Substring(at + 1 + m.Length, close - at - 1 - m.Length);
                if (string.IsNullOrWhiteSpace(value))
                {
                    break;
                }
                hoisted.Append(name).Append('=').Append(value).Append(';');
                var gap = before.Length > 0 && IsNameChar(before[before.Length - 1]) ? " " : "";
                body = before + gap + name + body.Substring(close + 1);
            }
            return hoisted.Length == 0 ? null : hoisted + body;
        }

        static string IfAssignmentHoisted(string statement)
        {
            if (!statement.StartsWith("if((", StringComparison.Ordinal))
            {
                return null;
            }
            var m = System.Text.RegularExpressions.Regex.Match(statement, @"^if\(\(([A-Za-z_]\w*)=(?!=)");
            if (!m.Success)
            {
                return null;
            }
            int group = 3;                                  // the "(" of "(v=...)"
            int close = MatchingParen(statement, group);
            if (close < 0)
            {
                return null;
            }
            var name = m.Groups[1].Value;
            var value = statement.Substring(m.Length, close - m.Length);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            return name + "=" + value + ";if((" + name + ")" + statement.Substring(close + 1);
        }

        static string TernaryNewAsIfElse(string statement)
        {
            var m = System.Text.RegularExpressions.Regex.Match(statement, @"^([A-Za-z_]\w*)=(?!=)(.+)$");
            if (!m.Success)
            {
                return null;
            }
            var rhs = m.Groups[2].Value;
            int question = -1, colon = -1, depth = 0;
            bool inString = false;
            char quote = '\0';
            for (int i = 0; i < rhs.Length; i++)
            {
                char c = rhs[i];
                if (inString)
                {
                    if (c == '\\') { i++; }
                    else if (c == quote) { inString = false; }
                    continue;
                }
                if (c == '"' || c == '\'') { inString = true; quote = c; continue; }
                if (c == '(' || c == '[' || c == '{') { depth++; continue; }
                if (c == ')' || c == ']' || c == '}') { depth--; continue; }
                if (depth != 0) { continue; }
                if (c == '?')
                {
                    if (question >= 0) { return null; }   // a nested ternary: left alone
                    question = i;
                }
                else if (c == ':' && question >= 0)
                {
                    if (colon >= 0) { return null; }
                    colon = i;
                }
            }
            if (question <= 0 || colon <= question + 1 || colon >= rhs.Length - 1)
            {
                return null;
            }
            var condition = rhs.Substring(0, question);
            var whenTrue = rhs.Substring(question + 1, colon - question - 1);
            var whenFalse = rhs.Substring(colon + 1);
            if (!StartsWithKeyword(whenTrue, "new") && !StartsWithKeyword(whenFalse, "new"))
            {
                return null;
            }
            var name = m.Groups[1].Value;
            return "if(" + condition + "){" + name + "=" + whenTrue + ";}else{" + name + "=" + whenFalse + ";}";
        }

        /// <summary>A group as the number CSCS makes of it: a plain operand stays as it is, a
        /// comparison becomes 1 or 0.</summary>
        static string AsNumber(Tuple<List<string>, List<string>> group) =>
            group.Item2.Count == 0 ? group.Item1[0] : "(" + RelationalChain(group) + "?1:0)";

        /// <summary>An integer literal, a read-only int argument, or (as a bound only) a
        /// ".Size"/".Length"/".Count", optionally followed by "+ k" or "- k".</summary>
        static bool IsIntAtom(string text, HashSet<string> intArgs, bool isBound)
        {
            var m = System.Text.RegularExpressions.Regex.Match(text,
                @"^(-?\d{1,9}|[A-Za-z_]\w*(\.(Size|Length|Count))?)([+-]\d{1,6})?$");
            if (!m.Success)
            {
                return false;
            }
            // The start is emitted as the counter's first value, so it must already be an
            // int in C#: a literal or an int slot. Arithmetic on an int argument is widened to
            // double by the translator, and a member is not known to be an int.
            if (!isBound && (m.Groups[4].Success || m.Groups[2].Success))
            {
                return false;
            }
            var atom = m.Groups[1].Value;
            if (atom.Length > 0 && (char.IsDigit(atom[0]) || atom[0] == '-'))
            {
                var value = long.Parse(atom, CultureInfo.InvariantCulture);
                return value >= -1000000000 && value <= 1000000000;
            }
            return m.Groups[2].Success || intArgs.Contains(atom);
        }

        static string StripSpacesOutsideStrings(string code)
        {
            var sb = new StringBuilder(code.Length);
            bool inString = false;
            char quote = '\0';
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < code.Length) { sb.Append(code[++i]); }
                    else if (c == quote) { inString = false; }
                    continue;
                }
                if (c == '"' || c == '\'') { inString = true; quote = c; }
                if (!char.IsWhiteSpace(c)) { sb.Append(c); }
            }
            return sb.ToString();
        }

        static int MatchingParen(string text, int open)
        {
            int depth = 0;
            bool inString = false;
            char quote = '\0';
            for (int i = open; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (c == '\\') { i++; }
                    else if (c == quote) { inString = false; }
                    continue;
                }
                if (c == '"' || c == '\'') { inString = true; quote = c; }
                else if (c == '(') { depth++; }
                else if (c == ')' && --depth == 0) { return i; }
            }
            return -1;
        }

        static List<string> SplitTopLevelSemicolons(string header)
        {
            var parts = new List<string>();
            int depth = 0, start = 0;
            bool inString = false;
            char quote = '\0';
            for (int i = 0; i < header.Length; i++)
            {
                char c = header[i];
                if (inString)
                {
                    if (c == '\\') { i++; }
                    else if (c == quote) { inString = false; }
                    continue;
                }
                if (c == '"' || c == '\'') { inString = true; quote = c; }
                else if (c == '(' || c == '[' || c == '{') { depth++; }
                else if (c == ')' || c == ']' || c == '}') { depth--; }
                else if (c == ';' && depth == 0) { parts.Add(header.Substring(start, i - start)); start = i + 1; }
            }
            parts.Add(header.Substring(start));
            return parts;
        }

        static string LeadingName(string text)
        {
            int end = 0;
            while (end < text.Length && IsNameChar(text[end])) { end++; }
            return end > 0 ? text.Substring(0, end) : null;
        }

        /// <summary>The spans between ";", "{" and "}" outside string literals.</summary>
        static IEnumerable<Tuple<int, int>> Statements(string text)
        {
            int start = 0;
            bool inString = false;
            char quote = '\0';
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (c == '\\') { i++; }
                    else if (c == quote) { inString = false; }
                    continue;
                }
                if (c == '"' || c == '\'') { inString = true; quote = c; }
                else if (c == ';' || c == '{' || c == '}')
                {
                    if (i > start) { yield return Tuple.Create(start, i); }
                    start = i + 1;
                }
            }
            if (text.Length > start) { yield return Tuple.Create(start, text.Length); }
        }

        /// <summary>The statement with each string literal's contents replaced by '_', so no
        /// operator or name inside text is mistaken for code. Same length as the input.</summary>
        static string Shape(string statement)
        {
            var chars = statement.ToCharArray();
            bool inString = false;
            char quote = '\0';
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (inString)
                {
                    if (c == '\\' && i + 1 < chars.Length) { chars[i] = '_'; chars[++i] = '_'; continue; }
                    if (c == quote) { inString = false; continue; }
                    chars[i] = '_';
                    continue;
                }
                if (c == '"' || c == '\'') { inString = true; quote = c; }
            }
            return new string(chars);
        }

        /// <summary>The shape with everything inside [...] removed.</summary>
        static string TopLevel(string shape)
        {
            var sb = new StringBuilder(shape.Length);
            int depth = 0;
            foreach (var c in shape)
            {
                if (c == '[') { depth++; }
                else if (c == ']') { depth--; }
                else if (depth == 0) { sb.Append(c); }
            }
            return sb.ToString();
        }

        static int IndexDepth(string shape, int at)
        {
            int depth = 0;
            for (int i = 0; i < at; i++)
            {
                if (shape[i] == '[') { depth++; }
                else if (shape[i] == ']') { depth--; }
            }
            return depth;
        }

        /// <summary>Where a name occurs as a whole name -- not part of a longer one, and not a
        /// member of something else ("p.i").</summary>
        static IEnumerable<int> NameOccurrences(string shape, string name)
        {
            for (int at = shape.IndexOf(name, StringComparison.Ordinal); at >= 0;
                 at = shape.IndexOf(name, at + 1, StringComparison.Ordinal))
            {
                bool startOk = at == 0 || (!IsNameChar(shape[at - 1]) && shape[at - 1] != '.');
                int end = at + name.Length;
                bool endOk = end >= shape.Length || !IsNameChar(shape[end]);
                if (startOk && endOk)
                {
                    yield return at;
                }
            }
        }

        bool ProcessForStatement(string statement, List<string> tokens, ref string converted)
        {
            string functionName = GetFunctionName(statement, out string suffix, out bool isArray).Trim();
            if (functionName != Constants.FOR)
            {
                return false;
            }
            // "for (v in a)": one statement rather than three, so it never reaches the
            // three-part handling below and used to be copied through as-is.
            if (ProcessForEachStatement(suffix, ref converted))
            {
                return true;
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

            // A CSCS call in the initialiser runs once, before the loop, so its statements go
            // ahead of the whole thing. Collected here: this builder returns straight to
            // ProcessStatement and never reaches the token loop, which normally flushes them.
            var outerPrelude = m_statementPrelude;
            m_statementPrelude = "";

            int forParen = statement.IndexOf('(');
            var header = forParen < 0 ? "" : statement.Substring(forParen + 1);
            var hoistedHeader = HoistConditionCalls(header, header);

            converted = "";
            // "for (;;)" and "for (; i < n; i++)" leave the initialiser out, so there is no
            // counter to declare -- and nothing to register either. Declaring one anyway
            // emitted "double ;", which is not C#.
            bool hasCounter = !string.IsNullOrWhiteSpace(varName) && IsPlainName(varName);
            // Not forced for a counter that is also a global: the write-back this builder
            // emits sits inside the loop body, so the global ended at the last value the body
            // saw rather than the one the loop exited on -- 2 instead of 3 for "for (b = 0;
            // b < 3; b++)". Such a counter is published from the loop's condition instead
            // (IsGlobalCounter, CscsLate.Publish), which also runs on the way out.
            if (hasCounter && !m_newVariables.Contains(varName))
            {
                m_newVariables.Add(varName);
                // Declared without a value: the loop's own initialiser assigns it, which C#
                // counts as definite assignment. Repeating the initialiser here went wrong
                // whenever GetFunctionName cut it short -- at a call ("= helper") or at a
                // member, where "i = s.Length - 1" became "double i = s", a string.
                converted = m_depth + (m_intCounters.Contains(varName) ? "int " :
                                       m_variableLocals.Contains(varName) ? "Variable " : "double ") +
                            varName + ";\n";
            }
            converted += forParen < 0 ? m_depth + statement :
                m_depth + statement.Substring(0, forParen + 1) +
                    ReplaceArgsInString(hoistedHeader);
            converted += converted.EndsWith(";") ? "" : ";";

            converted = m_statementPrelude + converted;
            m_statementPrelude = outerPrelude;
            m_statementId += 2;
            // The condition and the step run on every pass, so a call in either stays inline.
            m_forceInlineCalls = true;
            try
            {
                // "for (;;)" leaves the condition out. The tokenizer drops the empty text
                // between the two semicolons, so the condition slot holds ";" itself -- and
                // ProcessSpecialCases answers that with "", while the line below then sees
                // "for(;" already ending in a semicolon and adds none. The step's ")" and the
                // "{" landed straight after it: "for(;) {", which is "Invalid expression
                // term '{'". An absent condition needs its own semicolon emitted here. An
                // absent *step* always worked, being last, and an absent initialiser too.
                var forCondition = m_statements[m_statementId];
                bool noCondition = string.IsNullOrWhiteSpace(forCondition) ||
                                   forCondition.Trim() == ";";
                var conditionCode = noCondition ? "" :
                    ProcessStatement(forCondition, m_statements[m_statementId + 1], false).Trim().TrimEnd(';').Trim();
                // A counter named like an existing global is that global to the interpreter,
                // which a loop inside a function writes: "for (i = 0; i < n; i++)" left a global i
                // at 4. Published from the condition, which runs on every pass and once more on
                // the way out, so the global ends on the value the loop exited with.
                if (hasCounter && IsGlobalCounter(varName))
                {
                    conditionCode = "CscsLate.Publish(__interpreter, \"" + varName + "\", " + varName + ")" +
                        (conditionCode.Length == 0 ? "" : " && (" + conditionCode + ")");
                }
                converted += conditionCode.Length == 0 ? ";" : conditionCode + ";";
                // One statement along, not two, when the condition was left out: the
                // tokenizer drops the empty text between the two semicolons, so "for (;;)"
                // arrives as "for (" ";" ";" ")" "{" -- a slot shorter than a full header.
                // Advancing by two then read the block's "{" as the step and skipped the
                // ")" altogether, emitting "for(;;{ {": "CS1026: ) expected".
                m_statementId += noCondition ? 1 : 2;
                converted += ProcessStatement(m_statements[m_statementId], m_statements[m_statementId + 1], false).Trim() + " {\n";
            }
            finally
            {
                m_forceInlineCalls = false;
            }
            m_statementId++;

            m_depth += "  ";
            if (hasCounter)
            {
                converted += RegisterVariableString(varName, "", false /* never a global */);
            }

            return true;
        }
        /// <summary>
        /// Translates "for (v in collection)" into an indexed loop, or returns false when the
        /// header is not that form. Indexed rather than a C# foreach over Tuple: the loop runs
        /// over anything a script can iterate, and Variable's indexer already covers those.
        /// The loop variable holds an element, so it is a Variable -- CollectVariableLocals
        /// widens whatever it feeds for the same reason.
        /// </summary>
        bool ProcessForEachStatement(string header, ref string converted)
        {
            var inner = (header ?? "").Trim();
            if (!inner.StartsWith("(") || !inner.EndsWith(")"))
            {
                return false;
            }
            inner = inner.Substring(1, inner.Length - 2);

            var parts = SplitTopLevelOn(inner, " in ");
            if (parts.Count != 2)
            {
                return false;
            }
            var varName = parts[0].Trim();
            var collection = parts[1].Trim();
            if (!IsPlainName(varName) || string.IsNullOrWhiteSpace(collection))
            {
                return false;
            }

            var indexName = "__feIndex" + (++m_tempVarId);
            // A source that calls something -- "for (w in s.Split(\" \"))", "for (q in
            // build())" -- is assigned to a temporary first. That sends it through the whole
            // pipeline, which is what knows how to build a call; the resolver alone emitted
            // the call's text verbatim and the argument names in it were never resolved.
            // Only a call: a subscript or a member reads perfectly well where it stands, and
            // routing those through a temporary lost them.
            var prefix = "";
            if (!IsPlainName(collection) && collection.IndexOf('(') > 0)
            {
                var sourceName = "__feSrc" + m_tempVarId;
                var outerPrelude = m_statementPrelude;
                m_statementPrelude = "";
                var built = ProcessStatement(sourceName + "=" + collection, ";", false);
                prefix = m_statementPrelude + built;
                m_statementPrelude = outerPrelude;
                m_newVariables.Add(sourceName);
                m_variableLocals.Add(sourceName);
                collection = sourceName;
            }
            var source = ReplaceArgsInString(collection);
            // A string is walked one character at a time, each as a string of its own, which
            // is what the interpreter yields. It reaches C# as a string rather than a
            // Variable, so it has Length rather than Size and its indexer gives a char.
            bool overString = IsStringOperand(collection);
            // Anything else is asked what it holds at run time: a collection walks its
            // elements, a string its characters, a scalar itself once. Reading .Size straight
            // off it went wrong for a string -- Size is 0 for one -- so "for (ch in row)" over
            // a line of text ran no passes at all.
            var itemsName = "__feItems" + m_tempVarId;
            if (!overString)
            {
                converted = prefix + m_depth + "var " + itemsName + " = CscsConvert.AsItems(" +
                    source + ");\n";
                source = itemsName;
            }
            else
            {
                converted = prefix;
            }
            converted += m_depth + "for (int " + indexName + " = 0; " + indexName + " < " +
                source + (overString ? ".Length; " : ".Size; ") + indexName + "++) {\n";
            m_depth += "  ";
            // Always declared: a script that used the name for something else before the loop
            // -- "k = \"z\"; ... for (k in keys)" -- then does not compile, and falls back.
            // Skipping the declaration when the name is already known was tried, but a name
            // first declared inside another block is gone by the time this one runs.
            // Except where the name is a local already in scope, which CSCS rebinds: the loop
            // leaves it holding the last element, as the interpreter does. C# refused a second
            // declaration of it (CS0136).
            bool rebind = m_newVariables.Contains(varName) && m_topLevelAssigned.Contains(varName) &&
                          m_variableLocals.Contains(varName);
            converted += m_depth + (rebind ? "" : "var ") + varName + " = " +
                (overString ? "Variable.ConvertToVariable(" + source + "[" + indexName + "].ToString())"
                            : source + "[" + indexName + "]") + ";\n";
            m_newVariables.Add(varName);
            m_variableLocals.Add(varName);
            converted += RegisterVariableString(varName, varName, false /* never a global */);
            m_statementId++;
            return true;
        }

        // The catch variables whose blocks are still open, with the depth their block is at.
        readonly List<KeyValuePair<string, int>> m_openCatches = new List<KeyValuePair<string, int>>();

        string ProcessCatch(string exceptionVar)
        {
            string varName = GetFunctionName(exceptionVar.Substring(1), out string suffix, out bool isArray);
            // A catch inside the block of another catching into the same name -- "catch (exc) {
            // ... try { ... } catch (exc) {} }" -- rebinds it, as the interpreter does; a second
            // declaration in the nested scope is CS0136. Closed blocks are those deeper than here.
            m_openCatches.RemoveAll(open => open.Value > m_depth.Length);
            bool enclosingCatch = m_openCatches.Any(open => open.Key == varName);

            // The "{" is emitted here rather than arriving as its own statement, so the
            // depth has to be increased here too -- the matching "}" decreases it either way.
            // Without this every catch block lost two characters of indent, and a try nested
            // inside a try drove it below zero: "Mismatch of { } parentheses".
            m_depth += "  ";

            // The exception is caught under a private name and the script's variable is bound
            // to its Message, because that is what the interpreter binds it to. Leaving the
            // script's name on the Exception itself meant any use of it other than returning
            // it -- "throw \"outer:\" + e" -- concatenated
            // "System.ArgumentException: boom" and a stack trace instead of "boom".
            var caught = "__exc" + (++m_tempVarId);
            string result = "catch(Exception " + caught + ") {\n";
            // A local of the same name already in scope is rebound, as the interpreter does.
            bool rebind = enclosingCatch || (m_newVariables.Contains(varName) && m_topLevelAssigned.Contains(varName) &&
                          m_variableLocals.Contains(varName));
            m_openCatches.Add(new KeyValuePair<string, int>(varName, m_depth.Length));
            // Its Message is also its property "Message", as the interpreter's is (CscsConvert.Caught).
            result += m_depth + (rebind ? "" : "var ") + varName + " = CscsConvert.Caught(" + caught + ");\n";
            m_newVariables.Add(varName);
            result += RegisterVariableString(varName, varName, false /* never a global */);
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

            extra += m_depth + "if (" + BOOL_TEMP_VAR + ") " + "__interpreter.AddCompiledLocalVariable(\"" + arrayName +
                     "\", new GetVarFunction(Variable.ConvertToVariable(__varTempVar)));\n"; ;
            extra += m_depth + "else __interpreter.AddCompiledLocalVariable(\"" + arrayName +
                     "\", new GetVarFunction(Variable.ConvertToVariable(" + arrayName + ")));\n";
            return result;
        }

        string EvaluateToken(string token)
        {
            // A conversion's argument arrives with its quotes escaped -- string(m["a"]) hands
            // over m[\"a\"] -- and the odd-backslash branch of the resolver then turned the
            // key into '\''a, which is not C#. printc unescapes for the same reason. The
            // other caller passes an array index, which has no quotes to unescape.
            token = token.Replace("\\\"", "\"");
            // One whole string literal is already C#: nothing in it is a name. SplitToken
            // pays no attention to quotes, so a format such as "yyyy/MM/dd" came apart on
            // the slashes and its middle piece was resolved as a variable -- the literal
            // went out as "yyyy/__varTempVar1/dd", which .NET then formatted as a date,
            // turning the "m" in the temp's name into the minutes of the value.
            if (IsWholeStringLiteral(token.Trim()))
            {
                return token.Trim();
            }
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
                // Assigning to a name the interpreter already holds has to reach it, as it
                // does when the function is interpreted: "g = \"set\"" in a function that
                // touches nothing else of the interpreter's left the variable at its old
                // value, since the write-back is only emitted when something else in the
                // body needs the interpreter. Asking for it here turns that pass on.
                if (IsInterpreterVariable(functionName))
                {
                    m_usesInterpreter = true;
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
            // "r += f(x)" appends to r, so r's type decides, not the function's return type:
            // in a cfunction declared without one, a string result was read with AsDouble()
            // and "r" collected "000" where the interpreter builds "sss".
            var joined = string.Join("", tokens);
            int plusAssign = joined.IndexOf("+=", StringComparison.Ordinal);
            if (desTokenId < 0 && plusAssign > 0 && IsStringName(joined.Substring(0, plusAssign)))
            {
                return "string";
            }
            if (desTokenId < 0)
            {
                // No assignment in the statement, so the value is being returned, and the
                // function's declared return type says what it has to be. Assuming "double"
                // here turned a callback that yields a string into AsDouble() -- that is, 0 --
                // so "return helper(n) + helper(n);" in a function declared string quietly
                // produced 0 instead of "xx". A wrong answer, not a failure to compile.
                return m_returnType == Variable.VarType.STRING ? "string" : "double";
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
            // A member of an enum declared in this function, reached through the token loop --
            // "return Local.Type", "\"v=\" + Local.Y". Sent through ReplaceArgsInString so
            // ResolveToken's rule applies: a declared member, or nothing. Left to the branches
            // below, the local counted as a Variable and ".Type" became C#'s Variable.Type, which
            // answers ENUM where the interpreter answers NONE.
            int enumDot = token.IndexOf('.');
            if (enumDot > 0 && IsPlainName(token.Substring(0, enumDot)) &&
                m_enumLocals.ContainsKey(token.Substring(0, enumDot)))
            {
                result += ReplaceArgsInString(token);
                return;
            }

            // "s.Substring(n - 1, n + 1)" beside a string literal comes through the token loop as
            // "if(s.Substring(n", "-", "1,n", "+", "1)": the statement tokenizer splits on operators
            // but not on ','. The middle token has no "(" of its own, so ProcessFunction built it
            // as an interpreter call named "1,n" and the two arguments collapsed into one. A
            // token made only of plain operands either side of a top-level comma is ordinary
            // expression text, and ReplaceArgsInString emits it as such. A quote, a brace or a
            // call in it keeps the old path.
            if (IndexOfTopLevelChar(token, ',') >= 0 && token.IndexOfAny(new[] { '"', '{', '}', '[' }) < 0 &&
                SplitTopLevel(token, ',').All(part =>
                {
                    var piece = part.Trim().Trim('(', ')').Trim();
                    return IsNumber(piece) ||
                           (IsPlainName(piece) && (m_paramMap.ContainsKey(piece) || m_newVariables.Contains(piece)));
                }))
            {
                result += ReplaceArgsInString(token);
                return;
            }

            string functionName = GetFunctionName(token, out string suffix, out bool isArray);
            if (string.IsNullOrEmpty(functionName))
            {
                // A token starting with '(' -- "(a" in "b + (a + c)" -- has nothing before the
                // paren, so there is no name to look up and the token used to be copied
                // through verbatim with its argument name unresolved. Emit the grouping
                // parens and resolve the rest.
                int open = 0;
                while (open < suffix.Length && suffix[open] == '(') { open++; }
                if (open > 0 && open < suffix.Length)
                {
                    // Unless what the group starts with is a call to a script function:
                    // "(helper(n))" needs an interpreter callback, and the expression path
                    // copies the name through as a C# method that does not exist. The rest
                    // goes back through here as a token of its own, name first.
                    var inner = suffix.Substring(open);
                    if ((char.IsLetter(inner[0]) || inner[0] == '_') && MentionsScriptCall(inner) &&
                        GetFunctionName(inner, out _, out _) is string innerName &&
                        inner.StartsWith(innerName + "(") && MentionsScriptCall(innerName + "()"))
                    {
                        result += suffix.Substring(0, open);
                        tokens[id] = inner;
                        ProcessToken(tokens, ref id, ref result, ref newVarAdded);
                        return;
                    }
                    result += suffix.Substring(0, open) + ReplaceArgsInString(inner);
                    return;
                }
                result += suffix;
                return;
            }


            // "!b" as the second operand of "&&": the tokenizer splits on "&&", so the "!"
            // arrives glued to the name and the whole token was looked up as a function,
            // becoming an interpreter callback that yields a Variable -- which "&&" cannot
            // combine with a bool.
            if (functionName.Length > 1 && functionName[0] == '!' && functionName[1] != '=')
            {
                result += "!" + ReplaceArgsInString(functionName.Substring(1) + suffix);
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
                    // An "if" evaluates its condition exactly once, so a call in it can be
                    // emitted ahead of the statement. A "while" is left alone: moving the
                    // call out would run it once instead of on every iteration.
                    var conditionText = functionName == Constants.IF ?
                        HoistConditionCalls(suffix, suffix) : suffix;
                    token = functionName + AsCondition(ReplaceArgsInString(conditionText));
                }

                if (token == "new")
                {
                    var argsStr = "";
                    for (int i = id + 1; i < tokens.Count; i++)
                    {
                        argsStr += tokens[i].Trim().Replace("\"", "\\\"");
                    }
                    // The action variable has to be set before the call: it is passed by
                    // reference into ParserFunction, and every other callback site initialises
                    // it. Leaving it null threw a NullReferenceException from inside the call
                    // rather than failing to compile.
                    string tempFunc = m_depth + ACTION_TEMP_VAR + " =\"\";\n" +
                                      GetCSCSFunction(argsStr, token, '(',
                                          id > 0 ? GetFunctionName(tokens[0], out _, out _).Trim() : null);
                    id = tokens.Count - 1;
                    result = tempFunc + result + " " + VARIABLE_TEMP_VAR + ";";
                }
                else
                {
                    // "else if (...)" tokenizes as "else", " ", "if(..." and the lone space is
                    // dropped as whitespace, so the two keywords came out glued: "elseif(...)",
                    // "The name 'elseif' does not exist". Every elif whose condition is not a
                    // known expression -- a string, a bool local -- took this path and fell back.
                    result += token == "else" ? token + " " : token;
                }
                return;
            }
            // '~' is a prefix rather than one of the interpreter's actions, so it is not in
            // ACTIONS -- without this it was treated as a name and turned into an interpreter
            // callback, leaving its operand stranded next to the callback's result.
            if (Array.IndexOf(Constants.ACTIONS, token) >= 0 ||
                token.Trim() == Constants.BITWISE_NOT)
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
                // The token can carry the parentheses that close the statement around it --
                // "t.Lower)" is the last token of "if (t != t.Lower)" -- and "Lower)" is no
                // member at all, so the token went out verbatim. They are put back after.
                var memberText = SplitClosingParens(suffix.StartsWith(".") ? suffix.Substring(1) : "",
                                                    out string closing);
                if (!isArray && suffix.StartsWith(".") &&
                    MapStringMember(functionName, memberText, ref mappedMember))
                {
                    result += mappedMember + closing;
                    return;
                }

                // A member on a local that holds a Variable: a member of that name if the
                // Variable has one, otherwise the interpreter's property lookup, which is
                // where a class instance keeps its fields. Without this the token went out
                // verbatim -- "p.x" on something that has no x.
                if (!isArray && suffix.StartsWith(".") && m_variableLocals.Contains(functionName))
                {
                    var localMember = suffix.Substring(1);
                    if (IsVariableMember(localMember))
                    {
                        result += functionName + "." + CanonicalVariableMember(localMember);
                        return;
                    }
                    // The field may itself be followed by a member -- "p.name.Upper" -- and
                    // the property read yields a Variable, so that member can follow it.
                    var localChain = BuildMemberChain(functionName, localMember);
                    if (localChain != null)
                    {
                        result += localChain;
                        return;
                    }
                }

                // An index expression is ordinary code and needs resolving. Emitting the
                // whole token verbatim left argument names undeclared, so "a[n]" referred to
                // an "n" that does not exist in the generated method.
                // A local in a statement holding a string literal reaches C# through here,
                // and two kinds of local cannot go out verbatim.
                //
                // ".Type" on a primitive: the interpreter answers with the CSCS type's name,
                // and a C# double, string or bool has no such member -- "\"t=\" + x.Type"
                // did not compile. A collection local is a Variable and already worked.
                //
                // A truth value: CSCS renders it 1 or 0, C# renders a bool "True"/"False", so
                // "\"v=\" + b" quietly produced "v=True" where the interpreter says "v=1" --
                // a wrong answer rather than a failure to compile. Only these two: every other
                // local ("v", "t", an element, a field) is correct as it stands, and this line
                // serves all of them.
                if (!isArray)
                {
                    if (suffix.StartsWith(".") &&
                        TryMapTypeMember(functionName, suffix.Substring(1), out var typeInText))
                    {
                        result += typeInText;
                        return;
                    }
                    // A truth value joined to text: CSCS renders it 1 or 0 while C# renders a
                    // bool "True"/"False", so "\"v=\" + b" answered "v=True" where the
                    // interpreter says "v=1" -- a wrong answer, not a failure to compile.
                    //
                    // Only with a "+" actually beside it. Forcing the conversion for every
                    // bool local here was tried and cost four constructs that assign or test
                    // one instead of joining it -- "ok = n > 2", "found = true", "c = b",
                    // "return a && b && c" -- where C# needs the bool itself.
                    if (string.IsNullOrEmpty(suffix) && m_statementHasString &&
                        m_localTypes.TryGetValue(functionName, out var boolType) &&
                        boolType == "bool" &&
                        ((id > 0 && tokens[id - 1].Trim() == "+") ||
                         (id + 1 < tokens.Count && tokens[id + 1].Trim() == "+")))
                    {
                        result += "(" + functionName + "?1:0)";
                        return;
                    }
                }
                // "a[1].Sum()": a method on the element. The expression path builds these --
                // "return a[0].Sum() + a[1].Sum();" compiled -- but only when it is handed the
                // whole thing; splitting the receiver off and converting "[1].Sum()" on its own
                // left the call as C# ".Sum()" on a Variable (CS1929). A lone call has no operator
                // to make the statement a known expression, so it arrives here instead.
                if (isArray && ElementMethodCallFollows(suffix))
                {
                    // The element branch in the expression builder only runs for a known
                    // expression, which is what "a[0].Sum() + a[1].Sum()" is and a lone call is
                    // not -- that one difference is why the pair compiled and the single did not.
                    // Set for this conversion only: the element is certainly a Variable here, so
                    // the branch's own assumption holds.
                    var knownOuter = m_knownExpression;
                    m_knownExpression = true;
                    result += ReplaceArgsInString(token);
                    m_knownExpression = knownOuter;
                    return;
                }
                result += isArray && !string.IsNullOrEmpty(suffix) ?
                    functionName + ReplaceArgsInString(suffix) : token;
                return;
            }

            // A member on a string argument -- "s.EndsWith(...)" as the second operand of
            // "&&". The argument branch further down only fires when the suffix has no '.',
            // so this used to fall through to an interpreter callback yielding a Variable,
            // which "&&" cannot combine. ReplaceArgsInString resolves the receiver and copies
            // the member name through; one with no C# equivalent fails to compile and falls
            // back rather than answering differently.
            int memberDot = functionName.IndexOf('.');
            if (memberDot > 0)
            {
                var memberOwner = functionName.Substring(0, memberDot);
                var memberName = functionName.Substring(memberDot + 1);
                bool stringMember = m_argsMap.TryGetValue(memberOwner, out var argVariable) &&
                                    argVariable.Type == Variable.VarType.STRING &&
                                    IsMappedStringMember(memberName);
                if (stringMember || IsCompareMarker(memberName) ||
                    (m_newVariables.Contains(memberOwner) && IsVariableMember(memberName)))
                {
                    result += ReplaceArgsInString(token);
                    return;
                }
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

            if (!suffix.Contains('.') && !suffix.TrimStart().StartsWith("(") && m_argsMap.TryGetValue(functionName, out _))
            {
                string actualName = m_paramMap[functionName];
                // The widening the expression path gives an int argument next to arithmetic
                // (see AsDoubleNextToDivision), for a statement that comes this way instead:
                // "\"v\" + n * n" wrapped around just as "n * n" did.
                var before = id > 0 ? tokens[id - 1].Trim() : "";
                var after = id + 1 < tokens.Count ? tokens[id + 1].Trim() : "";
                if (string.IsNullOrEmpty(suffix) &&
                    actualName.StartsWith(INT_VAR_ARG, StringComparison.InvariantCulture) &&
                    (s_widening.Contains(before) || s_widening.Contains(after)))
                {
                    actualName = "(double)(" + actualName + ")";
                }
                token = " " + actualName + ReplaceArgsInString(suffix);
                result += token;
                return;
            }
            ProcessFunction(tokens, ref id, ref result, ref newVarAdded);
        }

        string ResolveToken(string token, out bool resolved, string arguments = "",
                            bool isCall = false, bool insideCall = false, bool allowLate = false)
        {
            resolved = true;
            if (IsString(token) || IsNumber(token))
            {
                return token;
            }

            // The Contains built-in inside "&&" or "||", where a call has to stay in place --
            // "if (Contains(values, field) && values[field] != null)": it tests whether the
            // variable its first argument names has that index or key (ContainsFunction,
            // Variable.Exists), which is not the member "values.Contains(field)". Only with a
            // plain name first; the call route takes anything else, outside such a statement.
            if (isCall && m_statementInlineCalls && string.Equals(token.Trim(), "Contains", StringComparison.OrdinalIgnoreCase) &&
                m_parentScript?.InterpreterInstance?.GetFunction("Contains") is ContainsFunction)
            {
                var firstArg = SplitTopLevel(arguments.Substring(0, Math.Max(0, FindMatchingParen("(" + arguments, 0) - 1)), ',');
                if (firstArg.Count == 2 && IsPlainName(firstArg[0].Trim()))
                {
                    m_usesInterpreter = true;
                    return "CscsCalls.ContainsIn";
                }
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

            if (ProcessArray(token, ref replacement, isCall))
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

            // Not when the name is called: a call is looked up among functions only, whatever an
            // argument or local of the same name holds -- "ShowView(v, showView)", even "g(1)"
            // with an argument g, calls the function g interpreted.
            if (!isCall && m_paramMap.TryGetValue(token, out replacement))
            {
                return replacement + arrayArg;
            }

            // "s.Length" arriving as one token: '.' is not a token separator, so neither half
            // resolves on its own and the whole thing was emitted verbatim, leaving the
            // argument name undeclared. Numeric conditions never reach here -- this is the
            // path a string-typed condition like "if (s.Length > 2)" takes.
            int dot = token.IndexOf('.');
            if (dot > 0 && string.IsNullOrEmpty(arrayArg))
            {
                var owner = token.Substring(0, dot);
                var member = token.Substring(dot + 1);
                // The marker the comparison rewrite emits. Its receiver may be a number as
                // well as a string -- "n == \"5\"" -- so it is not covered by the string-member
                // mapping below.
                // A local that holds a Variable has the members the interpreter exposes under
                // those names, so the member is copied through -- mapping it to the C# string
                // member instead produced "v.ToUpper()", which a Variable does not have.
                // An argument declared "variable" first: "t = s.Substring(1, 5)" went out with the
                // bare name, which only the callback path (a return) declares. First, because an
                // argument the body assigns a Variable to -- "v = v.Substring(1)" -- is also
                // recorded as holding one, and the local branch below then wrote "v.StartsWith"
                // with the bare name (CS0103): an argument is never a C# local.
                if (IsVariableMember(member) && IsVariableParam(owner, out var variableSlot))
                {
                    return variableSlot + "." + CanonicalVariableMember(member);
                }
                if (IsVariableMember(member) &&
                    (m_variableLocals.Contains(owner) || m_collectionLocals.Contains(owner)))
                {
                    return owner + "." + CanonicalVariableMember(member);
                }

                // A field on a class instance. The instance is a Variable, which has no member
                // named after the script's field, so it is read through the same property
                // lookup the interpreter uses. Fields only: a method needs a ParsingScript to
                // run, so those keep falling back.
                // Not when a call follows: "p.Sum()" would become
                // "p.GetProperty(\"Sum\")()", which is not a method call. It keeps going to
                // the interpreter callback instead, which can run the method.
                if (m_variableLocals.Contains(owner) && !isCall)
                {
                    // The field may itself be followed by a member -- "p.name.Upper". The
                    // property read yields a Variable, so whatever Variable answers to can
                    // simply follow it.
                    var chain = BuildMemberChain(owner, member);
                    if (chain != null)
                    {
                        return chain;
                    }
                }

                // The same for an argument declared "variable", which is a Variable as much as
                // a local holding one: "f(variable v) { return v.x + v.y; }" left v unresolved,
                // though "v.Sum()" -- a method, which goes to the interpreter -- worked.
                if (!isCall && m_paramMap.TryGetValue(owner, out var ownerParam) &&
                    !string.IsNullOrEmpty(ownerParam) &&
                    m_argsMap.TryGetValue(owner, out var ownerArg) &&
                    ownerArg.Type == Variable.VarType.VARIABLE)
                {
                    var paramChain = BuildMemberChain(ownerParam, member);
                    if (paramChain != null)
                    {
                        return paramChain;
                    }
                }

                // A member of an enum the interpreter holds -- "Colors.Green". The member is
                // not one of Variable's, so the branch below does not fire and the name went
                // out verbatim ("The name 'Colors' does not exist"). Checked first, and only
                // for a global that really holds an enum: the runtime helper answers exactly
                // what the interpreter does.
                // A member of an enum declared in this function, read through the interpreter's own
                // enum lookup (CscsEnums.Member, Variable.GetEnumProperty): a declared name, the
                // enum's ".Type" (ENUM), or a word after a declared name -- "Local.Y.Type" is
                // NUMBER, "Local.Y.Name" the name. Anything else stays with the interpreter.
                if (m_enumLocals.TryGetValue(owner, out var enumMembers))
                {
                    var memberParts = member.Split('.');
                    bool known = memberParts.All(IsPlainName) && memberParts.Length <= 2 &&
                        (enumMembers.Contains(memberParts[0]) ||
                         (memberParts.Length == 1 && member.Equals(Constants.OBJECT_TYPE, StringComparison.OrdinalIgnoreCase)));
                    if (!known)
                    {
                        throw new ArgumentException("Not a declared member of the local enum: " + token);
                    }
                    return "CscsEnums.Member(__interpreter, " + owner + ", \"" + member + "\")";
                }

                if (IsEnumGlobal(owner) && IsPlainName(member))
                {
                    m_usesInterpreter = true;
                    return "CscsEnums.Member(__interpreter, \"" + owner + "\", \"" + member + "\")";
                }

                // A member of a name nothing defines yet -- "rows.Size" with rows a global the
                // script assigns later -- is read on the value found when the code runs.
                if (allowLate && IsVariableMember(member) && !isCall && IsLateBoundName(owner, false))
                {
                    m_usesInterpreter = true;
                    return "CscsLate.Value(__interpreter, \"" + owner + "\")." + CanonicalVariableMember(member);
                }
                // Called -- "if (!selVolas.Contains(e))" -- on the interpreter's own Variable, as
                // for a global known at translation (below), so "g.Add(x)" changes the global.
                if (allowLate && IsVariableMember(member) && isCall && IsLateBoundName(owner, false))
                {
                    m_usesInterpreter = true;
                    return "CscsLate.Current(__interpreter, \"" + owner + "\")." + CanonicalVariableMember(member);
                }

                // A member on a global: the read yields a Variable, which has the members
                // the interpreter exposes under those names.
                if (IsVariableMember(member) && IsInterpreterVariable(owner))
                {
                    m_usesInterpreter = true;
                    return "__interpreter.GetVariableValue(\"" + owner + "\")." + CanonicalVariableMember(member);
                }

                // ".Type" where the owner is a primitive: the interpreter names the CSCS
                // type, and C# has no such member on a double, a string or a bool. This is
                // the expression path -- "\"t=\" + x.Type" and "if (x.Type == \"NUMBER\")".
                if (TryMapTypeMember(owner, member, out var typeMapped))
                {
                    return typeMapped;
                }

                if (IsCompareMarker(member))
                {
                    if (m_paramMap.TryGetValue(owner, out replacement))
                    {
                        return replacement + "." + CanonicalVariableMember(member);
                    }
                    if (m_newVariables.Contains(owner))
                    {
                        return token;
                    }
                }

                // Any other member of a collection this function holds -- "v.Count", "v.Value" -- is a
                // property the interpreter looks up, and does not find: C#'s Variable happens to
                // have members of those names, and copied through, "v.Count" answered 2 where the
                // script's answer is the error.
                if (!isCall && IsPlainName(owner) && IsPlainName(member) && m_collectionLocals.Contains(owner) &&
                    !IsVariableMember(member) && !IsCollectionMethod(member) && !IsMappedStringMember(member))
                {
                    return owner + ".ReadField(\"" + member + "\")";
                }

                string mapped = null;
                if (ProcessArray(owner, member, ref mapped) ||
                    ProcessStringMember(owner, member, ref mapped) ||
                    (m_newVariables.Contains(owner) && !m_collectionLocals.Contains(owner) &&
                     MapStringMember(owner, member, ref mapped)))
                {
                    return mapped;
                }
            }

            // "a[i]" on a collection local yields a Variable, which has no arithmetic
            // operators. Inside a known expression the result is numeric by definition --
            // IsKnownExpression rejects anything string-typed -- so AsDouble() is safe here
            // and nowhere else.
            // Except when the statement compares: a string element orders by text and a
            // numeric one by value, and reading both as 0 first made "c[0] > c[1]" over
            // {"pear","fig"} answer false. Variable's own comparison decides from the
            // runtime type, which is what the interpreter does.
            if (m_knownExpression && !string.IsNullOrEmpty(arrayArg) &&
                m_collectionLocals.Contains(token))
            {
                // A comparison leaves the element a Variable so its own operators can pick
                // the rule from the runtime type. An argument list is different: whatever the
                // comparison outside the call does, "Math.Abs(a[i])" needs a number.
                bool bitwiseOperand = m_statementBitwise && !insideCall;
                bool keepVariable = !bitwiseOperand && !insideCall &&
                                    (m_statementRelational || m_statementVariableAccum ||
                                     m_statementSameValue);
                return (bitwiseOperand ? "(int)" : "") + token + arrayArg +
                       (keepVariable ? "" : ".AsDouble()");
            }

            // A name this function never declared, which the interpreter already holds as a
            // variable: a global. Reading it through the interpreter makes "g = g + 1" work,
            // where the declaration used to come out as "var g = g + 1" -- a local referring
            // to itself. The write-back is the AddGlobalOrLocalVariable the assignment already
            // emits, so the global stays in step.
            // Tokens reach here with the surrounding whitespace still attached.
            var bareName = token.Trim();
            if (IsPlainName(bareName) && IsInterpreterVariable(bareName))
            {
                m_usesInterpreter = true;
                resolved = true;
                // A subscript still applies: the read yields a Variable, which is indexable.
                return "__interpreter.GetVariableValue(\"" + bareName + "\")" + arrayArg;
            }

            // A name nothing in this function defines and the interpreter does not hold yet -- a
            // global the script assigns later, "if (user == \"\")" -- is read when the code runs,
            // by name, as the interpreted function reads it: CscsLate.Value takes the same route
            // as the call-by-name path, and fails as the interpreter does if it is still missing.
            // Not a name the body assigns, which is a local -- unless a read of it comes first in
            // the text and the local is not declared yet (ReadBeforeAssigned).
            // Only where an expression is being built (ReplaceArgsInString): the other callers
            // classify a token, and a name they cannot resolve sends the whole statement to the
            // interpreter as text -- slower, but it also covers "g.Size" and "g[0] += 5".
            if (allowLate && IsLateBoundName(bareName, isCall))
            {
                m_usesInterpreter = true;
                resolved = true;
                return "CscsLate.Value(__interpreter, \"" + bareName + "\")" + arrayArg;
            }

            resolved = !string.IsNullOrWhiteSpace(arrayArg) ||
                        m_newVariables.Contains(token);
            return token + arrayArg;
        }

        /// <summary>A name called as a function that nothing defines at translation -- no script
        /// function, built-in, C# mapping or Math function -- and that is not a local or argument.</summary>
        bool IsLateFunctionName(string name)
        {
            return !m_scriptInCSharp && IsPlainName(name) && !char.IsDigit(name[0]) &&
                   !name.StartsWith("__", StringComparison.Ordinal) &&
                   // No argument or local of the name stops it: a call is only ever looked up
                   // among functions -- "searchTrie = ...; SearchTrie(searchTrie, t)", even "g(1)"
                   // with an argument g, calls the function, and fails when there is none.
                   !Constants.RESERVED.Contains(name) && !s_csKeywords.Contains(name) &&
                   !IsMathFunction(name, out _) && string.IsNullOrEmpty(GetCSharpFunction(name, "0")) &&
                   !InterpreterKnows(name);
        }

        /// <summary>Whether the text calls, by its bare name, a built-in the interpreter has and C#
        /// does not: not a Math function, not one with a C# mapping, not a member ("s.Size()").</summary>
        bool CallsInterpreterBuiltin(string text)
        {
            var interpreter = m_parentScript?.InterpreterInstance;
            if (interpreter == null || m_scriptInCSharp)
            {
                return false;
            }
            foreach (System.Text.RegularExpressions.Match call in System.Text.RegularExpressions.Regex.Matches(
                         WithoutStringContents(text), @"(?<![\w.])([A-Za-z_]\w*)\s*\("))
            {
                var name = call.Groups[1].Value;
                if (Constants.RESERVED.Contains(name) || s_csKeywords.Contains(name) || IsMathFunction(name, out _) ||
                    !string.IsNullOrEmpty(GetCSharpFunction(name, "0")) || m_newVariables.Contains(name))
                {
                    continue;
                }
                var function = interpreter.GetFunction(name);
                if (function != null && !(function is CustomFunction))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Whether a script call appears inside another call's argument list.</summary>
        bool NestsScriptCall(string text)
        {
            int open = text.IndexOf('(');
            while (open >= 0)
            {
                int close = FindMatchingParen(text, open);
                if (close < 0)
                {
                    return false;
                }
                if (MentionsScriptCall(text.Substring(open + 1, close - open - 1)))
                {
                    return true;
                }
                open = text.IndexOf('(', close + 1);
            }
            return false;
        }

        /// <summary>A built-in that reads its arguments as values (GetFunctionArgs) -- the string
        /// functions, "Substring(s, 1, 2)", tokenize and type -- which CscsCalls.Call can therefore run with values
        /// (ByName). Not one that reads a name, like Size.</summary>
        bool IsValueBuiltin(string name)
        {
            return !m_scriptInCSharp && IsPlainName(name) &&
                   (m_parentScript?.InterpreterInstance?.GetFunction(name) is StringManipulationFunction ||
                    m_parentScript?.InterpreterInstance?.GetFunction(name) is TokenizeFunction ||
                    m_parentScript?.InterpreterInstance?.GetFunction(name) is TypeFunction);
        }

        bool IsFunctionWithSpace(string name)
        {
            var interpreter = m_parentScript?.InterpreterInstance;
            return interpreter != null && IsPlainName(name) &&
                   interpreter.Translation.IsFunctWithSpace(Constants.ConvertName(name));
        }

        /// <summary>
        /// Whether a name starting with "__" -- the prefix of the translator's own temporaries --
        /// is one the script itself uses, a global like "__textView": it is in the function's
        /// source as written, where no temporary is.
        /// </summary>
        bool IsScriptsOwnName(string name)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(WithoutStringContents(m_originalCode ?? ""),
                @"(?<![\w.])" + System.Text.RegularExpressions.Regex.Escape(name) + @"(?!\w)");
        }

        bool IsLateBoundName(string name, bool isCall)
        {
            return !isCall && !m_scriptInCSharp && IsPlainName(name) && !char.IsDigit(name[0]) &&
                   (!name.StartsWith("__", StringComparison.Ordinal) || IsScriptsOwnName(name)) &&
                   (!m_assignedAnywhere.Contains(name) || m_readFirst.Contains(name)) &&
                   !m_paramMap.ContainsKey(name) &&
                   !m_newVariables.Contains(name) && !m_collectionArgs.Contains(name) &&
                   !m_widenedIntArgs.Contains(name) && !m_enumLocals.ContainsKey(name) &&
                   !m_declaredArgsMap.Keys.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) &&
                   !Constants.RESERVED.Contains(name) && !s_csKeywords.Contains(name) &&
                   name != Constants.TRUE && name != Constants.FALSE && name != "null" &&
                   !IsMathFunction(name, out _) && !InterpreterKnows(name);
        }

        /// <summary>Whether the interpreter holds the name in any way -- a function, a built-in, a
        /// variable, a class, a namespace. Those have paths of their own; only a name it knows
        /// nothing of is left to be read when the code runs.</summary>
        bool InterpreterKnows(string name)
        {
            var interpreter = m_parentScript?.InterpreterInstance;
            if (interpreter == null)
            {
                return true;
            }
            return interpreter.GetFunction(name) != null ||
                   interpreter.GetVariable(name, m_parentScript) != null ||
                   interpreter.GetClass(Constants.ConvertName(name)) != null ||
                   interpreter.NamespaceExists(name) ||
                   string.Equals(name, m_functionName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The assigned names whose first mention in the body is a read -- "if (key == arg) {
        /// return; } key = arg;". Until the assignment declares the C# local, such a read is of
        /// whatever the interpreter holds under the name when the code runs: a global, which the
        /// assignment then writes (AddGlobalOrLocalVariable writes an existing global rather
        /// than making a local), or nothing, which is the interpreter's own error. The C# local
        /// is not in scope there (CS0841), so the read is late-bound (IsLateBoundName) as long as
        /// the translator has not declared the local yet (m_newVariables).
        /// </summary>
        static HashSet<string> ReadBeforeAssigned(string code, HashSet<string> assigned, HashSet<string> selfRead = null)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var text = WithoutStringContents(code ?? "");
            foreach (var name in assigned)
            {
                var escaped = System.Text.RegularExpressions.Regex.Escape(name);
                var options = System.Text.RegularExpressions.RegexOptions.IgnoreCase;
                // A mention: the name on its own, not a member (".name") and not a call.
                var mention = System.Text.RegularExpressions.Regex.Match(text,
                    @"(?<![\w.])" + escaped + @"\b(?!\s*\()", options);
                if (!mention.Success)
                {
                    continue;
                }
                // Only what gives the name a value without reading it: "x += 1" and "x++"
                // read it first, and so count as a read (TryBuildGlobalCompound).
                int firstWrite = int.MaxValue;
                foreach (var pattern in new[] {
                    @"(?<![\w.])(" + escaped + @")\s*=(?!=)",
                    @"\b(?:for|foreach)\s*\(\s*(?:var\s+)?(" + escaped + @")\s*(?:\bin\b|:)",
                    @"\bcatch\s*\(\s*(" + escaped + @")\b" })
                {
                    var write = System.Text.RegularExpressions.Regex.Match(text, pattern, options);
                    if (write.Success)
                    {
                        firstWrite = Math.Min(firstWrite, write.Groups[1].Index);
                    }
                }
                bool readEarlier = mention.Index < firstWrite;
                // "x = !x", "q = (q + 1) % 3": the first write reads the name on its right side,
                // before the value is stored, so the read still comes first.
                var plain = System.Text.RegularExpressions.Regex.Match(text,
                    @"(?<![\w.])" + escaped + @"\s*=(?!=)([^;{}]*)", options);
                if (plain.Success && plain.Index == firstWrite &&
                    System.Text.RegularExpressions.Regex.IsMatch(plain.Groups[1].Value,
                        @"(?<![\w.])" + escaped + @"\b(?!\s*\()", options))
                {
                    names.Add(name);
                    selfRead?.Add(name);
                }
                // Read before the first write, whatever that write reads. "print(x); x = !x;" is both:
                // the self-read above has to be recorded as well, or "x = !x" still declares the
                // local it reads (CS0841).
                if (readEarlier)
                {
                    names.Add(name);
                }
            }
            return names;
        }

        /// <summary>The names the body assigns (plainly or compounded), steps, loops over or
        /// catches into.</summary>
        static HashSet<string> AssignedAnywhere(string code)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Text in quotes is blanked first: "t=" + t is no assignment to t.
            var text = WithoutStringContents(code ?? "");
            foreach (var pattern in new[] {
                @"(?<![\w.])([A-Za-z_]\w*)\s*(?:\+|-|\*|/|%|&|\||\^|<<|>>)?=(?!=)",
                @"\b(?:for|foreach)\s*\(\s*(?:var\s+)?([A-Za-z_]\w*)\s*(?:\bin\b|:)",
                @"\bcatch\s*\(\s*([A-Za-z_]\w*)",
                @"(?:\+\+|--)\s*([A-Za-z_]\w*)",
                @"(?<![\w.])([A-Za-z_]\w*)\s*(?:\+\+|--)" })
            {
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, pattern))
                {
                    names.Add(m.Groups[1].Value);
                }
            }
            return names;
        }

        /// <summary>
        /// Whether the text adds anywhere outside a string literal. "+=" and "++" do not
        /// count: the first is numeric accumulation into a declared local and the second is an
        /// increment, and neither leaves the result's type open the way a bare "+" does.
        /// </summary>
        static bool HasPlainPlus(string text)
        {
            bool inQuotes = false;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                }
                else if (!inQuotes && text[i] == '+' &&
                         (i + 1 >= text.Length || (text[i + 1] != '=' && text[i + 1] != '+')) &&
                         (i == 0 || text[i - 1] != '+'))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether the text compares with "&lt;", "&gt;", "&lt;=" or "&gt;=" anywhere outside a
        /// string literal. Shifts ("&lt;&lt;", "&gt;&gt;") are not comparisons and do not count,
        /// and neither do "==" and "!=", which Variable does not overload.
        /// </summary>
        /// <summary>
        /// The words whose parenthesis introduces a condition rather than a call. An element
        /// inside a call's argument list is numeric whatever the surrounding expression does,
        /// but "if (...)" is not a call, so what is inside it is still the expression itself.
        /// </summary>
        const string SAME_VALUE_NAME = "SameValue";
        const string SAME_VALUE_CALL = "Variable.SameValue(";

        static readonly HashSet<string> s_conditionWords = new HashSet<string>
        {
            "if", "elif", "else", "while", "for", "switch", "return", "do", "catch", "throw"
        };

        /// <summary>
        /// Whether this open parenthesis belongs to a call rather than to a condition or to
        /// plain grouping. Dotted names count: "Math.Abs(" is as much a call as "helper(",
        /// and reading it as grouping left an element unconverted inside an argument list.
        /// </summary>
        static bool IsCallOpener(string opener)
        {
            // SameValue is a call, but not one whose arguments are numbers: it exists to
            // compare two values by their runtime type, so an element in it stays a Variable.
            if (opener.EndsWith(SAME_VALUE_NAME, StringComparison.Ordinal))
            {
                return false;
            }
            if (opener.Length == 0 || s_conditionWords.Contains(opener) ||
                (!char.IsLetter(opener[0]) && opener[0] != '_'))
            {
                return false;
            }
            foreach (var ch in opener)
            {
                if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '.')
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Whether the text is "&lt;name&gt; += ..." for a local that holds a Variable. Reading
        /// the right-hand element as a number first built "000" for "r += a[i]" over
        /// {"a","b","c"}, where the interpreter concatenates to "abc".
        /// </summary>
        bool IsVariableAccumulation(string text)
        {
            int plus = (text ?? "").IndexOf("+=", StringComparison.Ordinal);
            if (plus <= 0)
            {
                return false;
            }
            var target = text.Substring(0, plus).Trim();
            return IsPlainName(target) && m_variableLocals.Contains(target);
        }

        /// <summary>
        /// Whether the text combines bits with "&amp;", "|" or "^" outside a string literal.
        /// The logical "&amp;&amp;" and "||" are not bit operations and do not count.
        /// </summary>
        static bool HasPlainBitwise(string text)
        {
            bool inQuotes = false;
            for (int i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                }
                else if (!inQuotes && (ch == '&' || ch == '|' || ch == '^'))
                {
                    if (i + 1 < text.Length && text[i + 1] == ch)
                    {
                        i++;        // "&&" or "||": logical, not bitwise
                        continue;
                    }
                    return true;
                }
            }
            return false;
        }

        static bool HasPlainRelational(string text)
        {
            bool inQuotes = false;
            for (int i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                }
                else if (!inQuotes && (ch == '<' || ch == '>'))
                {
                    if (i + 1 < text.Length && text[i + 1] == ch)
                    {
                        i++;        // a shift, not a comparison
                        continue;
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Appends the member chain that follows position <paramref name="at"/>, if any, and
        /// returns the position to carry on from. Used after a call has been built, whose
        /// result is a Variable and so can be read from like any other.
        /// </summary>
        int AppendMemberChainAfter(StringBuilder sb, string text, int at)
        {
            int i = at;
            while (i + 1 < text.Length && text[i + 1] == '.')
            {
                int nameEnd = i + 2;
                while (nameEnd < text.Length &&
                       (char.IsLetterOrDigit(text[nameEnd]) || text[nameEnd] == '_'))
                {
                    nameEnd++;
                }
                var member = text.Substring(i + 2, nameEnd - i - 2);
                if (member.Length == 0)
                {
                    break;
                }
                if (nameEnd < text.Length && text[nameEnd] == '(')
                {
                    // A property written as a call -- "a[0].Upper()". The interpreter reads the
                    // property and consumes the empty parentheses itself (GetCoreProperty), so the
                    // same value is read here and the "()" is dropped: Variable.Upper is a
                    // property, and ".Upper()" was CS1955. Only an empty pair, and only for the
                    // members that really are properties -- ".Sort()" and friends are methods and
                    // need their call, which the branches above build.
                    if (!IsEmptyPropertyCall(text, nameEnd, member))
                    {
                        break;      // a call: not a member read
                    }
                    sb.Append("." + CanonicalVariableMember(member));
                    i = nameEnd + 1;
                    continue;
                }
                sb.Append(IsVariableMember(member) || IsMappedStringMember(member) ?
                    "." + CanonicalVariableMember(member) : ".ReadField(\"" + member + "\")");
                i = nameEnd - 1;
            }
            return i;
        }

        /// <summary>
        /// Builds the reads for a member chain on a value that holds a Variable: each field is
        /// a property read, and the last segment may instead be something Variable itself
        /// answers to. Returns null when a segment is neither -- a call, say, which has to be
        /// built rather than read.
        /// </summary>
        string BuildMemberChain(string owner, string members)
        {
            var parts = (members ?? "").Split('.');
            var built = owner;
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (!IsPlainName(part))
                {
                    return null;
                }
                bool last = i == parts.Length - 1;
                if (last && (IsVariableMember(part) || IsMappedStringMember(part)))
                {
                    built += "." + CanonicalVariableMember(part);
                }
                else
                {
                    built += ".ReadField(\"" + part + "\")";
                }
            }
            return built;
        }

        /// <summary>
        /// Whether the token names a method on a local that holds a class instance, so that
        /// the call that follows has to be built rather than read as a member.
        /// </summary>
        bool IsInstanceMethodCall(string token)
        {
            var trimmed = (token ?? "").Trim();
            int dot = trimmed.IndexOf('.');
            if (dot <= 0)
            {
                return false;
            }
            var owner = trimmed.Substring(0, dot);
            var member = trimmed.Substring(dot + 1);
            return m_variableLocals.Contains(owner) && IsPlainName(member) &&
                   !IsVariableMember(member) && !IsMappedStringMember(member) &&
                   !IsCollectionMethod(member);
        }

        /// <summary>
        /// Whether an index is followed by a member of the element: the "[1].Sum(" of
        /// "a[1].Sum()", or the "[0].x" of "a[0].x". For a call, only a method the element could
        /// really have -- a Variable's own members, the mapped string ones and a collection's
        /// methods keep the path they already had.
        /// </summary>
        bool ElementMethodCallFollows(string suffix)
        {
            var text = (suffix ?? "").Trim();
            if (!text.StartsWith("["))
            {
                return false;
            }
            int depth = 0;
            int close = -1;
            for (int i = 0; i < text.Length && close < 0; i++)
            {
                if (text[i] == '[')
                {
                    depth++;
                }
                else if (text[i] == ']' && --depth == 0)
                {
                    close = i;
                }
            }
            // "e[0][0].v": further subscripts before the member. The expression path this hands
            // over to consumes the whole chain; stopping at the first "]" refused the read.
            while (close > 0 && close + 1 < text.Length && text[close + 1] == '[')
            {
                int next = close + 1;
                int nested = 0;
                close = -1;
                for (int i = next; i < text.Length; i++)
                {
                    if (text[i] == '[') { nested++; }
                    else if (text[i] == ']' && --nested == 0) { close = i; break; }
                }
            }
            if (close < 0 || close + 1 >= text.Length || text[close + 1] != '.')
            {
                return false;
            }
            int nameEnd = close + 2;
            while (nameEnd < text.Length && (char.IsLetterOrDigit(text[nameEnd]) || text[nameEnd] == '_'))
            {
                nameEnd++;
            }
            // Any subscript, numeric or keyed. Keyed reads were refused while a map literal whose
            // value was a "new" came out of the interpreter as a plain tuple -- "m[\"p\"]" threw
            // there while the compiled subscript answered 3. Fixed in CheckConsistencyAndSign
            // (Parser.cs), so the two agree and the restriction is gone.
            var method = text.Substring(close + 2, nameEnd - close - 2);
            if (method.Length == 0)
            {
                return false;
            }
            // A member read with no call of its own -- "a[0].x" -- goes the same way: the
            // expression path emits GetProperty for it, which is how a field on a class instance
            // is reached, while the token loop left "a[0].x" as C# (CS1061).
            if (nameEnd >= text.Length || text[nameEnd] != '(' ||
                IsEmptyPropertyCall(text, nameEnd, method))
            {
                return true;
            }
            return !IsVariableMember(method) && !IsMappedStringMember(method) &&
                   !IsCollectionMethod(method);
        }

        /// <summary>
        /// Methods a collection has rather than a class. Sending one to CallMethod threw "Not
        /// a class instance" at run time -- "k = m.Keys; k.Add(\"z\");" -- because the local
        /// holds a plain Variable. Left off that path they become an interpreter callback,
        /// which is what a collection local's own methods already use, so the interpreter does
        /// the work and nothing here has to restate what these mean.
        /// </summary>
        static bool IsCollectionMethod(string member)
        {
            int callStart = member.IndexOf('(');
            switch ((callStart < 0 ? member : member.Substring(0, callStart)).Trim().ToLower())
            {
                case "add":
                case "addunique":
                case "remove":
                case "removeat":
                case "insert":
                case "clear":
                case "join":
                case "merge": return true;
            }
            return false;
        }

        static bool IsTokenSeparator(char ch)
        {
            // Comparison and logical characters end a token just like the arithmetic ones.
            // Leaving them out meant "n>1&&n<100" was split only at '&', so everything after
            // the first comparison stayed one unresolved blob and argument names in it were
            // never replaced.
            return (ch == ',' || ch == '+' || ch == '-' || ch == '(' || ch == ')' || ch == '[' || ch == ']' ||
                    ch == '%' || ch == '*' || ch == '/' || ch == '&' || ch == '|' || ch == '^' || ch == '?' ||
                    ch == '<' || ch == '>' || ch == '=' || ch == '!' || ch == ':' || ch == '~');
        }

        string ReplaceArgsInString(string argStr)
        {
            StringBuilder sb = new StringBuilder();
            bool inQuotes = false;
            string token = "";
            int backSlashes = 0;
            int parenDepth = 0;
            // Which of the open parentheses belong to a call. "if (" is not one, so an
            // element inside a condition is still governed by the condition's operators.
            var parenIsCall = new Stack<bool>();
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
                    // An escaped character -- "{\"a\": 1}" -- stays escaped: the text is C# string
                    // literal content either way. It went out as '\'" and did not compile.
                    sb.Append("\\" + ch);
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
                // Trimmed: a token keeps the whitespace that preceded it, so the name after
                // an "=" arrives as " counts" and the lookup missed -- the subscript then went
                // out without its numeric conversion and would not assign to a double.
                else if (ch == '[' && m_knownExpression && m_collectionLocals.Contains(token.Trim()))
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
                    // No cast on the index: Variable has an indexer for each of the types one
                    // can arrive as -- int, double (loop counters are declared double so that
                    // "/" is not integer division), string, and Variable, which is what a
                    // subscript like "m[keys[i]]" produces. Casting to int here worked for the
                    // numeric cases and made the other two impossible to compile.
                    // Built up rather than appended straight away: a method call has to wrap
                    // the whole subscript expression, which means knowing it before emitting.
                    var elemExpr = token + "[" + ReplaceArgsInString(index) + "]";
                    i = close;

                    // Chained subscripts: each one yields a Variable, which is itself
                    // indexable, so consume the whole chain before converting. Converting
                    // after the first left "a[0].AsDouble()[1]" -- indexing a double.
                    while (i + 1 < argStr.Length && argStr[i + 1] == '[')
                    {
                        int nextClose = FindMatchingBracket(argStr, i + 1);
                        if (nextClose < 0)
                        {
                            break;
                        }
                        var nextIndex = argStr.Substring(i + 2, nextClose - i - 2);
                        elemExpr += "[" + ReplaceArgsInString(nextIndex) + "]";
                        i = nextClose;
                    }

                    // "a[0].Val()": a method on the element. The element is a Variable, so the
                    // call is built around it the same way it is for a named local.
                    if (i + 1 < argStr.Length && argStr[i + 1] == '.')
                    {
                        int callNameEnd = i + 2;
                        while (callNameEnd < argStr.Length &&
                               (char.IsLetterOrDigit(argStr[callNameEnd]) || argStr[callNameEnd] == '_'))
                        {
                            callNameEnd++;
                        }
                        var elemCall = argStr.Substring(i + 2, callNameEnd - i - 2);
                        if (elemCall.Length > 0 && callNameEnd < argStr.Length &&
                            argStr[callNameEnd] == '(' && !IsVariableMember(elemCall) &&
                            !IsMappedStringMember(elemCall))
                        {
                            int callClose = FindMatchingParen(argStr, callNameEnd);
                            if (callClose > 0)
                            {
                                sb.Append("Variable.CallMethod(").Append(elemExpr)
                                  .Append(",\"").Append(elemCall).Append("\"");
                                foreach (var callArg in SplitTopLevel(
                                    argStr.Substring(callNameEnd + 1, callClose - callNameEnd - 1), ','))
                                {
                                    if (!string.IsNullOrWhiteSpace(callArg))
                                    {
                                        sb.Append(",").Append(ReplaceArgsInString(callArg));
                                    }
                                }
                                sb.Append(")");
                                i = AppendMemberChainAfter(sb, argStr, callClose);
                                prevSeparator = ')';
                                token = "";
                                continue;
                            }
                        }
                    }

                    // An operand of "&", "|" or "^" has to be a whole number: C# has no bit
                    // operations on double, and the interpreter truncates toward zero -- it
                    // reads 6.9 & 3 as 6 & 3. A member or an argument list decides its own
                    // type, so neither takes the cast.
                    bool bitwiseOperand = m_statementBitwise && !parenIsCall.Contains(true) &&
                        (i + 1 >= argStr.Length || argStr[i + 1] != '.');
                    if (bitwiseOperand)
                    {
                        sb.Append("(int)");
                    }
                    sb.Append(elemExpr);
                    // Not when a member follows: "m[\"k\"].Size" needs the Variable, and
                    // converting first produced "m[\"k\"].AsDouble().Size", which does not
                    // compile. The member itself decides the type from there.
                    //
                    // A field on the element: it is a Variable, so a member that Variable
                    // does not have is read through the interpreter's property lookup, the
                    // same way a named local's is.
                    if (i + 1 < argStr.Length && argStr[i + 1] == '.')
                    {
                        int nameEnd = i + 2;
                        while (nameEnd < argStr.Length &&
                               (char.IsLetterOrDigit(argStr[nameEnd]) || argStr[nameEnd] == '_'))
                        {
                            nameEnd++;
                        }
                        var elemMember = argStr.Substring(i + 2, nameEnd - i - 2);
                        bool isCallMember = nameEnd < argStr.Length && argStr[nameEnd] == '(';
                        if (elemMember.Length > 0 && IsEmptyPropertyCall(argStr, nameEnd, elemMember))
                        {
                            sb.Append(".").Append(CanonicalVariableMember(elemMember));
                            i = nameEnd + 1;
                            prevSeparator = ')';
                            token = "";
                            continue;
                        }
                        if (elemMember.Length > 0 && !isCallMember && !IsVariableMember(elemMember) &&
                            !IsMappedStringMember(elemMember))
                        {
                            sb.Append(".ReadField(\"").Append(elemMember).Append("\")");
                            i = nameEnd - 1;
                            prevSeparator = ')';
                            token = "";
                            continue;
                        }
                    }

                    // Nor when the expression adds: an element's type is not known until it
                    // runs, and "+" is the one operator whose meaning depends on it, so
                    // converting to a number here read a string element as 0 --
                    // "a[1] + a[0]" over {1,"two",3} came back as 1 rather than "two1".
                    // Variable's own "+" applies the interpreter's rule instead.
                    // The "+" that suppresses this has to be at the element's own level: in
                    // "Math.Pow(a[1], 2) + n" the element is an argument, which is numeric
                    // whatever the addition outside the call does.
                    // "<" and ">" are suppressed for the same reason: strings order by text
                    // and numbers by value, so "c[0] > c[1]" over {"pear","fig"} answered
                    // false once both sides had been read as 0. Variable's own comparison
                    // picks the rule from the runtime type, exactly as the interpreter does.
                    if ((i + 1 >= argStr.Length || argStr[i + 1] != '.') &&
                        (bitwiseOperand || parenIsCall.Contains(true) ||
                         (!HasPlainPlus(argStr) && !HasPlainRelational(argStr) &&
                          !m_statementRelational && !m_statementVariableAccum &&
                          !m_statementSameValue)))
                    {
                        sb.Append(".AsDouble()");
                    }
                    prevSeparator = ']';
                    token = "";
                }
                else if (ch == '(' && IsPlainName(token.Trim()) && MentionsScriptCall(token.Trim() + "()") &&
                         FindMatchingParen(argStr, i) is int scriptClose && scriptClose > i &&
                         TryInlineScriptCall(token.Trim(), argStr.Substring(i + 1, scriptClose - i - 1),
                                             out string scriptCall))
                {
                    // A script call reaching the expression path -- inside SameValue's
                    // arguments, say, where a comparison rewrite has put it. It is made here, in
                    // place, as an expression; copied through it named a C# method that does
                    // not exist. The result stays a Variable: see ReadCallResult.
                    sb.Append(scriptCall);
                    i = scriptClose;
                    prevSeparator = ')';
                    token = "";
                }
                else if (ch == '(' && s_conversions.Contains(token.Trim()) &&
                         FindMatchingParen(argStr, i) is int convClose && convClose > i &&
                         !MentionsScriptCall(argStr.Substring(i + 1, convClose - i - 1)))
                {
                    // A conversion takes exactly its own parenthesised argument. Resolved as
                    // an ordinary token it was handed everything after the "(" -- "s)>10" in
                    // "int(s) > 10" -- wrapped that, and the loop then went on to emit
                    // "(s)>10" a second time.
                    var inner = argStr.Substring(i + 1, convClose - i - 1);
                    sb.Append(GetCSharpFunction(token.Trim(), inner));
                    i = convClose;
                    prevSeparator = ')';
                    token = "";
                }
                else if (ch == '(' && IsInstanceMethodCall(token))
                {
                    // "p.Sum()" on a local holding a class instance. The call's parentheses
                    // are consumed here rather than emitted as a separator: running a method
                    // needs an argument list, so it becomes a call to the helper rather than
                    // a member read, and the arguments have to move inside it.
                    int close = FindMatchingParen(argStr, i);
                    if (close < 0)
                    {
                        token += ch;
                        continue;
                    }
                    int dot = token.IndexOf('.');
                    var callArgs = SplitTopLevel(argStr.Substring(i + 1, close - i - 1), ',');
                    var resolved = new List<string>();
                    foreach (var callArg in callArgs)
                    {
                        if (!string.IsNullOrWhiteSpace(callArg))
                        {
                            resolved.Add(ReplaceArgsInString(callArg));
                        }
                    }
                    int chainStart = sb.Length;
                    sb.Append("Variable.CallMethod(").Append(token.Substring(0, dot).Trim())
                      .Append(",\"").Append(token.Substring(dot + 1).Trim()).Append("\"");
                    foreach (var callArg in resolved)
                    {
                        sb.Append(",").Append(callArg);
                    }
                    sb.Append(")");
                    i = close;
                    // A member may follow the call -- "p.Kid().v" -- and the result is a
                    // Variable, so the chain simply continues from it. So may another call --
                    // "p.Kid().Kid().v" -- which went out verbatim as ".Kid()" on a Variable
                    // (CS1061): it wraps everything built since the chain began, the same way
                    // the first call was built. A Variable, string or collection member is not a
                    // class method, so it ends the chain as before.
                    while (true)
                    {
                        i = AppendMemberChainAfter(sb, argStr, i);
                        if (i + 1 >= argStr.Length || argStr[i + 1] != '.')
                        {
                            break;
                        }
                        int nameEnd = i + 2;
                        while (nameEnd < argStr.Length &&
                               (char.IsLetterOrDigit(argStr[nameEnd]) || argStr[nameEnd] == '_'))
                        {
                            nameEnd++;
                        }
                        if (nameEnd == i + 2 || nameEnd >= argStr.Length || argStr[nameEnd] != '(')
                        {
                            break;
                        }
                        var nextMethod = argStr.Substring(i + 2, nameEnd - i - 2);
                        int nextClose = FindMatchingParen(argStr, nameEnd);
                        if (nextClose < 0 || IsVariableMember(nextMethod) ||
                            IsMappedStringMember(nextMethod) || IsCollectionMethod(nextMethod))
                        {
                            break;
                        }
                        sb.Insert(chainStart, "Variable.CallMethod(");
                        sb.Append(",\"").Append(nextMethod).Append("\"");
                        foreach (var nextArg in SplitTopLevel(argStr.Substring(nameEnd + 1, nextClose - nameEnd - 1), ','))
                        {
                            if (!string.IsNullOrWhiteSpace(nextArg))
                            {
                                sb.Append(",").Append(ReplaceArgsInString(nextArg));
                            }
                        }
                        sb.Append(")");
                        i = nextClose;
                    }
                    prevSeparator = ')';
                    token = "";
                }
                else if (ch == '(' && EmptyPropertyCallOnVariable(token, argStr, i))
                {
                    // "v.Upper()" where v holds a Variable: the interpreter reads the property
                    // and eats the "()" (GetCoreProperty), so the property is read and the
                    // pair dropped -- Variable.Upper is not a method (CS1955).
                    sb.Append(ResolveToken(token, out _));
                    i++;
                    prevSeparator = ')';
                    token = "";
                }
                else if (IsTokenSeparator(ch))
                {
                    if (ch == '(')
                    {
                        parenDepth++;
                        var opener = token.Trim();
                        parenIsCall.Push(IsCallOpener(opener));
                    }
                    else if (ch == ')')
                    {
                        parenDepth--;
                        if (parenIsCall.Count > 0) { parenIsCall.Pop(); }
                    }
                    string arguments = i + 1 < argStr.Length ? argStr.Substring(i + 1) : "";
                    sb.Append(AsCscsNumberIfBool(AsDoubleNextToDivision(
                        ResolveToken(token, out _, arguments, ch == '(', parenIsCall.Contains(true), allowLate: true),
                        token, prevSeparator, ch), token, prevSeparator, ch));
                    sb.Append(ch);
                    prevSeparator = ch;
                    token = "";
                }
                else
                { // We are collecting the chars
                    token += ch;
                }
            }

            sb.Append(AsCscsNumberIfBool(
                AsDoubleNextToDivision(ResolveToken(token, out _, allowLate: true), token, prevSeparator, '\0'),
                token, prevSeparator, '\0'));
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
        static readonly HashSet<string> s_widening = new HashSet<string> { "*", "+", "-", "/" };

        /// <summary>
        /// Whether a separator puts this operand next to arithmetic. CSCS has no boolean type
        /// -- a truth value is the number 1 or 0 -- so a local C# declared "bool" has to be
        /// converted there: "b + 1" does not compile at all. Conditions, "&amp;&amp;", "!" and
        /// "?:" want the bool itself, and all of those compile already.
        /// </summary>
        static bool IsArithmeticPosition(char separator)
        {
            return separator == '+' || separator == '-' || separator == '*' ||
                   separator == '/' || separator == '%';
        }

        /// <summary>
        /// Converts a bool-typed local to the number CSCS treats it as. Only a name whose
        /// every assignment agreed on "bool" (see CollectLocalTypes, which records nothing for
        /// a ternary) and only next to arithmetic -- widening either of those cost constructs
        /// that already compiled.
        /// </summary>
        /// <summary>
        /// Whether this text is a bare local whose every assignment agreed on "bool".
        /// </summary>
        bool IsBoolLocal(string text)
        {
            var name = (text ?? "").Trim();
            return IsPlainName(name) && m_localTypes.TryGetValue(name, out var t) && t == "bool";
        }

        /// <summary>
        /// Converts either side of an assignment or comparison from a C# bool to the number
        /// CSCS treats it as. Two shapes need it:
        ///
        ///   "t += b"  -- double += bool does not compile
        ///   "b == 1"  -- bool == int does not compile
        ///
        /// Only where the other side is *not* also a bool: "b == c" compiles as it stands and
        /// means the same thing. Only arithmetic compound operators: OPER_ACTIONS also holds
        /// "->" and ":", which are nothing of the kind. The left side is returned and the
        /// right side handed back through <paramref name="convertedRhs"/>.
        /// </summary>
        string AsCscsNumberBeside(string lhs, string op, string rhs, out string convertedRhs)
        {
            convertedRhs = rhs;
            var trimmedOp = (op ?? "").Trim();
            bool compound = trimmedOp.Length == 2 && trimmedOp[1] == '=' &&
                            "+-*/%".IndexOf(trimmedOp[0]) >= 0;
            bool comparison = trimmedOp == "==" || trimmedOp == "!=";
            if (!compound && !comparison)
            {
                return lhs;
            }
            // The sides arrive carrying the statement's punctuation -- "if(b" and "1)" -- so
            // the name is taken out of it and put back afterwards.
            var lhsName = (lhs ?? "").Trim().TrimStart('i', 'f', 'w', 'h', 'l', 'e', '(', ' ');
            var lhsPrefix = (lhs ?? "").Substring(0, (lhs ?? "").Length - lhsName.Length);
            var rhsName = (rhs ?? "").Trim().TrimEnd(')', ' ');
            var rhsSuffix = (rhs ?? "").Substring(rhsName.Length == 0 ? 0 :
                                (rhs ?? "").IndexOf(rhsName, StringComparison.Ordinal) + rhsName.Length);
            bool lhsBool = IsBoolLocal(lhsName);
            bool rhsBool = IsBoolLocal(rhsName);
            if (lhsBool && rhsBool)
            {
                return lhs;     // "b == c": both bools, and C# agrees with the interpreter
            }
            if (compound && rhsBool)
            {
                convertedRhs = "(" + rhsName + "?1:0)" + rhsSuffix;
                return lhs;
            }
            if (comparison && rhsBool && IsNumber(lhsName))
            {
                convertedRhs = "(" + rhsName + "?1:0)" + rhsSuffix;
                return lhs;
            }
            if (comparison && lhsBool && IsNumber(rhsName))
            {
                return lhsPrefix + "(" + lhsName + "?1:0)";
            }
            return lhs;
        }

        string AsCscsNumberIfBool(string resolved, string original, char before, char after)
        {
            var name = (original ?? "").Trim();
            if (name.Length == 0 || resolved != name || !IsPlainName(name) ||
                !m_localTypes.TryGetValue(name, out var localType) || localType != "bool")
            {
                return resolved;
            }
            if (!IsArithmeticPosition(before) && !IsArithmeticPosition(after))
            {
                return resolved;
            }
            return "(" + name + "?1:0)";
        }

        static string AsDoubleNextToDivision(string resolved, string original, char before, char after)
        {
            bool division = before == '/' || after == '/';
            // An int-typed argument is a C# int, so int/int would truncate here too -- and
            // "*", "+" and "-" would wrap around: "n * n * n" for 100000 came back as
            // -1530494976, where the interpreter, computing in doubles, has 10^15.
            if ((division || before == '*' || after == '*' || before == '+' || after == '+' ||
                 before == '-' || after == '-') &&
                (resolved.StartsWith(INT_VAR_ARG, StringComparison.InvariantCulture) ||
                 resolved.StartsWith(INT_ARRAY_ARG, StringComparison.InvariantCulture)))
            {
                return "(double)(" + resolved + ")";
            }
            if (!division)
            {
                return resolved;
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
        /// <summary>
        /// Whether the operand is a collection this function holds -- a local assigned a
        /// literal, or an argument declared list/map. Only usable once statements are being
        /// processed: m_collectionLocals is filled by TryBuildLiteralAssignment, which runs
        /// after the collecting passes.
        /// </summary>
        bool IsCollectionOperand(string operand)
        {
            var name = (operand ?? "").Trim();
            return IsPlainName(name) &&
                   (m_collectionLocals.Contains(name) || m_collectionArgs.Contains(name));
        }

        /// <summary>
        /// Whether the expression joins a collection with "+". CSCS concatenates the two
        /// collections' text there rather than merging them, so the value is text and the
        /// local receiving it cannot be a double.
        /// </summary>
        bool JoinsCollections(string expression)
        {
            var parts = SplitTopLevelOn(expression ?? "", "+");
            return parts.Count > 1 && parts.Any(part => IsCollectionOperand(part));
        }

        static bool YieldsBool(string expression)
        {
            bool inQuotes = false;
            for (int i = 0; i < expression.Length; i++)
            {
                var ch = expression[i];
                if (ch == '"' && !IsEscapedQuote(expression, i))
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
                // Keep whatever followed the subscript. Overwriting the suffix dropped it:
                // "words[i].Length" came back as "words[i]", so an assignment from it stored
                // the element instead of its length.
                suffix = arrayArg + suffix;
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

            // Split at the top level only: a plain Split(',') tore a nested call apart, so
            // "Math.Pow(2, Math.Pow(3, n))" lost the inner argument.
            var tokens = SplitTopLevel(rest, ',');
            int count = 0;
            foreach (var item in tokens)
            {
                result += ProcessStatement(item, "", false);
                count++;
                result += count != tokens.Count ? ',' : ')';
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

            // "!(x == 3)": a group negated, not a call of a function called "!" -- which the
            // interpreter was asked to find ("Couldn't find variable [!]").
            if (functionName.Trim() == "!" && paramStart >= 0 && callEnd > paramStart)
            {
                result += "!(" + ReplaceArgsInString(restStr.Substring(paramStart + 1, callEnd - paramStart - 1)) + ")";
                if (callEnd + 1 < restStr.Length)
                {
                    result += ReplaceArgsInString(restStr.Substring(callEnd + 1));
                }
                return;
            }

            // A global carrying the parenthesis that closes the condition around it --
            // "gcount)" is the last token of "if (1 == gcount)", because the tokenizer splits
            // on "==" and leaves the ")" attached to the right-hand operand. With no "(" of
            // its own the whole token was taken as a function name and emitted as a call:
            // new ParserFunction(..., "gcount)", '(') plus a hoisted temp, which left the
            // condition missing its ")" -- CS1026. Only this shape: a real call always has a
            // "(" of its own (paramStart >= 0), and a name the interpreter knows as a function
            // is left to the call path. Relational operators never get here, which is why
            // "1 > gcount" always compiled while "1 == gcount" did not.
            if (paramStart < 0 && functionName.TrimEnd().EndsWith(")"))
            {
                var globalName = SplitClosingParens(functionName.Trim(), out string globalClosing);
                if (globalClosing.Length > 0 && IsPlainName(globalName) &&
                    m_parentScript.InterpreterInstance.GetFunction(globalName) == null &&
                    IsInterpreterVariable(globalName))
                {
                    m_usesInterpreter = true;
                    result += "__interpreter.GetVariableValue(\"" + globalName + "\")" +
                              globalClosing;
                    return;
                }
            }
            // The same shape for a name the interpreter does not hold yet -- a global the script
            // assigns later, "if (count < total)" -- which goes to the interpreter by name, as
            // the interpreted function would look it up when it runs. The ")" belongs to the
            // condition; glued to the name it was lost from the condition (CS1026).
            string closingAfterName = "";
            if (paramStart < 0 && functionName.TrimEnd().EndsWith(")"))
            {
                var bareName = SplitClosingParens(functionName.Trim(), out string nameClosing);
                // Read in place, by name (CscsLate.Value takes the route the callback took): the
                // callback's statements went where the condition stands, which for an "elif" --
                // "} elif (p == ps) {" -- is between the "}" and the "else if" (CS8641).
                if (nameClosing.Length > 0 && IsLateBoundName(bareName, false))
                {
                    m_usesInterpreter = true;
                    result += "CscsLate.Value(__interpreter, \"" + bareName + "\")" + nameClosing;
                    return;
                }
                if (nameClosing.Length > 0 && IsPlainName(bareName) && !m_paramMap.ContainsKey(bareName) &&
                    !m_newVariables.Contains(bareName))
                {
                    functionName = bareName;
                    closingAfterName = nameClosing;
                }
            }
            // "rows.Size" with rows a name nothing defines yet, where an expression is being built
            // as statements -- a loop's condition: the member of the value found when it runs.
            if (paramStart < 0)
            {
                var dotted = SplitClosingParens(functionName.Trim(), out string dottedClosing);
                int lateDot = dotted.IndexOf('.');
                if (lateDot > 0 && dotted.IndexOf('.', lateDot + 1) < 0)
                {
                    var lateOwner = dotted.Substring(0, lateDot);
                    var lateMember = dotted.Substring(lateDot + 1);
                    if (IsPlainName(lateMember) && IsVariableMember(lateMember) && IsLateBoundName(lateOwner, false))
                    {
                        m_usesInterpreter = true;
                        result += "CscsLate.Value(__interpreter, \"" + lateOwner + "\")." +
                                  CanonicalVariableMember(lateMember) + dottedClosing;
                        return;
                    }
                }
            }
            // "lvs[i])" -- an element of a global, the condition's ")" glued on, as in
            // "if (arg == lvs[i])": the name went to the interpreter as "lvs[i])". The element of
            // the value, read by name (late when nothing holds the name yet), and the ")" back.
            if (paramStart < 0 && functionName.TrimEnd().EndsWith(")"))
            {
                var element = SplitClosingParens(functionName.Trim(), out string elementClosing);
                int bracket = element.IndexOf('[');
                if (elementClosing.Length > 0 && bracket > 0 && element.EndsWith("]") &&
                    FindMatchingBracket(element, bracket) == element.Length - 1)
                {
                    var baseName = element.Substring(0, bracket);
                    bool known = IsPlainName(baseName) && IsInterpreterVariable(baseName) &&
                                 m_parentScript.InterpreterInstance.GetFunction(baseName) == null;
                    bool late = !known && IsLateBoundName(baseName, false);
                    if (known || late)
                    {
                        m_usesInterpreter = true;
                        result += "CscsLate.Element(" +
                                  (late ? "CscsLate.Value(__interpreter, \"" : "__interpreter.GetVariableValue(\"") +
                                  baseName + "\"), (object)(" +
                                  ReplaceArgsInString(element.Substring(bracket + 1, element.Length - bracket - 2)) +
                                  "))" + elementClosing;
                        return;
                    }
                }
            }
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
            trailing = closingAfterName + trailing;

            string token = "";

            // "p.Sum()" on a local holding a class instance. Running a method needs an
            // argument list, so it becomes a call to the helper rather than a member read.
            // Handled here as well as in the expression path: a string anywhere in the
            // statement sends it through this loop instead.
            if (paramStart >= 0 && callEnd >= 0 && IsInstanceMethodCall(functionName))
            {
                int methodDot = functionName.IndexOf('.');
                var callInner = restStr.Substring(paramStart + 1, callEnd - paramStart - 1);
                var built = "Variable.CallMethod(" + functionName.Substring(0, methodDot).Trim() +
                            ",\"" + functionName.Substring(methodDot + 1).Trim() + "\"";
                foreach (var callArg in SplitTopLevel(callInner, ','))
                {
                    if (!string.IsNullOrWhiteSpace(callArg))
                    {
                        built += "," + ReplaceArgsInString(callArg);
                    }
                }
                built += ")";
                // "p.Kid().v": the member that follows reads from the call's result.
                var afterCall = (trailing ?? "").Trim();
                // "p.Kid().Kid().v": a further method call runs on what the previous one returned.
                // It was appended verbatim, and C# looked for a "Kid" on Variable (CS1061). Each
                // one wraps the result so far the same way the first call was built. A Variable,
                // string or collection member is not a class method, so it ends the loop.
                while (afterCall.StartsWith("."))
                {
                    int nameEnd = 1;
                    while (nameEnd < afterCall.Length &&
                           (char.IsLetterOrDigit(afterCall[nameEnd]) || afterCall[nameEnd] == '_'))
                    {
                        nameEnd++;
                    }
                    if (nameEnd == 1 || nameEnd >= afterCall.Length || afterCall[nameEnd] != '(')
                    {
                        break;
                    }
                    var nextMethod = afterCall.Substring(1, nameEnd - 1);
                    if (IsVariableMember(nextMethod) || IsMappedStringMember(nextMethod) ||
                        IsCollectionMethod(nextMethod))
                    {
                        break;
                    }
                    int nextClose = FindMatchingParen(afterCall, nameEnd);
                    if (nextClose < 0)
                    {
                        break;
                    }
                    var nextInner = afterCall.Substring(nameEnd + 1, nextClose - nameEnd - 1);
                    built = "Variable.CallMethod(" + built + ",\"" + nextMethod + "\"";
                    foreach (var nextArg in SplitTopLevel(nextInner, ','))
                    {
                        if (!string.IsNullOrWhiteSpace(nextArg))
                        {
                            built += "," + ReplaceArgsInString(nextArg);
                        }
                    }
                    built += ")";
                    afterCall = afterCall.Substring(nextClose + 1).Trim();
                    trailing = afterCall;
                }
                // "p.Kid().tag" and "p.Kid().tag.Upper": a path of members after the call. Only a
                // single name was accepted, and a second segment reached C# verbatim (CS1061).
                if (afterCall.StartsWith(".") && afterCall.Substring(1).Split('.').All(IsPlainName))
                {
                    var chained = BuildMemberChain(built, afterCall.Substring(1));
                    if (chained != null)
                    {
                        result += chained;
                        return;
                    }
                }
                result += built +
                          (string.IsNullOrEmpty(trailing) ? "" : ReplaceArgsInString(trailing));
                return;
            }

            if (ProcessArray(argsStr, functionName, ref token))
            {
                result += token;
                return;
            }

            // "a.Add(x)" on a collection built in this function: AddVariable is what the
            // interpreter itself calls, and this is usually inside a loop, so the callback it
            // replaces was being paid on every iteration.
            int addDot = functionName.IndexOf('.');
            // The raw text between the parentheses, not argsStr: that one arrives escaped for
            // embedding in a C# string literal, and re-running it through ReplaceArgsInString
            // mangles the backslashes. Quoted arguments used to be handed to the interpreter
            // for that reason, but the callback appends to the interpreter's own copy of the
            // collection while the compiled code reads the local one -- so "a.Add(new N(1,
            // \"x\"))" quietly added nothing here.
            if (addDot > 0 && paramStart >= 0 && callEnd > paramStart + 1 &&
                m_collectionLocals.Contains(functionName.Substring(0, addDot)) &&
                functionName.Substring(addDot + 1).Trim().ToLower() == "add")
            {
                var addInner = restStr.Substring(paramStart + 1, callEnd - paramStart - 1);
                // A brace literal argument -- a.Add({1,2}) -- is already a collection, so it
                // builds directly rather than going through ConvertToVariable.
                string added;
                if (!TryBuildArrayLiteral(addInner, out added))
                {
                    // A CSCS call as the argument becomes several statements plus a
                    // temporary, hoisted ahead of the Add. The token loop flushes the prelude.
                    added = "Variable.ConvertToVariable(" +
                        ReplaceArgsInString(HoistConditionCalls(addInner, addInner, true)) + ")";
                }
                result += functionName.Substring(0, addDot) + ".AddVariable(" + added + ")";
                return;
            }

            // "s.Upper" arrives here as a function name with no arguments; mapping it to a
            // direct C# call avoids a round trip through the interpreter for every use.
            // A call is not a property read even when it takes no arguments: "p.Sum()" has
            // to reach the interpreter callback further down, which can run the method.
            int memberDot = paramStart >= 0 ? -1 : functionName.IndexOf('.');
            if (memberDot > 0 && string.IsNullOrEmpty(argsStr))
            {
                var owner = functionName.Substring(0, memberDot);
                // The same trailing ")" as in ProcessToken: "s.Lower)" closing a condition.
                var member = SplitClosingParens(functionName.Substring(memberDot + 1), out string closingParens);
                // Arguments first, where the type is declared; otherwise a local, which is
                // attempted on the same reasoning as MapStringMember: a non-string local
                // simply fails to compile and the function falls back.
                // A local holding a Variable keeps the script's spelling: Variable has the
                // members under those names, while the C# string mapping would emit
                // "v.ToUpper()", which it does not have.
                if (IsVariableMember(member) && m_variableLocals.Contains(owner))
                {
                    result += owner + "." + CanonicalVariableMember(member);
                    return;
                }

                // ".Type" on a primitive owner, as on the expression path above: the answer is
                // the CSCS type's name, which a C# double, string or bool cannot supply.
                if (TryMapTypeMember(owner, member, out var typeToken))
                {
                    result += typeToken + closingParens;
                    return;
                }

                // A field on a class instance, read through the interpreter's own property
                // lookup: a Variable has no member named after the script's field.
                if (m_variableLocals.Contains(owner))
                {
                    var chain = BuildMemberChain(owner, member);
                    if (chain != null)
                    {
                        result += chain;
                        return;
                    }
                }
                if (ProcessStringMember(owner, member, ref token) ||
                    (m_newVariables.Contains(owner) && !m_collectionLocals.Contains(owner) &&
                     !m_variableLocals.Contains(owner) &&
                     MapStringMember(owner, member, ref token)))
                {
                    result += token + closingParens;
                    return;
                }
            }

            var tryCSharp = GetCSharpFunction(functionName, argsStr);
            if (!string.IsNullOrEmpty(tryCSharp))
            {
                // Whatever followed the call is still part of the statement: dropping it lost
                // the ")" closing "while (i < int(s))", and the condition did not compile.
                result += tryCSharp + (string.IsNullOrEmpty(trailing) ? "" : ReplaceArgsInString(trailing)) + "\n";
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
                    if (IsMapEntry(element))
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
                            BuildLiteralElement(pair[1]) + ");");
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
                        // An element may itself be a literal -- {{1,2},{3,4}} -- so it is built
                    // rather than handed to ReplaceArgsInString, which emits braces verbatim.
                    items.Add(BuildLiteralElement(element));
                    }
                    sb.AppendLine(m_depth + VARIABLE_TEMP_VAR + " = new Variable(new List<Variable> { " +
                        string.Join(", ", items) + " });");
                }
                EmitCallResult(tokens, sb.ToString(), trailing, ref result, ref newVarAdded);
                return;
            }

            // "garr[0]" on a global: the subscript is not part of the name. Asking the
            // interpreter for a variable called "garr[0]" returned the whole collection, so
            // "return garr[0];" answered [4,1,3] where the interpreter answers 4. Only this
            // shape reaches here -- a subscript inside a larger expression is resolved by
            // ResolveToken, which has always applied it to the value.
            int bracketAt = functionName.IndexOf('[');
            if (bracketAt > 0 && functionName.EndsWith("]"))
            {
                var baseName = functionName.Substring(0, bracketAt).Trim();
                // A global the script defines only later -- "glist[n]" -- the same way, read by
                // name when the code runs. Through the call route the interpreter looked the
                // whole "glist[n]" up, and could not see the compiled function's "n".
                bool lateBase = IsPlainName(baseName) && !IsInterpreterVariable(baseName) &&
                                IsLateBoundName(baseName, false);
                var baseRead = lateBase ? "CscsLate.Value(__interpreter, \"" + baseName + "\")" :
                                          "__interpreter.GetVariableValue(\"" + baseName + "\")";
                if (IsPlainName(baseName) && (lateBase || IsInterpreterVariable(baseName)) &&
                    !m_paramMap.ContainsKey(baseName) && !m_newVariables.Contains(baseName) &&
                    !m_collectionLocals.Contains(baseName) && !m_variableLocals.Contains(baseName))
                {
                    var subscripts = "";
                    int at = bracketAt;
                    while (at < functionName.Length && functionName[at] == '[')
                    {
                        int close = FindMatchingBracket(functionName, at);
                        if (close < 0)
                        {
                            subscripts = null;
                            break;
                        }
                        subscripts += ", (object)(" +
                            ReplaceArgsInString(functionName.Substring(at + 1, close - at - 1)) + ")";
                        at = close + 1;
                    }
                    // The element as the interpreter takes it (CscsLate.Element, ExtractArrayElement):
                    // an index past the end is its error, where Variable's indexer answered quietly.
                    var elementRead = subscripts == null ? null : "CscsLate.Element(" + baseRead + subscripts + ")";
                    if (subscripts != null && at == functionName.Length && m_statementInlineCalls)
                    {
                        // Where "&&", "||" or "?:" decides whether it is read: in place (see below).
                        m_usesInterpreter = true;
                        result += ReadCallResult(elementRead, tokens, trailing);
                        if (!string.IsNullOrEmpty(trailing))
                        {
                            result += ReplaceArgsInString(trailing);
                        }
                        return;
                    }
                    if (subscripts != null && at == functionName.Length)
                    {
                        m_usesInterpreter = true;
                        EmitCallResult(tokens,
                            m_depth + VARIABLE_TEMP_VAR + " = " + elementRead + ";\n",
                            trailing, ref result, ref newVarAdded);
                        return;
                    }
                }
                // "x[x.Size - 1]" on a text or untyped argument, or on a local holding text or a
                // Variable: an index with a member or a call in it came here whole, and went to
                // the interpreter as one name -- which could not see the argument's own member
                // ("Couldn't find variable [x.size]"), and later answered the whole value. Read in
                // C# instead, each index translated, the element as the interpreter takes it.
                string ownValue = null;
                if (IsPlainName(baseName) && m_paramMap.TryGetValue(baseName, out var mappedArg) &&
                    m_argsMap.TryGetValue(baseName, out var argType) &&
                    (argType.Type == Variable.VarType.STRING || argType.Type == Variable.VarType.VARIABLE))
                {
                    ownValue = mappedArg;
                }
                else if (IsPlainName(baseName) && !m_paramMap.ContainsKey(baseName) &&
                         (m_variableLocals.Contains(baseName) || m_collectionLocals.Contains(baseName) ||
                          (m_localTypes.TryGetValue(baseName, out var ownType) && ownType == "string")))
                {
                    ownValue = baseName;
                }
                if (ownValue != null)
                {
                    var ownSubscripts = "";
                    int at = bracketAt;
                    while (at < functionName.Length && functionName[at] == '[')
                    {
                        int close = FindMatchingBracket(functionName, at);
                        if (close < 0)
                        {
                            ownSubscripts = null;
                            break;
                        }
                        ownSubscripts += ", (object)(" +
                            ReplaceArgsInString(functionName.Substring(at + 1, close - at - 1)) + ")";
                        at = close + 1;
                    }
                    var ownRead = "CscsLate.Element(Variable.ConvertToVariable(" + ownValue + ")" + ownSubscripts + ")";
                    if (ownSubscripts != null && at == functionName.Length && m_statementInlineCalls)
                    {
                        // Where "&&", "||" or "?:" decides whether it is read: in place.
                        result += ReadCallResult(ownRead, tokens, trailing);
                        if (!string.IsNullOrEmpty(trailing))
                        {
                            result += ReplaceArgsInString(trailing);
                        }
                        return;
                    }
                    if (ownSubscripts != null && at == functionName.Length)
                    {
                        EmitCallResult(tokens, m_depth + VARIABLE_TEMP_VAR + " = " + ownRead + ";\n",
                            trailing, ref result, ref newVarAdded);
                        return;
                    }
                }
            }

            // A name read the same way -- "i > 0 && prev < 5" -- is made where it stands too.
            // Hoisted, it was read whether or not the operator reached it, and "prev", not
            // assigned yet at i = 0, failed where the interpreter never looks it up.
            // CscsLate.Value is the route the hoisted read took, by name through the interpreter.
            var bareRead = functionName.Trim();
            if (m_statementInlineCalls && paramStart < 0 && IsPlainName(bareRead) &&
                !char.IsDigit(bareRead[0]) && !m_scriptInCSharp)
            {
                m_usesInterpreter = true;
                result += ReadCallResult("CscsLate.Value(__interpreter, \"" + bareRead + "\")", tokens, trailing);
                if (!string.IsNullOrEmpty(trailing))
                {
                    result += ReplaceArgsInString(trailing);
                }
                return;
            }

            // Inside "&&", "||" or "?:", or a loop's condition, the call has to run where it
            // stands. Hoisted ahead of the statement it ran whether or not the operator would
            // have reached it -- "n > 5 && f(n) > 1" called f for n = 1 -- and ahead of a
            // loop only once, so "while (i < 10 && f(i) < n)" tested a stale value.
            // A call named like an argument or local of this function, case aside -- "ShowView(v,
            // showView)" -- is made inline too: the call route publishes that argument under its
            // name, and the interpreter's lookup of "ShowView" then found the value instead of the
            // function. CscsCalls.Call looks among functions only, as a call is looked up.
            bool shadowedCall = paramStart >= 0 && IsPlainName(functionName.Trim()) &&
                (m_paramMap.ContainsKey(functionName.Trim()) || m_assignedAnywhere.Contains(functionName.Trim()));
            if ((m_statementInlineCalls || shadowedCall) && paramStart >= 0 && callEnd > paramStart &&
                TryInlineScriptCall(functionName, restStr.Substring(paramStart + 1, callEnd - paramStart - 1),
                                    out string inlineCall))
            {
                result += ReadCallResult(inlineCall, tokens, trailing);
                if (!string.IsNullOrEmpty(trailing))
                {
                    result += ReplaceArgsInString(trailing);
                }
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

            m_nameWithoutCall = paramStart < 0;
            try
            {
                sb.AppendLine(GetCSCSFunction(argsStr, functionName, ch));
            }
            finally
            {
                m_nameWithoutCall = false;
            }

            token = sb.ToString();
            EmitCallResult(tokens, token, trailing, ref result, ref newVarAdded);
        }

        /// <summary>
        /// A condition that is just a value rather than a comparison -- "if (a[0])" -- reads a
        /// Variable, which C# cannot use as a bool. CSCS treats a value as true when its
        /// number is not zero, and nothing else: even the string "true" is false there, which
        /// is why this compares the number rather than calling AsBool.
        /// </summary>
        string AsCondition(string condition)
        {
            var trimmed = condition.Trim();
            if (trimmed.Length < 3 || trimmed[0] != '(' ||
                FindMatchingParen(trimmed, 0) != trimmed.Length - 1)
            {
                return condition;
            }

            var inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
            var asBool = AsBoolExpression(inner);
            return asBool == null ? condition : "(" + asBool + ")";
        }

        /// <summary>
        /// The condition with every term that is a bare Variable read as a number, since C# has
        /// no truth value for one -- "r = a[0] == \"x\"; if (r)" leaves a local holding the
        /// comparison's result, and "!r" and "r &amp;&amp; n &lt; 5" are the same term in a larger
        /// condition. Null when there is no such term and the condition can stand as it is.
        /// </summary>
        string AsBoolExpression(string text)
        {
            // Each side of && and || is its own condition.
            foreach (var connective in new[] { "&&", "||" })
            {
                var clauses = SplitTopLevelOn(text, connective);
                if (clauses.Count > 1)
                {
                    bool any = false;
                    var pieces = new List<string>();
                    foreach (var clause in clauses)
                    {
                        var piece = AsBoolExpression(clause);
                        any |= piece != null;
                        pieces.Add(piece ?? clause);
                    }
                    // No spaces: the statement arrived with its whitespace stripped.
                    return any ? string.Join(connective, pieces) : null;
                }
            }

            var term = text.Trim();
            bool negated = term.StartsWith("!");
            if (negated)
            {
                term = term.Substring(1).Trim();
            }
            if (term.Length > 1 && term[0] == '(' &&
                FindMatchingParen(term, 0) == term.Length - 1)
            {
                var grouped = AsBoolExpression(term.Substring(1, term.Length - 2));
                return grouped == null ? null : (negated ? "!" : "") + "(" + grouped + ")";
            }
            // A script call made in place ("n > 1 && f(n)") yields a Variable, which C# cannot
            // use as a side of "&&" (CS0019) -- tested before the operator scan below, since the
            // call's own arguments may hold a comparison.
            if (term.StartsWith("CscsCalls.Call(", StringComparison.Ordinal) &&
                FindMatchingParen(term, term.IndexOf('(')) == term.Length - 1)
            {
                return "CscsConvert." + (negated ? "IsFalse(" : "IsTrue(") + term + ")";
            }
            // Anything with an operator left in it already yields a bool.
            if (term.IndexOfAny(new[] { '<', '>', '=', '!', '&', '|' }) >= 0)
            {
                return null;
            }
            if (!term.EndsWith("]") && !IsVariableMemberTerm(term) && !(IsPlainName(term) &&
                (m_variableLocals.Contains(term) || IsStringOperand(term) ||
                 (m_localTypes.TryGetValue(term, out var termType) && termType == "string"))))
            {
                return null;
            }
            // The interpreter's one truth rule (Variable.IsTrue): a number unless 0, text unless
            // empty, "0" or "false", a list unless empty -- not AsDouble(), which reads "abc" as 0.
            // The object overloads accept a string term as well as a Variable.
            return "CscsConvert." + (negated ? "IsFalse(" : "IsTrue(") + term + ")";
        }

        /// <summary>Whether the text begins with this keyword as a whole word.</summary>
        static bool StartsWithKeyword(string text, string keyword)
        {
            var trimmed = text.TrimStart();
            return trimmed.StartsWith(keyword, StringComparison.Ordinal) &&
                (trimmed.Length == keyword.Length || !char.IsLetterOrDigit(trimmed[keyword.Length]));
        }

        /// <summary>
        /// Replaces CSCS calls in an "if" condition with a temporary, emitting the call ahead
        /// of the statement. A call generates statements, which cannot sit inside a condition,
        /// and the condition path resolves argument names without converting calls -- so
        /// "if (helper(n) > 5)" emitted a bare "helper" that does not compile.
        ///
        /// Only when the condition is a single clause. Hoisting out of "g() &amp;&amp; h()"
        /// would call h() even when g() is false, which the interpreter does not do.
        /// </summary>
        string HoistConditionCalls(string condition, string wholeStatement,
                                   bool asVariable = false)
        {
            // The clause check has to look at the whole statement: the tokenizer splits on the
            // comparison, so the condition arrives here in pieces.
            // Nor out of "?:", where only one branch runs.
            if (SplitTopLevelOn(wholeStatement, "&&").Count > 1 ||
                SplitTopLevelOn(wholeStatement, "||").Count > 1 ||
                NeedsInlineCalls(wholeStatement))
            {
                return condition;
            }

            var interpreter = m_parentScript.InterpreterInstance;
            var sb = new StringBuilder();
            bool inQuotes = false;
            int i = 0;
            while (i < condition.Length)
            {
                char ch = condition[i];
                if (ch == '"' && !IsEscapedQuote(condition, i))
                {
                    inQuotes = !inQuotes;
                    sb.Append(ch);
                    i++;
                    continue;
                }
                if (inQuotes || (!char.IsLetter(ch) && ch != '_'))
                {
                    sb.Append(ch);
                    i++;
                    continue;
                }

                int start = i;
                while (i < condition.Length &&
                       (char.IsLetterOrDigit(condition[i]) || condition[i] == '_'))
                {
                    i++;
                }
                var name = condition.Substring(start, i - start);

                int close = i < condition.Length && condition[i] == '(' ?
                    FindMatchingParen(condition, i) : -1;
                if (close < 0 || m_paramMap.ContainsKey(name) || m_newVariables.Contains(name) ||
                    Constants.RESERVED.Contains(name) || IsMathFunction(name, out _) ||
                    !string.IsNullOrEmpty(GetCSharpFunction(name, "0")) ||
                    // A member call is never hoisted: "s.Contains(\"BC\")" and "a.Contains(2)"
                    // compile as members already, and hoisting them as though they were bare
                    // calls is what cost fourteen constructs when this was widened before.
                    // The interpreter also answers the two differently -- "Contains(a, 2)" is
                    // 0 where "a.Contains(2)" is 1 -- so the distinction is semantic, not just
                    // a matter of spelling.
                    (start > 0 && condition[start - 1] == '.') ||
                    // A function the interpreter knows: one a script defined, or a built-in
                    // registered in C#. A built-in's value is read as a truth value below,
                    // since "if (StrEqual(s, \"AB\"))" wants one where ".AsDouble()" gives a
                    // number that C# will not accept as a condition. The function being
                    // translated is not registered yet, and is known by its name:
                    // "if (f(n - 1) > 4)" inside f went out as a C# call to f.
                    (interpreter.GetFunction(name) == null &&
                     !string.Equals(name, m_functionName, StringComparison.OrdinalIgnoreCase) &&
                     !IsLateFunctionName(name)))
                {
                    sb.Append(name);
                    continue;
                }

                var callText = condition.Substring(i, close - i + 1);
                var tmpScript = new ParsingScript(interpreter, callText);
                var argsStr = Utils.PrepareArgs(Utils.GetBodyBetween(tmpScript));
                var tempName = VARIABLE_TEMP_VAR + (++m_tempVarId);
                m_statementPrelude += m_depth + ACTION_TEMP_VAR + " =\"\";\n" +
                    GetCSCSFunction(argsStr, name) +
                    m_depth + "Variable " + tempName + " = " + VARIABLE_TEMP_VAR + ";\n";
                // A condition is numeric, so the call's value is read as a number there. A
                // value being stored into an element is not: it keeps the Variable, so that
                // "a[0] = f(n)" stores whatever f returned rather than a number.
                // Compared with a string, or handed to SameValue, it has to stay a Variable too:
                // read as a number, a string result was 0 against "s".
                // The call is the whole condition -- "if (StrEqual(s, \"AB\"))",
                // "if (helper(n))" -- so its value is the test itself. C# has no truth value
                // for a Variable or for the double ".AsDouble()" gives, and the interpreter
                // counts a non-zero number as true, which is what IsTrue answers.
                var afterCall = close + 1 < condition.Length ?
                    condition.Substring(close + 1).Trim() : "";
                var beforeCall = condition.Substring(0, start).Trim();
                // The call is the whole test when nothing but grouping surrounds it. The
                // condition arrives already inside its own parenthesis -- "(StrEqual(s,\"AB\"))"
                // with the name at index 1 -- so testing for index 0 matched nothing. A
                // condition that continues past the call ("if(helper(n)" with ">5" still to
                // come in the whole statement) keeps reading the value as a number.
                // Nothing but grouping around the call, and no comparison anywhere in the
                // statement. The tokenizer splits on a comparison, so "if (f(n) < \"t\")"
                // arrives here as "(f(n)" with the "< \"t\"" nowhere in sight -- measuring
                // this text cannot see it, and wrapping the value as a truth value then put a
                // bool where a string comparison wanted the string. m_statementRelational is
                // computed for the whole statement before it is torn up, which is what makes
                // the difference visible here. "==" and "!=" never reach this method.
                bool wholeCondition =
                    beforeCall.Trim('(').Length == 0 &&
                    afterCall.Trim(')').Length == 0 &&
                    !m_statementRelational;
                if (wholeCondition && !asVariable)
                {
                    sb.Append("CscsConvert.IsTrue(").Append(tempName).Append(")");
                }
                else if (asVariable || m_statementHasString || wholeStatement.Contains("\"") ||
                    wholeStatement.Contains(SAME_VALUE_CALL))
                {
                    m_newVariables.Add(tempName);
                    sb.Append(tempName);
                }
                else
                {
                    sb.Append(tempName).Append(".AsDouble()");
                }
                i = close + 1;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Places a call's generated statements and its resulting value into the statement
        /// being built: either as the right-hand side of an assignment, as the whole
        /// expression, or hoisted ahead of a larger expression.
        /// </summary>
        void EmitCallResult(List<string> tokens, string token, string trailing,
                            ref string result, ref bool newVarAdded)
        {
            // Only when the call is the last thing assigned. Followed by more -- "x = f(n) * 2" --
            // this ended the statement after the call and left "*2;" on a line of its own; the
            // hoisting branch below keeps the rest of the expression attached.
            if (tokens.Count >= 3 && tokens[1] == "=" && !string.IsNullOrWhiteSpace(result) &&
                m_tokenId >= tokens.Count - 1)
            {
                var type = GetTokenType(tokens);
                var last = type == "string" ? VARIABLE_TEMP_VAR + ".AsString()" : VARIABLE_TEMP_VAR;
                // The local now holds the Variable the call returned, so a member on it is one
                // of Variable's own: "r = Regex(...); r.size" went out with the script's
                // spelling, and "r.contains(..)" took the string mapping and became
                // ContainsCscs, which a Variable has no trace of.
                if (last == VARIABLE_TEMP_VAR && IsPlainName(tokens[0].Trim()))
                {
                    m_variableLocals.Add(tokens[0].Trim());
                }
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
                // "+" is the one operator whose meaning depends on the operand types, and a
                // callback's type is not known until it runs -- so hand the Variable itself to
                // Variable's own "+", which applies the interpreter's rule, instead of
                // committing to AsDouble() or AsString() here and getting it wrong. Every
                // other context is numeric in CSCS, so the conversion still applies there.
                // A subscript after the call indexes what it returned, so the Variable itself
                // has to carry it: converting first read "f(x)[1]" as the second *character*
                // of the result's text -- "t.Split(\",\")[1]" came back as a quote mark.
                result += ReadCallResult(tempName, tokens, trailing);
                if (!string.IsNullOrEmpty(trailing))
                {
                    result += ReplaceArgsInString(trailing);
                }
            }
        }

        /// <summary>
        /// Maps a CSCS built-in onto C#. The conversions return an *expression*, not a
        /// statement: they used to come back as "new Variable(Convert.ToInt32(x));", which
        /// spliced a statement into the middle of an expression, so "int(n) + 1" emitted a
        /// dangling "+1" and dropped it. printc stays a statement because it returns
        /// nothing.
        /// </summary>
        static readonly HashSet<string> s_conversions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "string", "int", "long", "bool", "double" };

        /// <summary>
        /// A conversion's argument in C#. One with a call in it -- "int(Math.Sqrt(n))" -- is an
        /// expression: the token loop EvaluateToken uses is built for statements, and spliced
        /// an interpreter callback into the middle of the conversion. A script call still
        /// needs that callback, so only calls the expression path resolves itself go there.
        /// </summary>
        string ConversionArgument(string arguments)
        {
            var unescaped = arguments.Replace("\\\"", "\"");
            // More than one argument: "string(d, \"yyyy/MM/dd\")" passes a format on. Each is
            // converted on its own -- handed over whole, the list went through the token loop
            // as a single statement, which resolved neither the value nor the format.
            var parts = SplitTopLevel(unescaped, ',');
            if (parts.Count > 1)
            {
                return string.Join(", ", parts.Select(part => ConversionArgument(part.Trim())));
            }
            if (unescaped.IndexOf('(') > 0 && !MentionsScriptCall(unescaped))
            {
                return ReplaceArgsInString(unescaped);
            }
            // The argument is a script call and nothing else -- "int(helper(n))": an inline
            // call gives the Variable, which the conversion takes as it is.
            var trimmedArg = unescaped.Trim();
            int argOpen = trimmedArg.IndexOf('(');
            if (argOpen > 0 && FindMatchingParen(trimmedArg, argOpen) == trimmedArg.Length - 1 &&
                TryInlineScriptCall(trimmedArg.Substring(0, argOpen),
                                    trimmedArg.Substring(argOpen + 1, trimmedArg.Length - argOpen - 2), out var argCall))
            {
                return argCall;
            }
            // A name nothing defines yet -- "int(id)" with id a global assigned later: read when
            // the code runs. EvaluateToken made it a call, statements in the middle of the cast.
            if (IsLateBoundName(trimmedArg, false))
            {
                m_usesInterpreter = true;
                return "CscsLate.Value(__interpreter, \"" + trimmedArg + "\")";
            }
            // An expression with such a name in it -- "int(duration / 500)": the expression
            // path reads the name late; the token loop spliced a callback into the cast.
            if (!MentionsScriptCall(unescaped) &&
                System.Text.RegularExpressions.Regex.Matches(WithoutStringContents(unescaped), @"(?<![\w.])[A-Za-z_]\w*(?![\w(])")
                    .Cast<System.Text.RegularExpressions.Match>().Any(word => IsLateBoundName(word.Value, false)))
            {
                return ReplaceArgsInString(unescaped);
            }
            // Unescaped: with "\\\"" still in it the tokenizer never saw the literal as
            // quoted -- its quote test skips one preceded by a backslash -- and split
            // "yyyy/MM/dd" on the slashes, leaving "MM" to be read as a name of its own.
            return EvaluateToken(unescaped);
        }

        string GetCSharpFunction(string functionName, string arguments = "")
        {
            // Script names are case-blind: "Double(x)" is double(x) to the interpreter.
            functionName = functionName ?? "";
            var lowerName = functionName.ToLowerInvariant();
            if (lowerName == "printc")
            {
                arguments = ReplaceArgsInString(arguments.Replace("\\\"", "\""));
                return "Console.WriteLine(" + arguments + ");";
            }
            else if (lowerName == "string" && !string.IsNullOrWhiteSpace(arguments))
            {
                return "CscsConvert.ToText(" + ConversionArgument(arguments) + ")";
            }
            else if (lowerName == "int" && !string.IsNullOrWhiteSpace(arguments))
            {
                // A cast, not Convert.ToInt32: CSCS truncates toward zero -- int(3.7) is 3,
                // int(2.5) is 2, int(-3.7) is -3 -- while Convert rounds. Going through
                // ToDouble first keeps int("5.7") working.
                return "(int)CscsConvert.ToNumber(" + ConversionArgument(arguments) + ")";
            }
            else if (lowerName == "long" && !string.IsNullOrWhiteSpace(arguments))
            {
                return "(long)CscsConvert.ToNumber(" + ConversionArgument(arguments) + ")";
            }
            else if (lowerName == "bool" && !string.IsNullOrWhiteSpace(arguments))
            {
                return "CscsConvert.ToFlag(" + ConversionArgument(arguments) + ")";
            }
            else if (lowerName == "double" && !string.IsNullOrWhiteSpace(arguments))
            {
                return "CscsConvert.ToNumber(" + ConversionArgument(arguments) + ")";
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

        string GetCSCSFunction(string argsStr, string functionName, char ch = '(',
                               string assignTo = null)
        {
            // The "**" rewrite is no function the interpreter knows; a callback to it would fail
            // at run time, so the function stays interpreted instead.
            if (string.Equals(functionName, POWER_OP, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("A power over a Variable reached the interpreter by name");
            }
            // A plain call with plain arguments goes straight to the function: its arguments
            // are evaluated here, in C#, and handed to its Run -- the one an interpreted call
            // reaches too, with the same argument frame, conversions and call-depth guard.
            // The text route below publishes the caller's arguments, builds the argument list
            // as a string, and has the interpreter parse it and find the function by name on
            // every call: that is what kept a compiled recursive fib only ~4.6x faster.
            // Not for a name read without parentheses ("if (n < total)"): that may be a variable
            // the script defines later, which only the text route resolves.
            if (ch == '(' && string.IsNullOrEmpty(assignTo) && !AsyncMode && !m_nameWithoutCall &&
                IsPlainArgumentList(argsStr) && TryInlineScriptCall(functionName, argsStr, out var direct))
            {
                return m_depth + VARIABLE_TEMP_VAR + " = " + direct + ";\n";
            }
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
                sb.AppendLine(m_depth + "__interpreter.AddCompiledLocalVariable(\"" + param.Key +
                    "\", new GetVarFunction(Variable.ConvertToVariable(" + param.Value + ")));");
            }
            // The argument text goes into a C# string literal and the interpreter parses it back
            // at run time, so it has to arrive as the script wrote it. It comes with every quote
            // escaped (Utils.PrepareArgs) but its backslashes as they were, so a script's own
            // escaped quote -- '{\"Result\": 1}' -- ended the literal early. Undone and escaped
            // properly: backslashes, then quotes.
            if (!string.IsNullOrEmpty(argsStr))
            {
                var original = argsStr.Replace("\\\"", "\"");
                argsStr = original.Replace("\\", "\\\\").Replace("\"", "\\\"");
            }

            // The Size built-in reads a variable's name and then the ")" after it (GetToken,
            // CheckNotEnd): given only "s", it threw "Incomplete arguments for [Size]" where the
            // interpreted call, which sees the ")", answers. It gets the text it would see.
            if (string.Equals(functionName, Constants.SIZE, StringComparison.OrdinalIgnoreCase) &&
                ch == '(' && !(m_parentScript?.InterpreterInstance?.GetFunction(functionName) is CustomFunction))
            {
                argsStr += ")";
            }
            sb.AppendLine(m_depth + ARGS_TEMP_VAR + " =\"" + argsStr + "\";");
            sb.AppendLine(m_depth + SCRIPT_TEMP_VAR + " = new ParsingScript(__interpreter, " + ARGS_TEMP_VAR + ", true);");
            if (!string.IsNullOrEmpty(assignTo))
            {
                // "new" names the instance after the variable it is assigned to, reading it
                // from the script being parsed. Here the script holds only the arguments, so
                // the name has to be handed over or the instance is built with none, which
                // throws inside the constructor.
                sb.AppendLine(m_depth + SCRIPT_TEMP_VAR + ".CurrentAssign = \"" + assignTo + "\";");
            }
            // Not escaped: a name carrying a map key -- gm["k"] -- closes the literal early
            // and the function does not compile, which is what sends it to the interpreter.
            // Escaping it made it compile and then hand the interpreter a name it parses as a
            // call to gm with ["k"] left over, which answered ["k" instead of the value.
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

        bool ProcessArray(string argStr, ref string result, bool callFollows = false)
        {
            int index = argStr.IndexOf('.');
            if (index <= 0)
            {
                return false;
            }
            string arrayName = argStr.Substring(0, index);
            string methodName = argStr.Substring(index + 1);
            if (ProcessStringMember(arrayName, methodName, ref result, callFollows))
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
        bool ProcessStringMember(string name, string member, ref string result,
                                 bool callFollows = false)
        {
            Variable arg;
            if (!m_argsMap.TryGetValue(name, out arg) || arg.Type != Variable.VarType.STRING ||
                !m_paramMap.ContainsKey(name))
            {
                return false;
            }

            var target = m_paramMap[name];
            return MapStringMember(target, member, ref result, callFollows);
        }

        /// <summary>
        /// Maps a CSCS string member onto its C# equivalent for any expression known to be a
        /// C# string. Safe to attempt on a local whose type is not tracked: if it turns out
        /// not to be a string, the generated call does not compile and the function falls
        /// back -- it cannot produce a wrong answer.
        /// </summary>
        /// <summary>
        /// Members Variable provides itself, so a collection local can call them directly in
        /// the generated C# rather than through an interpreter callback -- which yields a
        /// Variable, and "&&" cannot combine one of those with a bool. Only members whose
        /// implementation mirrors the interpreter's are listed; see Variable.Contains.
        /// </summary>
        /// <summary>
        /// The C# spelling of a Variable member a script may have written in any case. CSCS
        /// names are case-insensitive, and the member was emitted exactly as the script spelt
        /// it: "m.keys", "a.sort()" and "a.type" reached C# in lower case and did not compile,
        /// while "m.Keys" did. Anything not a member of Variable is returned untouched -- a
        /// class's own field keeps the case it was declared with.
        /// </summary>
        internal static string CanonicalVariableMember(string member)
        {
            var text = member ?? "";
            int callStart = text.IndexOf('(');
            var name = (callStart < 0 ? text : text.Substring(0, callStart)).Trim();
            var rest = callStart < 0 ? "" : text.Substring(callStart);
            if (name.Length == 0 || !s_variableMembers.TryGetValue(name, out var canonical))
            {
                return member;
            }
            return canonical + rest;
        }

        static readonly Dictionary<string, string> s_variableMembers =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "size", "Size" }, { "keys", "Keys" },
                { "first", "First" }, { "last", "Last" }, { "type", "Type" },
                { "sort", "Sort" }, { "reverse", "Reverse" }, { "split", "Split" }, { "join", "Join" },
                { "length", "Length" }, { "upper", "Upper" }, { "lower", "Lower" },
                { "contains", "Contains" }, { "startswith", "StartsWith" },
                { "endswith", "EndsWith" }, { "indexof", "IndexOf" },
                { "substring", "Substring" }, { "replace", "Replace" },
                { "add", "Add" }, { "addunique", "AddUnique" }, { "remove", "Remove" },
                { "removeat", "RemoveAt" }, { "insert", "Insert" }, { "clear", "Clear" },
            };

        /// <summary>
        /// Whether "(" at <paramref name="callStart"/> is an EMPTY call on a member that is a
        /// property in C#: "a[0].Upper()". The interpreter reads the property and eats the
        /// parentheses itself (Variable.GetCoreProperty), so the value is the property's and the
        /// "()" is dropped -- ".Upper()" would be CS1955. A member that really is a method keeps
        /// its call.
        /// </summary>
        static bool IsEmptyPropertyCall(string text, int callStart, string member)
        {
            return callStart < text.Length && text[callStart] == '(' &&
                   callStart + 1 < text.Length && text[callStart + 1] == ')' &&
                   s_variableProperties.Contains((member ?? "").ToLower());
        }

        /// <summary>The members of that map that are properties in C#, not methods.</summary>
        static readonly HashSet<string> s_variableProperties = new HashSet<string>
        {
            "size", "keys", "first", "last", "type", "length", "upper", "lower",
        };

        /// <summary>
        /// Whether "owner.member" followed by "()" at <paramref name="callStart"/> is an empty
        /// call on a property of a Variable held by a local or a "variable" argument.
        /// </summary>
        bool EmptyPropertyCallOnVariable(string token, string text, int callStart)
        {
            var trimmed = (token ?? "").Trim();
            int dot = trimmed.IndexOf('.');
            if (dot <= 0 || dot != trimmed.LastIndexOf('.'))
            {
                return false;
            }
            var owner = trimmed.Substring(0, dot);
            var member = trimmed.Substring(dot + 1);
            return IsPlainName(owner) && IsPlainName(member) && IsEmptyPropertyCall(text, callStart, member) &&
                   (m_variableLocals.Contains(owner) || IsVariableParam(owner, out _));
        }

        /// <summary>
        /// "Math.Sin(x)" as a call the interpreter runs -- CscsCalls.Builtin -- when the text is
        /// exactly one Math call and an argument mentions a Variable (a "variable" argument or a
        /// local holding one); null otherwise.
        /// </summary>
        string TryBuildMathOnVariable(string text)
        {
            var trimmed = (text ?? "").Trim();
            var m = System.Text.RegularExpressions.Regex.Match(trimmed, @"^Math\s*\.\s*([A-Za-z_]\w*)\s*\(");
            if (!m.Success)
            {
                return null;
            }
            int open = m.Length - 1;
            if (FindMatchingParen(trimmed, open) != trimmed.Length - 1)
            {
                return null;
            }
            var name = "Math." + m.Groups[1].Value;
            var interpreter = m_parentScript?.InterpreterInstance;
            if (interpreter == null || interpreter.GetFunction(name) == null)
            {
                return null;
            }
            var argsText = trimmed.Substring(open + 1, trimmed.Length - open - 2);
            if (!MentionsAny(argsText, VariableParamNames()) && !MentionsAny(argsText, m_variableLocals))
            {
                return null;
            }
            var args = SplitTopLevel(argsText, ',');
            var sb = new StringBuilder("CscsCalls.Builtin(__interpreter, \"" + name + "\"");
            foreach (var arg in args)
            {
                // Plain operands, or a Math call of its own -- built the same way, and run first, as
                // the interpreter runs it. Any other call would need statements of its own.
                if (string.IsNullOrWhiteSpace(arg) || arg.IndexOf('"') >= 0)
                {
                    return null;
                }
                if (arg.IndexOf('(') >= 0)
                {
                    var nested = TryBuildMathOnVariable(arg);
                    if (nested == null)
                    {
                        return null;
                    }
                    sb.Append(", (object)(").Append(nested).Append(")");
                    continue;
                }
                sb.Append(", (object)(").Append(ReplaceArgsInString(arg.Trim())).Append(")");
            }
            return sb.Append(")").ToString();
        }

        /// <summary>
        /// Whether the text is exactly one call "owner.Member(..)" on a Variable held by a local
        /// or a "variable" argument, of a member Variable has no C# counterpart for.
        /// </summary>
        bool IsUnmappedMemberCallOnVariable(string text)
        {
            var m = System.Text.RegularExpressions.Regex.Match((text ?? "").Trim(), @"^([A-Za-z_]\w*)\s*\.\s*([A-Za-z_]\w*)\s*\(");
            if (!m.Success)
            {
                return false;
            }
            var trimmed = text.Trim();
            int open = m.Length - 1;
            if (FindMatchingParen(trimmed, open) != trimmed.Length - 1)
            {
                return false;
            }
            var owner = m.Groups[1].Value;
            var member = m.Groups[2].Value;
            if (!(m_variableLocals.Contains(owner) || IsVariableParam(owner, out _)))
            {
                return false;
            }
            return !IsVariableMember(member) && !IsCollectionMethod(member);
        }

        HashSet<string> VariableParamNames()
        {
            return new HashSet<string>(m_declaredArgsMap.Where(arg => arg.Value.Type == Variable.VarType.VARIABLE)
                .Select(arg => arg.Key), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether the name is an argument declared "variable", and the slot it is read from.
        /// </summary>
        bool IsVariableParam(string name, out string slot)
        {
            slot = null;
            return m_argsMap.TryGetValue(name, out var arg) && arg.Type == Variable.VarType.VARIABLE &&
                   m_paramMap.TryGetValue(name, out slot) && !string.IsNullOrEmpty(slot);
        }

        static bool IsVariableMember(string member)
        {
            int callStart = member.IndexOf('(');
            switch ((callStart < 0 ? member : member.Substring(0, callStart)).Trim().ToLower())
            {
                case "contains":
                case "keys":
                case "first":
                case "last":
                case "length":
                case "upper":
                case "lower":
                case "startswith":
                case "endswith":
                case "indexof":
                case "substring":
                case "replace":
                case "size":
                case "type":
                // The collection methods Variable provides. Without these a call on a local
                // that holds one -- "k = m.Keys; k.Sort();" -- was read as a method of a class
                // and went to CallMethod, which threw "Not a class instance" at run time.
                // Only these two: "Add" and its siblings have their own handling, and listing
                // them here took that away.
                case "sort":
                case "reverse":
                case "split":
                case "join": return true;
            }
            return false;
        }

        /// <summary>
        /// Whether MapStringMember recognises this member. Checked before routing a token
        /// through argument resolution: a member it does not know -- Split, say -- has to
        /// keep going to the interpreter callback that already handled it, rather than being
        /// half-resolved into code that does not compile.
        /// </summary>
        static bool IsMappedStringMember(string member)
        {
            string ignored = null;
            return MapStringMember("", member, ref ignored);
        }

        /// <summary>The call's own parentheses: from the member, from the caller, or added here.</summary>
        static string CallParens(int callStart, string call, bool callFollows)
        {
            return callStart >= 0 ? call : callFollows ? "" : "()";
        }

        static bool MapStringMember(string target, string member, ref string result,
                                    bool callFollows = false)
        {
            // The member can arrive with its call still attached -- "EndsWith(\"d\"))" --
            // so the name has to be separated from the arguments, which the caller appends.
            int callStart = member.IndexOf('(');
            // A property can be followed by the rest of a chain: the comparison rewrite turns
            // "s.Upper > \"AA\"" into "s.Upper.CompareCscs(\"AA\")", and reading that as one
            // member name matched nothing. A dot before the parenthesis is the only one that
            // separates members -- any later dot belongs to a call's own arguments, and the
            // switch below already carries those along.
            int chain = member.IndexOf('.');
            if (chain > 0 && (callStart < 0 || chain < callStart))
            {
                string head = null;
                return MapStringMember(target, member.Substring(0, chain), ref head) &&
                       MapStringMember(head, member.Substring(chain + 1), ref result, callFollows);
            }
            // A member can also follow a call -- "s.At(0).Upper" -- and appending "(0).Upper"
            // as the call's arguments left a C# "Upper" that does not exist. The chain is split
            // after the call's own closing parenthesis, found outside any quotes in it.
            int callEnd = callStart < 0 ? -1 : FindCallEnd(member, callStart);
            if (callEnd > 0 && callEnd + 1 < member.Length && member[callEnd + 1] == '.')
            {
                string head = null;
                return MapStringMember(target, member.Substring(0, callEnd + 1), ref head) &&
                       MapStringMember(head, member.Substring(callEnd + 2), ref result, callFollows);
            }
            string call = callStart < 0 ? "" : member.Substring(callStart);
            switch ((callStart < 0 ? member : member.Substring(0, callStart)).Trim().ToLower())
            {
                // The parentheses reach here three ways, and adding a pair unconditionally --
                // ".ToUpper()" -- doubled them into ".ToUpper()()" (CS0149) for "s.Upper()", the
                // spelling scripts use most. They are in "member" already when it arrives with its
                // call attached; the caller appends the script's own when callFollows says a call
                // opens right after this token; and only the bare member form "s.Upper" needs a
                // pair supplied here. Compiling the call form was unsafe until the interpreter
                // stopped leaving a property's "()" unconsumed (Variable.GetCoreProperty).
                case "upper": result = target + ".ToUpper" + CallParens(callStart, call, callFollows); return true;
                case "lower": result = target + ".ToLower" + CallParens(callStart, call, callFollows); return true;
                // CSCS Length and Size on a string are the character count, which is what C#
                // Length gives (Size was 0 on text until September 2026). An empty "()" after
                // either is the interpreter's too (Variable.ConsumeEmptyCall).
                case "length":
                case "size": result = target + ".Length" + (call.Replace(" ", "") == "()" ? "" : call); return true;
                // Replace is the one method whose CSCS and C# behaviour already agree; the
                // rest go through CscsStringMembers, which restores the interpreter's
                // case-insensitive default and Substring's clamping.
                case "replace": result = target + ".Replace" + call; return true;
                // Trim is AsString().Trim() in the interpreter, which is C#'s own, with or without
                // its parentheses (the bare form took the rest of the expression until September
                // 2026). Not every caller says whether a call follows, so the bare form's "()" is
                // supplied by RoslynCompiler.RepairOperators (a method group used as a value).
                case "trim": result = target + ".Trim" + call; return true;
                case "contains": result = target + ".ContainsCscs" + call; return true;
                case "startswith": result = target + ".StartsWithCscs" + call; return true;
                case "endswith": result = target + ".EndsWithCscs" + call; return true;
                case "indexof": result = target + ".IndexOfCscs" + call; return true;
                case "substring": result = target + ".SubstringCscs" + call; return true;
                // A character as a one-letter string, and an empty one past the end -- the
                // interpreter's At, which C# strings do not have at all.
                case "at": result = target + ".AtCscs" + call; return true;
                // Not a CSCS member: the marker the string-comparison rewrite emits.
                case "comparecscs": result = target + ".CompareCscs" + call; return true;
                case "equals": result = target + ".EqualsCscs" + call; return true;
            }
            return false;
        }

        /// <summary>
        /// How a call's result is read inside a larger expression. Its type is settled only when
        /// the call runs, so it stays a Variable, whose operators apply the interpreter's rules:
        /// reading it as a number at translation time turned "f(n) * 2" into 0 whenever f
        /// returned a string, where the interpreter joins "s2". A string target still reads it
        /// as text; bit operations, which Variable does not define, still read it as a number.
        /// </summary>
        string ReadCallResult(string value, List<string> tokens, string trailing)
        {
            if (GetTokenType(tokens) == "string" && !tokens.Contains("+") && !m_statementVariableAccum &&
                !(trailing ?? "").TrimStart().StartsWith("["))
            {
                return value + ".AsString()";
            }
            return m_statementBitwise ? value + ".AsDouble()" : value;
        }

        /// <summary>
        /// Whether the text has "&amp;&amp;", "||" or a "?" outside string literals -- the places
        /// a call must not be moved out of, because the operator decides whether it runs.
        /// </summary>
        static bool NeedsInlineCalls(string text)
        {
            bool inQuotes = false;
            for (int i = 0; i < (text ?? "").Length; i++)
            {
                char ch = text[i];
                if (ch == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                }
                else if (!inQuotes && (ch == '?' ||
                         (i + 1 < text.Length && ((ch == '&' && text[i + 1] == '&') ||
                                                  (ch == '|' && text[i + 1] == '|')))))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Arguments C# can evaluate as they stand: no "name = value" (a named argument, which
        /// C# would read as an assignment), no collection literal in braces, and no text --
        /// by the time a call reaches GetCSCSFunction its quotes are already escaped for the
        /// string the text route builds, and read as C# they no longer compile.
        /// </summary>
        static bool IsPlainArgumentList(string argsStr)
        {
            var text = argsStr ?? "";
            if (text.IndexOfAny(new[] { '"', '\'', '\\' }) >= 0)
            {
                return false;
            }
            // A script variable named like a C# keyword -- "SetValue(switch, 1)" -- cannot be
            // written as C#; the text route names it to the interpreter instead.
            foreach (System.Text.RegularExpressions.Match word in
                     System.Text.RegularExpressions.Regex.Matches(text, @"(?<![\w.])[A-Za-z_]\w*(?!\s*\()"))
            {
                if (s_allCsKeywords.Contains(word.Value) && word.Value != "true" && word.Value != "false" &&
                    word.Value != "null")
                {
                    return false;
                }
            }
            bool inQuotes = false;
            int depth = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (inQuotes)
                {
                    continue;
                }
                if (ch == '{' || ch == '}')
                {
                    return false;
                }
                if (ch == '(' || ch == '[') { depth++; continue; }
                if (ch == ')' || ch == ']') { depth--; continue; }
                if (ch == '=' && depth == 0)
                {
                    char before = i > 0 ? text[i - 1] : ' ';
                    char after = i + 1 < text.Length ? text[i + 1] : ' ';
                    if (after != '=' && before != '=' && before != '!' && before != '<' && before != '>')
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// A call to a script function as one C# expression, through CscsCalls, or false when
        /// the call cannot be written that way -- a script call inside an argument that is not
        /// itself just a call, say. Arguments are translated as expressions; one that is a
        /// call of its own is made into a nested CscsCalls.Call.
        /// </summary>
        bool TryInlineScriptCall(string name, string inner, out string call)
        {
            call = null;
            name = name.Trim();
            // A native command -- print, copy, write, run, ... -- reads its arguments as text
            // (Translation.IsFunctWithSpace); the text route gives it that. A script function of
            // such a name takes values like any other since September 2026 (CustomFunction).
            if (IsFunctionWithSpace(name) && !(m_parentScript?.InterpreterInstance?.GetFunction(name) is CustomFunction))
            {
                return false;
            }
            if (!IsPlainName(name) || !MentionsScriptCall(name + "()"))
            {
                return false;
            }
            var parts = new StringBuilder();
            var values = new List<string>();
            foreach (var piece in SplitTopLevel(inner, ','))
            {
                var arg = piece.Trim();
                if (arg.Length == 0)
                {
                    continue;
                }
                if (MentionsScriptCall(arg))
                {
                    int open = arg.IndexOf('(');
                    if (open <= 0 || FindMatchingParen(arg, open) != arg.Length - 1 ||
                        !TryInlineScriptCall(arg.Substring(0, open),
                                             arg.Substring(open + 1, arg.Length - open - 2), out var nested))
                    {
                        return false;
                    }
                    parts.Append(", ").Append(nested);
                    values.Add(nested);
                    continue;
                }
                // A built-in of the interpreter's in the argument -- "Substring(decimal(r / q), 0,
                // 10)": written out as C# it is the keyword "decimal" (CS1525). The call route,
                // which hands the arguments to the interpreter, takes it.
                if (CallsInterpreterBuiltin(arg))
                {
                    return false;
                }
                var value = ReplaceArgsInString(arg);
                parts.Append(", ").Append(value);
                values.Add(value);
            }
            // Another compiled function with a typed entry point: called through it when that is
            // still the function the name stands for at run time (CscsDirect.Call).
            if (DirectCalls && !string.Equals(name, m_functionName, StringComparison.OrdinalIgnoreCase) && !AsyncMode &&
                m_parentScript?.InterpreterInstance?.GetFunction(name) is CustomCompiledFunction callee &&
                callee.Precompiler?.DirectTypes is List<string> calleeTypes)
            {
                if (values.Count == calleeTypes.Count && values.Count <= CscsDirect.MaxArguments)
                {
                    var funcType = "Func<Interpreter" + string.Concat(calleeTypes.Select(t => ", " + t)) + ", Variable>";
                    var lambdaArgs = string.Concat(Enumerable.Range(0, values.Count).Select(k => ", __dv" + k));
                    var converted = string.Concat(Enumerable.Range(0, values.Count).Select(k =>
                        ", CscsDirect." + (calleeTypes[k] == "int" ? "Int" : calleeTypes[k] == "double" ? "Num" :
                                           calleeTypes[k] == "string" ? "Text" : "Var") + "(__dv" + k + ")"));
                    call = "CscsDirect.Call(__interpreter, \"" + Constants.ConvertName(name) + "\", (" + funcType + ")null" +
                           string.Concat(values.Select(v => ", " + v.Trim())) +
                           ", (__dd, __di" + lambdaArgs + ") => __dd(__di" + converted + "))";
                    return true;
                }
            }
            // A built-in called with values publishes them as temporaries in the interpreter's
            // frame, so the function is not one that can run without it.
            if (IsValueBuiltin(name))
            {
                m_usesInterpreter = true;
            }
            // A call with values needs nothing else of the interpreter's: the callee gets its
            // arguments as values and registers them itself, and a CSCS function cannot see its
            // caller's locals (no dynamic scope), so the caller's locals need not be published
            // for it -- only the text route, whose argument text names them, needs that. A call
            // to itself may then skip the interpreter altogether (MakeDirectSelfCalls).
            call = "CscsCalls.Call(__interpreter, \"" + name + "\"" + parts + ")";
            return true;
        }

        /// <summary>
        /// Separates the closing parentheses a member token carries from the statement around
        /// it -- "Lower)" at the end of "if (t != t.Lower)" -- from the member itself. Only
        /// the unbalanced ones: "Substring(1)" keeps its own.
        /// </summary>
        static string SplitClosingParens(string member, out string closing)
        {
            closing = "";
            while (member.EndsWith(")") && member.Count(c => c == ')') > member.Count(c => c == '('))
            {
                closing += ")";
                member = member.Substring(0, member.Length - 1);
            }
            return member;
        }

        /// <summary>
        /// The index of the parenthesis closing the one at <paramref name="open"/>, skipping
        /// string literals, or -1 if it is not closed.
        /// </summary>
        static int FindCallEnd(string text, int open)
        {
            int depth = 0;
            bool inQuotes = false;
            for (int i = open; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                }
                else if (!inQuotes && ch == '(')
                {
                    depth++;
                }
                else if (!inQuotes && ch == ')' && --depth == 0)
                {
                    return i;
                }
            }
            return -1;
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
            // "t = s.At(n - 1)" with s a Variable: the call has to go to the interpreter, which the
            // known-expression path cannot do -- it declared t a double and wrote "s.At" as C#.
            var whole = string.Concat(tokens);
            int eqAt = tokens.Count > 1 && tokens[1] == "=" ? tokens[0].Length + 1 : 0;
            if (IsUnmappedMemberCallOnVariable(whole.Substring(Math.Min(eqAt, whole.Length))))
            {
                return false;
            }
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
                // A quote inside a subscript is a map key, not a string-typed operand:
                // "m[\"a\"] * 100" is arithmetic. IsString only looks for a quote anywhere in
                // the token, so the key made the whole expression look string-typed and the
                // subscript stopped being converted to a number.
                if (IsString(WithoutSubscripts(token)))
                {
                    return false;
                }
                if (Constants.ARITHMETIC_EXPR.Contains(token))
                {
                    numericCandidate = true;
                    continue;
                }

                // "s.Substring(n - 1, n + 1)" tokenizes as "s.Substring(n", "-", "1,n", "+", "1)":
                // the statement tokenizer splits on operators but not on ','. The middle token
                // resolved to nothing, the statement went to the token loop, and that read
                // "1,n" as the name of a call -- both arguments collapsed into one. Each side
                // of a top-level comma is judged as the token it really is. ("1,2" only ever
                // passed because Double.TryParse reads ',' as a thousands separator.) A piece is
                // judged beside an operator: this method answers "known and numeric", and an int
                // argument alone is known without ever marking the expression numeric -- judged
                // bare, "n" came back false and the check changed nothing.
                if (IndexOfTopLevelChar(token, ',') >= 0 && !token.Contains("{"))
                {
                    foreach (var part in SplitTopLevel(token, ','))
                    {
                        var piece = part.Trim();
                        if (piece.Length == 0 || IsNumber(piece.Trim('(', ')')))
                        {
                            continue;
                        }
                        if (!IsKnownExpression(new List<string> { piece, "+" }))
                        {
                            return false;
                        }
                    }
                    numericCandidate = true;
                    continue;
                }

                string paramName = GetFunctionName(token, out string suffix, out bool isArray);

                // Strip grouping punctuation the surrounding expression left on the token.
                // "!(n>100)" tokenizes with a trailing "100))", and GetFunctionName only
                // trims back to the last ')', leaving "100)" -- which is not a number, so the
                // whole condition looked unknown and stopped resolving its arguments.
                paramName = paramName.Trim('(', ')', '!', '~');

                // A group has no name before its parenthesis, so "(helper(n))" looked like
                // punctuation and a script call inside it went down the expression path,
                // which emits it as a C# method that does not exist. Only the token loop
                // makes the interpreter callback a script call needs.
                if (string.IsNullOrWhiteSpace(paramName) && MentionsScriptCall(token))
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(paramName) || Constants.RESERVED.Contains(paramName))
                {
                    continue;
                }

                // "s.Length" on a string argument is a number, whatever its owner is. Judged
                // by the owner alone the whole expression looked string-typed, so
                // "if (i < s.Length)" went to the token loop instead -- where a member inside
                // a condition came out as an interpreter callback with its parentheses
                // unbalanced, and the function would not compile at all.
                if (IsNumericStringMember(token))
                {
                    numericCandidate = true;
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

        /// <summary>
        /// Whether the token reads a member of a string argument whose value is a number.
        /// Only "Length" and "IndexOf" are: the rest of the string members yield strings or
        /// booleans, and Size is not mapped at all.
        /// </summary>
        bool IsNumericStringMember(string token)
        {
            var text = (token ?? "").Trim().Trim('(', ')', '!', '~', '{', '}', ';');
            int dot = text.IndexOf('.');
            if (dot <= 0)
            {
                return false;
            }
            var owner = text.Substring(0, dot).Trim();
            if (!m_argsMap.TryGetValue(owner, out var arg) ||
                arg.Type != Variable.VarType.STRING || !m_paramMap.ContainsKey(owner))
            {
                return false;
            }
            var member = text.Substring(dot + 1).Trim();
            int callStart = member.IndexOf('(');
            var bare = (callStart < 0 ? member : member.Substring(0, callStart)).Trim().ToLower();
            return bare == "length" || bare == "indexof";
        }

        static bool IsNumber(string text)
        {
            return Double.TryParse(text, NumberStyles.Number |
                                         NumberStyles.AllowExponent |
                                         NumberStyles.Float,
                                         CultureInfo.InvariantCulture, out _);
        }
        /// <summary>
        /// The token with its "[...]" sections removed, so that what is being indexed can be
        /// judged separately from the index itself.
        /// </summary>
        static string WithoutSubscripts(string token)
        {
            if (token == null || token.IndexOf('[') < 0)
            {
                return token;
            }
            var sb = new StringBuilder();
            int depth = 0;
            foreach (var ch in token)
            {
                if (ch == '[') { depth++; continue; }
                if (ch == ']') { depth = depth > 0 ? depth - 1 : 0; continue; }
                if (depth == 0) { sb.Append(ch); }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Whether the text is one complete string literal and nothing else. IsString is
        /// true of anything that merely contains a quote -- "m[\"a\"]" among them -- so it
        /// cannot decide this.
        /// </summary>
        static bool IsWholeStringLiteral(string text)
        {
            if (text.Length < 2 || text[0] != '"' || text[text.Length - 1] != '"')
            {
                return false;
            }
            for (int i = 1; i < text.Length - 1; i++)
            {
                if (text[i] == '"' && !IsEscapedQuote(text, i))
                {
                    return false;
                }
            }
            return true;
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
        /// <summary>
        /// Translates "name = condition ? literal : literal" into a C# conditional that builds
        /// each branch, or null when the statement is not one of those. At least one branch
        /// must be a literal -- that is what the ordinary path cannot express.
        /// </summary>
        string TryBuildTernaryLiteral(string statement, bool addNewVars)
        {
            var trimmed = statement.TrimEnd().TrimEnd(';').TrimEnd();
            var sides = SplitTopLevelOn(trimmed, "=");
            if (sides.Count != 2 || !IsPlainName(sides[0].Trim()))
            {
                return null;
            }

            var name = sides[0].Trim();
            var rhs = sides[1].Trim();
            var parts = SplitTopLevelOn(rhs, "?");
            if (parts.Count != 2)
            {
                return null;
            }
            var branches = SplitTopLevelOn(parts[1], ":");
            if (branches.Count != 2)
            {
                return null;
            }

            var whenTrue = branches[0].Trim();
            var whenFalse = branches[1].Trim();
            string builtTrue, builtFalse;
            if (!TryBuildArrayLiteral(whenTrue, out builtTrue) &&
                !TryBuildArrayLiteral(whenFalse, out builtFalse))
            {
                return null;   // no literal here, so the ordinary path handles it
            }

            m_newVariables.Add(name);
            m_collectionLocals.Add(name);
            var code = m_depth + "var " + name + " = (" + ReplaceArgsInString(parts[0]) + ") ? " +
                BuildLiteralElement(whenTrue) + " : " + BuildLiteralElement(whenFalse) + ";\n";
            return addNewVars ? code + RegisterVariableString(name, name) : code;
        }

        /// <summary>
        /// Translates "var Local = Enum {X, Y}" declared inside the function, or null when the
        /// statement is not one. It arrives as four statements -- "var Local = Enum", "{", "X, Y",
        /// "}" -- and each of the three names used to become an interpreter call of its own, their
        /// temporaries glued into "__varTempVar1__varTempVar2". The enum is built the way
        /// EnumFunction builds it: an ENUM Variable whose members are numbered from 0. Only the
        /// statement the loop is on can be one, since the names come from the statements after it.
        /// </summary>
        string TryBuildLocalEnum(string statement, bool addNewVars)
        {
            var trimmed = statement.Trim().TrimEnd(';').Trim();
            if (m_statements == null || m_statementId + 3 >= m_statements.Count ||
                (m_statements[m_statementId] ?? "").Trim() != statement.Trim())
            {
                return null;
            }
            if (trimmed.StartsWith("var ", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(4).Trim();
            }
            var sides = trimmed.Split('=');
            if (sides.Length != 2 || sides[1].Trim() != "Enum")
            {
                return null;
            }
            var name = sides[0].Trim();
            if (!IsPlainName(name) || m_newVariables.Contains(name) || m_paramMap.ContainsKey(name) ||
                m_statements[m_statementId + 1].Trim() != "{" || m_statements[m_statementId + 3].Trim() != "}")
            {
                return null;
            }
            var members = m_statements[m_statementId + 2].Split(',').Select(member => member.Trim()).ToList();
            if (members.Count == 0 || members.Any(member => !IsPlainName(member)) ||
                members.Distinct(StringComparer.OrdinalIgnoreCase).Count() != members.Count)
            {
                return null;
            }

            var code = m_depth + "var " + name + " = new Variable(Variable.VarType.ENUM);\n";
            for (int i = 0; i < members.Count; i++)
            {
                code += m_depth + name + ".SetEnumProperty(\"" + members[i] + "\", new Variable(" + i + "));\n";
            }
            m_newVariables.Add(name);
            m_enumLocals[name] = members;
            m_statementId += 3;
            return addNewVars ? code + RegisterVariableString(name, name) : code;
        }

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
                // IsMapEntry, not a bare split on ':' -- a ternary carries a ':' of its own, and
                // "{n > 2 ? 10 : 20}" was built as the map entry "n > 2 ? 10" -> 20.
                if (IsMapEntry(element))
                {
                    isMap = true;
                    break;
                }
            }

            var declaration = m_newVariables.Contains(name) ? "" : "var ";
            m_newVariables.Add(name);
            m_collectionLocals.Add(name);

            // A CSCS call in an element becomes statements of its own, which this builder has
            // to collect: it returns straight to ProcessStatement rather than going through
            // the token loop, which is what normally flushes them.
            var outerPrelude = m_statementPrelude;
            m_statementPrelude = "";
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
                    // The value is built like an array element, since it may be a literal of
                    // its own -- {"x": {1, 2}} -- which ReplaceArgsInString emits verbatim.
                    sb.Append(m_depth + name + ".SetHashVariable(Variable.ConvertToVariable(" +
                        ReplaceArgsInString(HoistValueCalls(pair[0].Trim())) +
                        ").AsString(), " + BuildLiteralElement(pair[1]) + ");\n");
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
                    // An element may itself be a literal -- {{1,2},{3,4}} -- so it is built
                    // rather than handed to ReplaceArgsInString, which emits braces verbatim.
                    items.Add(BuildLiteralElement(element));
                }
                sb.Append(m_depth + declaration + name + " = new Variable(new List<Variable> { " +
                    string.Join(", ", items) + " });\n");
            }

            if (addNewVars)
            {
                sb.Append(RegisterVariableString(name));
            }
            var literalPrelude = m_statementPrelude;
            m_statementPrelude = outerPrelude;
            return literalPrelude + sb.ToString();
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
                if (ch == '"' && !IsEscapedQuote(statement, i))
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
            if (close < 0 || close + 1 >= statement.Length)
            {
                return null;
            }

            // Chained subscripts: "a[0][1] = 9" assigns into the element a[0], which is
            // itself a Variable. Consume the whole chain so the assignment lands on the
            // innermost one -- without this the statement fell through to the generic path
            // and came out as the C# declaration "double a[0][1] = 9".
            var indices = new List<string>
            {
                statement.Substring(bracket + 1, close - bracket - 1).Trim()
            };
            while (close + 1 < statement.Length && statement[close + 1] == '[')
            {
                int nextClose = FindMatchingBracket(statement, close + 1);
                if (nextClose < 0)
                {
                    return null;
                }
                indices.Add(statement.Substring(close + 2, nextClose - close - 2).Trim());
                close = nextClose;
            }

            var after = statement.Substring(close + 1);
            string compound = null;      // the operator of "a[i] op= v", null for a plain "="
            string valueExpr;

            // A step is not the same as "+= 1": the interpreter steps numeric text as its number,
            // so "a[0]++" over the element "5" gives 6, while "a[0] += 1" concatenates to "51".
            bool isStep = after.StartsWith("++") || after.StartsWith("--");
            if (isStep)
            {
                compound = after.Substring(0, 1);
                valueExpr = "1";
            }
            else if (after.Length > 1 && after[1] == '=' && "+-*/%&|^".IndexOf(after[0]) >= 0)
            {
                compound = after.Substring(0, 1);
                valueExpr = after.Substring(2).Trim().TrimEnd(';');
            }
            else if (after.StartsWith("<<=") || after.StartsWith(">>="))
            {
                compound = after.Substring(0, 2);
                valueExpr = after.Substring(3).Trim().TrimEnd(';');
            }
            else if (after[0] == '=' && !(after.Length > 1 && after[1] == '='))
            {
                valueExpr = after.Substring(1).Trim().TrimEnd(';');
            }
            else
            {
                return null;   // a read, or "a[i] == b"
            }

            var indexExpr = indices[indices.Count - 1];
            if (string.IsNullOrWhiteSpace(indexExpr) || string.IsNullOrWhiteSpace(valueExpr) ||
                indices.Exists(string.IsNullOrWhiteSpace))
            {
                return null;
            }

            var root = m_paramMap.ContainsKey(name) ? m_paramMap[name] : name;
            // Everything before the last subscript selects the collection being assigned to;
            // the last one is the index handed to SetVariable. The registration below still
            // needs the bare name, so the subscripts go on a separate target.
            var target = root;
            for (int i = 0; i < indices.Count - 1; i++)
            {
                // No cast: the index can be a string key as well as a number, and Variable
                // has an indexer for each. Casting made "m[\"a\"][\"b\"] = ..." emit
                // "m[(int)(\"a\")]".
                target += "[" + ReplaceArgsInString(indices[i]) + "]";
            }
            // A CSCS call in the value has to be run ahead of the store: it becomes several
            // statements plus a temporary, and neither ReplaceArgsInString nor
            // BuildLiteralElement can produce those on their own.
            var outerPrelude = m_statementPrelude;
            m_statementPrelude = "";
            valueExpr = HoistConditionCalls(valueExpr, valueExpr, true);
            var valuePrelude = m_statementPrelude;
            m_statementPrelude = outerPrelude;

            var code = valuePrelude;
            if (compound == null)
            {
                // The value can be a literal -- "m[\"x\"] = {\"y\" : 5}" -- which has to be
                // built rather than handed to ReplaceArgsInString, which would emit the
                // braces verbatim.
                code += m_depth + target + ".SetVariable(Variable.ConvertToVariable(" +
                    ReplaceArgsInString(indexExpr) + "), " + BuildLiteralElement(valueExpr) + ");\n";
            }
            else
            {
                // "a[i] += v" is a read-modify-write; C# cannot compound-assign through the
                // indexer. The index goes into a temp so it is evaluated once, as it is in a
                // plain assignment.
                var indexTemp = "__idx" + (++m_tempVarId);
                code += m_depth + "var " + indexTemp + " = Variable.ConvertToVariable(" +
                    ReplaceArgsInString(indexExpr) + ");\n";
                // The element is indexed by the Variable itself rather than by AsInt(), so a
                // map key works as well as an array position. The step and the compound go
                // through the interpreter's own operators (CscsOps.Compound, which steps as
                // OperatorAssignFunction.Stepped does, and ProcessOperator): "x = x op v", with
                // numeric text stepped as its number -- the element "5" becomes 6.
                var element = target + "[" + indexTemp + "]";
                var stepped = isStep ?
                    "CscsOps.Compound(" + element + ", \"" + compound + compound + "\", null)" :
                    "CscsConvert.Compound(" + element + ", " +
                        ReplaceArgsInString(valueExpr) + ", \"" + compound + "=\")";
                code += m_depth + target + ".SetVariable(" + indexTemp + ", " + stepped + ");\n";
            }
            if (addNewVars)
            {
                code += RegisterVariableString(name, root);
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
                if (ch == '"' && !IsEscapedQuote(text, i))
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

        /// <summary>
        /// Builds the C# expression for an array literal such as "{1,2,3}", or returns false
        /// when the text is not one. Map literals return false: their entries need
        /// SetHashVariable calls, which are statements and cannot sit in an expression.
        /// </summary>
        bool TryBuildArrayLiteral(string literal, out string expression)
        {
            expression = null;
            literal = (literal ?? "").Trim();
            if (literal.Length < 2 || literal[0] != '{' ||
                FindMatchingBrace(literal, 0) != literal.Length - 1)
            {
                return false;
            }

            var inner = literal.Substring(1, literal.Length - 2).Trim();
            var elements = SplitTopLevel(inner, ',');
            var items = new List<string>();
            bool isMap = false;
            foreach (var element in elements)
            {
                if (string.IsNullOrWhiteSpace(element))
                {
                    continue;
                }
                var entry = SplitTopLevel(element, ':');
                // A ternary's ':' is not a key separator -- see IsMapEntry.
                if (entry.Count > 1 && IsMapEntry(element))
                {
                    // A "key : value" entry. Both halves become arguments to Variable.NewMap,
                    // which builds the map with SetHashVariable exactly as the interpreter
                    // does. Bailing out here instead left every map literal in an expression
                    // position -- "m[k] = {...}" -- untranslatable.
                    isMap = true;
                    items.Add(BuildLiteralElement(entry[0]));
                    items.Add(BuildLiteralElement(string.Join(":", entry.GetRange(1, entry.Count - 1))));
                    continue;
                }
                if (isMap)
                {
                    return false;      // entries with and without keys: leave it alone
                }
                items.Add(BuildLiteralElement(element));
            }
            expression = isMap ? "Variable.NewMap(" + string.Join(", ", items) + ")" :
                "new Variable(new List<Variable> { " + string.Join(", ", items) + " })";
            return true;
        }

        /// <summary>
        /// One element of a literal. It may itself be a literal -- {{1,2},{3,4}} or a nested
        /// map -- so recurse before falling back to treating it as a scalar expression.
        /// </summary>
        string BuildLiteralElement(string element)
        {
            string nested;
            return TryBuildArrayLiteral(element.Trim(), out nested) ? nested :
                "Variable.ConvertToVariable(" +
                    ReplaceArgsInString(HoistValueCalls(element.Trim())) + ")";
        }

        /// <summary>
        /// Runs any CSCS call in a value ahead of the statement, leaving the temporary that
        /// holds its result. The temporary stays a Variable: the value has to keep whatever
        /// the call returned, unlike a condition, which is read as a number.
        /// </summary>
        string HoistValueCalls(string value)
        {
            return HoistConditionCalls(value, value, true);
        }

        /// <summary>Whether the operand text mentions a "variable" argument, a local holding a
        /// Variable, or an interpreter-run power already built.</summary>
        bool MayHoldVariable(string operand)
        {
            return operand.Contains(POWER_OP) || MentionsAny(operand, VariableParamNames()) ||
                   MentionsAny(operand, m_variableLocals);
        }

        /// <summary>
        /// Rewrites "a ** b" into Math.Pow wherever it appears, or returns null when the
        /// statement has no "**". Grouped to the right, as the interpreter groups it, which
        /// falls out of replacing the last "**" first: a**b**c becomes a**Pow(b,c) and then
        /// Pow(a, Pow(b,c)).
        ///
        /// Only the operands either side are taken, not the whole expression, so the
        /// precedence of anything around them is left intact -- "1 + n ** 2" keeps the 1 out
        /// of the base.
        /// </summary>
        string TryRewritePower(string statement)
        {
            var text = statement;
            bool rewrote = false;
            for (int guard = 0; guard < 32; guard++)
            {
                int at = LastTopLevelPower(text);
                if (at < 0)
                {
                    break;
                }
                int leftStart = OperandStart(text, at);
                int rightEnd = OperandEnd(text, at + 2);
                if (leftStart < 0 || rightEnd < 0)
                {
                    return null;
                }
                var left = text.Substring(leftStart, at - leftStart).Trim();
                var right = text.Substring(at + 2, rightEnd - at - 2).Trim();
                if (left.Length == 0 || right.Length == 0)
                {
                    return null;
                }
                // A keyword may sit immediately before the operand, the statement having
                // arrived with its whitespace stripped -- "return(x+1)**2" -- so the call
                // must not be glued onto it.
                var head = text.Substring(0, leftStart);
                if (head.Length > 0 && (char.IsLetterOrDigit(head[head.Length - 1]) ||
                                        head[head.Length - 1] == '_'))
                {
                    head += " ";
                }
                // No space after the comma: statements reach here with their whitespace
                // already stripped, and a space there makes the argument resolve differently
                // -- "Math.Pow(2, Math.Pow(3, n))" left the inner "n" undeclared while
                // "Math.Pow(2,Math.Pow(3,n))" did not.
                // Math.Pow for numbers, as before; the interpreter's operator where an operand may
                // hold a Variable (a "variable" argument, a local holding one, an earlier "**"
                // that did), which Math.Pow cannot take.
                var power = MayHoldVariable(left) || MayHoldVariable(right) ? POWER_OP : "Math.Pow";
                text = head + power + "(" + left + "," + right + ")" + text.Substring(rightEnd);
                rewrote = true;
            }
            return rewrote ? text : null;
        }

        /// <summary>Position of the last "**" outside brackets and quotes, or -1.</summary>
        static int LastTopLevelPower(string text)
        {
            int depth = 0;
            bool inQuotes = false;
            int found = -1;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                }
                else if (inQuotes)
                {
                    continue;
                }
                else if (ch == '(' || ch == '[' || ch == '{')
                {
                    depth++;
                }
                else if (ch == ')' || ch == ']' || ch == '}')
                {
                    depth--;
                }
                else if (depth == 0 && ch == '*' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    found = i;
                    i++;
                }
            }
            return found;
        }

        /// <summary>
        /// Where the operand ending just before <paramref name="at"/> begins: a name, a
        /// number, or a bracketed group with whatever name precedes it.
        /// </summary>
        static int OperandStart(string text, int at)
        {
            int i = at - 1;
            while (i >= 0 && char.IsWhiteSpace(text[i])) { i--; }
            if (i < 0) { return -1; }
            if (text[i] == ')' || text[i] == ']')
            {
                int depth = 0;
                for (; i >= 0; i--)
                {
                    char ch = text[i];
                    if (ch == ')' || ch == ']') { depth++; }
                    else if (ch == '(' || ch == '[') { depth--; }
                    if (depth == 0) { break; }
                }
                if (i < 0) { return -1; }
                i--;   // step over the opener, onto whatever name precedes it
            }
            while (i >= 0 && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '.'))
            {
                i--;
            }

            // The statement arrives with its whitespace stripped, so "return (x + 1) ** 2"
            // is "return(x+1)**2" and the scan above walks straight back over the keyword,
            // making the operand "return(x+1)". A keyword is not part of the operand.
            int start = i + 1;
            int nameEnd = start;
            while (nameEnd < at && (char.IsLetterOrDigit(text[nameEnd]) || text[nameEnd] == '_'))
            {
                nameEnd++;
            }
            var name = text.Substring(start, nameEnd - start);
            if (name.Length > 0 && Constants.RESERVED.Contains(name))
            {
                start = nameEnd;
            }
            return start;
        }

        /// <summary>Where the operand beginning at <paramref name="at"/> ends.</summary>
        static int OperandEnd(string text, int at)
        {
            int i = at;
            while (i < text.Length && char.IsWhiteSpace(text[i])) { i++; }
            if (i >= text.Length) { return -1; }
            if (text[i] == '-' || text[i] == '+') { i++; }
            while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '.'))
            {
                i++;
            }
            while (i < text.Length && (text[i] == '(' || text[i] == '['))
            {
                int close = text[i] == '(' ? FindMatchingParen(text, i) : FindMatchingBracket(text, i);
                if (close < 0) { return -1; }
                i = close + 1;
            }
            return i;
        }

        /// <summary>
        /// Translates "do" and the "while" that closes it, or null for anything else. C# has
        /// the same loop, so only the bookkeeping is needed: the body's statements are emitted
        /// by the ordinary path between the two, and the closing "while" is recognised by its
        /// position rather than by its text, which is indistinguishable from a new loop.
        /// </summary>
        string TryBuildDoLoop(string statement)
        {
            if (m_doWhileTails.Contains(m_statementId))
            {
                m_doWhileTails.Remove(m_statementId);
                int open = statement.IndexOf('(');
                int close = open < 0 ? -1 : FindMatchingParen(statement, open);
                if (close < 0)
                {
                    return null;
                }
                var condition = statement.Substring(open + 1, close - open - 1);
                return m_depth + "while (" + ReplaceArgsInString(condition) + ");\n";
            }

            if (statement.Trim() != Constants.DO || m_nextStatement != "{")
            {
                return null;
            }

            // Find the "}" that closes the body; the "while" is the statement after it.
            int depth = 0;
            for (int i = m_statementId + 1; i < m_statements.Count; i++)
            {
                var text = m_statements[i].Trim();
                if (text == "{")
                {
                    depth++;
                }
                else if (text == "}")
                {
                    if (--depth == 0)
                    {
                        if (i + 1 >= m_statements.Count ||
                            !StartsWithKeyword(m_statements[i + 1], Constants.WHILE))
                        {
                            return null;
                        }
                        m_doWhileTails.Add(i + 1);
                        m_statementId++;      // the block's "{" is emitted here
                        var code = m_depth + "do {\n";
                        m_depth += "  ";
                        return code;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Translates a "while" whose condition calls a CSCS function, or null when it is not
        /// one. A call generates statements, which cannot sit inside a condition, and hoisting
        /// it above the loop would run it once instead of on every iteration -- so the loop is
        /// turned inside out:
        ///
        ///     while (true) { &lt;the call&gt; if (!(condition)) { break; } &lt;body&gt; }
        ///
        /// which evaluates the call exactly where the condition did. Only for a single-clause
        /// condition: hoisting out of "g() &amp;&amp; h()" would call h() even when g() is false.
        /// </summary>
        string TryBuildWhileWithCall(string statement)
        {
            if (!StartsWithKeyword(statement, Constants.WHILE) || m_nextStatement != "{")
            {
                return null;
            }
            int open = statement.IndexOf('(');
            int close = open < 0 ? -1 : FindMatchingParen(statement, open);
            if (close < 0 || statement.Substring(close + 1).Trim().Length > 0)
            {
                return null;
            }

            var condition = statement.Substring(open + 1, close - open - 1);
            var outerPrelude = m_statementPrelude;
            m_statementPrelude = "";
            var hoisted = HoistConditionCalls(condition, condition);
            var prelude = m_statementPrelude;
            m_statementPrelude = outerPrelude;
            if (string.IsNullOrEmpty(prelude))
            {
                return null;      // no call in the condition: the ordinary path handles it
            }

            var code = m_depth + "while (true) {\n";
            m_depth += "  ";
            code += prelude + m_depth + "if (!(" + ReplaceArgsInString(hoisted) + ")) { break; }\n";
            m_statementId++;      // the block's "{" is emitted above
            return code;
        }

        /// <summary>
        /// Rewrites two statements into equivalent ones the rest of the translator already
        /// handles, or returns null:
        ///
        ///   "--a[1]"  becomes "a[1]--". As statements the prefix and postfix forms differ
        ///             only in the value they yield, which a statement discards.
        ///   "g += 5"  becomes "g = g + 5" when g is a global. The compound form has nowhere
        ///             to read the old value from -- a global is not a C# local -- while the
        ///             spelled-out form goes through the global read and the write-back that
        ///             an ordinary assignment already emits.
        /// </summary>
        string TryRewriteStep(string statement)
        {
            var trimmed = statement.Trim().TrimEnd(';').Trim();

            if (trimmed.StartsWith("++") || trimmed.StartsWith("--"))
            {
                var target = trimmed.Substring(2).Trim();
                if (target.Length > 0 && (char.IsLetter(target[0]) || target[0] == '_'))
                {
                    return target + trimmed.Substring(0, 2);
                }
                return null;
            }

            return null;
        }

        /// <summary>
        /// Translates "g += 5" and "g++" on a global into the read-modify-write they stand
        /// for, or null when the statement is not one. The compound form has nowhere to read
        /// the old value from -- a global is not a C# local -- so the read, the operation and
        /// the write-back are emitted here rather than left to the assignment path.
        /// </summary>
        string TryBuildGlobalCompound(string statement)
        {
            var trimmed = statement.Trim().TrimEnd(';').Trim();
            string name = null;
            string op = null;
            string value = null;

            foreach (var candidate in new[] { "+=", "-=", "*=", "/=", "%=" })
            {
                int at = trimmed.IndexOf(candidate, StringComparison.Ordinal);
                if (at > 0)
                {
                    name = trimmed.Substring(0, at).Trim();
                    op = candidate.Substring(0, 1);
                    value = trimmed.Substring(at + candidate.Length).Trim();
                    break;
                }
            }
            if (name == null && (trimmed.EndsWith("++") || trimmed.EndsWith("--")))
            {
                name = trimmed.Substring(0, trimmed.Length - 2).Trim();
                op = trimmed.Substring(trimmed.Length - 1);
                value = "1";
            }
            if (name == null || !IsPlainName(name) || string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            // A global the script defines only later -- "counter++" in a handler -- is read by
            // name when the code runs, as for a read (IsLateBoundName), and a missing one fails
            // with the interpreter's own error for the operator.
            bool late = !IsInterpreterVariable(name) && !m_varLocals.Contains(name) && IsLateBoundName(name, false);
            if (!late && !IsInterpreterVariable(name))
            {
                return null;
            }

            m_usesInterpreter = true;
            var read = late ?
                "CscsLate.Existing(__interpreter, \"" + name + "\", \"" +
                    (value == "1" && trimmed.EndsWith(op + op) ? "Variable or function [" : "Object [") +
                    Constants.GetRealName(name) + "] doesn't exist.\")" :
                "__interpreter.GetVariableValue(\"" + name + "\")";
            // The interpreter's own compound (CscsOps.Compound): "g += \"!\"" on a number adds
            // the text's numeric value, which C#'s "+" on the Variable turned into "3!".
            var action = value == "1" && trimmed.EndsWith(op + op) ? op + op : op + "=";
            return m_depth + "__interpreter.AddGlobalOrLocalVariable(\"" + name +
                "\", new GetVarFunction(CscsOps.Compound(" + read + ", \"" + action +
                "\", (object)(" + ReplaceArgsInString(value) + "))));\n";
        }

        /// <summary>
        /// Moves a call that sits inside a subscript into a statement of its own, so that the
        /// index is a plain name by the time the subscript is built. Returns null when there
        /// is no such call. A Math call is left alone: that is C# already.
        /// </summary>
        string TryHoistSubscriptCall(string statement, string nextStatement, bool addNewVars)
        {
            var trimmed = statement.Trim();
            bool inQuotes = false;
            for (int i = 0; i < trimmed.Length; i++)
            {
                if (trimmed[i] == '"' && !IsEscapedQuote(trimmed, i))
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (inQuotes || trimmed[i] != '[')
                {
                    continue;
                }
                int close = FindMatchingBracket(trimmed, i);
                if (close < 0)
                {
                    return null;
                }
                var index = trimmed.Substring(i + 1, close - i - 1);
                // The subscripted name: on an argument, a Math call in the index is hoisted as
                // well -- the resolver takes it inline only for a local, and for an argument the
                // token was cut at the "(" ("x[Math.Abs"), leaving a stray "]" (CS1026).
                int nameStart = i;
                while (nameStart > 0 && (char.IsLetterOrDigit(trimmed[nameStart - 1]) || trimmed[nameStart - 1] == '_'))
                {
                    nameStart--;
                }
                var subscripted = trimmed.Substring(nameStart, i - nameStart);
                bool argumentWithCall = subscripted.Length > 0 && m_paramMap.ContainsKey(subscripted) && index.IndexOf('(') >= 0;
                if (MentionsScriptCall(index) || MentionsConversionCall(index) || argumentWithCall)
                {
                    var temp = "__idxVal" + (++m_tempVarId);
                    var rest = trimmed.Substring(0, i + 1) + temp + trimmed.Substring(close);
                    // No spaces around the "=": statements reach the translator stripped.
                    return ProcessStatement(temp + "=" + index, ";", addNewVars) +
                           ProcessStatement(rest, nextStatement, addNewVars);
                }
                i = close;
            }
            return null;
        }

        /// <summary>
        /// Whether the text calls a function the script itself defines. Those need the whole
        /// pipeline: the call becomes an interpreter callback plus a temporary.
        /// </summary>
        bool MentionsScriptCall(string text)
        {
            var interpreter = m_parentScript.InterpreterInstance;
            int i = 0;
            bool inQuotes = false;
            while (i < (text ?? "").Length)
            {
                if (text[i] == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                }
                if (!char.IsLetter(text[i]) && text[i] != '_')
                {
                    i++;
                    continue;
                }
                int start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }
                var name = text.Substring(start, i - start);
                // The function being translated is not registered yet, so a call to itself
                // is recognised by name.
                // An argument or a local of the name does not matter: a call is a function's.
                if (i < text.Length && text[i] == '(' && !Constants.RESERVED.Contains(name) &&
                    !IsMathFunction(name, out _) &&
                    (interpreter.GetFunction(name) is CustomFunction ||
                     string.Equals(name, m_functionName, StringComparison.OrdinalIgnoreCase) ||
                     // A function nothing defines yet -- defined further down the script -- is
                     // called by name when the code runs, as the interpreter calls it. Not a
                     // member (".fire(") and not inside a string.
                     (!inQuotes && (start == 0 || text[start - 1] != '.') &&
                      (IsLateFunctionName(name) || IsValueBuiltin(name)))))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether the text calls one of the conversions the translator maps to a complete C#
        /// call -- string(), int(), and their siblings. Those have to be resolved by the token
        /// loop, which consumes their arguments.
        /// </summary>
        static bool MentionsConversionCall(string text)
        {
            foreach (var name in new[] { "string", "int", "long", "bool", "double", "printc" })
            {
                int at = 0;
                while ((at = (text ?? "").IndexOf(name, at, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    int after = at + name.Length;
                    // A whole word followed by "(": not the tail of a longer name, and not a
                    // member of something else.
                    bool ownWord = at == 0 || (!char.IsLetterOrDigit(text[at - 1]) &&
                                               text[at - 1] != '_' && text[at - 1] != '.');
                    if (ownWord && after < text.Length && text[after] == '(')
                    {
                        return true;
                    }
                    at = after;
                }
            }
            return false;
        }

        /// <summary>
        /// Translates "r += value" on a local that holds a Variable into the interpreter's own
        /// compound operator, or null when the statement is not one: "r = r op value"
        /// (OperatorAssignFunction.ProcessOperator), so "r = 5; r += \"3\"" is "53".
        /// </summary>
        /// <summary>
        /// Translates "p.x += 5", and "p.x++" / "p.x--", into the read-apply-write the
        /// interpreter does, through the same Compound helper an element compound uses. One dot
        /// only: that is as far as the interpreter itself goes -- "p.kid.v += 1" still throws
        /// there, so compiling it would answer where the interpreter does not.
        /// </summary>
        /// <summary>
        /// Pulls "(b = &lt;comparison&gt;)" out of an expression into its own statement when b is a
        /// bool local and the group sits beside arithmetic -- the one shape the expression cannot
        /// express, since the group reaches C# as a bool. Never for a keyword's own condition:
        /// "if ((b = n > 2))" has a path of its own, and a "for" header must not be split.
        /// </summary>
        string TryHoistBoolGroupAssignment(string statement, string nextStatement, bool addNewVars)
        {
            var trimmed = (statement ?? "").Trim();
            if (trimmed.Length == 0 || trimmed.IndexOfAny(new[] { '"', '{', '}' }) >= 0 ||
                StartsWithKeyword(trimmed, Constants.IF) || StartsWithKeyword(trimmed, Constants.WHILE) ||
                StartsWithKeyword(trimmed, Constants.ELSE_IF) || StartsWithKeyword(trimmed, Constants.FOR))
            {
                return null;
            }
            for (int open = trimmed.IndexOf('('); open >= 0; open = trimmed.IndexOf('(', open + 1))
            {
                int close = FindMatchingParen(trimmed, open);
                if (close < 0)
                {
                    return null;
                }
                var inner = trimmed.Substring(open + 1, close - open - 1).Trim();
                int eq = inner.IndexOf('=');
                if (eq <= 0 || eq + 1 >= inner.Length || inner[eq + 1] == '=' ||
                    "<>!+-*/%".IndexOf(inner[eq - 1]) >= 0)
                {
                    continue;
                }
                var name = inner.Substring(0, eq).Trim();
                var value = inner.Substring(eq + 1).Trim();
                char before = open > 0 ? trimmed[open - 1] : '\0';
                char after = close + 1 < trimmed.Length ? trimmed[close + 1] : '\0';
                if (!IsPlainName(name) || !YieldsBool(value) ||
                    !m_localTypes.TryGetValue(name, out var nameType) || nameType != "bool" ||
                    (!IsArithmeticPosition(before) && !IsArithmeticPosition(after)))
                {
                    continue;
                }
                // A space either side of the name, and no parentheses. Statements arrive with
                // their whitespace stripped, so a bare name made "return(b=n>2)+b" into
                // "returnb+b" -- one token, and a callback to a function called "returnb". Keeping
                // the group's parentheses fixed that but left "(b)" beside the arithmetic, which
                // the bool-to-number rule does not see: it converts a bare token only. Nor may a
                // space follow the name -- that rule compares the resolved token with its own
                // trimmed text, and "b " is not "b".
                var rest = trimmed.Substring(0, open) + " " + name + trimmed.Substring(close + 1);
                return ProcessStatement(name + "=" + value, ";", addNewVars) +
                       ProcessStatement(rest, nextStatement, addNewVars);
            }
            return null;
        }

        string TryBuildMemberCompound(string statement)
        {
            var trimmed = statement.Trim().TrimEnd(';').Trim();
            string target = null;
            string value = null;
            string action = null;
            // "p.x++" is the interpreter's step (IncrementDecrementFunction), not "+= 1": on text
            // the step makes a number of it, where "+=" appended "1".
            string stepOp = null;
            foreach (var candidate in new[] { "+=", "-=", "*=", "/=", "%=" })
            {
                int at = trimmed.IndexOf(candidate, StringComparison.Ordinal);
                if (at > 0)
                {
                    target = trimmed.Substring(0, at).Trim();
                    value = trimmed.Substring(at + candidate.Length).Trim();
                    action = candidate;
                    break;
                }
            }
            if (action == null && (trimmed.EndsWith("++") || trimmed.EndsWith("--")))
            {
                // Only the statement form. "return ++p.x" is worth the field's new value, which
                // this shape does not produce, so that one is left to the interpreter.
                target = trimmed.Substring(0, trimmed.Length - 2).Trim();
                value = "1";
                action = trimmed.EndsWith("++") ? "+=" : "-=";
                stepOp = trimmed.EndsWith("++") ? "++" : "--";
            }
            if (action == null || string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(target))
            {
                return null;
            }
            int dot = target.IndexOf('.');
            if (dot <= 0 || target.IndexOf('.', dot + 1) >= 0)
            {
                return null;
            }
            var owner = target.Substring(0, dot).Trim();
            var field = target.Substring(dot + 1).Trim();
            bool elementOwner = owner.IndexOf('[') > 0;
            if (!IsPlainName(field) || IsVariableMember(field) ||
                (!elementOwner && (!IsPlainName(owner) || !m_variableLocals.Contains(owner))))
            {
                return null;
            }
            // The same statement-level flags TryBuildVariableCompound sets, for the same reason:
            // they still describe the previous statement until this one is tokenized, and the
            // field has to stay a Variable so Compound reads a string as a string.
            m_statementRelational = HasPlainRelational(trimmed);
            m_statementBitwise = HasPlainBitwise(trimmed);
            m_statementSameValue = trimmed.Contains(SAME_VALUE_CALL);
            m_statementInlineCalls = m_forceInlineCalls || NeedsInlineCalls(trimmed);
            m_statementHasString = trimmed.Contains("\"");
            m_statementVariableAccum = true;
            var outerPrelude = m_statementPrelude;
            m_statementPrelude = "";
            bool hasConversion = MentionsConversionCall(value);
            m_knownExpression = !hasConversion;
            var resolved = hasConversion ?
                ProcessStatement(value, "", false).Trim().TrimEnd(';') :
                ReplaceArgsInString(value);
            var valuePrelude = m_statementPrelude;
            m_statementPrelude = outerPrelude;
            // "a[0].v += 3" and "a[1].v++": the same read-apply-write on the element. The value
            // first, then the element -- the order OperatorAssignFunction evaluates them in.
            if (elementOwner)
            {
                string ownerPrelude;
                var valueTemp = "__memVal" + (++m_tempVarId);
                var valueLine = m_depth + "var " + valueTemp + " = Variable.ConvertToVariable(" + resolved + ");\n";
                var elemOwner = BuildElementOwnerForWrite(owner, out ownerPrelude);
                if (elemOwner == null)
                {
                    return null;
                }
                if (stepOp != null)
                {
                    return ownerPrelude + m_depth + "CscsFields.StepField(" + elemOwner + ", \"" + field + "\", \"" +
                           stepOp + "\", false);\n";
                }
                return valuePrelude + valueLine + ownerPrelude + m_depth + elemOwner +
                    ".SetProperty(\"" + field + "\", CscsConvert.Compound(" + elemOwner +
                    ".ReadField(\"" + field + "\").DeepClone(), " + valueTemp + ", \"" + action + "\"), null);\n";
            }
            // DeepClone before applying: GetProperty hands back the field BY REFERENCE, and for a
            // field the constructor never sets that reference is the class's own default, shared
            // by every instance. Compound mutates what it is given, so without the clone the
            // default itself moved -- "p.tag += \"z\"" over three calls read back "tzzz", the same
            // value from all three, since the returned Variable aliased that one default too.
            // The interpreter clones for the same reason (OperatorAssignFunction.ProcessOperator).
            if (stepOp != null)
            {
                return m_depth + "CscsFields.StepField(" + owner + ", \"" + field + "\", \"" + stepOp + "\", false);\n";
            }
            return valuePrelude + m_depth + owner + ".SetProperty(\"" + field + "\", CscsConvert.Compound(" +
                owner + ".ReadField(\"" + field + "\").DeepClone(), " + resolved + ", \"" + action + "\"), null);\n";
        }

        string TryBuildVariableCompound(string statement)
        {
            var trimmed = statement.Trim().TrimEnd(';').Trim();
            foreach (var candidate in new[] { "+=", "-=", "*=", "/=", "%=" })
            {
                int at = trimmed.IndexOf(candidate, StringComparison.Ordinal);
                if (at <= 0)
                {
                    continue;
                }
                var name = trimmed.Substring(0, at).Trim();
                var value = trimmed.Substring(at + candidate.Length).Trim();
                // A for header's step arrives with the header's ")" -- "i+=1)" -- which belongs
                // after the call, not inside it: "Compound(i, 1), \"+=\")" did not compile.
                var closing = "";
                while (value.EndsWith(")") && value.Count(ch => ch == ')') > value.Count(ch => ch == '('))
                {
                    value = value.Substring(0, value.Length - 1).TrimEnd();
                    closing += ")";
                }
                if (!IsPlainName(name) || !m_variableLocals.Contains(name) ||
                    m_paramMap.ContainsKey(name) || !m_newVariables.Contains(name) ||
                    string.IsNullOrWhiteSpace(value))
                {
                    return null;
                }
                // The statement-level flags are computed later, when the statement is
                // tokenized, so they still describe the previous one here. The value is
                // resolved with this statement's own: the element has to stay a Variable, so
                // that Compound sees a string as a string, while one inside an argument list
                // -- "t += Math.Abs(a[i])" -- still becomes a number.
                m_statementRelational = HasPlainRelational(trimmed);
                m_statementBitwise = HasPlainBitwise(trimmed);
                m_statementSameValue = trimmed.Contains(SAME_VALUE_CALL);
                m_statementInlineCalls = m_forceInlineCalls || NeedsInlineCalls(trimmed);
                m_statementHasString = trimmed.Contains("\"");
                m_statementVariableAccum = true;
                // A conversion in the value -- "q + string(m[q])" -- has to go through the
                // token loop, which consumes the call's arguments. The resolver does not: it
                // asks for the call, gets one complete with its arguments, and then emits
                // those arguments a second time. Everything else keeps the resolver, which is
                // what the accumulation rule above is written for.
                var outerPrelude = m_statementPrelude;
                m_statementPrelude = "";
                bool hasConversion = MentionsConversionCall(value);
                m_knownExpression = !hasConversion;
                var resolved = hasConversion ?
                    ProcessStatement(value, "", false).Trim().TrimEnd(';') :
                    ReplaceArgsInString(value);
                var valuePrelude = m_statementPrelude;
                m_statementPrelude = outerPrelude;
                // In a for header (the ")" came along) the step ends the header: no ";", which
                // ended the loop there with an empty body and ran the block once.
                // As a statement, published like any other write (RegisterVariableString): without
                // it a callback after "g += i" saw the old value, and the interpreter's copy was
                // never read back for this local either (ReadGlobalsThroughInterpreter).
                return closing.Length > 0 ?
                    valuePrelude + name + " = CscsConvert.Compound(" + name + ", " + resolved + ", \"" + candidate + "\")" + closing :
                    valuePrelude + m_depth + name + " = CscsConvert.Compound(" + name + ", " +
                    resolved + ", \"" + candidate + "\");\n" + RegisterVariableString(name);
            }
            return null;
        }

        /// <summary>
        /// Splits "a = b = value" into the statements it stands for, innermost first, or null
        /// when the statement is not one. Only plain names are accepted as targets, so an
        /// element or a field keeps its own path.
        /// </summary>
        /// <summary>
        /// A target a chained assignment can unroll onto: a plain name, or an element read whose
        /// index cannot change anything. The unrolling writes each target in its own statement and
        /// reads the one to its right, so an index with a side effect -- "a[i++]" -- would run
        /// twice, once as a target and once as the value beside it. Those keep the old path.
        /// </summary>
        bool IsChainTarget(string target)
        {
            var text = (target ?? "").Trim();
            if (IsPlainName(text))
            {
                return true;
            }
            // "p.x": a member of a local, both plain names. No index to run twice, and the plain
            // form "p.x = 9" already compiles, so the unrolling has something to unroll onto.
            // The owner has to be a name -- "f().x" or "a[0].v" would be evaluated twice.
            int memberDot = text.IndexOf('.');
            if (memberDot > 0 && text.IndexOf('.', memberDot + 1) < 0 && !text.EndsWith("]"))
            {
                return IsPlainName(text.Substring(0, memberDot).Trim()) &&
                       IsPlainName(text.Substring(memberDot + 1).Trim());
            }
            if (!text.EndsWith("]"))
            {
                return false;
            }
            int bracket = text.IndexOf('[');
            if (bracket <= 0 || !IsPlainName(text.Substring(0, bracket).Trim()))
            {
                return false;
            }
            var index = text.Substring(bracket + 1, text.Length - bracket - 2);
            return index.Trim().Length > 0 && index.IndexOf('(') < 0 &&
                   index.IndexOf("++", StringComparison.Ordinal) < 0 &&
                   index.IndexOf("--", StringComparison.Ordinal) < 0 &&
                   index.IndexOf('=') < 0;
        }

        static readonly System.Text.RegularExpressions.Regex s_leftmostChainTarget =
            new System.Text.RegularExpressions.Regex(@"^[A-Za-z_]\w*(\s*(\.\s*[A-Za-z_]\w*|\[[^\[\]=]+\]))+$");

        string TryBuildChainedAssignment(string statement, string nextStatement, bool addNewVars)
        {
            var trimmed = statement.Trim().TrimEnd(';').Trim();
            var parts = SplitTopLevelOn(trimmed, "=");
            if (parts.Count < 3)
            {
                return null;
            }
            // The unrolling below mentions every target but the leftmost twice -- as a target and
            // as the value of the next one out -- so those must be safe to evaluate twice. The
            // leftmost is written once, after the value, which is also when the interpreter
            // reads its index ("a[i] = i = 3" writes a[3]): any path of names, members and
            // subscripts will do -- "q.kid.x", "a[0].v", "a[i++]".
            for (int i = 0; i < parts.Count - 1; i++)
            {
                var target = parts[i].Trim();
                if (!IsChainTarget(target) && !(i == 0 && s_leftmostChainTarget.IsMatch(target)))
                {
                    return null;
                }
            }
            var value = parts[parts.Count - 1].Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            // No spaces around the "=": statements reach the translator with their whitespace
            // stripped, and putting any back changes how the sides resolve.
            var inner = parts[parts.Count - 2].Trim();
            var result = ProcessStatement(inner + "=" + value, ";", addNewVars);
            for (int i = parts.Count - 3; i >= 0; i--)
            {
                var target = parts[i].Trim();
                result += ProcessStatement(target + "=" + inner,
                                           i == 0 ? nextStatement : ";", addNewVars);
                inner = target;
            }
            return result;
        }

        /// <summary>
        /// Translates "g[i] = value" on a global into the read-modify-write it stands for,
        /// through the same SetVariable a local collection uses, or null when the statement is
        /// not one of those. The global is written back afterwards so the interpreter sees the
        /// change, exactly as a compound assignment to a global does.
        /// </summary>
        string TryBuildGlobalElementAssignment(string statement)
        {
            var trimmed = statement.Trim().TrimEnd(';').Trim();
            string target = null;
            string op = null;          // null for a plain store
            string value = null;

            // "g[i]++" and "g[i]--" stand for a step of one.
            if (trimmed.EndsWith("++") || trimmed.EndsWith("--"))
            {
                target = trimmed.Substring(0, trimmed.Length - 2).Trim();
                op = trimmed.Substring(trimmed.Length - 1);
                value = "1";
            }
            if (target == null)
            {
                foreach (var candidate in new[] { "+=", "-=", "*=", "/=", "%=" })
                {
                    int at2 = trimmed.IndexOf(candidate, StringComparison.Ordinal);
                    if (at2 > 0)
                    {
                        target = trimmed.Substring(0, at2).Trim();
                        op = candidate.Substring(0, 1);
                        value = trimmed.Substring(at2 + candidate.Length).Trim();
                        break;
                    }
                }
            }
            if (target == null)
            {
                var halves = SplitTopLevelOn(trimmed, "=");
                if (halves.Count != 2 || string.IsNullOrWhiteSpace(halves[1]))
                {
                    return null;
                }
                target = halves[0].Trim();
                value = halves[1].Trim();
            }
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            int bracket = target.IndexOf('[');
            if (bracket <= 0 || !target.EndsWith("]"))
            {
                return null;
            }
            var name = target.Substring(0, bracket).Trim();
            // A global the script defines only later -- "volaParamsData[stock] = arg" in a
            // handler -- is written the same way, looked up when the code runs; missing, it is a
            // new collection of the function's own, as the interpreter makes one.
            bool late = IsPlainName(name) && !IsInterpreterVariable(name) && IsLateBoundName(name, false) &&
                        !m_collectionLocals.Contains(name) && !m_variableLocals.Contains(name);
            // A local of the same name wins: it is a real C# variable and needs no callback.
            if (!IsPlainName(name) || !(late || IsInterpreterVariable(name)) ||
                m_paramMap.ContainsKey(name) || m_newVariables.Contains(name) ||
                m_collectionLocals.Contains(name) || m_variableLocals.Contains(name))
            {
                return null;
            }

            // "g[i][j] = v" indexes down to the last subscript and sets that one.
            var indices = new List<string>();
            int at = bracket;
            while (at < target.Length && target[at] == '[')
            {
                int close = FindMatchingBracket(target, at);
                if (close < 0)
                {
                    return null;
                }
                indices.Add(target.Substring(at + 1, close - at - 1));
                at = close + 1;
            }
            if (at != target.Length || indices.Count == 0)
            {
                return null;
            }

            // A CSCS call in the value has to run ahead of the store, exactly as it does for
            // a local element. This builder returns straight to ProcessStatement, so the
            // statements it produces are collected here rather than by the token loop.
            var outerPrelude = m_statementPrelude;
            m_statementPrelude = "";
            value = HoistConditionCalls(value, value, true);
            var valuePrelude = m_statementPrelude;
            m_statementPrelude = outerPrelude;

            m_usesInterpreter = true;
            var id = ++m_tempVarId;
            var temp = "__globElem" + id;
            var indexVar = "__globIdx" + id;
            var holder = temp;
            for (int k = 0; k < indices.Count - 1; k++)
            {
                holder += "[" + ReplaceArgsInString(indices[k]) + "]";
            }
            // The last index is read into a local: a compound assignment uses it twice, once
            // to read the element and once to store it back, and an index that is itself an
            // expression would otherwise be worked out twice.
            var newValue = op == null ?
                "Variable.ConvertToVariable(" + ReplaceArgsInString(value) + ")" :
                "CscsConvert.Compound(" + holder + "[" + indexVar + "], " +
                    ReplaceArgsInString(value) + ", \"" + op + "=\")";
            return valuePrelude +
                   m_depth + "var " + temp + " = " + (late ? "CscsLate.OrNewCollection(__interpreter, \"" + name + "\")" :
                       "__interpreter.GetVariableValue(\"" + name + "\")") + ";\n" +
                   m_depth + "var " + indexVar + " = Variable.ConvertToVariable(" +
                       ReplaceArgsInString(indices[indices.Count - 1]) + ");\n" +
                   m_depth + holder + ".SetVariable(" + indexVar + ", " + newValue + ");\n" +
                   m_depth + "__interpreter." + (late ? "AddCompiledLocalVariable" : "AddGlobalOrLocalVariable") +
                       "(\"" + name + "\", new GetVarFunction(" + temp + "));\n";
        }

        /// <summary>
        /// Translates "name &amp;= expr" and its |=, ^=, &lt;&lt;= and &gt;&gt;= siblings into the
        /// read-modify-write they stand for, or null when the statement is not one of those.
        /// </summary>
        string TryBuildBitwiseCompound(string statement)
        {
            var trimmed = statement.Trim().TrimEnd(';').Trim();
            foreach (var op in new[] { "<<=", ">>=", "&=", "|=", "^=" })   // the ones CSCS actually has
            {
                int at = trimmed.IndexOf(op, StringComparison.Ordinal);
                if (at <= 0)
                {
                    continue;
                }
                var name = trimmed.Substring(0, at).Trim();
                var value = trimmed.Substring(at + op.Length).Trim();
                if (string.IsNullOrWhiteSpace(value) || !IsPlainName(name) ||
                    (!m_newVariables.Contains(name) && !m_paramMap.ContainsKey(name)))
                {
                    return null;
                }
                var target = m_paramMap.ContainsKey(name) ? m_paramMap[name] : name;
                var binary = op.Substring(0, op.Length - 1);
                // An int, which converts to whatever the target is: a (double) here could not go
                // back into an int local ("y = n; y <<= 3" was CS0266).
                return m_depth + target + " = (int)" + target + " " + binary +
                    " (int)(" + ReplaceArgsInString(value) + ");\n" +
                    RegisterVariableString(name, target);
            }
            return null;
        }

        /// <summary>
        /// Translates "p.field = value" on a local holding a class instance, or null when the
        /// statement is not one. The instance keeps its fields where the interpreter's own
        /// property setter puts them, so the write goes through that rather than through a C#
        /// member that does not exist.
        /// </summary>
        /// <summary>
        /// The element a member write goes to, for "a[i].v" and "a[i][j].v" on a local that holds
        /// a collection: a temp assigned through CscsConvert.ElementForWrite, one subscript at a
        /// time, so each index is worked out once. Returns the temp's name and the statement that
        /// declares it, or null when the owner is not such an element.
        /// </summary>
        string BuildElementOwnerForWrite(string ownerText, out string prelude)
        {
            prelude = null;
            int bracket = ownerText.IndexOf('[');
            if (bracket <= 0 || !ownerText.EndsWith("]"))
            {
                return null;
            }
            var name = ownerText.Substring(0, bracket).Trim();
            if (!IsPlainName(name) || m_paramMap.ContainsKey(name) ||
                !(m_collectionLocals.Contains(name) || m_variableLocals.Contains(name)))
            {
                return null;
            }
            var holder = name;
            int at = bracket;
            int levels = 0;
            while (at < ownerText.Length && ownerText[at] == '[')
            {
                int close = FindMatchingBracket(ownerText, at);
                if (close < 0)
                {
                    return null;
                }
                var index = ReplaceArgsInString(ownerText.Substring(at + 1, close - at - 1));
                if (string.IsNullOrWhiteSpace(index))
                {
                    return null;
                }
                holder = "CscsConvert.ElementForWrite(" + holder + ", Variable.ConvertToVariable(" + index + "))";
                at = close + 1;
                levels++;
            }
            if (at != ownerText.Length || levels == 0)
            {
                return null;
            }
            var elem = "__memElem" + (++m_tempVarId);
            prelude = m_depth + "var " + elem + " = " + holder + ";\n";
            return elem;
        }

        string TryBuildFieldAssignment(string statement)
        {
            var trimmed = statement.TrimEnd().TrimEnd(';').TrimEnd().Trim();

            // Plain "=" only: "p.v++" and "p.v += 2" take the member-step and member-compound
            // paths (CscsFields.StepField, the compound builders).

            var sides = SplitTopLevelOn(trimmed, "=");
            if (sides.Count != 2)
            {
                return null;
            }
            var target = sides[0].Trim();
            int dot = target.IndexOf('.');
            if (dot <= 0)
            {
                return null;
            }
            var owner = target.Substring(0, dot);
            var field = target.Substring(dot + 1);
            // "p.kid[0] = n", "p.kid[\"a\"][1] = n": an element of a field's collection, set in the
            // collection itself (AssignFunction, OperatorAssignFunction.SetElementAt): the value
            // first, then the field, then each index; a level above the last must exist.
            int fieldBracket = field.IndexOf('[');
            if (fieldBracket > 0 && field.EndsWith("]") && IsPlainName(owner) && m_variableLocals.Contains(owner) &&
                IsPlainName(field.Substring(0, fieldBracket)) && !IsVariableMember(field.Substring(0, fieldBracket)) &&
                !string.IsNullOrWhiteSpace(sides[1]))
            {
                var holder = owner + ".ReadField(\"" + field.Substring(0, fieldBracket) + "\")";
                var indices = new List<string>();
                int at = fieldBracket;
                while (at < field.Length && field[at] == '[')
                {
                    int close = FindMatchingBracket(field, at);
                    if (close < 0)
                    {
                        return null;
                    }
                    indices.Add("Variable.ConvertToVariable(" + ReplaceArgsInString(field.Substring(at + 1, close - at - 1)) + ")");
                    at = close + 1;
                }
                if (at != field.Length || indices.Count == 0)
                {
                    return null;
                }
                string builtValue;
                var fieldValue = TryBuildArrayLiteral(sides[1].Trim(), out builtValue) ? builtValue :
                    "Variable.ConvertToVariable(" + ReplaceArgsInString(sides[1]) + ")";
                var fieldTemp = "__memVal" + (++m_tempVarId);
                for (int i = 0; i < indices.Count - 1; i++)
                {
                    holder = "CscsConvert.ElementForWrite(" + holder + ", " + indices[i] + ")";
                }
                return m_depth + "var " + fieldTemp + " = " + fieldValue + ";\n" +
                       m_depth + holder + ".SetVariable(" + indices[indices.Count - 1] + ", " + fieldTemp + ");\n";
            }
            // "a[0].v = 9": a member of an element. Written to the live element, which the
            // interpreter has done since its own version of this was fixed; the C# as it stood
            // declared "double a[0].v=9" (CS0650). The value is worked out before the subscript,
            // the order AssignFunction evaluates them in.
            if (owner.IndexOf('[') > 0 && target.LastIndexOf('.') == dot &&
                IsPlainName(field) && !IsVariableMember(field) && !string.IsNullOrWhiteSpace(sides[1]))
            {
                string builtElem;
                var elemValue = TryBuildArrayLiteral(sides[1].Trim(), out builtElem) ? builtElem :
                    "Variable.ConvertToVariable(" + ReplaceArgsInString(sides[1]) + ")";
                var valueTemp = "__memVal" + (++m_tempVarId);
                string ownerPrelude;
                var elemOwner = BuildElementOwnerForWrite(owner, out ownerPrelude);
                if (elemOwner != null)
                {
                    return m_depth + "var " + valueTemp + " = " + elemValue + ";\n" + ownerPrelude +
                           m_depth + elemOwner + ".SetProperty(\"" + field + "\", " + valueTemp + ", null);\n";
                }
            }
            // "p.kid.v = 9": the field written is the last one, on whatever the chain before it
            // reads -- the same GetProperty chain a read of "p.kid.v" builds. Only a plain field
            // at every step: a Variable member or a call in the middle is not a field. Before,
            // this returned null and the ordinary path declared "double p.kid.v=9", and a
            // three-deep "p.kid.kid = new Named(..)" failed the same way after its hoisting.
            var ownerExpression = owner;
            int lastDot = target.LastIndexOf('.');
            if (lastDot > dot)
            {
                foreach (var segment in target.Substring(dot + 1, lastDot - dot - 1).Split('.'))
                {
                    if (!IsPlainName(segment) || IsVariableMember(segment))
                    {
                        return null;
                    }
                    ownerExpression += ".ReadField(\"" + segment + "\")";
                }
                field = target.Substring(lastDot + 1);
            }
            if (!IsPlainName(owner) || !IsPlainName(field) ||
                !m_variableLocals.Contains(owner) || IsVariableMember(field) ||
                string.IsNullOrWhiteSpace(sides[1]))
            {
                return null;
            }

            string built;
            var value = TryBuildArrayLiteral(sides[1].Trim(), out built) ? built :
                "Variable.ConvertToVariable(" + ReplaceArgsInString(sides[1]) + ")";
            return m_depth + ownerExpression + ".SetProperty(\"" + field + "\", " + value + ", null);\n";
        }

        /// <summary>
        /// Translates an assignment to a local that holds a Variable, or null when the
        /// statement is not one. Every value is wrapped so that a number, a string and a
        /// collection element all fit the one declaration.
        /// </summary>
        string TryBuildVariableLocalAssignment(string statement, bool addNewVars)
        {
            var trimmed = statement.TrimEnd().TrimEnd(';').TrimEnd();
            var sides = SplitTopLevelOn(trimmed, "=");
            if (sides.Count != 2)
            {
                return null;
            }
            var name = sides[0].Trim();
            var rhs = sides[1].Trim();
            if (!IsPlainName(name) || !m_variableLocals.Contains(name) ||
                string.IsNullOrWhiteSpace(rhs) || m_paramMap.ContainsKey(name))
            {
                return null;
            }
            // "new Point(3, 4)" builds a class instance through the interpreter, which the
            // ordinary path already emits as a callback; wrapping it here would put a C# "new"
            // in front of a type that does not exist.
            if (StartsWithKeyword(rhs, "new"))
            {
                return null;
            }
            // "t = s.At(1)" on a Variable: a member C# does not have goes to the interpreter
            // as a callback, which the ordinary path emits, as it does for "return s.At(1)".
            if (IsUnmappedMemberCallOnVariable(rhs))
            {
                return null;
            }
            // A script call nested inside another's arguments -- "helper(helper(\"a\") + \"b\")" --
            // needs the call path, which builds each call; ReplaceArgsInString here wrote the
            // function names out as C#.
            if (MentionsScriptCall(rhs) && NestsScriptCall(rhs))
            {
                return null;
            }
            // A string with an escaped backslash -- "C:\\dir\\" + b: ReplaceArgsInString halves
            // backslash runs, which suits the escaped text of its other callers but not source,
            // and "a\\" came out "a\" (CS1010). The ordinary path keeps it.
            if (rhs.Contains("\\\\"))
            {
                return null;
            }
            // "length = size(text)": a built-in of the interpreter's, which C# does not have
            // (CS0103). The ordinary path calls it back, as it does for "return Size(text)".
            if (CallsInterpreterBuiltin(rhs))
            {
                return null;
            }
            // "t = Math.Sin(x)" with x a Variable: C#'s Math takes numbers, and the interpreter's
            // own Math functions read a Variable in their own way (the numeric field, so text is
            // 0; a truth value 1). The interpreter runs it, on the values already at hand.
            var builtin = TryBuildMathOnVariable(rhs);
            if (builtin != null)
            {
                bool wasDeclared = m_newVariables.Contains(name);
                m_newVariables.Add(name);
                m_usesInterpreter = true;
                var builtinCode = m_depth + (wasDeclared ? "" : "Variable ") + name + " = " + builtin + ";\n";
                return addNewVars ? builtinCode + RegisterVariableString(name, name) : builtinCode;
            }
            // A compound assignment ("v += 1") leaves its operator on the left-hand side and
            // is not a plain store, so it keeps the ordinary path.
            if (!IsPlainName(sides[0].TrimEnd()) && sides[0].TrimEnd().Length != name.Length)
            {
                return null;
            }
            // This builder runs before the comparison rewrite, so a value that compares has to
            // be rewritten here: "r = a[0] == \"x\"" otherwise reached C# as a Variable
            // compared with "==" to a string, which does not compile.
            if (TryRewriteStringComparison(rhs, out var comparedValue))
            {
                rhs = comparedValue;
            }

            bool declared = m_newVariables.Contains(name);
            m_newVariables.Add(name);
            string built;
            // A member read off an element -- "pts[1].Sum()", "pts[0].fx + pts[0].fy" -- is
            // built by the expression builder's element branch, which runs only for a known
            // expression. Without it the member went out as C# on a Variable (CS1929, CS1061),
            // while "r += pts[1].Sum()" and "return pts[1].Sum()" already compiled. The element
            // is certainly a Variable here, which is what that branch assumes.
            var knownOuter = m_knownExpression;
            if (System.Text.RegularExpressions.Regex.IsMatch(Shape(rhs), @"\]\s*\.\s*[A-Za-z_]"))
            {
                m_knownExpression = true;
            }
            string value;
            try
            {
                value = TryBuildArrayLiteral(rhs, out built) ? built :
                    "Variable.ConvertToVariable(" + ReplaceArgsInString(rhs) + ")";
            }
            finally
            {
                m_knownExpression = knownOuter;
            }
            var code = m_depth + (declared ? "" : "Variable ") + name + " = " + value + ";\n";
            return addNewVars ? code + RegisterVariableString(name, name) : code;
        }

        /// <summary>
        /// Finds the locals that are assigned an element read out of a collection, so that
        /// they can be declared as Variable. Without this the declaration followed the first
        /// assignment -- "hits = 0" made a double -- and the later "hits = counts[key]" had a
        /// Variable with nowhere to go. A Variable holds a number or a string just as the
        /// interpreter does, so widening the local cannot change an answer.
        /// </summary>
        /// <summary>
        /// Locals whose type a compound or a step can change, since September 2026 when these
        /// became "x = x op y" (OperatorAssignFunction): text stepped or compounded with a
        /// number -- "7"++ is 8, "abc"++ is "abc1", "x" -= 1 an error --, a number with text added
        /// -- 3 += "!" is "3!" --, a comparison's truth value compounded. A C# string, double or
        /// bool cannot follow, so these are Variables, which hold whatever the step leaves.
        /// </summary>
        HashSet<string> TypeChangingLocals(List<string> statements, HashSet<string> textAssigned)
        {
            var result = new HashSet<string>();
            var comparisonAssigned = new HashSet<string>();
            var stepped = new HashSet<string>();
            var compoundedWithNumber = new HashSet<string>();
            var compoundedWithText = new HashSet<string>();
            var joinedText = new HashSet<string>();
            var stringArgs = new HashSet<string>(m_argsMap.Where(a => a.Value.Type == Variable.VarType.STRING)
                .Select(a => a.Key), StringComparer.OrdinalIgnoreCase);
            foreach (var statement in statements)
            {
                var line = (statement ?? "").Trim().TrimEnd(';').Trim();
                var bare = WithoutStringContents(line);
                foreach (System.Text.RegularExpressions.Match step in System.Text.RegularExpressions.Regex.Matches(bare,
                             @"(?<![\w.])([A-Za-z_]\w*)\s*(?:\+\+|--)|(?:\+\+|--)\s*([A-Za-z_]\w*)"))
                {
                    stepped.Add(step.Groups[1].Success ? step.Groups[1].Value : step.Groups[2].Value);
                }
                var compound = System.Text.RegularExpressions.Regex.Match(line, @"^([A-Za-z_]\w*)\s*([-+*/%])=(?!=)\s*(.+)$");
                if (compound.Success)
                {
                    var value = compound.Groups[3].Value.Trim();
                    bool text = value.IndexOf('"') >= 0 || stringArgs.Contains(value) || m_stringLocals.Contains(value) ||
                                textAssigned.Contains(value);
                    if (text)
                    {
                        compoundedWithText.Add(compound.Groups[1].Value);
                    }
                    else
                    {
                        compoundedWithNumber.Add(compound.Groups[1].Value);
                    }
                    continue;
                }
                var halves = SplitTopLevelOn(line, "=");
                if (halves.Count == 2 && IsPlainName(halves[0].Trim()))
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(WithoutStringContents(halves[1]), @"[<>]|==|!="))
                    {
                        comparisonAssigned.Add(halves[0].Trim());
                    }
                    // Text joined into it -- "x = s + n" -- holds text as a literal does.
                    else if (halves[1].IndexOf('"') >= 0 || MentionsAny(halves[1], stringArgs))
                    {
                        joinedText.Add(halves[0].Trim());
                    }
                }
            }
            var assignedHere = new HashSet<string>(statements.Select(s => AssignedName(s)).Where(n => n != null));
            foreach (var name in assignedHere)
            {
                if (m_paramMap.ContainsKey(name) || !IsPlainName(name) || IsInterpreterVariable(name))
                {
                    continue;
                }
                bool holdsText = textAssigned.Contains(name) || joinedText.Contains(name);
                if ((holdsText && (stepped.Contains(name) || compoundedWithNumber.Contains(name))) ||
                    (!holdsText && compoundedWithText.Contains(name)) ||
                    (comparisonAssigned.Contains(name) &&
                     (stepped.Contains(name) || compoundedWithNumber.Contains(name) || compoundedWithText.Contains(name))))
                {
                    result.Add(name);
                }
            }
            return result;
        }

        void CollectVariableLocals(List<string> statements)
        {
            m_variableLocals.Clear();

            // A "for (v in a)" variable holds an element, so it is a Variable, and so is
            // anything it feeds. Purely syntactic, like the subscript rule.
            var loopVars = new HashSet<string>();
            foreach (var statement in statements)
            {
                var head = (statement ?? "").Trim();
                if (!StartsWithKeyword(head, Constants.FOR))
                {
                    continue;
                }
                // The header has to be taken out of its parentheses before it is split:
                // stripping them by trimming left the depth unbalanced, so " in " counted as
                // nested and no loop variable was ever found.
                int open = head.IndexOf('(');
                int close = open < 0 ? -1 : FindMatchingParen(head, open);
                if (close < 0)
                {
                    continue;
                }
                var parts = SplitTopLevelOn(head.Substring(open + 1, close - open - 1), " in ");
                if (parts.Count == 2)
                {
                    var name = parts[0].Trim();
                    if (IsPlainName(name))
                    {
                        loopVars.Add(name);
                        m_variableLocals.Add(name);
                    }
                }
            }

            // A "catch (e)" binds a Variable too. Recorded only for a name the function also
            // assigns itself, where the catch rebinds that local (ProcessCatch): then its other
            // assignments have to declare it a Variable, or the catch's value has nowhere to go.
            var assignedAnywhere = new HashSet<string>(statements.Select(s => AssignedName(s)).Where(n => n != null));
            foreach (var statement in statements)
            {
                var head = (statement ?? "").Trim();
                if (!StartsWithKeyword(head, Constants.CATCH))
                {
                    continue;
                }
                int open = head.IndexOf('(');
                int close = open < 0 ? -1 : FindMatchingParen(head, open);
                var caughtName = close < 0 ? null : head.Substring(open + 1, close - open - 1).Trim();
                if (caughtName != null && IsPlainName(caughtName) && assignedAnywhere.Contains(caughtName))
                {
                    m_variableLocals.Add(caughtName);
                }
            }

            // Every name this function defines: its parameters, its loop variables, and
            // anything it assigns. An identifier in a value that is none of these comes from
            // outside, so the value is a Variable and the local holding it has to be one too.
            // Decided from the function's own text rather than by asking the interpreter,
            // which at this point would answer for names like "i" that scripts also use at
            // the top level, and would widen locals that are nothing of the sort.
            var defined = new HashSet<string>(loopVars);
            foreach (var param in m_paramMap.Keys)
            {
                defined.Add(param);
            }
            defined.UnionWith(m_collectionArgs);
            defined.UnionWith(m_widenedIntArgs);
            foreach (var statement in statements)
            {
                var name = AssignedName(statement);
                if (name != null)
                {
                    defined.Add(name);
                }
            }

            // Every assignment to the name has to be a string for it to count as one.
            var stringCandidates = new HashSet<string>();
            var notStrings = new HashSet<string>();
            foreach (var statement in statements)
            {
                var line = (statement ?? "").Trim().TrimEnd(';').Trim();
                var halves = SplitTopLevelOn(line, "=");
                if (halves.Count != 2 || !IsPlainName(halves[0].Trim()))
                {
                    continue;
                }
                var lhs = halves[0].Trim();
                var rhs = halves[1].Trim();
                if (rhs.StartsWith("\"") ||
                    (m_argsMap.TryGetValue(rhs, out var rhsArg) &&
                     rhsArg.Type == Variable.VarType.STRING))
                {
                    stringCandidates.Add(lhs);
                }
                else
                {
                    notStrings.Add(lhs);
                }
            }
            m_stringLocals.Clear();
            foreach (var name in stringCandidates)
            {
                if (!notStrings.Contains(name) && !m_paramMap.ContainsKey(name))
                {
                    m_stringLocals.Add(name);
                }
            }
            foreach (var name in TypeChangingLocals(statements, stringCandidates))
            {
                m_variableLocals.Add(name);
                m_stringLocals.Remove(name);
            }
            // A global this function assigns, where it calls back into the interpreter: the callee
            // can store a value of another type in it -- text where the function stored a number --
            // which a double or string local cannot hold, and reading it back converted it
            // (ReadGlobalsThroughInterpreter): "g = 5; setText(); return g;" answered 0 where the
            // interpreter answers the text. Such a local is a Variable, and so is what it feeds.
            if (m_emitInterpreterSync)
            {
                foreach (var name in GlobalBoundNames())
                {
                    m_variableLocals.Add(name);
                    m_stringLocals.Remove(name);
                }
            }

            // A "variable" argument holds whatever the caller passed -- a number, text, a
            // collection -- so what it feeds is a Variable too, as with a loop variable:
            // "x = n + 1", "a1 = n", "b = n * 2" were declared double and could not take it
            // (CS0029).
            var variableArgs = new HashSet<string>(m_argsMap
                .Where(arg => arg.Value.Type == Variable.VarType.VARIABLE).Select(arg => arg.Key), StringComparer.OrdinalIgnoreCase);
            // The same for a loop counter started from one -- "for (i = n; i > 0; i--)" -- whose
            // header is no plain assignment, so the pass below never sees it.
            foreach (var statement in statements)
            {
                var head = (statement ?? "").Trim();
                if (!StartsWithKeyword(head, Constants.FOR) || variableArgs.Count == 0)
                {
                    continue;
                }
                var init = System.Text.RegularExpressions.Regex.Match(head, @"^for\s*\(\s*([A-Za-z_]\w*)\s*=(?!=)(.*)$");
                // Only the initializer: the rest of the header is the condition and the step,
                // and "for (i = 1; i <= n; i++)" bounded by a Variable does not start from one.
                var initValue = init.Success ? init.Groups[2].Value : "";
                int initEnd = initValue.IndexOf(';');
                if (initEnd >= 0)
                {
                    initValue = initValue.Substring(0, initEnd);
                }
                // Not when the start is a script call's result, which the for header reads as a
                // number (".AsDouble()") whatever the arguments are.
                if (init.Success && MentionsAny(initValue, variableArgs) &&
                    !System.Text.RegularExpressions.Regex.IsMatch(initValue.Trim(), @"^[A-Za-z_]\w*\s*\([^()]*\)$"))
                {
                    m_variableLocals.Add(init.Groups[1].Value);
                }
            }

            // Repeated to a fixed point: "q = p.Kid()" makes q a Variable only once p is
            // known to be one, and a chain of those can run in either order in the source.
            bool grew = true;
            while (grew)
            {
            grew = false;
            foreach (var statement in statements)
            {
                var trimmed = (statement ?? "").Trim().TrimEnd(';').Trim();
                // A counter started from a local that holds a Variable -- "lower = m[k]; for (x =
                // lower; x < upper; x++)" -- holds one too; declared a double it could not take
                // the start (CS0029). Here, not above: the local is known only once this pass has
                // found it.
                var forInit = System.Text.RegularExpressions.Regex.Match(trimmed, @"^for\s*\(\s*([A-Za-z_]\w*)\s*=(?!=)([^;]*)");
                if (forInit.Success && !IsInterpreterVariable(forInit.Groups[1].Value) &&
                    System.Text.RegularExpressions.Regex.Matches(WithoutStringContents(forInit.Groups[2].Value), @"(?<![\w.])[A-Za-z_]\w*")
                        .Cast<System.Text.RegularExpressions.Match>().Any(word => m_variableLocals.Contains(word.Value)))
                {
                    grew |= m_variableLocals.Add(forInit.Groups[1].Value);
                    continue;
                }
                // A for header's condition arrives as a statement of its own, and "i<=n" split
                // on "=" read as "i = n": a counter bounded by a Variable became one.
                int firstEq = trimmed.IndexOf('=');
                if (firstEq > 0 && IsComparisonAt(trimmed, firstEq))
                {
                    continue;
                }
                var sides = SplitTopLevelOn(trimmed, "=");
                if (sides.Count != 2)
                {
                    // A compound assignment keeps its operator on the left, so it does not
                    // split cleanly -- but "t += a[i]" leaves t holding an element just as
                    // "t = t + a[i]" does.
                    int compound = trimmed.IndexOf("=", StringComparison.Ordinal);
                    if (compound <= 1 || !IsPlainName(trimmed.Substring(0, compound - 1).Trim()))
                    {
                        continue;
                    }
                    sides = new List<string> { trimmed.Substring(0, compound - 1),
                                               trimmed.Substring(compound + 1) };
                }

                // Contains a subscript, not just is one: "v = v + a[i]" leaves v holding
                // whatever the element is, exactly as "v = a[i]" does, and the element's type
                // is not known until it runs.
                // Only a subscript widens the local. A global read yields a Variable too, but
                // this pass runs before any local is known, so a name that also happens to be
                // a global -- "i", which scripts use at the top level -- would be read as one
                // and widen locals that are nothing of the sort. The target itself must be a
                // local: assigning to a global goes through the interpreter, and treating it
                // as a local here shadowed it instead.
                // A compound assignment leaves its operator on the left ("t +" from "t += x"),
                // and it widens the target exactly as the spelled-out form would.
                var target = sides[0].Trim().TrimEnd('+', '-', '*', '/', '%', '&', '|', '^').Trim();
                if (IsPlainName(target) && !IsInterpreterVariable(target) &&
                    // "new Point(3, 4)" yields a class instance, a Variable as well.
                    (ContainsSubscript(sides[1]) || MentionsAny(sides[1], loopVars) ||
                     MentionsAny(sides[1], variableArgs) ||
                     MentionsExternal(sides[1], defined) ||
                     StartsWithKeyword(sides[1].Trim(), "new") ||
                     // A call to a script function yields whatever it returned. Declared a
                     // double, "x = f(n); y = x + 1;" could not take the Variable at all.
                     // Not an argument: those have typed slots of their own.
                     (MentionsScriptCall(sides[1]) && !m_paramMap.ContainsKey(target))))
                {
                    grew |= m_variableLocals.Add(target);
                }
                else if (IsPlainName(target) && !IsInterpreterVariable(target) &&
                         !m_variableLocals.Contains(target) &&
                         // Arithmetic on one is a Variable as well, not just a member of one:
                         // "y = x + 1" with x a Variable.
                         (MentionsVariableLocal(sides[1]) ||
                          (MentionsRuntimeTyped(sides[1]) && !m_paramMap.ContainsKey(target))))
                {
                    // "q = p.Kid()" or "q = p.kid": whatever a Variable local yields is one.
                    grew |= m_variableLocals.Add(target);
                }
            }
            }
        }


        /// <summary>The statement after a leading "case X:" or "default:" label.</summary>
        static string WithoutCaseLabel(string statement)
        {
            if (!StartsWithKeyword(statement, Constants.CASE) && !StartsWithKeyword(statement, Constants.DEFAULT))
            {
                return statement;
            }
            bool inString = false;
            for (int i = 0; i < statement.Length; i++)
            {
                char c = statement[i];
                if (c == '"' && !IsEscapedQuote(statement, i))
                {
                    inString = !inString;
                }
                else if (!inString && c == ':')
                {
                    return statement.Substring(i + 1).Trim();
                }
            }
            return statement;
        }

        /// <summary>
        /// Declares, at the top of the function, every local that is used outside the block
        /// it first appears in. C# scopes a local to its block while CSCS does not, so
        /// "for (...) { c = a.At(i); } for (...) { c = b.At(i); }" declared c inside the first
        /// loop and referred to it, out of scope, in the second. The type comes from the same
        /// analysis the declarations use; whatever is assigned first then just assigns.
        /// Loop variables of "for (x in ...)" are left alone: each loop declares its own.
        /// </summary>
        void DeclareBlockCrossingLocals(List<string> statements)
        {
            var firstPath = new Dictionary<string, List<int>>();
            var crossing = new HashSet<string>();
            var loopVars = new HashSet<string>();
            var path = new List<int>();
            int blockId = 0;
            int literalDepth = 0;
            string previous = "";
            foreach (var raw in statements)
            {
                // "case 1:t=5" carries its label: what follows it is the statement.
                var statement = WithoutCaseLabel((raw ?? "").Trim());
                if (statement == "{")
                {
                    // A brace straight after "=", "(" or "," opens a literal, not a block.
                    if (literalDepth > 0 || previous.EndsWith("=") || previous.EndsWith("(") ||
                        previous.EndsWith(",") || previous.EndsWith(":") || previous == Constants.RETURN)
                    {
                        literalDepth++;
                    }
                    else
                    {
                        path.Add(++blockId);
                    }
                }
                else if (statement == "}")
                {
                    if (literalDepth > 0)
                    {
                        literalDepth--;
                    }
                    else if (path.Count > 0)
                    {
                        path.RemoveAt(path.Count - 1);
                    }
                }
                else if (statement.Length > 0 && statement != ";")
                {
                    if (StartsWithKeyword(statement, Constants.FOR) && statement.Contains(" in "))
                    {
                        var header = statement.Substring(Constants.FOR.Length).Trim().TrimStart('(').Trim();
                        int inAt = header.IndexOf(" in ", StringComparison.Ordinal);
                        if (inAt > 0)
                        {
                            loopVars.Add(header.Substring(0, inAt).Trim());
                        }
                    }
                    foreach (var name in MentionedNames(statement))
                    {
                        if (!firstPath.TryGetValue(name, out var first))
                        {
                            if (AssignedName(statement) == name)
                            {
                                firstPath[name] = new List<int>(path);
                            }
                            continue;
                        }
                        // Used where the first block does not reach: not inside it at all.
                        bool inside = first.Count <= path.Count;
                        for (int i = 0; inside && i < first.Count; i++)
                        {
                            inside = first[i] == path[i];
                        }
                        if (!inside)
                        {
                            crossing.Add(name);
                        }
                    }
                }
                if (statement.Length > 0 && statement != ";")
                {
                    previous = statement;
                }
            }

            // A "for (q in c)" or "catch (e)" over a name the function already holds rebinds that
            // local in CSCS; C# needs the outer declaration to be in scope for an assignment,
            // which it is for a name first assigned at the top level.
            m_topLevelAssigned = new HashSet<string>(
                firstPath.Where(entry => entry.Value.Count == 0).Select(entry => entry.Key));

            // "x = !x" as the first write of a global: the right side reads the interpreter's x,
            // which a C# declaration at that statement would shadow (CS0841). Declared at the top
            // without a value, as a bound crossing name is, so every read goes to the interpreter.
            foreach (var name in m_selfReadFirst)
            {
                if (!crossing.Contains(name) && firstPath.ContainsKey(name))
                {
                    crossing.Add(name);
                }
            }
            foreach (var name in crossing)
            {
                // A name that is the interpreter's -- a global already, or one read before it is
                // first assigned, "idx--; if (idx < 0) { idx = 5; } return idx;" -- is declared
                // too, but without a value: every read of it then goes to the interpreter
                // (ReadGlobalsThroughInterpreter). Started from 0, as it once was, the global was
                // never read. Where that pass cannot take every read -- a write it cannot see
                // published -- the unassigned local does not compile (CS0165), and the function
                // falls back.
                bool bound = !m_scriptInCSharp && (IsInterpreterVariable(name) || m_readFirst.Contains(name)) &&
                             !m_varLocals.Contains(name);
                if (loopVars.Contains(name) || m_newVariables.Contains(name) || m_paramMap.ContainsKey(name) ||
                    m_collectionArgs.Contains(name) || m_widenedIntArgs.Contains(name) ||
                    !firstPath.ContainsKey(name) || (firstPath[name].Count == 0 && !m_selfReadFirst.Contains(name)) ||
                    (!bound && (IsInterpreterVariable(name) || m_readFirst.Contains(name))))
                {
                    continue;
                }
                // The C# type every plain assignment to it agrees on. Where they do not, the
                // local is left as it was: the function then falls back, as it did before.
                string type = null;
                bool disagreed = false;
                foreach (var statement in statements)
                {
                    var line = WithoutCaseLabel((statement ?? "").Trim()).TrimEnd(';').Trim();
                    int eq = line.IndexOf('=');
                    if (AssignedName(line) != name || eq <= 0 || "+-*/%&|^".IndexOf(line[eq - 1]) >= 0)
                    {
                        continue;
                    }
                    var valueType = AssignedValueType(name, line.Substring(eq + 1).Trim());
                    if (type != null && type != valueType)
                    {
                        type = null;
                        disagreed = true;
                        break;
                    }
                    type = valueType;
                }
                // Assignments that really disagree -- "v = \"text\"" in one branch, "v = 5" in the
                // other -- still agree on one thing the interpreter can hold: a Variable. Declared
                // as one, and registered as Variable-valued so its members, comparisons and
                // arithmetic take Variable's own operators. Before this the local went undeclared
                // and every use of it was CS0103.
                //
                // Only on a real disagreement. "type == null" also means no plain assignment was
                // found at all -- "double result = 0;" is invisible to AssignedName, and
                // "result += x" is compound -- and declaring those put a second "result" into the
                // same method (CS0128), which aborted test.cscs's dllfunction before its first
                // assertion. And never for a C#-form body, which declares its own locals.
                // A name bound to the interpreter holds whatever the script put there -- read
                // before this function assigns it, text is as likely as a number -- so it is a
                // Variable, whose operators are the interpreter's: a double read "7" as 7, and
                // "fb = fb + 1" gave 8 where the interpreter gives "71".
                if ((disagreed || (bound && type != null)) && !m_scriptInCSharp)
                {
                    type = "Variable";
                    m_variableLocals.Add(name);
                }
                if (type == null)
                {
                    continue;
                }
                if (bound)
                {
                    m_converted.AppendLine("     " + type + " " + name + ";");
                    m_usesInterpreter = true;
                }
                else
                {
                    m_converted.AppendLine("     " + type + " " + name + " = " +
                        (type == "Variable" ? "null" : type == "string" ? "\"\"" : type == "bool" ? "false" : "0") + ";");
                }
                m_newVariables.Add(name);
                if (type == "Variable" && !m_variableLocals.Contains(name))
                {
                    m_collectionLocals.Add(name);
                }
            }

            // The same holds for a local that never crosses a block: "v = 0; v = \"text\";" and
            // "v = 0; if (n > 0) { v = \"text\"; }" declared v a double at its first assignment,
            // and the later one could not convert (CS0029). A real disagreement among its plain
            // assignments declares it a Variable up here instead, exactly as a crossing local
            // gets. Skipped for any name with a ternary assignment -- AssignedValueType reads
            // "n > 2 ? 1 : 2" as a bool because YieldsBool only looks for a comparison
            // character, which would invent a disagreement for a local that compiles today --
            // and never for a C#-form body, which declares its own locals.
            if (!m_scriptInCSharp)
            {
                var assignedOrder = new List<string>();
                foreach (var statement in statements)
                {
                    var line = (statement ?? "").Trim().TrimEnd(';').Trim();
                    int eq = line.IndexOf('=');
                    var assigned = AssignedName(line);
                    if (assigned == null || eq <= 0 || "+-*/%&|^".IndexOf(line[eq - 1]) >= 0 ||
                        assignedOrder.Contains(assigned))
                    {
                        continue;
                    }
                    assignedOrder.Add(assigned);
                }
                foreach (var name in assignedOrder)
                {
                    if (loopVars.Contains(name) || m_newVariables.Contains(name) || m_paramMap.ContainsKey(name) ||
                        m_collectionArgs.Contains(name) || m_widenedIntArgs.Contains(name) ||
                        IsInterpreterVariable(name) || m_readFirst.Contains(name))
                    {
                        continue;
                    }
                    string first = null;
                    bool mixed = false, ternary = false;
                    foreach (var statement in statements)
                    {
                        var line = (statement ?? "").Trim().TrimEnd(';').Trim();
                        int eq = line.IndexOf('=');
                        if (AssignedName(line) != name || eq <= 0 || "+-*/%&|^".IndexOf(line[eq - 1]) >= 0)
                        {
                            continue;
                        }
                        var value = line.Substring(eq + 1).Trim();
                        if (IndexOfTopLevelChar(value, '?') >= 0)
                        {
                            ternary = true;
                            break;
                        }
                        var valueType = AssignedValueType(name, value);
                        if (first != null && first != valueType)
                        {
                            mixed = true;
                        }
                        first = first ?? valueType;
                    }
                    if (!mixed || ternary)
                    {
                        continue;
                    }
                    m_converted.AppendLine("     Variable " + name + " = null;");
                    m_newVariables.Add(name);
                    m_variableLocals.Add(name);
                }
            }

            // An assignment inside a condition -- "while ((x = n - t) > 2)" -- is not a plain
            // statement, so AssignedName never saw it and nothing declared x (CS0103). The same
            // loop declared beforehand ("x = 0; while ((x = ...") always compiled. Declared up
            // here with the type every assignment to the name agrees on, the condition's and any
            // plain ones alike. A ternary or a Variable-typed value, or any disagreement, is left
            // alone: those fall back rather than getting a guessed type.
            if (!m_scriptInCSharp)
            {
                var conditionTypes = new Dictionary<string, string>();
                var refused = new HashSet<string>();
                foreach (var raw in statements)
                {
                    var statement = (raw ?? "").Trim();

                    // The parenthesis a keyword opens itself, if it opens one at all. Whitespace is
                    // stripped by now, so "return(b=n*2)+b" is shaped exactly like a call to a
                    // function named "return" -- the insideCall guard below skipped it, and nothing
                    // declared b (CS0103). Taking IndexOf('(') instead would be wrong the other way:
                    // in "return helper((q=n*2))" the first parenthesis is the call's, and that one
                    // must keep counting as a call.
                    int keywordParen = -1;
                    foreach (var keyword in new[] { Constants.ELSE_IF, Constants.IF, Constants.WHILE, Constants.RETURN })
                    {
                        if (!StartsWithKeyword(statement, keyword))
                        {
                            continue;
                        }
                        int k = keyword.Length;
                        while (k < statement.Length && char.IsWhiteSpace(statement[k])) { k++; }
                        keywordParen = k < statement.Length && statement[k] == '(' ? k : -1;
                        break;
                    }
                    // Elsewhere too -- "x = ((b = 7))", "return (b = n * 2) + b" -- but never a "for"
                    // header, whose own assignments are the counter's.
                    if (StartsWithKeyword(statement, Constants.FOR))
                    {
                        continue;
                    }
                    bool inQuotes = false;
                    for (int i = 0; i < statement.Length; i++)
                    {
                        char ch = statement[i];
                        if (ch == '"' && !IsEscapedQuote(statement, i))
                        {
                            inQuotes = !inQuotes;
                            continue;
                        }
                        if (inQuotes || ch != '(')
                        {
                            continue;
                        }
                        // Never inside a call's argument list. "f(a = 1)" names an argument, and in
                        // "Math.Max((q = n * 2), 5) + q" the math path does not carry the assignment out
                        // -- declaring q made compiled code answer 5 where the interpreter says 9. Only
                        // grouping parentheses outside every call count; a keyword's own condition
                        // parenthesis ("if(") is not a call.
                        bool insideCall = false;
                        for (int o = 0; o <= i && !insideCall; o++)
                        {
                            if (statement[o] != '(' || o == keywordParen || o == 0 ||
                                !(char.IsLetterOrDigit(statement[o - 1]) || statement[o - 1] == '_'))
                            {
                                continue;
                            }
                            int callClose = FindMatchingParen(statement, o);
                            insideCall = o == i || callClose < 0 || callClose > i;
                        }
                        if (insideCall)
                        {
                            continue;
                        }
                        int j = i + 1;
                        while (j < statement.Length && char.IsWhiteSpace(statement[j])) { j++; }
                        int nameStart = j;
                        while (j < statement.Length && (char.IsLetterOrDigit(statement[j]) || statement[j] == '_')) { j++; }
                        if (j == nameStart || char.IsDigit(statement[nameStart]))
                        {
                            continue;
                        }
                        var condName = statement.Substring(nameStart, j - nameStart);
                        while (j < statement.Length && char.IsWhiteSpace(statement[j])) { j++; }
                        if (j >= statement.Length || statement[j] != '=' ||
                            (j + 1 < statement.Length && statement[j + 1] == '='))
                        {
                            continue;
                        }
                        int close = FindMatchingParen(statement, i);
                        if (close < 0)
                        {
                            continue;
                        }
                        var value = statement.Substring(j + 1, close - j - 1).Trim();
                        var valueType = IndexOfTopLevelChar(value, '?') >= 0 ? null : AssignedValueType(condName, value);
                        // "while ((x = n - t) > 2)" with n a "variable" argument, or a local holding a
                        // Variable: x holds whatever the arithmetic on it gives, a Variable. Declared
                        // one, its later reads and writes take the Variable paths.
                        if (valueType == "double" &&
                            (MentionsAny(value, VariableParamNames()) || MentionsAny(value, m_variableLocals)))
                        {
                            valueType = VARIABLE_FED;
                        }
                        // A truth value is fine now. It was refused while "if ((b = n > 2))" threw a
                        // NullReferenceException in the interpreter itself -- AssignFunction ate a
                        // second ")" -- and compiled code answering 1 would have differed from it.
                        if (valueType == null || valueType == "Variable" ||
                            (conditionTypes.TryGetValue(condName, out var seen) && seen != valueType))
                        {
                            refused.Add(condName);
                            continue;
                        }
                        conditionTypes[condName] = valueType;
                    }
                }
                foreach (var entry in conditionTypes)
                {
                    var name = entry.Key;
                    if (refused.Contains(name) || loopVars.Contains(name) || m_newVariables.Contains(name) ||
                        m_paramMap.ContainsKey(name) || m_collectionArgs.Contains(name) ||
                        m_widenedIntArgs.Contains(name) || IsInterpreterVariable(name))
                    {
                        continue;
                    }
                    if (entry.Value == VARIABLE_FED)
                    {
                        // A Variable takes any value, so the plain assignments need not agree.
                        m_converted.AppendLine("     Variable " + name + " = Variable.EmptyInstance;");
                        m_newVariables.Add(name);
                        m_variableLocals.Add(name);
                        continue;
                    }
                    bool agrees = true;
                    foreach (var statement in statements)
                    {
                        var line = (statement ?? "").Trim().TrimEnd(';').Trim();
                        int eq = line.IndexOf('=');
                        if (AssignedName(line) != name || eq <= 0 || "+-*/%&|^".IndexOf(line[eq - 1]) >= 0)
                        {
                            continue;
                        }
                        var plain = line.Substring(eq + 1).Trim();
                        if (IndexOfTopLevelChar(plain, '?') >= 0 || AssignedValueType(name, plain) != entry.Value)
                        {
                            agrees = false;
                            break;
                        }
                    }
                    if (!agrees)
                    {
                        continue;
                    }
                    m_converted.AppendLine("     " + entry.Value + " " + name + " = " +
                        (entry.Value == "string" ? "\"\"" : entry.Value == "bool" ? "false" : "0") + ";");
                    m_newVariables.Add(name);
                    // Recorded too, so a later read of a bool one -- "b + 10" -- gets the conversion
                    // to the number CSCS uses; the plain-assignment record never sees this name.
                    if (!m_localTypes.ContainsKey(name))
                    {
                        m_localTypes[name] = entry.Value;
                    }
                }
            }
        }

        /// <summary>
        /// Records, for every local a plain assignment names, the C# type all of its
        /// assignments agree on. DeclareBlockCrossingLocals works this out too, but only for
        /// the locals it has to declare at the top -- those used outside the block they first
        /// appear in -- and ".Type" is asked about ordinary locals as well: "x = n + 1;
        /// return x.Type;" never crosses a block. Where the assignments disagree the name is
        /// left out, so TryMapTypeMember declines and the function falls back.
        /// </summary>
        void CollectLocalTypes(List<string> statements)
        {
            var agreed = new Dictionary<string, string>();
            var conflicting = new HashSet<string>();
            foreach (var statement in statements)
            {
                var line = (statement ?? "").Trim().TrimEnd(';').Trim();
                int eq = line.IndexOf('=');
                var name = AssignedName(line);
                // Only plain assignments: a compound one ("t += x") keeps its operator in
                // front of the "=" and says nothing certain about the type on its own.
                if (name == null || eq <= 0 || "+-*/%&|^<>!".IndexOf(line[eq - 1]) >= 0 ||
                    !IsPlainName(name) || m_paramMap.ContainsKey(name))
                {
                    continue;
                }
                var value = line.Substring(eq + 1).Trim();
                // A ternary holds whatever its branches hold. AssignedValueType asks
                // YieldsBool, which only looks for a comparison character anywhere in the
                // value -- so "n = n % 2 == 0 ? n / 2 : 3 * n + 1" and
                // "y = n > 2 ? (x = 5) : (x = 7)" were both recorded as holding a bool, where
                // they hold numbers. Recorded as conflicting instead, which leaves the name
                // with no known type: nothing here may guess one.
                if (IndexOfTopLevelChar(value, '?') >= 0)
                {
                    conflicting.Add(name);
                    continue;
                }
                // An argument widened into a local, or a collection argument, is not a bool.
                if (m_widenedIntArgs.Contains(name) || m_collectionArgs.Contains(name))
                {
                    continue;
                }
                var valueType = AssignedValueType(name, value);
                // Joining collections yields their text, and ".Type" is answered from this
                // record at translation time -- leaving it "double" made "c = a + b; c.Type"
                // compile to the literal "NUMBER" against the interpreter's "STRING".
                // m_collectionLocals cannot be consulted here: it is filled while statements
                // are processed, which is after this pass. The operands are recognised instead
                // from this loop's own record -- a "{...}" literal arrives as "a =" with an
                // empty value, which AssignedValueType calls "Variable" -- and the literals
                // are always assigned before the join that reads them.
                if (valueType != "string" && SplitTopLevelOn(value, "+").Count > 1 &&
                    SplitTopLevelOn(value, "+").Any(part =>
                        agreed.TryGetValue(part.Trim(), out var partType) && partType == "Variable"))
                {
                    valueType = "string";
                }
                if (agreed.TryGetValue(name, out var already) && already != valueType)
                {
                    conflicting.Add(name);
                    continue;
                }
                agreed[name] = valueType;
            }
            foreach (var pair in agreed)
            {
                if (!conflicting.Contains(pair.Key))
                {
                    m_localTypes[pair.Key] = pair.Value;
                }
            }
        }

        /// <summary>
        /// Translates ".Type" -- in any case, as CSCS names are case-insensitive -- into the
        /// name of the CSCS type, which is what the interpreter answers with. A Variable has
        /// the member itself and only needs its canonical spelling; a C# double, string or
        /// bool has nothing of the kind, so the answer is written out as the literal the
        /// interpreter would give. Returns false unless the owner's type is known: a guessed
        /// literal would be a silent divergence, where a fallback is merely slower.
        /// </summary>
        bool TryMapTypeMember(string owner, string member, out string result)
        {
            result = null;
            var name = (member ?? "").Trim();
            // Only the bare property: "Type()" is not how the interpreter spells it, and
            // anything following it ("Type.Length") is a chain this does not handle.
            if (!name.Equals("type", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(owner) || !IsPlainName(owner.Trim()))
            {
                return false;
            }
            owner = owner.Trim();
            // A collection or a Variable local: Variable.Type answers the CSCS type already.
            if (m_collectionLocals.Contains(owner) || m_variableLocals.Contains(owner))
            {
                result = owner + ".Type";
                return true;
            }
            // An argument, whose type is declared. A collection argument is a Variable too.
            if (m_collectionArgs.Contains(owner))
            {
                result = owner + ".Type";
                return true;
            }
            if (m_argsMap.TryGetValue(owner, out var arg) && m_paramMap.TryGetValue(owner, out var slot))
            {
                if (arg.Type == Variable.VarType.STRING)
                {
                    result = "\"STRING\"";
                    return true;
                }
                if (arg.Type == Variable.VarType.NUMBER || arg.Type == Variable.VarType.INT)
                {
                    result = "\"NUMBER\"";
                    return true;
                }
                if (arg.Type == Variable.VarType.VARIABLE)
                {
                    result = slot + ".Type";
                    return true;
                }
                return false;
            }
            // An int argument widened into a double local is a number either way.
            if (m_widenedIntArgs.Contains(owner))
            {
                result = "\"NUMBER\"";
                return true;
            }
            // A local, if every assignment to it agreed on a type. A bool is a NUMBER to the
            // interpreter, which is what "b = n > 0; b.Type" answers there.
            if (m_localTypes.TryGetValue(owner, out var localType))
            {
                if (localType == "string")
                {
                    result = "\"STRING\"";
                    return true;
                }
                if (localType == "double" || localType == "bool")
                {
                    result = "\"NUMBER\"";
                    return true;
                }
                if (localType == "Variable")
                {
                    result = owner + ".Type";
                    return true;
                }
            }
            return false;
        }

        /// <summary>The C# type a local takes from this value, as DeclareBlockCrossingLocals uses it.</summary>
        string AssignedValueType(string name, string value)
        {
            // "m = {}" reaches here as "m =", the braces being tokens of their own.
            if (value.Length == 0 || value.StartsWith("{") || StartsWithKeyword(value, "new") ||
                m_variableLocals.Contains(name))
            {
                return "Variable";
            }
            if (IsStringOperand(value) ||
                SplitTopLevelOn(value, "+").Any(part => IsStringOperand(part.Trim())))
            {
                return "string";
            }
            // A comparison or a truth value is a C# bool, which is how the declaration an
            // unhoisted one gets ("var ok = n > 2") comes out too.
            if (value == Constants.TRUE || value == Constants.FALSE || YieldsBool(value))
            {
                return "bool";
            }
            return "double";
        }

        /// <summary>The plain names a statement mentions, outside string literals.</summary>
        static IEnumerable<string> MentionedNames(string text)
        {
            bool inQuotes = false;
            int i = 0;
            while (i < text.Length)
            {
                char ch = text[i];
                if (ch == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                    i++;
                    continue;
                }
                if (inQuotes || !(char.IsLetter(ch) || ch == '_') || (i > 0 && text[i - 1] == '.'))
                {
                    i++;
                    continue;
                }
                int start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }
                yield return text.Substring(start, i - start);
            }
        }

        /// <summary>Whether the text reads a member of a local that holds a Variable.</summary>
        bool MentionsVariableLocal(string text)
        {
            int i = 0;
            while (i < (text ?? "").Length)
            {
                if (!char.IsLetter(text[i]) && text[i] != '_')
                {
                    i++;
                    continue;
                }
                int start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }
                if (i < text.Length && text[i] == '.' &&
                    m_variableLocals.Contains(text.Substring(start, i - start)))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether the "=" at <paramref name="at"/> belongs to a comparison -- "<=", ">=", "!=" --
        /// rather than an assignment. "<<=" and ">>=" assign.
        /// </summary>
        static bool IsComparisonAt(string text, int at)
        {
            if (at <= 0)
            {
                return false;
            }
            char before = text[at - 1];
            return before == '!' || ((before == '<' || before == '>') && !(at >= 2 && text[at - 2] == before));
        }

        /// <summary>
        /// The name assigned by this statement, or null when it does not assign one. Covers
        /// the compound forms too, whose operator sits on the left of the "=".
        /// </summary>
        static string AssignedName(string statement)
        {
            var trimmed = (statement ?? "").Trim().TrimEnd(';').Trim();
            int at = trimmed.IndexOf('=');
            if (at <= 0 || (at + 1 < trimmed.Length && trimmed[at + 1] == '='))
            {
                return null;
            }
            // A comparison, not an assignment: "i<=n", "i>=n", "a!=b" -- a for header's condition
            // arrives as a statement of its own, and "i<=n" read as "i = n" made a counter bounded
            // by a Variable a Variable itself. "<<=" and ">>=" do assign.
            if (IsComparisonAt(trimmed, at))
            {
                return null;
            }
            var name = trimmed.Substring(0, at).TrimEnd('+', '-', '*', '/', '%',
                                                       '&', '|', '^', '!', '<', '>').Trim();
            // A "for" header assigns its counter: "for (i = 0" arrives with the keyword.
            if (StartsWithKeyword(name, Constants.FOR))
            {
                name = name.Substring(Constants.FOR.Length).Trim().TrimStart('(').Trim();
            }
            return IsPlainName(name) ? name : null;
        }

        /// <summary>
        /// Whether the text reads a name the function never defines -- a global, or anything
        /// else the interpreter holds. Calls and qualified names are skipped: "Math" in
        /// "Math.Sqrt(i)" is neither.
        /// </summary>
        static bool MentionsExternal(string text, HashSet<string> defined)
        {
            int i = 0;
            bool inQuotes = false;
            while (i < (text ?? "").Length)
            {
                char ch = text[i];
                if (ch == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                    i++;
                    continue;
                }
                if (inQuotes || (!char.IsLetter(ch) && ch != '_'))
                {
                    i++;
                    continue;
                }
                int start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }
                var name = text.Substring(start, i - start);
                bool qualifiedOrCall = i < text.Length && (text[i] == '(' || text[i] == '.');
                if (!qualifiedOrCall && !defined.Contains(name) &&
                    !Constants.RESERVED.Contains(name) && !IsNumber(name))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Whether the text uses any of these names as a whole word.</summary>
        static bool MentionsAny(string text, HashSet<string> names)
        {
            if (names.Count == 0 || string.IsNullOrEmpty(text))
            {
                return false;
            }
            int i = 0;
            while (i < text.Length)
            {
                if (!char.IsLetter(text[i]) && text[i] != '_')
                {
                    i++;
                    continue;
                }
                int start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }
                if (names.Contains(text.Substring(start, i - start)))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether a name is subscripted anywhere in the text, outside a string literal.
        /// </summary>
        static bool ContainsSubscript(string text)
        {
            bool inQuotes = false;
            for (int i = 1; i < (text ?? "").Length; i++)
            {
                if (text[i] == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                }
                else if (!inQuotes && text[i] == '[' &&
                         (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] == '_'))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether the text is nothing but a name with one or more subscripts -- "m[k]" or
        /// "g[b][r][c]" -- which is what yields a Variable. Anything else, including a
        /// subscript inside a larger expression, already converts to a number where it is used.
        /// </summary>
        static bool IsSubscriptExpression(string text)
        {
            int bracket = text.IndexOf('[');
            if (bracket <= 0 || !text.EndsWith("]") || !IsPlainName(text.Substring(0, bracket)))
            {
                return false;
            }
            int depth = 0;
            for (int i = bracket; i < text.Length; i++)
            {
                if (text[i] == '[') { depth++; }
                else if (text[i] == ']') { depth--; }
                else if (depth == 0) { return false; }   // something between the subscripts
            }
            return depth == 0;
        }

        static bool IsPlainName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }
            foreach (var ch in name)
            {
                if (!char.IsLetterOrDigit(ch) && ch != '_')
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Applies the string-comparison rewrite to the condition of an "if"/"while", or to
        /// the expression of a "return", leaving the surrounding keyword alone. Returns null
        /// when there is nothing to rewrite.
        /// </summary>
        string TryRewriteStatementComparison(string statement)
        {
            var trimmed = statement.TrimStart();
            var indent = statement.Substring(0, statement.Length - trimmed.Length);

            // "elif" and "else if" too: their conditions compare exactly as an "if" does, and
            // without them "elif (s < \"b\")" reached C# as a string ordered with "<". The keyword
            // is kept as written, so the "elif" -> "else if" step that follows still applies.
            foreach (var keyword in new[] { Constants.IF, Constants.WHILE, Constants.RETURN,
                                            Constants.ELSE_IF, "else if" })
            {
                if (!trimmed.StartsWith(keyword, StringComparison.OrdinalIgnoreCase) ||
                    (trimmed.Length > keyword.Length && char.IsLetterOrDigit(trimmed[keyword.Length])))
                {
                    continue;
                }

                var rest = trimmed.Substring(keyword.Length);
                // Keep whatever follows the condition -- the "{" of the block, the ";" of a
                // return -- out of the rewrite.
                var tail = "";
                if (keyword == Constants.RETURN)
                {
                    int semi = rest.LastIndexOf(';');
                    if (semi >= 0)
                    {
                        tail = rest.Substring(semi);
                        rest = rest.Substring(0, semi);
                    }
                }
                else
                {
                    int open = rest.IndexOf('(');
                    if (open < 0)
                    {
                        continue;
                    }
                    int close = FindMatchingParen(rest, open);
                    if (close < 0)
                    {
                        continue;
                    }
                    tail = rest.Substring(close + 1);
                    rest = rest.Substring(0, close + 1);
                }

                if (TryRewriteStringComparison(rest, out var rewritten))
                {
                    return indent + keyword + " " + rewritten + tail;
                }
                // A number as the whole condition -- "if (b)" over a double local, "if ((b = n + 2))",
                // "while ((t = t - 1))". The interpreter's truth for a number is exactly "!= 0"; C#
                // has no truth value for a double at all (CS0029). Only a certain number: a bool, a
                // Variable, an element or a string has its own handling.
                // Also a number beside "!", "&&" or "||" -- "if (!b)", "if (b && n < 5)". Each
                // clause that is certainly a number becomes "(x!=0)", or "(x==0)" under "!". The
                // connectives and every other clause stay exactly as written.
                if (keyword != Constants.RETURN && TryRewriteNumericTerms(rest, out var numericCondition))
                {
                    return indent + keyword + " " + numericCondition + tail;
                }
                // A bare Variable in an otherwise numeric condition -- "r && n < 5" -- is a
                // "known expression" and never reaches the token loop, so it is not read as a
                // number and does not compile. Rewriting it here instead was tried: the
                // ".AsDouble() != 0" then goes back through the pipeline as source text and
                // the statements that already worked stopped resolving, so it falls back.
                return null;
            }

            // No keyword: an assignment whose right-hand side compares. "r = s > \"m\";" is
            // as much a string comparison as the same test inside an "if", and left alone it
            // reached C# as "string > string", which does not compile.
            var halves = SplitTopLevelOn(trimmed, "=");
            if (halves.Count == 2 && IsPlainName(halves[0].Trim()))
            {
                var value = halves[1].TrimEnd();
                var semicolon = "";
                if (value.EndsWith(";"))
                {
                    semicolon = ";";
                    value = value.Substring(0, value.Length - 1);
                }
                if (TryRewriteStringComparison(value, out var rewrittenValue))
                {
                    // No spaces around the "=": statements arrive with their whitespace
                    // stripped, and putting any back changes how the sides resolve.
                    return indent + halves[0].Trim() + "=" + rewrittenValue + semicolon;
                }
            }
            return null;
        }

        /// <summary>
        /// Rewrites a relational comparison whose operands are strings into the
        /// string.Compare form the interpreter uses (Parser.MergeStrings), because C# has no
        /// &lt; or &gt; on strings and the generated code simply would not compile.
        /// Returns false and changes nothing unless one side is provably a string, so numeric
        /// comparisons keep going down their existing path untouched.
        /// </summary>
        bool TryRewriteStringComparison(string expr, out string rewritten)
        {
            rewritten = null;
            if (string.IsNullOrWhiteSpace(expr))
            {
                return false;
            }

            var trimmed = expr.Trim();

            // A condition arrives wrapped in its own parentheses -- "(s > \"aa\")". Unwrap,
            // rewrite the inside, and put them back.
            if (trimmed.StartsWith("(") && FindMatchingParen(trimmed, 0) == trimmed.Length - 1)
            {
                if (TryRewriteStringComparison(trimmed.Substring(1, trimmed.Length - 2), out var inner))
                {
                    rewritten = "(" + inner + ")";
                    return true;
                }
                return false;
            }

            // A ternary's condition ends at the top-level "?" -- the branches are values,
            // not operands. Splitting on the relational operator without this read the whole
            // ternary as the right-hand side, so "s > \"m\" ? \"hi\" : \"lo\"" came out as a
            // comparison against ("m" ? "hi" : "lo").
            int question = IndexOfTopLevelChar(trimmed, '?');
            if (question > 0)
            {
                // Each of the three parts can compare in its own right, so all three are
                // rewritten -- a nested "s > \"m\" ? (s > \"y\" ? ... : ...) : ..." has one in a
                // branch. The colon that closes this ternary is the one whose nesting is back
                // to zero, so a branch holding another ternary is not cut in half.
                int colon = MatchingTernaryColon(trimmed, question);
                if (colon < 0)
                {
                    return false;
                }
                var parts = new[]
                {
                    trimmed.Substring(0, question),
                    trimmed.Substring(question + 1, colon - question - 1),
                    trimmed.Substring(colon + 1)
                };
                bool anyPart = false;
                for (int part = 0; part < parts.Length; part++)
                {
                    if (TryRewriteStringComparison(parts[part], out var piece))
                    {
                        anyPart = true;
                        parts[part] = piece;
                    }
                }
                if (!anyPart)
                {
                    return false;
                }
                // No spaces: the statement arrived with its whitespace stripped.
                rewritten = parts[0] + "?" + parts[1] + ":" + parts[2];
                return true;
            }

            // Each side of && / || is its own comparison.
            foreach (var connective in new[] { "&&", "||" })
            {
                var clauses = SplitTopLevelOn(trimmed, connective);
                if (clauses.Count > 1)
                {
                    bool any = false;
                    var pieces = new List<string>();
                    foreach (var clause in clauses)
                    {
                        if (TryRewriteStringComparison(clause, out var piece))
                        {
                            any = true;
                            pieces.Add(piece);
                        }
                        else
                        {
                            pieces.Add(clause);
                        }
                    }
                    if (!any)
                    {
                        return false;
                    }
                    // No spaces around the connective: the statement arrived with its
                    // whitespace stripped, and putting any back left the clause after it
                    // unresolved -- it came out as an interpreter callback rather than the
                    // comparison it had just been rewritten into.
                    rewritten = string.Join(connective, pieces);
                    return true;
                }
            }

            // Longest operators first, so ">=" is not read as ">".
            foreach (var op in new[] { "===", "!==", "==", "!=", "<=", ">=", "<", ">", "*" })
            {
                var sides = SplitTopLevelOn(trimmed, op);
                if (sides.Count != 2)
                {
                    continue;
                }
                var left = sides[0].Trim();
                var right = sides[1].Trim();

                // "*" on a string joins rather than multiplies: MergeStrings concatenates the
                // trimmed text for it, exactly as it does for "+". C# has no "*" on strings at
                // all, so this never compiled. Numbers are left alone.
                if (op == "*")
                {
                    if (!IsStringOperand(left) && !IsStringOperand(right))
                    {
                        continue;
                    }
                    // Only when the two sides really are the operands of "*". With a "+" or
                    // "-" beside it at the top level, "*" binds tighter: in "\"\" + n * 100"
                    // it multiplies n, and taking "\"\" + n" as its left side joined text
                    // where the interpreter adds 1200 to the empty string.
                    if (SplitTopLevelOn(left, "+").Count > 1 || SplitTopLevelOn(left, "-").Count > 1 ||
                        SplitTopLevelOn(right, "+").Count > 1 || SplitTopLevelOn(right, "-").Count > 1)
                    {
                        return false;
                    }
                    rewritten = "(" + AsTrimmedText(left) + "+" + AsTrimmedText(right) + ")";
                    return true;
                }

                // "===" and "!==" are not C# at all. The strict form also requires the types
                // to match, so only a pair of strings is rewritten -- they compare ordinally,
                // which is what the interpreter does once the type check passes. A mixed pair
                // is left to the interpreter rather than folded to a constant false, since the
                // undefined cases make that unsafe.
                if (op == "===" || op == "!==")
                {
                    if (IsStringName(left) && IsStringOperand(right))
                    {
                        rewritten = (op == "!==" ? "!" : "") +
                            left + ".StrictEqCscs(" + right + ")";
                        return true;
                    }
                    // Two numbers behave exactly as "==" does, the type check having passed,
                    // so the operator is simply swapped. No spaces around it: statements
                    // reach the translator with their whitespace stripped, and adding any
                    // back makes the operands resolve differently.
                    // An int argument and a local every assignment agrees is a number count too:
                    // IsNumericOperand only knows arguments declared "double", so "n === 5" over
                    // "int n" and "v === 6" over a numeric local reached C# as "===" (CS1525).
                    if (IsStrictNumeric(left) && IsStrictNumeric(right))
                    {
                        rewritten = left + (op == "===" ? "==" : "!=") + right;
                        return true;
                    }
                    // Anything else -- a mixed pair, a Variable -- is asked of the interpreter's
                    // own strict comparison (Variable.SameValue with the operator). It used to be
                    // left to fall back: folding a mixed pair to false is unsafe because of the
                    // undefined cases, which the interpreter's merge handles as the script does.
                    if (!string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right))
                    {
                        rewritten = (op == "!==" ? "!" : "") +
                            "Variable.SameValue(" + left + "," + right + ",\"===\")";
                        return true;
                    }
                    return false;
                }

                // "==" and "!=" already compile when both sides are strings or both are
                // numbers. Only a mixed pair needs rewriting, and CSCS compares the two
                // AsString() forms for those -- C# has no operator at all.
                if (op == "==" || op == "!=")
                {
                    // A value whose type is only known at run time -- a collection element, or
                    // a local that holds one -- compared against a literal. C# has no "=="
                    // between a Variable and a string, and giving Variable one would change
                    // reference equality everywhere it is used, so the interpreter's own rule
                    // is called instead: compare as text if either side is a string, by value
                    // otherwise. That is what a switch label already does.
                    // Two of them compared with each other. C# reads that as reference
                    // equality on Variable, so "c[0] == c[1]" over {"a","a"} answered false
                    // where the interpreter answers true.
                    if (IsRuntimeTypedOperand(left) && IsRuntimeTypedOperand(right))
                    {
                        rewritten = (op == "!=" ? "!" : "") +
                            "Variable.SameValue(" + left + "," + right + ")";
                        return true;
                    }
                    if (IsRuntimeTypedOperand(left) && IsLiteralOperand(right))
                    {
                        rewritten = (op == "!=" ? "!" : "") +
                            "Variable.SameValue(" + left + "," + right + ")";
                        return true;
                    }
                    if (IsRuntimeTypedOperand(right) && IsLiteralOperand(left))
                    {
                        rewritten = (op == "!=" ? "!" : "") +
                            "Variable.SameValue(" + right + "," + left + ")";
                        return true;
                    }
                    // Or against a name whose type is fixed: "a[i] == n" with an int
                    // argument was Variable == int, which C# rejects, so a linear search
                    // never compiled.
                    if (IsRuntimeTypedOperand(left) && IsFixedTypeName(right))
                    {
                        rewritten = (op == "!=" ? "!" : "") +
                            "Variable.SameValue(" + left + "," + right + ")";
                        return true;
                    }
                    if (IsRuntimeTypedOperand(right) && IsFixedTypeName(left))
                    {
                        rewritten = (op == "!=" ? "!" : "") +
                            "Variable.SameValue(" + right + "," + left + ")";
                        return true;
                    }
                    // A global compared with text: "gstr == \"gs\"". The read yields a Variable,
                    // and C# has no "==" between that and a string -- nor can Variable be given
                    // one: an equality operator whose other parameter accepts null captures every
                    // "variable != null" in the interpreter and recursed through BothNumbers into
                    // a stack overflow. SameValue is the interpreter's own rule instead, text when
                    // either side is text. Only against a string: a global beside a number already
                    // compiles through the numeric operators.
                    // Against the literal null. The interpreter compares "==" as text whenever
                    // the sides are not both numbers (Parser.MergeCells), and null is
                    // Variable.EmptyInstance, whose text is "" -- so "v == null" is true for a null
                    // local, "" and an empty argument, and false for 0, {} and "gs". SameValue
                    // against "" is that comparison exactly. Before, "x == null" lost its paren and
                    // "null == x" compiled to a C# reference test that was always false -- the one
                    // silent divergence the probe set carried. "null == null" goes through the
                    // three-argument SameValue, which turns a C# null into the interpreter's null
                    // (the empty value) and asks its merge -- 1, as the interpreter answers; the
                    // two-argument form answered false for a C# null.
                    bool leftNull = left == Constants.NULL;
                    bool rightNull = right == Constants.NULL;
                    if (leftNull && rightNull)
                    {
                        rewritten = (op == "!=" ? "!" : "") + "Variable.SameValue(null,null,\"==\")";
                        return true;
                    }
                    if (leftNull != rightNull)
                    {
                        rewritten = (op == "!=" ? "!" : "") +
                            "Variable.SameValue(" + (leftNull ? right : left) + ",\"\")";
                        return true;
                    }
                    bool leftGlobal = IsPlainName(left) && IsInterpreterVariable(left);
                    bool rightGlobal = IsPlainName(right) && IsInterpreterVariable(right);
                    if ((leftGlobal && IsStringOperand(right)) || (rightGlobal && IsStringOperand(left)))
                    {
                        rewritten = (op == "!=" ? "!" : "") +
                            "Variable.SameValue(" + left + "," + right + ")";
                        return true;
                    }
                    bool mixed = (IsStringName(left) && IsNumericOperand(right)) ||
                                 (IsNumericName(left) && IsStringOperand(right));
                    if (!mixed)
                    {
                        return false;
                    }
                    // The left side is the receiver either way: CompareCscs has an overload
                    // for a string receiver taking a number and one for the reverse.
                    rewritten = left + ".CompareCscs(" + right + ") " + op + " 0";
                    return true;
                }

                // The comparison becomes a member call on one of the operands, because that
                // is the shape the token loop already knows how to resolve. The receiver has
                // to be a name rather than a literal, so if only the right-hand side names a
                // string the two are swapped and the operator flipped to match.
                string receiver, other, comparison = op;
                if (IsStringName(left))
                {
                    receiver = left;
                    other = right;
                }
                else if (IsStringName(right))
                {
                    receiver = right;
                    other = left;
                    comparison = op == "<" ? ">" : op == ">" ? "<" : op == "<=" ? ">=" : "<=";
                }
                else
                {
                    return false;   // neither side is known to be a string: leave it alone
                }

                if (!IsStringOperand(left) && !IsStringOperand(right))
                {
                    return false;   // numeric: leave it alone
                }

                // The operands stay as they were written: this runs before the statement is
                // tokenized, so resolving them here would hand already-generated C# back to
                // the token loop, which then tried to resolve it a second time.
                rewritten = receiver + ".CompareCscs(" + other + ") " + comparison + " 0";
                return true;
            }
            return false;
        }

        /// <summary>
        /// Whether this member is the marker the string-comparison rewrite emits, rather than
        /// anything a script can write.
        /// </summary>
        static bool IsCompareMarker(string member)
        {
            if (member == null)
            {
                return false;
            }
            var trimmed = member.TrimStart();
            // SameValue is one of these too: it is generated C#, not a CSCS member, and the
            // token loop otherwise read the second one in "a == x && b == y" as a call to an
            // unknown function and replaced it with an interpreter callback.
            return trimmed.StartsWith("IsTrue", StringComparison.Ordinal) ||
                trimmed.StartsWith("IsFalse", StringComparison.Ordinal) ||
                trimmed.StartsWith("CompareCscs", StringComparison.Ordinal) ||
                trimmed.StartsWith("StrictEqCscs", StringComparison.Ordinal) ||
                trimmed.StartsWith(SAME_VALUE_NAME, StringComparison.Ordinal) ||
                trimmed.StartsWith("ConvertToVariable", StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether the interpreter already holds a variable of this name -- a global, as far
        /// as the function being translated is concerned. Functions and everything else the
        /// interpreter knows are excluded: only a variable can be read as a value.
        /// </summary>
        /// <summary>
        /// Whether this name is a global holding an enum. Its members are read through the
        /// interpreter: an enum is a Variable of type ENUM whose member names live in its own
        /// map, so no C# name can stand for "Colors.Green".
        /// </summary>
        bool IsEnumGlobal(string name)
        {
            if (!IsInterpreterVariable(name))
            {
                return false;
            }
            var value = m_parentScript.InterpreterInstance.GetVariableValue(name);
            return value != null && value.Type == Variable.VarType.ENUM;
        }

        bool IsInterpreterVariable(string name)
        {
            if (m_paramMap.ContainsKey(name) || m_newVariables.Contains(name) || m_varLocals.Contains(name) ||
                Constants.RESERVED.Contains(name))
            {
                return false;
            }
            // GetFunction only searches functions, so the variable tables have to be asked
            // directly. GetVariable is the lookup the interpreter itself uses for a name in
            // an expression.
            return m_parentScript != null &&
                m_parentScript.InterpreterInstance.GetVariable(name, m_parentScript) is GetVarFunction;
        }

        /// <summary>Whether the operand is a number literal, or a name holding a number.</summary>
        /// <summary>
        /// Whether a strict comparison's operand is certainly a number: a literal, an argument
        /// declared double or int, or a local whose every assignment is a number and which does
        /// not hold a Variable. Anything whose type is settled only at run time is left out, so
        /// "===" on it keeps falling back rather than dropping the type check.
        /// </summary>
        /// <summary>
        /// Whether a parenthesised condition is a single term that is certainly a number: a
        /// numeric argument or local, or an assignment in parentheses to a name typed double.
        /// </summary>
        /// <summary>
        /// Rewrites every clause of a parenthesised condition that is certainly a number into a
        /// comparison with zero, keeping the connectives and the other clauses as written. False
        /// when no clause qualifies, so a condition that compiles today is left untouched.
        /// </summary>
        bool TryRewriteNumericTerms(string condition, out string rewritten)
        {
            rewritten = null;
            var text = (condition ?? "").Trim();
            if (text.Length < 2 || text[0] != '(' || FindMatchingParen(text, 0) != text.Length - 1)
            {
                return false;
            }
            bool any = false;
            var inner = text.Substring(1, text.Length - 2);
            // A Variable, an element or a string is read as a truth value only when the condition
            // joins clauses: on its own it already compiles through AsCondition, and that path
            // stays as it is. "!= 0" is wrong for those -- an element holding "abc" is true to the
            // interpreter (Variable.IsTrue), while AsDouble() would read it as 0.
            bool joined = SplitTopLevelOn(inner, "&&").Count > 1 || SplitTopLevelOn(inner, "||").Count > 1;
            var built = RewriteNumericClauses(inner, ref any, joined);
            if (!any)
            {
                return false;
            }
            rewritten = "(" + built + ")";
            return true;
        }

        string RewriteNumericClauses(string text, ref bool any, bool joined = false)
        {
            foreach (var connective in new[] { "||", "&&" })
            {
                var clauses = SplitTopLevelOn(text, connective);
                if (clauses.Count > 1)
                {
                    var parts = new List<string>();
                    foreach (var clause in clauses)
                    {
                        parts.Add(RewriteNumericClauses(clause, ref any, joined));
                    }
                    // No spaces: the statement arrived with its whitespace stripped.
                    return string.Join(connective, parts);
                }
            }
            var term = text.Trim();
            bool negated = false;
            while (term.StartsWith("!") && !term.StartsWith("!="))
            {
                negated = !negated;
                term = term.Substring(1).Trim();
            }
            var grouped = term.StartsWith("(") && FindMatchingParen(term, 0) == term.Length - 1 ?
                term : "(" + term + ")";
            if (term.Length == 0)
            {
                return text;
            }
            if (!IsNumericConditionTerm(grouped))
            {
                // A term whose type is settled when it runs: an element, a local holding a
                // Variable, a string. CscsConvert.IsTrue/IsFalse are the interpreter's own
                // pair -- NOT one negated, see their comment.
                // A string term needs no connective: on its own it reaches C# as a string, which
                // has no truth value there either. "(t = s + \"x\")" keeps doing the assignment.
                // A comparison first: "vals[0]==\"ab\"" holds a string but is not a string term,
                // and calling it one skipped the operator check below and emitted a truth test
                // around a bool -- 45 comparison constructs fell back.
                bool comparison = term.IndexOfAny(new[] { '<', '>', '&', '|' }) >= 0 ||
                    term.Contains("==") || term.Contains("!=");
                bool stringTerm = !comparison &&
                    (IsStringOperand(term) ||
                     (IsPlainName(term) && m_localTypes.TryGetValue(term, out var loneType) && loneType == "string") ||
                     (term.StartsWith("(") && FindMatchingParen(term, 0) == term.Length - 1 &&
                      IsAssignedStringGroup(term)));
                // A lone term needs no connective when its truth cannot be read by C# at all: a
                // string, or a member off a Variable-holding local. Both reach C# as something
                // an "if" will not take (CS0029), so there is nothing to lose by rewriting them.
                if ((!joined && !stringTerm && !IsVariableMemberTerm(term)) || comparison ||
                    (!stringTerm && term.IndexOf('=') >= 0) ||
                    !(term.EndsWith("]") || IsVariableMemberTerm(term) ||
                      (IsPlainName(term) && (m_variableLocals.Contains(term) || IsStringOperand(term))) ||
                      (IsPlainName(term) && m_localTypes.TryGetValue(term, out var clauseType) && clauseType == "string") ||
                      stringTerm))
                {
                    return text;
                }
                any = true;
                return "CscsConvert." + (negated ? "IsFalse(" : "IsTrue(") + term + ")";
            }
            any = true;
            return "(" + term + (negated ? "==0" : "!=0") + ")";
        }

        /// <summary>Whether a parenthesised group assigns a name whose type is settled as string.</summary>
        bool IsAssignedStringGroup(string group)
        {
            var inner = group.Substring(1, group.Length - 2).Trim();
            int eq = inner.IndexOf('=');
            if (eq <= 0 || eq + 1 >= inner.Length || inner[eq + 1] == '=' || "<>!".IndexOf(inner[eq - 1]) >= 0)
            {
                return false;
            }
            var name = inner.Substring(0, eq).Trim();
            return IsPlainName(name) && m_localTypes.TryGetValue(name, out var type) && type == "string";
        }

        /// <summary>
        /// Whether the term reads a member off a local holding a Variable -- "p.y" on a class
        /// instance. The value is a Variable when it runs, so its truth is the interpreter's own
        /// test rather than a C# conversion (CS0029 before this). Length and Size are excluded:
        /// those are ints in C# and go the numeric route, which reads them as "!= 0".
        /// </summary>
        bool IsVariableMemberTerm(string term)
        {
            int dot = term.IndexOf('.');
            if (dot <= 0 || term.IndexOf('.', dot + 1) >= 0 || term.EndsWith("."))
            {
                return false;
            }
            var owner = term.Substring(0, dot).Trim();
            var member = term.Substring(dot + 1).Trim();
            return IsPlainName(owner) && IsPlainName(member) &&
                   member.ToLower() != "length" && member.ToLower() != "size" &&
                   (m_variableLocals.Contains(owner) || m_newVariables.Contains(owner));
        }

        bool IsNumericConditionTerm(string condition)
        {
            var text = (condition ?? "").Trim();
            // Peel the condition's own parentheses.
            while (text.Length >= 2 && text[0] == '(' && FindMatchingParen(text, 0) == text.Length - 1)
            {
                var inner = text.Substring(1, text.Length - 2).Trim();
                int eq = inner.IndexOf('=');
                // "(b = n + 2)": the term is the name assigned, when its type is settled as double.
                if (eq > 0 && eq + 1 < inner.Length && inner[eq + 1] != '=' && "<>!".IndexOf(inner[eq - 1]) < 0 &&
                    IsPlainName(inner.Substring(0, eq).Trim()))
                {
                    var assigned = inner.Substring(0, eq).Trim();
                    return !m_variableLocals.Contains(assigned) &&
                           m_localTypes.TryGetValue(assigned, out var assignedType) && assignedType == "double";
                }
                text = inner;
            }
            // "if (s.Length)" and "if (a.Size)": a number in CSCS and an int in C#, and the two
            // agree on every type -- Variable.Size is the interpreter's own (GetSize: the element
            // count, or the length of text), Variable.Length is GetLength(), and a string argument
            // arrives as a C# string whose Length is the character count (".Size" on it is mapped
            // to that). A C# condition needs a bool, so without the "!= 0" this rewrite adds it
            // was CS0029.
            int memberDot = text.LastIndexOf('.');
            if (memberDot > 0)
            {
                var owner = text.Substring(0, memberDot).Trim();
                var property = text.Substring(memberDot + 1).Trim().ToLower();
                if ((property == "length" || property == "size") && IsPlainName(owner) &&
                    (m_paramMap.ContainsKey(owner) || m_localTypes.ContainsKey(owner) ||
                     m_collectionLocals.Contains(owner) || m_variableLocals.Contains(owner) ||
                     m_newVariables.Contains(owner)))
                {
                    return true;
                }
            }
            // "if (b)": a plain name that is certainly a number, and not recorded as anything else.
            if (!IsPlainName(text) || !IsStrictNumeric(text) ||
                (m_localTypes.TryGetValue(text, out var recorded) && recorded != "double"))
            {
                return false;
            }
            // Nor a copy of another name: "c = b" records c as a double, but C# holds whatever b
            // is -- a bool, for "b = n > 1" -- and "c != 0" then does not compile where "if (c)"
            // already did.
            foreach (var statement in m_statements ?? new List<string>())
            {
                var line = (statement ?? "").Trim().TrimEnd(';').Trim();
                int eq = line.IndexOf('=');
                if (AssignedName(line) == text && eq > 0 && IsPlainName(line.Substring(eq + 1).Trim()))
                {
                    return false;
                }
            }
            return true;
        }

        bool IsStrictNumeric(string operand)
        {
            var name = (operand ?? "").Trim();
            if (IsNumericOperand(name))
            {
                return true;
            }
            if (!IsPlainName(name) || m_variableLocals.Contains(name) || m_collectionLocals.Contains(name))
            {
                return false;
            }
            if (m_argsMap.TryGetValue(name, out var arg))
            {
                return arg.Type == Variable.VarType.INT;
            }
            return m_localTypes.TryGetValue(name, out var type) && type == "double";
        }

        bool IsNumericOperand(string operand)
        {
            var name = operand.Trim();
            return IsNumber(name) || IsNumericName(name);
        }

        /// <summary>Whether the operand is a bare name holding a number.</summary>
        bool IsNumericName(string operand)
        {
            var name = operand.Trim();
            return IsPlainName(name) && m_argsMap.TryGetValue(name, out var arg) &&
                arg.Type == Variable.VarType.NUMBER;
        }

        /// <summary>
        /// Whether the operand is a bare name holding a string, which is what the rewritten
        /// comparison needs for a receiver. A literal cannot serve: the token loop reads it
        /// as a quoted string and the member access glued to it never resolves.
        /// </summary>
        /// <summary>
        /// Whether the operand holds a Variable, whose type is settled only when it runs: a
        /// local that holds an element or an instance, or a subscript on a collection local.
        /// </summary>
        bool IsRuntimeTypedOperand(string operand)
        {
            var name = (operand ?? "").Trim();
            if (IsPlainName(name))
            {
                return m_variableLocals.Contains(name);
            }
            int bracket = name.IndexOf('[');
            if (bracket > 0 && name.EndsWith("]") &&
                m_collectionLocals.Contains(name.Substring(0, bracket).Trim()))
            {
                return true;
            }
            // So is a call to a script function: its result is whatever it returned, and C# has
            // no "==" between that and a literal. "f(n) == \"s\"" did not compile.
            int open = name.IndexOf('(');
            if (open > 0 && FindMatchingParen(name, open) == name.Length - 1 &&
                IsPlainName(name.Substring(0, open)) && MentionsScriptCall(name))
            {
                return true;
            }
            // An expression over one is one as well: "q % 2" yields a Variable, and C# has no
            // "==" between that and a number either.
            return MentionsRuntimeTyped(name);
        }

        /// <summary>Whether the text reads a Variable-valued local or a collection element.</summary>
        bool MentionsRuntimeTyped(string text)
        {
            int i = 0;
            while (i < (text ?? "").Length)
            {
                if (!char.IsLetter(text[i]) && text[i] != '_')
                {
                    i++;
                    continue;
                }
                int start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }
                var word = text.Substring(start, i - start);
                if (m_variableLocals.Contains(word) ||
                    (i < text.Length && text[i] == '[' && m_collectionLocals.Contains(word)))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether the operand is a bare name with a C# type of its own: a scalar argument, or a
        /// local the translator declared as a string or a number. SameValue takes any of them.
        /// </summary>
        bool IsFixedTypeName(string operand)
        {
            var name = (operand ?? "").Trim();
            if (!IsPlainName(name) || m_variableLocals.Contains(name) || m_collectionLocals.Contains(name))
            {
                return false;
            }
            if (m_argsMap.TryGetValue(name, out var arg))
            {
                return arg.Type == Variable.VarType.NUMBER || arg.Type == Variable.VarType.INT ||
                       arg.Type == Variable.VarType.STRING;
            }
            return m_stringLocals.Contains(name) || m_newVariables.Contains(name);
        }

        /// <summary>Whether the operand is a literal string or number, written out in full.</summary>
        static bool IsLiteralOperand(string operand)
        {
            var text = (operand ?? "").Trim();
            return (text.StartsWith("\"") && text.EndsWith("\"") && text.Length >= 2) ||
                   IsNumber(text);
        }

        /// <summary>
        /// The operand as trimmed text, which is what MergeStrings joins for "*". A literal is
        /// trimmed here rather than at run time; anything else goes through Variable so that a
        /// number becomes its text the way the interpreter renders it.
        /// </summary>
        static string AsTrimmedText(string operand)
        {
            var text = operand.Trim();
            return text.StartsWith("\"") && text.EndsWith("\"") && text.Length >= 2 ?
                "\"" + text.Substring(1, text.Length - 2).Trim() + "\"" :
                "Variable.ConvertToVariable(" + text + ").AsString().Trim()";
        }

        bool IsStringName(string operand)
        {
            var name = operand.Trim();
            return !name.StartsWith("\"") && IsStringOperand(name);
        }

        /// <summary>
        /// Whether this operand is known to be a string: a literal, a string argument, or a
        /// member on one that yields a string. Anything less certain is treated as numeric,
        /// which leaves the expression exactly as it was.
        /// </summary>
        bool IsStringOperand(string operand)
        {
            var name = operand.Trim();
            if (name.StartsWith("\""))
            {
                return true;
            }
            int dot = name.IndexOf('.');
            var owner = dot > 0 ? name.Substring(0, dot) : name;
            // A local counts too, but only when every assignment to it is a string.
            if (dot < 0 && m_stringLocals.Contains(owner))
            {
                return true;
            }
            if (!m_argsMap.TryGetValue(owner, out var arg) || arg.Type != Variable.VarType.STRING)
            {
                return false;
            }
            // A member that turns the string into a number -- "s.Length", "s.Size" (the length
            // too), "s.IndexOf(..)", "s.LastIndexOf(..)" -- makes this a numeric comparison, not a
            // string one.
            if (dot < 0)
            {
                return true;
            }
            var member = name.Substring(dot + 1).Trim();
            return !member.StartsWith("Length", StringComparison.OrdinalIgnoreCase) &&
                   !member.StartsWith("Size", StringComparison.OrdinalIgnoreCase) &&
                   !member.StartsWith("IndexOf", StringComparison.OrdinalIgnoreCase) &&
                   !member.StartsWith("LastIndexOf", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether a literal's element is a "key : value" pair. A ternary carries a ":" of its
        /// own, so "{n > 2 ? 10 : 20, 5}" looked like a map and its first element like the
        /// entry "n > 2 ? 10" -- the literal was then never built and the name it assigns to
        /// did not exist. The "?" of a ternary comes before its ":".
        /// </summary>
        static bool IsMapEntry(string element)
        {
            var parts = SplitTopLevel(element, ':');
            if (parts.Count <= 1)
            {
                return false;
            }
            return IndexOfTopLevelChar(parts[0], '?') < 0;
        }

        /// <summary>Splits on a multi-character operator, ignoring quotes and any nesting.</summary>
        /// <summary>
        /// Position of the first <paramref name="target"/> outside any string literal and
        /// outside every bracket, or -1.
        /// </summary>
        static int IndexOfTopLevelChar(string text, char target)
        {
            int depth = 0;
            bool inQuotes = false;
            for (int i = 0; i < text.Length; i++)
            {
                char current = text[i];
                if (current == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                }
                else if (inQuotes) { }
                else if (current == '(' || current == '[' || current == '{') { depth++; }
                else if (current == ')' || current == ']' || current == '}') { depth--; }
                else if (depth == 0 && current == target) { return i; }
            }
            return -1;
        }

        /// <summary>
        /// Position of the ":" belonging to the "?" at <paramref name="question"/>, skipping
        /// the ones inside a nested ternary, a map literal or a bracket, or -1.
        /// </summary>
        static int MatchingTernaryColon(string text, int question)
        {
            int depth = 0;
            int pending = 0;
            bool inQuotes = false;
            for (int i = question + 1; i < text.Length; i++)
            {
                char current = text[i];
                if (current == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                }
                else if (inQuotes) { }
                else if (current == '(' || current == '[' || current == '{') { depth++; }
                else if (current == ')' || current == ']' || current == '}') { depth--; }
                else if (depth != 0) { }
                else if (current == '?') { pending++; }
                else if (current == ':')
                {
                    if (pending == 0) { return i; }
                    pending--;
                }
            }
            return -1;
        }

        static List<string> SplitTopLevelOn(string text, string separator)
        {
            var parts = new List<string>();
            int depth = 0;
            bool inQuotes = false;
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char current = text[i];
                if (current == '"' && !IsEscapedQuote(text, i))
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (inQuotes)
                {
                    continue;
                }
                if (current == '(' || current == '[' || current == '{') { depth++; continue; }
                if (current == ')' || current == ']' || current == '}') { depth--; continue; }
                if (depth != 0 || i + separator.Length > text.Length ||
                    string.CompareOrdinal(text, i, separator, 0, separator.Length) != 0)
                {
                    continue;
                }
                // No separator may match inside a longer operator: "<" inside "<=", or "=="
                // inside "===" and "!==".
                char after = i + separator.Length < text.Length ? text[i + separator.Length] : '\0';
                char before = i > 0 ? text[i - 1] : '\0';
                if (after == '=' || before == '=' || before == '!' ||
                    (separator.Length == 1 && (before == '<' || before == '>')))
                {
                    continue;
                }
                parts.Add(text.Substring(start, i - start));
                start = i + separator.Length;
                i += separator.Length - 1;
            }
            parts.Add(text.Substring(start));
            return parts;
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
                if (current == '"' && !IsEscapedQuote(text, i))
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
                if (current == '"' && !IsEscapedQuote(text, i))
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

                if (ch == '"' && !IsEscapedQuote(scriptText, i))
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
                        if ((token.EndsWith("=") || token.EndsWith("(") || token.EndsWith(",") ||
                             token.EndsWith("?") || token.EndsWith(":")) && ch == '{')
                        {
                            // An array or map literal in value position -- after "=", as a
                            // call argument after "(" or ",", or as a ternary branch after "?"
                            // or ":". Braces normally end a statement,
                            // which shredded "a = {1,2,3}" and "a.Add({1,2})" into separate
                            // pieces before translation ever saw them. Keep it in one piece.
                            int literalEnd = FindMatchingBrace(scriptText, i);
                            if (literalEnd > 0)
                            {
                                // Skip over the literal and keep accumulating; the statement
                                // ends at the real separator. Emitting a token here cut
                                // "a.Add({1,2});" short and left a stray ");" behind.
                                i = literalEnd + 1;
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
                if (ch == '"' && !IsEscapedQuote(statement, i))
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
                    // '~' is a prefix, not one of the interpreter's actions, so ValidAction
                    // does not see it and "~n" came through glued into a single token that
                    // was then looked up as a variable by that name.
                    if (candidate == null && bracketDepth == 0 &&
                        (Constants.STATEMENT_TOKENS.IndexOf(statement[i]) >= 0 ||
                                             ch == '?' || ch == ':' || ch == '~' ||
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

        /// <summary>System.Math's own spelling of a member written in any case; the text as it
        /// was when System.Math has no such member.</summary>
        static string CanonicalMathMember(string member)
        {
            var match = typeof(System.Math).GetMembers(BindingFlags.Public | BindingFlags.Static)
                .Select(m => m.Name)
                .FirstOrDefault(n => n.Equals(member, StringComparison.OrdinalIgnoreCase));
            return match ?? member;
        }

        // The bare names the script being translated has functions for, which a bare Math name
        // must not take over (IsMathFunction). Set for each translation, per thread.
        [ThreadStatic] static HashSet<string> s_scriptFunctions;

        // The bare names the function being translated assigns or takes as arguments, and
        // never calls: "round = b * 2; return round + 1;" is that variable, not System.Math's
        // method group (CS0019). One it also calls keeps the Math reading. Set per translation.
        [ThreadStatic] static HashSet<string> s_localNames;

        // What "a ** b" is rewritten to before translation; no script can name a function so.
        const string POWER_OP = "Math.PowerOp";

        // The declared type of a local first assigned inside a condition from a Variable.
        const string VARIABLE_FED = "Variable (fed)";

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
            if (name.StartsWith("Math.", StringComparison.OrdinalIgnoreCase))
            {
                // The "**" operator (TryRewritePower): Math.Pow for two numbers, the interpreter's
                // own operator otherwise -- not the Math.Pow built-in, which reads text as 0.
                if (name.Equals(POWER_OP, StringComparison.OrdinalIgnoreCase))
                {
                    corrected = "CscsOps.Power";
                    return true;
                }
                // CSCS spells this one Math.Ceil; C# only has Math.Ceiling, so passing the
                // script's spelling through produced code that did not compile and sent the
                // whole function back to the interpreter.
                if (name.Equals(Constants.MATH_CEIL, StringComparison.OrdinalIgnoreCase))
                {
                    corrected = Constants.MATH_CEILING;
                    return true;
                }
                // CSCS names are case-insensitive, so "Math.round", "Math.sqrt" and "math.Abs"
                // are what a script may well write; C# knows only System.Math's own spelling,
                // and the lower-case ones did not compile.
                corrected = "Math." + CanonicalMathMember(name.Substring(5));
                return true;
            }

            // A bare name the script defines a function for -- "function abs(x)" -- is that
            // function, as it is to the interpreter; mapping it to System.Math answered 5 where
            // the script's abs gave 95. Only a bare name: "Math.Abs" is always the built-in.
            if (s_scriptFunctions != null && s_scriptFunctions.Contains(name))
            {
                return false;
            }
            if (s_localNames != null && s_localNames.Contains(name))
            {
                return false;
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
                if (current == '"' && !IsEscapedQuote(text, i))
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
                if (current == '"' && !IsEscapedQuote(token, i))
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
