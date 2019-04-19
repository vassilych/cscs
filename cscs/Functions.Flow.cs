using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace SplitAndMerge
{
    class BreakStatement : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(Variable.VarType.BREAK);
        }
    }
    class ContinueStatement : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(Variable.VarType.CONTINUE);
        }
    }

    class ReturnStatement : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            script.MoveForwardIf(Constants.SPACE);
            if (!script.FromPrev(Constants.RETURN.Length).Contains(Constants.RETURN))
            {
                script.Backward();
            }
            Variable result = Utils.GetItem(script);

            // If we are in Return, we are done:
            script.SetDone();
            result.IsReturn = true;

            return result;
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            script.MoveForwardIf(Constants.SPACE);
            if (!script.FromPrev(Constants.RETURN.Length).Contains(Constants.RETURN))
            {
                script.Backward();
            }
            Variable result = await Utils.GetItemAsync(script);

            // If we are in Return, we are done:
            script.SetDone();
            result.IsReturn = true;

            return result;
        }
    }

    class TryBlock : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return Interpreter.Instance.ProcessTry(script);
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            return await Interpreter.Instance.ProcessTryAsync(script);
        }
    }

    class ExitFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            Environment.Exit(0);
            return Variable.EmptyInstance;
        }
    }

    class ThrowFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            // 1. Extract what to throw.
            Variable arg = Utils.GetItem(script);

            // 2. Convert it to a string.
            string result = arg.AsString();

            // 3. Throw it!
            throw new ArgumentException(result);
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            // 1. Extract what to throw.
            Variable arg = await Utils.GetItemAsync(script);

            // 2. Convert it to a string.
            string result = arg.AsString();

            // 3. Throw it!
            throw new ArgumentException(result);
        }
    }

    class ShowFunction : ParserFunction, IStringFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);

            string funcName = args[0].AsString();

            ParserFunction function = ParserFunction.GetFunction(funcName, script);
            CustomFunction custFunc = function as CustomFunction;
            Utils.CheckNotNull(funcName, custFunc);

            string body = Utils.BeautifyScript(custFunc.Body, custFunc.Header);
            Utils.PrintScript(body, script);

            return new Variable(body);
        }
    }

    class FunctionCreator : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string funcName = Utils.GetToken(script, Constants.TOKEN_SEPARATION);
            funcName = Constants.ConvertName(funcName);

            string[] args = Utils.GetFunctionSignature(script);
            if (args.Length == 1 && string.IsNullOrWhiteSpace(args[0]))
            {
                args = new string[0];
            }

            script.MoveForwardIf(Constants.START_GROUP, Constants.SPACE);
            int lineNumber = 0;
            /*string line = */script.GetOriginalLine(out lineNumber);

            int parentOffset = script.Pointer;

            if (script.CurrentClass != null)
            {
                parentOffset += script.CurrentClass.ParentOffset;
            }

            string body = Utils.GetBodyBetween(script, Constants.START_GROUP, Constants.END_GROUP);

            CustomFunction customFunc = new CustomFunction(funcName, body, args, script);
            customFunc.ParentScript = script;
            customFunc.ParentOffset = parentOffset;

            if (script.CurrentClass != null)
            {
                script.CurrentClass.AddMethod(funcName, args, customFunc);
            }
            else
            {
                ParserFunction.RegisterFunction(funcName, customFunc, false /* not native */);
            }

            return Variable.EmptyInstance;
        }
    }

    public class CSCSClass : ParserFunction
    {
        public CSCSClass() {}

        public CSCSClass(string className)
        {
            Name = className;
            RegisterClass(className, this);
        }

        public CSCSClass(string className, string[] baseClasses)
        {
            Name = className;
            RegisterClass(className, this);

            foreach (string baseClass in baseClasses)
            {
                var bc = CSCSClass.GetClass(baseClass);
                if (bc == null)
                {
                    throw new ArgumentException("Base Class [" + baseClass + "] not found.");
                }

                foreach (var entry in bc.m_classProperties)
                {
                    m_classProperties[entry.Key] = entry.Value;
                }
                foreach (var entry in bc.m_customFunctions)
                {
                    m_customFunctions[entry.Key] = entry.Value;
                }
            }
        }

        public static void RegisterClass(string className, CSCSClass obj)
        {
            obj.Name = className;
            className = Constants.ConvertName(className);
            s_allClasses[className] = obj;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            script.GetFunctionArgs();

            // TODO: Work in progress, currently not functional
            return Variable.EmptyInstance;
        }

        public void AddMethod(string name, string[] args, CustomFunction method)
        {
            if (name == m_name)
            {
                m_constructors[args.Length] = method;
            }
            else
            {
                m_customFunctions[name] = method;
            }
        }

        public void AddProperty(string name, Variable property)
        {
            m_classProperties[name] = property;
        }

        public static CSCSClass GetClass(string name)
        {
            CSCSClass theClass = null;
            s_allClasses.TryGetValue(name, out theClass);
            return theClass;
        }

        static Dictionary<string, CSCSClass> s_allClasses =
            new Dictionary<string, CSCSClass>();

        Dictionary<int, CustomFunction> m_constructors =
            new Dictionary<int, CustomFunction>();
        Dictionary<string, CustomFunction> m_customFunctions =
            new Dictionary<string, CustomFunction>();
        Dictionary<string, Variable> m_classProperties =
            new Dictionary<string, Variable>();

        public ParsingScript ParentScript = null;
        public int ParentOffset = 0;

        public class ClassInstance : ScriptObject
        {
            public ClassInstance(string instanceName, string className, List<Variable> args,
                                 ParsingScript script = null)
            {
                InstanceName = instanceName;
                m_cscsClass = CSCSClass.GetClass(className);
                if (m_cscsClass == null)
                {
                    throw new ArgumentException("Class [" + className + "] not found.");
                }

                // Copy over all the properties defined for this class.
                foreach (var entry in m_cscsClass.m_classProperties)
                {
                    SetProperty(entry.Key, entry.Value);
                }

                // Run "constructor" if any is defined for this number of args.
                CustomFunction constructor = null;
                if (m_cscsClass.m_constructors.TryGetValue(args.Count, out constructor))
                {
                    constructor.Run(args, script, this);
                }
            }

            public string InstanceName { get; set; }
            CSCSClass m_cscsClass;

            Dictionary<string, Variable> m_properties = new Dictionary<string, Variable>();
            HashSet<string> m_propSet = new HashSet<string>();

            public override string ToString()
            {
                return m_cscsClass.Name + "." + InstanceName;
            }

            public Task<Variable> SetProperty(string name, Variable value)
            {
                m_properties[name] = value;
                m_propSet.Add(name);
                return Task.FromResult( Variable.EmptyInstance );
            }

            public async Task<Variable> GetProperty(string name, List<Variable> args = null, ParsingScript script = null)
            {
                Variable value = null;
                if (m_properties.TryGetValue(name, out value))
                {
                    return value;
                }

                CustomFunction customFunction = null;
                if (!m_cscsClass.m_customFunctions.TryGetValue(name, out customFunction))
                {
                    return null;
                }
                if (args == null)
                {
                    return Variable.EmptyInstance;
                }

                foreach (var entry in m_cscsClass.m_classProperties)
                {
                    args.Add(entry.Value);
                }

                Variable result = await customFunction.RunAsync(args, script, this);
                return result;
            }

            public List<KeyValuePair<string, Variable>> GetPropList()
            {
                List<KeyValuePair<string, Variable>> props = new List<KeyValuePair<string, Variable>>();
                foreach (var entry in m_properties)
                {
                    props.Add(new KeyValuePair<string, Variable>(entry.Key, entry.Value));
                }
                return props;
            }

            public List<string> GetProperties()
            {
                List<string> props = new List<string>(m_properties.Keys);
                props.AddRange(m_cscsClass.m_customFunctions.Keys);

                return props;
            }
            public bool PropertyExists(string name)
            {
                return m_propSet.Contains(name);
            }

            public bool FunctionExists(string name)
            {
                CustomFunction customFunction = null;
                if (!m_cscsClass.m_customFunctions.TryGetValue(name, out customFunction))
                {
                    return false;
                }
                return true;
            }
        }
    }

    class EnumFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<string> properties = Utils.ExtractTokens(script);

            if (properties.Count == 1 && properties[0].Contains("."))
            {
                return UseExistingEnum(properties[0]);
            }

            Variable enumVar = new Variable(Variable.VarType.ENUM);
            for (int i = 0; i < properties.Count; i++)
            {
                enumVar.SetEnumProperty(properties[i], new Variable(i));
            }

            return enumVar;
        }

        public static Variable UseExistingEnum(string enumName)
        {
            Type enumType = GetEnumType(enumName);
            if (enumType == null || !enumType.IsEnum)
            {
                return Variable.EmptyInstance;
            }

            var names = Enum.GetNames(enumType);

            Variable enumVar = new Variable(Variable.VarType.ENUM);
            for (int i = 0; i < names.Length; i++)
            {
                var numValue = Enum.Parse(enumType, names[i], true);
                enumVar.SetEnumProperty(names[i], new Variable((int)numValue));
            }

            return enumVar;
        }

        public static Type GetEnumType(string enumName)
        {
            string[] tokens = enumName.Split('.');

            Type enumType = null;
            int index = 0;
            string typeName = "";
            while(enumType == null && index < tokens.Length)
            {
                if (!string.IsNullOrWhiteSpace(typeName))
                {
                    typeName += ".";
                }
                typeName += tokens[index];
                enumType = GetType(typeName);
                index++;
            }

            for (int i = index; i < tokens.Length && enumType != null; i++)
            {
                enumType = enumType.GetNestedType(tokens[i]);
            }

            if (enumType == null || !enumType.IsEnum)
            {
                return null;
            }

            return enumType;
        }

        public static Type GetType(string typeName)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                var type = assembly.GetType(typeName, false, true);
                if (type != null)
                {
                    return type;
                }
            }
            return null;
        }
    }

    class NewObjectFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string className = Utils.GetToken(script, Constants.TOKEN_SEPARATION);
            className = Constants.ConvertName(className);
            script.MoveForwardIf(Constants.START_ARG);
            List<Variable> args = script.GetFunctionArgs();

            CompiledClass csClass = CSCSClass.GetClass(className) as CompiledClass;
            if (csClass != null)
            {
                ScriptObject obj = csClass.GetImplementation(args);
                return new Variable(obj);
            }

            CSCSClass.ClassInstance instance = new
                CSCSClass.ClassInstance(script.CurrentAssign, className, args, script);

            return new Variable(instance);
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            string className = Utils.GetToken(script, Constants.TOKEN_SEPARATION);
            className = Constants.ConvertName(className);
            script.MoveForwardIf(Constants.START_ARG);
            List<Variable> args = await script.GetFunctionArgsAsync();

            CompiledClassAsync csClass = CSCSClass.GetClass(className) as CompiledClassAsync;
            if (csClass != null)
            {
                ScriptObject obj = await csClass.GetImplementationAsync(args);
                return new Variable(obj);
            }

            CSCSClass.ClassInstance instance = new
                CSCSClass.ClassInstance(script.CurrentAssign, className, args, script);

            return new Variable(instance);
        }
    }

    public class ClassCreator : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string className = Utils.GetToken(script, Constants.TOKEN_SEPARATION);
            className = Constants.ConvertName(className);
            string[] baseClasses = Utils.GetBaseClasses(script);
            CSCSClass newClass = new CSCSClass(className, baseClasses);

            script.MoveForwardIf(Constants.START_GROUP, Constants.SPACE);

            newClass.ParentOffset = script.Pointer;
            newClass.ParentScript = script;
            int lineNumber = 0;
            /*string line = */script.GetOriginalLine(out lineNumber);

            string scriptExpr = Utils.GetBodyBetween(script, Constants.START_GROUP,
                                                     Constants.END_GROUP);
            Dictionary<int, int> char2Line;
            string body = Utils.ConvertToScript(scriptExpr, out char2Line);

            Variable result = null;
            ParsingScript tempScript = new ParsingScript(body);
            //tempScript.ScriptOffset = newClass.ParentOffset;
            tempScript.Char2Line = script.Char2Line;
            tempScript.Filename = script.Filename;
            tempScript.OriginalScript = script.OriginalScript;
            tempScript.CurrentClass = newClass;
            tempScript.ParentScript = script;
            tempScript.InTryBlock = script.InTryBlock;
            tempScript.DisableBreakpoints = true;

            // Uncomment if want to step into the class creation code when the debugger is attached (unlikely)
            /*Debugger debugger = script != null && script.Debugger != null ? script.Debugger : Debugger.MainInstance;
            if (debugger != null)
            {
                result = debugger.StepInFunctionIfNeeded(tempScript);
            }*/

            while (tempScript.Pointer < body.Length - 1 &&
                  (result == null || !result.IsReturn))
            {
                result = tempScript.ExecuteTo();
                tempScript.GoToNextStatement();
            }

            return Variable.EmptyInstance;
        }
    }

    public class CustomFunction : ParserFunction
    {
        internal CustomFunction(string funcName,
                                string body, string[] args, ParsingScript script)
        {
            Name = funcName;
            m_body = body;
            m_args = args;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                int ind = arg.IndexOf('=');
                if (ind > 0)
                {
                    m_args[i] = arg.Substring(0, ind).Trim().ToLower();
                    string defValue = ind >= arg.Length - 1 ? "" : arg.Substring(ind + 1).Trim();

                    Variable defVariable = Utils.GetVariableFromString(defValue, script);
                    defVariable.CurrentAssign = m_args[i];
                    defVariable.Index = i;

                    m_defArgMap[i] = m_defaultArgs.Count;
                    m_defaultArgs.Add(defVariable);
                }
                else
                {
                    m_args[i] = arg.ToLower();
                }
                m_argMap[m_args[i]] = i;
            }
        }

        public void RegisterArguments(List<Variable> args,
                                      List<KeyValuePair<string, Variable>> args2 = null)
        {
            int missingArgs = m_args.Length - args.Count;

            bool namedParameters = false;
            for (int i = 0; i < args.Count; i++)
            {
                var arg = args[i];
                int argIndex = -1;
                if (m_argMap.TryGetValue(arg.CurrentAssign, out argIndex))
                {
                    namedParameters = true;
                    if (i != argIndex)
                    {
                        args[i] = argIndex < args.Count ? args[argIndex] : args[i];
                        while (argIndex > args.Count - 1)
                        {
                            args.Add(Variable.EmptyInstance);
                        }
                        args[argIndex] = arg;
                    }
                }
                else if (namedParameters)
                {
                    throw new ArgumentException("All arguments in function [" + m_name +
                     "] must be arg=value form.");
                }
            }

            if (missingArgs > 0 && missingArgs <= m_defaultArgs.Count)
            {
                if (!namedParameters)
                {
                    for (int i = m_defaultArgs.Count - missingArgs; i < m_defaultArgs.Count; i++)
                    {
                        args.Add(m_defaultArgs[i]);
                    }
                }
                else
                {
                    for (int i = 0; i < args.Count; i++)
                    {
                        if (args[i].Type == Variable.VarType.NONE ||
                           (!string.IsNullOrWhiteSpace(args[i].CurrentAssign) &&
                            args[i].CurrentAssign != m_args[i]))
                        {
                            int defIndex = -1;
                            if (!m_defArgMap.TryGetValue(i, out defIndex))
                            {
                                throw new ArgumentException("No argument [" + m_args[i] +
                                 "] given for function [" + m_name + "].");
                            }
                            args[i] = m_defaultArgs[defIndex];
                        }
                    }
                }
            }
            for (int i = args.Count; i < m_args.Length; i++)
            {
                int defIndex = -1;
                if (!m_defArgMap.TryGetValue(i, out defIndex))
                {
                    throw new ArgumentException("No argument [" + m_args[i] +
                     "] given for function [" + m_name + "].");
                }
                args.Add(m_defaultArgs[defIndex]);
            }
            StackLevel stackLevel = new StackLevel(m_name);

            if (args2 != null)
            {
                foreach (var entry in args2)
                {
                    var arg = new GetVarFunction(entry.Value);
                    arg.Name = entry.Key;
                    stackLevel.Variables[entry.Key] = arg;
                }
            }

            int maxSize = Math.Min(args.Count, m_args.Length);
            for (int i = 0; i < maxSize; i++)
            {
                var arg = new GetVarFunction(args[i]);
                arg.Name = m_args[i];
                stackLevel.Variables[m_args[i]] = arg;
            }

            ParserFunction.AddLocalVariables(stackLevel);
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = Constants.FUNCT_WITH_SPACE.Contains(m_name) ?
                // Special case of extracting args.
                Utils.GetFunctionArgsAsStrings(script) :
                script.GetFunctionArgs();

            script.MoveBackIf(Constants.START_GROUP);

            if (args.Count + m_defaultArgs.Count < m_args.Length)
            {
                throw new ArgumentException("Function [" + m_name + "] arguments mismatch: " +
                                    m_args.Length + " declared, " + args.Count + " supplied");
            }

            Variable result = Run(args, script);
            return result;
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            List<Variable> args = Constants.FUNCT_WITH_SPACE.Contains(m_name) ?
                // Special case of extracting args.
                Utils.GetFunctionArgsAsStrings(script) :
                await script.GetFunctionArgsAsync();

            script.MoveBackIf(Constants.START_GROUP);

            if (args.Count + m_defaultArgs.Count < m_args.Length)
            {
                throw new ArgumentException("Function [" + m_name + "] arguments mismatch: " +
                                    m_args.Length + " declared, " + args.Count + " supplied");
            }

            Variable result = await RunAsync(args, script);
            return result;
        }

        public Variable Run(List<Variable> args, ParsingScript script = null,
                            CSCSClass.ClassInstance instance = null)
        {
            List<KeyValuePair<string, Variable>> args2 = instance == null ? null : instance.GetPropList();
            // 1. Add passed arguments as local variables to the Parser.
            RegisterArguments(args, args2);

            // 2. Execute the body of the function.
            Variable result = null;
            ParsingScript tempScript = new ParsingScript(m_body);
            tempScript.ScriptOffset = m_parentOffset;
            if (m_parentScript != null)
            {
                tempScript.Char2Line = m_parentScript.Char2Line;
                tempScript.Filename = m_parentScript.Filename;
                tempScript.OriginalScript = m_parentScript.OriginalScript;
            }
            tempScript.ParentScript = script;
            tempScript.InTryBlock = script == null ? false : script.InTryBlock;
            tempScript.ClassInstance = instance;

            Debugger debugger = script != null && script.Debugger != null ? script.Debugger : Debugger.MainInstance;
            if (debugger != null)
            {
                result = debugger.StepInFunctionIfNeeded(tempScript).Result;
            }

            while (tempScript.Pointer < m_body.Length - 1 &&
                  (result == null || !result.IsReturn))
            {
                result = tempScript.ExecuteTo();
                tempScript.GoToNextStatement();
            }

            ParserFunction.PopLocalVariables();

            if (result == null)
            {
                result = Variable.EmptyInstance;
            }
            else
            {
                result.IsReturn = false;
            }

            return result;
        }
        public async Task<Variable> RunAsync(List<Variable> args, ParsingScript script = null,
                            CSCSClass.ClassInstance instance = null)
        {
            List<KeyValuePair<string, Variable>> args2 = instance == null ? null : instance.GetPropList();
            // 1. Add passed arguments as local variables to the Parser.
            RegisterArguments(args, args2);

            // 2. Execute the body of the function.
            Variable result = null;
            ParsingScript tempScript = new ParsingScript(m_body);
            tempScript.ScriptOffset = m_parentOffset;
            if (m_parentScript != null)
            {
                tempScript.Char2Line = m_parentScript.Char2Line;
                tempScript.Filename = m_parentScript.Filename;
                tempScript.OriginalScript = m_parentScript.OriginalScript;
            }
            tempScript.ParentScript = script;
            tempScript.InTryBlock = script == null ? false : script.InTryBlock;
            tempScript.ClassInstance = instance;

            Debugger debugger = script != null && script.Debugger != null ? script.Debugger : Debugger.MainInstance;
            if (debugger != null)
            {
                result = await debugger.StepInFunctionIfNeeded(tempScript);
            }

            while (tempScript.Pointer < m_body.Length - 1 &&
                  (result == null || !result.IsReturn))
            {
                result = await tempScript.ExecuteToAsync();
                tempScript.GoToNextStatement();
            }

            ParserFunction.PopLocalVariables();

            if (result == null)
            {
                result = Variable.EmptyInstance;
            }
            else
            {
                result.IsReturn = false;
            }

            return result;
        }

        public static Task<Variable> Run(string functionName,
             Variable arg1 = null, Variable arg2 = null, Variable arg3 = null)
        {
            CustomFunction customFunction = ParserFunction.GetFunction(functionName, null) as CustomFunction;

            if (customFunction == null)
            {
                return null;
            }

            List<Variable> args = new List<Variable>();
            if (arg1 != null)
            {
                args.Add(arg1);
            }
            if (arg2 != null)
            {
                args.Add(arg2);
            }
            if (arg3 != null)
            {
                args.Add(arg3);
            }

            Variable result = customFunction.Run(args);
            return Task.FromResult( result );
        }

        public static async Task<Variable> RunAsync(string functionName,
             Variable arg1 = null, Variable arg2 = null, Variable arg3 = null)
        {
            CustomFunction customFunction = ParserFunction.GetFunction(functionName, null) as CustomFunction;

            if (customFunction == null)
            {
                return null;
            }

            List<Variable> args = new List<Variable>();
            if (arg1 != null)
            {
                args.Add(arg1);
            }
            if (arg2 != null)
            {
                args.Add(arg2);
            }
            if (arg3 != null)
            {
                args.Add(arg3);
            }

            Variable result = await customFunction.RunAsync(args);
            return result;
        }

        public ParsingScript ParentScript { set { m_parentScript = value; } }
        public int ParentOffset { set { m_parentOffset = value; } }
        public string Body { get { return m_body; } }

        public int ArgumentCount { get { return m_args.Length; } }
        public string Argument(int nIndex) { return m_args[nIndex]; }

        public string Header
        {
            get
            {
                return Constants.FUNCTION + " " + Constants.GetRealName(Name) + " " +
                       Constants.START_ARG + string.Join(", ", m_args) +
                       Constants.END_ARG + " " + Constants.START_GROUP;
            }
        }

        protected string m_body;
        protected string[] m_args;
        protected ParsingScript m_parentScript = null;
        protected int m_parentOffset = 0;

        List<Variable> m_defaultArgs = new List<Variable>();
        Dictionary<string, int> m_argMap = new Dictionary<string, int>();
        Dictionary<int, int> m_defArgMap = new Dictionary<int, int>();
    }

    class StringOrNumberFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            // First check if the passed expression is a string between quotes.
            if (Item.Length > 1 &&
              ((Item[0] == Constants.QUOTE  && Item[Item.Length - 1] == Constants.QUOTE) ||
               (Item[0] == Constants.QUOTE1 && Item[Item.Length - 1] == Constants.QUOTE1)))
            {
                return new Variable(Item.Substring(1, Item.Length - 2));
            }

            // Otherwise this should be a number.
            double num = Utils.ConvertToDouble(Item, script);
            return new Variable(num);
        }

        public string Item { private get; set; }
    }

    class AddFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 2, m_name);

            Variable currentValue = Utils.GetSafeVariable(args, 0);
            Variable item = Utils.GetSafeVariable(args, 1);
            int index = Utils.GetSafeInt(args, 2, -1);

            currentValue.AddVariable(item, index);
            if (!currentValue.ParsingToken.Contains(Constants.START_ARRAY.ToString()))
            {
                ParserFunction.AddGlobalOrLocalVariable(currentValue.ParsingToken,
                                                        new GetVarFunction(currentValue));
            }

            return currentValue;
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            List<Variable> args = await script.GetFunctionArgsAsync();
            Utils.CheckArgs(args.Count, 2, m_name);

            Variable currentValue = Utils.GetSafeVariable(args, 0);
            Variable item = Utils.GetSafeVariable(args, 1);
            int index = Utils.GetSafeInt(args, 2, -1);

            currentValue.AddVariable(item, index);
            if (!currentValue.ParsingToken.Contains(Constants.START_ARRAY.ToString()))
            {
                ParserFunction.AddGlobalOrLocalVariable(currentValue.ParsingToken,
                                                        new GetVarFunction(currentValue));
            }

            return currentValue;
        }
    }
    class RemoveFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            // 1. Get the name of the variable.
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 2, m_name, true);
            string varName = args[0].AsString();

            // 2. Get the current value of the variable.
            ParserFunction func = ParserFunction.GetFunction(varName, script);
            Utils.CheckNotNull(varName, func);
            Variable currentValue = func.GetValue(script);
            Utils.CheckArray(currentValue, varName);

            // 3. Get the variable to remove.
            Variable item = args[1];

            bool removed = currentValue.Tuple.Remove(item);

            ParserFunction.AddGlobalOrLocalVariable(varName,
                                                    new GetVarFunction(currentValue));
            return new Variable(removed);
        }
    }
    class RemoveAtFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            // 1. Get the name of the variable.
            string varName = Utils.GetToken(script, Constants.NEXT_OR_END_ARRAY);
            Utils.CheckNotEnd(script, m_name);

            // 2. Get the current value of the variable.
            ParserFunction func = ParserFunction.GetFunction(varName, script);
            Utils.CheckNotNull(varName, func);
            Variable currentValue = func.GetValue(script);
            Utils.CheckArray(currentValue, varName);

            // 3. Get the variable to remove.
            Variable item = Utils.GetItem(script);
            Utils.CheckNonNegativeInt(item);

            currentValue.Tuple.RemoveAt(item.AsInt());

            ParserFunction.AddGlobalOrLocalVariable(varName,
                                                    new GetVarFunction(currentValue));
            return Variable.EmptyInstance;
        }
    }

    class ContainsFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            // 1. Get the name of the variable.
            string varName = Utils.GetToken(script, Constants.NEXT_OR_END_ARRAY);
            Utils.CheckNotEnd(script, m_name);

            // 2. Get the current value of the variable.
            List<Variable> arrayIndices = Utils.GetArrayIndices(script, varName, (newVarName) => { varName = newVarName; } );

            ParserFunction func = ParserFunction.GetFunction(varName, script);
            Utils.CheckNotNull(varName, func);
            Variable currentValue = func.GetValue(script);

            // 2b. Special dealings with arrays:
            Variable query = arrayIndices.Count > 0 ?
                             Utils.ExtractArrayElement(currentValue, arrayIndices) :
                             currentValue;

            // 3. Get the value to be looked for.
            Variable searchValue = Utils.GetItem(script);
            Utils.CheckNotEnd(script, m_name);

            // 4. Check if the value to search for exists.
            bool exists = query.Exists(searchValue, true /* notEmpty */);

            script.MoveBackIf(Constants.START_GROUP);
            return new Variable(exists);
        }
    }

    class FindIndexFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 2, m_name);

            Variable var = Utils.GetSafeVariable(args, 0);
            string val = Utils.GetSafeString(args, 1);

            int index = var.FindIndex(val);

            return new Variable(index);
        }
    }

    class BoolFunction : ParserFunction, INumericFunction
    {
        bool m_value;
        public BoolFunction(bool init)
        {
            m_value = init;
        }
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(m_value);
        }
    }
    class ToDoubleFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];

            double result = Utils.ConvertToDouble(arg.AsString());
            return new Variable(result);
        }
    }
    class ToIntFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];

            int result = Utils.ConvertToInt(arg.AsString());
            return new Variable(result);
        }
    }
    class ToBoolFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];

            double result = Utils.ConvertToBool(arg.AsString()) ? 1 : 0;
            return new Variable(result);
        }
    }
    class ToDecimalFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];

            string result = Decimal.Parse(arg.AsString(), NumberStyles.Any).ToString();
            return new Variable(result);
        }
    }
    class ToStringFunction : ParserFunction, IStringFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];

            string result = arg.AsString();
            return new Variable(result);
        }
    }
    class IdentityFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return script.ExecuteTo(Constants.END_ARG);
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            return await script.ExecuteToAsync(Constants.END_ARG);
        }
    }

    class IfStatement : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            Variable result = Interpreter.Instance.ProcessIf(script);
            return result;
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            Variable result = await Interpreter.Instance.ProcessIfAsync(script);
            return result;
        }
    }

    class ForStatement : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return Interpreter.Instance.ProcessFor(script);
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            return await Interpreter.Instance.ProcessForAsync(script);
        }
    }

    class WhileStatement : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return Interpreter.Instance.ProcessWhile(script);
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            return await Interpreter.Instance.ProcessWhileAsync(script);
        }
    }

    class IncludeFile : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            string includeScript;
            ParsingScript tempScript = GetIncludeFileScript(script, args, out includeScript);

            Variable result = null;
            if (script.Debugger != null)
            {
                result = script.Debugger.StepInIncludeIfNeeded(tempScript).Result;
            }

            while (tempScript.Pointer < includeScript.Length)
            {
                result = tempScript.ExecuteTo();
                tempScript.GoToNextStatement();
            }
            return result == null ? Variable.EmptyInstance : result;
        }

        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            List<Variable> args = await script.GetFunctionArgsAsync();
            string includeScript;
            ParsingScript tempScript = GetIncludeFileScript(script, args, out includeScript);

            Variable result = null;
            if (script.Debugger != null)
            {
                result = await script.Debugger.StepInIncludeIfNeeded(tempScript);
            }

            while (tempScript.Pointer < includeScript.Length)
            {
                result = await tempScript.ExecuteToAsync();
                tempScript.GoToNextStatement();
            }
            return result == null ? Variable.EmptyInstance : result;
        }

        ParsingScript GetIncludeFileScript(ParsingScript script, List<Variable> args, out string includeScript)
        {
            Utils.CheckArgs(args.Count, 1, m_name, true);

            string filename = args[0].AsString();
            string pathname = script.GetFilePath(filename);
            string[] lines = Utils.GetFileLines(pathname);

            string includeFile = string.Join(Environment.NewLine, lines);
            Dictionary<int, int> char2Line;
            includeScript = Utils.ConvertToScript(includeFile, out char2Line, pathname);
            ParsingScript tempScript = new ParsingScript(includeScript, 0, char2Line);
            tempScript.Filename = pathname;
            tempScript.OriginalScript = string.Join(Constants.END_LINE.ToString(), lines);
            tempScript.ParentScript = script;
            tempScript.InTryBlock = script.InTryBlock;

            return tempScript;
        }
    }

    // Get a value of a variable or of an array element
    public class GetVarFunction : ParserFunction
    {
        public GetVarFunction(Variable value)
        {
            m_value = value;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            // First check if this element is part of an array:
            if (script.TryPrev() == Constants.START_ARRAY)
            {
                // There is an index given - it must be for an element of the tuple.
                if (m_value.Tuple == null || m_value.Tuple.Count == 0)
                {
                    throw new ArgumentException("No tuple exists for the index");
                }

                if (m_arrayIndices == null)
                {
                    string startName = script.Substr(script.Pointer - 1);
                    m_arrayIndices = Utils.GetArrayIndices(script, startName, m_delta, (newStart, newDelta) => { startName = newStart; m_delta = newDelta; } );
                }

                script.Forward(m_delta);
                while (script.MoveForwardIf(Constants.END_ARRAY))
                {
                }

                Variable result = Utils.ExtractArrayElement(m_value, m_arrayIndices);
                if (script.TryCurrent() != '.')
                {
                    return result;
                }

                script.Forward();
                m_propName = Utils.GetToken(script, Constants.NEXT_OR_END_ARRAY);
                Variable propValue = result.GetProperty(m_propName, script);
                Utils.CheckNotNull(propValue, m_propName);
                return propValue;
            }

            // Now check that this is an object:
            if (!string.IsNullOrWhiteSpace(m_propName))
            {
                string temp = m_propName;
                m_propName = null; // Need this to reset for recursive calls
                Variable propValue = m_value.Type == Variable.VarType.ENUM ?
                                     m_value.GetEnumProperty(temp, script) :
                                     m_value.GetProperty(temp, script);
                Utils.CheckNotNull(propValue, temp);
                return propValue;
            }

            // Otherwise just return the stored value.
            return m_value;
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            // First check if this element is part of an array:
            if (script.TryPrev() == Constants.START_ARRAY)
            {
                // There is an index given - it must be for an element of the tuple.
                if (m_value.Tuple == null || m_value.Tuple.Count == 0)
                {
                    throw new ArgumentException("No tuple exists for the index");
                }

                if (m_arrayIndices == null)
                {
                    string startName = script.Substr(script.Pointer - 1);
                    m_arrayIndices = await Utils.GetArrayIndicesAsync(script, startName, m_delta, (newStart, newDelta) => { startName = newStart; m_delta = newDelta; });
                }

                script.Forward(m_delta);
                while (script.MoveForwardIf(Constants.END_ARRAY))
                {
                }

                Variable result = Utils.ExtractArrayElement(m_value, m_arrayIndices);
                if (script.TryCurrent() != '.')
                {
                    return result;
                }

                script.Forward();
                m_propName = Utils.GetToken(script, Constants.NEXT_OR_END_ARRAY);
                Variable propValue = await result.GetPropertyAsync(m_propName, script); 
                Utils.CheckNotNull(propValue, m_propName);
                return propValue;
            }

            // Now check that this is an object:
            if (!string.IsNullOrWhiteSpace(m_propName))
            {
                string temp = m_propName;
                m_propName = null; // Need this to reset for recursive calls

                Variable propValue = m_value.Type == Variable.VarType.ENUM ?
                         m_value.GetEnumProperty(temp, script) :
                         await m_value.GetPropertyAsync(temp, script);
                Utils.CheckNotNull(propValue, temp, script);
                return propValue;
            }

            // Otherwise just return the stored value.
            return m_value;
        }

        public int Delta
        {
            set { m_delta = value; }
        }
        public Variable Value
        {
            get { return m_value; }
        }
        public List<Variable> Indices
        {
            set { m_arrayIndices = value; }
        }
        public string PropertyName
        {
            set { m_propName = value; }
        }

        Variable m_value;
        int m_delta = 0;
        List<Variable> m_arrayIndices = null;
        string m_propName;
    }
    class IncrementDecrementFunction : ActionFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            bool prefix = string.IsNullOrWhiteSpace(m_name);
            if (prefix)
            {// If it is a prefix we do not have the variable name yet.
                Name = Utils.GetToken(script, Constants.TOKEN_SEPARATION);
            }

            // Value to be added to the variable:
            int valueDelta = m_action == Constants.INCREMENT ? 1 : -1;
            int returnDelta = prefix ? valueDelta : 0;

            // Check if the variable to be set has the form of x[a][b],
            // meaning that this is an array element.
            double newValue = 0;
            List<Variable> arrayIndices = Utils.GetArrayIndices(script, m_name, (string name) => { m_name = name; });

            ParserFunction func = ParserFunction.GetFunction(m_name, script);
            Utils.CheckNotNull(m_name, func);

            Variable currentValue = func.GetValue(script);
            currentValue = currentValue.DeepClone();

            if (arrayIndices.Count > 0 || script.TryCurrent() == Constants.START_ARRAY)
            {
                if (prefix)
                {
                    string tmpName = m_name + script.Rest;
                    int delta = 0;
                    arrayIndices = Utils.GetArrayIndices(script, tmpName, delta, (string t, int d) => { tmpName = t; delta = d; });
                    script.Forward(Math.Max(0, delta - tmpName.Length));
                }

                Variable element = Utils.ExtractArrayElement(currentValue, arrayIndices);
                script.MoveForwardIf(Constants.END_ARRAY);

                newValue = element.Value + returnDelta;
                element.Value += valueDelta;
            }
            else
            { // A normal variable.
                newValue = currentValue.Value + returnDelta;
                currentValue.Value += valueDelta;
            }

            ParserFunction.AddGlobalOrLocalVariable(m_name,
                                                    new GetVarFunction(currentValue));
            return new Variable(newValue);
        }

        override public ParserFunction NewInstance()
        {
            return new IncrementDecrementFunction();
        }
    }

    class OperatorAssignFunction : ActionFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            // Value to be added to the variable:
            Variable right = Utils.GetItem(script);

            List<Variable> arrayIndices = Utils.GetArrayIndices(script, m_name, (string name) => { m_name = name; });

            ParserFunction func = ParserFunction.GetFunction(m_name, script);
            Utils.CheckNotNull(func, m_name, script);

            Variable currentValue = func.GetValue(script);
            currentValue = currentValue.DeepClone();
            Variable left = currentValue;

            if (arrayIndices.Count > 0)
            {// array element
                left = Utils.ExtractArrayElement(currentValue, arrayIndices);
                script.MoveForwardIf(Constants.END_ARRAY);
            }

            if (left.Type == Variable.VarType.NUMBER)
            {
                NumberOperator(left, right, m_action);
            }
            else
            {
                StringOperator(left, right, m_action);
            }

            if (arrayIndices.Count > 0)
            {// array element
                AssignFunction.ExtendArray(currentValue, arrayIndices, 0, left);
                ParserFunction.AddGlobalOrLocalVariable(m_name,
                                                         new GetVarFunction(currentValue));
            }
            else
            {
                ParserFunction.AddGlobalOrLocalVariable(m_name,
                                                         new GetVarFunction(left));
            }
            return left;
        }

        static void NumberOperator(Variable valueA,
                                   Variable valueB, string action)
        {
            switch (action)
            {
                case "+=":
                    valueA.Value += valueB.Value;
                    break;
                case "-=":
                    valueA.Value -= valueB.Value;
                    break;
                case "*=":
                    valueA.Value *= valueB.Value;
                    break;
                case "/=":
                    valueA.Value /= valueB.Value;
                    break;
                case "%=":
                    valueA.Value %= valueB.Value;
                    break;
                case "&=":
                    valueA.Value = (int)valueA.Value & (int)valueB.Value;
                    break;
                case "|=":
                    valueA.Value = (int)valueA.Value | (int)valueB.Value;
                    break;
                case "^=":
                    valueA.Value = (int)valueA.Value ^ (int)valueB.Value;
                    break;
            }
        }
        static void StringOperator(Variable valueA,
          Variable valueB, string action)
        {
            switch (action)
            {
                case "+=":
                    if (valueB.Type == Variable.VarType.STRING)
                    {
                        valueA.String += valueB.AsString();
                    }
                    else
                    {
                        valueA.String += valueB.Value;
                    }
                    break;
            }
        }

        override public ParserFunction NewInstance()
        {
            return new OperatorAssignFunction();
        }
    }

    class AssignFunction : ActionFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            m_name = Constants.GetRealName(m_name);
            script.CurrentAssign = m_name;
            Variable varValue = Utils.GetItem(script);

            // First try processing as an object (with a dot notation):
            Variable result = ProcessObject(script, varValue);
            if (result != null)
            {
                return result;
            }

            // Check if the variable to be set has the form of x[a][b]...,
            // meaning that this is an array element.
            List<Variable> arrayIndices = Utils.GetArrayIndices(script, m_name, (string name) => { m_name = name; });

            if (arrayIndices.Count == 0)
            {
                ParserFunction.AddGlobalOrLocalVariable(m_name, new GetVarFunction(varValue));
                Variable retVar = varValue.DeepClone();
                retVar.CurrentAssign = m_name;
                return retVar;
                //return varValue.DeepClone();
            }

            Variable array;

            ParserFunction pf = ParserFunction.GetFunction(m_name, script);
            if (pf != null)
            {
                array = pf.GetValue(script);
            }
            else
            {
                array = new Variable();
            }

            ExtendArray(array, arrayIndices, 0, varValue);

            ParserFunction.AddGlobalOrLocalVariable(m_name, new GetVarFunction(array));
            return array;
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            m_name = Constants.GetRealName(m_name);
            script.CurrentAssign = m_name;
            Variable varValue = await Utils.GetItemAsync(script);

            // First try processing as an object (with a dot notation):
            Variable result = await ProcessObjectAsync(script, varValue);
            if (result != null)
            {
                return result;
            }

            // Check if the variable to be set has the form of x[a][b]...,
            // meaning that this is an array element.
            List<Variable> arrayIndices = await Utils.GetArrayIndicesAsync(script, m_name, (string name) => { m_name = name; });

            if (arrayIndices.Count == 0)
            {
                ParserFunction.AddGlobalOrLocalVariable(m_name, new GetVarFunction(varValue));
                Variable retVar = varValue.DeepClone();
                retVar.CurrentAssign = m_name;
                return retVar;
                //return varValue.DeepClone();
            }

            Variable array;

            ParserFunction pf = ParserFunction.GetFunction(m_name, script);
            if (pf != null)
            {
                array = await pf.GetValueAsync(script);
            }
            else
            {
                array = new Variable();
            }

            ExtendArray(array, arrayIndices, 0, varValue);

            ParserFunction.AddGlobalOrLocalVariable(m_name, new GetVarFunction(array));
            return array;
        }

        Variable ProcessObject(ParsingScript script, Variable varValue)
        {
            if (script.CurrentClass != null)
            {
                script.CurrentClass.AddProperty(m_name, varValue);
                return varValue.DeepClone();
            }
            string varName = m_name;
            if (script.ClassInstance != null)
            {
                //varName = script.ClassInstance.InstanceName + "." + m_name;
                varValue = script.ClassInstance.SetProperty(m_name, varValue).Result;
                return varValue.DeepClone();
            }

            int ind = varName.IndexOf('.');
            if (ind <= 0)
            {
                return null;
            }

            Utils.CheckForValidName(varName, script);

            string name = varName.Substring(0, ind);
            string prop = varName.Substring(ind + 1);

            ParserFunction existing = ParserFunction.GetFunction(name, script);
            Variable baseValue = existing != null ? existing.GetValue(script) : new Variable(Variable.VarType.ARRAY);
            baseValue.SetProperty(prop, varValue, name);

            ParserFunction.AddGlobalOrLocalVariable(name, new GetVarFunction(baseValue));
            //ParserFunction.AddGlobal(name, new GetVarFunction(baseValue), false);

            return varValue.DeepClone();
        }
        async Task<Variable> ProcessObjectAsync(ParsingScript script, Variable varValue)
        {
            if (script.CurrentClass != null)
            {
                script.CurrentClass.AddProperty(m_name, varValue);
                return varValue.DeepClone();
            }
            string varName = m_name;
            if (script.ClassInstance != null)
            {
                //varName = script.ClassInstance.InstanceName + "." + m_name;
                await script.ClassInstance.SetProperty(m_name, varValue);
                return varValue.DeepClone();
            }

            int ind = varName.IndexOf('.');
            if (ind <= 0)
            {
                return null;
            }

            Utils.CheckForValidName(varName, script);

            string name = varName.Substring(0, ind);
            string prop = varName.Substring(ind + 1);

            ParserFunction existing = ParserFunction.GetFunction(name, script);
            Variable baseValue = existing != null ? await existing.GetValueAsync(script) : new Variable(Variable.VarType.ARRAY);
            await baseValue.SetPropertyAsync(prop, varValue, name);

            ParserFunction.AddGlobalOrLocalVariable(name, new GetVarFunction(baseValue));
            //ParserFunction.AddGlobal(name, new GetVarFunction(baseValue), false);

            return varValue.DeepClone();
        }


        override public ParserFunction NewInstance()
        {
            return new AssignFunction();
        }

        public static void ExtendArray(Variable parent,
                         List<Variable> arrayIndices,
                         int indexPtr,
                         Variable varValue)
        {
            if (arrayIndices.Count <= indexPtr)
            {
                return;
            }

            Variable index = arrayIndices[indexPtr];
            int currIndex = ExtendArrayHelper(parent, index);

            if (arrayIndices.Count - 1 == indexPtr)
            {
                parent.Tuple[currIndex] = varValue;
                return;
            }

            Variable son = parent.Tuple[currIndex];
            ExtendArray(son, arrayIndices, indexPtr + 1, varValue);
        }

        private static int ExtendArrayHelper(Variable parent, Variable indexVar)
        {
            parent.SetAsArray();

            int arrayIndex = parent.GetArrayIndex(indexVar);
            if (arrayIndex < 0)
            {
                // This is not a "normal index" but a new string for the dictionary.
                string hash = indexVar.AsString();
                arrayIndex = parent.SetHashVariable(hash, Variable.NewEmpty());
                return arrayIndex;
            }

            if (parent.Tuple.Count <= arrayIndex)
            {
                for (int i = parent.Tuple.Count; i <= arrayIndex; i++)
                {
                    parent.Tuple.Add(Variable.NewEmpty());
                }
            }
            return arrayIndex;
        }
    }

    class DeepCopyFunction : ActionFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            Variable varValue = Utils.GetItem(script);
            return varValue.DeepClone(); ;
        }
    }

    class TokenizeLinesFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 2, m_name);

            string varName = Utils.GetSafeString(args, 0);
            Variable lines = Utils.GetSafeVariable(args, 1);
            int fromLine = Utils.GetSafeInt(args, 2, 0);
            string sepStr = Utils.GetSafeString(args, 3, "\t");
            if (sepStr == "\\t")
            {
                sepStr = "\t";
            }
            char[] sep = sepStr.ToCharArray();

            // var function = ParserFunction.GetFunction(varName, script);
            Variable allTokensVar = new Variable(Variable.VarType.ARRAY);

            for (int counter = fromLine; counter < lines.Tuple.Count; counter++)
            {
                Variable lineVar = lines.Tuple[counter];
#pragma warning disable 219
                // bugbug - the toAdd has side effects
                Variable toAdd = new Variable(counter - fromLine);
#pragma warning restore 219
                string line = lineVar.AsString();
                var tokens = line.Split(sep);
                Variable tokensVar = new Variable(Variable.VarType.ARRAY);
                foreach (string token in tokens)
                {
                    tokensVar.Tuple.Add(new Variable(token));
                }
                allTokensVar.Tuple.Add(tokensVar);
            }

            ParserFunction.AddGlobalOrLocalVariable(varName,
                                                    new GetVarFunction(allTokensVar));

            return Variable.EmptyInstance;
        }
    }

    class AddVariablesToHashFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 3, m_name);

            string varName = Utils.GetSafeString(args, 0);
            Variable lines = Utils.GetSafeVariable(args, 1);
            int fromLine = Utils.GetSafeInt(args, 2);
            string hash2 = Utils.GetSafeString(args, 3);
            string sepStr = Utils.GetSafeString(args, 4, "\t");
            if (sepStr == "\\t")
            {
                sepStr = "\t";
            }
            char[] sep = sepStr.ToCharArray();

            var function = ParserFunction.GetFunction(varName, script);
            Variable mapVar = function != null ? function.GetValue(script) :
                                        new Variable(Variable.VarType.ARRAY);

            for (int counter = fromLine; counter < lines.Tuple.Count; counter++)
            {
                Variable lineVar = lines.Tuple[counter];
                Variable toAdd = new Variable(counter - fromLine);
                string line = lineVar.AsString();
                var tokens = line.Split(sep);
                string hash = tokens[0];
                mapVar.AddVariableToHash(hash, toAdd);
                if (!string.IsNullOrWhiteSpace(hash2) &&
                    !hash2.Equals(hash, StringComparison.OrdinalIgnoreCase))
                {
                    mapVar.AddVariableToHash(hash2, toAdd);
                }
            }

            ParserFunction.AddGlobalOrLocalVariable(varName,
                                              new GetVarFunction(mapVar));
            return Variable.EmptyInstance;
        }
    }
    class AddVariableToHashFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 3, m_name);

            string varName = Utils.GetSafeString(args, 0);
            Variable toAdd = Utils.GetSafeVariable(args, 1);
            string hash = Utils.GetSafeString(args, 2);

            var function = ParserFunction.GetFunction(varName, script);
            Variable mapVar = function != null ? function.GetValue(script) :
                                        new Variable(Variable.VarType.ARRAY);

            mapVar.AddVariableToHash(hash, toAdd);
            for (int i = 3; i < args.Count; i++)
            {
                string hash2 = Utils.GetSafeString(args, 3);
                if (!string.IsNullOrWhiteSpace(hash2) &&
                    !hash2.Equals(hash, StringComparison.OrdinalIgnoreCase))
                {
                    mapVar.AddVariableToHash(hash2, toAdd);
                }
            }

            ParserFunction.AddGlobalOrLocalVariable(varName,
                                                new GetVarFunction(mapVar));

            return Variable.EmptyInstance;
        }
    }
    class TokenCounterFunction : ParserFunction, IArrayFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 2, m_name);

            Variable all = Utils.GetSafeVariable(args, 0);
            string varName = Utils.GetSafeString(args, 1);
            int index = Utils.GetSafeInt(args, 2);

            // var function = ParserFunction.GetFunction(varName, script);
            Variable mapVar = new Variable(Variable.VarType.ARRAY);

            if (all.Tuple == null)
            {
                return Variable.EmptyInstance;
            }

            string currentValue = "";
            int currentCount = 0;

            int globalCount = 0;

            for (int i = 0; i < all.Tuple.Count; i++)
            {
                Variable current = all.Tuple[i];
                if (current.Tuple == null || current.Tuple.Count < index)
                {
                    break;
                }
                string newValue = current.Tuple[index].AsString();
                if (currentValue != newValue)
                {
                    currentValue = newValue;
                    currentCount = 0;
                }
                mapVar.Tuple.Add(new Variable(currentCount));
                currentCount++;
                globalCount++;
            }

            ParserFunction.AddGlobalOrLocalVariable(varName,
                                                new GetVarFunction(mapVar));
            // Script - Need to enable the warnings
#pragma warning restore 219
            return mapVar;
        }
    }

    class TypeFunction : ParserFunction, IStringFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);

            bool complexVariable = Utils.GetSafeInt(args, 1, 0) == 1;
            Variable element = null;
            if (complexVariable)
            {
                element = Utils.GetVariable(args[0].AsString(), script, false);
            }
            if (element == null)
            {
                element = Utils.GetSafeVariable(args, 0);
            }

            string type = element.GetTypeString();
            script.MoveForwardIf(Constants.END_ARG, Constants.SPACE);

            Variable newValue = new Variable(type);
            return newValue;
        }
    }

    class SizeFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            // 1. Get the name of the variable.
            string varName = Utils.GetToken(script, Constants.END_ARG_ARRAY);
            Utils.CheckNotEnd(script, m_name);

            List<Variable> arrayIndices = Utils.GetArrayIndices(script, varName, (newName) => { varName = newName; } );

            // 2. Get the current value of the variable.
            ParserFunction func = ParserFunction.GetFunction(varName, script);
            Utils.CheckNotNull(varName, func);
            Variable currentValue = func.GetValue(script);
            Variable element = currentValue;

            // 2b. Special case for an array.
            if (arrayIndices.Count > 0)
            {// array element
                element = Utils.ExtractArrayElement(currentValue, arrayIndices);
                script.MoveForwardIf(Constants.END_ARRAY);
            }

            // 3. Take either the length of the underlying tuple or
            // string part if it is defined,
            // or the numerical part converted to a string otherwise.
            int size = element.GetSize();

            script.MoveForwardIf(Constants.END_ARG, Constants.SPACE);

            Variable newValue = new Variable(size);
            return newValue;
        }
    }

    class DefineLocalFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);

            string varName = Utils.GetSafeString(args, 0);
            Variable currentValue = Utils.GetSafeVariable(args, 1);

            if (currentValue == null)
            {
                currentValue = new Variable("");
            }

            string scopeName = Path.GetFileName(script.Filename);
            ParserFunction.AddLocalScopeVariable(varName, scopeName,
                                                 new GetVarFunction(currentValue));

            return currentValue;
        }
    }

    class GetPropertiesFunction : ParserFunction, IArrayFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);

            Variable baseValue = args[0];
            List<Variable> props = baseValue.GetProperties();
            return new Variable(props);
        }
    }

    class GetPropertyFunction : ParserFunction, IArrayFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 2, m_name, true);

            Variable baseValue = args[0];
            string propName = Utils.GetSafeString(args, 1);

            Variable propValue = baseValue.GetProperty(propName, script);
            Utils.CheckNotNull(propValue, propName);

            return new Variable(propValue);
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            List<Variable> args = await script.GetFunctionArgsAsync();
            Utils.CheckArgs(args.Count, 2, m_name, true);

            Variable baseValue = args[0];
            string propName = Utils.GetSafeString(args, 1);

            Variable propValue = await baseValue.GetPropertyAsync(propName, script);
            Utils.CheckNotNull(propValue, propName);

            return new Variable(propValue);
        }
        public static Variable GetProperty(ParsingScript script, string sPropertyName)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, "GetProperty", true);

            Variable baseValue = args[0];

            Variable propValue = baseValue.GetProperty(sPropertyName, script);
            Utils.CheckNotNull(propValue, sPropertyName);

            return new Variable(propValue);
        }
        public static async Task<Variable> GetPropertyAsync(ParsingScript script, string sPropertyName)
        {
            List<Variable> args = await script.GetFunctionArgsAsync();
            Utils.CheckArgs(args.Count, 1, "GetProperty", true);

            Variable baseValue = args[0];

            Variable propValue = await baseValue.GetPropertyAsync(sPropertyName, script);
            Utils.CheckNotNull(propValue, sPropertyName);

            return new Variable(propValue);
        }
    }

    class SetPropertyFunction : ParserFunction, IArrayFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 3, m_name, true);

            Variable baseValue = args[0];
            string propName = Utils.GetSafeString(args, 1);
            Variable propValue = Utils.GetSafeVariable(args, 2);

            Variable result = baseValue.SetProperty(propName, propValue);

            ParserFunction.AddGlobalOrLocalVariable(baseValue.ParsingToken,
                                                    new GetVarFunction(baseValue));
            return result;
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            List<Variable> args = await script.GetFunctionArgsAsync();
            Utils.CheckArgs(args.Count, 3, m_name, true);

            Variable baseValue = args[0];
            string propName = Utils.GetSafeString(args, 1);
            Variable propValue = Utils.GetSafeVariable(args, 2);

            Variable result = await baseValue.SetPropertyAsync(propName, propValue);

            ParserFunction.AddGlobalOrLocalVariable(baseValue.ParsingToken,
                                                    new GetVarFunction(baseValue));
            return result;
        }

        public static Variable SetProperty(ParsingScript script, string sPropertyName)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 2, "SetProperty", true);

            Variable baseValue = args[0];
            Variable propValue = Utils.GetSafeVariable(args, 1);

            Variable result = baseValue.SetProperty(sPropertyName, propValue);

            ParserFunction.AddGlobalOrLocalVariable(baseValue.ParsingToken,
                                                    new GetVarFunction(baseValue));
            return result;
        }
        public static async Task<Variable> SetPropertyAsync(ParsingScript script, string sPropertyName)
        {
            List<Variable> args = await script.GetFunctionArgsAsync();
            Utils.CheckArgs(args.Count, 2, "SetProperty", true);

            Variable baseValue = args[0];
            Variable propValue = Utils.GetSafeVariable(args, 1);

            Variable result = await baseValue.SetPropertyAsync(sPropertyName, propValue);

            ParserFunction.AddGlobalOrLocalVariable(baseValue.ParsingToken,
                                                    new GetVarFunction(baseValue));
            return result;
        }
    }

    class CancelFunction : ParserFunction
    {
        public static bool Canceled { get; set; }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 0, m_name, true);

            bool mode = Utils.GetSafeInt(args, 0, 1) == 1;
            Canceled = mode;

            return new Variable(Canceled);
        }
    }

    public class ScheduleRunFunction : ParserFunction
    {
        static Dictionary<string, System.Timers.Timer> m_timers =
           new Dictionary<string, System.Timers.Timer>();

        bool m_startTimer;

        public ScheduleRunFunction(bool startTimer)
        {
            m_startTimer = startTimer;
        }
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();

            if (!m_startTimer)
            {
                Utils.CheckArgs(args.Count, 1, m_name);
                string cancelTimerId = Utils.GetSafeString(args, 0);
                System.Timers.Timer cancelTimer;
                if (m_timers.TryGetValue(cancelTimerId, out cancelTimer))
                {
                    cancelTimer.Stop();
                    cancelTimer.Dispose();
                    m_timers.Remove(cancelTimerId);
                }
                return Variable.EmptyInstance;
            }

            Utils.CheckArgs(args.Count, 2, m_name);
            int timeout      = args[0].AsInt();
            string strAction = args[1].AsString();
            string arg       = Utils.GetSafeString(args, 2);
            string timerId   = Utils.GetSafeString(args, 3);
            bool autoReset   = Utils.GetSafeInt(args, 4, 0) != 0;

            arg              = Utils.ProtectQuotes(arg);
            timerId          = Utils.ProtectQuotes(timerId);

            System.Timers.Timer pauseTimer = new System.Timers.Timer(timeout);
            pauseTimer.Elapsed += (sender, e) =>
            {
                if (!autoReset)
                {
                    pauseTimer.Stop();
                    pauseTimer.Dispose();
                    m_timers.Remove(timerId);
                }
                string body = string.Format("{0}({1},{2});", strAction,
                              "\"" + arg + "\"", "\"" + timerId + "\"");

                ParsingScript tempScript = new ParsingScript(body);
                tempScript.ExecuteTo();
            };
            pauseTimer.AutoReset = autoReset;
            m_timers[timerId] = pauseTimer;

            pauseTimer.Start();

            return Variable.EmptyInstance;
        }
    }

    public class SingletonFunction : ParserFunction
    {
        static Dictionary<string, Variable> m_singletons =
           new Dictionary<string, Variable>();

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);

            string expr = args[0].AsString();
            Dictionary<int, int> char2Line;
            expr = Utils.ConvertToScript(expr, out char2Line);

            Variable result;
            if (m_singletons.TryGetValue(expr, out result))
            {
                return result;
            }

            ParsingScript tempScript = new ParsingScript(expr);
            result = tempScript.ExecuteTo();

            m_singletons[expr] = result;

            return result;
        }
    }

    class GetColumnFunction : ParserFunction, IArrayFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 2, m_name);

            Variable arrayVar = Utils.GetSafeVariable(args, 0);
            int col = Utils.GetSafeInt(args, 1);
            int fromCol = Utils.GetSafeInt(args, 2, 0);

            var tuple = arrayVar.Tuple;

            List<Variable> result = new List<Variable>(tuple.Count);
            for (int i = fromCol; i < tuple.Count; i++)
            {
                Variable current = tuple[i];
                if (current.Tuple == null || current.Tuple.Count <= col)
                {
                    throw new ArgumentException(m_name + ": Index [" + col + "] doesn't exist in column " +
                                                i + "/" + (tuple.Count - 1));
                }
                result.Add(current.Tuple[col]);
            }

            return new Variable(result);
        }
    }

    class GetAllKeysFunction : ParserFunction, IArrayFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            Variable varName = Utils.GetItem(script);
            Utils.CheckNotNull(varName, m_name);

            List<Variable> results = varName.GetAllKeys();

            return new Variable(results);
        }
    }

    class CheckLoaderMainFunction : ParserFunction, IArrayFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            bool isMain = !string.IsNullOrWhiteSpace(script.MainFilename) &&
                           script.MainFilename == script.Filename;

            return new Variable(isMain);
        }
    }
}
