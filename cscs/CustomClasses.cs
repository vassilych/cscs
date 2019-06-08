using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SplitAndMerge
{
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
                case "Name": return Task.FromResult( GetNameProperty() );
                case "Color": return Task.FromResult( GetColorProperty() );
                case "Translate":
                    return Task.FromResult(
                        args != null && args.Count > 0 ?
                    Translate(args[0]) : Variable.EmptyInstance);
                default:
                    return Task.FromResult( Variable.EmptyInstance );
            }
        }

        public Task<Variable> SetNameProperty(string sValue)
        {
            m_name = sValue;
            return Task.FromResult( Variable.EmptyInstance );
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

    public abstract class CompiledClass : CSCSClass
    {
        public static void Init()
        {
            RegisterClass("CompiledTest", new TestCompiledClass());
            RegisterClass("CompiledTestAsync", new TestCompiledClassAsync());

            RegisterFunction("TestObject",
                new GetVarFunction(new Variable(new TestScriptObject())), true);
        }

        public abstract ScriptObject GetImplementation(List<Variable> args);
    }

    public abstract class CompiledClassAsync : CSCSClass
    {
        public abstract Task<ScriptObject> GetImplementationAsync(List<Variable> args);
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
            return Task.FromResult( myScriptObject );
        }
    }
}
