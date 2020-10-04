using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SplitAndMerge;

namespace cscs.Tests
{
    public class BaseCscsFixture
    {
        protected static StringBuilder outputBuffer = new StringBuilder();
        protected static void BufferOutput(object sender, OutputAvailableEventArgs e) => outputBuffer.Append(e.Output);

        public static Variable RunScript(string script) => Process(script);
        
        protected static Variable Process(string script)
        {
            try
            {
                return Interpreter.Instance.Process(script);
            }
            catch (ParsingException ex)
            {
                outputBuffer.Append("\n/****************************************************************************************************\\\nCSCS Parsing Exception : ");
                outputBuffer.Append(ex.Message + "\n");
                string[] stack = ex.ExceptionStack.Replace("\r", "").Split('\n');
                if (stack.Length > 2 && !string.IsNullOrWhiteSpace(stack[1]))
                {
                    outputBuffer.Append("in file " + stack[1].Trim() + " ");
                }

                if (stack.Length > 1 && !string.IsNullOrWhiteSpace(stack[0]))
                {
                    outputBuffer.Append("on line " + stack[0].Trim() + " ");
                }

                if (stack.Length > 2 && !string.IsNullOrWhiteSpace(stack[2]))
                {
                    outputBuffer.Append(":\n" + stack[2].Trim());
                }


                outputBuffer.Append("\n\\****************************************************************************************************/\n");
            }
            return new Variable();
        }

        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext ctx)
        {
            Interpreter.Instance.InitStandalone();
            Interpreter.Instance.OnOutput += BufferOutput;
        }


    }
}
