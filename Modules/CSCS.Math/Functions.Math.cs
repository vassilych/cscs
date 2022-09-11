using System;
using System.Collections.Generic;
using System.Linq;
using static System.Math;
using System.Threading.Tasks;

using SplitAndMerge;

namespace CSCSMath
{
    public class CscsMathModule : ICscsModule
    {
        public ICscsModuleInstance CreateInstance(Interpreter interpreter)
        {
            return new CscsMathModuleInstance(interpreter);
        }

        public void Terminate()
        {
        }
    }

    public class CscsMathModuleInstance : ICscsModuleInstance
    {
        public CscsMathModuleInstance(Interpreter interpreter)
        {
            interpreter.RegisterFunction(Constants.MATH_ABS, new AbsFunction());
            interpreter.RegisterFunction(Constants.MATH_ACOS, new AcosFunction());
            interpreter.RegisterFunction(Constants.MATH_ACOSH, new AcoshFunction());
            interpreter.RegisterFunction(Constants.MATH_ASIN, new AsinFunction());
            interpreter.RegisterFunction(Constants.MATH_ASINH, new AsinhFunction());
            interpreter.RegisterFunction(Constants.MATH_ATAN, new TanFunction());
            interpreter.RegisterFunction(Constants.MATH_ATAN2, new Atan2Function());
            interpreter.RegisterFunction(Constants.MATH_ATANH, new AtanhFunction());
            interpreter.RegisterFunction(Constants.MATH_CBRT, new CbrtFunction());
            interpreter.RegisterFunction(Constants.MATH_CEIL, new CeilFunction());
            interpreter.RegisterFunction(Constants.MATH_COS, new CosFunction());
            interpreter.RegisterFunction(Constants.MATH_COSH, new CoshFunction());
            interpreter.RegisterFunction(Constants.MATH_E, new EFunction());
            interpreter.RegisterFunction(Constants.MATH_EXP, new ExpFunction());
            interpreter.RegisterFunction(Constants.MATH_FLOOR, new FloorFunction());
            interpreter.RegisterFunction(Constants.MATH_INFINITY, new InfinityFunction());
            interpreter.RegisterFunction(Constants.MATH_ISFINITE, new IsFiniteFunction());
            interpreter.RegisterFunction(Constants.MATH_ISNAN, new IsNaNFunction());
            interpreter.RegisterFunction(Constants.MATH_LN2, new Ln2Function());
            interpreter.RegisterFunction(Constants.MATH_LN10, new Ln10Function());
            interpreter.RegisterFunction(Constants.MATH_LOG, new LogFunction());
            interpreter.RegisterFunction(Constants.MATH_LOG2E, new Log2EFunction());
            interpreter.RegisterFunction(Constants.MATH_LOG10E, new Log10EFunction());
            interpreter.RegisterFunction(Constants.MATH_MIN, new MinFunction());
            interpreter.RegisterFunction(Constants.MATH_MAX, new MaxFunction());
            interpreter.RegisterFunction(Constants.MATH_NEG_INFINITY, new NegInfinityFunction());
            interpreter.RegisterFunction(Constants.MATH_PI, new PiFunction());
            interpreter.RegisterFunction(Constants.MATH_POW, new PowFunction());
            interpreter.RegisterFunction(Constants.MATH_RANDOM, new GetRandomFunction(true));
            interpreter.RegisterFunction(Constants.MATH_ROUND, new RoundFunction());
            interpreter.RegisterFunction(Constants.MATH_SQRT, new SqrtFunction());
            interpreter.RegisterFunction(Constants.MATH_SQRT1_2, new Sqrt1_2Function());
            interpreter.RegisterFunction(Constants.MATH_SQRT2, new Sqrt2Function());
            interpreter.RegisterFunction(Constants.MATH_SIGN, new SignFunction());
            interpreter.RegisterFunction(Constants.MATH_SIN, new SinFunction());
            interpreter.RegisterFunction(Constants.MATH_SINH, new SinhFunction());
            interpreter.RegisterFunction(Constants.MATH_TAN, new TanFunction());
            interpreter.RegisterFunction(Constants.MATH_TANH, new TanhFunction());
            interpreter.RegisterFunction(Constants.MATH_TRUNC, new FloorFunction());
        }
    }


    class PiFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(System.Math.PI);
        }
        public override string Description()
        {
            return "Returns the number Pi (3.14159265358...)";
        }
    }
    class EFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(Math.E);
        }
        public override string Description()
        {
            return "Returns the number e (2.718281828...)";
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

    class Sqrt2Function : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(Math.Sqrt(2));
        }
        public override string Description()
        {
            return "Returns the squared root of 2.";
        }
    }
    class Sqrt1_2Function : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(Math.Sqrt(1 / 2));
        }
        public override string Description()
        {
            return "Returns the squared root of 1/2.";
        }
    }
    class Ln2Function : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(Math.Log(2));
        }
        public override string Description()
        {
            return "Returns the natural logarithm of 2.";
        }
    }
    class Ln10Function : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(Math.Log(10));
        }
        public override string Description()
        {
            return "Returns the natural logarithm of 10.";
        }
    }
    class Log2EFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(Math.Log(Math.E, 2));
        }
        public override string Description()
        {
            return "Returns the logarithm of e using base 2.";
        }
    }
    class Log10EFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(Math.Log10(Math.E));
        }
        public override string Description()
        {
            return "Returns the logarithm of e using base 10.";
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
        public override string Description()
        {
            return "Returns e raised to the specified power.";
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
        public override string Description()
        {
            return "Returns a specified number raised to the specified power.";
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
        public override string Description()
        {
            return "Returns the sine of the specified angle.";
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
        public override string Description()
        {
            return "Returns the cosine of the specified angle.";
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
        public override string Description()
        {
            return "Returns the tangent of the specified angle.";
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
        public override string Description()
        {
            return "Returns the hyperbolic sine of the specified angle.";
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
        public override string Description()
        {
            return "Returns the hyperbolic cosine of the specified angle.";
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
        public override string Description()
        {
            return "Returns the hyperbolic tangent of the specified angle.";
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
        public override string Description()
        {
            return "Returns the angle whose sine is the specified number.";
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
        public override string Description()
        {
            return "Returns the angle whose cosine is the specified number.";
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
        public override string Description()
        {
            return "Returns the angle whose tangent is the specified number.";
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
        public override string Description()
        {
            return "Returns the angle whose tangent is the quotient of two specified numbers.";
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
        public override string Description()
        {
            return "Returns the angle whose hyperbolic sine is the specified number.";
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
        public override string Description()
        {
            return "Returns the angle whose hyperbolic cosine is the specified number.";
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
        public override string Description()
        {
            return "Returns the angle whose hyperbolic tangent is the specified number.";
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
        public override string Description()
        {
            return "Returns the square root of a specified number.";
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
        public override string Description()
        {
            return "Returns the cube root of a specified number.";
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
        public override string Description()
        {
            return "Returns the smaller of two numbers.";
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
        public override string Description()
        {
            return "Returns the larger of two numbers.";
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
        public override string Description()
        {
            return "Returns the absolute value of a specified number.";
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
        public override string Description()
        {
            return "Returns an integer that indicates the sign of a number.";
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
        public override string Description()
        {
            return "Returns the smallest integral value greater than or equal to the specified number.";
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
        public override string Description()
        {
            return "Returns the largest integral value less than or equal to the specified number.";
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
        public override string Description()
        {
            return "Rounds a value to the nearest integer or to the specified number of fractional digits.";
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
        public override string Description()
        {
            return "Returns the natural (base e) logarithm of a specified number.";
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
        public override string Description()
        {
            return "Returns a random number between 0 and 1.";
        }
    }

}
