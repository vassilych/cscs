using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.CodeDom.Compiler;
using Microsoft.CSharp;

namespace SplitAndMerge
{
    public class Statics
    {
        public static string StringVar = "";
        public static double DoubleVar = 0.0;
        public static bool   BoolVar   = false;
        public static int    IntVar    = 0;

        static Dictionary<string, Func<string, string>> m_compiledCode =
           new Dictionary<string, Func<string, string>>();

        public static Variable InvokeCall(Type type, string methodName, string paramName,
                                          string paramValue, object master = null)
        {
            string key = type + "_" + methodName + "_" + paramName;
            Func<string, string> func = null;

            // Cache compiled function:
            if (!m_compiledCode.TryGetValue(key, out func))
            {
                MethodInfo methodInfo = type.GetMethod(methodName, new Type[] { typeof(string) });
                ParameterExpression param = Expression.Parameter(typeof(string), paramName);

                MethodCallExpression methodCall = master == null ? Expression.Call(methodInfo, param) :
                                                             Expression.Call(Expression.Constant(master), methodInfo, param);
                Expression<Func<string, string>> lambda =
                    Expression.Lambda<Func<string, string>>(methodCall, new ParameterExpression[] { param });
                func = lambda.Compile();
                m_compiledCode[key] = func;
            }

            string result = func(paramValue);
            return new Variable(result);
        }

        public static Object GetVariableValue(string name, ParsingScript script)
        {
            var field = typeof(Statics).GetField(name);
            Utils.CheckNotNull(field, name, script);
            Object result = field.GetValue(null);
            return result;
        }

        public static bool SetVariableValue(string name, Object value, ParsingScript script)
        {
            Type type   = typeof(Statics);
            var props   = type.GetProperties();
            var members = type.GetMembers();
            var methods = type.GetMethods();
            var fields  = type.GetFields();
            var field   = type.GetField(name);
            Utils.CheckNotNull(field, name, script);
            field.SetValue(null, Convert.ChangeType(value, field.FieldType));
            return true;
        }

        public static string ProcessClick(string arg)
        {
            var now = DateTime.Now.ToString("T");
            return "Clicks: " + arg + "\n" + now;
        }
    }

    public class InvokeNativeFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string methodName = Utils.GetItem(script).AsString();
            Utils.CheckNotEmpty(script, methodName, m_name);
            script.MoveForwardIf(Constants.NEXT_ARG);

            string paramName = Utils.GetToken(script, Constants.NEXT_ARG_ARRAY);
            Utils.CheckNotEmpty(script, paramName, m_name);
            script.MoveForwardIf(Constants.NEXT_ARG);

            Variable paramValueVar = Utils.GetItem(script);
            string paramValue = paramValueVar.AsString();

            var result = Statics.InvokeCall(typeof(Statics),
                                            methodName, paramName, paramValue);
            return result;
        }
    }

    public class GetNativeFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);

            string name = Utils.GetSafeString(args, 0);
            var objValue = Statics.GetVariableValue(name, script);

            return new Variable(objValue.ToString());
        }
    }

    public class SetNativeFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 2, m_name);

            string name  = Utils.GetSafeString(args, 0);
            string value = Utils.GetSafeString(args, 1);
            bool isSet   = Statics.SetVariableValue(name, value, script);

            return new Variable(isSet);
        }
    }
}
