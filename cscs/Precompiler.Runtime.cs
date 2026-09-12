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
    /// A call to a script function as a single expression. Precompiled code normally runs one
    /// as statements placed ahead of the expression that uses it, which is not where it
    /// belongs inside "&amp;&amp;", "||" or "?:" -- it then runs whether or not the operator
    /// reaches it -- nor in a loop's condition, where it has to run on every pass.
    /// </summary>
    public static class CscsCalls
    {
        public static Variable Call(Interpreter interpreter, string name, params object[] args)
        {
            var function = interpreter.GetFunction(name) as CustomFunction;
            if (function == null)
            {
                throw new ArgumentException("Function [" + name + "] is not defined.");
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
        /// The interpreter's own truth test: Convert.ToBoolean of the numeric field, so a
        /// string is false whatever it holds -- "5" included. Its negation is not the opposite
        /// of that: "!x" is true only for a number that is zero, so a string is false both
        /// ways round. IsFalse mirrors that rather than negating IsTrue.
        /// </summary>
        /// <summary>
        /// What "for (x in v)" walks. The interpreter iterates a collection's elements, a
        /// string's characters -- each as a string of its own -- and a scalar exactly once.
        /// Needed because the type is not known until it runs: "for (ch in row)" where row
        /// holds a line of text is a string, and asking such a value for its Size gives 0.
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

        public static bool IsTrue(Variable value)
        {
            return value != null && value.Type == Variable.VarType.NUMBER && value.Value != 0;
        }

        public static bool IsFalse(Variable value)
        {
            return value != null && value.Type == Variable.VarType.NUMBER && value.Value == 0;
        }

        /// <summary>
        /// A compound assignment, through the interpreter's own operator. It is not the same
        /// as "x = x + v": it dispatches on the *left* type and uses the right side's numeric
        /// field, so "r = 5; r += \"3\"" leaves 5 there -- the string contributes 0 -- while
        /// "r = r + \"3\"" gives "53". Calling the interpreter's code is the only way to keep
        /// every one of those corners in step.
        /// </summary>
        public static Variable Compound(Variable current, object value, string action)
        {
            var left = current ?? new Variable(0.0);
            OperatorAssignFunction.ProcessOperator(left, Variable.ConvertToVariable(value), action);
            return left;
        }

        public static bool ToFlag(object value)
        {
            var variable = value as Variable;
            return variable != null ? variable.AsBool() : Convert.ToBoolean(value);
        }
    }
}
