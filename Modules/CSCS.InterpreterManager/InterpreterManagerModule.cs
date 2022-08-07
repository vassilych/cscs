using SplitAndMerge;
using System;
using System.Collections.Generic;
using System.Text;

namespace CSCS.InterpreterManager
{
    public class InterpreterManagerModule : InterpreterManager, ICscsModule
    {
        public ICscsModuleInstance CreateInstance(Interpreter interpreter)
        {
            return new InterpreterManagerInstance(this, interpreter);
        }

        public void Terminate()
        {
        }
    }
}
