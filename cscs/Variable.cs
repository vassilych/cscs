using System;
using System.Collections.Generic;
using System.Text;

namespace SplitAndMerge
{
    public class Variable
    {
        public enum VarType
        {
            NONE, NUMBER, STRING, ARRAY,
            ARRAY_NUM, ARRAY_STR, MAP_NUM, MAP_STR,
            BREAK, CONTINUE, OBJECT
        };

        public Variable()
        {
            Reset();
        }
        public Variable(VarType type)
        {
            Type = type;
            if (Type == VarType.ARRAY)
            {
                SetAsArray();
            }
        }
        public Variable(double d)
        {
            Value = d;
        }
        public Variable(bool d)
        {
            Value = d ? 1.0 : 0.0;
        }
        public Variable(string s)
        {
            String = s;
        }
        public Variable(List<Variable> a)
        {
            this.Tuple = a;
        }
        public Variable(Variable other)
        {
            Copy(other);
        }
        public Variable(List<string> a)
        {
            List<Variable> tuple = new List<Variable>(a.Count);
            for (int i = 0; i < a.Count; i++)
            {
                tuple.Add(new Variable(a[i]));
            }
            this.Tuple = tuple;
        }
        public Variable(List<double> a)
        {
            List<Variable> tuple = new List<Variable>(a.Count);
            for (int i = 0; i < a.Count; i++)
            {
                tuple.Add(new Variable(a[i]));
            }
            this.Tuple = tuple;
        }
        public Variable(Dictionary<string, string> a)
        {
            List<Variable> tuple = new List<Variable>(a.Count);
            foreach (string key in a.Keys)
            {
                m_dictionary[key] = tuple.Count;
                tuple.Add(new Variable(a[key]));
            }
            this.Tuple = tuple;
        }
        public Variable(Dictionary<string, double> a)
        {
            List<Variable> tuple = new List<Variable>(a.Count);
            foreach (string key in a.Keys)
            {
                m_dictionary[key] = tuple.Count;
                tuple.Add(new Variable(a[key]));
            }
            this.Tuple = tuple;
        }

        public Variable(object o)
        {
            Object = o;
        }

        public virtual Variable Clone()
        {
            //Variable newVar = new Variable();
            //newVar.Copy(this);
            Variable newVar = (Variable)this.MemberwiseClone();
            return newVar;
        }
        public virtual Variable DeepClone()
        {
            Variable newVar = new Variable();
            newVar.Copy(this);

            if (m_tuple != null)
            {
                List<Variable> newTuple = new List<Variable>();
                foreach (var item in m_tuple)
                {
                    newTuple.Add(item.DeepClone());
                }

                newVar.Tuple = newTuple;
            }
            return newVar;
        }
        public virtual void Copy(Variable other)
        {
            Reset();
            Action = other.Action;
            Type = other.Type;
            IsReturn = other.IsReturn;
            m_dictionary = other.m_dictionary;
            m_propertyMap = other.m_propertyMap;
            //bug todo

            switch (other.Type)
            {
                case VarType.NUMBER:
                    Value = other.Value;
                    break;
                case VarType.STRING:
                    String = other.String;
                    break;
                case VarType.ARRAY:
                    this.Tuple = other.Tuple;
                    break;
                case VarType.OBJECT:
                    Object = other.Object;
                    break;
            }
        }

        public static Variable NewEmpty()
        {
            return new Variable();
        }

        public void Reset()
        {
            m_value = Double.NaN;
            m_string = null;
            m_object = null;
            m_tuple = null;
            Action = null;
            IsReturn = false;
            Type = VarType.NONE;
            m_dictionary.Clear();
            m_propertyMap.Clear();
        }

        public bool Equals(Variable other)
        {
            if (Type != other.Type)
            {
                return false;
            }
            if (Double.IsNaN(Value) != Double.IsNaN(other.Value) ||
              (!Double.IsNaN(Value) && Value != other.Value))
            {
                return false;
            }
            if (!String.Equals(this.String, other.String, StringComparison.Ordinal))
            {
                return false;
            }
            if (!String.Equals(this.Action, other.Action, StringComparison.Ordinal))
            {
                return false;
            }
            if ((this.Tuple == null) != (other.Tuple == null))
            {
                return false;
            }
            if (this.Tuple != null && !this.Tuple.Equals(other.Tuple))
            {
                return false;
            }
            if (!m_propertyMap.Equals(other.m_propertyMap))
            {
                return false;
            }
            return true;
        }

        public void AddVariableToHash(string hash, Variable newVar)
        {
            int retValue = 0;
            Variable listVar = null;
            if (m_dictionary.TryGetValue(hash, out retValue))
            {
                // already exists, change the value:
                listVar = m_tuple[retValue];
            }
            else
            {
                listVar = new Variable(VarType.ARRAY);
                m_tuple.Add(listVar);
                m_dictionary[hash] = m_tuple.Count - 1;
            }

            listVar.AddVariable(newVar);
        }

        public List<Variable> GetAllKeys()
        {
            List<Variable> results = new List<Variable>();
            var keys = m_dictionary.Keys;
            foreach (var key in keys)
            {
                results.Add(new Variable(key));
            }

            if (results.Count == 0 && m_tuple != null)
            {
                results = m_tuple;
            }

            return results;
        }

        public List<string> GetKeys()
        {
            List<string> results = new List<string>();
            var keys = m_dictionary.Keys;
            foreach (var key in keys)
            {
                results.Add(key);
            }
            return results;
        }

        public int SetHashVariable(string hash, Variable var)
        {
            int retValue = m_tuple.Count;
            if (m_dictionary.TryGetValue(hash, out retValue))
            {
                // already exists, change the value:
                m_tuple[retValue] = var;
                return retValue;
            }

            m_tuple.Add(var);
            m_dictionary[hash] = m_tuple.Count - 1;

            return m_tuple.Count - 1;
        }

        public int GetArrayIndex(Variable indexVar)
        {
            if (this.Type != VarType.ARRAY)
            {
                return -1;
            }

            if (indexVar.Type == VarType.NUMBER)
            {
                Utils.CheckNonNegativeInt(indexVar);
                return (int)indexVar.Value;
            }

            string hash = indexVar.AsString();
            int ptr = m_tuple.Count;
            if (m_dictionary.TryGetValue(hash, out ptr) &&
                ptr < m_tuple.Count)
            {
                return ptr;
            }

            int result = -1;
            if (!String.IsNullOrWhiteSpace(indexVar.String) &&
                Int32.TryParse(indexVar.String, out result))
            {
                return result;
            }

            return -1;
        }

        public void AddVariable(Variable v)
        {
            SetAsArray();
            m_tuple.Add(v);
        }

        public bool Exists(string hash)
        {
            return m_dictionary.ContainsKey(hash);
        }

        public bool Exists(Variable indexVar, bool notEmpty = false)
        {
            if (this.Type != VarType.ARRAY)
            {
                return false;
            }
            if (indexVar.Type == VarType.NUMBER)
            {
                if (indexVar.Value < 0 ||
                    indexVar.Value >= m_tuple.Count ||
                    indexVar.Value - Math.Floor(indexVar.Value) != 0.0)
                {
                    return false;
                }
                if (notEmpty)
                {
                    return m_tuple[(int)indexVar.Value].Type != VarType.NONE;
                }
                return true;
            }

            string hash = indexVar.AsString();
            return Exists(hash);
        }

        public bool AsBool()
        {
            if (Type == VarType.NUMBER && m_value != 0.0)
            {
                return true;
            }
            if (Type == VarType.STRING)
            {
                if (String.Compare(m_string, "true", true) == 0)
                    return true;
            }

            return false;
        }

        public int AsInt()
        {
            int result = 0;
            if (Type == VarType.NUMBER || Value != 0.0)
            {
                return (int)m_value;
            }
            if (Type == VarType.STRING)
            {
                Int32.TryParse(m_string, out result);
            }

            return result;
        }
        public long AsLong()
        {
            long result = 0;
            if (Type == VarType.NUMBER || Value != 0.0)
            {
                return (long)m_value;
            }
            if (Type == VarType.STRING)
            {
                long.TryParse(m_string, out result);
            }
            return result;
        }
        public double AsDouble()
        {
            double result = 0.0;
            if (Type == VarType.NUMBER)
            {// || (Value != 0.0 && Value != Double.NaN)) {
                return m_value;
            }
            if (Type == VarType.STRING)
            {
                Double.TryParse(m_string, out result);
            }

            return result;
        }

        public virtual string AsString(bool isList = true,
                                       bool sameLine = true,
                                       int maxCount = -1)
        {
            if (Type == VarType.NUMBER)
            {
                return Value.ToString();
            }
            if (Type == VarType.STRING)
            {
                return m_string == null ? "" : m_string;
            }
            if (Type == VarType.OBJECT)
            {
                return ObjectToString();
            }
            if (Type == VarType.NONE || m_tuple == null)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();

            if (isList)
            {
                sb.Append(Constants.START_GROUP.ToString() +
                         (sameLine ? "" : Environment.NewLine));
            }

            int count = maxCount < 0 ? m_tuple.Count : Math.Min(maxCount, m_tuple.Count);
            int i = 0;
            if (m_dictionary.Count > 0)
            {
                count = maxCount < 0 ? m_dictionary.Count : Math.Min(maxCount, m_dictionary.Count);
                foreach (KeyValuePair<string, int> entry in m_dictionary)
                {
                    if (entry.Value >= 0 || entry.Value < m_tuple.Count)
                    {
                        string value = m_tuple[entry.Value].AsString(isList, sameLine, maxCount);
                        sb.Append("\"" + entry.Key + "\" : " + value);
                        if (i++ < count - 1)
                        {
                            sb.Append(sameLine ? ", " : Environment.NewLine);
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
            else
            {
                for (; i < count; i++)
                {
                    Variable arg = m_tuple[i];
                    sb.Append(arg.AsString(isList, sameLine, maxCount));
                    if (i != count - 1)
                    {
                        sb.Append(sameLine ? ", " : Environment.NewLine);
                    }
                }
            }
            if (count < m_tuple.Count)
            {
                sb.Append(" ...");
            }
            if (isList)
            {
                sb.Append(Constants.END_GROUP.ToString() +
                         (sameLine ? "" : Environment.NewLine));
            }

            return sb.ToString();
        }

        string ObjectToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append((m_object !=null ? (m_object.ToString() + " ") : "") + Constants.START_GROUP.ToString());

            List<string> allProps = GetAllProperties();
            foreach (string prop in allProps)
            {
                if (prop == Constants.OBJECT_PROPERTIES)
                {
                    sb.Append(prop);
                    continue;
                }
                Variable propValue = GetProperty(prop);
                string value = "";
                if (propValue != null && propValue != Variable.EmptyInstance)
                {
                    value = propValue.AsString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        if (propValue.Type == VarType.STRING && prop != Constants.OBJECT_TYPE)
                        {
                            value = "\"" + value + "\"";
                        }
                        value = ": " + value;
                    }
                }
                sb.Append(prop + value + ", ");
            }

            sb.Append(Constants.END_GROUP.ToString());
            return sb.ToString();
        }

        public void SetAsArray()
        {
            Type = VarType.ARRAY;
            if (m_tuple == null)
            {
                m_tuple = new List<Variable>();
            }
        }

        public int TotalElements()
        {
            return Type == VarType.ARRAY ? m_tuple.Count : 1;
        }

        public Variable SetProperty(string name, Variable value)
        {
            Variable result = Variable.EmptyInstance;
            m_propertyMap[name] = value;
            Type = VarType.OBJECT;

            if (Object is ScriptObject)
            {
                ScriptObject obj = Object as ScriptObject;
                result = obj.SetProperty(name, value);
            }
            return result;
        }

        public Variable GetProperty(string name, ParsingScript script = null)
        {
            Variable result = Variable.EmptyInstance;

            if (Object is ScriptObject)
            {
                ScriptObject obj = Object as ScriptObject;
                var supported = obj.GetProperties();
                if (supported.Contains(name))
                {
                    List<Variable> args = null;
                    if (script != null && script.TryPrev() == Constants.START_ARG)
                    {
                        args = script.GetFunctionArgs();
                    }
                    result = obj.GetProperty(name, args);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }

            if (m_propertyMap.TryGetValue(name, out result))
            {
                return result;
            }
            else if (name.Equals(Constants.OBJECT_PROPERTIES, StringComparison.OrdinalIgnoreCase))
            {
                return new Variable(GetProperties());
            }
            else if (name.Equals(Constants.OBJECT_TYPE, StringComparison.OrdinalIgnoreCase))
            {
                return new Variable(GetTypeString());
            }

            return result;
        }

        public List<Variable> GetProperties()
        {
            List<string> all = GetAllProperties();
            List <Variable> allVars = new List<Variable>(all.Count);
            foreach (string key in all)
            {
                allVars.Add(new Variable(key));
            }

            return allVars;
        }

        public List<string> GetAllProperties()
        {
            HashSet<string> allSet = new HashSet<string>();
            foreach (string key in m_propertyMap.Keys)
            {
                allSet.Add(key);
            }

            if (Object is ScriptObject)
            {
                ScriptObject obj = Object as ScriptObject;
                List<string> objProps = obj.GetProperties();
                foreach (string key in objProps)
                {
                    allSet.Add(key);
                }
            }

            List<string> all = new List<string>(allSet);
            all.Sort();

            if (!allSet.Contains(Constants.OBJECT_TYPE))
            {
                all.Insert(0, Constants.OBJECT_TYPE);
            }
            if (!allSet.Contains(Constants.OBJECT_PROPERTIES))
            {
                all.Add(Constants.OBJECT_PROPERTIES);
            }

            return all;
        }

        public string GetTypeString()
        {
            if (Type == VarType.OBJECT && Object != null)
            {
                return Object.GetType().ToString();
            }
            return Constants.TypeToString(Type);
        }

        public Variable GetValue(int index)
        {
            if (index >= TotalElements())
            {
                throw new ArgumentException("There are only [" + TotalElements() +
                                             "] but " + index + " requested.");

            }
            if (Type == VarType.ARRAY)
            {
                return m_tuple[index];
            }
            return this;
        }

        public double Value
        {
            get { return m_value; }
            set { m_value = value; Type = VarType.NUMBER; }
        }

        public string String
        {
            get { return m_string; }
            set { m_string = value; Type = VarType.STRING; }
        }

        public object Object
        {
            get { return m_object; }
            set { m_object = value; Type = VarType.OBJECT; }
        }

        public List<Variable> Tuple
        {
            get { return m_tuple; }
            set { m_tuple = value; Type = VarType.ARRAY; }
        }

        public string Action { get; set; }
        public VarType Type { get; set; }
        public bool IsReturn { get; set; }
        public string ParsingToken { get; set; }

        public static Variable EmptyInstance = new Variable();

        double m_value;
        string m_string;
        object m_object;
        List<Variable> m_tuple;
        Dictionary<string, int> m_dictionary = new Dictionary<string, int>();

        Dictionary<string, Variable> m_propertyMap = new Dictionary<string, Variable>();
    }

    // A Variable supporting "dot-notation" must have an object implementing this interface.
    public interface ScriptObject
    {
        // SetProperty is triggered by the following scripting call: "a.name = value;"
        Variable SetProperty(string name, Variable value);

        // GetProperty is triggered by the following scripting call: "x = a.name;"
        // If args are not null, it is triggered by a function call: "y = a.name(arg1, arg2, ...);"
        Variable GetProperty(string name, List<Variable> args = null);

        // Returns all of the properties that this object implements. Only these properties will be processed
        // by SetProperty() and GetProperty() methods above.
        List<string> GetProperties();
    }
}

