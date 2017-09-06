using System;
using System.Collections.Generic;
using System.Text;

namespace SplitAndMerge
{
  public class Variable
  {
    public enum VarType { NONE, NUMBER, STRING, ARRAY, BREAK, CONTINUE };

    public Variable() {
      Reset();
    }
    public Variable(VarType type) {
      Type = type;
    }
    public Variable(double d) {
      Value = d;
    }
    public Variable(bool d)
    {
      Value = d ? 1.0 : 0.0;
    }
    public Variable(string s) {
      String = s;
    }
    public Variable(List<Variable> a) {
      this.Tuple = a;
    }
    public virtual Variable Clone()
    {
        Variable newVar = new Variable();
        newVar.Copy(this);
        return newVar;
    }
    public virtual void Copy(Variable other) {
      Reset();
      Action       = other.Action;
      Type         = other.Type;
      IsReturn     = other.IsReturn;
      m_dictionary = other.m_dictionary;

      switch (other.Type) {
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
      m_value  = Double.NaN;
      m_string = null;
      m_tuple  = null;
      Action   = null;
      IsReturn = false;
      Type     = VarType.NONE;
      m_dictionary.Clear();
    }

    public static Variable ResetOnBreak(Variable v)
    {
      if (v.Type == Variable.VarType.BREAK ||
          v.Type == Variable.VarType.CONTINUE) {
        return v;
      }
      return EmptyInstance;
    }

    public bool Equals(Variable other)
    {
      if (Type != other.Type) {
        return false;
      }
      if (Double.IsNaN(Value) != Double.IsNaN(other.Value) ||
        (!Double.IsNaN(Value) && Value != other.Value)) {
        return false;
      }
      if (!String.Equals(this.String, other.String, StringComparison.Ordinal)) {
        return false;
      }
      if (!String.Equals(this.Action, other.Action, StringComparison.Ordinal)) {
        return false;
      }
      if ((this.Tuple == null) != (other.Tuple == null)) {
        return false;
      }
      if (this.Tuple != null && !this.Tuple.Equals(other.Tuple)) {
        return false;
      }
      return true;
    }

    public int SetHashVariable(string hash, Variable var)
    {
      int retValue = m_tuple.Count;
      if (m_dictionary.TryGetValue(hash, out retValue)) {
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
      if (this.Type != VarType.ARRAY) {
        return -1;
      }

      if (indexVar.Type == VarType.NUMBER) {
        Utils.CheckNonNegativeInt(indexVar);
        return (int)indexVar.Value;
      }

      string hash = indexVar.AsString();
      int ptr = m_tuple.Count;
      if (m_dictionary.TryGetValue(hash, out ptr) &&
          ptr < m_tuple.Count) {
        return ptr;
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
      if (this.Type != VarType.ARRAY) {
        return false;
        //throw new ArgumentException ("Cannot perform array operations on a variable");
      }
      if (indexVar.Type == VarType.NUMBER) {
        if (indexVar.Value < 0 ||
            indexVar.Value >= m_tuple.Count ||
            indexVar.Value - Math.Floor(indexVar.Value) != 0.0) {
            return false;
        }
        if (notEmpty) {
          return m_tuple[(int)indexVar.Value].Type != VarType.NONE;
        }
        return true;
      } 
    
      string hash = indexVar.AsString();
      return Exists (hash);
    }

    public int AsInt()
    {
       int result = 0;
       if (Type == VarType.NUMBER || Value != 0.0) {
           return (int)Value;
       }
       if (Type == VarType.STRING) {
           Int32.TryParse(String, out result);
       }

       return result;
    }
    public double AsDouble()
    {
        double result = 0.0;
        if (Type == VarType.NUMBER || Value != 0.0) {
            return Value;
        }
        if (Type == VarType.STRING) {
            Double.TryParse(String, out result);
        }

        return result;
    }

    public virtual string AsString(bool isList   = true,
                               bool sameLine = true)
    {
      if (Type == VarType.NUMBER) {
        return Value.ToString();
      }
      if (Type == VarType.STRING) {
        return String;
      }
      if (Type == VarType.NONE || m_tuple == null) {
        return string.Empty;
      }

      StringBuilder sb = new StringBuilder();

      if (isList) {
        sb.Append(Constants.START_GROUP.ToString() +
                 (sameLine ? "" : Environment.NewLine));
      }
      for (int i = 0; i < m_tuple.Count; i++) {
        Variable arg = m_tuple[i];
        sb.Append(arg.AsString(isList, sameLine));
        if (i != m_tuple.Count - 1) {
          sb.Append(sameLine ? " " : Environment.NewLine);
        }
      }
      if (isList) {
        sb.Append(Constants.END_GROUP.ToString() +
                 (sameLine ? " " : Environment.NewLine));
      }

      return sb.ToString();
    }

    public void SetAsArray()
    {
      Type = VarType.ARRAY;
      if (m_tuple == null) {
        m_tuple = new List<Variable>();
      }
    }

    public int TotalElements()
    {
      return Type == VarType.ARRAY ? m_tuple.Count : 1;
    }

    public Variable GetValue(int index)
    {
      if (index >= TotalElements()) {
        throw new ArgumentException ("There are only [" + TotalElements() +
                                     "] but " + index + " requested.");

      }
      if (Type == VarType.ARRAY) {
        return m_tuple[index];
      }
      return this;
    }

    public double         Value  {
      get { return m_value; }
      set { m_value = value; Type = VarType.NUMBER; } }
    
    public string         String {
      get { return m_string; }
      set { m_string = value; Type = VarType.STRING; } }
    
    public List<Variable> Tuple  {
      get { return m_tuple; }
      set { m_tuple = value; Type = VarType.ARRAY; } }
    
    public string         Action   { get; set; }
    public VarType        Type     { get; set; }
    public bool           IsReturn { get; set; }

    public static Variable EmptyInstance = new Variable();

    private double m_value;
    private string m_string;
    private List<Variable> m_tuple;
    private Dictionary<string, int> m_dictionary = new Dictionary<string, int>();
  }
}
