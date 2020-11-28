using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SplitAndMerge
{
    class CSCS_SQL
    {
        internal static string ConnectionString { get; set; }

        public static void Init()
        {
            ParserFunction.RegisterFunction("SQLConnectionString", new SQLConnectionStringFunction());
            ParserFunction.RegisterFunction("SQLTableColumns", new SQLColumnsFunction());
            ParserFunction.RegisterFunction("SQLQuery", new SQLQueryFunction());
            ParserFunction.RegisterFunction("SQLNonQuery", new SQLNonQueryFunction());
            ParserFunction.RegisterFunction("SQLInsert", new SQLInsertFunction());
            ParserFunction.RegisterFunction("SQLCursorInit", new SQLCursorFunction(SQLCursorFunction.Mode.SETUP));
            ParserFunction.RegisterFunction("SQLCursor", new SQLCursorFunction(SQLCursorFunction.Mode.NEXT));
            ParserFunction.RegisterFunction("SQLCursorClose", new SQLCursorFunction(SQLCursorFunction.Mode.CLOSE));
            ParserFunction.RegisterFunction("SQLCursorTotal", new SQLCursorFunction(SQLCursorFunction.Mode.TOTAL));
        }
    }

    class SQLConnectionStringFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);

            var connString = Utils.GetSafeString(args, 0);
            CSCS_SQL.ConnectionString = connString;
            return Variable.EmptyInstance;
        }
    }

    class SQLCursorFunction : ParserFunction
    {
        internal enum Mode { SETUP, NEXT, TOTAL, CLOSE };
        Mode m_mode;

        internal SQLCursorFunction(Mode mode)
        {
            m_mode = mode;
        }

        class SQLQueryObj
        {
            public string Table { get; set; }
            public string Query { get; set; }
            public SqlConnection Connection { get; set; }
            public SqlCommand Command { get; set; }
            public SqlDataReader DataReader { get; set; }
            public int CurrentRow { get; set; }
            public Dictionary<string, SqlDbType> Columns { get; set; }
        }

        static List<SQLQueryObj> s_queries = new List<SQLQueryObj>();

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);

            if (m_mode == Mode.SETUP)
            {
                var query = Utils.GetSafeString(args, 0);
                Variable result = new Variable(ExecuteQuery(query));
                return result;
            }
            else if (m_mode == Mode.NEXT)
            {
                var id = Utils.GetSafeInt(args, 0);
                return GetNextRecord(id);
            }
            else if (m_mode == Mode.CLOSE)
            {
                var id = Utils.GetSafeInt(args, 0);
                Close(id);
                return Variable.EmptyInstance;
            }
            else if (m_mode == Mode.TOTAL)
            {
                var id = Utils.GetSafeInt(args, 0);
                Variable result = new Variable(GetTotalRecords(id));
                return result;
            }

            return Variable.EmptyInstance;
        }

        static int ExecuteQuery(string query)
        {
            SQLQueryObj newQuery = new SQLQueryObj();
            newQuery.Query = GetSQLQuery(query);
            newQuery.Connection = new SqlConnection(CSCS_SQL.ConnectionString);
            newQuery.Connection.Open();

            newQuery.Command = new SqlCommand(newQuery.Query, newQuery.Connection);
            newQuery.DataReader = newQuery.Command.ExecuteReader();

            newQuery.Table = GetTableName(query);
            newQuery.Columns = SQLQueryFunction.GetColumnData(newQuery.Table);

            s_queries.Add(newQuery);

            return s_queries.Count - 1;// (int)count;
        }

        static int GetTotalRecords(int id)
        {
            SQLQueryObj obj = GetSQLObject(id);

            int rows = 0;
            using (var sqlCon = new SqlConnection(CSCS_SQL.ConnectionString))
            {
                sqlCon.Open();
                var com = sqlCon.CreateCommand();
                com.CommandText = GetCountQuery(obj.Query);
                var totalRow = com.ExecuteScalar();
                rows = (int)totalRow;
                sqlCon.Close();
            }
            return rows;
        }

        static Variable GetNextRecord(int id)
        {
            SQLQueryObj obj = GetSQLObject(id);
            if (obj == null || !obj.DataReader.HasRows || !obj.DataReader.Read())
            {
                return Variable.EmptyInstance;
            }

            Variable rowVar = new Variable(Variable.VarType.ARRAY);
            for (int i = 0; i < obj.DataReader.FieldCount; i++)
            {
                var cell     = obj.DataReader.GetValue(i);
                var cellType = obj.DataReader.GetDataTypeName(i);
                var variable = SQLQueryFunction.ConvertToVariable(cell, cellType);
                rowVar.AddVariable(variable);
            }
            return rowVar;
        }

        static void Close(int id)
        {
            SQLQueryObj obj = GetSQLObject(id);
            obj.DataReader.Dispose();
            obj.Command.Dispose();
            obj.Connection.Dispose();
            s_queries[id] = null;
        }

        static SQLQueryObj GetSQLObject(int id, bool throwExc = true)
        {
            if (id < 0 || id >= s_queries.Count)
            {
                if (!throwExc)
                {
                    return null;
                }
                throw new ArgumentException("Invalid handle: " + id);
            }
            SQLQueryObj obj = s_queries[id];

            if (obj == null)
            {
                if (!throwExc)
                {
                    return null;
                }
                throw new ArgumentException("Object has already been recycled. Handle: " + id);
            }
            return obj;
        }

        public static string GetTableName(string query)
        {
            query = query.ToUpper();
            int index1 = query.LastIndexOf(" FROM ");
            if (index1 <= 0)
            {
                return query;
            }
            var rest = query.Substring(index1 + 6).Trim();
            int index2 = rest.IndexOfAny(" ;".ToCharArray());
            index2 = index2 < 0 ? rest.Length - 1 : index2;
            var tableName = rest.Substring(0, index2 + 1);
            return tableName;
        }

        public static string GetSQLQuery(string query)
        {
            query = query.ToUpper();
            if (!query.Contains(' '))
            {
                query = "SELECT * FROM " + query;
            }
            return query;
        }
        public static string GetCountQuery(string query)
        {
            query = query.ToUpper();
            int index1 = query.LastIndexOf(" FROM ");
            string rest = index1 <= 0 ? query : query.Substring(index1 + 6);
            return "SELECT COUNT(*) FROM " + rest;
        }
    }

    class SQLQueryFunction : ParserFunction
    {
        static Dictionary<string, Dictionary<string, SqlDbType>> s_columns = new Dictionary<string, Dictionary<string, SqlDbType>>();
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);

            var query = Utils.GetSafeString(args, 0);
            Variable results = GetData(query);
            return results;
        }

        public static Dictionary<string, SqlDbType> GetColumnData(string tableName)
        {
            Dictionary<string, SqlDbType> tableData = null;
            if (s_columns.TryGetValue(tableName, out tableData))
            {
                return tableData;
            }

            var query = "select * from " + tableName + " where 1 = 2";
            GetData(query, tableName);
            if (s_columns.TryGetValue(tableName, out tableData))
            {
                return tableData;
            }

            return null;
        }

        public static Variable GetData(string query, string tableName = "")
        {
            Variable results = new Variable(Variable.VarType.ARRAY);
            DataTable table = new DataTable("results");

            using (SqlConnection con = new SqlConnection(CSCS_SQL.ConnectionString))
            {
                using (SqlCommand cmd = new SqlCommand(query, con))
                {
                    SqlDataAdapter dap = new SqlDataAdapter(cmd);
                    con.Open();
                    dap.Fill(table);
                    con.Close();
                }
            }
            Dictionary<string, SqlDbType> tableData = new Dictionary<string, SqlDbType>();
            Variable headerRow = new Variable(Variable.VarType.ARRAY);
            for (int i = 0; i < table.Columns.Count; i++)
            {
                DataColumn col = table.Columns[i];
                headerRow.AddVariable(new Variable(col.ColumnName));
                if (!string.IsNullOrWhiteSpace(tableName))
                {
                    tableData[col.ColumnName] = StringToSqlDbType(col.DataType.Name);
                }
            }
            results.AddVariable(headerRow);

            if (!string.IsNullOrWhiteSpace(tableName))
            {
                s_columns[tableName] = tableData;
            }

            foreach (var rowObj in table.Rows)
            {
                DataRow row = rowObj as DataRow;
                Variable rowVar = new Variable(Variable.VarType.ARRAY);
                int i = 0;
                foreach (var item in row.ItemArray)
                {
                    DataColumn col = table.Columns[i++];
                    rowVar.AddVariable(ConvertToVariable(item, col.DataType.Name));
                }

                results.AddVariable(rowVar);
            }

            return results;
        }

        public static Variable ConvertToVariable(object item, string objType)
        {
            objType = objType.ToLower();
            switch (objType)
            {
                case "smallint":
                case "tinyint":
                case "int16":
                    return new Variable((Int16)item);
                case "int":
                case "int32":
                    return new Variable((int)item);
                case "bigint":
                case "int64":
                    return new Variable((long)item);
                case "bit":
                case "boolean":
                    return new Variable((bool)item);
                case "real":
                case "float":
                case "single":
                    return new Variable((float)item);
                case "double":
                    return new Variable((double)item);
                case "char":
                case "nchar":
                case "varchar":
                case "nvarchar":
                case "text":
                case "ntext":
                case "string":
                    return new Variable((string)item);
                case "date":
                case "datetime":
                    return new Variable((DateTime)item);
                case "decimal":
                case "money":
                    return new Variable(Decimal.ToDouble((Decimal)item));
                default:
                    throw new ArgumentException("Unknown type: " + objType);
            }
        }

        public static SqlDbType StringToSqlDbType(string strType)
        {
            switch(strType)
            {
                case "Int16":    return SqlDbType.SmallInt;
                case "Int32":    return SqlDbType.Int;
                case "Int64":    return SqlDbType.BigInt;
                case "String":   return SqlDbType.NVarChar;
                case "Single":   return SqlDbType.Real;
                case "Double":   return SqlDbType.Float;
                case "Boolean":  return SqlDbType.Bit;
                case "DateTime": return SqlDbType.DateTime;
                case "Binary":   return SqlDbType.Binary;
                case "Decimal":  return SqlDbType.Decimal;
                default:
                    throw new ArgumentException("Unknown type: " + strType);
            }
        }

        public static object SqlDbTypeToType(SqlDbType dbType, Variable var)
        {
            switch (dbType)
            {
                case SqlDbType.SmallInt:
                case SqlDbType.Int:     
                case SqlDbType.BigInt:   return var.AsInt();
                case SqlDbType.NVarChar: return var.AsString();
                case SqlDbType.Real:
                case SqlDbType.Decimal:
                case SqlDbType.Float:    return var.AsDouble();
                case SqlDbType.Bit:      return var.AsBool();
                case SqlDbType.SmallDateTime:
                case SqlDbType.DateTime: return var.AsDateTime();
            }
            return var.AsString();
        }
    }

    class SQLInsertFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 3, m_name);

            var tableName = Utils.GetSafeString(args, 0).Trim();
            var colsStr   = Utils.GetSafeString(args, 1).Trim();

            var colData = SQLQueryFunction.GetColumnData(tableName);
            if (colData == null || colData.Count == 0)
            {
                throw new ArgumentException("Error: table [" + tableName + "] doesn't exist.");
            }

            var queryStatement = "INSERT INTO " + tableName + " (" + colsStr + ") VALUES ("; //@a,@b,@c);"
            var cols = colsStr.Split(',');
            for (int i = 0; i < cols.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(cols[i]) || !colData.Keys.Contains(cols[i]))
                {
                    throw new ArgumentException("Error: column [" + cols[i] + "] doesn't exist.");
                }
                queryStatement += "@" + cols[i] + ",";
            }
            queryStatement = queryStatement.Remove(queryStatement.Length - 1) + ")";

            var valsVariable = args[2];
            bool oneEntry = valsVariable.Type == Variable.VarType.ARRAY && valsVariable.Tuple.Count >= 1 &&
                            valsVariable.Tuple[0].Type != Variable.VarType.ARRAY;

            using (SqlConnection con = new SqlConnection(CSCS_SQL.ConnectionString))
            {
                con.Open();
                if (oneEntry)
                {
                    using (SqlCommand cmd = new SqlCommand(queryStatement, con))
                    {
                        InsertRow(cmd, colData, valsVariable, cols);
                    }
                }
                else
                {
                    for (int i = 0; i < valsVariable.Tuple.Count; i++)
                    {
                        using (SqlCommand cmd = new SqlCommand(queryStatement, con))
                        {
                            InsertRow(cmd, colData, valsVariable.Tuple[i], cols);
                        }
                    }
                }
            }
            return new Variable(oneEntry ? 1 : valsVariable.Tuple.Count);
        }

        static void InsertRow(SqlCommand cmd, Dictionary<string, SqlDbType> colData, Variable values, string[] cols)
        {
            if (values.Type != Variable.VarType.ARRAY || values.Tuple.Count < cols.Length)
            {
                throw new ArgumentException("Error: not enough values (" + values.Tuple.Count +
                                            ") given for " + cols.Length + " columns.");
            }
            for (int i = 0; i < cols.Length; i++)
            {
                var varName = "@" + cols[i];
                var varType = colData[cols[i]];
                cmd.Parameters.Add(varName, varType);
                cmd.Parameters[varName].Value = SQLQueryFunction.SqlDbTypeToType(varType, values.Tuple[i]);
            }

            cmd.ExecuteNonQuery();
        }
    }

    class SQLNonQueryFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);

            var queryStatement = Utils.GetSafeString(args, 0).Trim();
            using (SqlConnection con = new SqlConnection(CSCS_SQL.ConnectionString))
            {
                using (SqlCommand cmd = new SqlCommand(queryStatement, con))
                {
                    con.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            return new Variable(true);
        }
    }

    class SQLColumnsFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);

            var tableName = Utils.GetSafeString(args, 0);
            return GetColsData(tableName);
        }

        public static Variable GetColsData(string tableName)
        {
            var colData = SQLQueryFunction.GetColumnData(tableName);

            if (colData == null || colData.Count == 0)
            {
                return new Variable("");
            }

            Variable results = new Variable(Variable.VarType.ARRAY);
            foreach (KeyValuePair<string, SqlDbType> entry in colData)
            {
                results.AddVariable(new Variable(entry.Key));
                results.AddVariable(new Variable(entry.Value.ToString()));
            }
            return results;
        }
    }
}