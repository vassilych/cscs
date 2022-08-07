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

        int _nextId = 1;

        public IEnumerable<ICscsModule> Modules { get; set; }

        public int NewInterpreter()
        {
            var interpreter = new Interpreter();

            foreach (var module in Modules)
                module.CreateInstance(interpreter);

            var handler = OnInterpreterCreated;
            if (handler != null)
                handler(interpreter, EventArgs.Empty);

            lock (Interpreters)
            {
                int id = _nextId++;
                Interpreters.Add(id, interpreter);
                return id;
            }
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

                CurrentInterpreter = interpreter;
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
                    CurrentInterpreter = Interpreters.FirstOrDefault().Value;
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
                return Interpreters.SingleOrDefault(x => x.Value == interpreter).Key;
        }
    }
}
