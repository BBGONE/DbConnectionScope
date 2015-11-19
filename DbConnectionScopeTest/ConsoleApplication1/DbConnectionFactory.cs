using System;
using System.Data.Common;
using System.Configuration;
using System.Data.SqlClient;

namespace Bell.PPS.Database.Shared
{
    public class DbConnectionFactory : IDbConnectionFactory
    {
        public DbConnection CreateConnection(string connectionName)
        {
            ConnectionStringSettings connStrings = ConfigurationManager.ConnectionStrings[connectionName];
            if (connStrings == null)
            {
                throw new InvalidOperationException(string.Format("Connection string {0} is not found", connectionName));
            }
            var result =  SqlClientFactory.Instance.CreateConnection();
            result.ConnectionString = connStrings.ConnectionString;
            return result;
        }
    }
}
