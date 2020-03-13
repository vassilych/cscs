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
        public static Action<string, Variable, bool> OnVariableChange;

        public ParserFunction()
        {
            m_impl = this;
        }

        // A "virtual" Constructor
        public ParserFunction(ParsingScript script, string item, char ch, ref string action)
        {
            if (item.Length == 0 && (ch == Constants.START_ARG || !script.StillValid()))
            {
                // There is no function, just an expression in parentheses
                m_impl = s_idFunction;
                return;
            }
            if (item.Length > 1 &&
              ((item[0] == Constants.QUOTE  && item[item.Length - 1] == Constants.QUOTE) ||
               (item[0] == Constants.QUOTE1 && item[item.Length - 1] == Constants.QUOTE1)))
            {
                // We are dealing with a string.
                s_strOrNumFunction.Item = item;
                m_impl = s_strOrNumFunction;
                return;
            }

            item = Constants.ConvertName(item);

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

            m_impl = GetObjectFunction(item, script);
            if (m_impl != null)
            {
                return;
            }

            m_impl = GetVariable(item, script);
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

        static ParserFunction GetArrayFunction(string name, ParsingScript script, string action)
        {
            int arrayStart = name.IndexOf(Constants.START_ARRAY);
            if (arrayStart < 0)
            {
                return null;
            }

            string arrayName = name;

            int delta = 0;
            List<Variable> arrayIndices = Utils.GetArrayIndices(script, arrayName, delta, (string arr, int del) => { arrayName = arr; delta = del; });

            if (arrayIndices.Count == 0)
            {
                return null;
            }

            ParserFunction pf = ParserFunction.GetVariable(arrayName, script);
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

        static ParserFunction GetObjectFunction(string name, ParsingScript script)
        {
            if (script.CurrentClass != null && script.CurrentClass.Name == name)
            {
                script.Backward(name.Length + 1);
                return new FunctionCreator();
            }
            if (script.ClassInstance != null &&
               (script.ClassInstance.PropertyExists(name) || script.ClassInstance.FunctionExists(name)))
            {
                name = script.ClassInstance.InstanceName + "." + name;
            }
            //int ind = name.LastIndexOf('.');
            int ind = name.IndexOf('.');
            if (ind <= 0)
            {
                return null;
            }
            string baseName = name.Substring(0, ind);
            if (s_namespaces.ContainsKey(baseName))
            {
                int ind2 = name.IndexOf('.', ind + 1);
                if (ind2 > 0)
                {
                    ind = ind2;
                    baseName = name.Substring(0, ind);
                }
            }

            string prop = name.Substring(ind + 1);

            ParserFunction pf = ParserFunction.GetFromNamespace(prop, baseName, script);
            if (pf != null)
            {
                return pf;
            }

            pf = ParserFunction.GetVariable(baseName, script, true);
            if (pf == null || !(pf is GetVarFunction))
            {
                pf = ParserFunction.GetFunction(baseName, script);
                if (pf == null)
                {
                    pf = Utils.ExtractArrayElement(baseName);
                }
            }

            GetVarFunction varFunc = pf as GetVarFunction;
            if (varFunc == null)
            {
                return null;
            }

            varFunc.PropertyName = prop;
            return varFunc;
        }

        static ParserFunction GetRegisteredAction(string name, ref string action)
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

        public static bool TryAddToNamespace(string name, string nameSpace, Variable varValue)
        {
            StackLevel level;
            if (string.IsNullOrWhiteSpace(nameSpace) ||
               !s_namespaces.TryGetValue(nameSpace, out level))
            {
                return false;
            }

            var vars = level.Variables;
            vars[name] = new GetVarFunction(varValue);

            return true;
        }

        public static ParserFunction GetFromNamespace(string name, ParsingScript script)
        {
            ParserFunction result = GetFromNamespace(name, s_namespace, script);
            return result;
        }

        public static ParserFunction GetFromNamespace(string name, string nameSpace, ParsingScript script)
        {
            if (string.IsNullOrWhiteSpace(nameSpace))
            {
                return null;
            }

            int ind = nameSpace.IndexOf('.');
            string prop = "";
            if (ind >= 0)
            {
                prop      = name; 
                name      = nameSpace.Substring(ind + 1);
                nameSpace = nameSpace.Substring(0, ind);
            }

            StackLevel level;
            if  (!s_namespaces.TryGetValue(nameSpace, out level))
            {
                return null;
            }

            if (!name.StartsWith(nameSpace, StringComparison.OrdinalIgnoreCase))
            {
                name = nameSpace + "." + name;
            }

            var vars = level.Variables;
            ParserFunction impl;
            if (!vars.TryGetValue(name, out impl) &&
                !s_variables.TryGetValue(name, out impl) &&
                !s_functions.TryGetValue(name, out impl)
                )
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(prop) &&  impl is GetVarFunction)
            {
                ((GetVarFunction) impl).PropertyName = prop;
            }
            return impl;
        }

        public static ParserFunction GetVariable(string name, ParsingScript script, bool force = false)
        {
            if (!force && script.TryPrev() == Constants.START_ARG)
            {
                return GetFunction(name, script);
            }
            name = Constants.ConvertName(name);
            ParserFunction impl;
            StackLevel localStack = script.StackLevel != null ? script.StackLevel : s_locals.Count > StackLevelDelta ? s_lastExecutionLevel : null;
            if (localStack != null)
            {
                Dictionary<string, ParserFunction> local = localStack.Variables;
                if (local.TryGetValue(name, out impl))
                {
                    return impl;
                }
            }

            string scopeName = script == null || script.Filename == null ? "" : script.Filename;
            impl = GetLocalScopeVariable(name, scopeName);
            if (impl != null)
            {
                return impl;
            }

            if (s_variables.TryGetValue(name, out impl))
            {
                return impl.NewInstance();
            }

            return GetFunction(name, script);
        }

        public static ParserFunction GetFunction(string name, ParsingScript script)
        {
            name = Constants.ConvertName(name);
            ParserFunction impl;

            if (s_functions.TryGetValue(name, out impl))
            {
                // Global function exists and is registered (e.g. pi, exp, or a variable)
                return impl.NewInstance();
            }

            return GetFromNamespace(name, script);
        }

        public static void UpdateFunction(Variable variable)
        {
            UpdateFunction(variable.ParsingToken, new GetVarFunction(variable));
        }
        public static void UpdateFunction(string name, ParserFunction function)
        {
            name = Constants.ConvertName(name);
            lock (s_variables)
            {
                // First search among local variables.
                if (s_lastExecutionLevel != null && s_locals.Count > StackLevelDelta)
                {
                    Dictionary<string, ParserFunction> local = s_lastExecutionLevel.Variables;

                    if (local.ContainsKey(name))
                    {
                        // Local function exists (a local variable)
                        local[name] = function;
                        return;
                    }
                }
            }
            // If it's not a local variable, update global.
            s_variables[name] = function;
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

        public static void AddGlobalOrLocalVariable(string name, GetVarFunction function, ParsingScript script = null)
        {
            name          = Constants.ConvertName(name);
            if (Constants.CheckReserved(name))
            {
                Utils.ThrowErrorMsg(name + " is a reserved name.", script, name);
            }

            Dictionary<string, ParserFunction> lastLevel = GetLastLevel();
            if (lastLevel != null && s_lastExecutionLevel.IsNamespace && !string.IsNullOrWhiteSpace(s_namespace))
            {
                name = s_namespacePrefix + name;
            }

            function.Name = Constants.GetRealName(name);
            function.Value.ParamName = function.Name;
            if (script != null && script.StackLevel != null && !GlobalNameExists(name))
            {
                script.StackLevel.Variables[name] = function;
                var handle = OnVariableChange;
                if (handle != null)
                {
                    handle.Invoke(function.Name, function.Value, false);
                }
            }
            else if (s_locals.Count > StackLevelDelta && (LocalNameExists(name) || !GlobalNameExists(name)))
            {
                AddLocalVariable(function);
            }
            else
            {
                AddGlobal(name, function, false /* not native */);
            }
        }

        static string CreateVariableEntry(Variable var, string name, bool isLocal = false)
        {
            try
            {
                string value = var.AsString(true, true, 16);
                string localGlobal = isLocal ? "0" : "1";
                string varData = name + ":" + localGlobal + ":" +
                                 Constants.TypeToString(var.Type).ToLower() + ":" + value;
                return varData.Trim();
            }
            catch(Exception exc)
            {
                // TODO: Clean up not used objects.
                bool removed = isLocal ? PopLocalVariable(name) : RemoveGlobal(name);
                Console.WriteLine("Object {0} is probably dead ({1}): {2}. Removing it.", name, removed, exc);
                return null;
            }
        }

        static void GetVariables(Dictionary<string, ParserFunction> variablesScope,
                                 StringBuilder sb, bool isLocal = false)
        {
            var all = variablesScope.Values.ToList();
            for (int i = 0; i < all.Count; i++)
            {
                var variable = all[i];
                GetVarFunction gvf = variable as GetVarFunction;
                if (gvf == null || string.IsNullOrWhiteSpace(variable.Name))
                {
                    continue;
                }

                string varData = CreateVariableEntry(gvf.Value, variable.Name, isLocal);
                if (!string.IsNullOrWhiteSpace(varData))
                {
                    sb.AppendLine(varData);
                    if (gvf.Value.Type == Variable.VarType.OBJECT)
                    {
                        var props = gvf.Value.GetProperties();
                        foreach (Variable var in props)
                        {
                            var val = gvf.Value.GetProperty(var.AsString());
                            varData = CreateVariableEntry(val, variable.Name + "." + var.AsString(), isLocal);
                            if (!string.IsNullOrWhiteSpace(varData))
                            {
                                sb.AppendLine(varData);
                            }
                        }
                    }
                }
            }
        }

        public static string GetVariables(ParsingScript script)
        {
            StringBuilder sb = new StringBuilder();
            // Locals, if any:
            if (s_lastExecutionLevel != null)
            {
                Dictionary<string, ParserFunction> locals = s_lastExecutionLevel.Variables;
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
            GetVariables(s_variables, sb, false);

            return sb.ToString().Trim();
        }

        static Dictionary<string, ParserFunction> GetLastLevel()
        {
            lock (s_variables)
            {
                if (s_lastExecutionLevel == null || s_locals.Count <= StackLevelDelta)
                {
                    return null;
                }
                var result = s_lastExecutionLevel.Variables;
                return result;
            }
        }

        static bool LocalNameExists(string name)
        {
            Dictionary<string, ParserFunction> lastLevel = GetLastLevel();
            if (lastLevel == null)
            {
                return false;
            }
            name = Constants.ConvertName(name);
            return lastLevel.ContainsKey(name);
        }

        static bool GlobalNameExists(string name)
        {
            name = Constants.ConvertName(name);
            return s_variables.ContainsKey(name) || s_functions.ContainsKey(name);
        }

        public static Variable RegisterEnum(string varName, string enumName)
        {
            Variable enumVar = EnumFunction.UseExistingEnum(enumName);
            if (enumVar == Variable.EmptyInstance)
            {
                return enumVar;
            }

            RegisterFunction(varName, new GetVarFunction(enumVar));
            return enumVar;
        }

        public static void RegisterFunction(string name, ParserFunction function,
                                            bool isNative = true)
        {
            name = Constants.ConvertName(name);
            function.Name = Constants.GetRealName(name);

            if (!string.IsNullOrWhiteSpace(s_namespace))
            {
                StackLevel level;
                if (s_namespaces.TryGetValue(s_namespace, out level) &&
                   function is CustomFunction)
                {
                    ((CustomFunction)function).NamespaceData = level;
                    name = s_namespacePrefix + name;
                }
            }

            s_functions[name] = function;
            function.isNative = isNative;
        }

        public static bool UnregisterFunction(string name)
        {
            name = Constants.ConvertName(name);

            bool removed = s_functions.Remove(name);
            return removed;
        }

        public static bool RemoveGlobal(string name)
        {
            name = Constants.ConvertName(name);
            return s_variables.Remove(name);
        }

        static void NormalizeValue(ParserFunction function)
        {
            GetVarFunction gvf = function as GetVarFunction;
            if (gvf != null)
            {
                gvf.Value.CurrentAssign = "";
            }
        }

        public static void AddGlobal(string name, ParserFunction function,
                                     bool isNative = true)
        {
            name = Constants.ConvertName(name);
            NormalizeValue(function);
            function.isNative = isNative;
            s_variables[name] = function;

            function.Name = Constants.GetRealName(name);
#if UNITY_EDITOR == false && UNITY_STANDALONE == false && __ANDROID__ == false && __IOS__ == false
            if (!isNative)
            {
                Translation.AddTempKeyword(name);
            }
#endif
            var handle = OnVariableChange;
            if (handle != null && function is GetVarFunction)
            {
                handle.Invoke(function.Name, ((GetVarFunction)function).Value, true);
            }
        }

        public static void AddLocalScopeVariable(string name, string scopeName, ParserFunction variable)
        {
            name = Constants.ConvertName(name);
            variable.isNative = false;
            variable.Name = Constants.GetRealName(name);
            if (variable is GetVarFunction)
            {
                ((GetVarFunction)variable).Value.ParamName = variable.Name;
            }

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

            name = Constants.ConvertName(name);
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
            lock (s_variables)
            {
                s_locals.Push(locals);
                s_lastExecutionLevel = locals;
            }
        }

        public static void AddNamespace(string namespaceName)
        {
            namespaceName = Constants.ConvertName(namespaceName);
            if (!string.IsNullOrWhiteSpace(s_namespace))
            {
                throw new ArgumentException("Already inside of namespace [" + s_namespace + "].");
            }

            StackLevel level;
            if (!s_namespaces.TryGetValue(namespaceName, out level))
            {
                level = new StackLevel(namespaceName, true); ;
            }

            lock (s_variables)
            {
                s_locals.Push(level);
                s_lastExecutionLevel = level;
            }

            s_namespaces[namespaceName] = level;

            s_namespace = namespaceName;
            s_namespacePrefix = namespaceName + ".";
        }

        public static void PopNamespace()
        {
            s_namespace = s_namespacePrefix = "";
            lock (s_variables)
            {
                while (s_locals.Count > 0)
                {
                    var level = s_locals.Pop();
                    s_lastExecutionLevel = s_locals.Count == 0 ? null : s_locals.Peek();
                    if (level.IsNamespace)
                    {
                        return;
                    }
                }
            }
        }

        public static string AdjustWithNamespace(string name)
        {
            name = Constants.ConvertName(name);
            return s_namespacePrefix + name;
        }

        public static StackLevel AddStackLevel(string scopeName)
        {
            lock (s_variables)
            {
                s_locals.Push(new StackLevel(scopeName));
                s_lastExecutionLevel = s_locals.Peek();
                return s_lastExecutionLevel;
            }
        }

        public static void AddLocalVariable(ParserFunction local, string varName = "")
        {
            NormalizeValue(local);
            local.m_isGlobal = false;

            lock (s_variables)
            {

                if (s_lastExecutionLevel == null)
                {
                    s_lastExecutionLevel = new StackLevel();
                    s_locals.Push(s_lastExecutionLevel);
                }
            }

            var name = Constants.ConvertName(string.IsNullOrWhiteSpace(varName) ? local.Name : varName);
            local.Name = Constants.GetRealName(name);
            if (local is GetVarFunction)
            {
                ((GetVarFunction)local).Value.ParamName = local.Name;
            }
            s_lastExecutionLevel.Variables[name] = local;
#if UNITY_EDITOR == false && UNITY_STANDALONE == false && __ANDROID__ == false && __IOS__ == false
            Translation.AddTempKeyword(name);
#endif
            var handle = OnVariableChange;
            if (handle != null && local is GetVarFunction)
            {
                handle.Invoke(local.Name, ((GetVarFunction)local).Value, false);
            }
        }

        public static void PopLocalVariables(int id)
        {
            lock (s_variables)
            {
                if (s_lastExecutionLevel == null)
                {
                    return;
                }
                if (id < 0 || s_lastExecutionLevel.Id == id)
                {
                    s_locals.Pop();
                    s_lastExecutionLevel = s_locals.Count == 0 ? null : s_locals.Peek();
                    return;
                }

                var array = s_locals.ToArray();
                for (int i = 1; i < array.Length; i++)
                {
                    var stack = array[i];
                    if (stack.Id == id)
                    {
                        for (int j = 0; j < i + 1 && s_locals.Count > 0; j++)
                        {
                            s_locals.Pop();
                        }
                        for (int j = 0; j < i; j++)
                        {
                            s_locals.Push(array[j]);
                        }
                        s_lastExecutionLevel = s_locals.Peek();
                        return;
                    }
                }
            }
        }

        public static int GetCurrentStackLevel()
        {
            lock (s_variables)
            {
                return s_locals.Count;
            }
        }

        public static void InvalidateStacksAfterLevel(int level)
        {
            lock (s_variables)
            {
                while (level >= 0 && s_locals.Count > level)
                {
                    s_locals.Pop();
                }
                s_lastExecutionLevel = s_locals.Count == 0 ? null : s_locals.Peek();
            }
        }

        public static bool PopLocalVariable(string name)
        {
            if (s_lastExecutionLevel == null)
            {
                return false;
            }
            Dictionary<string, ParserFunction> locals = s_lastExecutionLevel.Variables;
            name = Constants.ConvertName(name);
            return locals.Remove(name);
        }

        public Variable GetValue(ParsingScript script)
        {
            return m_impl.Evaluate(script);
        }

        public async Task<Variable> GetValueAsync(ParsingScript script)
        {
            return await m_impl.EvaluateAsync(script);
        }

        protected virtual Variable Evaluate(ParsingScript script)
        {
            // The real implementation will be in the derived classes.
            return new Variable();
        }

        protected virtual Task<Variable> EvaluateAsync(ParsingScript script)
        {
            // If not overriden, the non-sync version will be called.
            return Task.FromResult( Evaluate(script) );
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
            CleanUpVariables();
        }

        public static void CleanUpVariables()
        {
            s_variables.Clear();
            s_locals.Clear();
            s_localScope.Clear();
            s_namespaces.Clear();
            s_namespace = s_namespacePrefix = "";
            CompiledClass.Init();
        }

        protected string m_name;
        public string Name
        {
            get { return m_name; }
            set { m_name = value; }
        }

        protected bool m_isGlobal = true;
        public bool isGlobal { get { return m_isGlobal; } set { m_isGlobal = value; } }

        protected bool m_isNative = true;
        public bool isNative { get { return m_isNative; } set { m_isNative = value; } }

        ParserFunction m_impl;

        // Global functions:
        static Dictionary<string, ParserFunction> s_functions = new Dictionary<string, ParserFunction>();

        // Global variables:
        static Dictionary<string, ParserFunction> s_variables = new Dictionary<string, ParserFunction>();

        // Global actions to functions map:
        static Dictionary<string, ActionFunction> s_actions = new Dictionary<string, ActionFunction>();

        // Local scope variables:
        static Dictionary<string, Dictionary<string, ParserFunction>> s_localScope =
           new Dictionary<string, Dictionary<string, ParserFunction>>();

        public static bool IsNumericFunction(string paramName, ParsingScript script = null)
        {
            ParserFunction function = ParserFunction.GetFunction(paramName, script);
            return function is INumericFunction;
        }

        public class StackLevel
        {
            static int s_id;

            public StackLevel(string name = null, bool isNamespace = false)
            {
                Id = ++s_id;
                Name = name;
                IsNamespace = isNamespace;
                Variables = new Dictionary<string, ParserFunction>();
            }

            public string Name { get; private set; }
            public bool IsNamespace { get; private set; }
            public int Id { get; private set; }

            public Dictionary<string, ParserFunction> Variables { get; set; }
        }

        // Local variables:
        // Stack of the functions being executed:
        static Stack<StackLevel> s_locals = new Stack<StackLevel>();
        public static Stack<StackLevel> ExecutionStack { get { return s_locals; } }

        static StackLevel s_lastExecutionLevel;

        static Dictionary<string, StackLevel> s_namespaces = new Dictionary<string, StackLevel>();
        static string s_namespace;
        static string s_namespacePrefix;

        public static string GetCurrentNamespace { get { return s_namespace; } }

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