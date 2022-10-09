using System;


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
