using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitAndMerge;

namespace cscs.Tests
{
    public class BaseCscsFixture
    {
        protected static StringBuilder OutputBuffer = new StringBuilder();
        protected static bool Initialized;
        
        protected static Variable Process(string script)
        {
            try
            {
                if (!Initialized)
                {
                    Interpreter.LastInstance.InitStandalone();
                    // The Math fixtures exercise abs/sin/cos/... which live in the
                    // CSCS.Math module, not in the core interpreter.
                    new CSCSMath.CscsMathModule().CreateInstance(Interpreter.LastInstance);
                    Interpreter.LastInstance.OnOutput += (o, args) => OutputBuffer.Append(args.Output);
                    Initialized = true;
                }

                return Interpreter.LastInstance.Process(script);
            }
            catch (ParsingException ex)
            {
                OutputBuffer.Append("\n/****************************************************************************************************\\\nCSCS Parsing Exception : ");
                OutputBuffer.Append(ex.Message + "\n");
                string[] stack = ex.ExceptionStack.Replace("\r", "").Split('\n');
                if (stack.Length > 2 && !string.IsNullOrWhiteSpace(stack[1]))
                {
                    OutputBuffer.Append("in file " + stack[1].Trim() + " ");
                }

                if (stack.Length > 1 && !string.IsNullOrWhiteSpace(stack[0]))
                {
                    OutputBuffer.Append("on line " + stack[0].Trim() + " ");
                }

                if (stack.Length > 2 && !string.IsNullOrWhiteSpace(stack[2]))
                {
                    OutputBuffer.Append(":\n" + stack[2].Trim());
                }


                OutputBuffer.Append("\n\\****************************************************************************************************/\n");
            }
            return new Variable();
        }

    }
}
