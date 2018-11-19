using System;
using System.Collections.Generic;

namespace SplitAndMerge
{
    public class TestScriptObject : ScriptObject
    {
        static List<string> s_properties = new List<string> {
            "name", "color", "translate"
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

        public virtual Variable GetProperty(string sPropertyName, List<Variable> args = null, ParsingScript script = null)
        {
            switch (sPropertyName)
            {
                case "name": return GetNameProperty();
                case "color": return GetColorProperty();
                case "translate":
                    return args != null && args.Count > 0 ?
                    Translate(args[0]) : Variable.EmptyInstance;
                default:
                    return Variable.EmptyInstance;
            }
        }

        public Variable SetNameProperty(string sValue)
        {
            m_name = sValue;
            SetProperty("name", new Variable(sValue));
            return Variable.EmptyInstance;
        }

        public Variable SetColorProperty(string aColor)
        {
            m_color = aColor;
            return Variable.EmptyInstance;
        }

        public virtual Variable SetProperty(string sPropertyName, Variable argValue)
        {
            switch (sPropertyName)
            {
                case "name": return SetNameProperty(argValue.AsString());
                case "color": return SetColorProperty(argValue.AsString());
                case "translate": return Translate(argValue);
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
        }

        public abstract ScriptObject GetImplementation(List<Variable> args);
    }

    public class TestCompiledClass : CompiledClass
    {
        public override ScriptObject GetImplementation(List<Variable> args)
        {
            string name  = Utils.GetSafeString(args, 0);
            string color = Utils.GetSafeString(args, 1);
            return new TestScriptObject(name, color);
        }
    }
}
