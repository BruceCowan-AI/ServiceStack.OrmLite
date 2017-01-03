using System;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using ServiceStack.Text;

namespace ServiceStack.OrmLite
{
    internal static class WriteExpressionCommandExtensions
    {
        public static int UpdateOnly<T>(this IDbCommand dbCmd,
            T model,
            SqlExpression<T> onlyFields,
            Action<IDbCommand> commandFilter = null)
        {
            UpdateOnlySql(dbCmd, model, onlyFields);
            commandFilter?.Invoke(dbCmd);
            return dbCmd.ExecNonQuery();
        }

        internal static void UpdateOnlySql<T>(this IDbCommand dbCmd, T model, SqlExpression<T> onlyFields)
        {
            OrmLiteConfig.UpdateFilter?.Invoke(dbCmd, model);

            var fieldsToUpdate = onlyFields.UpdateFields.Count == 0
                ? onlyFields.GetAllFields()
                : onlyFields.UpdateFields;

            onlyFields.CopyParamsTo(dbCmd);

            dbCmd.GetDialectProvider().PrepareUpdateRowStatement(dbCmd, model, fieldsToUpdate);

            if (!onlyFields.WhereExpression.IsNullOrEmpty())
                dbCmd.CommandText += " " + onlyFields.WhereExpression;
        }

        internal static int UpdateOnly<T>(this IDbCommand dbCmd, T obj,
            Expression<Func<T, object>> onlyFields = null,
            Expression<Func<T, bool>> where = null,
            Action<IDbCommand> commandFilter = null)
        {
            if (onlyFields == null)
                throw new ArgumentNullException("onlyFields");

            var q = dbCmd.GetDialectProvider().SqlExpression<T>();
            q.Update(onlyFields);
            q.Where(where);
            return dbCmd.UpdateOnly(obj, q, commandFilter);
        }

        internal static int UpdateOnly<T>(this IDbCommand dbCmd, T obj,
            string[] onlyFields = null,
            Expression<Func<T, bool>> where = null,
            Action<IDbCommand> commandFilter = null)
        {
            if (onlyFields == null)
                throw new ArgumentNullException("onlyFields");

            var q = dbCmd.GetDialectProvider().SqlExpression<T>();
            q.Update(onlyFields);
            q.Where(where);
            return dbCmd.UpdateOnly(obj, q, commandFilter);
        }

        internal static int UpdateOnly<T>(this IDbCommand dbCmd,
            Expression<Func<T>> updateFields,
            SqlExpression<T> q,
            Action<IDbCommand> commandFilter = null)
        {
            var cmd = dbCmd.InitUpdateOnly(updateFields, q);
            commandFilter?.Invoke(cmd);
            return cmd.ExecNonQuery();
        }

        internal static IDbCommand InitUpdateOnly<T>(this IDbCommand dbCmd, Expression<Func<T>> updateFields, SqlExpression<T> q)
        {
            if (updateFields == null)
                throw new ArgumentNullException("updateFields");

            if (OrmLiteConfig.UpdateFilter != null)
                OrmLiteConfig.UpdateFilter(dbCmd, CachedExpressionCompiler.Evaluate(updateFields));

            q.CopyParamsTo(dbCmd);

            var updateFieldValues = updateFields.AssignedValues();
            dbCmd.GetDialectProvider().PrepareUpdateRowStatement<T>(dbCmd, updateFieldValues, q.WhereExpression);

            return dbCmd;
        }

        public static int UpdateAdd<T>(this IDbCommand dbCmd,
            Expression<Func<T>> updateFields,
            SqlExpression<T> q,
            Action<IDbCommand> commandFilter)
        {
            var cmd = dbCmd.InitUpdateAdd(updateFields, q);
            commandFilter?.Invoke(cmd);
            return cmd.ExecNonQuery();
        }

        internal static IDbCommand InitUpdateAdd<T>(this IDbCommand dbCmd, Expression<Func<T>> updateFields, SqlExpression<T> q)
        {
            if (updateFields == null)
                throw new ArgumentNullException("updateFields");

            OrmLiteConfig.UpdateFilter?.Invoke(dbCmd, CachedExpressionCompiler.Evaluate(updateFields));

            q.CopyParamsTo(dbCmd);

            var updateFieldValues = updateFields.AssignedValues();
            dbCmd.GetDialectProvider().PrepareUpdateRowAddStatement<T>(dbCmd, updateFieldValues, q.WhereExpression);

            return dbCmd;
        }

        public static int UpdateNonDefaults<T>(this IDbCommand dbCmd, T item, Expression<Func<T, bool>> obj)
        {
            OrmLiteConfig.UpdateFilter?.Invoke(dbCmd, item);

            var q = dbCmd.GetDialectProvider().SqlExpression<T>();
            q.Where(obj);
            q.PrepareUpdateStatement(dbCmd, item, excludeDefaults: true);
            return dbCmd.ExecNonQuery();
        }

        public static int Update<T>(this IDbCommand dbCmd, T item, Expression<Func<T, bool>> expression, Action<IDbCommand> commandFilter = null)
        {
            OrmLiteConfig.UpdateFilter?.Invoke(dbCmd, item);

            var q = dbCmd.GetDialectProvider().SqlExpression<T>();
            q.Where(expression);
            q.PrepareUpdateStatement(dbCmd, item);
            commandFilter?.Invoke(dbCmd);
            return dbCmd.ExecNonQuery();
        }

        public static int Update<T>(this IDbCommand dbCmd, object updateOnly, Expression<Func<T, bool>> where = null, Action<IDbCommand> commandFilter = null)
        {
            var q = dbCmd.GetDialectProvider().SqlExpression<T>();
            var whereSql = q.Where(where).WhereExpression;
            q.CopyParamsTo(dbCmd);
            dbCmd.PrepareUpdateAnonSql<T>(dbCmd.GetDialectProvider(), updateOnly, whereSql);

            commandFilter?.Invoke(dbCmd);
            return dbCmd.ExecNonQuery();
        }

        internal static void PrepareUpdateAnonSql<T>(this IDbCommand dbCmd, IOrmLiteDialectProvider dialectProvider, object updateOnly, string whereSql)
        {
            var sql = StringBuilderCache.Allocate();
            var modelDef = typeof(T).GetModelDefinition();
            var fields = modelDef.FieldDefinitionsArray;

            foreach (var setField in updateOnly.GetType().GetPublicProperties())
            {
                var fieldDef = fields.FirstOrDefault(x => string.Equals(x.Name, setField.Name, StringComparison.OrdinalIgnoreCase));
                if (fieldDef == null || fieldDef.ShouldSkipUpdate()) continue;

                if (sql.Length > 0)
                    sql.Append(", ");

                var value = setField.GetPropertyGetterFn()(updateOnly);
                sql
                    .Append(dialectProvider.GetQuotedColumnName(fieldDef.FieldName))
                    .Append("=")
                    .Append(dialectProvider.AddParam(dbCmd, value, fieldDef.ColumnType).ParameterName);
            }

            dbCmd.CommandText = string.Format("UPDATE {0} SET {1} {2}",
                dialectProvider.GetQuotedTableName(modelDef), StringBuilderCache.ReturnAndFree(sql), whereSql);
        }

        public static void InsertOnly<T>(this IDbCommand dbCmd, T obj, string[] onlyFields)
        {
            if (OrmLiteConfig.InsertFilter != null)
                OrmLiteConfig.InsertFilter(dbCmd, obj);

            var sql = dbCmd.GetDialectProvider().ToInsertRowStatement(dbCmd, obj, onlyFields);
            dbCmd.ExecuteSql(sql);
        }

        public static int InsertOnly<T>(this IDbCommand dbCmd, Expression<Func<T>> insertFields)
        {
            if (insertFields == null)
                throw new ArgumentNullException("insertFields");

            OrmLiteConfig.InsertFilter?.Invoke(dbCmd, CachedExpressionCompiler.Evaluate(insertFields));

            var insertFieldsValues = insertFields.AssignedValues();
            dbCmd.GetDialectProvider().PrepareParameterizedInsertStatement<T>(dbCmd, insertFieldsValues.Keys);

            dbCmd.SetParameters(insertFieldsValues, excludeDefaults:false);

            return dbCmd.ExecNonQuery();
        }

        public static int Delete<T>(this IDbCommand dbCmd, Expression<Func<T, bool>> where)
        {
            var ev = dbCmd.GetDialectProvider().SqlExpression<T>();
            ev.Where(where);
            return dbCmd.Delete(ev);
        }

        public static int Delete<T>(this IDbCommand dbCmd, SqlExpression<T> where)
        {
            var sql = where.ToDeleteRowStatement();
            return dbCmd.ExecuteSql(sql, where.Params);
        }
    }
}

