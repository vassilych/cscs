using System;
using System.Collections.Generic;
using System.Linq;
using static System.Math;
using System.Threading.Tasks;

namespace SplitAndMerge
{
    interface INumericFunction { }
    interface IArrayFunction { }
    interface IStringFunction { }

    class PiFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(Math.PI);
        }
    }

    class ExpFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];
            arg.Value = Exp(arg.Value);
            return arg;
        }
    }

    class PowFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 2, m_name, true);
            Variable arg1 = args[0];
            Variable arg2 = args[1];

            arg1.Value = Math.Pow(arg1.Value, arg2.Value);
            return arg1;
        }
    }

    class SinFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];
            arg.Value = Sin(arg.Value);
            return arg;
        }
    }

    class CosFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];
            arg.Value = Math.Cos(arg.Value);
            return arg;
        }
    }

    class AsinFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];
            arg.Value = Math.Asin(arg.Value);
            return arg;
        }
    }

    class AcosFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];
            arg.Value = Math.Acos(arg.Value);
            return arg;
        }
    }

    class SqrtFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];
            arg.Value = Math.Sqrt(arg.Value);
            return arg;
        }
    }

    class AbsFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];
            arg.Value = Math.Abs(arg.Value);
            return arg;
        }
    }

    class CeilFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];
            arg.Value = Math.Ceiling(arg.Value);
            return arg;
        }
    }

    class FloorFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];
            arg.Value = Math.Floor(arg.Value);
            return arg;
        }
    }

    class RoundFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);

            int numberDigits = Utils.GetSafeInt(args, 1, 0);
            args[0].Value = Math.Round(args[0].Value, numberDigits);
            return args[0];
        }
    }

    class LogFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];
            arg.Value = Math.Log(arg.Value);
            return arg;
        }
    }
    class GetRandomFunction : ParserFunction, INumericFunction
    {
        static Random m_random = new Random();

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);
            int limit = args[0].AsInt();
            Utils.CheckPosInt(args[0], script);
            int numberRandoms = Utils.GetSafeInt(args, 1, 1);

            if (numberRandoms <= 1)
            {
                return new Variable(m_random.Next(0, limit));
            }

            List<int> available = Enumerable.Range(0, limit).ToList();
            List<Variable> result = new List<Variable>();

            for (int i = 0; i < numberRandoms && available.Count > 0; i++)
            {
                int nextRandom = m_random.Next(0, available.Count);
                result.Add(new Variable(available[nextRandom]));
                available.RemoveAt(nextRandom);
            }

            return new Variable(result);
        }
    }

}
