using System;
using System.Data.Common;
using System.Configuration;
using System.Data.SqlClient;

namespace Bell.PPS.Database.Shared
{
    public class DbConnectionFactory : IDbConnectionFactory
    {
        private static Lazy<DbConnectionFactory> _instance = new Lazy<DbConnectionFactory>(() => new DbConnectionFactory(), true);

        public virtual string GetConnectionString(string connectionName)
        {
            ConnectionStringSettings connstrings = ConfigurationManager.ConnectionStrings[connectionName];
            if (connstrings == null)
            {
                throw new Exception(string.Format("Connection string {0} was not found", connectionName));
            }
            return connstrings.ConnectionString;
        }

        public DbConnection CreateConnection(string connectionName)
        {
            string connectionString = GetConnectionString(connectionName);
            var result = SqlClientFactory.Instance.CreateConnection();
            result.ConnectionString = connectionString;
            return result;
        }

        public static DbConnectionFactory Instance
        {
            get { return _instance.Value; }
        }
    }
}
