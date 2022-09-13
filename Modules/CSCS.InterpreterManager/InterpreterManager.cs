using SplitAndMerge;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CSCS.InterpreterManager
{
    public class InterpreterManager
    {
        public event EventHandler OnInterpreterCreated;

        public Interpreter CurrentInterpreter { get; private set; }

        private Dictionary<int, Interpreter> Interpreters { get; } = new Dictionary<int, Interpreter>();

        static int _nextId = 1;
        public int LastId { get { return _nextId - 1;} }

        public List<ICscsModule> Modules { get; set; }

        public List<Interpreter> AllInterpreters { get { return Interpreters.Values.ToList<Interpreter>(); } }

        public int NewInterpreter()
        {
            Interpreter interpreter;
            lock (Interpreters)
            {
                interpreter = new Interpreter(_nextId++);
                Interpreters.Add(interpreter.Id, interpreter);
            }

            foreach (var module in Modules)
            {
                module.CreateInstance(interpreter);
            }

            var handler = OnInterpreterCreated;
            if (handler != null)
            {
                handler(interpreter, EventArgs.Empty);
            }

            return interpreter.Id;
        }

        public bool RemoveInterpreter(int interpreterHandle)
        {
            lock (Interpreters)
            {
                if (!Interpreters.TryGetValue(interpreterHandle, out Interpreter interpreter))
                    return false;
                if (interpreter == CurrentInterpreter)
                    return false;               // Don't let them remove the current interpreter
                return Interpreters.Remove(interpreterHandle);
            }
        }

        public bool SetInterpreter(int interpreterHandle)
        {
            lock (Interpreters)
            {
                if (!Interpreters.TryGetValue(interpreterHandle, out Interpreter interpreter))
                    return false;

                Interpreter.LastInstance = CurrentInterpreter = interpreter;
            }
            return true;
        }

        public bool SwitchFromAndRemoveInterpreter(Interpreter interpreter)
        {
            lock (Interpreters)
            {
                int handle = GetInterpreterHandle(interpreter);
                if (handle == 0)
                    return false;

                Interpreters.Remove(handle);

                if (interpreter == CurrentInterpreter)
                {
                    Interpreter.LastInstance = CurrentInterpreter = Interpreters.FirstOrDefault().Value;
                }
            }
            return true;
        }

        public void TerminateModules()
        {
            foreach (var module in Modules)
                module.Terminate();
        }

        public int GetInterpreterHandle(Interpreter interpreter)
        {
            lock (Interpreters)
            {
                var id = Interpreters.SingleOrDefault(x => x.Value == interpreter).Key;
                return id;
            }
        }
        public Interpreter GetInterpreter(int handle)
        {
            lock (Interpreters)
            {
                var interp = Interpreters.SingleOrDefault(x => x.Key == handle).Value;
                return interp;
            }
        }

        public void AddModule(ICscsModule module, Interpreter interpreter)
        {
            Modules.Add(module);
            module.CreateInstance(interpreter);
        }
    }
}
