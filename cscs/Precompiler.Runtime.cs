using System;
using System.Collections.Generic;

namespace SplitAndMerge
{
    /// <summary>
    /// String members that precompiled code calls in place of the C# ones of the same name.
    ///
    /// CSCS string members match case unless the call passes "no_case", and Substring()
    /// clamps instead of throwing. The C#
    /// members spelled the same way do none of that, so translating a call straight through
    /// made compiled code answer differently from the interpreter -- "abcd".Contains("BC")
    /// is true in CSCS and false in C#. These mirror Variable's own implementations exactly,
    /// including the optional trailing argument, so that compiling a call cannot change what
    /// it returns.
    /// </summary>
    public static class CscsStringMembers
    {
        static StringComparison Comparison(string mode)
        {
            return mode != null && mode.Equals("case", StringComparison.OrdinalIgnoreCase) ?
                StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;
        }

        public static bool ContainsCscs(this string self, string what, string mode = "case")
        {
            return what != "" && self.IndexOf(what, Comparison(mode)) >= 0;
        }

        public static bool StartsWithCscs(this string self, string what, string mode = "case")
        {
            return self.StartsWith(what, Comparison(mode));
        }

        // A Variable where these read text -- "s.StartsWith(p)" with s typed string and p untyped:
        // the interpreter reads the argument through GetSafeString, i.e. AsString().
        public static bool StartsWithCscs(this string self, Variable what, string mode = "case")
        {
            return self.StartsWithCscs(what == null ? "" : what.AsString(), mode);
        }

        public static bool EndsWithCscs(this string self, Variable what, string mode = "case")
        {
            return self.EndsWithCscs(what == null ? "" : what.AsString(), mode);
        }

        public static bool ContainsCscs(this string self, Variable what, string mode = "case")
        {
            return self.ContainsCscs(what == null ? "" : what.AsString(), mode);
        }

        public static bool EndsWithCscs(this string self, string what, string mode = "case")
        {
            return self.EndsWith(what, Comparison(mode));
        }

        /// <summary>
        /// Not a C# Equals overload: string.Equals(string) compares ordinally, while CSCS
        /// compares by the current culture and takes the same optional "no_case" argument as
        /// the other members. Calling the C# one would have been a silent change of rule.
        /// </summary>
        public static bool EqualsCscs(this string self, string what, string mode = "case")
        {
            return self.Equals(what, Comparison(mode));
        }

        public static int IndexOfCscs(this string self, string what, int startFrom = 0,
                                      string mode = "case")
        {
            return self.IndexOf(what, startFrom, Comparison(mode));
        }

        /// <summary>
        /// C# has no "&lt;" or "&gt;" on strings, so the precompiler rewrites a string
        /// comparison into a call to this and compares the result with 0 -- the same shape
        /// Parser.MergeStrings uses.
        /// </summary>
        public static int CompareCscs(this string self, string what)
        {
            return string.Compare(self, what);
        }

        /// <summary>
        /// Comparing a string with a number. CSCS compares the two AsString() forms, so
        /// "05" == 5 is false while "5" == 5.0 is true. Going through Variable gives exactly
        /// the interpreter's formatting of the number rather than C#'s.
        /// </summary>
        /// <summary>
        /// The "===" of two strings. Unlike "==", which the interpreter resolves through
        /// string.Compare, the strict form compares the AsString() values with "==" -- an
        /// ordinal match -- after checking that the types agree.
        /// </summary>
        public static bool StrictEqCscs(this string self, string what)
        {
            return string.Equals(self, what, StringComparison.Ordinal);
        }

        public static int CompareCscs(this string self, double what)
        {
            return string.Compare(self, new Variable(what).AsString());
        }

        public static int CompareCscs(this double self, string what)
        {
            return string.Compare(new Variable(self).AsString(), what);
        }

        public static int CompareCscs(this int self, string what)
        {
            return string.Compare(new Variable((double)self).AsString(), what);
        }

        /// <summary>Text against a Variable, either way round: the AsString() forms, as for a
        /// number -- "rank(w) { if (w > \"melon\") ..." with w untyped.</summary>
        public static int CompareCscs(this string self, Variable what)
        {
            return string.Compare(self, what == null ? "" : what.AsString());
        }

        public static int CompareCscs(this Variable self, string what)
        {
            return string.Compare(self == null ? "" : self.AsString(), what);
        }

        /// <summary>
        /// The character at the index as a string of its own, or an empty string past the end,
        /// as the interpreter's At returns. The index is truncated the way GetSafeInt does it,
        /// so an expression over doubles -- "(k+3)%26" -- indexes as it does there.
        /// </summary>
        public static string AtCscs(this string self, double at)
        {
            int index = (int)at;
            return self.Length > index ? self[index].ToString() : "";
        }

        public static string AtCscs(this string self, Variable at)
        {
            return self.AtCscs(at.AsDouble());
        }

        // With a double -- an int argument next to arithmetic is widened, "s.Substring(n - 1)"
        // -- the positions are truncated, as the interpreter's GetSafeInt truncates them.
        public static string SubstringCscs(this string self, double startFrom, double length = int.MaxValue)
        {
            return self.SubstringCscs((int)startFrom, (int)Math.Min(length, int.MaxValue));
        }

        public static string SubstringCscs(this string self, int startFrom = 0,
                                           int length = int.MaxValue)
        {
            length = Math.Min(length, self.Length - startFrom);
            return self.Substring(startFrom, length);
        }
    }

    /// <summary>
    /// What the int(), double(), long(), bool() and string() conversions call. Convert's own
    /// methods need an IConvertible, so handing them a Variable -- which is what a collection
    /// or map subscript yields -- threw at run time. Everything else keeps going through
    /// Convert, so int("5.7") still parses and int(3.7) still truncates.
    /// </summary>
    /// <summary>
    /// A name compiled code reads that nothing defined when it was translated -- a global the
    /// script assigns later. Resolved when the code runs, by name, the way every call-by-name
    /// callback resolves one, so it is a variable's value, a function's result, or the
    /// interpreter's own "Couldn't find variable".
    /// </summary>
    public static class CscsLate
    {
        public static Variable Value(Interpreter interpreter, string name)
        {
            var action = "";
            var script = new ParsingScript(interpreter, "", true);
            var function = new ParserFunction(script, name, '(', ref action);
            return function.GetValue(script);
        }

        /// <summary>"g[i]" and "g[i][j]" read as the interpreter reads them (GetVarFunction,
        /// Utils.ExtractArrayElement): by position or by key, and an index past the end is
        /// "Unknown index [..] for tuple of size ..".</summary>
        public static Variable Element(Variable value, params object[] indices)
        {
            var list = new List<Variable>(indices.Length);
            foreach (var index in indices)
            {
                list.Add(index as Variable ?? Variable.ConvertToVariable(index));
            }
            return Utils.ExtractArrayElement(value, list, null);
        }

        /// <summary>"++x" and "x--" on a variable read by name, as the interpreter steps one
        /// (IncrementDecrementFunction, OperatorAssignFunction.Stepped): the stepped value written
        /// back, and returned for a prefix; a postfix returns the value before the step. A missing
        /// name is its error.</summary>
        public static Variable Step(Interpreter interpreter, string name, string op, bool prefix)
        {
            var current = interpreter.GetVariableValue(name);
            if (current == null)
            {
                throw new ArgumentException("Variable or function [" + name + "] doesn't exist.");
            }
            var before = current.DeepClone();
            var updated = OperatorAssignFunction.Stepped(current, op == "++" ? 1 : -1);
            interpreter.AddCompiledLocalVariable(name, new GetVarFunction(updated));
            return prefix ? updated.DeepClone() : before;
        }

        /// <summary>The collection "name[k] = v" writes into: the interpreter's variable, or a new
        /// one where there is none, which is what the interpreter's assignment then makes.</summary>
        public static Variable OrNewCollection(Interpreter interpreter, string name)
        {
            return interpreter.GetVariableValue(name) ?? new Variable(Variable.VarType.ARRAY);
        }

        /// <summary>Hands the interpreter a value compiled code holds, as an assignment would
        /// (AddCompiledLocalVariable: an existing global is written); true, so that it can stand
        /// in front of a loop's condition.</summary>
        public static bool Publish(Interpreter interpreter, string name, object value)
        {
            interpreter.AddCompiledLocalVariable(name, new GetVarFunction(Variable.ConvertToVariable(value)));
            return true;
        }

        /// <summary>The interpreter's current value of a variable compiled code also keeps as a
        /// local (ReadGlobalsThroughInterpreter): what a callback may have changed since.</summary>
        public static Variable Current(Interpreter interpreter, string name)
        {
            return interpreter.GetVariableValue(name) ?? Value(interpreter, name);
        }

        /// <summary>The value of a name "x += 1" or "x++" works on, which the interpreter
        /// requires to exist: a missing one is its error for the operator, not a new variable.</summary>
        public static Variable Existing(Interpreter interpreter, string name, string missing)
        {
            var value = interpreter.GetVariableValue(name);
            if (value == null)
            {
                throw new ArgumentException(missing);
            }
            return value;
        }
    }

    /// <summary>
    /// A property read from compiled code, checked as the interpreter checks one: a property the
    /// value does not have is "Object [name] doesn't exist." -- the name lower-cased, as the
    /// interpreter converts it (GetVarFunction, Utils.CheckNotNull),
    /// where GetProperty alone answers null and compiled code went on with an empty value.
    /// </summary>
    public static class CscsFields
    {
        /// <summary>"++p.x" and "p.x--" as the interpreter steps a member (IncrementDecrementFunction):
        /// the field stepped (OperatorAssignFunction.Stepped) and set back on the object, the value
        /// after the step for a prefix and before it for a postfix; a missing field is ReadField's
        /// error.</summary>
        public static Variable StepField(Variable holder, string name, string op, bool prefix)
        {
            var property = holder.ReadField(name);
            var before = property.DeepClone();
            var updated = OperatorAssignFunction.Stepped(property, op == "++" ? 1 : -1);
            holder.SetProperty(name, updated, null);
            return prefix ? updated.DeepClone() : before;
        }

        /// <summary>"b.l[1]++" and "--b.l[1]" on an element of a field's collection, as the
        /// interpreter steps one (OperatorAssignFunction.Stepped): set back into the collection, the
        /// value after the step for a prefix and before it for a postfix.</summary>
        public static Variable StepElement(Variable holder, object index, string op, bool prefix)
        {
            var key = index as Variable ?? Variable.ConvertToVariable(index);
            var before = Utils.ExtractArrayElement(holder, new List<Variable> { key }, null).DeepClone();
            var updated = OperatorAssignFunction.Stepped(before, op == "++" ? 1 : -1);
            holder.SetVariable(key, updated);
            return prefix ? updated.DeepClone() : before;
        }

        public static Variable ReadField(this Variable holder, string name)
        {
            var value = holder?.GetProperty(name);
            if (value == null)
            {
                throw new ArgumentException("Object [" + Constants.ConvertName(name) + "] doesn't exist.");
            }
            return value;
        }
    }

    /// <summary>
    /// What generated code reads from a temporary that held a Variable, for one that now holds
    /// a double (typed returns): the number itself, and its text as the interpreter writes it.
    /// </summary>
    public static class CscsNumberMembers
    {
        public static double AsDouble(this double value) { return value; }
        public static string AsString(this double value) { return new Variable(value).AsString(); }
    }

    /// <summary>
    /// A compiled function calling itself without the interpreter: typed arguments straight to
    /// its typed body (Precompiler.MakeDirectSelfCalls). The conversions are PrepareArgs' own --
    /// AsInt, AsDouble, AsString of the value as a Variable -- with a short cut for a value that
    /// already has the type, and the depth counts against InterpreterSecurity.MaxCallDepth as a
    /// pushed stack level does, so deep recursion stops with the interpreter's own error rather
    /// than overflowing the process stack.
    /// </summary>
    public static class CscsDirect
    {
        [ThreadStatic] static int s_depth;

        public static void Enter(Interpreter interpreter)
        {
            int max = InterpreterSecurity.MaxCallDepth;
            if (max > 0 && interpreter.CallDepth + s_depth >= max)
            {
                throw new ArgumentException("Recursion too deep: more than " + max + " nested function calls.");
            }
            s_depth++;
        }

        public static void Leave()
        {
            s_depth--;
        }

        public static int Int(int value) { return value; }
        public static int Int(double value) { return (int)value; }   // AsInt of a number
        public static int Int(object value) { return Variable.ConvertToVariable(value).AsInt(); }

        public static double Num(double value) { return value; }
        public static double Num(int value) { return value; }
        public static double Num(object value) { return Variable.ConvertToVariable(value).AsDouble(); }

        public static string Text(string value) { return value; }
        public static string Text(object value) { return value == null ? null : Variable.ConvertToVariable(value).AsString(); }

        public static Variable Var(object value) { return Variable.ConvertToVariable(value); }

        public static Variable Result(Variable value) { return value ?? Variable.EmptyInstance; }
        // A typed return (Precompiler.TypedReturnBody): a double or an int only, so that anything
        // else returned fails to compile and the untyped body is used.
        public static double Number(double value) { return value; }
        public static double Number(int value) { return value; }

        /// <summary>The most arguments a typed call takes; more keep the interpreter's call.</summary>
        public const int MaxArguments = 6;

        /// <summary>
        /// A call from compiled code to another compiled function. The name is looked up as the
        /// interpreter looks it up, on every call, so a function redefined since keeps its new
        /// meaning; when it is a compiled function with a typed entry point of the expected type,
        /// that runs with the arguments converted (the lambda), and otherwise the interpreter's
        /// call runs with them as they are. TFunc arrives as a typed null, to fix the lambda's types;
        /// the name arrives in the form Constants.ConvertName gives it.
        /// </summary>
        public static Variable Call<TFunc>(Interpreter interpreter, string name, TFunc hint,
            Func<TFunc, Interpreter, Variable> invoke) where TFunc : class
        {
            if (interpreter.GetRegisteredFunction(name) is CustomCompiledFunction function && function.Precompiler?.Direct is TFunc direct)
            {
                return Result(invoke(direct, interpreter));
            }
            return CscsCalls.Call(interpreter, name);
        }

        public static Variable Call<TFunc, A0>(Interpreter interpreter, string name, TFunc hint, A0 a0,
            Func<TFunc, Interpreter, A0, Variable> invoke) where TFunc : class
        {
            if (interpreter.GetRegisteredFunction(name) is CustomCompiledFunction function && function.Precompiler?.Direct is TFunc direct)
            {
                return Result(invoke(direct, interpreter, a0));
            }
            return CscsCalls.Call(interpreter, name, a0);
        }

        public static Variable Call<TFunc, A0, A1>(Interpreter interpreter, string name, TFunc hint, A0 a0, A1 a1,
            Func<TFunc, Interpreter, A0, A1, Variable> invoke) where TFunc : class
        {
            if (interpreter.GetRegisteredFunction(name) is CustomCompiledFunction function && function.Precompiler?.Direct is TFunc direct)
            {
                return Result(invoke(direct, interpreter, a0, a1));
            }
            return CscsCalls.Call(interpreter, name, a0, a1);
        }

        public static Variable Call<TFunc, A0, A1, A2>(Interpreter interpreter, string name, TFunc hint, A0 a0, A1 a1, A2 a2,
            Func<TFunc, Interpreter, A0, A1, A2, Variable> invoke) where TFunc : class
        {
            if (interpreter.GetRegisteredFunction(name) is CustomCompiledFunction function && function.Precompiler?.Direct is TFunc direct)
            {
                return Result(invoke(direct, interpreter, a0, a1, a2));
            }
            return CscsCalls.Call(interpreter, name, a0, a1, a2);
        }

        public static Variable Call<TFunc, A0, A1, A2, A3>(Interpreter interpreter, string name, TFunc hint, A0 a0, A1 a1, A2 a2, A3 a3,
            Func<TFunc, Interpreter, A0, A1, A2, A3, Variable> invoke) where TFunc : class
        {
            if (interpreter.GetRegisteredFunction(name) is CustomCompiledFunction function && function.Precompiler?.Direct is TFunc direct)
            {
                return Result(invoke(direct, interpreter, a0, a1, a2, a3));
            }
            return CscsCalls.Call(interpreter, name, a0, a1, a2, a3);
        }

        public static Variable Call<TFunc, A0, A1, A2, A3, A4>(Interpreter interpreter, string name, TFunc hint, A0 a0, A1 a1, A2 a2, A3 a3, A4 a4,
            Func<TFunc, Interpreter, A0, A1, A2, A3, A4, Variable> invoke) where TFunc : class
        {
            if (interpreter.GetRegisteredFunction(name) is CustomCompiledFunction function && function.Precompiler?.Direct is TFunc direct)
            {
                return Result(invoke(direct, interpreter, a0, a1, a2, a3, a4));
            }
            return CscsCalls.Call(interpreter, name, a0, a1, a2, a3, a4);
        }

        public static Variable Call<TFunc, A0, A1, A2, A3, A4, A5>(Interpreter interpreter, string name, TFunc hint, A0 a0, A1 a1, A2 a2, A3 a3, A4 a4, A5 a5,
            Func<TFunc, Interpreter, A0, A1, A2, A3, A4, A5, Variable> invoke) where TFunc : class
        {
            if (interpreter.GetRegisteredFunction(name) is CustomCompiledFunction function && function.Precompiler?.Direct is TFunc direct)
            {
                return Result(invoke(direct, interpreter, a0, a1, a2, a3, a4, a5));
            }
            return CscsCalls.Call(interpreter, name, a0, a1, a2, a3, a4, a5);
        }
    }

    /// <summary>
    /// A call to a script function as a single expression. Precompiled code normally runs one
    /// as statements placed ahead of the expression that uses it, which is not where it
    /// belongs inside "&amp;&amp;", "||" or "?:" -- it then runs whether or not the operator
    /// reaches it -- nor in a loop's condition, where it has to run on every pass.
    /// </summary>
    public static class CscsCalls
    {
        /// <summary>The Contains built-in (ContainsFunction) on the value its first argument names:
        /// whether that value has the index or key -- Variable.Exists, an empty element not
        /// counting -- as the number 1 or 0.</summary>
        public static Variable ContainsIn(object holder, object search)
        {
            var value = holder as Variable ?? Variable.ConvertToVariable(holder);
            var what = search as Variable ?? Variable.ConvertToVariable(search);
            return new Variable(value.Exists(what, true));
        }

        public static Variable Call(Interpreter interpreter, string name, params object[] args)
        {
            var registered = interpreter.GetFunction(name);
            var function = registered as CustomFunction;
            if (function == null)
            {
                // Compiled code calls a function by name that was not defined when it was
                // translated. Missing still: the interpreter's own error. A built-in after all:
                // run the way a callback runs one, with the values published by name.
                if (registered == null)
                {
                    throw new ArgumentException("Couldn't find function [" + name + "].");
                }
                return ByName(interpreter, name, args);
            }
            var list = new List<Variable>(args.Length);
            foreach (var arg in args)
            {
                list.Add(Variable.ConvertToVariable(arg));
            }
            // A cfunction's Run is its own, not an override of the interpreted one.
            var result = function is CustomCompiledFunction compiled ?
                compiled.Run(list) : function.Run(list);
            return result ?? Variable.EmptyInstance;
        }

        /// <summary>A built-in called with values: numbers and text as copies, as the interpreter
        /// hands them over, a collection as the same collection.</summary>
        static Variable ByName(Interpreter interpreter, string name, object[] args)
        {
            var names = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                var value = Variable.ConvertToVariable(args[i]);
                if (value.Type == Variable.VarType.NUMBER || value.Type == Variable.VarType.STRING)
                {
                    value = value.Clone();
                }
                names[i] = "__cscsbyname" + i;
                interpreter.AddCompiledLocalOnlyVariable(names[i], new GetVarFunction(value));
            }
            var action = "";
            var script = new ParsingScript(interpreter, string.Join(",", names), true);
            var function = new ParserFunction(script, name, '(', ref action);
            return function.GetValue(script) ?? Variable.EmptyInstance;
        }

        /// <summary>
        /// A built-in function -- "Math.Sin", "Math.Round" -- run by the interpreter on values the
        /// compiled code already holds. A built-in reads its arguments by parsing script text, so
        /// the values are published under temporary names at the function's own level and the
        /// call goes the way every interpreter callback goes; the answer is the interpreter's
        /// whatever the arguments hold (text, a truth value). Clones, since a built-in may set
        /// the value of the argument it was handed, as the interpreter's copies allow.
        /// </summary>
        public static Variable Builtin(Interpreter interpreter, string name, params object[] args)
        {
            var names = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                names[i] = "__cscsbuiltinarg" + i;
                interpreter.AddCompiledLocalOnlyVariable(names[i],
                    new GetVarFunction(Variable.ConvertToVariable(args[i]).Clone()));
            }
            var action = "";
            var script = new ParsingScript(interpreter, string.Join(",", names), true);
            var function = new ParserFunction(script, name, '(', ref action);
            return function.GetValue(script) ?? Variable.EmptyInstance;
        }
    }

    /// <summary>
    /// A comparison the C# operators cannot express -- a Variable against text, a bool, or
    /// null -- answered by the interpreter itself (Parser.MergeCells), so it cannot differ from
    /// what the script means. RoslynCompiler.RepairOperators writes calls to it where the
    /// generated C# failed with CS0019.
    /// </summary>
    public static class CscsOps
    {
        public static bool Compare(Interpreter interpreter, object left, string action, object right)
        {
            var script = new ParsingScript(interpreter, "");
            var result = Parser.MergePair(AsOperand(left), action, AsOperand(right), script);
            return result.Value != 0;
        }

        /// <summary>An operator C# has no overload for between these operands -- "(n | 4)" with n a
        /// Variable -- applied by the interpreter (Parser.MergeCells), so its answer is the
        /// script's.</summary>
        /// <summary>
        /// "a && b" and "a || b" as the interpreter evaluates them (Parser.UpdateIfBool,
        /// MergeCells): always 1 or 0, by the one truth rule (Variable.IsTrue). The right side
        /// runs only when the left one does not decide.
        /// </summary>
        public static Variable And(Interpreter interpreter, object left, Func<object> right)
        {
            return Logical(interpreter, left, "&&", right);
        }

        public static Variable Or(Interpreter interpreter, object left, Func<object> right)
        {
            return Logical(interpreter, left, "||", right);
        }

        static Variable Logical(Interpreter interpreter, object left, string action, Func<object> right)
        {
            bool leftTrue = AsOperand(left).IsTrue();
            if (action == "&&" ? !leftTrue : leftTrue)
            {
                return new Variable(leftTrue ? 1.0 : 0.0);
            }
            return new Variable(AsOperand(right()).IsTrue() ? 1.0 : 0.0);
        }

        /// <summary>
        /// "g += v" and "g++" on a name the interpreter holds, as the interpreter does them
        /// (OperatorAssignFunction.ProcessOperator and Stepped): on a copy, which the caller
        /// writes back. A compound is "g = g op v"; a step is that with 1, numeric text read as
        /// its number.
        /// </summary>
        public static Variable Compound(Variable current, string action, object right)
        {
            if (action == "++" || action == "--")
            {
                return OperatorAssignFunction.Stepped(current, action == "++" ? 1 : -1);
            }
            var updated = current.DeepClone();
            OperatorAssignFunction.ProcessOperator(updated, AsOperand(right), action);
            return updated;
        }

        /// <summary>Whether a switch value matches a case label as the interpreter decides it
        /// (Interpreter.ProcessSwitch): the same type, and then Equals.</summary>
        public static bool CaseMatches(object value, object label)
        {
            var left = AsOperand(value);
            var right = AsOperand(label);
            return left.Type == right.Type && left.Equals(right);
        }

        /// <summary>What a postfix step returns in the interpreter (IncrementDecrementFunction):
        /// the value before the step, whatever its type.</summary>
        public static Variable StepValue(object value)
        {
            return value is Variable variable ? variable.DeepClone() : Variable.ConvertToVariable(value);
        }

        /// <summary>The value of "!x" as the interpreter gives it: 1 or 0 for any value, by the
        /// one truth rule (Variable.IsTrue). It used to leave anything but a number as it was:
        /// "!\"abc\"" was "abc".</summary>
        public static Variable Not(object value)
        {
            return new Variable(AsOperand(value).IsTrue() ? 0.0 : 1.0);
        }

        public static Variable Apply(Interpreter interpreter, object left, string action, object right)
        {
            var script = new ParsingScript(interpreter, "");
            return Parser.MergePair(AsOperand(left), action, AsOperand(right), script);
        }

        /// <summary>"a ** b". Two numbers are Math.Pow, as the interpreter merges them; anything
        /// else -- a Variable, which may hold text -- goes through the interpreter's merge.</summary>
        public static double Power(double left, double right)
        {
            return Math.Pow(left, right);
        }

        public static Variable Power(object left, object right)
        {
            var script = new ParsingScript(Interpreter.LastInstance, "");
            return Parser.MergePair(AsOperand(left), Constants.POWER, AsOperand(right), script);
        }

        /// <summary>The value as the interpreter holds it: a C# null is CSCS's null (the empty
        /// value), and a C# bool -- a comparison's result -- is the number 1 or 0.</summary>
        internal static Variable AsOperand(object value)
        {
            if (value == null)
            {
                return Variable.EmptyInstance;
            }
            if (value is bool flag)
            {
                return new Variable(flag ? 1 : 0);
            }
            return value as Variable ?? Variable.ConvertToVariable(value);
        }
    }

    public static class CscsEnums
    {
        /// <summary>
        /// Reads a member of an enum the interpreter holds -- "Colors.Green" -- which no
        /// generated C# name can stand for. The interpreter resolves such a member through
        /// Variable.GetEnumProperty, which needs a ParsingScript only to spot the call form
        /// "Colors(x)": it tests script.Prev, and a freshly built script answers
        /// Constants.EMPTY there, so the plain member read falls through to the name lookup.
        /// Verified against the interpreter: Red is 0, Green 1, Blue 2.
        /// </summary>
        /// <summary>
        /// The same read on an enum the function declared itself, held in a local.
        /// </summary>
        public static Variable Member(Interpreter interpreter, Variable holder, string member)
        {
            return holder.GetEnumProperty(member, new ParsingScript(interpreter, ""));
        }

        public static Variable Member(Interpreter interpreter, string enumName, string member)
        {
            var holder = interpreter.GetVariableValue(enumName);
            if (holder == null)
            {
                throw new ArgumentException("Enum [" + enumName + "] is not defined.");
            }
            var script = new ParsingScript(interpreter, "");
            return holder.Type == Variable.VarType.ENUM ?
                holder.GetEnumProperty(member, script) : holder.GetProperty(member, script);
        }
    }

    public static class CscsConvert
    {
        public static double ToNumber(object value)
        {
            var variable = value as Variable;
            return variable != null ? variable.AsDouble() : Convert.ToDouble(value);
        }

        // "string(d, \"yyyy/MM/dd\")" -- a second argument is a format, which is what the
        // interpreter's own string() passes to AsString. Without this overload a date, or any
        // formatted value, could not compile.
        // "m[\"a\"].Add(x)" and "a[i].Add(x)": the element is a Variable, which has no Add of
        // its own, so a nested collection could not be appended to. AddVariable with no index
        // appends, which is what the interpreter's own Add does; the element is the same
        // object the collection holds, so the change is visible through it.
        public static void Add(this Variable self, object value)
        {
            self.AddVariable(value as Variable ?? Variable.ConvertToVariable(value));
        }

        // Adds only what is not there yet, as the interpreter's AddUnique does.
        public static void AddUnique(this Variable self, object value)
        {
            var variable = value as Variable ?? Variable.ConvertToVariable(value);
            if (!self.Contains(variable))
            {
                self.AddVariable(variable);
            }
        }

        // "{5, 6, 7}.IndexOf(6)" searches the collection's *text* -- 4, where "6" sits in
        // "[5, 6, 7]" -- and -1 when it is not there at all. Variable's own IndexOf takes a
        // string, so a number could not be handed to it; this overload is the one C# picks for
        // one, and it answers what the interpreter answers.
        public static int IndexOf(this Variable self, double what)
        {
            return ToText(self).IndexOfCscs(ToText(what));
        }

        public static string ToText(object value, string format)
        {
            var variable = value as Variable ?? Variable.ConvertToVariable(value);
            return variable.AsString(format);
        }

        public static string ToText(object value)
        {
            var variable = value as Variable;
            if (variable != null)
            {
                return variable.AsString();
            }
            // A comparison's result reaches here as a C# bool, and the interpreter renders one
            // as 1 or 0 rather than True or False.
            if (value is bool flag)
            {
                return flag ? "1" : "0";
            }
            return Convert.ToString(value);
        }

        /// <summary>
        /// What "for (x in v)" walks. The interpreter iterates a collection's elements, a
        /// string's characters -- each as a string of its own -- and a scalar exactly once.
        /// Needed because the type is not known until it runs: "for (ch in row)" where row
        /// holds a line of text is a string, whose Size is its length, not an element count.
        /// </summary>
        public static Variable AsItems(Variable value)
        {
            if (value == null)
            {
                return new Variable(new List<Variable>());
            }
            if (value.Type == Variable.VarType.ARRAY)
            {
                return value;
            }
            var items = new List<Variable>();
            if (value.Type == Variable.VarType.STRING)
            {
                var text = value.AsString();
                for (int i = 0; i < text.Length; i++)
                {
                    items.Add(new Variable(text[i].ToString()));
                }
            }
            else
            {
                items.Add(value);
            }
            return new Variable(items);
        }

        // A condition is the one truth rule (Variable.IsTrue) -- if, while, for, "?:", "!", "&&"
        // and "||" alike: a number unless 0, text unless empty, "0" or "false", a list unless
        // empty, null never.
        public static bool IsTrue(Variable value)
        {
            return value != null && value.IsTrue();
        }

        // The truth of "!value" in a condition: the opposite, null included.
        public static bool IsFalse(Variable value)
        {
            return !IsTrue(value);
        }
        /// <summary>
        /// The same two tests for a term whose C# type is not Variable -- a string local, an
        /// argument, a bool.
        /// </summary>
        public static bool IsTrue(object value)
        {
            switch (value)
            {
                case null: return false;
                case Variable variable: return IsTrue(variable);
                case bool flag: return flag;
                case string text: return new Variable(text).IsTrue();
                default: return Variable.ConvertToVariable(value).IsTrue();
            }
        }

        public static bool IsFalse(object value)
        {
            return !IsTrue(value);
        }

        /// <summary>
        /// <summary>
        /// The element a member write goes to: "a[i].v = x" in compiled code. The bounds check is
        /// the interpreter's own, from Utils.ExtractArrayElement, and so is the message. The
        /// indexer cannot be used for this: a missing element comes back as
        /// Variable.EmptyInstance, which is a NEW Variable on every read, so a reference check
        /// against it never matched and "a[5].v = 1" quietly wrote to a throwaway object and
        /// carried on, where the interpreter stops with "Unknown index".
        /// </summary>
        public static Variable ElementForWrite(Variable holder, Variable index)
        {
            int size = holder == null || holder.Tuple == null ? 0 : holder.Tuple.Count;
            int arrayIndex = holder == null || index == null ? -1 : holder.GetArrayIndex(index);
            if (arrayIndex < 0 || arrayIndex >= size)
            {
                throw new ArgumentException("Unknown index [" + (index == null ? "" : index.AsString()) +
                    "] for tuple of size " + size);
            }
            return holder.Tuple[arrayIndex];
        }

        /// <summary>
        /// A compound assignment, through the interpreter's own operator: "x = x op v"
        /// (OperatorAssignFunction.ProcessOperator), so "r = 5; r += \"3\"" is "53".
        /// </summary>
        public static Variable Compound(Variable current, object value, string action)
        {
            // On a copy, as the interpreter's compound does (OperatorAssignFunction, DeepClone);
            // every caller stores the result. In place, "L = G; L += 1" changed the global G,
            // whose Variable the local held.
            var left = current == null ? new Variable(0.0) : current.DeepClone();
            OperatorAssignFunction.ProcessOperator(left, Variable.ConvertToVariable(value), action);
            return left;
        }

        /// <summary>A catch variable, as the interpreter binds one (Interpreter.ProcessTry): the
        /// message text, which also answers e.Message.</summary>
        public static Variable Caught(Exception exception)
        {
            var caught = new Variable(exception.Message);
            caught.AddTextProperty("Message", new Variable(exception.Message));
            return caught;
        }

        public static bool ToFlag(object value)
        {
            // As the interpreter's bool(x) (ToBoolFunction): the text of the value, read by
            // Utils.ConvertToBool -- "5" is true there, where Variable.AsBool took only "true".
            var variable = value as Variable ?? Variable.ConvertToVariable(value);
            return Utils.ConvertToBool(variable.AsString());
        }
    }
}
