using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SplitAndMerge
{
    public class ParserFunction
    {
        public ParserFunction()
        {
            m_impl = this;
        }

        // A "virtual" Constructor
        internal ParserFunction(ParsingScript script, string item, char ch, ref string action)
        {
            if (item.Length == 0 && (ch == Constants.START_ARG || !script.StillValid()))
            {
                // There is no function, just an expression in parentheses
                m_impl = s_idFunction;
                return;
            }
            if (item.Length > 1 && item[0] == '"' && item[item.Length - 1] == '"')
            {
                // We are dealing with a string.
                s_strOrNumFunction.Item = item.Replace("\\\"", "\"");
                m_impl = s_strOrNumFunction;
                return;
            }

            m_impl = GetRegisteredAction(item, ref action);
            if (m_impl != null)
            {
                return;
            }

            m_impl = GetArrayFunction(item, script, action);
            if (m_impl != null)
            {
                return;
            }

            m_impl = GetFunction(item, script);
            if (m_impl != null)
            {
                return;
            }

            if (m_impl == s_strOrNumFunction && string.IsNullOrWhiteSpace(item))
            {
                string problem = (!string.IsNullOrWhiteSpace(action) ? action : ch.ToString());
                string restData = ch.ToString() + script.Rest;
                throw new ArgumentException("Couldn't parse [" + problem + "] in " + restData + "...");
            }

            // Function not found, will try to parse this as a string in quotes or a number.
            s_strOrNumFunction.Item = item;
            m_impl = s_strOrNumFunction;
        }

        public static ParserFunction GetArrayFunction(string name, ParsingScript script, string action)
        {
            int arrayStart = name.IndexOf(Constants.START_ARRAY);
            if (arrayStart < 0)
            {
                return null;
            }

            string arrayName = name;

            int delta = 0;
            List<Variable> arrayIndices = Utils.GetArrayIndices(script, ref arrayName, ref delta);

            if (arrayIndices.Count == 0)
            {
                return null;
            }

            ParserFunction pf = ParserFunction.GetFunction(arrayName, script);
            GetVarFunction varFunc = pf as GetVarFunction;
            if (varFunc == null)
            {
                return null;
            }

            // we temporarily backtrack for the processing
            script.Backward(name.Length - arrayStart - 1);
            script.Backward(action != null ? action.Length : 0);
            // delta shows us how manxy chars we need to advance forward in GetVarFunction()
            delta -= arrayName.Length;
            delta += action != null ? action.Length : 0;

            varFunc.Indices = arrayIndices;
            varFunc.Delta = delta;
            return varFunc;
        }

        public static ParserFunction GetRegisteredAction(string name, ref string action)
        {
            ActionFunction actionFunction = GetAction(action);

            // If passed action exists and is registered we are done.
            if (actionFunction == null)
            {
                return null;
            }

            ActionFunction theAction = actionFunction.NewInstance() as ActionFunction;
            theAction.Name = name;
            theAction.Action = action;

            action = null;
            return theAction;
        }

        public static ParserFunction GetFunction(string name, ParsingScript script)
        {
            ParserFunction impl;
            // First search among local variables.
            if (s_locals.Count > StackLevelDelta)
            {
                Dictionary<string, ParserFunction> local = s_locals.Peek().Variables;
                if (local.TryGetValue(name, out impl))
                {
                    // Local function exists (a local variable)
                    return impl;
                }
            }

            string scopeName = script == null || script.Filename == null ? "" : script.Filename;
            impl = GetLocalScopeVariable(name, scopeName);
            if (impl != null)
            {
                // Local scope variable exists
                return impl;
            }

            if (s_functions.TryGetValue(name, out impl))
            {
                // Global function exists and is registered (e.g. pi, exp, or a variable)
                return impl.NewInstance();
            }

            return null;
        }

        public static void UpdateFunction(Variable variable)
        {
            UpdateFunction(variable.ParsingToken, new GetVarFunction(variable));
        }
        public static void UpdateFunction(string name, ParserFunction function)
        {
            // First search among local variables.
            if (s_locals.Count > StackLevelDelta)
            {
                Dictionary<string, ParserFunction> local = s_locals.Peek().Variables;

                if (local.ContainsKey(name))
                {
                    // Local function exists (a local variable)
                    local[name] = function;
                    return;
                }
            }
            // If it's not a local variable, update global.
            s_functions[name] = function;
        }
        public static ActionFunction GetAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return null;
            }

            ActionFunction impl;
            if (s_actions.TryGetValue(action, out impl))
            {
                // Action exists and is registered (e.g. =, +=, --, etc.)
                return impl;
            }

            return null;
        }

        public static bool FunctionExists(string item)
        {
            // If it is not defined locally, then check globally:
            return LocalNameExists(item) || GlobalNameExists(item);
        }

        public static void AddGlobalOrLocalVariable(string name, ParserFunction function)
        {
            function.Name = name;
            if (s_locals.Count > StackLevelDelta && (LocalNameExists(name) || !GlobalNameExists(name)))
            {
                AddLocalVariable(function);
            }
            else
            {
                AddGlobal(name, function, false /* not native */);
            }
        }

        static string CreateVariableEntry(ParserFunction variable, bool isLocal = false)
        {
            if (!(variable is GetVarFunction) || string.IsNullOrWhiteSpace(variable.Name))
            {
                return null;
            }
            GetVarFunction gvf = variable as GetVarFunction;
            string value = gvf.Value.AsString(true, true, 16);
            string localGlobal = isLocal ? "0" : "1";
            string varData = variable.Name + ":" + localGlobal + ":" +
                             Constants.TypeToString(gvf.Value.Type).ToLower() + ":" + value;
            return varData.Trim();
        }

        static void GetVariables(Dictionary<string, ParserFunction> variablesScope,
                                 StringBuilder sb, bool isLocal = false)
        {
            foreach (var variable in variablesScope.Values.ToList())
            {
                string varData = CreateVariableEntry(variable, isLocal);
                if (!string.IsNullOrWhiteSpace(varData))
                {
                    sb.AppendLine(varData);
                }
            }
        }

        public static string GetVariables(ParsingScript script)
        {
            StringBuilder sb = new StringBuilder();
            // Locals, if any:
            if (s_locals.Count > 0)
            {
                Dictionary<string, ParserFunction> locals = s_locals.Peek().Variables;
                GetVariables(locals, sb, true);
            }

            // Variables in the local file scope:
            if (script != null && script.Filename != null)
            {
                Dictionary<string, ParserFunction> localScope;
                string scopeName = Path.GetFileName(script.Filename);
                if (s_localScope.TryGetValue(scopeName, out localScope))
                {
                    GetVariables(localScope, sb, true);
                }
            }

            // Globals:
            GetVariables(s_functions, sb, false);

            return sb.ToString().Trim();
        }

        static bool LocalNameExists(string name)
        {
            if (s_locals.Count <= StackLevelDelta)
            {
                return false;
            }
            var vars = s_locals.Peek().Variables;
            return vars.ContainsKey(name);
        }
        static bool GlobalNameExists(string name)
        {
            return s_functions.ContainsKey(name);
        }

        public static void RegisterFunction(string name, ParserFunction function,
                                            bool isNative = true)
        {
            AddGlobal(name, function, isNative);
        }

        public static void RemoveGlobal(string name)
        {
            s_functions.Remove(name);
        }

        public static void AddGlobal(string name, ParserFunction function,
                                     bool isNative = true)
        {
            function.isNative = isNative;
            s_functions[name] = function;

            if (string.IsNullOrWhiteSpace(function.Name))
            {
                function.Name = name;
            }
            if (!isNative)
            {
                Translation.AddTempKeyword(name);
            }
        }

        public static void AddLocalScopeVariable(string name, string scopeName, ParserFunction variable)
        {
            variable.isNative = false;
            variable.Name = name;

            if (scopeName == null)
            {
                scopeName = "";
            }

            Dictionary<string, ParserFunction> localScope;
            if (!s_localScope.TryGetValue(scopeName, out localScope))
            {
                localScope = new Dictionary<string, ParserFunction>();
            }
            localScope[name] = variable;
            s_localScope[scopeName] = localScope;
        }

        static ParserFunction GetLocalScopeVariable(string name, string scopeName)
        {
            scopeName = Path.GetFileName(scopeName);
            Dictionary<string, ParserFunction> localScope;
            if (!s_localScope.TryGetValue(scopeName, out localScope))
            {
                return null;
            }

            ParserFunction function = null;
            localScope.TryGetValue(name, out function);
            return function;
        }

        public static void AddAction(string name, ActionFunction action)
        {
            s_actions[name] = action;
        }

        public static void AddLocalVariables(StackLevel locals)
        {
            s_locals.Push(locals);
        }

        public static void AddStackLevel(string name)
        {
            s_locals.Push(new StackLevel(name));
        }

        public static void AddLocalVariable(ParserFunction local)
        {
            local.m_isGlobal = false;
            StackLevel locals = null;
            if (s_locals.Count == 0)
            {
                locals = new StackLevel();
                s_locals.Push(locals);
            }
            else
            {
                locals = s_locals.Peek();
            }

            locals.Variables[local.Name] = local;
            Translation.AddTempKeyword(local.Name);
        }

        public static void PopLocalVariables()
        {
            if (s_locals.Count > 0)
            {
                s_locals.Pop();
            }
        }

        public static int GetCurrentStackLevel()
        {
            return s_locals.Count;
        }

        public static void InvalidateStacksAfterLevel(int level)
        {
            while (level >= 0 && s_locals.Count > level)
            {
                s_locals.Pop();
            }
        }

        public static void PopLocalVariable(string name)
        {
            if (s_locals.Count == 0)
            {
                return;
            }
            Dictionary<string, ParserFunction> locals = s_locals.Peek().Variables;
            locals.Remove(name);
        }

        public Variable GetValue(ParsingScript script)
        {
            return m_impl.Evaluate(script);
        }

        protected virtual Variable Evaluate(ParsingScript script)
        {
            // The real implementation will be in the derived classes.
            return new Variable();
        }

        // Derived classes may want to return a new instance in order to
        // not to use same object in calculations.
        public virtual ParserFunction NewInstance()
        {
            return this;
        }

        public static void CleanUp()
        {
            s_functions.Clear();
            s_actions.Clear();
            s_locals.Clear();
            s_localScope.Clear();
        }

        protected string m_name;
        public string Name { get { return m_name; } set { m_name = value; } }

        protected bool m_isGlobal = true;
        public bool isGlobal { get { return m_isGlobal; } set { m_isGlobal = value; } }

        protected bool m_isNative = true;
        public bool isNative { get { return m_isNative; } set { m_isNative = value; } }

        ParserFunction m_impl;
        // Global functions and variables:
        static Dictionary<string, ParserFunction> s_functions = new Dictionary<string, ParserFunction>();

        // Global actions - function:
        static Dictionary<string, ActionFunction> s_actions = new Dictionary<string, ActionFunction>();

        // Local scope variables- defined only in the current file:
        static Dictionary<string, Dictionary<string, ParserFunction>> s_localScope =
           new Dictionary<string, Dictionary<string, ParserFunction>>();

        public static bool IsNumericFunction(string paramName, ParsingScript script = null)
        {
            ParserFunction function = ParserFunction.GetFunction(paramName, script);
            return function is INumericFunction;
        }

        public class StackLevel
        {
            public StackLevel(string name = null)
            {
                Name = name;
                Variables = new Dictionary<string, ParserFunction>();
            }
            public string Name { get; set; }
            public Dictionary<string, ParserFunction> Variables { get; set; }
        }

        // Local variables:
        // Stack of the functions being executed:
        static Stack<StackLevel> s_locals = new Stack<StackLevel>();
        public static Stack<StackLevel> ExecutionStack { get { return s_locals; } }

        static StringOrNumberFunction s_strOrNumFunction =
          new StringOrNumberFunction();
        static IdentityFunction s_idFunction =
          new IdentityFunction();

        public static int StackLevelDelta { get; set; }
    }

    public abstract class ActionFunction : ParserFunction
    {
        protected string m_action;
        public string Action { set { m_action = value; } }
    }
}