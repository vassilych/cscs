using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using SplitAndMerge;


namespace CSCS.Tests
{
    public class CscsDLL : ICustomDLL
    {
        public Variable DoWork1
            (Interpreter __interpreter,
             List<string> __varStr,
             List<double> __varNum,
             List<int> __varInt,
             List<List<string>> __varArrStr,
             List<List<double>> __varArrNum,
             List<List<int>> __varArrInt,
             List<Dictionary<string, string>> __varMapStr,
             List<Dictionary<string, double>> __varMapNum,
             List<Variable> __varVar)
        {
            return new Variable("OK");
        }
        public ArgData GetArgData1(int id)
        {
            ArgData arg = new ArgData();
            if (id == 0)
            {
                arg.exists = true;
                arg.name = "X";
                arg.type = Variable.VarType.STRING;
                arg.defValue = new Variable(arg.type);
                arg.defValue.String = null;
            }
            return arg;
        }
        public Variable DoWork2
            (Interpreter __interpreter,
             List<string> __varStr,
             List<double> __varNum,
             List<int> __varInt,
             List<List<string>> __varArrStr,
             List<List<double>> __varArrNum,
             List<List<int>> __varArrInt,
             List<Dictionary<string, string>> __varMapStr,
             List<Dictionary<string, double>> __varMapNum,
             List<Variable> __varVar)
        {
            return new Variable("OK");
        }
        public ArgData GetArgData2(int id)
        {
            ArgData arg = new ArgData();
            return arg;
        }
        public Variable DoWork3(Interpreter __interpreter, List<string> __varStr, List<double> __varNum, List<int> __varInt,
             List<List<string>> __varArrStr, List<List<double>> __varArrNum, List<List<int>> __varArrInt,
             List<Dictionary<string, string>> __varMapStr, List<Dictionary<string, double>> __varMapNum, List<Variable> __varVar) {
            return new Variable("OK");
        }
        public ArgData GetArgData3(int id)
        {
            return null;
        }
        public Variable DoWork4(Interpreter __interpreter, List<string> __varStr, List<double> __varNum, List<int> __varInt,
             List<List<string>> __varArrStr, List<List<double>> __varArrNum, List<List<int>> __varArrInt,
             List<Dictionary<string, string>> __varMapStr, List<Dictionary<string, double>> __varMapNum, List<Variable> __varVar)
        {
            return new Variable("OK");
        }
        public ArgData GetArgData4(int id)
        {
            return null;
        }
        public Variable DoWork5(Interpreter __interpreter, List<string> __varStr, List<double> __varNum, List<int> __varInt,
     List<List<string>> __varArrStr, List<List<double>> __varArrNum, List<List<int>> __varArrInt,
     List<Dictionary<string, string>> __varMapStr, List<Dictionary<string, double>> __varMapNum, List<Variable> __varVar)
        {
            return new Variable("OK");
        }
        public ArgData GetArgData5(int id)
        {
            return null;
        }
        public Variable DoWork6(Interpreter __interpreter, List<string> __varStr, List<double> __varNum, List<int> __varInt,
     List<List<string>> __varArrStr, List<List<double>> __varArrNum, List<List<int>> __varArrInt,
     List<Dictionary<string, string>> __varMapStr, List<Dictionary<string, double>> __varMapNum, List<Variable> __varVar)
        {
            return new Variable("OK");
        }
        public ArgData GetArgData6(int id)
        {
            return null;
        }
        public Variable DoWork7(Interpreter __interpreter, List<string> __varStr, List<double> __varNum, List<int> __varInt,
     List<List<string>> __varArrStr, List<List<double>> __varArrNum, List<List<int>> __varArrInt,
     List<Dictionary<string, string>> __varMapStr, List<Dictionary<string, double>> __varMapNum, List<Variable> __varVar)
        {
            return new Variable("OK");
        }
        public ArgData GetArgData7(int id)
        {
            return null;
        }
        public Variable DoWork8(Interpreter __interpreter, List<string> __varStr, List<double> __varNum, List<int> __varInt,
     List<List<string>> __varArrStr, List<List<double>> __varArrNum, List<List<int>> __varArrInt,
     List<Dictionary<string, string>> __varMapStr, List<Dictionary<string, double>> __varMapNum, List<Variable> __varVar)
        {
            return new Variable("OK");
        }
        public ArgData GetArgData8(int id)
        {
            return null;
        }
        public Variable DoWork9(Interpreter __interpreter, List<string> __varStr, List<double> __varNum, List<int> __varInt,
     List<List<string>> __varArrStr, List<List<double>> __varArrNum, List<List<int>> __varArrInt,
     List<Dictionary<string, string>> __varMapStr, List<Dictionary<string, double>> __varMapNum, List<Variable> __varVar)
        {
            return new Variable("OK");
        }
        public ArgData GetArgData9(int id)
        {
            return null;
        }
        public Variable DoWork10(Interpreter __interpreter, List<string> __varStr, List<double> __varNum, List<int> __varInt,
     List<List<string>> __varArrStr, List<List<double>> __varArrNum, List<List<int>> __varArrInt,
     List<Dictionary<string, string>> __varMapStr, List<Dictionary<string, double>> __varMapNum, List<Variable> __varVar)
        {
            return new Variable("OK");
        }
        public ArgData GetArgData10(int id)
        {
            return null;
        }

        public Func<Interpreter, List<string>, List<double>, List<int>,
            List<List<string>>, List<List<double>>, List<List<int>>,
            List<Dictionary<string, string>>, List<Dictionary<string, double>>, List<Variable>, Variable> GetWorkFunction(string name)
        {
            if (name == "a")
            {
                return DoWork1;
            }
            if (name == "b")
            {
                return DoWork2;
            }
            return null;
        }

        public Func<int, ArgData> GetArgFunction(string name)
        {
            if (name == "a")
            {
                return this.GetArgData1;
            }
            if (name == "b")
            {
                return this.GetArgData2;
            }
            return null;
        }

        public int NumberWorkMethods()
        {
            return 2;
        }
        public List<string> MethodNames()
        {
            var res = new List<string>();
            res.Add("A");
            return res;
        }
    }

    public class CscsTestModule : ICscsModule
    {
        public ICscsModuleInstance CreateInstance(Interpreter interpreter)
        {
            return new CscsTestModuleInstance(interpreter);
        }

        public void Terminate()
        {
        }
    }

    public class CscsTestModuleInstance : ICscsModuleInstance
    {
        public CscsTestModuleInstance(Interpreter interpreter)
        {
            TestScriptObject.RegisterTests(interpreter);
        }
    }

    public class TestScriptObject : ScriptObject
    {
        static List<string> s_properties = new List<string> {
            "Name", "Color", "Translate"
        };

        public TestScriptObject(string name = "", string color = "")
        {
            m_name = name;
            m_color = color;
        }

        string m_name;
        string m_color;

        public static void RegisterTests(Interpreter interpreter)
        {
            interpreter.RegisterClass("CompiledTest", new TestCompiledClass());
            interpreter.RegisterClass("CompiledTestAsync", new TestCompiledClassAsync());

            interpreter.RegisterFunction("TestObject",
                new GetVarFunction(new Variable(new TestScriptObject())), true);
            interpreter.RegisterFunction("GetTestObj",
                new GetVarFunction(new Variable(new TestObj())), true);
        }

        public virtual List<string> GetProperties()
        {
            return s_properties;
        }

        public Variable GetNameProperty()
        {
            return new Variable(m_name);
        }
        public Variable GetColorProperty()
        {
            return new Variable(m_color);
        }

        public virtual Task<Variable> GetProperty(string sPropertyName, List<Variable> args = null, ParsingScript script = null)
        {
            sPropertyName = Variable.GetActualPropertyName(sPropertyName, GetProperties());
            switch (sPropertyName)
            {
                case "Name": return Task.FromResult(GetNameProperty());
                case "Color": return Task.FromResult(GetColorProperty());
                case "Translate":
                    return Task.FromResult(
                        args != null && args.Count > 0 ?
                    Translate(args[0]) : Variable.EmptyInstance);
                default:
                    return Task.FromResult(Variable.EmptyInstance);
            }
        }

        public Task<Variable> SetNameProperty(string sValue)
        {
            m_name = sValue;
            return Task.FromResult(Variable.EmptyInstance);
        }

        public Variable SetColorProperty(string aColor)
        {
            m_color = aColor;
            return Variable.EmptyInstance;
        }

        public virtual async Task<Variable> SetProperty(string sPropertyName, Variable argValue)
        {
            sPropertyName = Variable.GetActualPropertyName(sPropertyName, GetProperties());
            switch (sPropertyName)
            {
                case "Name": return await SetNameProperty(argValue.AsString());
                case "Color": return SetColorProperty(argValue.AsString());
                case "Translate": return Translate(argValue);
                default: return Variable.EmptyInstance;
            }
        }

        public Variable Translate(Variable aVariable)
        {
            return new Variable(m_name + "_" + m_color + "_" + aVariable.AsString());
        }
    }

    public class TestCompiledClass : CompiledClass
    {
        public override ScriptObject GetImplementation(List<Variable> args)
        {
            string name = Utils.GetSafeString(args, 0);
            string color = Utils.GetSafeString(args, 1);
            return new TestScriptObject(name, color);
        }
    }
    public class TestCompiledClassAsync : CompiledClassAsync
    {
        public override Task<ScriptObject> GetImplementationAsync(List<Variable> args)
        {
            string name = Utils.GetSafeString(args, 0);
            string color = Utils.GetSafeString(args, 1);
            ScriptObject myScriptObject = new TestScriptObject(name, color);
            return Task.FromResult(myScriptObject);
        }
    }

    public interface ITest
    {
        string RunTest();
    }

    public class TestObj : ITest
    {
        public ITest TestInterface => this;

        string ITest.RunTest()
        {
            return "Test output";
        }
    }
}
