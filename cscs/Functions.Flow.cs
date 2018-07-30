using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

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
    }

    class TryBlock : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return Interpreter.Instance.ProcessTry(script);
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
            Translation.PrintScript(body, script);

            return new Variable(body);
        }
    }

    class TranslateFunction : ParserFunction, IStringFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 2, m_name, true);

            string language = args[0].AsString();
            string funcName = args[1].AsString();

            ParserFunction function = ParserFunction.GetFunction(funcName, script);
            CustomFunction custFunc = function as CustomFunction;
            Utils.CheckNotNull(funcName, custFunc);

            string body = Utils.BeautifyScript(custFunc.Body, custFunc.Header);
            string translated = Translation.TranslateScript(body, language, script);
            Translation.PrintScript(translated, script);

            return new Variable(translated);
        }
    }

    class FunctionCreator : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string funcName = Utils.GetToken(script, Constants.TOKEN_SEPARATION);
            //Interpreter.Instance.AppendOutput("Registering function [" + funcName + "] ...");

            string[] args = Utils.GetFunctionSignature(script);
            if (args.Length == 1 && string.IsNullOrWhiteSpace(args[0]))
            {
                args = new string[0];
            }

            script.MoveForwardIf(Constants.START_GROUP, Constants.SPACE);
            int parentOffset = script.Pointer;

            string body = Utils.GetBodyBetween(script, Constants.START_GROUP, Constants.END_GROUP);

            CustomFunction customFunc = new CustomFunction(funcName, body, args);
            customFunc.ParentScript = script;
            customFunc.ParentOffset = parentOffset;

            ParserFunction.RegisterFunction(funcName, customFunc, false /* not native */);

            return Variable.EmptyInstance;
        }
    }

    class CustomFunction : ParserFunction
    {
        internal CustomFunction(string funcName,
                                string body, string[] args)
        {
            m_name = funcName;
            m_body = body;
            m_args = args;
        }

        public void RegisterArguments(List<Variable> args)
        {
            StackLevel stackLevel = new StackLevel(m_name);
            for (int i = 0; i < m_args.Length; i++)
            {
                var arg = new GetVarFunction(args[i]);
                arg.Name = m_args[i];
                stackLevel.Variables[m_args[i]] = arg;
            }

            ParserFunction.AddLocalVariables(stackLevel);
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            script.MoveBackIf(Constants.START_GROUP);

            if (args.Count != m_args.Length)
            {
                throw new ArgumentException("Function [" + m_name + "] arguments mismatch: " +
                                    m_args.Length + " declared, " + args.Count + " supplied");
            }

            // 1. Add passed arguments as local variables to the Parser.
            RegisterArguments(args);

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
            tempScript.InTryBlock = script.InTryBlock;

            if (script.Debugger != null)
            {
                result = script.Debugger.StepInFunctionIfNeeded(tempScript);
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

        public ParsingScript ParentScript { set { m_parentScript = value; } }
        public int ParentOffset { set { m_parentOffset = value; } }
        public string Body { get { return m_body; } }
        public string Header
        {
            get
            {
                return Constants.FUNCTION + " " + Name + " " +
                       Constants.START_ARG + string.Join(", ", m_args) +
                       Constants.END_ARG + " " + Constants.START_GROUP;
            }
        }

        protected string m_body;
        protected string[] m_args;
        protected ParsingScript m_parentScript = null;
        protected int m_parentOffset = 0;
    }

    class CompiledFunctionCreator : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string funcReturn, funcName;
            Utils.GetCompiledArgs(script, out funcReturn, out funcName);

            Precompiler.RegisterReturnType(funcName, funcReturn);

            Dictionary<string, Variable> argsMap;
            string[] args = Utils.GetCompiledFunctionSignature(script, out argsMap);

            script.MoveForwardIf(Constants.START_GROUP, Constants.SPACE);
            int parentOffset = script.Pointer;

            string body = Utils.GetBodyBetween(script, Constants.START_GROUP, Constants.END_GROUP);

            Precompiler precompiler = new Precompiler(funcName, args, argsMap, body, script);
            precompiler.Compile();

            CustomCompiledFunction customFunc = new CustomCompiledFunction(funcName, body, args, precompiler, argsMap);
            customFunc.ParentScript = script;
            customFunc.ParentOffset = parentOffset;

            ParserFunction.RegisterFunction(funcName, customFunc, false /* not native */);

            return new Variable(funcName);
        }
    }
    class CustomCompiledFunction : CustomFunction
    {
        internal CustomCompiledFunction(string funcName,
                                        string body, string[] args,
                                        Precompiler precompiler,
                                        Dictionary<string, Variable> argsMap)
          : base(funcName, body, args)
        {
            m_precompiler = precompiler;
            m_argsMap = argsMap;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            script.MoveBackIf(Constants.START_GROUP);

            if (args.Count != m_args.Length)
            {
                throw new ArgumentException("Function [" + m_name + "] arguments mismatch: " +
                                    m_args.Length + " declared, " + args.Count + " supplied");
            }

            Variable result = Run(args);
            return result;
        }

        public Variable Run(List<Variable> args)
        {
            RegisterArguments(args);

            List<string> argsStr = new List<string>();
            List<double> argsNum = new List<double>();
            List<List<string>> argsArrStr = new List<List<string>>();
            List<List<double>> argsArrNum = new List<List<double>>();
            List<Dictionary<string, string>> argsMapStr = new List<Dictionary<string, string>>();
            List<Dictionary<string, double>> argsMapNum = new List<Dictionary<string, double>>();

            for (int i = 0; i < m_args.Length; i++)
            {
                Variable typeVar = m_argsMap[m_args[i]];
                if (typeVar.Type == Variable.VarType.STRING)
                {
                    argsStr.Add(args[i].AsString());
                }
                else if (typeVar.Type == Variable.VarType.NUMBER)
                {
                    argsNum.Add(args[i].AsDouble());
                }
                else if (typeVar.Type == Variable.VarType.ARRAY_STR)
                {
                    List<string> subArrayStr = new List<string>();
                    var tuple = args[i].Tuple;
                    for (int j = 0; j < tuple.Count; j++)
                    {
                        subArrayStr.Add(tuple[j].AsString());
                    }
                    argsArrStr.Add(subArrayStr);
                }
                else if (typeVar.Type == Variable.VarType.ARRAY_NUM)
                {
                    List<double> subArrayNum = new List<double>();
                    var tuple = args[i].Tuple;
                    for (int j = 0; j < tuple.Count; j++)
                    {
                        subArrayNum.Add(tuple[j].AsDouble());
                    }
                    argsArrNum.Add(subArrayNum);
                }
                else if (typeVar.Type == Variable.VarType.MAP_STR)
                {
                    Dictionary<string, string> subMapStr = new Dictionary<string, string>();
                    var tuple = args[i].Tuple;
                    var keys = args[i].GetKeys();
                    for (int j = 0; j < tuple.Count; j++)
                    {
                        subMapStr.Add(keys[j], tuple[j].AsString());
                    }
                    argsMapStr.Add(subMapStr);
                }
                else if (typeVar.Type == Variable.VarType.MAP_NUM)
                {
                    Dictionary<string, double> subMapNum = new Dictionary<string, double>();
                    var tuple = args[i].Tuple;
                    var keys = args[i].GetKeys();
                    for (int j = 0; j < tuple.Count; j++)
                    {
                        subMapNum.Add(keys[j], tuple[j].AsDouble());
                    }
                    argsMapNum.Add(subMapNum);
                }
            }

            Variable result = m_precompiler.Run(argsStr, argsNum, argsArrStr, argsArrNum, argsMapStr, argsMapNum, false);
            ParserFunction.PopLocalVariables();

            return result;
        }

        Precompiler m_precompiler;
        Dictionary<string, Variable> m_argsMap;
    }

    class StringOrNumberFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            // First check if the passed expression is a string between quotes.
            if (Item.Length > 1 &&
                Item[0] == Constants.QUOTE &&
                Item[Item.Length - 1] == Constants.QUOTE)
            {
                return new Variable(Item.Substring(1, Item.Length - 2));
            }

            // Otherwise this should be a number.
            double num = Utils.ConvertToDouble(Item, "StringOrNumber");
            /*if (!Double.TryParse(Item, NumberStyles.Number |
                                 NumberStyles.AllowExponent |
                                 NumberStyles.Float,
                                 CultureInfo.InvariantCulture, out num)) {
              Utils.ThrowException(script, "parseToken", Item, "parseTokenExtra");
            }*/
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

            currentValue.AddVariable(item);
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
            List<Variable> arrayIndices = Utils.GetArrayIndices(script, ref varName);

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
    }

    class IfStatement : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            Variable result = Interpreter.Instance.ProcessIf(script);
            return result;
        }
    }

    class ForStatement : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return Interpreter.Instance.ProcessFor(script);
        }
    }

    class WhileStatement : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return Interpreter.Instance.ProcessWhile(script);
        }
    }

    class IncludeFile : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);

            string filename = args[0].AsString();
            string[] lines = Utils.GetFileLines(filename);

            string includeFile = string.Join(Environment.NewLine, lines);
            Dictionary<int, int> char2Line;
            string includeScript = Utils.ConvertToScript(includeFile, out char2Line);
            ParsingScript tempScript = new ParsingScript(includeScript, 0, char2Line);
            tempScript.Filename = filename;
            tempScript.OriginalScript = string.Join(Constants.END_LINE.ToString(), lines);
            tempScript.ParentScript = script;
            tempScript.InTryBlock = script.InTryBlock;

            Variable result = null;
            if (script.Debugger != null)
            {
                result = script.Debugger.StepInIncludeIfNeeded(tempScript);
            }

            while (tempScript.Pointer < includeScript.Length)
            {
                result = tempScript.ExecuteTo();
                tempScript.GoToNextStatement();
            }
            return result == null ? Variable.EmptyInstance : result;
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
                    m_arrayIndices = Utils.GetArrayIndices(script, ref startName, ref m_delta);
                }

                script.Forward(m_delta);
                while (script.MoveForwardIf(Constants.END_ARRAY))
                {
                }

                Variable result = Utils.ExtractArrayElement(m_value, m_arrayIndices);
                return result;
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

        private Variable m_value;
        private int m_delta = 0;
        private List<Variable> m_arrayIndices = null;
    }
    class IncrementDecrementFunction : ActionFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            bool prefix = string.IsNullOrWhiteSpace(m_name);
            if (prefix)
            {// If it is a prefix we do not have the variable name yet.
                m_name = Utils.GetToken(script, Constants.TOKEN_SEPARATION);
            }

            // Value to be added to the variable:
            int valueDelta = m_action == Constants.INCREMENT ? 1 : -1;
            int returnDelta = prefix ? valueDelta : 0;

            // Check if the variable to be set has the form of x[a][b],
            // meaning that this is an array element.
            double newValue = 0;
            List<Variable> arrayIndices = Utils.GetArrayIndices(script, ref m_name);

            ParserFunction func = ParserFunction.GetFunction(m_name, script);
            Utils.CheckNotNull(m_name, func);

            Variable currentValue = func.GetValue(script);

            if (arrayIndices.Count > 0 || script.TryCurrent() == Constants.START_ARRAY)
            {
                if (prefix)
                {
                    string tmpName = m_name + script.Rest;
                    int delta = 0;
                    arrayIndices = Utils.GetArrayIndices(script, ref tmpName, ref delta);
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

            List<Variable> arrayIndices = Utils.GetArrayIndices(script, ref m_name);

            ParserFunction func = ParserFunction.GetFunction(m_name, script);
            Utils.CheckNotNull(m_name, func);

            Variable currentValue = func.GetValue(script);
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
            Variable varValue = Utils.GetItem(script);

            // Check if the variable to be set has the form of x[a][b]...,
            // meaning that this is an array element.
            List<Variable> arrayIndices = Utils.GetArrayIndices(script, ref m_name);

            if (arrayIndices.Count == 0)
            {
                ParserFunction.AddGlobalOrLocalVariable(m_name, new GetVarFunction(varValue));
                return varValue.DeepClone();
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
            Utils.CheckArgs(args.Count, 3, m_name);

            string varName = Utils.GetSafeString(args, 0);
            Variable lines = Utils.GetSafeVariable(args, 1);
            int fromLine = Utils.GetSafeInt(args, 2);
            string sepStr = Utils.GetSafeString(args, 3, "\t");
            if (sepStr == "\\t")
            {
                sepStr = "\t";
            }
            char[] sep = sepStr.ToCharArray();

            var function = ParserFunction.GetFunction(varName, script);
            Variable allTokensVar = new Variable(Variable.VarType.ARRAY);

            for (int counter = fromLine; counter < lines.Tuple.Count; counter++)
            {
                Variable lineVar = lines.Tuple[counter];
                Variable toAdd = new Variable(counter - fromLine);
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

            var function = ParserFunction.GetFunction(varName, script);
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
            return mapVar;
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

    class TypeFunction : ParserFunction, IStringFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            // 1. Get the name of the variable.
            string varName = Utils.GetToken(script, Constants.END_ARG_ARRAY);
            Utils.CheckNotEnd(script, m_name);

            List<Variable> arrayIndices = Utils.GetArrayIndices(script, ref varName);

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

            // 3. Convert type to string.
            string type = Constants.TypeToString(element.Type);
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

            List<Variable> arrayIndices = Utils.GetArrayIndices(script, ref varName);

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
            int size = element.Type == Variable.VarType.ARRAY ?
                              element.Tuple.Count :
                              element.AsString().Length;

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

            string varName  = Utils.GetSafeString(args, 0);
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
}