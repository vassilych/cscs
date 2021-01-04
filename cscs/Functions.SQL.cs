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

        internal static void CheckConnectionString(ParsingScript script = null, string funcName = "")
        {
            if (string.IsNullOrWhiteSpace(ConnectionString))
            {
                Utils.ThrowErrorMsg("SQL Connection string has not been initialized.",
                         script, funcName);
            }

        }

        public static void Init()
        {
            ParserFunction.RegisterFunction("SQLConnectionString", new SQLConnectionStringFunction());
            ParserFunction.RegisterFunction("SQLTableColumns", new SQLColumnsFunction());
            ParserFunction.RegisterFunction("SQLQuery", new SQLQueryFunction());
            ParserFunction.RegisterFunction("SQLNonQuery", new SQLNonQueryFunction());
            ParserFunction.RegisterFunction("SQLInsert", new SQLInsertFunction());

            ParserFunction.RegisterFunction("SQLCreateDB", new SQLDBOperationsFunction(SQLDBOperationsFunction.Mode.CREATE_DB));
            ParserFunction.RegisterFunction("SQLDropDB", new SQLDBOperationsFunction(SQLDBOperationsFunction.Mode.DROP_DB));
            ParserFunction.RegisterFunction("SQLDropTable", new SQLDBOperationsFunction(SQLDBOperationsFunction.Mode.DROP_TABLE));
            ParserFunction.RegisterFunction("SQLProcedure", new SQLSPFunction());

            ParserFunction.RegisterFunction("SQLCursorInit", new SQLCursorFunction(SQLCursorFunction.Mode.SETUP));
            ParserFunction.RegisterFunction("SQLCursorNext", new SQLCursorFunction(SQLCursorFunction.Mode.NEXT));
            ParserFunction.RegisterFunction("SQLCursorCurrentRow", new SQLCursorFunction(SQLCursorFunction.Mode.CURRENT_ROW));
            ParserFunction.RegisterFunction("SQLCursorTotal", new SQLCursorFunction(SQLCursorFunction.Mode.TOTAL));
            ParserFunction.RegisterFunction("SQLCursorClose", new SQLCursorFunction(SQLCursorFunction.Mode.CLOSE));
        }
    }

    class SQLConnectionStringFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);

            CSCS_SQL.ConnectionString = Utils.GetSafeString(args, 0);
            return Variable.EmptyInstance;
        }
    }

    class SQLCursorFunction : ParserFunction
    {
        internal enum Mode { SETUP, NEXT, CURRENT_ROW, TOTAL, CLOSE };
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
            public int TotalRows { get; set; } = -1;
            public Dictionary<string, SqlDbType> Columns { get; set; }
        }

        static List<SQLQueryObj> s_queries = new List<SQLQueryObj>();

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);
            CSCS_SQL.CheckConnectionString(script, m_name);

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
            else if (m_mode == Mode.CURRENT_ROW)
            {
                var id = Utils.GetSafeInt(args, 0);
                SQLQueryObj obj = GetSQLObject(id);
                Variable result = new Variable(obj.CurrentRow);
                return result;
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
            if (obj.TotalRows >= 0)
            {
                return obj.TotalRows;
            }

            using (var sqlCon = new SqlConnection(CSCS_SQL.ConnectionString))
            {
                sqlCon.Open();
                var com = sqlCon.CreateCommand();
                com.CommandText = GetCountQuery(obj.Query);
                var totalRow = com.ExecuteScalar();
                obj.TotalRows = (int)totalRow;
                sqlCon.Close();
            }
            return obj.TotalRows;
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
                var cell = obj.DataReader.GetValue(i);
                var cellType = obj.DataReader.GetDataTypeName(i);
                var variable = SQLQueryFunction.ConvertToVariable(cell, cellType);
                rowVar.AddVariable(variable);
            }
            obj.CurrentRow++;
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
            int index2 = rest.LastIndexOf(" ORDER ");
            rest = index2 < 0 ? rest : rest.Substring(0, index2);
            return "SELECT COUNT(*) FROM " + rest;
        }
    }

    class SQLQueryFunction : ParserFunction
    {
        static Dictionary<string, object> s_cache =
            new Dictionary<string, object>();

        static Dictionary<string, Dictionary<string, SqlDbType>> s_columns =
            new Dictionary<string, Dictionary<string, SqlDbType>>();
        static List<KeyValuePair<string, SqlDbType>> s_colList =
            new List<KeyValuePair<string, SqlDbType>>();

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);
            CSCS_SQL.CheckConnectionString(script, m_name);

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

        public static List<KeyValuePair<string, SqlDbType>> GetColumnUserData(string tableName)
        {
            List<KeyValuePair<string, SqlDbType>> result = null;
            if (s_cache.TryGetValue(tableName, out object tableData) &&
                tableData is List<KeyValuePair<string, SqlDbType>>)
            {
                return tableData as List<KeyValuePair<string, SqlDbType>>;
            }
            result = new List<KeyValuePair<string, SqlDbType>>();

            var query = @"select t.name      [TableTypeName]
                                ,c.name      [ColumnName]
                                ,y.name      [DataType]
                                ,c.max_length[MaxLength]
                          from sys.table_types t
                    inner join sys.columns c on c.object_id = t.type_table_object_id
                    inner join sys.types y ON y.system_type_id = c.system_type_id
                          WHERE t.is_user_defined = 1 AND t.is_table_type = 1
                            AND t.name = '" + tableName + "' order by c.column_id";
            var data = GetData(query, tableName);
            for (int i = 1; data.Tuple != null && i < data.Tuple.Count; i++)
            {
                var row = data.Tuple[i];
                if (row.Type == Variable.VarType.ARRAY && row.Tuple.Count > 2)
                {
                    var colName = row.Tuple[1].AsString();
                    var colType = SQLQueryFunction.StringToSqlDbType(row.Tuple[2].AsString());
                    result.Add(new KeyValuePair<string, SqlDbType>(colName, colType));
                }
            }

            s_cache[tableName] = result;
            return result;
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
                case "time":
                case "timestamp":
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
            switch (strType.ToLower())
            {
                case "int16":
                case "smallint": return SqlDbType.SmallInt;
                case "int32":
                case "int": return SqlDbType.Int;
                case "int64":
                case "bigint": return SqlDbType.BigInt;
                case "string":
                case "char":
                case "varchar":
                case "nvarchar": return SqlDbType.NVarChar;
                case "text":
                case "ntext": return SqlDbType.NText;
                case "single":
                case "real": return SqlDbType.Real;
                case "double":
                case "float": return SqlDbType.Float;
                case "boolean":
                case "binary": return SqlDbType.Binary;
                case "bit": return SqlDbType.Bit;
                case "datetime": return SqlDbType.DateTime;
                case "time": return SqlDbType.Time;
                case "timestamp": return SqlDbType.Timestamp;
                case "decimal": return SqlDbType.Decimal;
                case "money": return SqlDbType.Money;
                case "smallmoney": return SqlDbType.SmallMoney;
                default:
                    throw new ArgumentException("Unknown type: " + strType);
            }
        }

        public static object SqlDbTypeToVariable(SqlDbType dbType, Variable var)
        {
            switch (dbType)
            {
                case SqlDbType.SmallInt:
                case SqlDbType.Int:
                case SqlDbType.BigInt: return var.AsInt();
                case SqlDbType.NChar:
                case SqlDbType.NText:
                case SqlDbType.NVarChar: return var.AsString();
                case SqlDbType.Real:
                case SqlDbType.Decimal:
                case SqlDbType.SmallMoney:
                case SqlDbType.Money:
                case SqlDbType.Float: return var.AsDouble();
                case SqlDbType.Binary:
                case SqlDbType.Bit: return var.AsBool();
                case SqlDbType.SmallDateTime:
                case SqlDbType.Date:
                case SqlDbType.Time:
                case SqlDbType.DateTime: return var.AsDateTime();
            }
            return var.AsString();
        }

        public static Type SqlDbTypeToType(SqlDbType dbType)
        {
            switch (dbType)
            {
                case SqlDbType.SmallInt:
                case SqlDbType.Int: return typeof(int);
                case SqlDbType.BigInt: return typeof(long);
                case SqlDbType.NChar:
                case SqlDbType.NText:
                case SqlDbType.NVarChar: return typeof(string);
                case SqlDbType.Real:
                case SqlDbType.Decimal:
                case SqlDbType.SmallMoney:
                case SqlDbType.Money:
                case SqlDbType.Float: return typeof(double);
                case SqlDbType.Binary:
                case SqlDbType.Bit: return typeof(bool);
                case SqlDbType.SmallDateTime:
                case SqlDbType.Date:
                case SqlDbType.Time:
                case SqlDbType.DateTime: return typeof(DateTime);
            }
            return typeof(string);
        }
    }

    class SQLInsertFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 3, m_name);
            CSCS_SQL.CheckConnectionString(script, m_name);

            var tableName = Utils.GetSafeString(args, 0).Trim();
            var colsStr = Utils.GetSafeString(args, 1).Trim();

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
                cmd.Parameters[varName].Value = SQLQueryFunction.SqlDbTypeToVariable(varType, values.Tuple[i]);
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
            CSCS_SQL.CheckConnectionString(script, m_name);

            var queryStatement = Utils.GetSafeString(args, 0).Trim();
            int result = 0;
            using (SqlConnection con = new SqlConnection(CSCS_SQL.ConnectionString))
            {
                using (SqlCommand cmd = new SqlCommand(queryStatement, con))
                {
                    con.Open();
                    result = cmd.ExecuteNonQuery();
                }
            }
            return new Variable(result);
        }
    }

    class SQLSPFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);
            CSCS_SQL.CheckConnectionString(script, m_name);

            var spName = Utils.GetSafeString(args, 0);
            var colTypes = GetSPData(spName);
            int result = 0;

            SqlCommand sqlcom = new SqlCommand(spName);
            sqlcom.CommandType = CommandType.StoredProcedure;
            for (int i = 0; i < colTypes.Count && i+1 < args.Count; i++)
            {
                var arg = args[i + 1];
                var currName = colTypes[i].Key;
                var currType = colTypes[i].Value;
                if (arg.Type == Variable.VarType.ARRAY && currType is List<KeyValuePair<string, SqlDbType>>)
                {
                    var typeData = currType as List<KeyValuePair<string, SqlDbType>>;
                    DataTable dt = new DataTable();
                    foreach (var entry in typeData)
                    {
                        var type = SQLQueryFunction.SqlDbTypeToType((SqlDbType)entry.Value);
                        dt.Columns.Add(new DataColumn(entry.Key, type));
                    }
                    for (int j = 0; j < arg.Tuple.Count; j++)
                    {
                        var row = arg.Tuple[j];
                        var objs = row.AsObject() as List<object>;
                        var dataRow = dt.NewRow();
                        if (objs != null)
                        {
                            for (int k = 0; k < objs.Count; k++)
                            {
                                dataRow[typeData[k].Key] = objs[k];
                            }
                        }
                        dt.Rows.Add(dataRow);
                    }
                    sqlcom.Parameters.AddWithValue("@" + currName, dt);
                }
                else
                {
                    sqlcom.Parameters.AddWithValue("@" + currName, arg.AsObject());
                }
            }

            using (SqlConnection con = new SqlConnection(CSCS_SQL.ConnectionString))
            {
                sqlcom.Connection = con;
                con.Open();
                result = sqlcom.ExecuteNonQuery();
            }
            return new Variable(result);
        }

        static List<KeyValuePair<string, object>> GetSPData(string spName)
        {
            var colTypes = new List<KeyValuePair<string, object>>();
            var existing = new HashSet<string>();
            var query = @"SELECT definition FROM sys.sql_modules WHERE object_id = (OBJECT_ID(N'" + spName + "'))";
            var data = SQLQueryFunction.GetData(query);
            var str = data.AsString().ToLower();
            int start = str.IndexOf('@');
            while (start > 0)
            {
                int end1 = str.IndexOf(' ', start + 1);
                int end2 = str.IndexOfAny(" \n\t".ToCharArray(), end1 + 1);
                if (end1 < 0 || end2 < 0)
                {
                    break;
                }
                var paramName = str.Substring(start + 1, end1 - start).Trim();
                if (existing.Contains(paramName))
                {
                    break;
                }
                existing.Add(paramName);
                var paramType = str.Substring(end1 + 1, end2 - end1).Trim();
                try
                {
                    var sqlType = SQLQueryFunction.StringToSqlDbType(paramType);
                    colTypes.Add(new KeyValuePair<string, object>(paramName, sqlType));
                }
                catch (Exception)
                {
                    var colData = SQLQueryFunction.GetColumnUserData(paramType);
                    colTypes.Add(new KeyValuePair<string, object>(paramName, colData));
                }
                start = str.IndexOf('@', end2 + 1);
            }
            return colTypes;
        }
    }

    class SQLDBOperationsFunction : ParserFunction
    {
        internal enum Mode { DROP_DB, CREATE_DB, DROP_TABLE };
        Mode m_mode;

        internal SQLDBOperationsFunction(Mode mode)
        {
            m_mode = mode;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);
            CSCS_SQL.CheckConnectionString(script, m_name);

            var arg = Utils.GetSafeString(args, 0).Trim();
            string statement = "";
            switch (m_mode)
            {
                case Mode.DROP_DB:
                    statement = "DROP DATABASE " + arg;
                    break;
                case Mode.CREATE_DB:
                    statement = "CREATE DATABASE " + arg;
                    break;
                case Mode.DROP_TABLE:
                    statement = "DROP TABLE " + arg;
                    break;
            }
            int result = 0;
            using (SqlConnection con = new SqlConnection(CSCS_SQL.ConnectionString))
            {
                using (SqlCommand cmd = new SqlCommand(statement, con))
                {
                    con.Open();
                    result = cmd.ExecuteNonQuery();
                }
            }
            return new Variable(result);
        }
    }

    class SQLColumnsFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);
            CSCS_SQL.CheckConnectionString(script, m_name);

            var tableName = Utils.GetSafeString(args, 0);
            bool namesOnly = Utils.GetSafeInt(args, 1, 0) == 1;

            return GetColsData(tableName, namesOnly);
        }

        public static Variable GetColsData(string tableName, bool namesOnly = false)
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
                if (!namesOnly)
                {
                    results.AddVariable(new Variable(entry.Value.ToString()));
                }
            }
            return results;
        }
    }
}