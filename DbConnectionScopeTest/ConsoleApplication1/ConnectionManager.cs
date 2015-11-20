using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bell.PPS.Database.Shared
{
    public static class ConnectionManager
    {
        public const string CONNECTION_STRING_NAME = "DbConnectionString";
        private static Lazy<DbNameConnectionFactory> _dbNameFactory = new Lazy<DbNameConnectionFactory>(()=> new DbNameConnectionFactory(CONNECTION_STRING_NAME), true);
       
        public static string GetDefaultConnectionString()
        {
            ConnectionStringSettings connstrings = ConfigurationManager.ConnectionStrings[CONNECTION_STRING_NAME];
            if (connstrings == null)
            {
                throw new Exception(string.Format("Connection string {0} was not found", CONNECTION_STRING_NAME));
            }
            return connstrings.ConnectionString;
        }

        public static SqlConnection GetSqlConnection()
        {
            return DbConnectionScope.GetOpenConnection<SqlConnection>(DbConnectionFactory.Instance, CONNECTION_STRING_NAME);
        }

        public static async Task<SqlConnection> GetSqlConnectionAsync()
        {
            return await DbConnectionScope.GetOpenConnectionAsync<SqlConnection>(DbConnectionFactory.Instance, CONNECTION_STRING_NAME);
        }

        public static SqlConnection GetSqlConnection(string dbname)
        {
            return DbConnectionScope.GetOpenConnection<SqlConnection>(_dbNameFactory.Value, dbname);
        }

        public static async Task<SqlConnection> GetSqlConnectionAsync(string dbname)
        {
            return await DbConnectionScope.GetOpenConnectionAsync<SqlConnection>(_dbNameFactory.Value, dbname);
        }
    }
}
