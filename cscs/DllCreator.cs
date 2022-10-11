using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SplitAndMerge
{
#if __ANDROID__ == false && __IOS__ == false

    using WorkFunc = Func<Interpreter, List<string>, List<double>, List<int>,
            List<List<string>>, List<List<double>>, List<List<int>>,
            List<Dictionary<string, string>>, List<Dictionary<string, double>>, List<Variable>, Variable>;
    using GetArgsFunc = Func<int, ArgData>;

    public class DLLConst
    {
        public const int MaxWorkMethods = 10;
        public const string WorkerName = "DoWork";
        public const string GetArgsName = "GetArgData";
        public const string ClassName = "CustomPrecompiler";
        public const string ClassHeader = "  public class " + ClassName + " : ICustomDLL {";

        public static string GetWorkerMethod(int id = 1)
        {
            return WorkerName + id;
        }
        public static string GetArgsMethod(int id)
        {
            return GetArgsName + id;
        }
    }

    public class ArgData
    {
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

    public class DLLCreator : ParserFunction
    {
        bool m_scriptInCSharp = false;

        public DLLCreator(bool scriptInCSharp)
        {
            m_scriptInCSharp = scriptInCSharp;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            var precompiler = Precompiler.ImplementCustomDLL(script, m_scriptInCSharp, true);
            return new Variable(precompiler.OutputDLL);
        }
    }

    public class DLLFunction : ParserFunction
    {
        ImportDLLFunction.DLLData m_dllData;
        string m_funcName;

        public DLLFunction(ImportDLLFunction.DLLData dllData, string funcName)
        {
            m_dllData = dllData;
            m_funcName = funcName;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            var args = script.GetFunctionArgs();
            var result = ImportDLLFunction.Execute(m_dllData, m_funcName, script, args);
            return result;
        }
    }

    public class ImportDLLFunction : ParserFunction
    {
        public class DLLData
        {
            public string name;
            public Dictionary<string, DLLFunctionData> functionMap;
            public ICustomDLL dll;
        }
        public class DLLFunctionData
        {
            public string name;
            public string[] args;
            public Variable[] defArgs;
            public Dictionary<string, Variable> argsMap;
            public WorkFunc workMethod;
        }

        bool m_executeMode;

        static List<DLLData> s_dlls = new List<DLLData>();
        static Dictionary<string, DLLData> s_func2dll = new Dictionary<string, DLLData>();

        public ImportDLLFunction(bool executeMode = false)
        {
            m_executeMode = executeMode;
        }

        public static DLLFunction GetDllFunction(string funcName)
        {
            if (!s_func2dll.TryGetValue(funcName, out DLLData dllData))
            {
                return null;
            }
            return new DLLFunction(dllData, funcName);
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            var args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);

            if (m_executeMode)
            {
                var result = ExecuteCustom(script, args);
                return result;
            }

            var name = Utils.GetSafeString(args, 0);

            var DLL = LoadDLL(name, script);
            var loaded = LoadCustom(DLL, script);
            if (loaded == null)
            {
                Utils.ThrowErrorMsg("Couldn´t add dll: " + name, script, m_name);
            }
            return loaded;
        }

        Variable ExecuteCustom(ParsingScript script, List<Variable> args)
        {
            var handle = Utils.GetSafeInt(args, 0);
            var funcName = Utils.GetSafeString(args, 1);
            if (handle < 0 || handle >= s_dlls.Count)
            {
                Utils.ThrowErrorMsg("Couldn´t find handle: " + handle, script, m_name);
            }

            args.RemoveAt(0); // dll handle
            args.RemoveAt(0); // function name

            var dll = s_dlls[handle];
            return Execute(dll, funcName, script, args);
        }

        public static Variable Execute(DLLData dll, string funcName, ParsingScript script, List<Variable> args)
        {
            Type type = dll.GetType();
            var module = Activator.CreateInstance(type) as SplitAndMerge.ICustomDLL;

            if (!dll.functionMap.TryGetValue(funcName.ToLower(), out DLLFunctionData dllFuncData))
            {
                Utils.ThrowErrorMsg("Couldn't find function: " + funcName, script, dll.name);
            }

            CustomCompiledFunction.PrepareArgs(args, dllFuncData.args, dllFuncData.defArgs, dllFuncData.argsMap,
                out List<string> argsStr, out List<double> argsNum, out List<int> argsInt,
                out List<List<string>> argsArrStr, out List<List<double>> argsArrNum, out List<List<int>> argsArrInt,
                out List<Dictionary<string, string>> argsMapStr, out List<Dictionary<string, double>> argsMapNum, out List<Variable> argsVar);

            var result = dllFuncData.workMethod(script.InterpreterInstance, argsStr, argsNum, argsInt,
                argsArrStr, argsArrNum, argsArrInt, argsMapStr, argsMapNum, argsVar);
            return result;
        }

        static Variable LoadCustom(Assembly DLL, ParsingScript script = null)
        {
            var types = DLL.GetExportedTypes();
            var data = new DLLData();
            data.name = Path.GetFileNameWithoutExtension(DLL.FullName);
            foreach (var type in types)
            {
                var needed = typeof(SplitAndMerge.ICustomDLL).IsAssignableFrom(type);
                if (!needed)
                {
                    continue;
                }

                var module = Activator.CreateInstance(type) as SplitAndMerge.ICustomDLL;
                if (module == null)
                {
                    Utils.ThrowErrorMsg("Couldn´t load dll: " + DLL.FullName, script, DLL.GetName().Name);
                }

                Precompiler.ExtractArgsFromDLL(module, ref data);
                var names = module.MethodNames();
                foreach (var name in names)
                {
                    s_func2dll[name.ToLower()] = data;
                }

                data.dll = module;
                s_dlls.Add(data);
                return new Variable(s_dlls.Count - 1);
            }
            return null;
        }

        public static Assembly LoadDLL(string name, ParsingScript script = null)
        {
            if (!name.ToLower().EndsWith(".dll"))
            {
                name += ".dll";
            }

            var absolute = Path.IsPathRooted(name);
            var filename = name;
            if (!absolute)
            {
                var pwd = Directory.GetCurrentDirectory();
                var baseDir = Directory.GetParent(pwd);
                for (int i = 0; i < 3 && baseDir != null; i++)
                {
                    var files = Directory.EnumerateFiles(baseDir.FullName, name, SearchOption.AllDirectories).ToList<string>();
                    if (files.Count > 0)
                    {
                        filename = files[0];
                        break;
                    }
                    baseDir = Directory.GetParent(baseDir.FullName);
                }
            }

            if (!File.Exists(filename))
            {
                Utils.ThrowErrorMsg("Couldn´t find DLL: " + filename + ", current dir: " +
                    Directory.GetCurrentDirectory(), script, name);
            }

            Assembly DLL = null;
            try
            {
                DLL = Assembly.LoadFile(filename);
            }
            catch (Exception exc)
            {
                Utils.ThrowErrorMsg("Couldn´t load DLL: " + filename + ", current dir: " +
                    Directory.GetCurrentDirectory() + ":" + exc.Message, script, name);
            }

            return DLL;
        }
    }
#endif
}
