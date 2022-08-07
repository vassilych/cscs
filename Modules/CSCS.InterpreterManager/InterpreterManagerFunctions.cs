using SplitAndMerge;

namespace CSCS.InterpreterManager
{
    internal class NewInterpreterFunction : ParserFunction
    {
        private InterpreterManager _mgr;

        public NewInterpreterFunction(InterpreterManager mgr)
        {
            _mgr = mgr;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(_mgr.NewInterpreter());
        }
    }

    internal class RemoveInterpreterFunction : ParserFunction
    {
        private InterpreterManager _mgr;

        public RemoveInterpreterFunction(InterpreterManager mgr)
        {
            _mgr = mgr;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            var args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            return new Variable(_mgr.RemoveInterpreter(Utils.GetSafeInt(args, 0)));
        }
    }

    internal class SetInterpreterFunction : ParserFunction
    {
        private InterpreterManager _mgr;

        public SetInterpreterFunction(InterpreterManager mgr)
        {
            _mgr = mgr;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            var args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);

            bool changed = _mgr.SetInterpreter(Utils.GetSafeInt(args, 0));

            // Let's try to change the interpreter of the script.
            // This might not work, but it's better than not changing it, I think.
            if (changed)
                script.SetInterpreter(_mgr.CurrentInterpreter);
            return new Variable(changed);
        }
    }

    internal class GetInterpreterHandleFunction : ParserFunction
    {
        private InterpreterManager _mgr;

        public GetInterpreterHandleFunction(InterpreterManager mgr)
        {
            _mgr = mgr;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(_mgr.GetInterpreterHandle(InterpreterInstance));
        }
    }


}