using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using SplitAndMerge;

namespace CSCS.InterpreterManager
{
    internal class NewInterpreterFunction : ParserFunction
    {
        private InterpreterManager _mgr;
        private ParsingScript _script;

        public NewInterpreterFunction(InterpreterManager mgr)
        {
            _mgr = mgr;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            var args = script.GetFunctionArgs();

            var load = Utils.GetSafeString(args, 0);
            int newHandle = _mgr.NewInterpreter();

            if (args.Count > 0)
            {
                _mgr.SetInterpreter(newHandle);
                script.SetInterpreter(_mgr.CurrentInterpreter);
            }
            if (string.IsNullOrWhiteSpace(load))
            {
                return new Variable(newHandle);
            }

            bool newThread = Utils.GetSafeInt(args, 1) > 0;

            InterpreterInstance = script.InterpreterInstance;
            _script = script;
            if (!newThread)
            {
                IncludeScriptLocal(load);
            }
            else
            {
                ThreadPool.QueueUserWorkItem(IncludeScriptLocal, load);
            }

            return new Variable(newHandle);
        }

        void IncludeScriptLocal(Object stateInfo)
        {
            string filename = (string)stateInfo;
            IncludeFile.Execute(filename, InterpreterInstance, _script);
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

            int newId = Utils.GetSafeInt(args, 0);
            bool changed = _mgr.SetInterpreter(newId);

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
            script.GetFunctionArgs();
            var handle = _mgr.GetInterpreterHandle(InterpreterInstance);
            return new Variable(handle);
        }
    }

    internal class GetLastHandleFunction : ParserFunction
    {
        private InterpreterManager _mgr;

        public GetLastHandleFunction(InterpreterManager mgr)
        {
            _mgr = mgr;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            script.GetFunctionArgs();
            return new Variable(_mgr.LastId);
        }
    }

    internal class ResetAllVariablesFunction : ParserFunction
    {
        private InterpreterManager _mgr;

        public ResetAllVariablesFunction(InterpreterManager mgr)
        {
            _mgr = mgr;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            var interpreters = _mgr.AllInterpreters;
            foreach(var interpreter in interpreters)
            {
                interpreter.CleanUpVariables();
            }
            return Variable.EmptyInstance;
        }
    }

    internal class ImportModuleFunction : ParserFunction
    {
        private InterpreterManager _mgr;

        public ImportModuleFunction(InterpreterManager mgr)
        {
            _mgr = mgr;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            var args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);
            var name = Utils.GetSafeString(args, 0);

            var DLL = ImportDLLFunction.LoadDLL(name, script);
            var types = DLL.GetExportedTypes();

            bool added = false;
            foreach (var type in types)
            {
                //var c = Activator.CreateInstance(type);
                var needed = typeof(ICscsModule).IsAssignableFrom(type);
                if (!needed)
                {
                    continue;
                }
                var module = Activator.CreateInstance(type) as ICscsModule;
                if (module != null)
                {
                    _mgr.AddModule(module, InterpreterInstance);
                    added = true;
                    break;
                }
            }
            if (!added)
            {
                Utils.ThrowErrorMsg("Couldn´t add module: " + name,
                                     script, m_name);
            }

            return new Variable(DLL.Location);
        }
    }

}