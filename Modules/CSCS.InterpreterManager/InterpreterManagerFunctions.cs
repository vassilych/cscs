using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
            if (!name.ToLower().EndsWith(".dll"))
            {
                name += ".dll";
            }

            var absolute = Path.IsPathRooted(name);
            var filename = name;
            if (!absolute)
            {
                var pwd = Directory.GetCurrentDirectory();
                var baseDir = Path.GetFullPath(Path.Combine(pwd, "..", "..", ".."));
                var files = Directory.EnumerateFiles(baseDir, name, SearchOption.AllDirectories).ToList<string>();
                if (files.Count > 0)
                {
                    filename = files[0];
                }
            }

            if (!File.Exists(filename))
            {
                Utils.ThrowErrorMsg("Couldn´t find DLL: " + name + ", current dir: " +
                    Directory.GetCurrentDirectory(), script, m_name);
            }

            bool added = false;
            var DLL = Assembly.LoadFile(filename);

            var types = DLL.GetExportedTypes();
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

            return Variable.EmptyInstance;
        }
    }


}