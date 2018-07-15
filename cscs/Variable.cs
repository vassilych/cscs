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
            BREAK, CONTINUE
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
            m_tuple = null;
            Action = null;
            IsReturn = false;
            Type = VarType.NONE;
            m_dictionary.Clear();
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
            int count = maxCount < -1 ? m_tuple.Count : Math.Min(maxCount, m_tuple.Count);
            for (int i = 0; i < count; i++)
            {
                Variable arg = m_tuple[i];
                sb.Append(arg.AsString(isList, sameLine, maxCount));
                if (i != m_tuple.Count - 1)
                {
                    sb.Append(sameLine ? " " : Environment.NewLine);
                }
            }
            if (count < m_tuple.Count)
            {
                sb.Append(" ...");
            }
            if (isList)
            {
                sb.Append(Constants.END_GROUP.ToString() +
                         (sameLine ? " " : Environment.NewLine));
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

        public int TotalElements()
        {
            return Type == VarType.ARRAY ? m_tuple.Count : 1;
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

        private double m_value;
        private string m_string;
        private List<Variable> m_tuple;
        private Dictionary<string, int> m_dictionary = new Dictionary<string, int>();
    }
}

