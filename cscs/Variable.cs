using System;
using System.Globalization;
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
            ID = ++GlobalID;
            Reset();
        }
        public Variable(VarType type)
        {
            ID = ++GlobalID;
            Type = type;
            if (Type == VarType.ARRAY)
            {
                SetAsArray();
            }
        }
        public Variable(double d)
        {
            ID = ++GlobalID;
            Value = d;
            Original = OriginalType.DOUBLE;
        }
        public Variable(int d)
        {
            ID = ++GlobalID;
            Value = d;
            Original = OriginalType.INT;
        }
        public Variable(long d)
        {
            ID = ++GlobalID;
            Value = d;
            Original = OriginalType.LONG;
        }
        public Variable(bool d)
        {
            ID = ++GlobalID;
            Value = d ? 1.0 : 0.0;
            Original = OriginalType.BOOL;
        }
        public Variable(string s)
        {
            ID = ++GlobalID;
            String = s;
            Original = OriginalType.STRING;
        }
        public Variable(DateTime dt)
        {
            ID = ++GlobalID;
            DateTime = dt;
            Original = OriginalType.DATE_TIME;
        }
        public Variable(byte[] ba)
        {
            ID = ++GlobalID;
            ByteArray = ba;
            Original = OriginalType.BYTE_ARRAY;
        }
        public Variable(List<Variable> a)
        {
            ID = ++GlobalID;
            this.Tuple = a;
            Original = OriginalType.ARRAY;
        }
        public Variable(List<string> a)
        {
            ID = ++GlobalID;
            List<Variable> tuple = new List<Variable>(a.Count);
            for (int i = 0; i < a.Count; i++)
            {
                var son = new Variable(a[i]);
                son.Parent = this;
                tuple.Add(son);
            }
            this.Tuple = tuple;
            Original = OriginalType.ARRAY;
        }
        public Variable(List<double> a)
        {
            ID = ++GlobalID;
            List<Variable> tuple = new List<Variable>(a.Count);
            for (int i = 0; i < a.Count; i++)
            {
                var son = new Variable(a[i]);
                son.Parent = this;
                tuple.Add(son);
            }
            this.Tuple = tuple;
            Original = OriginalType.ARRAY;
        }
        public Variable(Dictionary<string, string> a)
        {
            ID = ++GlobalID;
            List<Variable> tuple = new List<Variable>(a.Count);
            foreach (string key in a.Keys)
            {
                string lower = key.ToLower();
                m_keyMappings[lower] = key;
                m_dictionary[lower] = tuple.Count;
                var son = new Variable(a[key]);
                son.Parent = this;
                tuple.Add(son);
            }
            this.Tuple = tuple;
            Original = OriginalType.ARRAY;
        }
        public Variable(Dictionary<string, double> a)
        {
            ID = ++GlobalID;
            List<Variable> tuple = new List<Variable>(a.Count);
            foreach (string key in a.Keys)
            {
                string lower = key.ToLower();
                m_keyMappings[lower] = key;
                m_dictionary[lower] = tuple.Count;
                var son = new Variable(a[key]);
                son.Parent = this;
                tuple.Add(son);
            }
            this.Tuple = tuple;
            Original = OriginalType.ARRAY;
        }

        public Variable(object o, Type t = null)
        {
            ID = ++GlobalID;
            Object = o;
            Original = OriginalType.OBJECT;
            ObjectType = t == null ? o?.GetType() : t;
        }

        /// <summary>
        /// Indexers so precompiled code can write a[i] and m["key"] the way the script does.
        /// Both delegate to GetVariable, so out-of-range and missing keys behave exactly as
        /// they do in the interpreter rather than throwing.
        /// </summary>
        public Variable this[int index]
        {
            get { return GetVariable(index); }
        }

        public Variable this[string key]
        {
            get { return GetVariable(key); }
        }

        /// <summary>
        /// Loop counters in precompiled code are declared double so that "/" is not integer
        /// division, so a subscript arrives as one. Truncates exactly as the interpreter's
        /// GetArrayIndex does.
        /// </summary>
        public Variable this[double index]
        {
            get { return GetVariable((int)index); }
        }

        /// <summary>
        /// Indexing by a Variable, which is what "m[keys[i]]" produces: the subscript is
        /// itself read out of a collection, so its type is only known at run time. Dispatches
        /// the way the interpreter does -- a number indexes the tuple, anything else is a key.
        /// </summary>
        public Variable this[Variable index]
        {
            get
            {
                if (index == null)
                {
                    return EmptyInstance;
                }
                return index.Type == VarType.NUMBER ?
                    GetVariable((int)index.Value) : GetVariable(index.AsString());
            }
        }

        public virtual Variable Clone()
        {
            Variable newVar = (Variable)this.MemberwiseClone();
            newVar.ID = ++GlobalID;
            return newVar;
        }

        public virtual Variable DeepClone(string newName = "")
        {
            Variable newVar = (Variable)this.MemberwiseClone();
            newVar.ID = ++GlobalID;
            if (Type == VarType.ARRAY && m_tuple != null)
            {
                List<Variable> newTuple = new List<Variable>();
                foreach (var item in m_tuple)
                {
                    var son = item.DeepClone();
                    son.Parent = newVar;
                    newTuple.Add(son);
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
                listVar.Parent = this;
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

        /// <summary>
        /// Assigns through an index, numeric or string, the way the interpreter does:
        /// GetArrayIndex resolves the key, a key with no index becomes a new map entry, and
        /// a numeric index past the end extends the array. Mirrors ExtendArrayHelper so
        /// compiled and interpreted assignment stay identical.
        /// </summary>
        public void SetVariable(Variable index, Variable value)
        {
            SetAsArray();
            int arrayIndex = GetArrayIndex(index);
            if (arrayIndex < 0)
            {
                SetHashVariable(index.AsString(), value);
                return;
            }
            while (m_tuple.Count <= arrayIndex)
            {
                m_tuple.Add(Variable.NewEmpty());
            }
            value.Parent = this;
            m_tuple[arrayIndex] = value;
        }

        public int SetHashVariable(string hash, Variable var)
        {
            SetAsArray();
            var.Parent = this;
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
                m_tuple[i].Parent = this;
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
            if (m_dictionary.TryGetValue(lower, out ptr) && ptr < m_tuple.Count)
            {
                return ptr;
            }

            int result = -1;
            if (!String.IsNullOrWhiteSpace(indexVar.String) &&
                Int32.TryParse(indexVar.String, out result))
            {
                return result;
            }
            if (m_dictionary.TryGetValue(lower, out ptr) && ptr < m_tuple.Count)
            {
                return ptr;
            }
            return -1;
        }

        public bool ResetHashArrays()
        {
            if ((m_dictionary != null && m_dictionary.Count > 0) || Type != VarType.ARRAY || Tuple == null)
            {
                return false;
            }
            bool result = false;
            // reassign map links from children to the parent
            m_dictionary = new Dictionary<string, int>();
            m_keyMappings = new Dictionary<string, string>();
            for (int i = 0; i < Tuple.Count; i++)
            {
                Variable arg = Tuple[i];
                if (arg.m_dictionary == null || arg.m_dictionary.Count == 0)
                {
                    continue;
                }
                result = true;
                foreach (var kvp in arg.m_dictionary)
                {
                    m_dictionary[kvp.Key] = i;
                }
                foreach (var kvp in arg.m_keyMappings)
                {
                    m_keyMappings[kvp.Key] = kvp.Value;
                }
                if (arg.Type == VarType.ARRAY && arg.Tuple != null && arg.Tuple.Count == 1)
                {
                    arg = arg.Tuple[0];
                    Tuple[i] = arg;
                    Tuple[i].Parent = this;
                }
            }
            for (int i = 0; i < Tuple.Count; i++)
            {
                Variable arg = Tuple[i];
                result = arg.ResetHashArrays() || result;
            }
            return result;
        }

        public void AddVariable(Variable v, int index = -1)
        {
            SetAsArray();
            v.Parent = this;
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
                return DateTime.ToString(format, CultureInfo.InvariantCulture);
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
                        res = DateTime.ToString(m_format, CultureInfo.InvariantCulture);
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
                            child.Parent = result;
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
                    if (propValue != null && propValue.Type != VarType.NONE)
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

        /// <summary>
        /// Scripts spell this "Size", and precompiled code copies the script's spelling
        /// through, so the generated C# needs a member of that name.
        /// </summary>
        /// <summary>
        /// What a script sees for ".Size": the element count of a collection, and 0 for
        /// anything else -- a number, a string, a class instance. Not Count, which answers 1
        /// for those, so "a[0].Size" on a scalar element came back as 1 where the interpreter
        /// says 0. Nothing in the interpreter reads this; it exists for precompiled code.
        /// </summary>
        public int Size
        {
            get { return Type == VarType.ARRAY ? Count : 0; }
        }

        public int Count
        {
            get
            {
                return Type == VarType.ARRAY ? m_tuple.Count :
                       Type == VarType.NONE ? 0 : 1;
            }
        }

        /// <summary>
        /// The "+" precompiled code uses when it cannot know whether a value is a number or a
        /// string until it runs -- an interpreter callback's result, typically. CSCS decides
        /// this from the operands at run time, so translating it to a fixed AsDouble() or
        /// AsString() had to guess, and a wrong guess did not fail to compile, it quietly
        /// changed the answer: "helper(n) + helper(n)" over strings came back as 0.
        ///
        /// Mirrors Parser.MergeNumbers and Parser.MergeStrings exactly: a string on the left
        /// concatenates, and a number on the left concatenates unless the right is a number
        /// too.
        /// </summary>
        public static Variable operator +(Variable left, Variable right)
        {
            if (left == null || right == null)
            {
                return left ?? right ?? Variable.EmptyInstance;
            }
            if (!BothNumbers(left, right))
            {
                return new Variable(left.AsString() + right.AsString());
            }
            return new Variable(left.Value + right.Value);
        }

        public static Variable operator +(Variable left, double right)
        {
            return left + new Variable(right);
        }

        public static Variable operator +(double left, Variable right)
        {
            return new Variable(left) + right;
        }

        public static Variable operator +(Variable left, string right)
        {
            return left + new Variable(right);
        }

        public static Variable operator +(string left, Variable right)
        {
            return new Variable(left) + right;
        }

        /// <summary>
        /// Scripts spell this "Keys", and precompiled code copies the script's spelling
        /// through, so the generated C# needs a member of that name. Same list the
        /// interpreter returns for the Keys property.
        /// </summary>
        public Variable Keys
        {
            get { return new Variable(GetAllKeys()); }
        }

        /// <summary>
        /// The remaining arithmetic and relational operators, for the same reason as "+":
        /// precompiled code holds values whose type is only known at run time -- a global read
        /// back through the interpreter, or a callback's result -- and C# has no operators for
        /// those. Each mirrors Parser.MergeNumbers and Parser.MergeStrings, including the
        /// cases the interpreter refuses: "*" concatenates trimmed strings, while "-", "/" and
        /// "%" on a string raise the same error the interpreter raises.
        /// </summary>
        /// <summary>
        /// Whether the pair takes the numeric path. Parser.MergeCells uses MergeNumbers only
        /// when *both* sides are numbers and sends everything else to MergeStrings, so a
        /// number against a string concatenates and compares as text -- 5 &lt; "abc" is true
        /// there, because "5" sorts before "a". Reading the left side alone got that backwards.
        /// </summary>
        static bool BothNumbers(Variable left, Variable right)
        {
            return left != null && right != null &&
                   left.Type == VarType.NUMBER && right.Type == VarType.NUMBER;
        }

        static Variable Arithmetic(Variable left, Variable right, string action)
        {
            if (left == null || right == null)
            {
                return EmptyInstance;
            }
            if (!BothNumbers(left, right))
            {
                if (action == "*")
                {
                    return new Variable(left.AsString().Trim() + right.AsString().Trim());
                }
                throw new ArgumentException("Can't process operation [" + action + "] on strings.");
            }
            switch (action)
            {
                case "-": return new Variable(left.Value - right.Value);
                case "*": return new Variable(left.Value * right.Value);
                case "/": return new Variable(left.Value / right.Value);
                default: return new Variable(left.Value % right.Value);
            }
        }

        static bool Compare(Variable left, Variable right, string action)
        {
            int order;
            if (!BothNumbers(left, right))
            {
                order = string.Compare(left == null ? "" : left.AsString(),
                                       right == null ? "" : right.AsString());
            }
            else
            {
                double l = left == null ? 0 : left.Value;
                double r = right == null ? 0 : right.Value;
                order = l < r ? -1 : l > r ? 1 : 0;
            }
            switch (action)
            {
                case "<": return order < 0;
                case ">": return order > 0;
                case "<=": return order <= 0;
                // Equality orders by the same rule as the rest: by value when both sides are
                // numbers, by text otherwise. Without these two the default below answered
                // ">=" for them, so "==" would have been true for anything.
                case "==": return order == 0;
                case "!=": return order != 0;
                default: return order >= 0;
            }
        }

        // Unary minus mirrors the interpreter, which negates the numeric field rather than
        // the parsed text: "-a[0]" over the element "7" is -0 there, not -7.
        public static Variable operator -(Variable value)
        {
            return new Variable(-(value == null ? 0 : value.Value));
        }
        // "++" and "--" step the numeric field for the same reason: incrementing the element
        // "7" gives 1 in the interpreter, not 8, because a string's numeric field is 0.
        public static Variable operator ++(Variable value)
        {
            return new Variable((value == null ? 0 : value.Value) + 1);
        }
        public static Variable operator --(Variable value)
        {
            return new Variable((value == null ? 0 : value.Value) - 1);
        }
        public static Variable operator -(Variable left, Variable right) { return Arithmetic(left, right, "-"); }
        public static Variable operator -(Variable left, double right) { return Arithmetic(left, new Variable(right), "-"); }
        public static Variable operator -(double left, Variable right) { return Arithmetic(new Variable(left), right, "-"); }
        public static Variable operator *(Variable left, Variable right) { return Arithmetic(left, right, "*"); }
        public static Variable operator *(Variable left, double right) { return Arithmetic(left, new Variable(right), "*"); }
        public static Variable operator *(double left, Variable right) { return Arithmetic(new Variable(left), right, "*"); }
        public static Variable operator /(Variable left, Variable right) { return Arithmetic(left, right, "/"); }
        public static Variable operator /(Variable left, double right) { return Arithmetic(left, new Variable(right), "/"); }
        public static Variable operator /(double left, Variable right) { return Arithmetic(new Variable(left), right, "/"); }
        public static Variable operator %(Variable left, Variable right) { return Arithmetic(left, right, "%"); }
        public static Variable operator %(Variable left, double right) { return Arithmetic(left, new Variable(right), "%"); }
        public static Variable operator %(double left, Variable right) { return Arithmetic(new Variable(left), right, "%"); }

        public static bool operator <(Variable left, Variable right) { return Compare(left, right, "<"); }
        public static bool operator >(Variable left, Variable right) { return Compare(left, right, ">"); }
        public static bool operator <(Variable left, double right) { return Compare(left, new Variable(right), "<"); }
        public static bool operator >(Variable left, double right) { return Compare(left, new Variable(right), ">"); }
        public static bool operator <(double left, Variable right) { return Compare(new Variable(left), right, "<"); }
        public static bool operator >(double left, Variable right) { return Compare(new Variable(left), right, ">"); }
        public static bool operator <=(Variable left, Variable right) { return Compare(left, right, "<="); }
        public static bool operator >=(Variable left, Variable right) { return Compare(left, right, ">="); }
        public static bool operator <=(Variable left, double right) { return Compare(left, new Variable(right), "<="); }
        public static bool operator >=(Variable left, double right) { return Compare(left, new Variable(right), ">="); }
        public static bool operator <=(double left, Variable right) { return Compare(new Variable(left), right, "<="); }
        public static bool operator >=(double left, Variable right) { return Compare(new Variable(left), right, ">="); }
        // Against a string too: a collection element compared with a literal -- "v > \"grape\""
        // -- is the shape a script writes, and Compare already applies the interpreter's rule
        // of ordering by string.Compare whenever either side is text.
        public static bool operator <(Variable left, string right) { return Compare(left, new Variable(right), "<"); }
        public static bool operator >(Variable left, string right) { return Compare(left, new Variable(right), ">"); }
        public static bool operator <(string left, Variable right) { return Compare(new Variable(left), right, "<"); }
        public static bool operator >(string left, Variable right) { return Compare(new Variable(left), right, ">"); }
        public static bool operator <=(Variable left, string right) { return Compare(left, new Variable(right), "<="); }
        public static bool operator >=(Variable left, string right) { return Compare(left, new Variable(right), ">="); }
        public static bool operator <=(string left, Variable right) { return Compare(new Variable(left), right, "<="); }
        public static bool operator >=(string left, Variable right) { return Compare(new Variable(left), right, ">="); }        // Equality against a number, which "Colors.Green == 1" and "gcount == 1" need: a
        // Variable-valued expression had no "==" at all, so those did not compile.
        //
        // Deliberately not (Variable, Variable): Compare tests "left == null" itself, and the
        // codebase has some 207 "== null" checks on Variable-typed names. An overload for two
        // Variables would reroute every one of them from a reference test into this method --
        // recursively, in Compare's own case. Two Variables keep reference equality, as they
        // do today; the interpreter compares their values through its own path.
        public static bool operator ==(Variable left, double right) { return Compare(left, new Variable(right), "=="); }
        public static bool operator !=(Variable left, double right) { return Compare(left, new Variable(right), "!="); }
        public static bool operator ==(double left, Variable right) { return Compare(new Variable(left), right, "=="); }
        public static bool operator !=(double left, Variable right) { return Compare(new Variable(left), right, "!="); }


        /// <summary>
        /// Whether a value equals a switch label, by the rule the interpreter uses: text is
        /// compared as text and numbers as numbers. Written as a call rather than as an "=="
        /// operator on Variable, which would change what every existing reference comparison
        /// in the interpreter means.
        /// </summary>
        public static bool SameValue(object left, object label)
        {
            // Either side may arrive as a C# value: "a[0].Trim()" is already a string.
            var value = left == null ? null : ConvertToVariable(left);
            if (value == null)
            {
                return false;
            }
            var other = ConvertToVariable(label);
            if (value.Type == VarType.STRING || other.Type == VarType.STRING)
            {
                return string.Compare(value.AsString(), other.AsString()) == 0;
            }
            return value.Value == other.Value;
        }

        /// <summary>
        /// Calls a method on a class instance and returns its result, as a single expression.
        /// Running one needs an argument list -- that is what tells the instance a method is
        /// wanted rather than a property -- so precompiled code cannot simply read the member.
        /// </summary>
        public static Variable CallMethod(Variable instance, string name, params object[] args)
        {
            var target = instance == null ? null : instance.Object as CSCSClass.ClassInstance;
            if (target == null)
            {
                throw new ArgumentException("Not a class instance: [" + name + "]");
            }
            var list = new List<Variable>();
            foreach (var arg in args)
            {
                list.Add(ConvertToVariable(arg));
            }
            return target.GetProperty(name.ToLower(), list).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Builds a map from alternating keys and values, so that precompiled code can put a
        /// map literal where an expression is required -- the right-hand side of "m[k] = {...}",
        /// or an argument. Uses SetHashVariable, which is what the interpreter itself uses, so
        /// key handling is identical.
        /// </summary>
        public static Variable NewMap(params Variable[] keysAndValues)
        {
            var result = new Variable(VarType.ARRAY);
            for (int i = 0; i + 1 < keysAndValues.Length; i += 2)
            {
                result.SetHashVariable(keysAndValues[i].AsString(), keysAndValues[i + 1]);
            }
            return result;
        }

        /// <summary>
        /// Scripts spell these "Upper" and "Lower", and precompiled code copies the script's
        /// spelling through, so the generated C# needs members of those names.
        /// </summary>
        public string Upper
        {
            get { return AsString().ToUpper(); }
        }

        public string Lower
        {
            get { return AsString().ToLower(); }
        }

        // The C# spellings as well: "Upper" is mapped to ToUpper() before it is known whether
        // the receiver is a string or a Variable, and a Variable has to answer either way.
        public string ToUpper()
        {
            return AsString().ToUpper();
        }

        public string ToLower()
        {
            return AsString().ToLower();
        }

        /// <summary>
        /// String members on a value whose type is only known at run time -- an element read
        /// out of a collection, typically. They delegate to CscsStringMembers so that a
        /// collection element behaves exactly as a string variable does, including the
        /// optional "no_case" argument and Substring's clamping.
        /// </summary>
        public bool StartsWith(string what, string mode = "case")
        {
            return AsString().StartsWithCscs(what, mode);
        }

        public bool EndsWith(string what, string mode = "case")
        {
            return AsString().EndsWithCscs(what, mode);
        }

        public int IndexOf(string what, int startFrom = 0, string mode = "case")
        {
            return AsString().IndexOfCscs(what, startFrom, mode);
        }

        public string Substring(int startFrom = 0, int length = int.MaxValue)
        {
            return AsString().SubstringCscs(startFrom, length);
        }

        public string Replace(string what, string with)
        {
            return AsString().Replace(what, with);
        }

        // The interpreter's Trim property is AsString().Trim(); a loop element needs it by
        // that name, "for (p in parts) { r += p.Trim(); }" having nothing to call.
        public string Trim()
        {
            return AsString().Trim();
        }

        /// <summary>
        /// Scripts spell this "Length", and precompiled code copies the script's spelling
        /// through, so the generated C# needs a member of that name. Same rule as the
        /// interpreter's property: the element count of an array, otherwise the length of the
        /// string form -- which is why it is not the same as Size, that being 0 for a string.
        /// </summary>
        public int Length
        {
            get { return GetLength(); }
        }

        /// <summary>
        /// Scripts spell these "First" and "Last", and precompiled code copies the script's
        /// spelling through, so the generated C# needs members of those names. Same rule the
        /// interpreter's properties use: an element for an array, a character for a string.
        /// </summary>
        public Variable First
        {
            get
            {
                if (m_tuple != null && m_tuple.Count > 0)
                {
                    return m_tuple[0];
                }
                return AsString().Length > 0 ? new Variable("" + AsString()[0]) : EmptyInstance;
            }
        }

        public Variable Last
        {
            get
            {
                if (m_tuple != null && m_tuple.Count > 0)
                {
                    return m_tuple[m_tuple.Count - 1];
                }
                return AsString().Length > 0 ?
                    new Variable("" + AsString()[AsString().Length - 1]) : EmptyInstance;
            }
        }

        /// <summary>
        /// Scripts spell this "Contains", and precompiled code copies the script's spelling
        /// through, so the generated C# needs a member of that name. The interpreter's
        /// semantics are mirrored exactly -- case-insensitive, matching either a map key or
        /// any element's string form -- so that compiling a call cannot change its answer.
        /// </summary>
        public bool Contains(Variable what)
        {
            return Contains(what == null ? "" : what.AsString());
        }
        public bool Contains(double what)
        {
            return Contains(new Variable(what).AsString());
        }
        public bool Contains(string what, string mode)
        {
            // The explicit-mode form. Only a string can be matched case-insensitively; a map
            // key is stored lower-cased either way, which the single-argument form handles.
            return Type == VarType.ARRAY ? Contains(what) : AsString().ContainsCscs(what, mode);
        }

        public bool Contains(string what)
        {
            var comp = StringComparison.CurrentCulture;
            if (Type != VarType.ARRAY)
            {
                return what != "" && AsString().IndexOf(what, comp) >= 0;
            }
            // Map keys are stored lower-cased, so this lookup stays case-insensitive however
            // the element scan below compares -- otherwise Contains would disagree with the
            // indexing that put the entry there.
            if (m_dictionary != null && m_dictionary.ContainsKey(what.ToLower()))
            {
                return true;
            }
            if (m_tuple != null)
            {
                foreach (var item in m_tuple)
                {
                    if (item.AsString().Equals(what, comp))
                    {
                        return true;
                    }
                }
            }
            return false;
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
                result = obj.SetProperty(propName, value).GetAwaiter().GetResult();
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
                    // GetAwaiter().GetResult(), not .Result: a method body runs asynchronously, and
                    // .Result wraps whatever it throws in an AggregateException, whose message
                    // became the script's error text -- "One or more errors occurred. (boom)" where
                    // a plain function's "throw \"boom\"" gives "boom". Same blocking, original exception.
                    result = task != null ? task.GetAwaiter().GetResult() : null;
                    if (result != null)
                    {
                        return result;
                    }
                }
            }

            (string purePropName, string rest) = Utils.Extract(propName);
            var propValue = GetCoreProperty(purePropName, script);
            if (!string.IsNullOrWhiteSpace(rest))
            {
                var arrayIndices = Utils.GetArrayIndices(script, propName);
                propValue = Utils.ExtractArrayElement(propValue, arrayIndices, script);
            }
            return propValue;
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
            // Not in a sandboxed host: this is how a script reaches arbitrary .NET methods.
            if (!InterpreterSecurity.AllowDotNet)
                return null;
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
            // Not in a sandboxed host: this is how a script reaches arbitrary .NET methods.
            if (!InterpreterSecurity.AllowDotNet)
                return null;
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

            (string purePropName, string rest) = Utils.Extract(propName);
            var propValue = GetCoreProperty(purePropName, script);
            if (!string.IsNullOrWhiteSpace(rest))
            {
                var arrayIndices = Utils.GetArrayIndices(script, propName);
                propValue = Utils.ExtractArrayElement(propValue, arrayIndices, script);
            }
            return propValue;
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

        /// <summary>
        /// A property may be written as a call: "s.Upper()" as well as "s.Upper". The ones that
        /// take arguments consume their own list -- Substring and IndexOf read it, Sort and
        /// Reverse call GetFunctionArgs purely to eat it -- but a value property like Upper, Size,
        /// Length or First never did, so its "()" was left for whatever came next. On its own that
        /// went unnoticed; in a larger expression the empty group was parsed as one and threw
        /// "Couldn't find variable []", so "s.Upper() == \"AB\"" and "s.Upper() + \"!\"" failed
        /// while "s.Upper" worked. Only an empty pair is taken, so a real argument list is left
        /// for the property that reads it.
        /// </summary>
        Variable GetCoreProperty(string propName, ParsingScript script = null)
        {
            var propertyValue = GetCorePropertyValue(propName, script);
            // Only when the name really was a property. The lookup is also attempted for names
            // it does not know, and eating the parentheses there robs whoever does handle them.
            // A name it does not know comes back as null -- the failed TryGetValue overwrites the
            // default. This first compared against EmptyInstance, which never matched anything:
            // EmptyInstance is a new Variable on every read. The empty-pair test in
            // ConsumeEmptyCall is what kept Replace's arguments safe until then.
            if (propertyValue != null)
            {
                ConsumeEmptyCall(script);
            }
            return propertyValue;
        }

        static void ConsumeEmptyCall(ParsingScript script)
        {
            // The "(" is already gone when this runs -- the token loop takes it as the action
            // character that ended the name "s.Upper" -- so what waits is the argument list
            // itself, starting at the ")". Prev being "(" is what says the list is this
            // property's own: in "print(s.Upper)" the pointer also sits on a ")", but the
            // character before it is the end of the name, and that ")" belongs to print.
            // Same test GetArgs uses for the properties that do read their arguments. Only an
            // immediately empty pair is taken, so a real argument list stays for its own reader.
            if (script != null && script.StillValid() && script.Pointer > 0 &&
                script.Prev == Constants.START_ARG && script.Current == Constants.END_ARG)
            {
                script.Forward();
            }
        }

        Variable GetCorePropertyValue(string propName, ParsingScript script = null)
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
                // IndexOf matches case by default, like the standalone StrIndexOf function
                // and like C#. Pass "no_case" as the third argument for the older behaviour.
                string param = Utils.GetSafeString(args, 2, "case");
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

                var reset = var.ResetHashArrays();

                if (Tuple != null)
                {
                    var.Parent = this;
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
                    var.Parent = this;
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
                // Matches case by default. Pass "no_case" as the second argument for the
                // older behaviour. Map keys are the exception below: they are stored
                // lower-cased, so m["K1"] and m["k1"] are the same entry and Contains has to
                // agree with indexing.
                string param = Utils.GetSafeString(args, 1, "case");
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
                string param = Utils.GetSafeString(args, 1, "case");   // matches case unless told "no_case"
                StringComparison comp = param.Equals("case", StringComparison.OrdinalIgnoreCase) ?
                    StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;

                return new Variable(AsString().Equals(val, comp));
            }
            else if (script != null && propName.Equals(Constants.STARTS_WITH, StringComparison.OrdinalIgnoreCase))
            {
                List<Variable> args = script.GetFunctionArgs();
                Utils.CheckArgs(args.Count, 1, propName);
                string val = Utils.GetSafeString(args, 0);
                string param = Utils.GetSafeString(args, 1, "case");   // matches case unless told "no_case"
                StringComparison comp = param.Equals("case", StringComparison.OrdinalIgnoreCase) ?
                    StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;

                return new Variable(AsString().StartsWith(val, comp));
            }
            else if (script != null && propName.Equals(Constants.ENDS_WITH, StringComparison.OrdinalIgnoreCase))
            {
                List<Variable> args = script.GetFunctionArgs();
                Utils.CheckArgs(args.Count, 1, propName);
                string val = Utils.GetSafeString(args, 0);
                string param = Utils.GetSafeString(args, 1, "case");   // matches case unless told "no_case"
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

        /// <summary>
        /// What a script's Split(...) does, through the interpreter's own tokenizer and with
        /// its defaults. The interpreter also folds an immediate "[i]" into the call; that is
        /// left out because C# indexes the returned collection itself.
        /// </summary>
        public Variable Split(string sep = " ", string option = "", int max = int.MaxValue - 1)
        {
            return TokenizeFunction.Tokenize(AsString(), sep, option, max);
        }

        /// <summary>Reverses a collection in place, as a script's Reverse() does.</summary>
        public void Reverse()
        {
            if (Tuple != null)
            {
                Tuple.Reverse();
            }
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
                var v = new Variable(numbers[i]);
                v.Parent = this;
                newTuple.Add(v);
            }
            for (int i = 0; i < strings.Count; i++)
            {
                var v = new Variable(strings[i]);
                v.Parent = this;
                newTuple.Add(v);
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

        /// <summary>
        /// A new empty value each time. It used to be one shared instance, and a value is
        /// mutable: "m[k] += 1" on a missing key could get it back and update it in place,
        /// after which every "nothing here" in the whole process was the string "1" -- list
        /// literals then ran on past their closing brace into the statements after them.
        /// </summary>
        public static Variable EmptyInstance => new Variable();
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

        public static int GlobalID { get; private set; }
        public int ID { get; private set; }
        public Variable Parent { get; private set; }
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
