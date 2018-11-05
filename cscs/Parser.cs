using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SplitAndMerge
{
    public class Parser
    {
        public static bool Verbose { get; set; }

        public static Variable SplitAndMerge(ParsingScript script)
        {
            return SplitAndMerge(script, Constants.END_PARSE_ARRAY);
        }
        public static Variable SplitAndMerge(ParsingScript script, char[] to)
        {
            // First step: process passed expression by splitting it into a list of cells.
            List<Variable> listToMerge = Split(script, to);

            if (listToMerge.Count == 0)
            {
                throw new ArgumentException("Couldn't parse [" +
                                            script.Rest + "]");
            }

            // Second step: merge list of cells to get the result of an expression.
            Variable result = MergeList(listToMerge);
            return result;
        }

        private static List<Variable> Split(ParsingScript script, char[] to)
        {
            List<Variable> listToMerge = new List<Variable>(16);

            if (!script.StillValid() || to.Contains(script.Current))
            {
                listToMerge.Add(Variable.EmptyInstance);
                script.Forward();
                return listToMerge;
            }

            StringBuilder item = new StringBuilder();
            int arrayIndexDepth = 0;
            bool inQuotes = false;
            int negated = 0;

            string rest = script.Rest;
            //if (rest == "b[a[0]];") {
            //  int stop = 1;
            //}

            do
            { // Main processing cycle of the first part.
                string negateSymbol = Utils.IsNotSign(script.Rest);
                if (negateSymbol != null && !inQuotes)
                {
                    negated++;
                    script.Forward(negateSymbol.Length);
                    continue;
                }

                char ch = script.CurrentAndForward();
                CheckQuotesIndices(script, ch, ref inQuotes, ref arrayIndexDepth);
                string action = null;

                bool keepCollecting = inQuotes || arrayIndexDepth > 0 ||
                     StillCollecting(item.ToString(), to, script, ref action);
                if (keepCollecting)
                {
                    // The char still belongs to the previous operand.
                    item.Append(ch);

                    bool goForMore = script.StillValid() &&
                        (inQuotes || arrayIndexDepth > 0 || !to.Contains(script.Current));
                    if (goForMore)
                    {
                        continue;
                    }
                }

                if (SkipOrAppendIfNecessary(item, ch, to))
                {
                    continue;
                }

                string token = item.ToString();

                bool ternary = UpdateIfTernary(script, token, ch, ref listToMerge);
                if (ternary)
                {
                    return listToMerge;
                }

                CheckConsistency(token, listToMerge, script);

                script.MoveForwardIf(Constants.SPACE);

                if (action != null && action.Length > 1)
                {
                    script.Forward(action.Length - 1);
                }

                bool negSign = CheckNegativeSign(ref token);

                // We are done getting the next token. The GetValue() call below may
                // recursively call SplitAndMerge(). This will happen if extracted
                // item is a function or if the next item is starting with a START_ARG '('.
                ParserFunction func = new ParserFunction(script, token, ch, ref action);
                Variable current = func.GetValue(script);
                if (current == null)
                {
                    current = Variable.EmptyInstance;
                }
                current.ParsingToken = token;

                if (negSign)
                {
                    current = new Variable(-1 * current.Value);
                }

                if (negated > 0 && current.Type == Variable.VarType.NUMBER)
                {
                    // If there has been a NOT sign, this is a boolean.
                    // Use XOR (true if exactly one of the arguments is true).
                    bool neg = !((negated % 2 == 0) ^ Convert.ToBoolean(current.Value));
                    current = new Variable(Convert.ToDouble(neg));
                    negated = 0;
                }

                if (action == null)
                {
                    action = UpdateAction(script, to);
                }
                else
                {
                    script.MoveForwardIf(action[0]);
                }

                char next = script.TryCurrent(); // we've already moved forward
                bool done = listToMerge.Count == 0 &&
                            (next == Constants.END_STATEMENT ||
                            (action == Constants.NULL_ACTION && current.Type != Variable.VarType.NUMBER) ||
                             current.IsReturn);
                if (done)
                {
                    if (action != null && action != Constants.END_ARG_STR)
                    {
                        throw new ArgumentException("Action [" +
                                  action + "] without an argument.");
                    }
                    // If there is no numerical result, we are not in a math expression.
                    listToMerge.Add(current);
                    return listToMerge;
                }

                Variable cell = current.Clone();
                cell.Action = action;

                bool addIt = UpdateIfBool(script, ref cell, ref listToMerge);
                if (addIt)
                {
                    listToMerge.Add(cell);
                }
                item.Clear();

            } while (script.StillValid() &&
                    (inQuotes || arrayIndexDepth > 0 || !to.Contains(script.Current)));

            // This happens when called recursively inside of the math expression:
            script.MoveForwardIf(Constants.END_ARG);

            return listToMerge;
        }

        static void CheckConsistency(string item, List<Variable> listToMerge,
                                             ParsingScript script)
        {
            if (Constants.CONTROL_FLOW.Contains(item) && listToMerge.Count > 0)
            {//&&
             //item != Constants.RETURN) {
             // This can happen when the end of statement ";" is forgotten.
                listToMerge.Clear();
                //throw new ArgumentException("Token [" +
                //   item + "] can't be part of an expression. Check \";\". Stopped at [" +
                //    script.Rest + " ...]");
            }
        }

        static void CheckQuotesIndices(ParsingScript script,
                            char ch, ref bool inQuotes, ref int arrayIndexDepth)
        {
            switch (ch)
            {
                case Constants.QUOTE:
                    {
                        char prev = script.TryPrevPrev();
                        inQuotes = prev != '\\' ? !inQuotes : inQuotes;
                        return;
                    }
                case Constants.START_ARRAY:
                    {
                        if (!inQuotes)
                        {
                            arrayIndexDepth++;
                        }
                        return;
                    }
                case Constants.END_ARRAY:
                    {
                        if (!inQuotes)
                        {
                            arrayIndexDepth--;
                        }
                        return;
                    }
            }
        }

        static bool CheckNegativeSign(ref string token)
        {
            if (token.Length < 2 || token[0] != '-' || token[1] == Constants.QUOTE)
            {
                return false;
            }
            double num = 0;
            if (Double.TryParse(token, NumberStyles.Number |
                   NumberStyles.AllowExponent |
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture, out num))
            {
                return false;
            }

            token = token.Substring(1);
            return true;
        }

        static void AppendIfNecessary(StringBuilder item, char ch, char[] to)
        {
            if (ch == Constants.END_ARRAY && to.Length == 1 && to[0] == Constants.END_ARRAY &&
                item.Length > 0 && item[item.Length - 1] != Constants.END_ARRAY)
            {
                item.Append(ch);
            }
        }
        private static bool SkipOrAppendIfNecessary(StringBuilder item, char ch, char[] to)
        {
            if (to.Length == 1 && to[0] == Constants.END_ARRAY && item.Length > 0)
            {
                if (ch == Constants.END_ARRAY && item[item.Length - 1] != Constants.END_ARRAY)
                {
                    item.Append(ch);
                }
                else if (item.Length == 1 && item[0] == Constants.END_ARRAY)
                {
                    return true;
                }
            }
            return false;
        }

        static bool StillCollecting(string item, char[] to, ParsingScript script,
                                    ref string action)
        {
            char prev = script.TryPrevPrev();
            char ch = script.TryPrev();
            char next = script.TryCurrent();

            if (to.Contains(ch) || ch == Constants.START_ARG ||
                                   ch == Constants.START_GROUP ||
                                 next == Constants.EMPTY)
            {
                return false;
            }

            // Case of a negative number, or starting with the closing bracket:
            if (item.Length == 0 &&
               ((ch == '-' && next != '-') || ch == Constants.END_ARRAY
                                           || ch == Constants.END_ARG))
            {
                return true;
            }

            // Case of a scientific notation 1.2e+5 or 1.2e-5 or 1e5:
            if (Char.ToUpper(prev) == 'E' &&
               (ch == '-' || ch == '+' || Char.IsDigit(ch)) &&
               item.Length > 1 && Char.IsDigit(item[item.Length - 2]))
            {
                return true;
            }

            // Otherwise if it's an action (+, -, *, etc.) or a space
            // we're done collecting current token.
            if ((action = Utils.ValidAction(script.FromPrev())) != null ||
                (item.Length > 0 && ch == Constants.SPACE))
            {
                return false;
            }

            if (ch == Constants.TERNARY_OPERATOR)
            {
                script.Backward();
                return false;
            }
            return true;
        }

        static bool UpdateIfTernary(ParsingScript script, string token, char ch, ref List<Variable> listToMerge)
        {
            if (listToMerge.Count < 1 || ch != Constants.TERNARY_OPERATOR || token.Length > 0)
            {
                return false;
            }

            Variable arg1 = MergeList(listToMerge);
            script.MoveForwardIf(Constants.TERNARY_OPERATOR);
            Variable arg2 = script.Execute(Constants.TERNARY_SEPARATOR);
            script.MoveForwardIf(Constants.TERNARY_SEPARATOR);
            Variable arg3 = script.Execute(Constants.NEXT_OR_END_ARRAY);
            script.MoveForwardIf(Constants.NEXT_OR_END_ARRAY);

            double condition = arg1.AsDouble();
            Variable result = condition != 0 ? arg2 : arg3;

            listToMerge.Clear();
            listToMerge.Add(result);

            return true;
        }

        static bool UpdateIfBool(ParsingScript script, ref Variable current, ref List<Variable> listToMerge)
        {
            // Short-circuit evaluation: check if don't need to evaluate more.
            bool needToAdd = true;
            if ((current.Action == "&&" || current.Action == "||") &&
                    listToMerge.Count > 0)
            {
                if (CanMergeCells(listToMerge.Last(), current))
                {
                    listToMerge.Add(current);
                    current = MergeList(listToMerge);
                    listToMerge.Clear();
                    needToAdd = false;
                }
            }
            if ((current.Action == "&&" && current.Value == 0.0) ||
                (current.Action == "||" && current.Value != 0.0))
            {
                Utils.SkipRestExpr(script);
                current.Action = Constants.NULL_ACTION;
                needToAdd = true;
            }
            return needToAdd;
        }

        private static string UpdateAction(ParsingScript script, char[] to)
        {
            // We search a valid action till we get to the End of Argument ')'
            // or pass the end of string.
            if (!script.StillValid() || script.Current == Constants.END_ARG ||
                to.Contains(script.Current))
            {
                return Constants.NULL_ACTION;
            }

            string action = Utils.ValidAction(script.Rest);

            // We need to advance forward not only the action length but also all
            // the characters we skipped before getting the action.
            int advance = action == null ? 0 : action.Length;
            script.Forward(advance);
            return action == null ? Constants.NULL_ACTION : action;
        }

        private static Variable MergeList(List<Variable> listToMerge)
        {
            if (listToMerge.Count == 0)
            {
                return Variable.EmptyInstance;
            }
            // If there is just one resulting cell there is no need
            // to perform the second step to merge tokens.
            if (listToMerge.Count == 1)
            {
                return listToMerge[0];
            }

            Variable baseCell = listToMerge[0];
            int index = 1;

            // Second step: merge list of cells to get the result of an expression.
            Variable result = Merge(baseCell, ref index, listToMerge);
            return result;
        }

        // From outside this function is called with mergeOneOnly = false.
        // It also calls itself recursively with mergeOneOnly = true, meaning
        // that it will return after only one merge.
        private static Variable Merge(Variable current, ref int index, List<Variable> listToMerge,
                                      bool mergeOneOnly = false)
        {
            if (Verbose)
            {
                Utils.PrintList(listToMerge, index - 1);
            }

            while (index < listToMerge.Count)
            {
                Variable next = listToMerge[index++];

                while (!CanMergeCells(current, next))
                {
                    // If we cannot merge cells yet, go to the next cell and merge
                    // next cells first. E.g. if we have 1+2*3, we first merge next
                    // cells, i.e. 2*3, getting 6, and then we can merge 1+6.
                    Merge(next, ref index, listToMerge, true /* mergeOneOnly */);
                }

                MergeCells(current, next);
                if (mergeOneOnly)
                {
                    break;
                }
            }

            if (Verbose)
            {
                Console.WriteLine("Calculated: {0} {1}",
                                current.Value, current.String);
            }
            return current;
        }

        private static void MergeCells(Variable leftCell, Variable rightCell)
        {
            if (leftCell.IsReturn ||
                leftCell.Type == Variable.VarType.BREAK ||
                leftCell.Type == Variable.VarType.CONTINUE)
            {
                // Done!
                return;
            }
            if (leftCell.Type == Variable.VarType.NUMBER)
            {
                MergeNumbers(leftCell, rightCell);
            }
            else
            {
                MergeStrings(leftCell, rightCell);
            }

            leftCell.Action = rightCell.Action;
        }

        private static void MergeNumbers(Variable leftCell, Variable rightCell)
        {
            if (rightCell.Type != Variable.VarType.NUMBER)
            {
                rightCell.Value = rightCell.AsDouble();
            }
            switch (leftCell.Action)
            {
                case "%":
                    leftCell.Value %= rightCell.Value;
                    break;
                case "*":
                    leftCell.Value *= rightCell.Value;
                    break;
                case "/":
                    if (rightCell.Value == 0.0)
                    {
                        throw new ArgumentException("Division by zero");
                    }
                    leftCell.Value /= rightCell.Value;
                    break;
                case "+":
                    if (rightCell.Type != Variable.VarType.NUMBER)
                    {
                        leftCell.String = leftCell.AsString() + rightCell.String;
                    }
                    else
                    {
                        leftCell.Value += rightCell.Value;
                    }
                    break;
                case "-":
                    leftCell.Value -= rightCell.Value;
                    break;
                case "<":
                    leftCell.Value = Convert.ToDouble(leftCell.Value < rightCell.Value);
                    break;
                case ">":
                    leftCell.Value = Convert.ToDouble(leftCell.Value > rightCell.Value);
                    break;
                case "<=":
                    leftCell.Value = Convert.ToDouble(leftCell.Value <= rightCell.Value);
                    break;
                case ">=":
                    leftCell.Value = Convert.ToDouble(leftCell.Value >= rightCell.Value);
                    break;
                case "==":
                    leftCell.Value = Convert.ToDouble(leftCell.Value == rightCell.Value);
                    break;
                case "!=":
                    leftCell.Value = Convert.ToDouble(leftCell.Value != rightCell.Value);
                    break;
                case "&":
                    leftCell.Value = (int)leftCell.Value & (int)rightCell.Value;
                    break;
                case "^":
                    leftCell.Value = (int)leftCell.Value ^ (int)rightCell.Value;
                    break;
                case "|":
                    leftCell.Value = (int)leftCell.Value | (int)rightCell.Value;
                    break;
                case "&&":
                    leftCell.Value = Convert.ToDouble(
           Convert.ToBoolean(leftCell.Value) && Convert.ToBoolean(rightCell.Value));
                    break;
                case "||":
                    leftCell.Value = Convert.ToDouble(
           Convert.ToBoolean(leftCell.Value) || Convert.ToBoolean(rightCell.Value));
                    break;
            }
        }

        static void MergeStrings(Variable leftCell, Variable rightCell)
        {
            switch (leftCell.Action)
            {
                case "+":
                    leftCell.String = leftCell.AsString() + rightCell.AsString();
                    break;
                case "<":
                    string arg1 = leftCell.AsString();
                    string arg2 = rightCell.AsString();
                    leftCell.Value = Convert.ToDouble(string.Compare(arg1, arg2) < 0);
                    break;
                case ">":
                    leftCell.Value = Convert.ToDouble(
                     string.Compare(leftCell.AsString(), rightCell.AsString()) > 0);
                    break;
                case "<=":
                    leftCell.Value = Convert.ToDouble(
                      string.Compare(leftCell.AsString(), rightCell.AsString()) <= 0);
                    break;
                case ">=":
                    leftCell.Value = Convert.ToDouble(
                      string.Compare(leftCell.AsString(), rightCell.AsString()) >= 0);
                    break;
                case "==":
                    leftCell.Value = Convert.ToDouble(
                     string.Compare(leftCell.AsString(), rightCell.AsString()) == 0);
                    break;
                case "!=":
                    leftCell.Value = Convert.ToDouble(
                      string.Compare(leftCell.AsString(), rightCell.AsString()) != 0);
                    break;
                case ")":
                    break;
                default:
                    throw new ArgumentException("Can't perform action [" +
                      leftCell.Action + "] on strings");
            }
        }

        static bool CanMergeCells(Variable leftCell, Variable rightCell)
        {
            return GetPriority(leftCell.Action) >= GetPriority(rightCell.Action);
        }

        static int GetPriority(string action)
        {
            switch (action)
            {
                case "++":
                case "--": return 11;
                case "%":
                case "*":
                case "/": return 10;
                case "+":
                case "-": return 9;
                case "<":
                case ">":
                case ">=":
                case "<=": return 8;
                case "==":
                case "!=": return 7;
                case "&": return 6;
                case "|": return 5;
                case "^": return 4;
                case "&&": return 3;
                case "||": return 2;
                case "+=":
                case "-=":
                case "*=":
                case "/=":
                case "%=":
                case "=": return 1;
            }
            return 0; // NULL action has priority 0.
        }
    }
}
