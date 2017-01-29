using System;

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
      Variable arg = script.ExecuteTo(Constants.END_ARG);
      arg.Value = Math.Round(arg.Value);
      return arg;
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

}
