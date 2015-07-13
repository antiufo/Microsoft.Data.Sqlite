// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite.Interop;
using Xunit;

namespace Microsoft.Data.Sqlite
{
    public class SqliteConcurrencyTest : IDisposable
    {
        private const int SQLITE_BUSY = 5;
        private const int SQLITE_LOCKED = 6;

        public SqliteConcurrencyTest()
        {
            using (var connection = CreateConnection())
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS a (b);
INSERT INTO a VALUES (1);
INSERT INTO a VALUES (2);";
                command.ExecuteNonQuery();
            }
        }

        [Fact]
        public void Private_cache_results_in_locking()
        {
            var list = new List<Action>();
            for (var i = 0; i < 100; i++)
            {
                var copy_i = i;
                list.Add(() =>
                {
                    using (var connection = CreateConnection(shared: false))
                    {
                        connection.Open();
                        var command = connection.CreateCommand();
                        if (copy_i % 2 == 0)
                        {
                            command.CommandText = "SELECT * FROM a;";
                            using (var reader = command.ExecuteReader())
                            {
                                while (reader.Read());
                            }
                        }
                        else
                        {
                            command.CommandText = "INSERT INTO a VALUES ( 1);";
                            Assert.Equal(1, command.ExecuteNonQuery());
                        }
                    }
                });
                {

                }
            }
            var tasks = list.Select(execute => Task.Factory.StartNew(execute)).ToArray();
            var ex = Assert.Throws<AggregateException>(()=>Task.WaitAll(tasks));
            Assert.True(ex.InnerExceptions?.Any(t=>(t as SqliteException)?.SqliteErrorCode==SQLITE_BUSY));
        }

        [Fact]
        public void Shared_cache_avoids_locked()
        {
            var list = new List<Action>();
            for (var i = 0; i < 100; i++)
            {
                var copy_i = i;
                list.Add(() =>
                    {
                        using (var connection = CreateConnection(shared: true))
                        {
                            connection.Open();
                            var command = connection.CreateCommand();
                            if (copy_i % 2 == 0)
                            {
                                command.CommandText = "SELECT * FROM a;";
                                using (var reader = command.ExecuteReader())
                                {
                                    while(reader.Read());
                                }
                            }
                            else
                            {
                                command.CommandText = "INSERT INTO a VALUES ( 1);";
                                Assert.Equal(1, command.ExecuteNonQuery());
                            }
                        }
                    });
                {
                    
                }
            }
            var tasks = list.Select(execute => Task.Factory.StartNew(execute)).ToArray();
            Task.WaitAll(tasks);
        }

        [Fact]
        public void It_throws_timeout_on_deadlock()
        {
            using (var connection = CreateConnection())
            {
                var selectCommand = connection.CreateCommand();
                connection.Open();
                selectCommand.CommandText = "SELECT * FROM a;";

                var dropCommand = connection.CreateCommand();
                dropCommand.CommandTimeout = 10;
                dropCommand.CommandText = "DROP TABLE a;";

                using (var reader = selectCommand.ExecuteReader())
                {
                    reader.Read();
                    var ex = Assert.Throws<TimeoutException>(() => dropCommand.ExecuteNonQuery());

                    Assert.Equal(Strings.CommandTimeout, ex.Message);
                }

                dropCommand.ExecuteNonQuery();
                Assert.Throws<SqliteException>(() => dropCommand.ExecuteNonQuery());
            }
        }

        [Fact]
        public void It_throws_sqlite_busy()
        {
            using (var connection = CreateConnection())
            {
                var selectCommand = connection.CreateCommand();
                connection.Open();
                selectCommand.CommandText = "SELECT * FROM a;";
                using (var connection2 = CreateConnection())
                {
                    var dropCommand = connection2.CreateCommand();
                    connection2.Open();
                    dropCommand.CommandText = "DROP TABLE a;";
                    using (var reader = selectCommand.ExecuteReader())
                    {
                        reader.Read();
                        var ex = Assert.Throws<SqliteException>(() => dropCommand.ExecuteNonQuery());

                        Assert.Equal(SQLITE_BUSY, ex.SqliteErrorCode);
                        var message = NativeMethods.sqlite3_errstr(SQLITE_BUSY);
                        Assert.Equal(Strings.FormatSqliteNativeError(SQLITE_BUSY, message), ex.Message);
                    }

                    dropCommand.ExecuteNonQuery();
                    Assert.Throws<SqliteException>(() => dropCommand.ExecuteNonQuery());
                }
            }
        }

        private const string FileName = "./concurrency.db";

        private DbConnection CreateConnection(bool shared = false) => new SqliteConnection($"Data Source={FileName};Cache={(shared ? "Shared" : "Private")}");

        public void Dispose()
        {
            if (File.Exists(FileName))
            {
                File.Delete(FileName);
            }
        }
    }
}
