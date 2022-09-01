using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SplitAndMerge
{
    class BreakStatement : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(Variable.VarType.BREAK);
        }
        public override string Description()
        {
            return "Breaks out of a loop.";
        }
    }

    class ContinueStatement : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(Variable.VarType.CONTINUE);
        }
        public override string Description()
        {
            return "Forces the next iteration of the loop.";
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
        public override string Description()
        {
            return "Finishes execution of a function and optionally can return a value.";
        }
    }

    class TryBlock : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return InterpreterInstance.ProcessTry(script);
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            return await InterpreterInstance.ProcessTryAsync(script);
        }
        public override string Description()
        {
            return "Try and catch control flow: try { statements; } catch (exceptionString) { statements; }. Curly brackets are mandatory.";
        }
    }

    class ExitFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            InterpreterInstance.ExitCode = Utils.GetSafeInt(args, 0, 0);
            //            Environment.Exit(code);
            InterpreterInstance.IsRunning = false;
            return new Variable(Variable.VarType.QUIT);
        }
        public override string Description()
        {
            return "Stops execution and exits process with the specified return code (default 0).";
        }
    }
    class QuitFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(Variable.VarType.QUIT);
        }
        public override string Description()
        {
            return "Quits scripting engine without terminating the process. Stops Debugger if attached.";
        }
    }

    class NullFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return Variable.EmptyInstance;
        }
        public override string Description()
        {
            return "Returns a null value.";
        }
    }
    class InfinityFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(double.PositiveInfinity);
        }
        public override string Description()
        {
            return "Returns mathematical C# PositiveInfinity.";
        }
    }
    class NegInfinityFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(double.NegativeInfinity);
        }
        public override string Description()
        {
            return "Returns mathematical C# NegativeInfinity.";
        }
    }

    class IsNaNFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);
            Variable arg = args[0];
            return new Variable(arg.Type != Variable.VarType.NUMBER || double.IsNaN(arg.Value));
        }
        public override string Description()
        {
            return "Returns if the expression is not a number.";
        }
    }

    class TypeOfFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            var args = Utils.GetTokens(script, Constants.TOKEN_SEPARATION);
            Utils.CheckArgs(args.Count, 1, m_name);

            if (args[0].StartsWith("\""))
            {
                return new Variable(Constants.TypeToString(Variable.VarType.STRING).ToLower());
            }
            if (Utils.CanConvertToDouble(args[0], out double _))
            {
                return new Variable(Constants.TypeToString(Variable.VarType.NUMBER).ToLower());
            }

            var vari = InterpreterInstance.GetVariable(args[0], script);
            if (vari == null || vari.GetValue(script).Type == Variable.VarType.UNDEFINED)
            {
                return Variable.Undefined;
            }

            var exists = InterpreterInstance.FunctionExists(args[0]);
            if (!exists)
            {
                return Variable.Undefined;
            }

            bool complexVariable = args.Count > 1 &&
                Utils.CanConvertToDouble(args[1], out double converted) && converted > 0;
            Variable element = null;
            if (complexVariable)
            {
                element = Utils.GetVariable(args[0], script, false);
            }
            if (element == null)
            {
                element = new Variable(args[0]);
            }

            string type = element.GetTypeString();
            script.MoveForwardIf(Constants.END_ARG, Constants.SPACE);

            Variable newValue = new Variable(type.ToLower());
            return newValue;
        }
        public override string Description()
        {
            return "Returns what type of a variable the expression is.";
        }
    }

    class IsFiniteFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);
            Variable arg = args[0];

            double value = arg.Value;
            if (arg.Type != Variable.VarType.NUMBER &&
               !double.TryParse(arg.String, out value))
            {
                value = double.PositiveInfinity;
            }

            return new Variable(!double.IsInfinity(value));
        }
        public override string Description()
        {
            return "Returns if the current expression is finite.";
        }
    }

    class IsUndefinedFunction : ParserFunction
    {
        string m_argument;
        string m_action;

        internal IsUndefinedFunction(string arg = "", string action = "")
        {
            m_argument = arg;
            m_action = action;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            var variable = InterpreterInstance.GetVariable(m_argument, script);
            var varValue = variable == null ? null : variable.GetValue(script);
            bool isUndefined = varValue == null || varValue.Type == Variable.VarType.UNDEFINED;

            bool result = m_action == "===" || m_action == "==" ? isUndefined :
                          !isUndefined;
            return new Variable(result);
        }
        public override string Description()
        {
            return "Returns if the current expression is defined.";
        }
    }

    class ObjectPropsFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            Variable obj = Utils.GetItem(script, true);
            string propName = Utils.GetItem(script, true).AsString();
            script.MoveForwardIf(',');

            Variable value = Utils.GetProperties(script);
            obj.SetProperty(propName, value, script);

            InterpreterInstance.AddGlobal(obj.ParamName, new GetVarFunction(obj), false);
             
            return new Variable(obj.ParamName);
        }
        public override string Description()
        {
            return "Returns all of the properties of the given object.";
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
        public override string Description()
        {
            return "Throws an exception, e.g. throw \"value must be positive\".";
        }
    }

    class VarFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            var args = Utils.GetTokens(script);
            Variable result = Variable.EmptyInstance;
            foreach (var arg in args)
            {
                var ind = arg.IndexOf('=');
                if (ind <= 0)
                {
                    if (!InterpreterInstance.FunctionExists(arg))
                    {
                        InterpreterInstance.AddGlobalOrLocalVariable(arg, new GetVarFunction(new Variable(Variable.VarType.NONE)), script);
                    }
                    continue;
                }
                var varName = arg.Substring(0, ind);
                ParsingScript tempScript = NewParsingScript(arg.Substring(ind + 1));
                AssignFunction assign = new AssignFunction(InterpreterInstance);
                result = assign.Assign(tempScript, varName, true);
            }
            return result;
        }

        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            //var varName = Utils.GetToken(script, Constants.TOKEN_SEPARATION);
            //char action = script.CurrentAndForward();

            var args = Utils.GetTokens(script);
            Task<Variable> result = null;
            foreach (var arg in args)
            {
                var ind = arg.IndexOf('=');
                if (ind <= 0)
                {
                    if (!InterpreterInstance.FunctionExists(arg))
                    {
                        InterpreterInstance.AddGlobalOrLocalVariable(arg, new GetVarFunction(new Variable(Variable.VarType.UNDEFINED)), script);
                    }
                    continue;
                }
                var varName = arg.Substring(0, ind);
                ParsingScript tempScript = NewParsingScript(arg.Substring(ind + 1));
                AssignFunction assign = new AssignFunction(InterpreterInstance);
                result = assign.AssignAsync(tempScript, varName, true);
            }

            return result == null ? Variable.EmptyInstance: await result;
        }
    }

    class ShowFunction : ParserFunction, IStringFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);

            string functionName = args[0].AsString();

            CustomFunction custFunc = InterpreterInstance.GetFunction(functionName) as CustomFunction;
            Utils.CheckNotNull(functionName, custFunc, script);

#if __ANDROID__ == false && __IOS__ == false
            CustomCompiledFunction comp = custFunc as CustomCompiledFunction;
            if (comp != null)
            {
                return new Variable(comp.Precompiler.CSharpCode);
            }
#endif

            string body = Utils.BeautifyScript(custFunc.Body, custFunc.Header);
            Utils.PrintScript(body, script);

            return new Variable(body);
        }
        public override string Description()
        {
            return "Shows the implementation of a CSCS function.";
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
            /*string line = */script.GetOriginalLine(out _);

            int parentOffset = script.Pointer;

            if (script.CurrentClass != null)
            {
                parentOffset += script.CurrentClass.ParentOffset;
            }

            string body = Utils.GetBodyBetween(script, Constants.START_GROUP, Constants.END_GROUP);
            script.MoveForwardIf(Constants.END_GROUP);

            CustomFunction customFunc = new CustomFunction(funcName, body, args, script);
            customFunc.ParentScript = script;
            customFunc.ParentOffset = parentOffset;

            if (script.CurrentClass != null)
            {
                script.CurrentClass.AddMethod(funcName, args, customFunc);
            }
            else
            {
                InterpreterInstance.RegisterFunction(funcName, customFunc, false /* not native */);
            }

            return Variable.EmptyInstance;
        }

        public override string Description()
        {
            return "Creates a new CSCS function.";
        }
    }

    public class CSCSClass : ParserFunction
    {
        // Does this need an InterpreterInstance?
        public CSCSClass() { }

        public CSCSClass(Interpreter interpreterInstance, string className)
        {
            InterpreterInstance = interpreterInstance;
            InterpreterInstance.RegisterClass(className, this);
        }

        public CSCSClass(Interpreter interpreterInstance, string className, string[] baseClasses)
        {
            InterpreterInstance = interpreterInstance;
            InterpreterInstance.RegisterClass(className, this);

            foreach (string baseClass in baseClasses)
            {
                var bc = InterpreterInstance.GetClass(baseClass);
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
                for (int i = 0; i < method.DefaultArgsCount && i < args.Length; i++)
                {
                    m_constructors[args.Length - i - 1] = method;
                }
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

        public string OriginalName { get; set; }

        Dictionary<int, CustomFunction> m_constructors =
            new Dictionary<int, CustomFunction>();
        Dictionary<string, CustomFunction> m_customFunctions =
            new Dictionary<string, CustomFunction>();
        Dictionary<string, Variable> m_classProperties =
            new Dictionary<string, Variable>();

        public Dictionary<string, Variable> ClassProperties { get { return m_classProperties; } }

        public ParsingScript ParentScript = null;
        public int ParentOffset = 0;

        public string Namespace { get; set; }

        public class ClassInstance : ScriptObject
        {
            public ClassInstance(Interpreter interpreter, string instanceName, string className, List<Variable> args,
                                 ParsingScript script = null)
            {
                InstanceName = instanceName.ToLower();
                m_cscsClass = interpreter.GetClass(className);
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
                if (args != null &&
                     m_cscsClass.m_constructors.TryGetValue(args.Count, out CustomFunction constructor))
                {
                    constructor.Run(args, script, this);
                }
                s_classInstances[InstanceName] = this;
            }

            public string InstanceName { get; set; }
            CSCSClass m_cscsClass;
            public CSCSClass CscsClass { get { return m_cscsClass; } }

            Dictionary<string, Variable> m_properties = new Dictionary<string, Variable>();
            static Dictionary<string, ClassInstance> s_classInstances =
                    new Dictionary<string, ClassInstance>();

            HashSet<string> m_propSet = new HashSet<string>();
            HashSet<string> m_propSetLower = new HashSet<string>();

            public override string ToString()
            {
                CustomFunction customFunction = null;
                if (!m_cscsClass.m_customFunctions.TryGetValue(Constants.PROP_TO_STRING.ToLower(),
                     out customFunction))
                {
                    int counter = 0;
                    var props = m_cscsClass.m_classProperties;
                    StringBuilder sb = new StringBuilder(m_cscsClass.Name + "." + InstanceName + "[");
                    foreach (var entry in m_properties)
                    {
                        sb.Append(entry.Key + "=" + entry.Value);
                        if (++counter < m_properties.Count)
                        {
                            sb.Append(",");
                        }
                    }
                    sb.Append("]");
                    return sb.ToString();
                }

                Variable result = customFunction.Run(null, null, this); 
                return result.ToString();
            }

            public static bool AssignIfClass(Variable oldVar, Variable newVar)
            {
                var classObj = oldVar.Object as ClassInstance;
                if (classObj == null ||
                    !s_classInstances.TryGetValue(classObj.InstanceName, out ClassInstance cscsObj))
                {
                    return false;
                }
                if (string.Compare(classObj.InstanceName, newVar.ParamName,
                    StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return false;
                }
                s_classInstances[newVar.ParamName.ToLower()] = cscsObj;
                return true;
            }

            public static string MarshalInstance(string varName)
            {
                if (!s_classInstances.TryGetValue(varName, out CSCSClass.ClassInstance cscsObj))
                {
                    return null;
                }

                StringBuilder sb = new StringBuilder();
                sb.Append("<" + cscsObj.InstanceName + ":class:" + cscsObj.CscsClass.Name + ">");
                var props = Interpreter.GetClassProperties(cscsObj.CscsClass);

                foreach (var prop in props)
                {
                    var propName = prop.String.ToLower();
                    var propVal = cscsObj.GetProperty(propName).Result;
                    if (propVal == null)
                    {
                        continue;
                    }
                    sb.Append(propVal.Marshal(propName));
                }
                return sb.ToString();
            }

            public Task<Variable> SetProperty(string name, Variable value)
            {
                var namelower = name.ToLower();
                m_properties[namelower] = value;
                m_propSet.Add(name);
                m_propSetLower.Add(namelower);
                return Task.FromResult( Variable.EmptyInstance );
            }

            public async Task<Variable> GetProperty(string name, List<Variable> args = null, ParsingScript script = null)
            {
                if (m_properties.TryGetValue(name, out Variable value))
                {
                    return value;
                }

                if (!m_cscsClass.m_customFunctions.TryGetValue(name, out CustomFunction customFunction))
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
                return m_propSetLower.Contains(name.ToLower());
            }

            public bool FunctionExists(string name)
            {
                if (!m_cscsClass.m_customFunctions.TryGetValue(name, out CustomFunction customFunction))
                {
                    return false;
                }
                return true;
            }
        }
    }

    class NameExistsFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string varName = Utils.GetToken(script, Constants.TOKEN_SEPARATION);
            varName = Constants.ConvertName(varName);

            bool result = InterpreterInstance.GetVariable(varName, script) != null;
            return new Variable(result);
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

            Type t = TypeRefFunction.GetTypeAnywhere(className, true);
            if (t != null)
            {
                object reflectedObj = CreateReflectedObj(t, args);
                return new Variable(reflectedObj, t);
            }

            var c = InterpreterInstance.GetClass(className);

            if (c is CompiledClass csClass)
            {
                ScriptObject obj = csClass.GetImplementation(args);
                return new Variable(obj);
            }

            if (c is CompiledClassAsync csClassAsync)
            {
                ScriptObject obj = csClassAsync.GetImplementationAsync(args).Result;
                return new Variable(obj);
            }

            var instance = new
                CSCSClass.ClassInstance(InterpreterInstance, script.CurrentAssign, className, args, script);

            var newObject = new Variable(instance);
            newObject.ParamName = instance.InstanceName;
            return newObject;
        }

       private object CreateReflectedObj(Type t, List<Variable> args)
        {
            var constructors = t.GetConstructors(BindingFlags.Public);
            if (constructors == null)
                return null;

            ConstructorInfo bestConstructor = null;
            var pConv = new Variable.ParameterConverter();
            foreach (var ctor in constructors)
            {
                if (pConv.ConvertVariablesToTypedArgs(args, ctor.GetParameters()))
                {
                    bestConstructor = ctor;
                    if (pConv.BestConversion == Variable.ParameterConverter.Conversion.Exact)
                        break;
                }
            }

            if (bestConstructor == null)
            {
                if (args.Count > 0)
                    throw new ArgumentException($"No suitable constructor found for [{t.FullName}]");
                return Activator.CreateInstance(t);
            }
            return bestConstructor?.Invoke(pConv.BestTypedArgs);
        }

        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            string className = Utils.GetToken(script, Constants.TOKEN_SEPARATION);
            className = Constants.ConvertName(className);
            script.MoveForwardIf(Constants.START_ARG);
            List<Variable> args = await script.GetFunctionArgsAsync();

            Type t = TypeRefFunction.GetTypeAnywhere(className, true);
            if (t != null)
            {
                object reflectedObj = CreateReflectedObj(t, args);
                return new Variable(reflectedObj, t);
            }

            var c = InterpreterInstance.GetClass(className);

            if (c is CompiledClassAsync csClassAsync)
            {
                ScriptObject obj = await csClassAsync.GetImplementationAsync(args);
                return new Variable(obj);
            }

            if (c is CompiledClass csClass)
            {
                ScriptObject obj = csClass.GetImplementation(args);
                return new Variable(obj);
            }

            CSCSClass.ClassInstance instance = new
                CSCSClass.ClassInstance(InterpreterInstance, script.CurrentAssign, className, args, script);

            var newObject = new Variable(instance);
            newObject.ParamName = instance.InstanceName;
            return newObject;
        }
    }

    public class ClassCreator : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string className = Utils.GetToken(script);
            //className = Constants.ConvertName(className);
            string[] baseClasses = Utils.GetBaseClasses(script);
            CSCSClass newClass = new CSCSClass(InterpreterInstance, className, baseClasses);

            script.MoveForwardIf(Constants.START_GROUP, Constants.SPACE);

            newClass.ParentOffset = script.Pointer;
            newClass.ParentScript = script;
            /*string line = */script.GetOriginalLine(out _);

            string scriptExpr = Utils.GetBodyBetween(script, Constants.START_GROUP,
                                                     Constants.END_GROUP);
            script.MoveForwardIf(Constants.END_GROUP);

            string body = Utils.ConvertToScript(InterpreterInstance, scriptExpr, out _);

            ParsingScript tempScript = script.GetTempScript(body);
            tempScript.CurrentClass = newClass;
            tempScript.DisableBreakpoints = true;
            var result = tempScript.ExecuteScript();
            return result;
            // Uncomment if want to step into the class creation code when the debugger is attached (unlikely)
            /*Debugger debugger = script != null && script.Debugger != null ? script.Debugger : Debugger.MainInstance;
            if (debugger != null)
            {
                result = debugger.StepInFunctionIfNeeded(tempScript);
            }*/
        }
    }

    public class HelpFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            var data = InterpreterInstance.GetDefinedFunctions();            

            return new Variable(data);
        }
    }

    public class NamespaceFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string namespaceName = Utils.GetToken(script, Constants.NEXT_OR_END_ARRAY);
            //Utils.CheckNotEnd(script, m_name);
            Variable result = null;

            InterpreterInstance.AddNamespace(namespaceName);
            try
            {
                script.MoveForwardIf(Constants.START_GROUP);
                string scriptExpr = Utils.GetBodyBetween(script, Constants.START_GROUP,
                                                         Constants.END_GROUP);
                script.MoveForwardIf(Constants.END_GROUP);

                Dictionary<int, int> char2Line;
                string body = Utils.ConvertToScript(InterpreterInstance, scriptExpr, out char2Line);

                ParsingScript tempScript = script.GetTempScript(body);
                tempScript.DisableBreakpoints = true;
                tempScript.MoveForwardIf(Constants.START_GROUP);

                Debugger debugger = script != null && script.Debugger != null ? script.Debugger : Debugger.MainInstance;
                if (debugger != null)
                {
                    result = debugger.StepInFunctionIfNeeded(tempScript).Result;
                }

                while (tempScript.Pointer < body.Length - 1 &&
                      (result == null || !result.IsReturn))
                {
                    result = tempScript.Execute();
                    tempScript.GoToNextStatement();
                }
            }
            finally
            {
                InterpreterInstance.PopNamespace();
            }

            return result;
        }
    }

    public class CustomFunction : ParserFunction
    {
        internal CustomFunction(string funcName,
                                string body, string[] args, ParsingScript script)
        {
            InterpreterInstance = script.InterpreterInstance;
            Name = funcName;
            m_body = body;
            m_args = RealArgs = args;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                int ind = arg.IndexOf('=');
                if (ind > 0)
                {
                    RealArgs[i] = arg.Substring(0, ind).Trim();
                    m_args[i] = RealArgs[i].ToLower();
                    string defValue = ind >= arg.Length - 1 ? "" : arg.Substring(ind + 1).Trim();

                    Variable defVariable = Utils.GetVariableFromString(defValue, script);
                    defVariable.CurrentAssign = m_args[i];
                    defVariable.Index = i;

                    m_defArgMap[i] = m_defaultArgs.Count;
                    m_defaultArgs.Add(defVariable);
                }
                else
                {
                    m_args[i] = RealArgs[i].ToLower();
                }

                ArgMap[m_args[i]] = i;
            }
        }

        public void RegisterArguments(List<Variable> args,
                                      List<KeyValuePair<string, Variable>> args2 = null)
        {
            if (args == null)
            {
                args = new List<Variable>();
            }
            int missingArgs = m_args.Length - args.Count;

            bool namedParameters = false;
            for (int i = 0; i < args.Count; i++)
            {
                var arg = args[i];
                int argIndex = -1;
                if (ArgMap.TryGetValue(arg.CurrentAssign, out argIndex))
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
                     "] must be in arg=value form.");
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
            m_stackLevel = new StackLevel(m_name);

            if (args2 != null)
            {
                foreach (var entry in args2)
                {
                    var arg = new GetVarFunction(entry.Value);
                    arg.Name = entry.Key;
                    m_stackLevel.Variables[entry.Key] = arg;
                }
            }

            int maxSize = Math.Min(args.Count, m_args.Length);
            for (int i = 0; i < maxSize; i++)
            {
                var arg = new GetVarFunction(args[i]);
                arg.Name = m_args[i];
                m_stackLevel.Variables[m_args[i]] = arg;
            }

            for (int i = m_args.Length; i < args.Count; i++)
            {
                var arg = new GetVarFunction(args[i]);
                m_stackLevel.Variables[args[i].ParamName.ToLower()] = arg;
            }

            if (NamespaceData  != null)
            {
                var vars = NamespaceData.Variables;
                string prefix = NamespaceData.Name + ".";
                foreach (KeyValuePair<string, ParserFunction> elem in vars)
                {
                    string key = elem.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ?
                        elem.Key.Substring(prefix.Length) : elem.Key;
                    m_stackLevel.Variables[key] = elem.Value;
                }
            }

            InterpreterInstance.AddLocalVariables(m_stackLevel);
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = InterpreterInstance.Translation.IsFunctWithSpace(m_name) ?
                // Special case of extracting args.
                Utils.GetFunctionArgsAsStrings(script) :
                script.GetFunctionArgs();

            Utils.ExtractParameterNames(args, m_name, script);

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
            List<Variable> args = InterpreterInstance.Translation.IsFunctWithSpace(m_name) ?
                // Special case of extracting args.
                Utils.GetFunctionArgsAsStrings(script) :
                await script.GetFunctionArgsAsync();

            Utils.ExtractParameterNames(args, m_name, script);

            script.MoveBackIf(Constants.START_GROUP);

            if (args.Count + m_defaultArgs.Count < m_args.Length)
            {
                throw new ArgumentException("Function [" + m_name + "] arguments mismatch: " +
                                    m_args.Length + " declared, " + args.Count + " supplied");
            }

            Variable result = await RunAsync(args, script);
            return result;
        }

        public Variable Run(List<Variable> args = null, ParsingScript script = null,
                            CSCSClass.ClassInstance instance = null)
        {
            List<KeyValuePair<string, Variable>> args2 = instance == null ? null : instance.GetPropList();
            // 1. Add passed arguments as local variables to the Parser.
            RegisterArguments(args, args2);

            // 2. Execute the body of the function.
            Variable result = null;
            ParsingScript tempScript = Utils.GetTempScript(InterpreterInstance,
                                                           m_body, m_stackLevel, m_name, script,
                                                           m_parentScript, m_parentOffset, instance);

            Debugger debugger = script != null && script.Debugger != null ? script.Debugger : Debugger.MainInstance;
            if (script != null && debugger != null)
            {
                result = debugger.StepInFunctionIfNeeded(tempScript).Result;
            }

            while (tempScript.Pointer < m_body.Length - 1 &&
                  (result == null || !result.IsReturn))
            {
                result = tempScript.Execute();
                tempScript.GoToNextStatement();
            }

            InterpreterInstance.PopLocalVariables(m_stackLevel.Id);

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
        public async Task<Variable> RunAsync(List<Variable> args = null, ParsingScript script = null,
                            CSCSClass.ClassInstance instance = null)
        {
            List<KeyValuePair<string, Variable>> args2 = instance == null ? null : instance.GetPropList();
            // 1. Add passed arguments as local variables to the Parser.
            RegisterArguments(args, args2);

            // 2. Execute the body of the function.
            Variable result = null;
            ParsingScript tempScript = Utils.GetTempScript(InterpreterInstance,
                                                           m_body, m_stackLevel, m_name, script,
                                                           m_parentScript, m_parentOffset, instance);

            Debugger debugger = script != null && script.Debugger != null ? script.Debugger : Debugger.MainInstance;
            if (debugger != null)
            {
                result = await debugger.StepInFunctionIfNeeded(tempScript);
            }

            while (tempScript.Pointer < m_body.Length - 1 &&
                  (result == null || !result.IsReturn))
            {
                result = await tempScript.ExecuteAsync();
                tempScript.GoToNextStatement();
            }

            InterpreterInstance.PopLocalVariables(m_stackLevel.Id);

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

        public static Task<Variable> Run(Interpreter interpreter, string functionName,
             Variable arg1 = null, Variable arg2 = null, Variable arg3 = null, ParsingScript script = null)
        {
            CustomFunction customFunction = interpreter.GetFunction(functionName) as CustomFunction;

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

            Variable result = customFunction.Run(args, script);
            return Task.FromResult( result );
        }

        public static Task<Variable> Run(Interpreter interpreter, string functionName,
             List<Variable> args, ParsingScript script = null)
        {
            CustomFunction customFunction = interpreter.GetFunction(functionName) as CustomFunction;

            if (customFunction == null)
            {
                return null;
            }

            Variable result = customFunction.Run(args, script);
            return Task.FromResult(result);
        }

        public static async Task<Variable> RunAsync(Interpreter interpreter, string functionName,
             Variable arg1 = null, Variable arg2 = null, Variable arg3 = null, ParsingScript script = null)
        {
            CustomFunction customFunction = interpreter.GetFunction(functionName) as CustomFunction;

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

            Variable result = await customFunction.RunAsync(args, script);
            return result;
        }

        public static async Task<Variable> RunAsync(Interpreter interpreter, string functionName,
             List<Variable> args, ParsingScript script = null)
        {
            CustomFunction customFunction = interpreter.GetFunction(functionName) as CustomFunction;

            if (customFunction == null)
            {
                return null;
            }

            Variable result = await customFunction.RunAsync(args, script);
            return result;
        }

        public override ParserFunction NewInstance()
        {
            var newInstance = (CustomFunction)this.MemberwiseClone();
            newInstance.m_stackLevel = null;
            return newInstance;
        }

        public ParsingScript ParentScript { set { m_parentScript = value; } }
        public int ParentOffset { set { m_parentOffset = value; } }
        public string Body { get { return m_body; } }

        public int ArgumentCount { get { return m_args.Length; } }
        public string Argument(int nIndex) { return m_args[nIndex]; }

        public StackLevel NamespaceData { get; set; }

        public int DefaultArgsCount
        {
            get
            {
                return m_defaultArgs.Count;
            }
        }

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
        protected StackLevel m_stackLevel;

        List<Variable> m_defaultArgs = new List<Variable>();
        Dictionary<int, int> m_defArgMap = new Dictionary<int, int>();

        public Dictionary<string, int> ArgMap { get; private set; } = new Dictionary<string, int>();
        public string[] RealArgs { get; private set; }
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
                InterpreterInstance.AddGlobalOrLocalVariable(currentValue.ParsingToken,
                                                        new GetVarFunction(currentValue), script);
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
                InterpreterInstance.AddGlobalOrLocalVariable(currentValue.ParsingToken,
                                                        new GetVarFunction(currentValue), script);
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
            ParserFunction func = InterpreterInstance.GetVariable(varName, script);
            Utils.CheckNotNull(varName, func, script);
            Variable currentValue = func.GetValue(script);
            Utils.CheckArray(currentValue, varName);

            // 3. Get the variable to remove.
            Variable item = args[1];

            bool removed = currentValue.Tuple.Remove(item);

            InterpreterInstance.AddGlobalOrLocalVariable(varName,
                                                    new GetVarFunction(currentValue), script);
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
            ParserFunction func = InterpreterInstance.GetVariable(varName, script);
            Utils.CheckNotNull(varName, func, script);
            Variable currentValue = func.GetValue(script);
            Utils.CheckArray(currentValue, varName);

            // 3. Get the variable to remove.
            Variable item = Utils.GetItem(script);
            Utils.CheckNonNegativeInt(item, script);

            currentValue.Tuple.RemoveAt(item.AsInt());

            InterpreterInstance.AddGlobalOrLocalVariable(varName,
                                                    new GetVarFunction(currentValue), script);
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

            ParserFunction func = InterpreterInstance.GetVariable(varName, script);
            Utils.CheckNotNull(varName, func, script);
            Variable currentValue = func.GetValue(script);

            // 2b. Special dealings with arrays:
            Variable query = arrayIndices.Count > 0 ?
                             Utils.ExtractArrayElement(currentValue, arrayIndices, script) :
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

    class UndefinedFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(Variable.VarType.UNDEFINED);
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
    class ToByteArrayFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);

            string arg = Utils.GetSafeString(args, 0);
            string pass = Utils.GetSafeString(args, 1);

            var encoded = !string.IsNullOrWhiteSpace(pass);

            var bytes = encoded ? Utils.EncryptStringToBytes(arg, pass) :
                                  Encoding.UTF8.GetBytes(arg);

            return new Variable(bytes);
        }
    }

    class EncodeDecodeFunction : ParserFunction, IStringFunction
    {
        bool m_encode;
        internal EncodeDecodeFunction(bool encode = true)
        {
            m_encode = encode;
        }
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 2, m_name);

            string arg = Utils.GetSafeString(args, 0);
            string pass = Utils.GetSafeString(args, 1);

            var result = m_encode ? Utils.EncryptString(arg, pass) :
                                    Utils.DecryptString(arg, pass);

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
            Utils.CheckArgs(args.Count, 1, m_name);

            Variable arg = args[0];
            string format = Utils.GetSafeString(args, 1);

            if (arg.Type == Variable.VarType.BYTE_ARRAY && !string.IsNullOrWhiteSpace(format))
            {
                var decoded = Utils.DecryptStringFromBytes(arg.AsByteArray(), format);
                return new Variable(decoded);
            }

            string result = arg.AsString(format);
            return new Variable(result);
        }
    }
    class IdentityFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return script.Execute(Constants.END_ARG_ARRAY);
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            return await script.ExecuteAsync(Constants.END_ARG_ARRAY);
        }
    }

    class ConstantsFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(m_name);
        }
    }

    class IfStatement : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            Variable result = InterpreterInstance.ProcessIf(script);
            return result;
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            Variable result = await InterpreterInstance.ProcessIfAsync(script);
            return result;
        }
        public override string Description()
        {
            return "If-else control flow statements. if (condition) { statements; } elif(condition) { statements; } else { statements; }";
        }
    }

    class ForStatement : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return InterpreterInstance.ProcessFor(script);
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            return await InterpreterInstance.ProcessForAsync(script);
        }
        public override string Description()
        {
            return "A canonic for loop, e.g. for (i = 0; i < 10; ++i) or for (item : listOfValues)";
        }
    }

    class WhileStatement : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return InterpreterInstance.ProcessWhile(script);
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            return await InterpreterInstance.ProcessWhileAsync(script);
        }
        public override string Description()
        {
            return "Execute a loop as long as the condition is true: while (condition) { statements; }";
        }
    }

    class DoWhileStatement : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return InterpreterInstance.ProcessDoWhile(script);
        }
        public override string Description()
        {
            return "Execute a loop at least once and as long as the condition is true: do { statements; } while (condition);";
        }
    }

    class SwitchStatement : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return InterpreterInstance.ProcessSwitch(script);
        }
        public override string Description()
        {
            return "Execute a switch(value) statement.";
        }
    }

    class CaseStatement : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return InterpreterInstance.ProcessCase(script, Name);
        }
        public override string Description()
        {
            return "A case inside of a switch statement.";
        }
    }

    class IncludeFile : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            string filename = args[0].AsString();
            return Execute(filename, InterpreterInstance, script);
        }

        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            List<Variable> args = await script.GetFunctionArgsAsync();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            string filename = args[0].AsString();
            return await ExecuteAsync(filename, InterpreterInstance, script);
        }

        public static Variable Execute(string filename, Interpreter interpreter, ParsingScript parentScript = null)
        {
            if (parentScript == null)
            {
                parentScript = new ParsingScript(interpreter, "");
            }
            ParsingScript tempScript = parentScript.GetIncludeFileScript(filename);

            Variable result = null;
            if (parentScript.Debugger != null)
            {
                result = parentScript.Debugger.StepInIncludeIfNeeded(tempScript).Result;
            }

            while (tempScript.StillValid())
            {
                result = tempScript.Execute();
                tempScript.GoToNextStatement();
            }
            return result == null ? Variable.EmptyInstance : result;
        }

        public static Variable Execute(string filename, Interpreter interpreter)
        {
            return Execute(filename, interpreter, new ParsingScript(interpreter, ""));
        }

        public static async Task<Variable> ExecuteAsync(string filename, Interpreter interpreter, ParsingScript parentScript = null)
        {
            if (parentScript == null)
            {
                parentScript = new ParsingScript(interpreter, "");
            }
            ParsingScript tempScript = parentScript.GetIncludeFileScript(filename);

            Variable result = null;
            if (parentScript.Debugger != null)
            {
                result = await parentScript.Debugger.StepInIncludeIfNeeded(tempScript);
            }

            while (tempScript.StillValid())
            {
                result = await tempScript.ExecuteAsync();
                tempScript.GoToNextStatement();
            }
            return result == null ? Variable.EmptyInstance : result;
        }

        public static Task<Variable> ExecuteAsync(string filename, Interpreter interpreter)
        {
            return ExecuteAsync(filename, interpreter, new ParsingScript(interpreter, ""));
        }

        public override string Description()
        {
            var name = this.GetType().Name;
            return "Includes another scripting file, e.g. include(\"functions.cscs\");";
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
            if (script == null)
            {
                return m_value;
            }
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

                Variable result = Utils.ExtractArrayElement(m_value, m_arrayIndices, script);
                if (script.Prev == '.')
                {
                    script.Backward();
                }

                if (script.TryCurrent() != '.')
                {
                    return result;
                }
                script.Forward();

                m_propName = Utils.GetToken(script, Constants.TOKEN_SEPARATION);
                Variable propValue = result.GetProperty(m_propName, script);
                Utils.CheckNotNull(propValue, m_propName, script);
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
                Utils.CheckNotNull(propValue, temp, script);
                return EvaluateFunction(propValue, script, m_propName);
            }

            // Otherwise just return the stored value.
            return m_value;
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            if (script == null)
            {
                return m_value;
            }
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

                Variable result = Utils.ExtractArrayElement(m_value, m_arrayIndices, script);
                if (script.Prev == '.')
                {
                    script.Backward();
                }
                if (script.TryCurrent() != '.')
                {
                    return result;
                }

                script.Forward();
                m_propName = Utils.GetToken(script, Constants.NEXT_OR_END_ARRAY);
                Variable propValue = await result.GetPropertyAsync(m_propName, script); 
                Utils.CheckNotNull(propValue, m_propName, script);
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
                return await EvaluateFunctionAsync(propValue, script, m_propName);
            }

            // Otherwise just return the stored value.
            return m_value;
        }

        public static Variable EvaluateFunction(Variable var, ParsingScript script, string m_propName)
        {
            if (var.CustomFunctionGet != null)
            {
                List<Variable> args = script.Prev == '(' ? script.GetFunctionArgs() : new List<Variable>();
                if (var.StackVariables != null)
                {
                    args.AddRange(var.StackVariables);
                }
                return var.CustomFunctionGet.Run(args, script);
            }
            if (!string.IsNullOrWhiteSpace(var.CustomGet))
            {
                return ParsingScript.RunString(script.InterpreterInstance, var.CustomGet); 
            }
            return var;
        }

        public static async Task<Variable> EvaluateFunctionAsync(Variable var, ParsingScript script, string m_propName)
        {
            if (var.CustomFunctionGet != null)
            {
                List<Variable> args = script.Prev == '(' ? await script.GetFunctionArgsAsync() : new List<Variable>();
                if (var.StackVariables != null)
                {
                    args.AddRange(var.StackVariables);
                }
                return await var.CustomFunctionGet.RunAsync(args, script);
            }
            if (!string.IsNullOrWhiteSpace(var.CustomGet))
            {
                return ParsingScript.RunString(script.InterpreterInstance, var.CustomGet);
            }
            return var;
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

            Utils.CheckForValidName(Name, script);

            // Value to be added to the variable:
            int valueDelta = m_action == Constants.INCREMENT ? 1 : -1;
            int returnDelta = prefix ? valueDelta : 0;

            // Check if the variable to be set has the form of x[a][b],
            // meaning that this is an array element.
            double newValue = 0;
            List<Variable> arrayIndices = Utils.GetArrayIndices(script, m_name, (string name) => { m_name = name; });

            ParserFunction func = InterpreterInstance.GetVariable(m_name, script);
            Utils.CheckNotNull(m_name, func, script);

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

                Variable element = Utils.ExtractArrayElement(currentValue, arrayIndices, script);
                script.MoveForwardIf(Constants.END_ARRAY);

                newValue = element.Value + returnDelta;
                element.Value += valueDelta;
            }
            else
            { // A normal variable.
                newValue = currentValue.Value + returnDelta;
                currentValue.Value += valueDelta;
            }

            InterpreterInstance.AddGlobalOrLocalVariable(m_name,
                                                    new GetVarFunction(currentValue), script);
            return new Variable(newValue);
        }

        override public ParserFunction NewInstance()
        {
            var newFunc = new IncrementDecrementFunction();
            newFunc.InterpreterInstance = InterpreterInstance;
            return newFunc;
        }
    }

    class OperatorAssignFunction : ActionFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            // Value to be added to the variable:
            Variable right = Utils.GetItem(script);

            List<Variable> arrayIndices = Utils.GetArrayIndices(script, m_name, (string name) => { m_name = name; });

            ParserFunction func = InterpreterInstance.GetVariable(m_name, script);
            Utils.CheckNotNull(func, m_name, script);

            Variable currentValue = func.GetValue(script);
            currentValue = currentValue.DeepClone();
            Variable left = currentValue;

            if (arrayIndices.Count > 0)
            {// array element
                left = Utils.ExtractArrayElement(currentValue, arrayIndices, script);
                script.MoveForwardIf(Constants.END_ARRAY);
            }

            if (left.Type == Variable.VarType.NUMBER)
            {
                NumberOperator(left, right, m_action);
            }
            else if (left.Type == Variable.VarType.DATETIME)
            {
                DateOperator(left, right, m_action, script,m_name);
            }
            else
            {
                StringOperator(left, right, m_action);
            }

            if (arrayIndices.Count > 0)
            {// array element
                AssignFunction.ExtendArray(currentValue, arrayIndices, 0, left);
                InterpreterInstance.AddGlobalOrLocalVariable(m_name,
                                                         new GetVarFunction(currentValue), script);
            }
            else
            {
                InterpreterInstance.AddGlobalOrLocalVariable(m_name,
                                                         new GetVarFunction(left), script);
            }
            return left;
        }

        public static void DateOperator(Variable valueA,
                          Variable valueB, string action, ParsingScript script, string name = "")
        {
            int sign = 1;
            char ch = action.Length > 0 ? action[0] : '\0';
            switch (ch)
            {
               case '+':
                   sign = 1;
                   break;
               case '-':
                   sign = -1;
                   break;
               default:
                   Utils.ThrowErrorMsg("Not a valid action [" + action + "] on a date.",
                                        script, name);
                   break;
            }
            valueA.AddToDate(valueB, sign);
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
            var newFunc = new OperatorAssignFunction();
            newFunc.InterpreterInstance = InterpreterInstance;
            return newFunc;
        }
    }

    class AssignFunction : ActionFunction
    {
        public AssignFunction()
        {

        }

        public AssignFunction(Interpreter interpreter)
        {
            InterpreterInstance = interpreter;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            return Assign(script, m_name);
        }

        public Variable Assign(ParsingScript script, string varName, bool localIfPossible = false)
        {
            m_name = Constants.GetRealName(varName);
            script.CurrentAssign = m_name;
            Variable varValue = Utils.GetItem(script);

            script.MoveBackIfPrevious(Constants.END_ARG);
            varValue.TrySetAsMap();

            if (script.Current == ' ' || script.Prev == ' ')
            {
                Utils.ThrowErrorMsg("Can't process expression [" + script.Rest + "].",
                                    script, m_name);
            }

            // First try processing as an object (with a dot notation):
            Variable result = ProcessObject(script, varValue);
            if (result != null)
            {
                if (script.CurrentClass == null && script.ClassInstance == null)
                {
                    InterpreterInstance.AddGlobalOrLocalVariable(m_name, new GetVarFunction(result), script, localIfPossible);
                }
                return result;
            }

            // Check if the variable to be set has the form of x[a][b]...,
            // meaning that this is an array element.
            List<Variable> arrayIndices = Utils.GetArrayIndices(script, m_name, (string name) => { m_name = name; });

            if (arrayIndices.Count == 0)
            {
                InterpreterInstance.AddGlobalOrLocalVariable(m_name, new GetVarFunction(varValue), script, localIfPossible);
                Variable retVar = varValue.DeepClone(m_name);
                retVar.CurrentAssign = m_name;
                return retVar;
            }

            Variable array;

            ParserFunction pf = InterpreterInstance.GetVariable(m_name, script);
            array = pf != null ? (pf.GetValue(script)) : new Variable();

            ExtendArray(array, arrayIndices, 0, varValue);

            InterpreterInstance.AddGlobalOrLocalVariable(m_name, new GetVarFunction(array), script, localIfPossible);
            return array;
        }

        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            return await AssignAsync(script, m_name);
        }

        public async Task<Variable> AssignAsync(ParsingScript script, string varName, bool localIfPossible = false)
        {
            m_name = Constants.GetRealName(varName);
            script.CurrentAssign = m_name;
            Variable varValue = await Utils.GetItemAsync(script);

            script.MoveBackIfPrevious(Constants.END_ARG);
            varValue.TrySetAsMap();

            if (script.Current == ' ' || script.Prev == ' ')
            {
                Utils.ThrowErrorMsg("Can't process expression [" + script.Rest + "].",
                                    script, m_name);
            }

            // First try processing as an object (with a dot notation):
            Variable result = await ProcessObjectAsync(script, varValue);
            if (result != null)
            {
                if (script.CurrentClass == null)
                {
                    InterpreterInstance.AddGlobalOrLocalVariable(m_name, new GetVarFunction(result), script, localIfPossible);
                }
                return result;
            }

            // Check if the variable to be set has the form of x[a][b]...,
            // meaning that this is an array element.
            List<Variable> arrayIndices = await Utils.GetArrayIndicesAsync(script, m_name, (string name) => { m_name = name; });

            if (arrayIndices.Count == 0)
            {
                InterpreterInstance.AddGlobalOrLocalVariable(m_name, new GetVarFunction(varValue), script, localIfPossible);
                Variable retVar = varValue.DeepClone(m_name);
                retVar.CurrentAssign = m_name;
                return retVar;
            }

            Variable array;

            ParserFunction pf = InterpreterInstance.GetVariable(m_name, script);
            array = pf != null ? (await pf.GetValueAsync(script)) : new Variable();

            ExtendArray(array, arrayIndices, 0, varValue);

            InterpreterInstance.AddGlobalOrLocalVariable(m_name, new GetVarFunction(array), script, localIfPossible);
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

            if (InterpreterInstance.TryAddToNamespace(prop, name, varValue))
            {
                return varValue.DeepClone();
            }

            ParserFunction existing = InterpreterInstance.GetVariable(name, script);
            Variable baseValue = existing != null ? existing.GetValue(script) : new Variable(Variable.VarType.ARRAY);
            baseValue.SetProperty(prop, varValue, script, name);

            InterpreterInstance.AddGlobalOrLocalVariable(name, new GetVarFunction(baseValue), script);
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

            if (InterpreterInstance.TryAddToNamespace(prop, name, varValue))
            {
                return varValue.DeepClone();
            }

            ParserFunction existing = InterpreterInstance.GetVariable(name, script);
            Variable baseValue = existing != null ? await existing.GetValueAsync(script) : new Variable(Variable.VarType.ARRAY);
            await baseValue.SetPropertyAsync(prop, varValue, script, name);

            InterpreterInstance.AddGlobalOrLocalVariable(name, new GetVarFunction(baseValue), script);
            //ParserFunction.AddGlobal(name, new GetVarFunction(baseValue), false);

            return varValue.DeepClone();
        }


        override public ParserFunction NewInstance()
        {
            return new AssignFunction(InterpreterInstance);
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
            return varValue.DeepClone();
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

            // var function = ParserFunction.GetVariable(varName, script);
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

            InterpreterInstance.AddGlobalOrLocalVariable(varName,
                                                    new GetVarFunction(allTokensVar), script);

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

            var function = InterpreterInstance.GetVariable(varName, script);
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

            InterpreterInstance.AddGlobalOrLocalVariable(varName,
                                              new GetVarFunction(mapVar), script);
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

            var function = InterpreterInstance.GetVariable(varName, script);
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

            InterpreterInstance.AddGlobalOrLocalVariable(varName,
                                                new GetVarFunction(mapVar), script);

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

            // var function = ParserFunction.GetVariable(varName, script);
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

            InterpreterInstance.AddGlobalOrLocalVariable(varName,
                                                new GetVarFunction(mapVar), script);
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
        public override string Description()
        {
            return "Returns the type of the specified variable.";
        }
    }

    class TypeRefFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);

            string typeName = Utils.GetSafeString(args, 0);

            Type t = GetTypeAnywhere(typeName, true);

            // TODO: Support Using to look in other namespaces
            if (t == null)
                throw new ArgumentException($"Type [{typeName}] not found");
            return new Variable(t, typeof(Type));
        }

        public static Type GetTypeAnywhere(string typeName, bool ignoreCase = false)
        {
            // If this is called often and is slow, we could save the Assembly array globally
            // We could also save the answer in a Dictionary<string, Type>
            Type type = Type.GetType(typeName, false, ignoreCase);
            if (type != null)
                return type;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var a in assemblies)
            {
                type = a.GetType(typeName, false, ignoreCase);
                if (type != null)
                    return type;
            }

            return GetTypeFromAssembly(typeName, ignoreCase, System.Reflection.Assembly.GetEntryAssembly(), assemblies.ToList(), new List<System.Reflection.Assembly>());
        }

        static Type GetTypeFromAssembly(string typeName, bool ignoreCase, System.Reflection.Assembly assembly,
            List<System.Reflection.Assembly> loadedAssemblies, List<System.Reflection.Assembly> checkedAssemblies)
        {
            Type type;

            // Maybe it's not loaded yet. Try to load them all and look again.
            // I could put in an optimization to first look for any assemblies whose name begins with part of the type name
            var location = assembly.Location;
            var folder = Path.GetDirectoryName(location);

            var refAssemblies = assembly.GetReferencedAssemblies();
            foreach (var refAssemblyName in refAssemblies)
            {
                try
                {
                    var loadedAssembly = FindAssembly(loadedAssemblies, refAssemblyName);
                    if (loadedAssembly == null)
                    {
                        string name = refAssemblyName.Name;
                        string assemblyPath = Path.Combine(folder, name + ".dll");
                        if (File.Exists(assemblyPath))
                        {
                            loadedAssembly = System.Reflection.Assembly.LoadFrom(assemblyPath);
                            if (loadedAssembly != null)
                            {
                                type = loadedAssembly.GetType(typeName, false, ignoreCase);
                                if (type != null)
                                    return type;
                                loadedAssemblies.Add(loadedAssembly);
                            }
                        }
                    }
                    if (loadedAssembly != null)
                    {
                        if (FindAssembly(checkedAssemblies, refAssemblyName) == null)
                        {
                            checkedAssemblies.Add(loadedAssembly);
                            type = GetTypeFromAssembly(typeName, ignoreCase, loadedAssembly, loadedAssemblies, checkedAssemblies);
                            if (type != null)
                                return type;
                        }
                    }
                }
                catch
                {
                }
            }
            return null;
        }

        private static System.Reflection.Assembly FindAssembly(List<System.Reflection.Assembly> assemblies, System.Reflection.AssemblyName assemblyName)
        {
            return assemblies.Where((a) => a.FullName == assemblyName.FullName).FirstOrDefault();
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
            ParserFunction func = InterpreterInstance.GetVariable(varName, script);
            Utils.CheckNotNull(varName, func, script);
            Variable currentValue = func.GetValue(script);
            Variable element = currentValue;

            // 2b. Special case for an array.
            if (arrayIndices.Count > 0)
            {// array element
                element = Utils.ExtractArrayElement(currentValue, arrayIndices, script);
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
        public override string Description()
        {
            return "Returns either a number of elements in an array if variable is of type ARRAY or a number of characters in a string representation of this variable.";
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

            if (script.StackLevel != null)
            {
                InterpreterInstance.AddLocalVariable(new GetVarFunction(currentValue), varName);
            }
            else if (script.CurrentClass != null)
            {
                Utils.ThrowErrorMsg(m_name + " function can't be defined inside of a class.",
                                    script, m_name);
            }
            else
            {
                string scopeName = Path.GetFileName(script.Filename);
                InterpreterInstance.AddLocalScopeVariable(varName, scopeName,
                                                     new GetVarFunction(currentValue));
            }

            return currentValue;
        }
    }

    class GetPropertiesFunction : ParserFunction, IArrayFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            //List<Variable> args = script.GetFunctionArgs();
            //Utils.CheckArgs(args.Count, 1, m_name, true);
            string className = Utils.GetToken(script, Constants.TOKEN_SEPARATION);
            className = Constants.ConvertName(className);

            var cscsClass = InterpreterInstance.GetClass(className);
            if (cscsClass != null)
            {
                var result = Interpreter.GetClassProperties(cscsClass);
                return new Variable(result);
            }

            var pf = InterpreterInstance.GetVariable(className, script);
            if (pf == null)
            {
                return Variable.EmptyInstance;
            }

            Variable baseValue = pf.GetValue(script);
            List<Variable> props = baseValue.GetProperties();
            return new Variable(props);
        }
    }

    class MarshalFunction : ParserFunction, IArrayFunction
    {
        bool m_marshal;
        public MarshalFunction(bool marshal = true)
        {
            m_marshal = marshal;
        }
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            var obj = Utils.GetSafeVariable(args, 0);
            string objName = obj.ParsingToken;
            objName = Constants.ConvertName(objName);

            if (!m_marshal)
            {
                Variable result = Unmarshal(obj.String, InterpreterInstance);
                return result;
            }

            var cscsVar = Marshal(objName, InterpreterInstance, script);
            if (string.IsNullOrWhiteSpace(cscsVar))
            {
                return Variable.EmptyInstance;
            }
            return new Variable(cscsVar);
        }

        public static string Marshal(string varName, Interpreter interpreter, ParsingScript script = null)
        {
            var classInstance = CSCSClass.ClassInstance.MarshalInstance(varName);
            if (classInstance != null)
            {
                return classInstance;
            }

            ParserFunction func = interpreter.GetVariable(varName, script);
            Variable currentValue = func != null ? func.GetValue(script) : new Variable(varName);
            varName = func == null ? "" : varName;
            var result = currentValue.Marshal(varName);

            return result;
        }

        public static Variable Unmarshal(string source, Interpreter interpreter)
        {
            Variable result = Variable.EmptyInstance;
            if (string.IsNullOrWhiteSpace(source))
            {
                return result;
            }
            int pointer = 0;
            string instanceName = Utils.GetNextToken(source, ref pointer, ':');
            var type = Utils.GetNextToken(source, ref pointer, ':');

            if (type == "class")
            {
                var className = Utils.GetNextToken(source, ref pointer, ':');
                var cscsClass = interpreter.GetClass(className);
                if (cscsClass == null)
                {
                    throw new ArgumentException("Class [" + className + "] not found.");
                }

                var args = new List<Variable>();
                var classInstance = new CSCSClass.ClassInstance(interpreter, instanceName, className, args);
                while (pointer < source.Length)
                {
                    var propName = Utils.GetNextToken(source, ref pointer, ':');
                    var propType = Utils.GetNextToken(source, ref pointer, ':');
                    var propValue = Variable.Unmarshal(propType, source, ref pointer);
                    classInstance.SetProperty(propName, propValue);
                }
                result = new Variable(classInstance);
            }
            else
            {
                result = Variable.Unmarshal(type, source, ref pointer);
            }

            return result;
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
            Utils.CheckNotNull(propValue, propName, script);

            return new Variable(propValue);
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            List<Variable> args = await script.GetFunctionArgsAsync();
            Utils.CheckArgs(args.Count, 2, m_name, true);

            Variable baseValue = args[0];
            string propName = Utils.GetSafeString(args, 1);

            Variable propValue = await baseValue.GetPropertyAsync(propName, script);
            Utils.CheckNotNull(propValue, propName, script);

            return new Variable(propValue);
        }
        public static Variable GetProperty(ParsingScript script, string sPropertyName)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, "GetProperty", true);

            Variable baseValue = args[0];

            Variable propValue = baseValue.GetProperty(sPropertyName, script);
            Utils.CheckNotNull(propValue, sPropertyName, script);

            return new Variable(propValue);
        }
        public static async Task<Variable> GetPropertyAsync(ParsingScript script, string sPropertyName)
        {
            List<Variable> args = await script.GetFunctionArgsAsync();
            Utils.CheckArgs(args.Count, 1, "GetProperty", true);

            Variable baseValue = args[0];

            Variable propValue = await baseValue.GetPropertyAsync(sPropertyName, script);
            Utils.CheckNotNull(propValue, sPropertyName, script);

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

            Variable result = baseValue.SetProperty(propName, propValue, script);

            InterpreterInstance.AddGlobalOrLocalVariable(baseValue.ParsingToken,
                                                    new GetVarFunction(baseValue), script);
            return result;
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            List<Variable> args = await script.GetFunctionArgsAsync();
            Utils.CheckArgs(args.Count, 3, m_name, true);

            Variable baseValue = args[0];
            string propName = Utils.GetSafeString(args, 1);
            Variable propValue = Utils.GetSafeVariable(args, 2);

            Variable result = await baseValue.SetPropertyAsync(propName, propValue, script);

            InterpreterInstance.AddGlobalOrLocalVariable(baseValue.ParsingToken,
                                                    new GetVarFunction(baseValue), script);
            return result;
        }

#if false

        public static Variable SetProperty(ParsingScript script, string sPropertyName)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 2, "SetProperty", true);

            Variable baseValue = args[0];
            Variable propValue = Utils.GetSafeVariable(args, 1);

            Variable result = baseValue.SetProperty(sPropertyName, propValue, script);

            InterpreterInstance.AddGlobalOrLocalVariable(baseValue.ParsingToken,
                                                    new GetVarFunction(baseValue), script);
            return result;
        }
        public static async Task<Variable> SetPropertyAsync(ParsingScript script, string sPropertyName)
        {
            List<Variable> args = await script.GetFunctionArgsAsync();
            Utils.CheckArgs(args.Count, 2, "SetProperty", true);

            Variable baseValue = args[0];
            Variable propValue = Utils.GetSafeVariable(args, 1);

            Variable result = await baseValue.SetPropertyAsync(sPropertyName, propValue, script);

            InterpreterInstance.AddGlobalOrLocalVariable(baseValue.ParsingToken,
                                                    new GetVarFunction(baseValue), script);
            return result;
        }
#endif
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

                ParsingScript tempScript = NewParsingScript(body);
                tempScript.Execute();
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
            expr = Utils.ConvertToScript(InterpreterInstance, expr, out char2Line);

            Variable result;
            if (m_singletons.TryGetValue(expr, out result))
            {
                return result;
            }

            ParsingScript tempScript = NewParsingScript(expr);
            result = tempScript.Execute();

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
            Utils.CheckNotNull(varName, m_name, script);

            List<Variable> results = varName.GetAllKeys();

            return new Variable(results);
        }
    }

    class CheckLoaderMainFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            bool isMain = !string.IsNullOrWhiteSpace(script.MainFilename) &&
                           script.MainFilename == script.Filename;

            return new Variable(isMain);
        }
    }

    class ResetVariablesFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            InterpreterInstance.CleanUpVariables();
            return Variable.EmptyInstance;
        }
    }
}
