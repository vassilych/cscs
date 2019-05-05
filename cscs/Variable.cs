using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SplitAndMerge
{
    public class Variable
    {
        public enum VarType
        {
            NONE, NUMBER, STRING, ARRAY,
            ARRAY_NUM, ARRAY_STR, MAP_NUM, MAP_STR,
            BREAK, CONTINUE, OBJECT, ENUM
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
                string lower = key.ToLower();
                m_keyMappings[lower] = key;
                m_dictionary[lower] = tuple.Count;
                tuple.Add(new Variable(a[key]));
            }
            this.Tuple = tuple;
        }
        public Variable(Dictionary<string, double> a)
        {
            List<Variable> tuple = new List<Variable>(a.Count);
            foreach (string key in a.Keys)
            {
                string lower = key.ToLower();
                m_keyMappings[lower] = key;
                m_dictionary[lower] = tuple.Count;
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
            Variable newVar = (Variable)this.MemberwiseClone();
            return newVar;
        }

        public virtual Variable DeepClone()
        {
            //Variable newVar = new Variable();
            //newVar.Copy(this);
            Variable newVar = (Variable)this.MemberwiseClone();

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
            string lower = hash.ToLower();
            if (m_dictionary.TryGetValue(lower, out retValue))
            {
                // already exists, change the value:
                listVar = m_tuple[retValue];
            }
            else
            {
                listVar = new Variable(VarType.ARRAY);
                m_tuple.Add(listVar);

                m_keyMappings[lower] = hash;
                m_dictionary[lower] = m_tuple.Count - 1;
            }

            listVar.AddVariable(newVar);
        }

        public List<Variable> GetAllKeys()
        {
            List<Variable> results = new List<Variable>();
            var keys = m_keyMappings.Values;
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
            var keys = m_keyMappings.Values;
            foreach (var key in keys)
            {
                results.Add(key);
            }
            return results;
        }

        public int SetHashVariable(string hash, Variable var)
        {
            int retValue = m_tuple.Count;
            string lower = hash.ToLower();
            if (m_dictionary.TryGetValue(lower, out retValue))
            {
                // already exists, change the value:
                m_tuple[retValue] = var;
                return retValue;
            }

            m_tuple.Add(var);
            m_keyMappings[lower] = hash;
            m_dictionary[lower] = m_tuple.Count - 1;

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
            string lower = hash.ToLower();
            int ptr = m_tuple.Count;
            if (m_dictionary.TryGetValue(lower, out ptr) &&
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

        public void AddVariable(Variable v, int index = -1)
        {
            SetAsArray();
            if (index < 0 || m_tuple.Count <= index)
            {
                m_tuple.Add(v);
            }
            else
            {
                m_tuple.Insert(index, v);
            }
        }

        public Variable GetVariable(int index)
        {
            if (index < 0 || m_tuple == null || m_tuple.Count <= index)
            {
                return Variable.EmptyInstance;
            }
            return m_tuple[index];
        }

        public Variable GetVariable(string hash)
        {
            int index = 0;
            string lower = hash.ToLower();
            if (m_tuple == null || !m_dictionary.TryGetValue(lower, out index) ||
                m_tuple.Count <= index)
            {
                return Variable.EmptyInstance;
            }
            return m_tuple[index];
        }

        public bool Exists(string hash)
        {
            string lower = hash.ToLower();
            return m_dictionary.ContainsKey(lower);
        }

        public int FindIndex(string val)
        {
            if (this.Type != VarType.ARRAY)
            {
                return -1;
            }
            int result = m_tuple.FindIndex(item => item.AsString() == val);
            return result;
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
        public float AsFloat()
        {
            float result = 0;
            if (Type == VarType.NUMBER || Value != 0.0)
            {
                return (float)m_value;
            }
            if (Type == VarType.STRING)
            {
                float.TryParse(m_string, out result);
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

        public override string ToString()
        {
            return AsString();
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

            StringBuilder sb = new StringBuilder();
            if (Type == VarType.ENUM)
            {
                sb.Append(Constants.START_GROUP.ToString() + " ");
                foreach (string key in m_propertyMap.Keys)
                {
                    sb.Append(key + " ");
                }
                sb.Append(Constants.END_GROUP.ToString());
                return sb.ToString();
            }

            if (Type == VarType.NONE || m_tuple == null)
            {
                return string.Empty;
            }

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
                        string realKey = entry.Key;
                        m_keyMappings.TryGetValue(entry.Key.ToLower(), out realKey);

                        sb.Append("\"" + realKey + "\" : " + value);
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
            if (m_object != null)
            {
                sb.Append(m_object.ToString());
            }
            else
            {
                sb.Append((m_object != null ? (m_object.ToString() + " ") : "") +
                           Constants.START_GROUP.ToString());

                List<string> allProps = GetAllProperties();
                for (int i = 0;  i < allProps.Count; i++)
                {
                    string prop = allProps[i];
                    if (prop.Equals(Constants.OBJECT_PROPERTIES, StringComparison.OrdinalIgnoreCase))
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
                            if (propValue.Type == VarType.STRING &&
                               !prop.Equals(Constants.OBJECT_TYPE, StringComparison.OrdinalIgnoreCase))
                            {
                                value = "\"" + value + "\"";
                            }
                            value = ": " + value;
                        }
                    }
                    sb.Append(prop + value);
                    if (i < allProps.Count - 1)
                    {
                        sb.Append(", ");
                    }
                }

                sb.Append(Constants.END_GROUP.ToString());
            }
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

        public int Count
        {
            get { return Type == VarType.ARRAY ? m_tuple.Count :
                         Type == VarType.NONE  ? 0 : 1; }
        }

        public int TotalElements()
        {
            return Count;
        }

        public Variable SetProperty(string propName, Variable value, string baseName = "")
        {
            propName = Constants.ConvertName(propName);

            int ind = propName.IndexOf('.');
            if (ind > 0)
            { // The case a.b.c = ... is dealt here recursively
                string varName = propName.Substring(0, ind);
                string actualPropName = propName.Substring(ind + 1);
                Variable property = GetProperty(varName);
                Utils.CheckNotNull(property, varName);
                return property.SetProperty(actualPropName, value, baseName);
            }
            return FinishSetProperty(propName, value, baseName);
        }

        public async Task<Variable> SetPropertyAsync(string propName, Variable value, string baseName = "")
        {
            Variable result = Variable.EmptyInstance;
            propName = Constants.ConvertName(propName);

            int ind = propName.IndexOf('.');
            if (ind > 0)
            { // The case a.b.c = ... is dealt here recursively
                string varName = propName.Substring(0, ind);
                string actualPropName = propName.Substring(ind + 1);
                Variable property = await GetPropertyAsync(varName);
                Utils.CheckNotNull(property, varName);
                result = await property.SetPropertyAsync(actualPropName, value, baseName);
                return result;
            }
            return FinishSetProperty(propName, value, baseName);
        }

        public Variable FinishSetProperty(string propName, Variable value, string baseName = "")
        {
            Variable result = Variable.EmptyInstance;
            string match = GetActualPropertyName(propName, GetAllProperties(), baseName, this);
            m_propertyMap[match] = value;
            Type = VarType.OBJECT;

            if (Object is ScriptObject)
            {
                ScriptObject obj = Object as ScriptObject;
                result = obj.SetProperty(match, value).Result;
            }
            return result;
        }

        public void SetEnumProperty(string propName, Variable value, string baseName = "")
        {
            m_propertyMap[propName] = value;

            if (m_enumMap == null)
            {
                m_enumMap = new Dictionary<int, string>();
            }
            m_enumMap[value.AsInt()] = propName;
        }

        public Variable GetEnumProperty(string propName, ParsingScript script, string baseName = "")
        {
            propName = Constants.ConvertName(propName);
            if (script.TryPrev() == Constants.START_ARG)
            {
                Variable value = Utils.GetItem(script);
                if (propName == Constants.TO_STRING)
                {
                    return ConvertEnumToString(value);
                }
                else
                {
                    return new Variable(m_enumMap != null && m_enumMap.ContainsKey(value.AsInt()));
                }
            }

            string[] tokens = propName.Split('.');
            if (tokens.Length > 1)
            {
                propName = tokens[0];
            }

            string match = GetActualPropertyName(propName, GetAllProperties(), baseName, this);

            Variable result = GetCoreProperty(match, script);

            if (tokens.Length > 1)
            {
                result = ConvertEnumToString(result);
                if (tokens.Length > 2)
                {
                    string rest = string.Join(".", tokens, 2, tokens.Length - 2);
                    result = result.GetProperty(rest, script);
                }
            }

            return result;
        }

        public Variable ConvertEnumToString(Variable value)
        {
            string result = "";
            if (m_enumMap != null && m_enumMap.TryGetValue(value.AsInt(), out result))
            {
                return new Variable(result);
            }
            return Variable.EmptyInstance;
        }

        public Variable GetProperty(string propName, ParsingScript script = null)
        {
            Variable result = Variable.EmptyInstance;

            int ind = propName.IndexOf('.');
            if (ind > 0)
            { // The case x = a.b.c ... is dealt here recursively
                string varName = propName.Substring(0, ind);
                string actualPropName = propName.Substring(ind + 1);
                Variable property = GetProperty(varName, script);
                result = string.IsNullOrEmpty(actualPropName) ? property :
                               property.GetProperty(actualPropName, script);
                return result;
            }

            if (Object is ScriptObject)
            {
                ScriptObject obj = Object as ScriptObject;
                string match = GetActualPropertyName(propName, obj.GetProperties());
                if (!string.IsNullOrWhiteSpace(match))
                {
                    List<Variable> args = null;
                    if (script != null && script.TryPrev() == Constants.START_ARG)
                    {
                        args = script.GetFunctionArgs();
                    }
                    else if (script != null)
                    {
                        args = new List<Variable>();
                    }
                    var task = obj.GetProperty(match, args, script);
                    result   = task != null ? task.Result : null;
                    if (result != null)
                    {
                        return result;
                    }
                }
            }

            return GetCoreProperty(propName, script);
        }

        public async Task<Variable> GetPropertyAsync(string propName, ParsingScript script = null)
        {
            Variable result = Variable.EmptyInstance;

            int ind = propName.IndexOf('.');
            if (ind > 0)
            { // The case x = a.b.c ... is dealt here recursively
                string varName = propName.Substring(0, ind);
                string actualPropName = propName.Substring(ind + 1);
                Variable property = await GetPropertyAsync(varName, script);
                result = string.IsNullOrEmpty(actualPropName) ? property :
                               await property.GetPropertyAsync(actualPropName, script);
                return result;
            }

            if (Object is ScriptObject)
            {
                ScriptObject obj = Object as ScriptObject;
                string match = GetActualPropertyName(propName, obj.GetProperties());
                if (!string.IsNullOrWhiteSpace(match))
                {
                    List<Variable> args = null;
                    if (script != null && script.TryPrev() == Constants.START_ARG)
                    {
                        args = await script.GetFunctionArgsAsync();
                    }
                    else if (script != null)
                    {
                        args = new List<Variable>();
                    }
                    result = await obj.GetProperty(match, args, script);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }

            return GetCoreProperty(propName, script);
        }

        Variable GetCoreProperty(string propName, ParsingScript script = null)
        {
            Variable result = Variable.EmptyInstance;

            if (m_propertyMap.TryGetValue(propName, out result))
            {
                return result;
            }
            else if (propName.Equals(Constants.OBJECT_PROPERTIES, StringComparison.OrdinalIgnoreCase))
            {
                return new Variable(GetProperties());
            }
            else if (propName.Equals(Constants.OBJECT_TYPE, StringComparison.OrdinalIgnoreCase))
            {
                return new Variable((int)Type);
            }
            else if (propName.Equals(Constants.SIZE, StringComparison.OrdinalIgnoreCase))
            {
                return new Variable(GetSize());
            }
            else if (propName.Equals(Constants.UPPER, StringComparison.OrdinalIgnoreCase))
            {
                return new Variable(AsString().ToUpper());
            }
            else if (propName.Equals(Constants.LOWER, StringComparison.OrdinalIgnoreCase))
            {
                return new Variable(AsString().ToLower());
            }
            else if (propName.Equals(Constants.STRING, StringComparison.OrdinalIgnoreCase))
            {
                return new Variable(AsString());
            }
            else if (propName.Equals(Constants.FIRST, StringComparison.OrdinalIgnoreCase))
            {
                if (Tuple != null && Tuple.Count > 0)
                {
                    return Tuple[0];
                }
                return AsString().Length > 0 ? new Variable("" + AsString()[0]) : Variable.EmptyInstance;
            }
            else if (propName.Equals(Constants.LAST, StringComparison.OrdinalIgnoreCase))
            {
                if (Tuple != null && Tuple.Count > 0)
                {
                    return Tuple.Last<Variable>();
                }
                return AsString().Length > 0 ? new Variable("" + AsString().Last<char>()) : Variable.EmptyInstance;
            }
            else if (script != null && propName.Equals(Constants.INDEX_OF, StringComparison.OrdinalIgnoreCase))
            {
                List<Variable> args = script.GetFunctionArgs();
                Utils.CheckArgs(args.Count, 1, propName);

                string search = Utils.GetSafeString(args, 0);
                int startFrom = Utils.GetSafeInt(args, 1, 0);
                string param  = Utils.GetSafeString(args, 2, "no_case");
                StringComparison comp = param.Equals("case", StringComparison.OrdinalIgnoreCase) ?
                    StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;

                return new Variable(AsString().IndexOf(search, startFrom,comp));
            }
            else if (script != null && propName.Equals(Constants.SUBSTRING, StringComparison.OrdinalIgnoreCase))
            {
                List<Variable> args = script.GetFunctionArgs();
                Utils.CheckArgs(args.Count, 1, propName);

                int startFrom = Utils.GetSafeInt(args, 0, 0);
                int length = Utils.GetSafeInt(args, 1, AsString().Length);
                length = Math.Min(length, AsString().Length - startFrom);

                return new Variable(AsString().Substring(startFrom, length));
            }
            else if (script != null && propName.Equals(Constants.REVERSE, StringComparison.OrdinalIgnoreCase))
            {
                script.GetFunctionArgs();
                if (Tuple != null)
                {
                    Tuple.Reverse();
                }
                else if (Type == VarType.STRING)
                {
                    char[] charArray = AsString().ToCharArray();
                    Array.Reverse(charArray);
                    String = new string(charArray);
                }

                return this;
            }
            else if (script != null && propName.Equals(Constants.SORT, StringComparison.OrdinalIgnoreCase))
            {
                script.GetFunctionArgs();
                Sort();

                return this;
            }
            else if (script != null && propName.Equals(Constants.SPLIT, StringComparison.OrdinalIgnoreCase))
            {
                List<Variable> args = script.GetFunctionArgs();
                string sep = Utils.GetSafeString(args, 0, " ");
                var option = Utils.GetSafeString(args, 1);

                return TokenizeFunction.Tokenize(AsString(), sep, option);
            }
            else if (script != null && propName.Equals(Constants.JOIN, StringComparison.OrdinalIgnoreCase))
            {
                List<Variable> args = script.GetFunctionArgs();
                string sep = Utils.GetSafeString(args, 0, " ");
                if (Tuple == null)
                {
                    return new Variable(AsString());
                }

                var join = string.Join(sep, Tuple);
                return new Variable(join);
            }
            else if (script != null && propName.Equals(Constants.ADD, StringComparison.OrdinalIgnoreCase))
            {
                List<Variable> args = script.GetFunctionArgs();
                Utils.CheckArgs(args.Count, 1, propName);
                Variable var = Utils.GetSafeVariable(args, 0);
                if (Tuple != null)
                {
                    Tuple.Add(var);
                }
                else if (Type == VarType.NUMBER)
                {
                    Value += var.AsDouble();
                }
                else
                {
                    String += var.AsString();
                }
                return var;
            }
            else if (script != null && propName.Equals(Constants.AT, StringComparison.OrdinalIgnoreCase))
            {
                List<Variable> args = script.GetFunctionArgs();
                Utils.CheckArgs(args.Count, 1, propName);
                int at = Utils.GetSafeInt(args, 0);

                if (Tuple != null && Tuple.Count > 0)
                {
                    return Tuple.Count > at ? Tuple[at] : Variable.EmptyInstance;
                }
                string str = AsString();
                return str.Length > at ? new Variable("" + str[at]) : Variable.EmptyInstance;
            }
            else if (script != null && propName.Equals(Constants.REPLACE, StringComparison.OrdinalIgnoreCase))
            {
                List<Variable> args = script.GetFunctionArgs();
                Utils.CheckArgs(args.Count, 2, propName);
                string oldVal = Utils.GetSafeString(args, 0);
                string newVal = Utils.GetSafeString(args, 1);

                return new Variable(AsString().Replace(oldVal, newVal));
            }
            else if (script != null && propName.Equals(Constants.CONTAINS, StringComparison.OrdinalIgnoreCase))
            {
                List<Variable> args = script.GetFunctionArgs();
                Utils.CheckArgs(args.Count, 1, propName);
                string val = Utils.GetSafeString(args, 0);
                string param = Utils.GetSafeString(args, 1, "no_case");
                StringComparison comp = param.Equals("case", StringComparison.OrdinalIgnoreCase) ?
                    StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;

                bool contains = val != "" && AsString().IndexOf(val, comp) >= 0;
                if (!contains && (Type == Variable.VarType.ARRAY && m_dictionary != null))
                {
                    string lower = val.ToLower();
                    contains = m_dictionary.ContainsKey(lower);
                }
                return new Variable(contains);
            }
            else if (script != null && propName.Equals(Constants.EQUALS, StringComparison.OrdinalIgnoreCase))
            {
                List<Variable> args = script.GetFunctionArgs();
                Utils.CheckArgs(args.Count, 1, propName);
                string val = Utils.GetSafeString(args, 0);
                string param = Utils.GetSafeString(args, 1, "no_case");
                StringComparison comp = param.Equals("case", StringComparison.OrdinalIgnoreCase) ?
                    StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;

                return new Variable(AsString().Equals(val, comp));
            }
            else if (script != null && propName.Equals(Constants.STARTS_WITH, StringComparison.OrdinalIgnoreCase))
            {
                List<Variable> args = script.GetFunctionArgs();
                Utils.CheckArgs(args.Count, 1, propName);
                string val = Utils.GetSafeString(args, 0);
                string param = Utils.GetSafeString(args, 1, "no_case");
                StringComparison comp = param.Equals("case", StringComparison.OrdinalIgnoreCase) ?
                    StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;

                return new Variable(AsString().StartsWith(val, comp));
            }
            else if (script != null && propName.Equals(Constants.ENDS_WITH, StringComparison.OrdinalIgnoreCase))
            {
                List<Variable> args = script.GetFunctionArgs();
                Utils.CheckArgs(args.Count, 1, propName);
                string val = Utils.GetSafeString(args, 0);
                string param = Utils.GetSafeString(args, 1, "no_case");
                StringComparison comp = param.Equals("case", StringComparison.OrdinalIgnoreCase) ?
                    StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;

                return new Variable(AsString().EndsWith(val, comp));
            }
            else if (script != null && propName.Equals(Constants.TRIM, StringComparison.OrdinalIgnoreCase))
            {
                script.GetFunctionArgs();
                return new Variable(AsString().Trim());
            }
            else if (propName.Equals(Constants.KEYS, StringComparison.OrdinalIgnoreCase))
            {
                List<Variable> results = GetAllKeys();
                return new Variable(results);
            }

            return result;
        }


        public List<Variable> GetProperties()
        {
            List<string> all = GetAllProperties();
            List<Variable> allVars = new List<Variable>(all.Count);
            foreach (string key in all)
            {
                allVars.Add(new Variable(key));
            }

            return allVars;
        }

        public List<string> GetAllProperties()
        {
            HashSet<string> allSet = new HashSet<string>();
            List<string> all = new List<string>();

            foreach (string key in m_propertyMap.Keys)
            {
                allSet.Add(key.ToLower());
                all.Add(key);
            }

            if (Object is ScriptObject)
            {
                ScriptObject obj = Object as ScriptObject;
                List<string> objProps = obj.GetProperties();
                foreach (string key in objProps)
                {
                    if (allSet.Add(key.ToLower()))
                    {
                        all.Add(key);
                    }
                }
            }

            all.Sort();

            if (!allSet.Contains(Constants.OBJECT_TYPE.ToLower()))
            {
                all.Add(Constants.OBJECT_TYPE);
            }

            return all;
        }

        public int GetSize()
        {
            int size = Type == Variable.VarType.ARRAY ?
                  Tuple.Count :
                  AsString().Length;
            return size;
        }

        public virtual string GetTypeString()
        {
            if (Type == VarType.OBJECT && Object != null)
            {
                return Object.GetType().ToString();
            }
            return Constants.TypeToString(Type);
        }

        public Variable GetValue(int index)
        {
            if (index >= Count)
            {
                throw new ArgumentException("There are only [" + Count +
                                             "] but " + index + " requested.");

            }
            if (Type == VarType.ARRAY)
            {
                return m_tuple[index];
            }
            return this;
        }

        public static string GetActualPropertyName(string propName, List<string> properties,
                                                   string baseName = "", Variable root = null)
        {
            string match = properties.FirstOrDefault(element => element.Equals(propName,
                                   StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(match))
            {
                match = "";
                if (root != null)
                {
                    string objName = !string.IsNullOrWhiteSpace(baseName) ? baseName + "." : "";
                    if (string.IsNullOrWhiteSpace(objName))
                    {
                        CSCSClass.ClassInstance obj = root.m_object as CSCSClass.ClassInstance;
                        objName = obj != null ? obj.InstanceName + "." : "";
                    }
                    match = Constants.GetRealName(objName + propName);
                    match = match.Substring(objName.Length);
                }
            }
            return match;
        }

        public void Sort()
        {
            if (Tuple == null || Tuple.Count <= 1)
            {
                return;
            }

            List<double> numbers = new List<double>();
            List<string> strings = new List<string>();
            for (int i = 0; i < Tuple.Count; i++)
            {
                Variable arg = Tuple[i];
                if (arg.Tuple != null)
                {
                    arg.Sort();
                }
                else if (arg.Type == VarType.NUMBER)
                {
                    numbers.Add(arg.AsDouble());
                }
                else
                {
                    strings.Add(arg.AsString());
                }
            }
            List<Variable> newTuple = new List<Variable>(Tuple.Count);
            numbers.Sort();
            strings.Sort();

            for (int i = 0; i < numbers.Count; i++)
            {
                newTuple.Add(new Variable(numbers[i]));
            }
            for (int i = 0; i < strings.Count; i++)
            {
                newTuple.Add(new Variable(strings[i]));
            }
            Tuple = newTuple;
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
        public VarType Type
        {
            get;
            set;
        }
        public bool IsReturn { get; set; }
        public string ParsingToken { get; set; }
        public int Index { get; set; }
        public string CurrentAssign { get; set; } = "";

        public static Variable EmptyInstance = new Variable();

        double m_value;
        string m_string;
        object m_object;
        List<Variable> m_tuple;
        Dictionary<string, int> m_dictionary = new Dictionary<string, int>();
        Dictionary<string, string> m_keyMappings = new Dictionary<string, string>();

        Dictionary<string, Variable> m_propertyMap = new Dictionary<string, Variable>();
        Dictionary<int, string> m_enumMap;
    }

    // A Variable supporting "dot-notation" must have an object implementing this interface.
    public interface ScriptObject
    {
        // SetProperty is triggered by the following scripting call: "a.name = value;"
        Task<Variable> SetProperty(string name, Variable value);

        // GetProperty is triggered by the following scripting call: "x = a.name;"
        // If args are null, it is triggered by object.ToString() function"
        // If args are not empty, it is triggered by a function call: "y = a.name(arg1, arg2, ...);"
        Task<Variable> GetProperty(string name, List<Variable> args = null, ParsingScript script = null);

        // Returns all of the properties that this object implements. Only these properties will be processed
        // by SetProperty() and GetProperty() methods above.
        List<string> GetProperties();
    }
}
