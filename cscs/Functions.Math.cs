using System;
using System.Collections.Generic;
using System.Linq;

namespace SplitAndMerge
{
  class PiFunction : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      return new Variable(Math.PI);
    }
  }

  class ExpFunction : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      Variable result = script.ExecuteTo(Constants.END_ARG);
      result.Value = Math.Exp(result.Value);
      return result;
    }
  }

  class PowFunction : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      Variable arg1 = script.ExecuteTo(Constants.NEXT_ARG);
      script.Forward(); // eat separation
      Variable arg2 = script.ExecuteTo(Constants.END_ARG);

      arg1.Value = Math.Pow(arg1.Value, arg2.Value);
      return arg1;
    }
  }

  class SinFunction : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      Variable arg = script.ExecuteTo(Constants.END_ARG);
      arg.Value = Math.Sin(arg.Value);
      return arg;
    }
  }

  class CosFunction : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      Variable arg = script.ExecuteTo(Constants.END_ARG);
      arg.Value = Math.Cos(arg.Value);
      return arg;
    }
  }

  class AsinFunction : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      Variable arg = script.ExecuteTo(Constants.END_ARG);
      arg.Value = Math.Asin(arg.Value);
      return arg;
    }
  }

  class AcosFunction : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      Variable arg = script.ExecuteTo(Constants.END_ARG);
      arg.Value = Math.Acos(arg.Value);
      return arg;
    }
  }

  class SqrtFunction : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      Variable arg = script.ExecuteTo(Constants.END_ARG);
      arg.Value = Math.Sqrt(arg.Value);
      return arg;
    }
  }

  class AbsFunction : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      Variable arg = script.ExecuteTo(Constants.END_ARG);
      arg.Value = Math.Abs(arg.Value);
      return arg;
    }
  }

  class CeilFunction : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      Variable arg = script.ExecuteTo(Constants.END_ARG);
      arg.Value = Math.Ceiling(arg.Value);
      return arg;
    }
  }

  class FloorFunction : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      Variable arg = script.ExecuteTo(Constants.END_ARG);
      arg.Value = Math.Floor(arg.Value);
      return arg;
    }
  }

  class RoundFunction : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      bool isList = false;
      List<Variable> args = Utils.GetArgs(script,
                            Constants.START_ARG, Constants.END_ARG, out isList);
      Utils.CheckArgs(args.Count, 1, m_name);
      int numberDigits = Utils.GetSafeInt(args, 1, 0);

      args[0].Value = Math.Round(args[0].Value, numberDigits);
      return args[0];
    }
  }

  class LogFunction : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      Variable arg = script.ExecuteTo(Constants.END_ARG);
      arg.Value = Math.Log(arg.Value);
      return arg;
    }
  }
  class GetRandomFunction : ParserFunction
  {
    static Random m_random = new Random();

    protected override Variable Evaluate(ParsingScript script)
    {
      bool isList = false;
      List<Variable> args = Utils.GetArgs(script,
                            Constants.START_ARG, Constants.END_ARG, out isList);
      Utils.CheckArgs(args.Count, 1, m_name);
      int limit = args[0].AsInt();
      Utils.CheckPosInt(args[0]);
      int numberRandoms = Utils.GetSafeInt(args, 1, 1);

      if (numberRandoms <= 1) {
        return new Variable(m_random.Next(0, limit));
      }

      List<int> available = Enumerable.Range(0, limit).ToList();
      List<Variable> result = new List<Variable>();

      for (int i = 0; i < numberRandoms && available.Count > 0; i++) {
        int nextRandom = m_random.Next(0, available.Count);
        result.Add(new Variable(available[nextRandom]));
        available.RemoveAt(nextRandom);
      }

      return new Variable(result);
    }
  }

}
