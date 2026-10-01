using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SplitAndMerge
{
    /// <summary>
    /// Roslyn-based replacement for the CodeDom CSharpCodeProvider backend.
    ///
    /// CodeDom's CompileAssemblyFromSource throws PlatformNotSupportedException on
    /// .NET Core / .NET 5+, which silently disabled the whole cfunction/dllfunction
    /// feature once the project moved off .NET Framework.
    /// </summary>
    public static class RoslynCompiler
    {
        static List<MetadataReference> s_references;
        static string s_referencesFingerprint;
        static readonly object s_lock = new object();

        /// <summary>
        /// Where compiled assemblies are cached between runs. Set to null or empty,
        /// or set CacheEnabled to false, to compile every time.
        /// </summary>
        public static string CacheDirectory { get; set; } =
            Path.Combine(Path.GetTempPath(), "cscs-precompiled");

        public static bool CacheEnabled { get; set; } = true;

        /// <summary>Compiled assemblies loaded during this process, keyed by source hash.</summary>
        static readonly Dictionary<string, Assembly> s_memoryCache = new Dictionary<string, Assembly>();

        public static int CacheHits { get; private set; }
        public static int CacheMisses { get; private set; }

        public static IReadOnlyList<MetadataReference> GetReferences()
        {
            lock (s_lock)
            {
                if (s_references != null)
                {
                    return s_references;
                }
                var refs = new List<MetadataReference>();
                var paths = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string location;
                    try
                    {
                        if (asm.IsDynamic) continue;
                        location = asm.Location;
                    }
                    catch { continue; }

                    if (string.IsNullOrWhiteSpace(location) || !File.Exists(location) ||
                        !seen.Add(location))
                    {
                        continue;
                    }
                    try
                    {
                        refs.Add(MetadataReference.CreateFromFile(location));
                        paths.Add(location);
                    }
                    catch { }
                }
                paths.Sort(StringComparer.OrdinalIgnoreCase);
                var fingerprint = new StringBuilder();
                foreach (var path in paths)
                {
                    fingerprint.Append(path).Append('|');
                    try { fingerprint.Append(File.GetLastWriteTimeUtc(path).Ticks); }
                    catch { }
                    fingerprint.Append('\n');
                }
                s_referencesFingerprint = Hash(fingerprint.ToString());
                s_references = refs;
                return s_references;
            }
        }

        /// <summary>
        /// Forgets the cached reference set. Call after loading assemblies that compiled
        /// scripts need to see (a module DLL, for instance).
        /// </summary>
        public static void ResetReferences()
        {
            lock (s_lock)
            {
                s_references = null;
                s_referencesFingerprint = null;
            }
        }

        static readonly HashSet<SyntaxKind> s_comparisons = new HashSet<SyntaxKind>
        {
            SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression,
            SyntaxKind.LessThanExpression, SyntaxKind.GreaterThanExpression,
            SyntaxKind.LessThanOrEqualExpression, SyntaxKind.GreaterThanOrEqualExpression,
        };

        /// <summary>
        /// Rewrites each comparison that failed with CS0019 because one side is a Variable --
        /// "request == \"stock\"" on a variable argument, a Variable against a bool -- into
        /// CscsOps.Compare, which asks the interpreter. Which expressions those are comes from
        /// Roslyn's own diagnostics and semantic model, so no builder of the translator has to
        /// know; and only code that did not compile is touched. Repeats for nested cases.
        /// Returns the source unchanged when there is nothing of that kind to repair.
        /// </summary>
        public static string RepairOperators(string source, Func<string, bool> isScriptFunction = null)
        {
            GetReferences();
            // Each round repairs the outermost targets, so a chain takes one round per operator:
            // "!a && !b && s != \"\" && !c.Contains(s)" on late globals needed six.
            for (int round = 0; round < 8; round++)
            {
                var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
                var compilation = CSharpCompilation.Create("CscsRepair", new[] { tree }, GetReferences(),
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                var model = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot();
                // Each target and the expression it becomes. Targets never nest within a round;
                // an inner one is taken up by the next.
                var targets = new Dictionary<ExpressionSyntax, string>();
                void Add(ExpressionSyntax node, string replacement)
                {
                    if (node == null || targets.Keys.Any(t => t.Span.Contains(node.Span) || node.Span.Contains(t.Span)))
                    {
                        return;
                    }
                    targets[node] = replacement;
                }
                foreach (var diagnostic in compilation.GetDiagnostics())
                {
                    if (diagnostic.Severity != DiagnosticSeverity.Error)
                    {
                        continue;
                    }
                    var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
                    // "s.Trim" without parentheses on a string -- returned, joined into text,
                    // passed on: the interpreter's property, the trimmed text; in C# a method
                    // group, which is no value (CS0428, CS0019, CS1503 by where it stands).
                    var bareTrim = diagnostic.Id == "CS0428" || diagnostic.Id == "CS0019" || diagnostic.Id == "CS1503" ?
                        node.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>().FirstOrDefault(g =>
                            g.Name.Identifier.Text == "Trim" && !(g.Parent is InvocationExpressionSyntax) &&
                            IsString(model, g.Expression)) : null;
                    if (bareTrim != null)
                    {
                        Add(bareTrim, bareTrim + "()");
                    }
                    else if (diagnostic.Id == "CS0019")
                    {
                        var binary = node.AncestorsAndSelf().OfType<BinaryExpressionSyntax>()
                            .FirstOrDefault(b => b.Span == diagnostic.Location.SourceSpan);
                        if (binary == null)
                        {
                            continue;
                        }
                        if (s_comparisons.Contains(binary.Kind()) &&
                            (IsVariable(model, binary.Left) || IsVariable(model, binary.Right) ||
                             // Text ordered against text -- "w.Upper > \"AA\"": C# has no "<" on a
                             // string, the interpreter orders them.
                             (IsString(model, binary.Left) && IsString(model, binary.Right))))
                        {
                            Add(binary, "CscsOps.Compare(__interpreter, (object)(" + binary.Left + "), \"" +
                                        binary.OperatorToken.Text + "\", (object)(" + binary.Right + "))");
                        }
                        else if (s_comparisons.Contains(binary.Kind()) &&
                                 ((IsVarType(model, binary.Left) && IsString(model, binary.Right)) ||
                                  (IsString(model, binary.Left) && IsVarType(model, binary.Right))))
                        {
                            // "e.type != \"NUMBER\"" on an untyped argument: the member is the
                            // Variable's VarType, whose name is the text the interpreter answers.
                            var left = IsVarType(model, binary.Left) ? "(" + binary.Left + ").ToString()" : binary.Left.ToString();
                            var right = IsVarType(model, binary.Right) ? "(" + binary.Right + ").ToString()" : binary.Right.ToString();
                            Add(binary, "CscsOps.Compare(__interpreter, (object)(" + left + "), \"" +
                                        binary.OperatorToken.Text + "\", (object)(" + right + "))");
                        }
                        else if ((binary.IsKind(SyntaxKind.LeftShiftExpression) || binary.IsKind(SyntaxKind.RightShiftExpression)) &&
                                 !IsBool(model, binary.Left) && !IsBool(model, binary.Right))
                        {
                            // "x << 1" with x a double or a Variable: C# shifts only integers. The
                            // interpreter's shift truncates both sides to int (Parser.MergeNumbers).
                            Add(binary, "CscsOps.Apply(__interpreter, (object)(" + binary.Left + "), \"" +
                                        binary.OperatorToken.Text + "\", (object)(" + binary.Right + "))");
                        }
                        else if (s_arithmetic.Contains(binary.Kind()) &&
                                 (IsVariable(model, binary.Left) || IsVariable(model, binary.Right) ||
                                  // Text with a number -- "4 * \"ab\"" is "4ab" to the interpreter.
                                  IsString(model, binary.Left) || IsString(model, binary.Right)) &&
                                 !IsBool(model, binary.Left) && !IsBool(model, binary.Right))
                        {
                            // "(n | 4) ^ 1" with n a Variable. Never with a C# bool on either side:
                            // that bool may be C#'s own reference test of a Variable against null,
                            // which is not the interpreter's answer.
                            Add(binary, "CscsOps.Apply(__interpreter, (object)(" + binary.Left + "), \"" +
                                        binary.OperatorToken.Text + "\", (object)(" + binary.Right + "))");
                        }
                        else if (s_arithmetic.Contains(binary.Kind()) &&
                                 (IsCalledBool(model, binary.Left) || IsCalledBool(model, binary.Right)))
                        {
                            // "s.Contains(\"a\") + 1": the member's truth value is the number 1 or 0
                            // to the interpreter. A called member's bool, never a comparison's, which
                            // may be C#'s own reference test against null (see above).
                            var left = IsCalledBool(model, binary.Left) ? "((" + binary.Left + ") ? 1 : 0)" : binary.Left.ToString();
                            var right = IsCalledBool(model, binary.Right) ? "((" + binary.Right + ") ? 1 : 0)" : binary.Right.ToString();
                            Add(binary, left + " " + binary.OperatorToken.Text + " " + right);
                        }
                        else if (binary.IsKind(SyntaxKind.LogicalAndExpression) || binary.IsKind(SyntaxKind.LogicalOrExpression))
                        {
                            // "s && n > 1" with s a Variable: the interpreter's own operator
                            // (CscsOps.And / Or), 1 or 0 by the one truth rule. The right side
                            // stays a lambda: it runs only where the interpreter's does.
                            var helper = binary.IsKind(SyntaxKind.LogicalAndExpression) ? "And" : "Or";
                            Add(binary, "CscsOps." + helper + "(__interpreter, (object)(" + binary.Left +
                                        "), () => (object)(" + binary.Right + "))");
                        }
                    }
                    else if (diagnostic.Id == "CS0029")
                    {
                        // A Variable or text as a whole condition -- "if (flag)" on a global, "s ? a : b"
                        // on a string: the one truth rule (CscsConvert.IsTrue, Variable.IsTrue).
                        var expression = node as ExpressionSyntax ?? node.AncestorsAndSelf().OfType<ExpressionSyntax>()
                            .FirstOrDefault(x => x.Span == diagnostic.Location.SourceSpan);
                        if (expression != null && IsCondition(expression) &&
                            (IsVariable(model, expression) || IsString(model, expression)))
                        {
                            Add(expression, "CscsConvert.IsTrue(" + expression + ")");
                        }
                        // A number as a whole condition -- "if (x.Size * 2)", "if (x.IndexOf(\"b\"))",
                        // "if (int(x))" -- or a type, whose name is text: the one truth rule, NaN
                        // included (Variable.IsTrue). Until September 2026 a member's own number
                        // had answers of its own there, so only arithmetic was repaired.
                        else if (expression != null && IsCondition(expression) &&
                                 (IsNumeric(model, expression) || IsVarType(model, expression)))
                        {
                            // "m[\"k\"].AsDouble()" is the translator's reading of a Variable as a
                            // number, which loses text: the Variable itself is what is tested.
                            // (The script's own double(x) is CscsConvert.ToNumber.)
                            var tested = Unparenthesized(expression) is InvocationExpressionSyntax asDouble &&
                                         asDouble.ArgumentList.Arguments.Count == 0 &&
                                         asDouble.Expression is MemberAccessExpressionSyntax asDoubleMember &&
                                         asDoubleMember.Name.Identifier.Text == "AsDouble" &&
                                         IsVariable(model, asDoubleMember.Expression) ?
                                         asDoubleMember.Expression.ToString() :
                                         IsVarType(model, expression) ? "(" + expression + ").ToString()" : expression.ToString();
                            Add(expression, "CscsConvert.IsTrue((object)(" + tested + "))");
                        }
                        // A number, text or truth value stored into a Variable -- "v = 5" with v an
                        // argument declared "variable" (its slot is a Variable). The interpreter
                        // stores the value as it is; ConvertToVariable is how compiled code does.
                        else if (expression != null && expression.Parent is AssignmentExpressionSyntax store &&
                                 store.IsKind(SyntaxKind.SimpleAssignmentExpression) && store.Right == expression &&
                                 IsVariable(model, store.Left) &&
                                 (IsNumeric(model, expression) || IsString(model, expression) || IsBool(model, expression)))
                        {
                            Add(expression, "Variable.ConvertToVariable(" + expression + ")");
                        }
                        // Text joined with a Variable, stored into a string local -- "alert = \"<p>\" +
                        // values[\"name\"] + \"</p>\"": the interpreter joins that into text whatever
                        // the element holds, so the Variable C#'s "+" gives is read as its text.
                        else if (expression != null && expression.Parent is AssignmentExpressionSyntax textStore &&
                                 textStore.IsKind(SyntaxKind.SimpleAssignmentExpression) && textStore.Right == expression &&
                                 IsString(model, textStore.Left) && IsVariable(model, expression) &&
                                 JoinsText(model, expression))
                        {
                            Add(expression, "(" + expression + ").AsString()");
                        }
                        // A Variable added onto a string local -- "t += (s && 5)": the interpreter
                        // joins its text. The error is on the whole assignment.
                        else if (expression is AssignmentExpressionSyntax textAdd &&
                                 textAdd.IsKind(SyntaxKind.AddAssignmentExpression) &&
                                 IsString(model, textAdd.Left) && IsVariable(model, textAdd.Right))
                        {
                            Add(textAdd.Right, "(" + textAdd.Right + ").AsString()");
                        }
                    }
                    else if (diagnostic.Id == "CS1503" && isScriptFunction != null)
                    {
                        // "Math.Max(b, 5)" with b a Variable: the interpreter's own Math function,
                        // run on the values (CscsCalls.Builtin), as for an untyped argument.
                        // The call the argument belongs to -- not a call the argument is itself,
                        // "Math.Pow(CscsLate.Element(a, 1), 2)" (element reads are calls now).
                        var mathArgument = node.AncestorsAndSelf().OfType<ArgumentSyntax>().FirstOrDefault();
                        var invocation = mathArgument?.Parent?.Parent as InvocationExpressionSyntax ??
                                         node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
                        if (invocation?.Expression is MemberAccessExpressionSyntax access &&
                            access.Expression.ToString() == "Math" &&
                            invocation.ArgumentList.Arguments.Any(a => IsVariable(model, a.Expression)) &&
                            !invocation.ArgumentList.Arguments.Any(a => IsBool(model, a.Expression)))
                        {
                            var member = access.Name.Identifier.Text;
                            var name = "Math." + (member == "Ceiling" ? "Ceil" : member);
                            if (isScriptFunction(name))
                            {
                                var call = new StringBuilder("CscsCalls.Builtin(__interpreter, \"" + name + "\"");
                                foreach (var argument in invocation.ArgumentList.Arguments)
                                {
                                    call.Append(", (object)(").Append(argument.Expression).Append(")");
                                }
                                Add(invocation, call.Append(")").ToString());
                            }
                        }
                    }
                    else if (diagnostic.Id == "CS0117" && isScriptFunction != null)
                    {
                        // "Math.Random(n)": a Math function CSCS has and System.Math does not. The
                        // interpreter's own runs it, on the values (CscsCalls.Builtin).
                        var access = node.AncestorsAndSelf().OfType<MemberAccessExpressionSyntax>().FirstOrDefault();
                        if (access?.Parent is InvocationExpressionSyntax invocation && access.Expression.ToString() == "Math" &&
                            isScriptFunction("Math." + access.Name.Identifier.Text) &&
                            !invocation.ArgumentList.Arguments.Any(a => IsBool(model, a.Expression)))
                        {
                            var call = new StringBuilder("CscsCalls.Builtin(__interpreter, \"Math." + access.Name.Identifier.Text + "\"");
                            foreach (var argument in invocation.ArgumentList.Arguments)
                            {
                                call.Append(", (object)(").Append(argument.Expression).Append(")");
                            }
                            call.Append(")");
                            // Math.Random with at most one argument answers a number (with two, a
                            // list), and the code around a Math call expects the double C# gives.
                            if (access.Name.Identifier.Text == "Random" && invocation.ArgumentList.Arguments.Count <= 1)
                            {
                                call.Append(".AsDouble()");
                            }
                            Add(invocation, call.ToString());
                        }
                    }
                    else if (diagnostic.Id == "CS1061")
                    {
                        // "a[0].substring(0, 3)": script members are case-blind, and the Variable
                        // member of that name is the one the interpreter runs.
                        var access = node.AncestorsAndSelf().OfType<MemberAccessExpressionSyntax>().FirstOrDefault();
                        if (access != null && IsVariable(model, access.Expression))
                        {
                            var name = access.Name.Identifier.Text;
                            var canonical = Precompiler.CanonicalVariableMember(name);
                            if (canonical != name && string.Equals(canonical, name, StringComparison.OrdinalIgnoreCase))
                            {
                                Add(access, "(" + access.Expression + ")." + canonical);
                            }
                            // "ex.Message" -- a property Variable has no member for, read (not
                            // called): the interpreter's property lookup, and its error when the
                            // value has no such property ("Object [message] doesn't exist").
                            else if (!(access.Parent is InvocationExpressionSyntax call && call.Expression == access) &&
                                     !(access.Parent is AssignmentExpressionSyntax store && store.Left == access))
                            {
                                Add(access, "(" + access.Expression + ").ReadField(\"" + name + "\")");
                            }
                        }
                    }
                    else if (diagnostic.Id == "CS1059")
                    {
                        // "++adCounter" on a global read by name -- "!p && ++adCounter % 3 == 0",
                        // where the step cannot be moved out of the statement: stepped in place,
                        // as the interpreter steps a variable (CscsLate.Step).
                        var step = node.AncestorsAndSelf().FirstOrDefault(s =>
                            s is PrefixUnaryExpressionSyntax || s is PostfixUnaryExpressionSyntax) as ExpressionSyntax;
                        var operand = (step as PrefixUnaryExpressionSyntax)?.Operand ?? (step as PostfixUnaryExpressionSyntax)?.Operand;
                        var name = NameReadByName(operand);
                        // "++p.x": a field of a Variable, read through ReadField.
                        if (step != null && name == null && Unparenthesized(operand) is InvocationExpressionSyntax field &&
                            field.Expression is MemberAccessExpressionSyntax read && read.Name.Identifier.Text == "ReadField" &&
                            field.ArgumentList.Arguments.Count == 1)
                        {
                            var fieldOp = step.ToString().Contains("++") ? "++" : "--";
                            Add(step, "CscsFields.StepField(" + read.Expression + ", " + field.ArgumentList.Arguments[0] +
                                      ", \"" + fieldOp + "\", " + (step is PrefixUnaryExpressionSyntax ? "true" : "false") + ")");
                        }
                        else if (step != null && name != null)
                        {
                            var op = step.ToString().Contains("++") ? "++" : "--";
                            Add(step, "CscsLate.Step(__interpreter, \"" + name + "\", \"" + op + "\", " +
                                      (step is PrefixUnaryExpressionSyntax ? "true" : "false") + ")");
                        }
                    }
                    else if (diagnostic.Id == "CS0173")
                    {
                        // "isEdit ? lineId : \"(new)\"": a Variable against text, a number or a bool.
                        // The interpreter's "?:" yields whichever branch it takes, as it is; the
                        // other branch as a Variable gives both one type.
                        var ternary = node.AncestorsAndSelf().OfType<ConditionalExpressionSyntax>()
                            .FirstOrDefault(t => t.Span == diagnostic.Location.SourceSpan) ??
                            node.AncestorsAndSelf().OfType<ConditionalExpressionSyntax>().FirstOrDefault();
                        if (ternary != null)
                        {
                            bool whenVariable = IsVariable(model, ternary.WhenTrue);
                            bool elseVariable = IsVariable(model, ternary.WhenFalse);
                            var other = whenVariable && !elseVariable ? ternary.WhenFalse :
                                        elseVariable && !whenVariable ? ternary.WhenTrue : null;
                            if (other != null && (IsString(model, other) || IsNumeric(model, other) || IsBool(model, other) ||
                                                  other.IsKind(SyntaxKind.NullLiteralExpression)))
                            {
                                Add(other, "Variable.ConvertToVariable(" + other + ")");
                            }
                        }
                    }
                    else if (diagnostic.Id == "CS0200")
                    {
                        // "b.l[0] = x", "b.m[\"k\"] += x", "b.l[1]++" on a field's collection: a
                        // Variable, whose C# indexer only reads. Written as the translator writes a
                        // local's element (SetVariable), into the field's own collection, which is
                        // the interpreter's too; a compound or step through its own operator.
                        var element = node.AncestorsAndSelf().OfType<ElementAccessExpressionSyntax>().FirstOrDefault();
                        var stepNode = element?.Parent as ExpressionSyntax;
                        if (element != null && element.ArgumentList.Arguments.Count == 1 && IsVariable(model, element.Expression) &&
                            (stepNode is PostfixUnaryExpressionSyntax || stepNode is PrefixUnaryExpressionSyntax))
                        {
                            // A step, in a statement or as a value: CscsFields.StepElement.
                            var stepOp = (stepNode as PostfixUnaryExpressionSyntax)?.OperatorToken.Text ??
                                         ((PrefixUnaryExpressionSyntax)stepNode).OperatorToken.Text;
                            Add(stepNode, "CscsFields.StepElement(" + element.Expression + ", (object)(" +
                                element.ArgumentList.Arguments[0] + "), \"" + stepOp + "\", " +
                                (stepNode is PrefixUnaryExpressionSyntax ? "true" : "false") + ")");
                        }
                        else if (element != null && element.ArgumentList.Arguments.Count == 1 &&
                            IsVariable(model, element.Expression) && element.Parent?.Parent is ExpressionStatementSyntax)
                        {
                            var holder = element.Expression.ToString();
                            var index = "Variable.ConvertToVariable(" + element.ArgumentList.Arguments[0] + ")";
                            var read = "CscsLate.Element(" + holder + ", (object)(" + element.ArgumentList.Arguments[0] + "))";
                            string value = null;
                            if (element.Parent is AssignmentExpressionSyntax store && store.Left == element)
                            {
                                var op = store.OperatorToken.Text;
                                value = op == "=" ? "Variable.ConvertToVariable(" + store.Right + ")" :
                                    "CscsOps.Compound(" + read + ", \"" + op + "\", (object)(" + store.Right + "))";
                            }
                            else if (element.Parent is PostfixUnaryExpressionSyntax post)
                            {
                                value = "CscsOps.Compound(" + read + ", \"" + post.OperatorToken.Text + "\", null)";
                            }
                            else if (element.Parent is PrefixUnaryExpressionSyntax pre)
                            {
                                value = "CscsOps.Compound(" + read + ", \"" + pre.OperatorToken.Text + "\", null)";
                            }
                            if (value != null)
                            {
                                Add((ExpressionSyntax)element.Parent, "(" + holder + ").SetVariable(" + index + ", " + value + ")");
                            }
                        }
                    }
                    else if (diagnostic.Id == "CS1955")
                    {
                        // "s.Length()" or "s.Size()" on a string: the script's empty "()" after a
                        // property, which the interpreter consumes (Variable.ConsumeEmptyCall).
                        var call = node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
                        if (call?.Expression is MemberAccessExpressionSyntax property &&
                            property.Name.Identifier.Text == "Length" && call.ArgumentList.Arguments.Count == 0)
                        {
                            Add(call, property.ToString());
                        }
                    }
                    else if (diagnostic.Id == "CS0023")
                    {
                        // "!v": the opposite of the one truth rule (Variable.IsTrue).
                        var not = node.AncestorsAndSelf().OfType<PrefixUnaryExpressionSyntax>()
                            .FirstOrDefault(u => u.IsKind(SyntaxKind.LogicalNotExpression));
                        if (not != null && (IsVariable(model, not.Operand) || IsString(model, not.Operand)))
                        {
                            // The interpreter's value of "!v" (CscsOps.Not); in a condition the
                            // truth repair then tests it as the interpreter tests one.
                            Add(not, "CscsOps.Not((object)(" + not.Operand + "))");
                        }
                        else if (not != null && IsNumeric(model, not.Operand))
                        {
                            // "!running" on a local held as a number: true exactly for 0.
                            Add(not, "((" + not.Operand + ") == 0)");
                        }
                    }
                }
                if (targets.Count == 0)
                {
                    return source;
                }
                var repaired = root.ReplaceNodes(targets.Keys, (original, rewritten) =>
                    SyntaxFactory.ParseExpression(targets[original]).WithTriviaFrom(original));
                source = repaired.ToFullString();
            }
            return source;
        }

        /// <summary>
        /// Whether the method writes a number into text with C#'s own formatting -- "+" between a
        /// string and a number, or a number in an interpolated string -- which is not how the
        /// interpreter writes one. Checked on the typed-return variant, whose doubles were Variables.
        /// </summary>
        public static bool JoinsNumberToText(string source, string methodName)
        {
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
            var compilation = CSharpCompilation.Create("CscsTypedCheck", new[] { tree }, GetReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == methodName);
            if (method == null)
            {
                return true;
            }
            foreach (var node in method.DescendantNodes())
            {
                if (node is InterpolatedStringExpressionSyntax)
                {
                    return true;
                }
                if (node is BinaryExpressionSyntax add && add.IsKind(SyntaxKind.AddExpression))
                {
                    bool leftText = IsString(model, add.Left), rightText = IsString(model, add.Right);
                    if ((leftText && IsNumeric(model, add.Right)) || (rightText && IsNumeric(model, add.Left)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        static bool IsNumeric(SemanticModel model, ExpressionSyntax expression)
        {
            switch (model.GetTypeInfo(expression).Type?.SpecialType)
            {
                case SpecialType.System_Double:
                case SpecialType.System_Single:
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                case SpecialType.System_Decimal:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// "x == y" and "x != y" with a Variable on both sides compile in C# -- as a comparison of
        /// references, which two values never share: "3 == 3" answered 0. No compile error points
        /// at it, so this runs on every translation: each such comparison becomes the
        /// interpreter's (CscsOps.Compare, Parser.MergeCells).
        /// </summary>
        public static string FixVariableEquality(string source)
        {
            if (string.IsNullOrEmpty(source) || (source.IndexOf("==", StringComparison.Ordinal) < 0 &&
                                                 source.IndexOf("!=", StringComparison.Ordinal) < 0))
            {
                return source;
            }
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
            var compilation = CSharpCompilation.Create("CscsEquality", new[] { tree }, GetReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var targets = root.DescendantNodes().OfType<BinaryExpressionSyntax>()
                .Where(e => (e.IsKind(SyntaxKind.EqualsExpression) || e.IsKind(SyntaxKind.NotEqualsExpression)) &&
                            IsVariable(model, e.Left) && IsVariable(model, e.Right))
                .ToList();
            if (targets.Count == 0)
            {
                return source;
            }
            // Innermost first is not needed: operands keep their text, and ReplaceNodes rewrites
            // nested matches through the "rewritten" node it hands over.
            var repaired = root.ReplaceNodes(targets, (original, rewritten) =>
            {
                var b = (BinaryExpressionSyntax)rewritten;
                return SyntaxFactory.ParseExpression("CscsOps.Compare(__interpreter, (object)(" + b.Left + "), \"" +
                    b.OperatorToken.Text + "\", (object)(" + b.Right + "))").WithTriviaFrom(original);
            });
            return repaired.ToFullString();
        }

        /// <summary>
        /// A local that stands for one of the interpreter's variables -- a global the function
        /// assigns -- is read from the interpreter, not from the C# local, wherever the function
        /// can call back into the interpreter: "g = 1; bump(); return g;" answered 1 where bump()
        /// had set the global to 5. Only a name every write of which is published right after it
        /// (AddCompiledLocalVariable), so the interpreter's value is the local's unless a callback
        /// changed it; a compound write is spelt out so that it reads the current value too.
        /// </summary>
        public static string ReadGlobalsThroughInterpreter(string source, ICollection<string> names)
        {
            if (string.IsNullOrEmpty(source) || names == null || names.Count == 0)
            {
                return source;
            }
            var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
            var compilation = CSharpCompilation.Create("CscsGlobals", new[] { tree }, GetReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();

            // The locals in question, with the reads and the writes of each.
            var locals = new Dictionary<ILocalSymbol, List<IdentifierNameSyntax>>(SymbolEqualityComparer.Default);
            var unsafeLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
            foreach (var id in root.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (!wanted.Contains(id.Identifier.ValueText) ||
                    !(model.GetSymbolInfo(id).Symbol is ILocalSymbol local) || ReadTarget(local.Type) == null)
                {
                    continue;
                }
                if (!locals.TryGetValue(local, out var uses))
                {
                    locals[local] = uses = new List<IdentifierNameSyntax>();
                }
                uses.Add(id);
                if (IsWrite(id) && !IsPublishedWrite(id, local.Name))
                {
                    unsafeLocals.Add(local);
                }
            }
            foreach (var local in locals.Keys)
            {
                foreach (var declarator in local.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<VariableDeclaratorSyntax>())
                {
                    if (declarator.Initializer != null && !IsPublished(declarator.Parent?.Parent as StatementSyntax, local.Name))
                    {
                        unsafeLocals.Add(local);
                    }
                }
            }

            var replacements = new Dictionary<SyntaxNode, Func<SyntaxNode, SyntaxNode>>();
            foreach (var entry in locals)
            {
                if (unsafeLocals.Contains(entry.Key))
                {
                    continue;
                }
                var read = ReadTarget(entry.Key.Type).Replace("NAME", entry.Key.Name);
                foreach (var id in entry.Value)
                {
                    if (InsidePublish(id))
                    {
                        continue;
                    }
                    if (IsWrite(id))
                    {
                        // "g += x" and "g++" as statements: the old value is the interpreter's.
                        var statement = id.Parent?.Parent as ExpressionStatementSyntax;
                        string op = null;
                        bool compound = id.Parent is AssignmentExpressionSyntax assignment && assignment.Left == id &&
                                        !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression);
                        if (compound)
                        {
                            op = ((AssignmentExpressionSyntax)id.Parent).OperatorToken.Text.TrimEnd('=');
                        }
                        else if (id.Parent is PostfixUnaryExpressionSyntax || id.Parent is PrefixUnaryExpressionSyntax)
                        {
                            op = id.Parent.ToString().Contains("++") ? "+" : "-";
                        }
                        if (op != null && statement != null && op.Length == 1 && "+-*/%".Contains(op))
                        {
                            var name = entry.Key.Name;
                            replacements[id.Parent] = rewritten => SyntaxFactory.ParseExpression(
                                name + " = " + read + " " + op + " (" +
                                (compound ? ((AssignmentExpressionSyntax)rewritten).Right.ToString() : "1") + ")")
                                .WithTriviaFrom(rewritten);
                        }
                        continue;
                    }
                    replacements[id] = rewritten => SyntaxFactory.ParseExpression(read).WithTriviaFrom(rewritten);
                }
            }
            if (replacements.Count == 0)
            {
                return source;
            }
            return root.ReplaceNodes(replacements.Keys, (original, rewritten) => replacements[original](rewritten)).ToFullString();
        }

        static string ReadTarget(ITypeSymbol type)
        {
            switch (type?.SpecialType)
            {
                case SpecialType.System_Double: return "CscsLate.Current(__interpreter, \"NAME\").AsDouble()";
                case SpecialType.System_String: return "CscsLate.Current(__interpreter, \"NAME\").AsString()";
                case SpecialType.System_Int32: return "((int)CscsLate.Current(__interpreter, \"NAME\").AsDouble())";
                case SpecialType.System_Boolean: return "CscsLate.Current(__interpreter, \"NAME\").AsBool()";
                default:
                    return type?.Name == "Variable" ? "CscsLate.Current(__interpreter, \"NAME\")" : null;
            }
        }

        static bool IsWrite(IdentifierNameSyntax id)
        {
            switch (id.Parent)
            {
                case AssignmentExpressionSyntax a: return a.Left == id;
                case PostfixUnaryExpressionSyntax _: return true;
                case PrefixUnaryExpressionSyntax p:
                    return p.IsKind(SyntaxKind.PreIncrementExpression) || p.IsKind(SyntaxKind.PreDecrementExpression);
                case ArgumentSyntax arg: return !arg.RefKindKeyword.IsKind(SyntaxKind.None);
                default: return false;
            }
        }

        // A write standing as a statement of its own, with the publish of the name right after.
        static bool IsPublishedWrite(IdentifierNameSyntax id, string name)
        {
            return id.Parent?.Parent is ExpressionStatementSyntax statement && IsPublished(statement, name);
        }

        static bool IsPublished(StatementSyntax statement, string name)
        {
            if (!(statement?.Parent is BlockSyntax block))
            {
                return false;
            }
            int at = block.Statements.IndexOf(statement);
            if (at < 0 || at + 1 >= block.Statements.Count ||
                !(block.Statements[at + 1] is ExpressionStatementSyntax next) ||
                !(next.Expression is InvocationExpressionSyntax call))
            {
                return false;
            }
            var text = call.Expression.ToString();
            return (text == "__interpreter.AddCompiledLocalVariable" || text == "__interpreter.AddCompiledLocalOnlyVariable") &&
                   call.ArgumentList.Arguments.Count > 0 &&
                   call.ArgumentList.Arguments[0].ToString() == "\"" + name + "\"";
        }

        static bool InsidePublish(SyntaxNode node)
        {
            return node.Ancestors().OfType<InvocationExpressionSyntax>().Any(call =>
            {
                var text = call.Expression.ToString();
                return text == "__interpreter.AddCompiledLocalVariable" ||
                       text == "__interpreter.AddCompiledLocalOnlyVariable" ||
                       text == "__interpreter.AddGlobalOrLocalVariable";
            });
        }

        /// <summary>
        /// A C# bool joined into text -- "\"<\" + s.Contains(\"a\") + \">\"", "\"f=\" + bool(x)" --
        /// is "True" or "False"; the interpreter's truth values are the numbers 1 and 0, and that
        /// is what it joins. No compile error points at it, so this runs on every translation.
        /// </summary>
        public static string NumbersForBoolsInText(string source)
        {
            if (string.IsNullOrEmpty(source) || source.IndexOf('"') < 0)
            {
                return source;
            }
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
            var compilation = CSharpCompilation.Create("CscsBoolText", new[] { tree }, GetReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var targets = new List<ExpressionSyntax>();
            foreach (var add in root.DescendantNodes().OfType<BinaryExpressionSyntax>()
                         .Where(e => e.IsKind(SyntaxKind.AddExpression)))
            {
                if (IsString(model, add.Left) && IsBool(model, add.Right))
                {
                    targets.Add(add.Right);
                }
                else if (IsString(model, add.Right) && IsBool(model, add.Left))
                {
                    targets.Add(add.Left);
                }
            }
            if (targets.Count == 0)
            {
                return source;
            }
            return root.ReplaceNodes(targets, (original, rewritten) =>
                SyntaxFactory.ParseExpression("((" + rewritten + ") ? 1 : 0)").WithTriviaFrom(original)).ToFullString();
        }

        /// <summary>
        /// Two things C# does to a Variable in its own way, found by a generated audit of
        /// statements over every kind of value:
        /// "x += e" (and -=, *=, /=, %=) on a Variable went through Variable's "+": the
        /// interpreter's compound operator is another thing -- "x += \"!\"" leaves a number as it
        /// was, "x *= 3" leaves text -- and runs on a copy, which it writes back (CscsOps.Compound).
        /// "v[i]" read off a Variable went through its C# indexer, which answers an empty value
        /// for a value that is not a collection or an index past the end; the interpreter's
        /// element read fails there (CscsLate.Element, Utils.ExtractArrayElement).
        /// </summary>
        /// <summary>
        /// CSCS has one kind of number, a double. A local C# infers as an int -- "var x = __p0" from
        /// an int argument -- divided by 0 with "x /= 0" threw DivideByZeroException where the
        /// interpreter answers NaN, overflowed with "x *= n" (1410065408 for 10^10), and gave 0
        /// for -2 % 2 where the interpreter writes -0. Such a local is declared double, and "%"
        /// between two ints is computed in doubles. Loop counters proven to stay integers are
        /// declared "int" explicitly (FindIntCounters) and keep that.
        /// </summary>
        public static string WidenIntArithmetic(string source)
        {
            if (string.IsNullOrEmpty(source) ||
                (source.IndexOf("var ", StringComparison.Ordinal) < 0 && source.IndexOf('%') < 0))
            {
                return source;
            }
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
            var compilation = CSharpCompilation.Create("CscsWiden", new[] { tree }, GetReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var replacements = new Dictionary<SyntaxNode, Func<SyntaxNode, SyntaxNode>>();
            foreach (var declaration in root.DescendantNodes().OfType<VariableDeclarationSyntax>())
            {
                if (declaration.Type.IsVar &&
                    model.GetTypeInfo(declaration.Type).Type?.SpecialType == SpecialType.System_Int32)
                {
                    replacements[declaration.Type] = rewritten =>
                        SyntaxFactory.ParseTypeName("double").WithTriviaFrom(rewritten);
                }
            }
            foreach (var modulo in root.DescendantNodes().OfType<BinaryExpressionSyntax>()
                         .Where(b => b.IsKind(SyntaxKind.ModuloExpression)))
            {
                if (model.GetTypeInfo(modulo.Left).Type?.SpecialType == SpecialType.System_Int32 &&
                    model.GetTypeInfo(modulo.Right).Type?.SpecialType == SpecialType.System_Int32)
                {
                    replacements[modulo] = rewritten =>
                    {
                        var m = (BinaryExpressionSyntax)rewritten;
                        return SyntaxFactory.ParseExpression("((double)(" + m.Left + ") % (" + m.Right + "))")
                            .WithTriviaFrom(rewritten);
                    };
                }
            }
            if (replacements.Count == 0)
            {
                return source;
            }
            return root.ReplaceNodes(replacements.Keys, (original, rewritten) => replacements[original](rewritten)).ToFullString();
        }

        public static string InterpreterCompoundsAndElements(string source)
        {
            if (string.IsNullOrEmpty(source) || (source.IndexOf('[') < 0 && source.IndexOf("=", StringComparison.Ordinal) < 0))
            {
                return source;
            }
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
            var compilation = CSharpCompilation.Create("CscsCompounds", new[] { tree }, GetReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var replacements = new Dictionary<SyntaxNode, Func<SyntaxNode, SyntaxNode>>();
            foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                string action = null;
                switch (assignment.Kind())
                {
                    case SyntaxKind.AddAssignmentExpression: action = "+="; break;
                    case SyntaxKind.SubtractAssignmentExpression: action = "-="; break;
                    case SyntaxKind.MultiplyAssignmentExpression: action = "*="; break;
                    case SyntaxKind.DivideAssignmentExpression: action = "/="; break;
                    case SyntaxKind.ModuloAssignmentExpression: action = "%="; break;
                }
                if (action == null || !IsVariable(model, assignment.Left) ||
                    !(assignment.Left is IdentifierNameSyntax || assignment.Left is ElementAccessExpressionSyntax))
                {
                    continue;
                }
                replacements[assignment] = rewritten =>
                {
                    var a = (AssignmentExpressionSyntax)rewritten;
                    // The target keeps its own spelling on the left; read, it is whatever the
                    // element pass made of it.
                    var left = assignment.Left.ToString();
                    return SyntaxFactory.ParseExpression(left + " = CscsOps.Compound(" + ReadOf(a.Left) +
                        ", \"" + action + "\", (object)(" + a.Right + "))").WithTriviaFrom(rewritten);
                };
            }
            foreach (var access in root.DescendantNodes().OfType<ElementAccessExpressionSyntax>())
            {
                // "s[i]" on a C# string: the interpreter's character, as text, and its error past
                // the end (Utils.ExtractArrayElement) -- C#'s indexer gives a char, which "+" adds
                // as a number: "abc"[2] + r was "99..." rather than "c...".
                if (IsString(model, access.Expression) && access.ArgumentList.Arguments.Count == 1)
                {
                    replacements[access] = rewritten =>
                    {
                        var e = (ElementAccessExpressionSyntax)rewritten;
                        return SyntaxFactory.ParseExpression("CscsLate.Element(Variable.ConvertToVariable(" + e.Expression +
                            "), (object)(" + e.ArgumentList.Arguments[0] + "))").WithTriviaFrom(rewritten);
                    };
                    continue;
                }
                if (!IsVariable(model, access.Expression) || access.ArgumentList.Arguments.Count != 1 ||
                    IsElementWrite(access))
                {
                    continue;
                }
                replacements[access] = rewritten =>
                {
                    var e = (ElementAccessExpressionSyntax)rewritten;
                    return SyntaxFactory.ParseExpression("CscsLate.Element(" + e.Expression + ", (object)(" +
                        e.ArgumentList.Arguments[0] + "))").WithTriviaFrom(rewritten);
                };
            }
            if (replacements.Count == 0)
            {
                return source;
            }
            return root.ReplaceNodes(replacements.Keys, (original, rewritten) => replacements[original](rewritten)).ToFullString();
        }

        // The read of a compound's target: an element through the interpreter's element read.
        static string ReadOf(ExpressionSyntax target)
        {
            if (target is ElementAccessExpressionSyntax e && e.ArgumentList.Arguments.Count == 1)
            {
                return "CscsLate.Element(" + e.Expression + ", (object)(" + e.ArgumentList.Arguments[0] + "))";
            }
            return target.ToString();
        }

        // An element written rather than read: the left of an assignment (a compound's target is
        // handled with it), a step's operand, a ref or out argument.
        static bool IsElementWrite(ElementAccessExpressionSyntax access)
        {
            switch (access.Parent)
            {
                case AssignmentExpressionSyntax a: return a.Left == access;
                case PostfixUnaryExpressionSyntax _: return true;
                case PrefixUnaryExpressionSyntax p:
                    return p.IsKind(SyntaxKind.PreIncrementExpression) || p.IsKind(SyntaxKind.PreDecrementExpression);
                case ArgumentSyntax arg: return !arg.RefKindKeyword.IsKind(SyntaxKind.None);
                default: return false;
            }
        }

        /// <summary>The errors RepairOperators can do something about.</summary>
        public static bool IsRepairable(string errors)
        {
            return errors != null && (errors.Contains("CS0019") || errors.Contains("CS0029") || errors.Contains("CS0023") ||
                                      errors.Contains("CS1503") || errors.Contains("CS0117") ||
                                      errors.Contains("CS1061") || errors.Contains("CS1059") || errors.Contains("CS1955") ||
                                      errors.Contains("CS0173") || errors.Contains("CS0428") || errors.Contains("CS0200"));
        }

        // A "+" chain with a string among its operands: the interpreter's result is text.
        static bool JoinsText(SemanticModel model, ExpressionSyntax expression)
        {
            while (expression is ParenthesizedExpressionSyntax group)
            {
                expression = group.Expression;
            }
            if (!(expression is BinaryExpressionSyntax add) || !add.IsKind(SyntaxKind.AddExpression))
            {
                return false;
            }
            return IsString(model, add.Left) || IsString(model, add.Right) ||
                   JoinsText(model, add.Left) || JoinsText(model, add.Right);
        }

        // A bool a call returns -- "s.Contains(\"a\")", "s.StartsWith(x)".
        static bool IsCalledBool(SemanticModel model, ExpressionSyntax expression)
        {
            return IsBool(model, expression) && Unparenthesized(expression) is InvocationExpressionSyntax;
        }

        static ExpressionSyntax Unparenthesized(ExpressionSyntax expression)
        {
            while (expression is ParenthesizedExpressionSyntax group)
            {
                expression = group.Expression;
            }
            return expression;
        }

        // The name a read by name is of: CscsLate.Value(__interpreter, "x"), CscsLate.Current(...),
        // __interpreter.GetVariableValue("x").
        static string NameReadByName(ExpressionSyntax expression)
        {
            if (Unparenthesized(expression ?? SyntaxFactory.IdentifierName("_")) is InvocationExpressionSyntax call)
            {
                var callee = call.Expression.ToString();
                var args = call.ArgumentList.Arguments;
                if ((callee == "CscsLate.Value" || callee == "CscsLate.Current") && args.Count == 2 &&
                    args[1].Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    return literal.Token.ValueText;
                }
                if (callee == "__interpreter.GetVariableValue" && args.Count == 1 &&
                    args[0].Expression is LiteralExpressionSyntax single && single.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    return single.Token.ValueText;
                }
            }
            return null;
        }

        static bool IsCondition(ExpressionSyntax expression)
        {
            switch (expression.Parent)
            {
                case IfStatementSyntax s: return s.Condition == expression;
                case WhileStatementSyntax s: return s.Condition == expression;
                case DoStatementSyntax s: return s.Condition == expression;
                case ForStatementSyntax s: return s.Condition == expression;
                // "?:" decides by the same truth as "if" since September 2026; before, text there
                // gave the interpreter's own odd answers.
                case ConditionalExpressionSyntax s: return s.Condition == expression;
                default: return false;
            }
        }

        static readonly HashSet<SyntaxKind> s_arithmetic = new HashSet<SyntaxKind>
        {
            SyntaxKind.AddExpression, SyntaxKind.SubtractExpression, SyntaxKind.MultiplyExpression,
            SyntaxKind.DivideExpression, SyntaxKind.ModuloExpression, SyntaxKind.BitwiseOrExpression,
            SyntaxKind.BitwiseAndExpression, SyntaxKind.ExclusiveOrExpression,
        };

        static bool IsString(SemanticModel model, ExpressionSyntax expression)
        {
            return model.GetTypeInfo(expression).Type?.SpecialType == SpecialType.System_String;
        }

        static bool IsVarType(SemanticModel model, ExpressionSyntax expression)
        {
            var type = model.GetTypeInfo(expression).Type;
            return type != null && type.Name == "VarType" && type.ContainingType?.Name == "Variable";
        }

        static bool IsBool(SemanticModel model, ExpressionSyntax expression)
        {
            return model.GetTypeInfo(expression).Type?.SpecialType == SpecialType.System_Boolean;
        }

        static bool IsVariable(SemanticModel model, ExpressionSyntax expression)
        {
            var type = model.GetTypeInfo(expression).Type;
            return type != null && type.Name == "Variable";
        }

        /// <summary>Compiles C# source and returns the loaded assembly. Throws on errors.</summary>
        public static Assembly Compile(string source, string assemblyNamePrefix, string outputDLL = "")
        {
            // The one place compiled code is loaded. Explain mode promises it never is.
            if (PrecompileExplainer.Enabled)
            {
                throw new InvalidOperationException("Loading compiled code is disabled in explain mode.");
            }
            GetReferences();
            var key = Hash(source + "\n@refs:" + s_referencesFingerprint);
            var assemblyName = assemblyNamePrefix + "_" + key;

            lock (s_lock)
            {
                if (CacheEnabled && s_memoryCache.TryGetValue(key, out var cached) &&
                    string.IsNullOrWhiteSpace(outputDLL))
                {
                    CacheHits++;
                    return cached;
                }
            }

            var cachePath = GetCachePath(key);
            if (CacheEnabled && cachePath != null && string.IsNullOrWhiteSpace(outputDLL) &&
                File.Exists(cachePath))
            {
                try
                {
                    var cachedAssembly = Assembly.Load(File.ReadAllBytes(cachePath));
                    lock (s_lock)
                    {
                        s_memoryCache[key] = cachedAssembly;
                        CacheHits++;
                    }
                    return cachedAssembly;
                }
                catch
                {
                    // A corrupt or unreadable cache entry must never be fatal: fall through
                    // and compile from source.
                    try { File.Delete(cachePath); } catch { }
                }
            }

            var bytes = Emit(source, assemblyName);
            lock (s_lock) { CacheMisses++; }

            if (!string.IsNullOrWhiteSpace(outputDLL))
            {
                File.WriteAllBytes(outputDLL, bytes);
            }
            else if (CacheEnabled && cachePath != null)
            {
                WriteCacheEntry(cachePath, bytes);
            }

            var assembly = Assembly.Load(bytes);
            lock (s_lock) { s_memoryCache[key] = assembly; }
            return assembly;
        }

        /// <summary>
        /// Compiles C# source in memory and returns the errors, empty when it compiles. Nothing is
        /// loaded or written anywhere -- which is what makes it safe for source nobody has vetted.
        /// </summary>
        public static List<string> CheckCompiles(string source, string assemblyNamePrefix)
        {
            TryEmit(source, assemblyNamePrefix + "_check", out var errors);
            return errors;
        }

        static byte[] Emit(string source, string assemblyName)
        {
            var bytes = TryEmit(source, assemblyName, out var errors);
            if (errors.Count > 0)
            {
                throw new ArgumentException("Compile error: " +
                    string.Join(" -- ", errors) + "\n--- Generated code ---\n" +
                    NumberLines(source));
            }
            return bytes;
        }

        static byte[] TryEmit(string source, string assemblyName, out List<string> errors)
        {
            // Diagnostic: VDDUMP=<substring> prints the generated C# for any function whose
            // source contains that text, which is how a fallback's real cause gets found -- the
            // compile error alone says where C# gave up, not what the translator emitted.
            // Console.Error, because the probe harness sends stdout to TextWriter.Null.
            var dump = Environment.GetEnvironmentVariable("VDDUMP");
            if (!string.IsNullOrEmpty(dump) && source.Contains(dump))
            {
                Console.Error.WriteLine("===== " + assemblyName + " =====");
                Console.Error.WriteLine(source);
            }
            var tree = CSharpSyntaxTree.ParseText(source,
                new CSharpParseOptions(LanguageVersion.Latest));

            var options = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true,
                assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default);

            var compilation = CSharpCompilation.Create(assemblyName,
                new[] { tree }, GetReferences(), options);

            using (var peStream = new MemoryStream())
            {
                var result = compilation.Emit(peStream);
                errors = result.Success ? new List<string>() : result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d =>
                    {
                        var span = d.Location.GetLineSpan();
                        return "(" + (span.StartLinePosition.Line + 1) + "," +
                               (span.StartLinePosition.Character + 1) + ") " +
                               d.Id + ": " + d.GetMessage();
                    })
                    .ToList();
                return result.Success ? peStream.ToArray() : null;
            }
        }

        static string GetCachePath(string key)
        {
            var dir = CacheDirectory;
            if (string.IsNullOrWhiteSpace(dir))
            {
                return null;
            }
            return Path.Combine(dir, key + ".dll");
        }

        static void WriteCacheEntry(string cachePath, byte[] bytes)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                // Write to a unique temp name and move into place, so that two processes
                // compiling the same script cannot leave a half-written .dll behind.
                var temp = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(cachePath))
                {
                    File.Delete(temp);
                }
                else
                {
                    File.Move(temp, cachePath);
                }
            }
            catch
            {
                // The cache is an optimisation; failing to populate it is not an error.
            }
        }

        /// <summary>Removes every cached assembly, in memory and on disk.</summary>
        public static void ClearCache()
        {
            lock (s_lock)
            {
                s_memoryCache.Clear();
                CacheHits = CacheMisses = 0;
            }
            var dir = CacheDirectory;
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                return;
            }
            foreach (var file in Directory.GetFiles(dir, "*.dll"))
            {
                try { File.Delete(file); } catch { }
            }
        }

        static string Hash(string text)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString(0, 32);
            }
        }

        static string NumberLines(string source)
        {
            var lines = source.Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                sb.Append((i + 1).ToString().PadLeft(4)).Append(": ").AppendLine(lines[i]);
            }
            return sb.ToString();
        }
    }
}
