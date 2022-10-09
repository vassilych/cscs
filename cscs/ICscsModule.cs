using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;


namespace SplitAndMerge
{
    using WorkFunc = Func<Interpreter, List<string>, List<double>, List<int>,
            List<List<string>>, List<List<double>>, List<List<int>>,
            List<Dictionary<string, string>>, List<Dictionary<string, double>>, List<Variable>, Variable>;
    using GetArgsFunc = Func<int, ArgData>;

    public interface ICscsModule
    {
        ICscsModuleInstance CreateInstance(Interpreter interpreter);
        void Terminate();
    }

    public interface ICscsModuleInstance
    {
    }

    public class ArgData
    {
        public const int MaxWorkMethods = 10;
        public string name = "";
        public Variable.VarType type = Variable.VarType.NONE;
        public Variable defValue = Variable.EmptyInstance;
        public bool exists = false;
    }

    public interface ICustomDLL
    {
        int NumberWorkMethods();
        List<string> MethodNames();

        WorkFunc GetWorkFunction(string name);

        GetArgsFunc GetArgFunction(string name);

        Variable DoWork1(
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
        ArgData GetArgData1(int id);

        Variable DoWork2(
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
        ArgData GetArgData2(int id);

        Variable DoWork3(
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
        ArgData GetArgData3(int id);

        Variable DoWork4(
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
        ArgData GetArgData4(int id);

        Variable DoWork5(
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
        ArgData GetArgData5(int id);

        Variable DoWork6(
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
        ArgData GetArgData6(int id);

        Variable DoWork7(
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
        ArgData GetArgData7(int id);

        Variable DoWork8(
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
        ArgData GetArgData8(int id);

        Variable DoWork9(
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
        ArgData GetArgData9(int id);

        Variable DoWork10(
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
        ArgData GetArgData10(int id);
    }
}
