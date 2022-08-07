using System;
using System.Collections.Generic;
using System.Text;

namespace SplitAndMerge
{
    public interface ICscsModule
    {
        ICscsModuleInstance CreateInstance(Interpreter interpreter);
        void Terminate();
    }

    public interface ICscsModuleInstance
    {
    }

}
