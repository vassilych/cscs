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
    class EFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(Math.E);
        }
    }
    class Sqrt2Function : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(Math.Sqrt(2));
        }
    }
    class Sqrt1_2Function : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(Math.Sqrt(1/2));
        }
    }
    class Ln2Function : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(Math.Log(2));
        }
    }
    class Ln10Function : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(Math.Log(10));
        }
    }
    class Log2EFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(Math.Log(Math.E, 2)) ;
        }
    }
    class Log10EFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(Math.Log10(Math.E));
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
    class TanFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            return new Variable(Math.Tan(args[0].Value));
        }
    }
    class SinhFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];
            arg.Value = Math.Sinh(arg.Value);
            return arg;
        }
    }
    class CoshFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];
            arg.Value = Math.Cosh(arg.Value);
            return arg;
        }
    }
    class TanhFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            return new Variable(Math.Tanh(args[0].Value));
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
    class AtanFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];
            arg.Value = Math.Atan(arg.Value);
            return arg;
        }
    }
    class Atan2Function : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 2, m_name, true);
            return new Variable(Math.Atan2(args[0].Value, args[1].Value));
        }
    }
    class AsinhFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];
            arg.Value = Math.Log(arg.Value + Math.Sqrt(arg.Value * arg.Value + 1));
            return arg;
        }
    }
    class AcoshFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];
            arg.Value = Math.Log(arg.Value + Math.Sqrt(arg.Value * arg.Value - 1));
            return arg;
        }
    }
    class AtanhFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];
            arg.Value = Math.Log((1 + arg.Value) / (1 - arg.Value)) / 2;
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
    class CbrtFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            return new Variable(Math.Pow(args[0].Value, 1.0 / 3.0));
        }
    }
    class MinFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 2, m_name);
            var result = args[0].Value;
            for (int i = 1; i < args.Count; i++)
            {
                if (args[i].Value < result)
                {
                    result = args[i].Value;
                } 
            }
            return new Variable(result);
        }
    }
    class MaxFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 2, m_name);
            var result = args[0].Value;
            for (int i = 1; i < args.Count; i++)
            {
                if (args[i].Value > result)
                {
                    result = args[i].Value;
                }
            }
            return new Variable(result);
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
    class SignFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            Variable arg = args[0];
            arg.Value = Math.Sign(arg.Value);
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

            args[0].Value = Math.Round(args[0].AsDouble(), numberDigits);
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
        bool m_decimal = true;

        public GetRandomFunction(bool isDecimal = false)
        {
            m_decimal = isDecimal;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();

            if (m_decimal)
            {
                return new Variable(m_random.NextDouble());
            }

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
