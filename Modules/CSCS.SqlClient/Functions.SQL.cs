using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SplitAndMerge
{
    public class CscsSqlModule : ICscsModule
    {
        public ICscsModuleInstance CreateInstance(Interpreter interpreter)
        {
            return new CscsSqlModuleInstance(interpreter);
        }

        public void Terminate()
        {
        }
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

    public class CscsSqlModuleInstance : ICscsModuleInstance
    {
        internal string ConnectionString { get; set; }
        internal SQLQueryFunction SQLQueryFunction { get; }
        internal SQLSPFunction SQLSPFunction { get; }

        internal void CheckConnectionString(ParsingScript script = null, string funcName = "")
        {
            if (string.IsNullOrWhiteSpace(ConnectionString))
            {
                Utils.ThrowErrorMsg("SQL Connection string has not been initialized.",
                         script, funcName);
            }
        }

        public CscsSqlModuleInstance(Interpreter interpreter)
        {
            interpreter.RegisterFunction("SQLConnectionString", new SQLConnectionStringFunction(this));
            interpreter.RegisterFunction("SQLTableColumns", new SQLColumnsFunction(this));
            SQLQueryFunction = new SQLQueryFunction(this);
            interpreter.RegisterFunction("SQLQuery", SQLQueryFunction);
            interpreter.RegisterFunction("SQLNonQuery", new SQLNonQueryFunction(this));
            interpreter.RegisterFunction("SQLInsert", new SQLInsertFunction(this));

            interpreter.RegisterFunction("SQLCreateDB", new SQLDBOperationsFunction(this, SQLDBOperationsFunction.Mode.CREATE_DB));
            interpreter.RegisterFunction("SQLDropDB", new SQLDBOperationsFunction(this, SQLDBOperationsFunction.Mode.DROP_DB));
            interpreter.RegisterFunction("SQLDropTable", new SQLDBOperationsFunction(this, SQLDBOperationsFunction.Mode.DROP_TABLE));
            SQLSPFunction = new SQLSPFunction(this);
            interpreter.RegisterFunction("SQLProcedure", SQLSPFunction);
            interpreter.RegisterFunction("SQLDescribe", new SQLDescribe(this, SQLDescribe.Mode.SP_DESC));
            interpreter.RegisterFunction("SQLAllTables", new SQLDescribe(this, SQLDescribe.Mode.TABLES));
            interpreter.RegisterFunction("SQLAllProcedures", new SQLDescribe(this, SQLDescribe.Mode.PROCEDURES));

            interpreter.RegisterFunction("SQLCursorInit", new SQLCursorFunction(this, SQLCursorFunction.Mode.SETUP));
            interpreter.RegisterFunction("SQLCursorNext", new SQLCursorFunction(this, SQLCursorFunction.Mode.NEXT));
            interpreter.RegisterFunction("SQLCursorCurrentRow", new SQLCursorFunction(this, SQLCursorFunction.Mode.CURRENT_ROW));
            interpreter.RegisterFunction("SQLCursorTotal", new SQLCursorFunction(this, SQLCursorFunction.Mode.TOTAL));
            interpreter.RegisterFunction("SQLCursorClose", new SQLCursorFunction(this, SQLCursorFunction.Mode.CLOSE));
        }

        internal List<SQLQueryObj> s_queries = new List<SQLQueryObj>();
    }

    class SQLConnectionStringFunction : ParserFunction
    {
        private CscsSqlModuleInstance _instance;

        public SQLConnectionStringFunction(CscsSqlModuleInstance instance)
        {
            _instance = instance;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);

            _instance.ConnectionString = Utils.GetSafeString(args, 0);
            return Variable.EmptyInstance;
        }
    }

    class SQLCursorFunction : ParserFunction
    {
        private CscsSqlModuleInstance _instance;

        internal enum Mode { SETUP, NEXT, CURRENT_ROW, TOTAL, CLOSE };
        Mode m_mode;

        internal SQLCursorFunction(CscsSqlModuleInstance instance, Mode mode)
        {
            _instance = instance;
            m_mode = mode;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);
            _instance.CheckConnectionString(script, m_name);

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

        int ExecuteQuery(string query)
        {
            SQLQueryObj newQuery = new SQLQueryObj();
            newQuery.Query = GetSQLQuery(query);
            newQuery.Connection = new SqlConnection(_instance.ConnectionString);
            newQuery.Connection.Open();

            newQuery.Command = new SqlCommand(newQuery.Query, newQuery.Connection);
            newQuery.DataReader = newQuery.Command.ExecuteReader();

            newQuery.Table = GetTableName(query);
            newQuery.Columns = _instance.SQLQueryFunction.GetColumnData(newQuery.Table);

            _instance.s_queries.Add(newQuery);

            return _instance.s_queries.Count - 1;// (int)count;
        }

        int GetTotalRecords(int id)
        {
            SQLQueryObj obj = GetSQLObject(id);
            if (obj.TotalRows >= 0)
            {
                return obj.TotalRows;
            }

            using (var sqlCon = new SqlConnection(_instance.ConnectionString))
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

        Variable GetNextRecord(int id)
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
                var variable = _instance.SQLQueryFunction.ConvertToVariable(cell, cellType);
                rowVar.AddVariable(variable);
            }
            obj.CurrentRow++;
            return rowVar;
        }

        void Close(int id)
        {
            SQLQueryObj obj = GetSQLObject(id);
            obj.DataReader.Dispose();
            obj.Command.Dispose();
            obj.Connection.Dispose();
            _instance.s_queries[id] = null;
        }

        SQLQueryObj GetSQLObject(int id, bool throwExc = true)
        {
            if (id < 0 || id >= _instance.s_queries.Count)
            {
                if (!throwExc)
                {
                    return null;
                }
                throw new ArgumentException("Invalid handle: " + id);
            }
            SQLQueryObj obj = _instance.s_queries[id];

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

        public string GetTableName(string query)
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

        public string GetSQLQuery(string query)
        {
            query = query.ToUpper();
            if (!query.Contains(' '))
            {
                query = "SELECT * FROM " + query;
            }
            return query;
        }
        public string GetCountQuery(string query)
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
        CscsSqlModuleInstance _instance;

        Dictionary<string, object> s_cache = new Dictionary<string, object>();

        Dictionary<string, Dictionary<string, SqlDbType>> s_columns = new Dictionary<string, Dictionary<string, SqlDbType>>();
        List<KeyValuePair<string, SqlDbType>> s_colList = new List<KeyValuePair<string, SqlDbType>>();
        
        public SQLQueryFunction(CscsSqlModuleInstance instance)
        {
            _instance = instance;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);
            _instance.CheckConnectionString(script, m_name);

            var query = Utils.GetSafeString(args, 0);
            var spArgs = Utils.GetSafeVariable(args, 1);
            var sp = GetParameters(spArgs);
            Variable results = GetData(query, "", sp);
            return results;
        }

        public List<SqlParameter> GetParameters(Variable spArgs)
        {
            if (spArgs == null || spArgs.Type != Variable.VarType.ARRAY || spArgs.Tuple.Count == 0)
            {
                return null;
            }

            List<SqlParameter> sp = new List<SqlParameter>();
            for (int i = 0; i < spArgs.Count; i++)
            {
                var paramData = spArgs.Tuple[i];
                if (paramData.Type != Variable.VarType.ARRAY || paramData.Tuple.Count < 2)
                {
                    continue;
                }
                var parameter = new SqlParameter(paramData.Tuple[0].AsString(),
                                                 paramData.Tuple[1].AsObject());
                sp.Add(parameter);
            }

            return sp.Count == 0 ? null : sp;
        }

        public Dictionary<string, SqlDbType> GetColumnData(string tableName)
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

        public List<KeyValuePair<string, SqlDbType>> GetColumnUserData(string tableName)
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
                    var colType = _instance.SQLQueryFunction.StringToSqlDbType(row.Tuple[2].AsString());
                    result.Add(new KeyValuePair<string, SqlDbType>(colName, colType));
                }
            }

            s_cache[tableName] = result;
            return result;
        }

        public Variable GetData(string query, string tableName = null,
            List<SqlParameter> sp = null, bool addHeader = true)
        {
            Variable results = new Variable(Variable.VarType.ARRAY);
            DataTable table = new DataTable("results");

            using (SqlConnection con = new SqlConnection(_instance.ConnectionString))
            {
                using (SqlCommand cmd = new SqlCommand(query, con))
                {
                    if (sp != null)
                    {
                        cmd.Parameters.AddRange(sp.ToArray());
                    }
                    SqlDataAdapter dap = new SqlDataAdapter(cmd);
                    con.Open();
                    dap.Fill(table);
                    con.Close();
                }
            }

            if (addHeader)
            {
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
                if (!string.IsNullOrWhiteSpace(tableName))
                {
                    s_columns[tableName] = tableData;
                }
                results.AddVariable(headerRow);
            }

            return FillWithResults(table, results);
        }

        public Variable FillWithResults(DataTable table, Variable results = null)
        {
            if (table.Rows == null || table.Rows.Count == 0)
            {
                return results;
            }
            if (results == null)
            {
                results = new Variable(Variable.VarType.ARRAY);
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

        public Variable ConvertToVariable(object item, string objType)
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

        public SqlDbType StringToSqlDbType(string strType)
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

        public object SqlDbTypeToVariable(SqlDbType dbType, Variable var)
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

        public Type SqlDbTypeToType(SqlDbType dbType)
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
        private CscsSqlModuleInstance _instance;

        public SQLInsertFunction(CscsSqlModuleInstance instance)
        {
            _instance = instance;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 3, m_name);
            _instance.CheckConnectionString(script, m_name);

            var tableName = Utils.GetSafeString(args, 0).Trim();
            var colsStr = Utils.GetSafeString(args, 1).Trim();

            var colData = _instance.SQLQueryFunction.GetColumnData(tableName);
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

            using (SqlConnection con = new SqlConnection(_instance.ConnectionString))
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

        void InsertRow(SqlCommand cmd, Dictionary<string, SqlDbType> colData, Variable values, string[] cols)
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
                cmd.Parameters[varName].Value = _instance.SQLQueryFunction.SqlDbTypeToVariable(varType, values.Tuple[i]);
            }

            cmd.ExecuteNonQuery();
        }
    }

    class SQLNonQueryFunction : ParserFunction
    {
        private CscsSqlModuleInstance _instance;

        public SQLNonQueryFunction(CscsSqlModuleInstance instance)
        {
            _instance = instance;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);
            _instance.CheckConnectionString(script, m_name);

            var queryStatement = Utils.GetSafeString(args, 0);
            var spArgs = Utils.GetSafeVariable(args, 1);
            var sp = _instance.SQLQueryFunction.GetParameters(spArgs);

            int result = 0;
            using (SqlConnection con = new SqlConnection(_instance.ConnectionString))
            {
                using (SqlCommand cmd = new SqlCommand(queryStatement, con))
                {
                    if (sp != null)
                    {
                        cmd.Parameters.AddRange(sp.ToArray());
                    }
                    con.Open();
                    result = cmd.ExecuteNonQuery();
                }
            }
            return new Variable(result);
        }
    }


    class SQLDescribe : ParserFunction
    {
        private CscsSqlModuleInstance _instance;

        internal enum Mode { SP_DESC, TABLES, PROCEDURES};
        Mode m_mode;

        internal SQLDescribe(CscsSqlModuleInstance instance, Mode mode = Mode.SP_DESC)
        {
            _instance = instance;
            m_mode = mode;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            _instance.CheckConnectionString(script, m_name);

            switch (m_mode)
            {
                case Mode.SP_DESC:
                    Utils.CheckArgs(args.Count, 1, m_name);
                    var spName = "sp_helptext";
                    var argName = Utils.GetSafeString(args, 0);
                    List<KeyValuePair<string, object>> spParams = new List<KeyValuePair<string, object>>()
                    {
                        new KeyValuePair<string, object>("@objname", argName)
                    };
                    var results = _instance.SQLSPFunction.ExecuteSP(spName, spParams);
                    if (results.Type == Variable.VarType.ARRAY && results.Tuple.Count >= 1 &&
                        results.Tuple[0].Type == Variable.VarType.ARRAY && results.Tuple[0].Count >= 1)
                    {
                        var r = results.Tuple[0].Tuple[0].AsString();
                        var res = System.Text.RegularExpressions.Regex.Replace(r, @"\s{2,}", " ");
                        return new Variable(res);
                    }
                    return results;
                case Mode.TABLES:
                    return RemoveListEntries(_instance.SQLQueryFunction.GetData("SELECT name FROM sysobjects WHERE xtype = 'U'",
                        null, null, false));
                case Mode.PROCEDURES:
                    return RemoveListEntries(_instance.SQLQueryFunction.GetData("SELECT NAME from SYS.PROCEDURES",
                        null, null, false));
            }

            return Variable.EmptyInstance;
        }

        Variable RemoveListEntries(Variable v)
        {
            if (v.Type != Variable.VarType.ARRAY || v.Tuple.Count == 0)
            {
                return v;
            }
            for (int i = 0; i < v.Tuple.Count; i++)
            {
                if (v.Tuple[i].Type == Variable.VarType.ARRAY && v.Tuple[i].Count > 0)
                {
                    v.Tuple[i] = v.Tuple[i].Tuple[0];
                }
            }
            return v;
        }
    }

    class SQLSPFunction : ParserFunction
    {
        private CscsSqlModuleInstance _instance;

        public SQLSPFunction(CscsSqlModuleInstance instance)
        {
            _instance = instance;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);
            _instance.CheckConnectionString(script, m_name);
            var spName = Utils.GetSafeString(args, 0);

            return ExecuteSP(spName, null, args);
        }

        public Variable ExecuteSP(string spName, List<KeyValuePair<string,object>> spParams = null,
            List<Variable> args = null)
        {
            SqlCommand sqlcom = new SqlCommand(spName);
            sqlcom.CommandType = CommandType.StoredProcedure;
            int result = 0;

            if (spParams != null)
            {
                for (int i = 0; i < spParams.Count; i++)
                {
                    sqlcom.Parameters.AddWithValue(spParams[i].Key, spParams[i].Value);
                }
            }
            else
            {
                var colTypes = GetSPData(spName);
                for (int i = 0; i < colTypes.Count && i + 1 < args.Count; i++)
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
                            var type = _instance.SQLQueryFunction.SqlDbTypeToType((SqlDbType)entry.Value);
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
            }

            DataTable table = new DataTable("results");
            using (SqlConnection con = new SqlConnection(_instance.ConnectionString))
            {
                sqlcom.Connection = con;
                con.Open();
                result = sqlcom.ExecuteNonQuery();
                SqlDataAdapter dap = new SqlDataAdapter(sqlcom);
                dap.Fill(table);
                con.Close();
            }

            Variable results = _instance.SQLQueryFunction.FillWithResults(table);
            return results == null ? new Variable(result) : results;
        }

        List<KeyValuePair<string, object>> GetSPData(string spName)
        {
            var colTypes = new List<KeyValuePair<string, object>>();
            var existing = new HashSet<string>();

            SqlCommand sqlcom = new SqlCommand("sp_helptext");
            sqlcom.CommandType = CommandType.StoredProcedure;

            //var query = @"SELECT definition FROM sys.sql_modules WHERE object_id = (OBJECT_ID(N'" + spName + "'))";
            var query = @"SELECT definition FROM sys.sql_modules WHERE object_id = (OBJECT_ID(@0))";
            List<SqlParameter> sp = new List<SqlParameter>();
            sp.Add(new SqlParameter("@0", spName));

            var data = _instance.SQLQueryFunction.GetData(query, "", sp);
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
                    var sqlType = _instance.SQLQueryFunction.StringToSqlDbType(paramType);
                    colTypes.Add(new KeyValuePair<string, object>(paramName, sqlType));
                }
                catch (Exception)
                {
                    var colData = _instance.SQLQueryFunction.GetColumnUserData(paramType);
                    colTypes.Add(new KeyValuePair<string, object>(paramName, colData));
                }
                start = str.IndexOf('@', end2 + 1);
            }
            return colTypes;
        }
    }

    class SQLDBOperationsFunction : ParserFunction
    {
        private CscsSqlModuleInstance _instance;

        internal enum Mode { DROP_DB, CREATE_DB, DROP_TABLE };
        Mode m_mode;

        internal SQLDBOperationsFunction(CscsSqlModuleInstance instance, Mode mode)
        {
            _instance = instance;
            m_mode = mode;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);
            _instance.CheckConnectionString(script, m_name);

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
            using (SqlConnection con = new SqlConnection(_instance.ConnectionString))
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
        private CscsSqlModuleInstance _instance;

        public SQLColumnsFunction(CscsSqlModuleInstance instance)
        {
            _instance = instance;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);
            _instance.CheckConnectionString(script, m_name);

            var tableName = Utils.GetSafeString(args, 0);
            bool namesOnly = Utils.GetSafeInt(args, 1, 0) == 1;

            return GetColsData(tableName, namesOnly);
        }

        public Variable GetColsData(string tableName, bool namesOnly = false)
        {
            var colData = _instance.SQLQueryFunction.GetColumnData(tableName);

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