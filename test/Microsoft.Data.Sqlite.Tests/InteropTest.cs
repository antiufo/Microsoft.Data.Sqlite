// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Data.Sqlite
{
    public class InteropTest
    {
        [Fact]
        public void Library_is_compiled_with_enable_unlock()
        {
            var options = new List<string>();
            using (var connection = new SqliteConnection("Data Source=:memory:"))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "PRAGMA compile_options";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {

                        options.Add(reader.GetFieldValue<string>(0));
                    }
                }
            }
            Assert.Contains(options, p => p == "ENABLE_UNLOCK_NOTIFY");
        }
    }
}
