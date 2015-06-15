// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite.Interop;

namespace Microsoft.Data.Sqlite
{
    /// <summary>
    /// Represents a SQL statement to be executed against a SQLite database.
    /// </summary>
    public class SqliteCommand : DbCommand
    {
        private readonly Lazy<SqliteParameterCollection> _parameters = new Lazy<SqliteParameterCollection>(
            () => new SqliteParameterCollection());

        public SqliteCommand()
        {
        }

        public SqliteCommand(string commandText)
        {
            CommandText = commandText;
        }

        public SqliteCommand(string commandText, SqliteConnection connection)
            : this(commandText)
        {
            Connection = connection;
        }

        public SqliteCommand(string commandText, SqliteConnection connection, SqliteTransaction transaction)
            : this(commandText, connection)
        {
            Transaction = transaction;
        }

        public override CommandType CommandType
        {
            get { return CommandType.Text; }
            set
            {
                if (value != CommandType.Text)
                {
                    throw new ArgumentException(Strings.FormatInvalidCommandType(value));
                }
            }
        }

        public override string CommandText { get; set; }
        public new virtual SqliteConnection Connection { get; set; }

        protected override DbConnection DbConnection
        {
            get { return Connection; }
            set { Connection = (SqliteConnection)value; }
        }

        public new virtual SqliteTransaction Transaction { get; set; }

        protected override DbTransaction DbTransaction
        {
            get { return Transaction; }
            set { Transaction = (SqliteTransaction)value; }
        }

        public new virtual SqliteParameterCollection Parameters => _parameters.Value;
        protected override DbParameterCollection DbParameterCollection => Parameters;
        public override int CommandTimeout { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        public new virtual SqliteParameter CreateParameter() => new SqliteParameter();
        protected override DbParameter CreateDbParameter() => CreateParameter();

        public override void Prepare()
        {
        }

        public new virtual SqliteDataReader ExecuteReader() => ExecuteReader(CommandBehavior.Default);

        public new virtual SqliteDataReader ExecuteReader(CommandBehavior behavior)
        {
            if (behavior != CommandBehavior.Default)
            {
                throw new ArgumentException(Strings.FormatInvalidCommandBehavior(behavior));
            }
            if (Connection == null
                || Connection.State != ConnectionState.Open)
            {
                throw new InvalidOperationException(Strings.FormatCallRequiresOpenConnection("ExecuteReader"));
            }
            if (string.IsNullOrEmpty(CommandText))
            {
                throw new InvalidOperationException(Strings.FormatCallRequiresSetCommandText("ExecuteReader"));
            }
            if (Transaction != Connection.Transaction)
            {
                throw new InvalidOperationException(
                    Transaction == null
                        ? Strings.TransactionRequired
                        : Strings.TransactionConnectionMismatch);
            }

            var hasChanges = false;
            var changes = 0;
            var stmts = new Queue<Tuple<Sqlite3StmtHandle, bool>>();
            var tail = CommandText;
            do
            {
                Sqlite3StmtHandle stmt;
                var rc = NativeMethods.sqlite3_prepare_v2(
                    Connection.DbHandle,
                    tail,
                    out stmt,
                    out tail);
                MarshalEx.ThrowExceptionForRC(rc, Connection.DbHandle);

                // Statement was empty, white space, or a comment
                if (stmt.IsInvalid)
                {
                    if (!string.IsNullOrEmpty(tail))
                    {
                        continue;
                    }

                    break;
                }

                var boundParams = 0;

                if (_parameters.IsValueCreated)
                {
                    boundParams = _parameters.Value.Bind(stmt);
                }

                var expectedParams = NativeMethods.sqlite3_bind_parameter_count(stmt);
                if (expectedParams != boundParams)
                {
                    var unboundParams = new List<string>();
                    for (var i = 1; i <= expectedParams; i++)
                    {
                        var name = NativeMethods.sqlite3_bind_parameter_name(stmt, i);

                        if (_parameters.IsValueCreated
                            ||
                            !_parameters.Value.Cast<SqliteParameter>().Any(p => p.ParameterName == name))
                        {
                            unboundParams.Add(name);
                        }
                    }
                    throw new InvalidOperationException(Strings.FormatMissingParameters(string.Join(", ", unboundParams)));
                }

                rc = NativeMethods.sqlite3_step(stmt);
                MarshalEx.ThrowExceptionForRC(rc, Connection.DbHandle);

                // NB: This is only a heuristic to separate SELECT statements from INSERT/UPDATE/DELETE statements. It will result
                //     in unexpected corner cases, but it's the best we can do without re-parsing SQL
                if (NativeMethods.sqlite3_stmt_readonly(stmt) != 0)
                {
                    stmts.Enqueue(Tuple.Create(stmt, rc != Constants.SQLITE_DONE));
                }
                else
                {
                    hasChanges = true;
                    changes += NativeMethods.sqlite3_changes(Connection.DbHandle);
                    stmt.Dispose();
                }
            }
            while (!string.IsNullOrEmpty(tail));

            return new SqliteDataReader(Connection.DbHandle, stmts, hasChanges ? changes : -1);
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => ExecuteReader(behavior);

        public new virtual Task<SqliteDataReader> ExecuteReaderAsync() =>
            ExecuteReaderAsync(CommandBehavior.Default, CancellationToken.None);

        public new virtual Task<SqliteDataReader> ExecuteReaderAsync(CancellationToken cancellationToken) =>
            ExecuteReaderAsync(CommandBehavior.Default, cancellationToken);

        public new virtual Task<SqliteDataReader> ExecuteReaderAsync(CommandBehavior behavior) =>
            ExecuteReaderAsync(behavior, CancellationToken.None);

        public new virtual Task<SqliteDataReader> ExecuteReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(ExecuteReader(behavior));
        }

        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(
            CommandBehavior behavior,
            CancellationToken cancellationToken) =>
                await ExecuteReaderAsync(behavior, cancellationToken);

        public override int ExecuteNonQuery()
        {
            if (Connection == null
                || Connection.State != ConnectionState.Open)
            {
                throw new InvalidOperationException(Strings.FormatCallRequiresOpenConnection("ExecuteNonQuery"));
            }
            if (CommandText == null)
            {
                throw new InvalidOperationException(Strings.FormatCallRequiresSetCommandText("ExecuteNonQuery"));
            }

            var reader = ExecuteReader();
            reader.Dispose();

            return reader.RecordsAffected;
        }

        public override object ExecuteScalar()
        {
            if (Connection == null
                || Connection.State != ConnectionState.Open)
            {
                throw new InvalidOperationException(Strings.FormatCallRequiresOpenConnection("ExecuteScalar"));
            }
            if (CommandText == null)
            {
                throw new InvalidOperationException(Strings.FormatCallRequiresSetCommandText("ExecuteScalar"));
            }

            using (var reader = ExecuteReader())
            {
                return reader.Read()
                    ? reader.GetValue(0)
                    : null;
            }
        }

        public override void Cancel()
        {
            throw new NotSupportedException();
        }
    }
}
