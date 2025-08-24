using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SplitAndMerge
{
    public class Variable
    {
        public enum VarType
        {
            NONE, UNDEFINED, NUMBER, STRING, ARRAY,
            ARRAY_NUM, ARRAY_STR, ARRAY_INT, INT, MAP_INT, MAP_NUM, MAP_STR, BYTE_ARRAY, QUIT,
            BREAK, CONTINUE, OBJECT, ENUM, VARIABLE, DATETIME, CUSTOM, POINTER
        };
        public enum OriginalType
        {
            NONE, UNDEFINED, INT, LONG, BOOL, DOUBLE, STRING, BYTE_ARRAY, ARRAY, DATE_TIME, OBJECT
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
            Original = OriginalType.DOUBLE;
        }
        public Variable(int d)
        {
            Value = d;
            Original = OriginalType.INT;
        }
        public Variable(long d)
        {
            Value = d;
            Original = OriginalType.LONG;
        }
        public Variable(bool d)
        {
            Value = d ? 1.0 : 0.0;
            Original = OriginalType.BOOL;
        }
        public Variable(string s)
        {
            String = s;
            Original = OriginalType.STRING;
        }
        public Variable(DateTime dt)
        {
            DateTime = dt;
            Original = OriginalType.DATE_TIME;
        }
        public Variable(byte[] ba)
        {
            ByteArray = ba;
            Original = OriginalType.BYTE_ARRAY;
        }
        public Variable(List<Variable> a)
        {
            this.Tuple = a;
            Original = OriginalType.ARRAY;
        }
        public Variable(List<string> a)
        {
            List<Variable> tuple = new List<Variable>(a.Count);
            for (int i = 0; i < a.Count; i++)
            {
                tuple.Add(new Variable(a[i]));
            }
            this.Tuple = tuple;
            Original = OriginalType.ARRAY;
        }
        public Variable(List<double> a)
        {
            List<Variable> tuple = new List<Variable>(a.Count);
            for (int i = 0; i < a.Count; i++)
            {
                tuple.Add(new Variable(a[i]));
            }
            this.Tuple = tuple;
            Original = OriginalType.ARRAY;
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
            Original = OriginalType.ARRAY;
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
            Original = OriginalType.ARRAY;
        }

        public Variable(object o, Type t = null)
        {
            Object = o;
            Original = OriginalType.OBJECT;
            ObjectType = t == null ? o?.GetType() : t;
        }

        public virtual Variable Clone()
        {
            Variable newVar = (Variable)this.MemberwiseClone();
            return newVar;
        }

        public virtual Variable DeepClone(string newName = "")
        {
            Variable newVar = (Variable)this.MemberwiseClone();
            if (Type == VarType.ARRAY && m_tuple != null)
            {
                List<Variable> newTuple = new List<Variable>();
                foreach (var item in m_tuple)
                {
                    newTuple.Add(item.DeepClone());
                }

                newVar.Tuple = newTuple;

                newVar.m_dictionary = new Dictionary<string, int>(m_dictionary);
                newVar.m_keyMappings = new Dictionary<string, string>(m_keyMappings);
                newVar.m_propertyStringMap = new Dictionary<string, string>(m_propertyStringMap);
                newVar.m_propertyMap = new Dictionary<string, Variable>(m_propertyMap);
                newVar.m_enumMap = m_enumMap == null ? null : new Dictionary<int, string>(m_enumMap);
            }
            newVar.ParamName = string.IsNullOrWhiteSpace(newName) ? newVar.ParamName : newName;
            var newClass = CSCSClass.ClassInstance.AssignIfClass(this, newVar);
            return newVar;
        }

        public static Variable NewEmpty()
        {
            return new Variable();
        }

        public static Variable ConvertToVariable(object obj, Type objectType = null)
        {
            if (obj == null)
            {
                return Variable.EmptyInstance;
            }
            if (obj is Variable)
            {
                return (Variable)obj;
            }
            if (obj is string || obj is char)
            {
                return new Variable(Convert.ToString(obj));
            }
            if (obj is double || obj is float || obj is int || obj is long)
            {
                return new Variable(Convert.ToDouble(obj));
            }
            if (obj is bool)
            {
                return new Variable(((bool)obj));
            }
            if (obj is byte[])
            {
                return new Variable(((byte[])obj));
            }
            if (obj is List<string>)
            {
                return new Variable(((List<string>)obj));
            }
            if (obj is List<double>)
            {
                return new Variable(((List<double>)obj));
            }

            return new Variable(obj, objectType);
        }

        public void Reset()
        {
            m_value = Double.NaN;
            m_string = null;
            m_object = null;
            ObjectType = null;
            m_tuple = null;
            m_byteArray = null;
            Action = null;
            IsReturn = false;
            Type = VarType.NONE;
            m_dictionary.Clear();
            m_keyMappings.Clear();
            m_propertyMap.Clear();
            m_propertyStringMap.Clear();
        }

        public bool Equals(Variable other)
        {
            if (Type != other.Type)
            {
                return false;
            }

            if (Type == VarType.NUMBER && Value == other.Value)
            {
                return true;
            }
            bool stringsEqual = String.Equals(this.String, other.String, StringComparison.Ordinal);
            if (Type == VarType.STRING && stringsEqual)
            {
                return true;
            }
            if (Type == VarType.OBJECT)
            {
                return Object == other.Object;
            }
            if (Type == VarType.BYTE_ARRAY)
            {
                return ByteArray == other.ByteArray;
            }

            if (Double.IsNaN(Value) != Double.IsNaN(other.Value) ||
              (!Double.IsNaN(Value) && Value != other.Value))
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
            if (!stringsEqual)
            {
                return false;
            }
            return AsString() == other.AsString();
        }

        public virtual bool Preprocess()
        {
            return false;
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
            SetAsArray();
            int retValue;
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

        public void TrySetAsMap()
        {
            if (m_tuple == null || m_tuple.Count < 1 ||
                m_dictionary.Count > 0 || m_keyMappings.Count > 0 ||
                m_tuple[0].m_dictionary.Count == 0)
            {
                return;
            }

            for (int i = 0; i < m_tuple.Count; i++)
            {
                var current = m_tuple[i];
                if (current.m_tuple == null || current.m_dictionary.Count == 0)
                {
                    continue;
                }

                var key = current.m_dictionary.First().Key;
                m_keyMappings[key] = current.m_keyMappings[key];
                m_dictionary[key] = i;

                current.m_dictionary.Clear();
                m_tuple[i] = current.m_tuple[0];
            }
        }

        public int RemoveItem(string item)
        {
            string lower = item.ToLower();
            if (m_dictionary.Count > 0)
            {
                int index = 0;
                if (!m_dictionary.TryGetValue(lower, out index))
                {
                    return 0;
                }

                m_tuple.RemoveAt(index);
                m_keyMappings.Remove(lower);
                m_dictionary.Remove(lower);

                // "Rehash" the dictionary so that it points correctly to the indices after removed.
                foreach (var key in m_dictionary.Keys.ToList())
                {
                    int value = m_dictionary[key];
                    if (value > index)
                    {
                        m_dictionary[key] = value - 1;
                    }
                }

                return 1;
            }

            int removed = m_tuple.RemoveAll(p => p.AsString() == item);
            return removed;
        }

        public int GetArrayIndex(Variable indexVar)
        {
            if (this.Type != VarType.ARRAY)
            {
                return -1;
            }

            if (indexVar.Type == VarType.NUMBER)
            {
                Utils.CheckNonNegativeInt(indexVar, null);
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

        public virtual bool AsBool()
        {
            if (Type == VarType.NUMBER && Value != 0.0)
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

        public virtual int AsInt()
        {
            int result = 0;
            if (Type == VarType.NUMBER || Value != 0.0)
            {
                return (int)Value;
            }
            if (Type == VarType.STRING)
            {
                Int32.TryParse(String, out result);
            }

            return result;
        }
        public virtual float AsFloat()
        {
            float result = 0;
            if (Type == VarType.NUMBER || Value != 0.0)
            {
                return (float)Value;
            }
            if (Type == VarType.STRING)
            {
                float.TryParse(String, out result);
            }

            return result;
        }
        public virtual long AsLong()
        {
            long result = 0;
            if (Type == VarType.NUMBER || Value != 0.0)
            {
                return (long)Value;
            }
            if (Type == VarType.STRING)
            {
                long.TryParse(String, out result);
            }
            return result;
        }
        public virtual double AsDouble()
        {
            double result = 0.0;
            if (Type == VarType.NUMBER)
            {// || (Value != 0.0 && Value != Double.NaN)) {
                return Value;
            }
            if (Type == VarType.STRING)
            {
                Double.TryParse(String, out result);
            }

            return result;
        }
        public virtual DateTime AsDateTime()
        {
            return m_datetime;
        }

        public virtual byte[] AsByteArray()
        {
            if (Type == VarType.STRING)
            {
                return Encoding.Unicode.GetBytes(m_string);
            }
            return m_byteArray;
        }
        public override string ToString()
        {
            return AsString();
        }

        public object AsObject()
        {
            switch (Type)
            {
                case VarType.NUMBER: return AsDouble();
                case VarType.DATETIME: return AsDateTime();
                case VarType.OBJECT: return Object;
                case VarType.ARRAY:
                case VarType.ARRAY_NUM:
                case VarType.ARRAY_STR:
                    var list = new List<object>();
                    for (int i = 0; i < m_tuple.Count; i++)
                    {
                        list.Add(m_tuple[i].AsObject());
                    }
                    return list;
                case VarType.NONE:
                    return null;
            }
            return AsString();
        }

        public virtual string AsString(string format)
        {
            if (Type == VarType.DATETIME && !string.IsNullOrWhiteSpace(format))
            {
                return DateTime.ToString(format);
            }

            return AsString();
        }

        public virtual string AsString(bool isList = true,
                                       bool sameLine = true,
                                       int maxCount = -1)
        {
            var result = BaseAsString();
            if (result != null)
            {
                return result;
            }
            StringBuilder sb = new StringBuilder();
            if (isList)
            {
                sb.Append(Constants.START_ARRAY.ToString() +
                         (sameLine ? "" : Environment.NewLine));
            }

            int count = maxCount < 0 ? m_tuple.Count : Math.Min(maxCount, m_tuple.Count);
            int i = 0;
            HashSet<int> arrayKeys = new HashSet<int>();
            if (m_dictionary.Count > 0)
            {
                count = maxCount < 0 ? m_dictionary.Count : Math.Min(maxCount, m_dictionary.Count);
                foreach (KeyValuePair<string, int> entry in m_dictionary)
                {
                    if (entry.Value >= 0 && entry.Value < m_tuple.Count)
                    {
                        var quote = m_tuple[entry.Value].Type == VarType.STRING ? "\"" : "";
                        string value = quote + m_tuple[entry.Value].AsString(isList, sameLine, maxCount) + quote;
                        string realKey = entry.Key;
                        m_keyMappings.TryGetValue(entry.Key.ToLower(), out realKey);
                        arrayKeys.Add(entry.Value);

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
                    else
                    {
                        Console.WriteLine("Error condition: dictionary value {0} out of bounds {1}", entry.Value, m_tuple.Count);
                    }
                }
            }
            else
            {
                for (; i < count; i++)
                {
                    Variable arg = m_tuple[i];
                    var quote = arg.Type == VarType.STRING ? "\"" : "";
                    sb.Append(quote + arg.AsString(isList, sameLine, maxCount) + quote);
                    if (i != count - 1)
                    {
                        sb.Append(sameLine ? ", " : Environment.NewLine);
                    }
                }
            }
            if (count < m_tuple.Count)
            {
                for (int j = 0; j < m_tuple.Count; j++)
                {
                    if (arrayKeys.Contains(j))
                    {
                        continue;
                    }
                    if (sb.Length > 0)
                    {
                        sb.Append(sameLine ? ", " : Environment.NewLine);
                    }
                    Variable arg = m_tuple[j];
                    var quote = arg.Type == VarType.STRING ? "\"" : "";
                    sb.Append(quote + arg.AsString(isList, sameLine, maxCount) + quote);
                }
                //sb.Append(" ...");
            }
            if (isList)
            {
                sb.Append(Constants.END_ARRAY.ToString() +
                         (sameLine ? "" : Environment.NewLine));
            }

            return sb.ToString();
        }

        public string BaseAsString()
        {
            if (Type == VarType.NUMBER)
            {
                return Value.ToString();
            }
            if (Type == VarType.STRING)
            {
                return m_string == null ? "" : m_string;
            }
            if (Type == VarType.DATETIME)
            {
                var res = "";
                try
                {
                    if (!string.IsNullOrEmpty(m_format))
                    {
                        res = DateTime.ToString(m_format);
                        return res;
                    }
                }
                catch (Exception) { }
                res = DateTime.ToString();
                return res;
            }
            if (Type == VarType.OBJECT)
            {
                return ObjectToString();
            }
            if (Type == VarType.BYTE_ARRAY)
            {
                return Encoding.Unicode.GetString(m_byteArray, 0, m_byteArray.Length);
            }
            if (Type == VarType.UNDEFINED)
            {
                return Constants.UNDEFINED;
            }
            if (Type == VarType.ENUM)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(Constants.START_ARRAY.ToString() + " ");
                foreach (string key in m_propertyMap.Keys)
                {
                    sb.Append(key + " ");
                }
                sb.Append(Constants.END_ARRAY.ToString());
                return sb.ToString();
            }
            if (Type == VarType.NONE || m_tuple == null)
            {
                return string.Empty;
            }

            return null;
        }

        public string GetStringRep()
        {
            var stringRep = BaseAsString();
            var type = ToString(Type);
            if (stringRep != null)
            {
                var quote = Type == VarType.STRING ? "\"" : "";
                return type + ":" + quote + stringRep + quote;
            }

            StringBuilder sb = new StringBuilder(type + ":[");
            for (int i = 0; i < m_tuple.Count; i++)
            {
                var child = m_tuple[i].GetStringRep();
                sb.Append(child);
                if (i != m_tuple.Count - 1)
                {
                    sb.Append(",");
                }
            }
            sb.Append("]");
            GetMapRep(sb);

            return sb.ToString();
        }

        public string GetMapRep(StringBuilder sb)
        {
            if (m_dictionary == null || m_dictionary.Count == 0)
            {
                return "";
            }

            sb.Append("MAP:[");
            int count = 0;
            foreach (var entry in m_dictionary)
            {
                if (!m_keyMappings.TryGetValue(entry.Key, out string key))
                {
                    key = entry.Key;
                }
                sb.Append('"' + key + "\":" + entry.Value);
                if (count++ != m_dictionary.Count - 1)
                {
                    sb.Append(",");
                }
            }
            sb.Append("]");
            return sb.ToString();
        }

        public string Marshal(string name)
        {
            var stringRep = GetStringRep();
            var result = "<" + name + ":" + stringRep + ">";
            return result;
        }

        public static Variable Unmarshal(string type, string source, ref int pointer)
        {
            var propStr = Utils.GetNextToken(source, ref pointer);
            var varValue = UnmarshalVariable(type, propStr);
            return varValue;
        }

        public static Variable UnmarshalVariable(string varType, string varStr)
        {
            switch (varType.ToLower())
            {
                case "num":
                    double.TryParse(varStr, out double number);
                    return new Variable(number);
                case "obj":
                case "map":
                case "arr":
                    int pointer = 0;
                    var result = new Variable(VarType.ARRAY);
                    while (pointer < varStr.Length)
                    {
                        var tmp1 = varStr.Substring(pointer);
                        var propType = Utils.GetNextToken(varStr, ref pointer, ':', '[', ']');
                        if (propType.StartsWith(","))
                        {
                            propType = propType.Substring(1);
                        }
                        if (string.IsNullOrWhiteSpace(propType) && pointer >= varStr.Length)
                        {
                            break;
                        }
                        var sep = string.Equals(propType, "arr", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(propType, "obj", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(propType, "map", StringComparison.OrdinalIgnoreCase) ?
                            '\0' : ',';
                        var tmp2 = varStr.Substring(pointer);
                        var propData = Utils.GetNextToken(varStr, ref pointer, sep, '[', ']');
                        if (string.Equals(propType, "map", StringComparison.OrdinalIgnoreCase))
                        {
                            result.UnmarshalMap(propData);
                        }
                        else
                        {
                            var child = UnmarshalVariable(propType, propData);
                            result.Tuple.Add(child);
                        }
                    }
                    return result;
            }

            var str = varStr.StartsWith("\"") && varStr.Length >= 2 ?
                varStr.Substring(1, varStr.Length - 2) : varStr;
            return new Variable(str);
        }

        public void UnmarshalMap(string mapData)
        {
            if (string.IsNullOrWhiteSpace(mapData))
            {
                return;
            }
            Type = VarType.ARRAY;
            if (m_dictionary == null)
            {
                m_dictionary = new Dictionary<string, int>();
            }
            if (m_keyMappings == null)
            {
                m_keyMappings = new Dictionary<string, string>();
            }

            int pointer = 0;
            var items = mapData.Split(',');
            while (pointer < mapData.Length)
            {
                var key = Utils.GetNextToken(mapData, ref pointer, ':');
                if (string.IsNullOrWhiteSpace(key) || key.Length < 2)
                {
                    break;
                }
                key = key.Substring(1, key.Length - 2);
                var val = Utils.GetNextToken(mapData, ref pointer, ',');
                if (!int.TryParse(val, out int arrayPtr))
                {
                    return;
                }
                var lower = key.ToLower();
                m_dictionary[lower] = arrayPtr;
                m_keyMappings[lower] = key;
            }
        }

        public static string ToString(VarType type)
        {
            return type.ToString().Substring(0, 3).ToUpper();
        }

        public static VarType ToType(string type)
        {
            switch (type.ToLower())
            {
                case "num":
                    return VarType.NUMBER;
                case "str":
                    return VarType.STRING;
                case "arr":
                    return VarType.ARRAY;
                case "byt":
                    return VarType.BYTE_ARRAY;
                case "dat":
                    return VarType.DATETIME;
                case "enu":
                    return VarType.ENUM;
                case "map":
                    return VarType.MAP_NUM;
                case "obj":
                    return VarType.OBJECT;
                default:
                    return VarType.NONE;
            }
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
                           Constants.START_ARRAY.ToString());

                List<string> allProps = GetAllProperties();
                for (int i = 0; i < allProps.Count; i++)
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
            get
            {
                return Type == VarType.ARRAY ? m_tuple.Count :
                       Type == VarType.NONE ? 0 : 1;
            }
        }

        public int TotalElements()
        {
            return Count;
        }

        public Variable SetProperty(string propName, Variable value, ParsingScript script, string baseName = "")
        {
            int ind = propName.IndexOf('.');
            if (ind > 0)
            { // The case a.b.c = ... is dealt here recursively
                string varName = propName.Substring(0, ind);
                string actualPropName = propName.Substring(ind + 1);
                Variable property = GetProperty(varName);
                Utils.CheckNotNull(property, varName, script);
                return property.SetProperty(actualPropName, value, script, baseName);
            }
            return FinishSetProperty(propName, value, script, baseName);
        }

        public async Task<Variable> SetPropertyAsync(string propName, Variable value, ParsingScript script, string baseName = "")
        {
            int ind = propName.IndexOf('.');
            if (ind > 0)
            { // The case a.b.c = ... is dealt here recursively
                string varName = propName.Substring(0, ind);
                string actualPropName = propName.Substring(ind + 1);
                Variable property = await GetPropertyAsync(varName);
                Utils.CheckNotNull(property, varName, script);
                Variable result = await property.SetPropertyAsync(actualPropName, value, script, baseName);
                return result;
            }
            return FinishSetProperty(propName, value, script, baseName);
        }

        string GetRealName(string name)
        {
            string converted = Constants.ConvertName(name);
            if (!m_propertyStringMap.TryGetValue(converted, out string realName))
            {
                realName = name;
            }
            return realName;
        }

        public Variable FinishSetProperty(string propName, Variable value, ParsingScript script, string baseName = "")
        {
            Variable reflectedProp = SetReflectedProperty(propName, value);
            if (reflectedProp != null)
                return reflectedProp;
            Variable result = Variable.EmptyInstance;

            // Check for an existing custom setter
            if ((m_propertyMap.TryGetValue(propName, out result) ||
                m_propertyMap.TryGetValue(GetRealName(propName), out result)))
            {
                if (!result.Writable)
                {
                    Utils.ThrowErrorMsg("Property [" + propName + "] is not writable.",
                        script, propName);
                }
                if (result.CustomFunctionSet != null)
                {
                    var args = new List<Variable> { value };
                    result.CustomFunctionSet.Run(args, script);
                    return result;
                }
                if (!string.IsNullOrWhiteSpace(result.CustomSet))
                {
                    return ParsingScript.RunString(script.InterpreterInstance, result.CustomSet);
                }
            }

            m_propertyMap[propName] = value;

            string converted = Constants.ConvertName(propName);
            m_propertyStringMap[converted] = propName;

            Type = VarType.OBJECT;

            if (Object is ScriptObject)
            {
                ScriptObject obj = Object as ScriptObject;
                result = obj.SetProperty(propName, value).Result;
            }
            return result;
        }

        public void SetEnumProperty(string propName, Variable value, string baseName = "")
        {
            m_propertyMap[propName] = value;

            string converted = Constants.ConvertName(propName);
            m_propertyStringMap[converted] = propName;

            if (m_enumMap == null)
            {
                m_enumMap = new Dictionary<int, string>();
            }
            m_enumMap[value.AsInt()] = propName;
        }

        public Variable GetEnumProperty(string propName, ParsingScript script, string baseName = "")
        {
            propName = Constants.ConvertName(propName);
            if (script.Prev == Constants.START_ARG)
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
                    var args = GetArgs(script);
                    var task = obj.GetProperty(match, args, script);
                    result = task != null ? task.Result : null;
                    if (result != null)
                    {
                        return result;
                    }
                }
            }

            return GetCoreProperty(propName, script);
        }

        List<Variable> GetArgs(ParsingScript script)
        {
            List<Variable> args = null;
            if (script != null)
            {
                if (script.Pointer == 0 || script.Prev == Constants.START_ARG)
                {
                    args = script.GetFunctionArgs();
                }
                else
                {
                    args = new List<Variable>();
                }
            }

            return args;
        }

        Variable SetReflectedProperty(string propName, Variable value)
        {
            if (Object == null)
                return null;

            BindingFlags bf = BindingFlags.Instance;

            Type t;
            if (Object is Type ot)
            {
                t = ot;
                bf = BindingFlags.Static;
            }
            else
                t = ObjectType;

            var property = FindNestedMatchingProperty(t, propName, bf | BindingFlags.Public | BindingFlags.SetProperty);

            if (property != null)
            {
                property.SetValue(Object, ParameterConverter.ChangeType(value.AsObject(), property.PropertyType));
                return value;
            }

            return null;
        }

        Variable GetReflectedProperty(string propName, ParsingScript script)
        {
            if (Object == null)
                return null;

            Type t = Object is Type ot ?
                ot : ObjectType;
            BindingFlags bf = Object is Type ?
                BindingFlags.Static : BindingFlags.Instance;
            bf |= BindingFlags.Public;

            var property = FindNestedMatchingProperty(t, propName, bf | BindingFlags.GetProperty);

            if (property != null)
            {
                object val = property.GetValue(Object);
                return ConvertToVariable(val, property.PropertyType);
            }

            // TODO: If we couldn't find the property, there is other code I could write
            // that uses custom attributes, DefaultMemberAttribute. I'm not sure that
            // the syntax of the scripting language would get that to this code, and
            // so I'm not going to implement it yet.

            if (script != null)
            {
                int startPointer = script.Pointer;
                List<Variable> args = GetArgs(script);
                ParameterConverter pConv = new ParameterConverter();

                MethodInfo bestMethod = pConv.FindBestMethod(t, propName, args, bf);
                if (pConv.BestConversion != ParameterConverter.Conversion.Exact)
                {
                    if (t.IsInterface)
                    {
                        foreach (Type implementedInterface in t.GetInterfaces())
                        {
                            var newMethod = pConv.FindBestMethod(implementedInterface, propName, args, bf);
                            if (newMethod != null)
                            {
                                bestMethod = newMethod;
                                if (pConv.BestConversion == ParameterConverter.Conversion.Exact)
                                    break;
                            }
                        }
                    }
                }

                if (bestMethod != null)
                {
                    object res = bestMethod.Invoke(Object, pConv.BestTypedArgs);
                    return ConvertToVariable(res, bestMethod.ReturnType);
                }

                script.Pointer = startPointer;
            }

            return null;
        }

        PropertyInfo FindNestedMatchingProperty(Type t, string propName, BindingFlags bf)
        {
            var property = FindMatchingProperty(t, propName, bf);
            if (property == null && t.IsInterface)
            {
                foreach (Type implementedInterface in t.GetInterfaces())
                {
                    property = FindMatchingProperty(implementedInterface, propName, bf);
                    if (property != null)
                        break;
                }
            }
            return property;
        }

        PropertyInfo FindMatchingProperty(Type t, string propName, BindingFlags bf)
        {
            var properties = t.GetProperties(bf);
            if (properties != null)
            {
                foreach (var property in properties)
                {
                    if (String.Compare(property.Name, propName, true) == 0)
                        return property;
                }
            }

            return null;
        }

        public class ParameterConverter
        {
            public Conversion BestConversion { get; private set; }
            public object[] BestTypedArgs { get; private set; }

            public ParameterConverter()
            {
                BestConversion = Conversion.Mismatch;
            }

            public MethodInfo FindBestMethod(Type t, string propName, List<Variable> args, BindingFlags bf)
            {
                MethodInfo bestMethod = null;

                var methods = t.GetMethods(bf);

                if (methods != null)
                {
                    foreach (var method in methods)
                    {
                        if (String.Compare(method.Name, propName, true) == 0)
                        {
                            var parameters = method.GetParameters();
                            if (ConvertVariablesToTypedArgs(args, parameters))
                            {
                                bestMethod = method;
                                if (BestConversion == Conversion.Exact)
                                    break;
                            }
                        }
                    }
                }
                return bestMethod;
            }

            public bool ConvertVariablesToTypedArgs(List<Variable> args, ParameterInfo[] parameters)
            {
                if (args.Count == parameters.GetLength(0))
                {
                    if (args.Count == 0)
                    {
                        BestConversion = Conversion.Exact;
                        return true;
                    }
                    object[] typedArgs = new object[args.Count];
                    Conversion thisConversion = ChangeTypes(args, parameters, typedArgs);
                    if (thisConversion < BestConversion || BestTypedArgs == null)
                    {
                        BestTypedArgs = typedArgs;
                        BestConversion = thisConversion;
                        return true;
                    }
                }
                return false;
            }

            public enum Conversion
            {
                Exact,
                Assignable,
                Convertible,
                Mismatch
            }

            public static Conversion ChangeTypes(List<Variable> args, ParameterInfo[] parameters, object[] typedArgs)
            {
                Conversion worstConversion = Conversion.Exact;
                if (args.Count > 0)
                {
                    for (int arg = 0; arg < args.Count; ++arg)
                    {
                        typedArgs[arg] = ChangeType(args[arg].AsObject(), parameters[arg].ParameterType, out Conversion conversion);
                        if (conversion > worstConversion)
                            worstConversion = conversion;
                    }
                }
                return worstConversion;
            }

            public static object ChangeType(object value, Type conversionType)
            {
                return ChangeType(value, conversionType, out Conversion conversion);
            }

            public static object ChangeType(object value, Type conversionType, out Conversion conversion)
            {
                try
                {
                    Type underlyingType = Nullable.GetUnderlyingType(conversionType);

                    if (value == null)
                    {
                        if (underlyingType == null && conversionType.IsValueType)
                        {
                            conversion = Conversion.Mismatch;
                        }
                        else
                        {
                                conversion = Conversion.Exact;
                        }

                        return value;
                    }

                    Type t = value.GetType();
                    if (t == conversionType)
                    {
                        conversion = Conversion.Exact;
                        return value;
                    }

                    if (t == underlyingType)
                    {
                        conversion = Conversion.Convertible;
                        return value;
                    }

                    if (conversionType.IsAssignableFrom(t))
                    {
                        conversion = Conversion.Assignable;
                        return value;
                    }

                    if (underlyingType != null && underlyingType.IsAssignableFrom(t))
                    {
                        conversion = Conversion.Convertible;
                        return value;
                    }

                    conversion = Conversion.Convertible;
                    if (conversionType.IsEnum)
                    {
                        if (value is string svalue)
                        {
                             return Enum.Parse(conversionType, svalue, true);
                        }

                        if (value is double dvalue)
                        {
                            return Enum.ToObject(conversionType, (long)dvalue);
                        }

                        // Let's see if it's some other type that just happens to work
                        conversion = Conversion.Mismatch;
                        return Enum.ToObject(conversionType, value);
                    }

                    IList genericList = ConvertToGenericList(value, conversionType);
                    if (genericList != null)
                    {
                        AddToGenericList(genericList, value);
                        // Do we need to call ChangeType on this again to convert it? It seems to work without that.
                        return genericList;
                    }

                    try
                    {
                        return Convert.ChangeType(value, conversionType);
                    }

                    catch (Exception)
                    {
                        if (underlyingType != null)
                        {
                            return Convert.ChangeType(value, underlyingType);
                        }
                    }
                }
                catch (InvalidCastException)
                {
                }
                catch (FormatException)
                {
                }
                catch
                {
                }
                conversion = Conversion.Mismatch;
                return value;
            }

            private static IList ConvertToGenericList(object value, Type conversionType)
            {
                // 1) Check if both types are generic and have 1 parameter
                Type valueType = value.GetType();
                if (!valueType.IsGenericType || !conversionType.IsGenericType)
                    return null;
                if (valueType.GenericTypeArguments.Length != 1 || conversionType.GenericTypeArguments.Length != 1)
                    return null;

                // 2) Check if both types support IEnumerable
                Type iEnumerableType = typeof(IEnumerable);
                if (!iEnumerableType.IsAssignableFrom(valueType) || !iEnumerableType.IsAssignableFrom(conversionType))
                    return null;

                // 3) Create an instance of the target type
                Type emptyGenericListType = typeof(List<>);
                Type genericListType = emptyGenericListType.MakeGenericType(conversionType.GenericTypeArguments);
                object genericList = Activator.CreateInstance(genericListType);

                return genericList as IList;
            }

            private static void AddToGenericList(IList genericList, object value)
            {
                Type genericListType = genericList.GetType();
                if (genericListType.GenericTypeArguments.Length != 1)
                    return;         // TODO: Throw an exception?
                Type itemType = genericListType.GenericTypeArguments[0];

                if (value is IEnumerable enumValue)
                {
                    foreach (object item in enumValue)
                        genericList.Add(ChangeType(item, itemType));
                }
            }

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
                    if (script != null &&
                       (script.Pointer == 0 || script.Prev == Constants.START_ARG))
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

        bool ProcessForEach(ParsingScript script)
        {
            var token = Utils.GetNextToken(script, true);
            Utils.CheckNotEmpty(token, Constants.FOREACH);

            CustomFunction customFunc = Utils.GetFunction(script, "", token);
            script.MoveForwardIf(Constants.END_ARG);

            if (customFunc == null)
            {
                customFunc = script.InterpreterInstance.GetFunction(token) as CustomFunction;
            }
            if (customFunc == null)
            {
                Utils.ThrowErrorMsg("No function found for [" + Constants.FOREACH + "].",
                                    script, token);
            }
            if (Tuple == null)
            {
                Utils.ThrowErrorMsg("No array found for [" + Constants.FOREACH + "].",
                                    script, token);
            }

            var args = script.InterpreterInstance.VariablesSnaphot(script);
            string propArg = customFunc.RealArgs[0];
            List<Variable> funcArgs = new List<Variable>();

            int index = 0;
            foreach (var item in Tuple)
            {
                funcArgs.Clear();
                funcArgs.Add(item);
                funcArgs.Add(new Variable(index++));
                funcArgs.Add(this);
                customFunc.Run(funcArgs, script);
            }
            return true;
        }

        Variable GetCoreProperty(string propName, ParsingScript script = null)
        {
            Variable reflectedProp = GetReflectedProperty(propName, script);
            if (reflectedProp != null)
                return reflectedProp;
            Variable result = Variable.EmptyInstance;

            if (m_propertyMap.TryGetValue(propName, out result) ||
                m_propertyMap.TryGetValue(GetRealName(propName), out result))
            {
                return result;
            }
            else if (propName.Equals(Constants.OBJECT_PROPERTIES, StringComparison.OrdinalIgnoreCase))
            {
                return new Variable(GetProperties());
            }
            else if (propName.Equals(Constants.OBJECT_TYPE, StringComparison.OrdinalIgnoreCase))
            {
                return new Variable(GetTypeString());
            }
            else if (propName.Equals(Constants.SIZE, StringComparison.OrdinalIgnoreCase))
            {
                return new Variable(GetSize());
            }
            else if (propName.Equals(Constants.LENGTH, StringComparison.OrdinalIgnoreCase))
            {
                return new Variable(GetLength());
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
                string param = Utils.GetSafeString(args, 2, "no_case");
                StringComparison comp = param.Equals("case", StringComparison.OrdinalIgnoreCase) ?
                    StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;

                return new Variable(AsString().IndexOf(search, startFrom, comp));
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
            else if (script != null && propName.Equals(Constants.FOREACH, StringComparison.OrdinalIgnoreCase))
            {
                ProcessForEach(script);
                return this;
            }
            else if (script != null && propName.Equals(Constants.SPLIT, StringComparison.OrdinalIgnoreCase))
            {
                List<Variable> args = script.GetFunctionArgs();
                string sep = Utils.GetSafeString(args, 0, " ");
                var option = Utils.GetSafeString(args, 1);
                var max = Utils.GetSafeInt(args, 2, int.MaxValue - 1);

                var data = AsString();
                var candidate = TokenizeFunction.Tokenize(data, sep, option, max);
                var splitResult = Interpreter.TryExtractArray(candidate, data, script);
                return splitResult;
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
                int index = Utils.GetSafeInt(args, 1, -1);

                if (Tuple != null)
                {
                    if (index >= 0)
                    {
                        Tuple.Insert(index, var);
                    }
                    else
                    {
                        Tuple.Add(var);
                    }
                }
                else if (Type == VarType.NUMBER)
                {
                    Value += var.AsDouble();
                }
                else if (Type == VarType.DATETIME)
                {
                    DateTime = DateTimeFunction.Add(DateTime, var.AsString());
                }
                else
                {
                    String += var.AsString();
                }
                return this;
            }
            else if (script != null && propName.Equals(Constants.ADD_UNIQUE, StringComparison.OrdinalIgnoreCase))
            {
                List<Variable> args = script.GetFunctionArgs();
                Utils.CheckArgs(args.Count, 1, propName);

                Variable var = Utils.GetSafeVariable(args, 0);
                string comp = var.AsString();
                int index = Utils.GetSafeInt(args, 1, -1);

                bool containsItem = m_tuple != null && m_tuple.Any(item => item.AsString() == comp);

                if (!containsItem)
                {
                    if (index >= 0)
                    {
                        m_tuple.Insert(index, var);
                    }
                    else
                    {
                        m_tuple.Add(var);
                    }
                    return new Variable(true);
                }
                return new Variable(false);
            }
            else if (script != null && propName.Equals(Constants.REMOVE_AT, StringComparison.OrdinalIgnoreCase))
            {
                List<Variable> args = script.GetFunctionArgs();
                Utils.CheckArgs(args.Count, 1, propName);
                int index = Utils.GetSafeInt(args, 0);

                int removed = 0;
                if (m_dictionary.Count == 0 && m_tuple != null && m_tuple.Count > index && index >= 0)
                {
                    m_tuple.RemoveAt(index);
                    removed = 1;
                }

                return new Variable(removed);
            }
            else if (script != null && propName.Equals(Constants.REMOVE_ITEM, StringComparison.OrdinalIgnoreCase))
            {
                List<Variable> args = script.GetFunctionArgs();
                Utils.CheckArgs(args.Count, 1, propName);
                string oldVal = Utils.GetSafeString(args, 0);

                int removed = RemoveItem(oldVal);
                return new Variable(removed);
            }
            else if (script != null && propName.Equals(Constants.DEEP_COPY, StringComparison.OrdinalIgnoreCase))
            {
                script.GetFunctionArgs();
                return DeepClone();
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
            else if (propName.Equals(Constants.EMPTY_WHITE, StringComparison.OrdinalIgnoreCase))
            {
                bool isEmpty = string.IsNullOrWhiteSpace(AsString());
                return new Variable(isEmpty);
            }
            else if (script != null && propName.Equals(Constants.REPLACE_TRIM, StringComparison.OrdinalIgnoreCase))
            {
                List<Variable> args = script.GetFunctionArgs();
                Utils.CheckArgs(args.Count, 2, propName);
                string currentValue = AsString();

                for (int i = 0; i < args.Count; i += 2)
                {
                    string oldVal = Utils.GetSafeString(args, i);
                    string newVal = Utils.GetSafeString(args, i + 1);
                    currentValue = currentValue.Replace(oldVal, newVal);
                }

                return new Variable(currentValue.Trim());
            }
            else if (script != null && propName.Equals(Constants.CONTAINS, StringComparison.OrdinalIgnoreCase))
            {
                List<Variable> args = script.GetFunctionArgs();
                Utils.CheckArgs(args.Count, 1, propName);
                string val = Utils.GetSafeString(args, 0);
                string param = Utils.GetSafeString(args, 1, "no_case");
                StringComparison comp = param.Equals("case", StringComparison.OrdinalIgnoreCase) ?
                    StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;

                bool contains = false;
                if (Type == Variable.VarType.ARRAY)
                {
                    string lower = val.ToLower();
                    contains = m_dictionary != null && m_dictionary.ContainsKey(lower);
                    if (!contains && m_tuple != null)
                    {
                        foreach (var item in m_tuple)
                        {
                            if (item.AsString().Equals(val, comp))
                            {
                                contains = true;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    contains = val != "" && AsString().IndexOf(val, comp) >= 0;
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
            int size = Type == Variable.VarType.ARRAY ? Tuple.Count : 0;
            return size;
        }

        public int GetLength()
        {
            int len = Type == Variable.VarType.ARRAY ?
                  Tuple.Count : AsString().Length;
            return len;
        }

        public virtual string GetTypeString()
        {
            if (Type == VarType.OBJECT && Object != null)
            {
                var result = ObjectType.ToString();
                var instance = Object as CSCSClass.ClassInstance;
                if (instance != null)
                {
                    result += ": " + instance.CscsClass.OriginalName;
                }
                return result;
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

        public virtual void AddToDate(Variable valueB, int sign)
        {
            var dt = AsDateTime();
            if (valueB.Type == Variable.VarType.NUMBER)
            {
                var delta = valueB.Value * sign;
                if (dt.Date == DateTime.MinValue)
                {
                    DateTime = dt.AddSeconds(delta);
                }
                else
                {
                    DateTime = dt.AddDays(delta);
                }
            }
            else if (valueB.Type == Variable.VarType.DATETIME)
            {
                if (dt.Date == DateTime.MinValue)
                {
                    if (sign < 0)
                    {
                        Value = DateTime.Subtract(valueB.DateTime).TotalSeconds;
                    }
                    else
                    {
                        DateTime = DateTime.AddSeconds(valueB.DateTime.Second);
                    }
                }
                else
                {
                    if (sign < 0)
                    {
                        Value = DateTime.Subtract(valueB.DateTime).TotalDays;
                    }
                    else
                    {
                        DateTime = DateTime.AddDays(valueB.DateTime.Day);
                    }
                }
            }
            else
            {
                char ch = sign > 0 ? '+' : '-';
                DateTime = DateTimeFunction.Add(DateTime, ch + valueB.AsString());
            }
        }

        public virtual double Value
        {
            get { return m_value; }
            set { m_value = value; Type = VarType.NUMBER; }
        }

        public virtual string String
        {
            get { return m_string; }
            set { m_string = value; Type = VarType.STRING; }
        }
        public virtual string Format
        {
            get { return m_format; }
            set { m_format = value; }
        }

        public object Object
        {
            get { return m_object; }
            set
            {
                m_object = value;
                Type = VarType.OBJECT;
            }
        }

        Type _objectType;
        public Type ObjectType
        {
            get
            {
                if (Type == VarType.OBJECT)
                    return _objectType;
                return AsObject()?.GetType();
            }
            set
            {
                _objectType = value;
            }
        }

        public DateTime DateTime
        {
            get { return m_datetime; }
            set { m_datetime = value; Type = VarType.DATETIME; }
        }

        public byte[] ByteArray
        {
            get { return m_byteArray; }
            set { m_byteArray = value; Type = VarType.BYTE_ARRAY; }
        }

        public string Pointer
        {
            get;
            set;
        } = null;

        public CustomFunction CustomFunctionGet
        {
            get { return m_customFunctionGet; }
            set { m_customFunctionGet = value; }
        }
        public CustomFunction CustomFunctionSet
        {
            get { return m_customFunctionSet; }
            set { m_customFunctionSet = value; }
        }

        public List<Variable> Tuple
        {
            get { return m_tuple; }
            set { m_tuple = value; Type = VarType.ARRAY; }
        }

        public string Action { get; set; }
        public VarType Type { get; set; }
        public OriginalType Original { get; set; }
        public bool IsReturn { get; set; }
        public string ParsingToken { get; set; }
        public int Index { get; set; }
        public string CurrentAssign { get; set; } = "";
        public string ParamName { get; set; } = "";

        public bool Writable { get; set; } = true;
        public bool Enumerable { get; set; } = true;
        public bool Configurable { get; set; } = true;

        public string CustomGet { get; set; }
        public string CustomSet { get; set; }

        public List<Variable> StackVariables { get; set; }

        public static Variable EmptyInstance = new Variable();
        public static Variable Undefined = new Variable(VarType.UNDEFINED);

        public virtual Variable Default()
        {
            return EmptyInstance;
        }

        protected double m_value;
        protected string m_string;
        protected object m_object;
        protected DateTime m_datetime;
        protected string m_format;
        CustomFunction m_customFunctionGet;
        CustomFunction m_customFunctionSet;
        protected List<Variable> m_tuple;
        protected byte[] m_byteArray;
        Dictionary<string, int> m_dictionary = new Dictionary<string, int>();
        Dictionary<string, string> m_keyMappings = new Dictionary<string, string>();
        Dictionary<string, string> m_propertyStringMap = new Dictionary<string, string>();

        Dictionary<string, Variable> m_propertyMap = new Dictionary<string, Variable>();
        Dictionary<int, string> m_enumMap;

        //Dictionary<string, Func<ParsingScript, Variable, string, Variable>> m_properties = new Dictionary<string, Func<ParsingScript, Variable, string, Variable>>();
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
