using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SplitAndMerge
{
#if UNITY_EDITOR == false && UNITY_STANDALONE == false
    class WriteToConsole : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();

            for (int i = 0; i < args.Count; i++)
            {
                Console.Write(args[i].AsString());
            }
            Console.WriteLine();

            return Variable.EmptyInstance;
        }
    }

#if __ANDROID__ == false && __IOS__ == false
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
            Utils.PrintScript(translated, script);

            return new Variable(translated);
        }
    }

    // Reads either a string or a number from the Console.
    class ReadConsole : ParserFunction
    {
        internal ReadConsole(bool isNumber = false)
        {
            m_isNumber = isNumber;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            script.Forward(); // Skip opening parenthesis.
            string line = Console.ReadLine();

            if (!m_isNumber)
            {
                return new Variable(line);
            }

            double number = Double.NaN;
            if (!Double.TryParse(line, out number))
            {
                throw new ArgumentException("Couldn't parse number [" + line + "]");
            }

            return new Variable(number);
        }

        private bool m_isNumber;
    }

    class ClearConsole : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            Console.Clear();
            return Variable.EmptyInstance;
        }
    }

    // Prints passed list of arguments
    class PrintColorFunction : ParserFunction
    {
        internal PrintColorFunction(bool newLine = true)
        {
            m_newLine = newLine;
        }

        internal PrintColorFunction(ConsoleColor fgcolor)
        {
            m_fgcolor = fgcolor;
            m_changeColor = true;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();

            string output = string.Empty;
            for (int i = 0; i < args.Count; i++)
            {
                output += args[i].AsString();
            }

            output += (m_newLine ? Environment.NewLine : string.Empty);
            if (m_changeColor)
            {
                Utils.PrintColor(output, m_fgcolor);
            }
            else
            {
                Interpreter.Instance.AppendOutput(output);
            }

            if (script.Debugger != null)
            {
                script.Debugger.AddOutput(output, script);
            }

            return Variable.EmptyInstance;
        }

        private bool m_newLine = true;
        private bool m_changeColor = false;
        private ConsoleColor m_fgcolor;
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

            CustomCompiledFunction customFunc = new CustomCompiledFunction(funcName, body, args, precompiler, argsMap, script);
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
                                        Dictionary<string, Variable> argsMap,
                                        ParsingScript script)
          : base(funcName, body, args, script)
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
#endif
#endif
}
