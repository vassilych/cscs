using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

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

    public interface ICscsDLL
    {
        string DoWork(object load);
    }

    public interface ICustomDLL
    {
        Variable DoWork(
            Interpreter __interpreter,
            List<string> __varStr,
            List<double> __varNum,
            List<int> __varInt,
            List<List<string>> __varArrStr,
            List<List<double>> __varArrNum,
            List<List<int>> __varArrInt,
            List<Dictionary<string, string>> __varMapStr,
            List<Dictionary<string, double>> __varMapNum,
            List<Variable> __varVar);

        bool ArgData(int id, out string name, out Variable.VarType type);
    }
}
