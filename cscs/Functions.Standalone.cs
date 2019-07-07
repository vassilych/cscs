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
            Utils.CheckNotNull(funcName, custFunc, script);

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
#endif
#endif
}
