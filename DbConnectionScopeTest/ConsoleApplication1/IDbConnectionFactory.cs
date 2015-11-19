using System.Data.Common;

namespace Bell.PPS.Database.Shared
{
    public interface IDbConnectionFactory
    {
        DbConnection CreateConnection(string connectionName);
    }
}
