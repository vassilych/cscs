using System;
using System.Collections.Generic;

namespace SplitAndMerge
{
  class BreakStatement : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      return new Variable(Variable.VarType.BREAK);
    }
  }
  class ContinueStatement : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      return new Variable(Variable.VarType.CONTINUE);
    }
  }

  class ReturnStatement : ParserFunction
  {
    
    protected override Variable Evaluate(ParsingScript script)
    {
      script.MoveForwardIf(Constants.SPACE);

      Variable result = Utils.GetItem(script);

      // If we are in Return, we are done:
      script.SetDone();
      result.IsReturn = true;

      return result;
    }
  }

  class TryBlock : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      return Interpreter.Instance.ProcessTry(script);
    }
  }

  class ExitFunction : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      Environment.Exit(0);
      return Variable.EmptyInstance;
    }
  }

  class ThrowFunction : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      // 1. Extract what to throw.
       Variable arg = Utils.GetItem(script);

      // 2. Convert it to a string.
      string result = arg.AsString();

      // 3. Throw it!
      throw new ArgumentException(result);
    }
  }

  class ShowFunction : ParserFunction
  {
    protected override Variable Evaluate (ParsingScript script)
    {
      string funcName = Utils.GetToken(script, Constants.TOKEN_SEPARATION);
      ParserFunction function = ParserFunction.GetFunction(funcName);
      CustomFunction custFunc = function as CustomFunction;
      Utils.CheckNotNull(funcName, custFunc);

      string body = Utils.BeautifyScript(custFunc.Body, custFunc.Header);
      Translation.PrintScript(body);

      return new Variable(body);
    }
  }

  class TranslateFunction : ParserFunction
  {
    protected override Variable Evaluate (ParsingScript script)
    {
      string language = Utils.GetToken(script, Constants.TOKEN_SEPARATION);
      string funcName = Utils.GetToken(script, Constants.TOKEN_SEPARATION);

      ParserFunction function = ParserFunction.GetFunction(funcName);
      CustomFunction custFunc = function as CustomFunction;
      Utils.CheckNotNull(funcName, custFunc);

      string body = Utils.BeautifyScript(custFunc.Body, custFunc.Header);
      string translated = Translation.TranslateScript(body, language);
      Translation.PrintScript(translated);

      return new Variable(translated);
    }
  }

  class FunctionCreator : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      string funcName = Utils.GetToken(script, Constants.TOKEN_SEPARATION);
      //Interpreter.Instance.AppendOutput("Registering function [" + funcName + "] ...");

      string[] args = Utils.GetFunctionSignature(script);
      if (args.Length == 1 && string.IsNullOrWhiteSpace(args[0])) {
        args = new string[0];
      }

      script.MoveForwardIf(Constants.START_GROUP, Constants.SPACE);
      int parentOffset = script.Pointer;

      string body = Utils.GetBodyBetween(script, Constants.START_GROUP, Constants.END_GROUP);

      CustomFunction customFunc = new CustomFunction(funcName, body, args);
      customFunc.ParentScript = script;
      customFunc.ParentOffset = parentOffset;

      ParserFunction.RegisterFunction(funcName, customFunc, false /* not native */);

      return new Variable(funcName);
    }
  }

  class CustomFunction : ParserFunction
  {
    internal CustomFunction(string funcName,
                            string body, string[] args)
    {
      m_name = funcName;
      m_body = body;
      m_args = args;
    }

    protected override Variable Evaluate(ParsingScript script)
    {
      bool isList;

      List<Variable> functionArgs = Utils.GetArgs(script,
          Constants.START_ARG, Constants.END_ARG, out isList);

      //script.MoveForwardIf(Constants.END_ARG);
      script.MoveBackIf(Constants.START_GROUP);

      if (functionArgs.Count != m_args.Length) {   
        throw new ArgumentException("Function [" + m_name + "] arguments mismatch: " +
          m_args.Length + " declared, " + functionArgs.Count + " supplied");
      }

      // 1. Add passed arguments as local variables to the Parser.
      StackLevel stackLevel = new StackLevel(m_name);
      for (int i = 0; i < m_args.Length; i++) {
        stackLevel.Variables[m_args[i]] = new GetVarFunction(functionArgs[i]);
      }

      ParserFunction.AddLocalVariables(stackLevel);

      // 2. Execute the body of the function.
      Variable result = null;
      ParsingScript tempScript = new ParsingScript(m_body);
      tempScript.ScriptOffset = m_parentOffset;
      if (m_parentScript != null) {
        tempScript.Char2Line      = m_parentScript.Char2Line;
        tempScript.Filename       = m_parentScript.Filename;
        tempScript.OriginalScript = m_parentScript.OriginalScript;
      }

      while (tempScript.Pointer < m_body.Length - 1 && 
            (result == null || !result.IsReturn)) {
        result = tempScript.ExecuteTo();
        tempScript.GoToNextStatement();
      }

      ParserFunction.PopLocalVariables();
      //script.MoveForwardIf(Constants.END_ARG);
      //script.MoveForwardIf(Constants.END_STATEMENT);

      if (result == null) {
        result = Variable.EmptyInstance;
      } else {
        result.IsReturn = false;
      }

      return result;
    }

    public ParsingScript ParentScript { set { m_parentScript = value; } }
    public int           ParentOffset { set { m_parentOffset = value; } }
    public string        Body         { get { return m_body; } }
    public string        Header       { get {
        return Constants.FUNCTION + " " + Name + " " +
               Constants.START_ARG + string.Join (", ", m_args) +
               Constants.END_ARG + " " + Constants.START_GROUP;
         } }

    private string        m_body;
    private string[]      m_args;
    private ParsingScript m_parentScript = null;
    private int           m_parentOffset = 0;
  }

    class StringOrNumberFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            // First check if the passed expression is a string between quotes.
            if (Item.Length > 1 &&
                Item[0] == Constants.QUOTE &&
                Item[Item.Length - 1]  == Constants.QUOTE) {
              return new Variable(Item.Substring(1, Item.Length - 2));
            }

            // Otherwise this should be a number.
            double num;
            if (!Double.TryParse(Item, out num)) {
                Utils.ThrowException(script, "parseToken", Item, "parseTokenExtra");
            }
            return new Variable(num);
        }
    
        public string Item { private get; set; }
    }

  class AddFunction : ParserFunction
  {
    protected override Variable Evaluate (ParsingScript script)
    {
      // 1. Get the name of the variable.
      string varName = Utils.GetToken(script, Constants.NEXT_OR_END_ARRAY);
      Utils.CheckNotEnd(script, Constants.CONTAINS);

      // 2. Get the current value of the variable.
      ParserFunction func = ParserFunction.GetFunction(varName);
      Utils.CheckNotNull(varName, func);
      Variable currentValue = func.GetValue(script);

      // 3. Get the variable to add.
      Variable item = Utils.GetItem(script);

      // 4. Add it to the tuple.
      currentValue.AddVariable(item);

      ParserFunction.AddGlobalOrLocalVariable(varName,
                                              new GetVarFunction (currentValue));

      return currentValue;
    }
  }

  class ContainsFunction : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      // 1. Get the name of the variable.
      string varName = Utils.GetToken(script, Constants.NEXT_OR_END_ARRAY);
      Utils.CheckNotEnd(script, Constants.CONTAINS);

      // 2. Get the current value of the variable.
      List<Variable> arrayIndices = Utils.GetArrayIndices(ref varName);

      ParserFunction func = ParserFunction.GetFunction(varName);
      Utils.CheckNotNull(varName, func);
      Variable currentValue = func.GetValue(script);

      // 2b. Special dealings with arrays:
      Variable query = arrayIndices.Count > 0 ?
                       Utils.ExtractArrayElement(currentValue, arrayIndices) :
                       currentValue;

      // 3. Get the value to be looked for.
      Variable searchValue = Utils.GetItem(script);
      Utils.CheckNotEnd(script, Constants.CONTAINS);

      // 4. Check if the value to search for exists.
      bool exists = query.Exists(searchValue, true /* notEmpty */);

      script.MoveBackIf(Constants.START_GROUP);
      return new Variable(exists);
    }
  }

  class BoolFunction : ParserFunction
  {
      bool m_value;
      public BoolFunction(bool init)
      {
        m_value = init;  
      }
      protected override Variable Evaluate(ParsingScript script)
      {
        return new Variable(m_value);
      }
  }
  class IdentityFunction : ParserFunction
  {
      protected override Variable Evaluate(ParsingScript script)
      {
        return script.ExecuteTo(Constants.END_ARG);
      }
  }

  class IfStatement : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      Variable result = Interpreter.Instance.ProcessIf(script);
      return result;
    }
  }

  class ForStatement : ParserFunction
  {
    protected override Variable Evaluate (ParsingScript script)
    {
      return Interpreter.Instance.ProcessFor(script);
    }
  }

  class WhileStatement : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      return Interpreter.Instance.ProcessWhile(script);
    }
  }

  class IncludeFile : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      string filename = Utils.GetItem(script).AsString();
      string[] lines = Utils.GetFileLines(filename);

      string includeFile = string.Join(Environment.NewLine, lines);
      Dictionary<int, int> char2Line;
      string includeScript = Utils.ConvertToScript(includeFile, out char2Line);
      ParsingScript tempScript = new ParsingScript(includeScript, 0, char2Line);
      tempScript.Filename = filename;
      tempScript.OriginalScript = string.Join(Constants.END_LINE.ToString(), lines);

      while (tempScript.Pointer < includeScript.Length) {
        tempScript.ExecuteTo();
        tempScript.GoToNextStatement();
      }
      return Variable.EmptyInstance;
    }
  }

  // Get a value of a variable or of an array element
  class GetVarFunction : ParserFunction
  {
    internal GetVarFunction(Variable value)
    {
      m_value = value;
    }

    protected override Variable Evaluate(ParsingScript script)
    {
      // First check if this element is part of an array:
      if (script.TryPrev() == Constants.START_ARRAY)
      {
        // There is an index given - it must be for an element of the tuple.
        if (m_value.Tuple == null || m_value.Tuple.Count == 0) {
          throw new ArgumentException("No tuple exists for the index");
        }

        if (m_arrayIndices == null) {
          string startName = script.Substr(script.Pointer - 1);
          m_arrayIndices = Utils.GetArrayIndices(ref startName, ref m_delta);
        }

        script.Forward(m_delta);

        Variable result = Utils.ExtractArrayElement(m_value, m_arrayIndices);
        return result;
      }

      // Otherwise just return the stored value.
      return m_value;
    }

    public int Delta {
      set { m_delta = value; }
    }
    public List<Variable> Indices {
      set { m_arrayIndices = value; }
    }

    private Variable m_value;
    private int m_delta = 0;
    private List<Variable> m_arrayIndices = null;
  }

  class IncrementDecrementFunction : ActionFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      bool prefix = string.IsNullOrWhiteSpace(m_name);
      if (prefix) {// If it is a prefix we do not have the variable name yet.
        m_name = Utils.GetToken(script, Constants.TOKEN_SEPARATION);
      }

      // Value to be added to the variable:
      int valueDelta = m_action == Constants.INCREMENT ? 1 : -1;
      int returnDelta = prefix ? valueDelta : 0;

      // Check if the variable to be set has the form of x[a][b],
      // meaning that this is an array element.
      double newValue = 0;
      List<Variable> arrayIndices = Utils.GetArrayIndices(ref m_name);

      ParserFunction func = ParserFunction.GetFunction(m_name);
      Utils.CheckNotNull(m_name, func);

      Variable currentValue = func.GetValue(script);

      if (arrayIndices.Count > 0 || script.TryCurrent () == Constants.START_ARRAY) {
        if (prefix) {
          string tmpName = m_name + script.Rest;
          int delta = 0;
          arrayIndices = Utils.GetArrayIndices(ref tmpName, ref delta);
          script.Forward(Math.Max(0, delta - tmpName.Length));
        }

        Variable element = Utils.ExtractArrayElement(currentValue, arrayIndices);
        script.MoveForwardIf(Constants.END_ARRAY);

        newValue = element.Value + returnDelta;
        element.Value += valueDelta;
      } else { // A normal variable.
        newValue = currentValue.Value + returnDelta;
        currentValue.Value += valueDelta;
      }

      ParserFunction.AddGlobalOrLocalVariable(m_name,
                                              new GetVarFunction(currentValue));
      return new Variable(newValue);
    }

    override public ParserFunction NewInstance()
    {
      return new IncrementDecrementFunction();
    }
  }

  class OperatorAssignFunction : ActionFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      // Value to be added to the variable:
      Variable right  = Utils.GetItem(script);

      List<Variable> arrayIndices = Utils.GetArrayIndices(ref m_name);

      ParserFunction func = ParserFunction.GetFunction(m_name);
      Utils.CheckNotNull(m_name, func);

      Variable currentValue = func.GetValue(script);
      Variable left = currentValue;

      if (arrayIndices.Count > 0) {// array element
        left = Utils.ExtractArrayElement(currentValue, arrayIndices);
        script.MoveForwardIf(Constants.END_ARRAY);
      }

      if (left.Type == Variable.VarType.NUMBER) {
        NumberOperator(left, right, m_action);
      } else {
        StringOperator(left, right, m_action);
      }

      if (arrayIndices.Count > 0) {// array element
        AssignFunction.ExtendArray(currentValue, arrayIndices, 0, left);
        ParserFunction.AddGlobalOrLocalVariable (m_name,
                                                 new GetVarFunction(currentValue));
      } else {
        ParserFunction.AddGlobalOrLocalVariable (m_name,
                                                 new GetVarFunction(left));
      }
      return left;
    }

    static void NumberOperator(Variable valueA,
                               Variable valueB, string action)
    {
      switch (action) {
      case "+=":
        valueA.Value += valueB.Value;
        break;
      case "-=":
        valueA.Value -= valueB.Value;
        break;
      case "*=":
        valueA.Value *= valueB.Value;
        break;
      case "/=":
        valueA.Value /= valueB.Value;
        break;
      case "%=":
        valueA.Value %= valueB.Value;
        break;
      case "&=":
        valueA.Value = (int)valueA.Value & (int)valueB.Value;
        break;
      case "|=":
        valueA.Value = (int)valueA.Value | (int)valueB.Value;
        break;
      case "^=":
        valueA.Value = (int)valueA.Value ^ (int)valueB.Value;
        break;
      }
    }
    static void StringOperator(Variable valueA,
      Variable valueB, string action)
    {
      switch (action)
      {
      case "+=":
        if (valueB.Type == Variable.VarType.STRING) {
          valueA.String += valueB.AsString();
        } else {
          valueA.String += valueB.Value;
        }
        break;
      }
    }

    override public ParserFunction NewInstance()
    {
      return new OperatorAssignFunction();
    }
  }

  class AssignFunction : ActionFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      Variable varValue = Utils.GetItem(script);

      // Check if the variable to be set has the form of x[a][b]...,
      // meaning that this is an array element.
      List<Variable> arrayIndices = Utils.GetArrayIndices(ref m_name);

      if (arrayIndices.Count == 0) {
        ParserFunction.AddGlobalOrLocalVariable(m_name, new GetVarFunction(varValue));
        return varValue;
      }

      Variable array;
 
      ParserFunction pf = ParserFunction.GetFunction(m_name);
      if (pf != null) {
        array = pf.GetValue(script);
      } else {
        array = new Variable();
      }

      ExtendArray(array, arrayIndices, 0, varValue);

      ParserFunction.AddGlobalOrLocalVariable(m_name, new GetVarFunction(array));
      return array;
    }

    override public ParserFunction NewInstance()
    {
      return new AssignFunction();
    }

    public static void ExtendArray(Variable parent,
                     List<Variable> arrayIndices,
                     int indexPtr,
                     Variable varValue)
    {
      if (arrayIndices.Count <= indexPtr) {
        return;
      }
      
      Variable index = arrayIndices[indexPtr];
      int currIndex = ExtendArrayHelper(parent, index);
      
      if (arrayIndices.Count - 1 == indexPtr) {
        parent.Tuple[currIndex] = varValue;
        return;
      }

      Variable son = parent.Tuple[currIndex];
      ExtendArray(son, arrayIndices, indexPtr + 1, varValue);
    }

    private static int ExtendArrayHelper(Variable parent, Variable indexVar)
    {
      parent.SetAsArray();
  
      int arrayIndex = parent.GetArrayIndex(indexVar);
      if (arrayIndex < 0) {
        // This is not a "normal index" but a new string for the dictionary.
        string hash = indexVar.AsString();
        arrayIndex  = parent.SetHashVariable(hash, Variable.NewEmpty());
        return arrayIndex;
      }
  
      if (parent.Tuple.Count <= arrayIndex) {
        for (int i = parent.Tuple.Count; i <= arrayIndex; i++) {
          parent.Tuple.Add(Variable.NewEmpty());
        }
      }
      return arrayIndex;
    }
  }

  class TypeFunction : ParserFunction
  {
    protected override Variable Evaluate (ParsingScript script)
    {
      // 1. Get the name of the variable.
      string varName = Utils.GetToken (script, Constants.END_ARG_ARRAY);
      Utils.CheckNotEnd(script, m_name);

      List<Variable> arrayIndices = Utils.GetArrayIndices(ref varName);

      // 2. Get the current value of the variable.
      ParserFunction func = ParserFunction.GetFunction(varName);
      Utils.CheckNotNull(varName, func);
      Variable currentValue = func.GetValue(script);
      Variable element = currentValue;

      // 2b. Special case for an array.
      if (arrayIndices.Count > 0) {// array element
        element = Utils.ExtractArrayElement(currentValue, arrayIndices);
        script.MoveForwardIf(Constants.END_ARRAY);
      }

      // 3. Convert type to string.
      string type = Constants.TypeToString(element.Type);
      script.MoveForwardIf (Constants.END_ARG, Constants.SPACE);

      Variable newValue = new Variable(type);
      return newValue;
    }
  }

  class SizeFunction : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      // 1. Get the name of the variable.
      string varName = Utils.GetToken(script, Constants.END_ARG_ARRAY);
      Utils.CheckNotEnd(script, m_name);

      List<Variable> arrayIndices = Utils.GetArrayIndices(ref varName);

      // 2. Get the current value of the variable.
      ParserFunction func = ParserFunction.GetFunction(varName);
      Utils.CheckNotNull(varName, func);
      Variable currentValue = func.GetValue(script);
      Variable element = currentValue;

      // 2b. Special case for an array.
      if (arrayIndices.Count > 0) {// array element
        element = Utils.ExtractArrayElement(currentValue, arrayIndices);
        script.MoveForwardIf(Constants.END_ARRAY);
      }

      // 3. Take either the length of the underlying tuple or
      // string part if it is defined,
      // or the numerical part converted to a string otherwise.
      int size = element.Type == Variable.VarType.ARRAY ?
                        element.Tuple.Count :
                        element.AsString().Length;

      script.MoveForwardIf(Constants.END_ARG, Constants.SPACE);

      Variable newValue = new Variable(size);
      return newValue;
    }
  }
}
