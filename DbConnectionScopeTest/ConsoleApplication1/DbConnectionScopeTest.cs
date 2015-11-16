using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bell.PPS.Database.Shared;
using System.Data.SqlClient;
using System.Transactions;

namespace ConsoleApplication1
{
    class DbConnectionScopeTest
    {
        private static string connectionString1;

        public static async Task Start(string connectionString)
        {
            connectionString1 = connectionString;
            /*
            using (var afc = ExecutionContext.SuppressFlow())
            {
                Task.WaitAll(Enumerable.Range(1, 10).Select(async i =>
                {
                    await FirstAsync(i, 100 * i);
                }).ToArray());
            }
            */


            using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled))
            using (DbConnectionScope scope = new DbConnectionScope(DbConnectionScopeOption.Required))
            {
                Console.WriteLine("Starting On Thread: {0}", Thread.CurrentThread.ManagedThreadId);
                var conn1 = GetSqlConnection();
                await Task.WhenAll(Enumerable.Range(1, 3).Select(i => FirstAsync(i, 100 * i)));
                var conn2 = GetSqlConnection();
                Console.WriteLine("Ending On Thread: {0}, Test Passed: {1}", Thread.CurrentThread.ManagedThreadId, Object.ReferenceEquals(conn1, conn2));
                Console.WriteLine("Before Scope End: DbConnectionScope.GetScopeStoreCount()== {0}",  DbConnectionScope.GetScopeStoreCount());
                transactionScope.Complete();
            }
            Console.WriteLine("After Scope End: DbConnectionScope.GetScopeStoreCount()== {0}", DbConnectionScope.GetScopeStoreCount());
        }
        private static async Task<byte[]> CPU_TASK()
        {
            var bytes = await Task.Run(() =>
            {
                byte[] res = new byte[0];
                for (int i = 0; i < 10000; ++i)
                {
                    var str = Guid.NewGuid().ToString();
                    res = System.Text.Encoding.UTF8.GetBytes(str);
                }
                return res;
            });
            return bytes;
        }

        private static async Task CONNECTION_TASK(SqlConnection expected_conn)
        {
            await Task.Run(async () =>
            {
                using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled))
                using (DbConnectionScope scope = new DbConnectionScope(DbConnectionScopeOption.Required))
                {
                    await WaitAndWriteAsync(0, expected_conn, true, "CTask");
                    transactionScope.Complete();
                }
            });
        }

        static async Task FirstAsync(int num, int wait)
        {
            using (DbConnectionScope scope = new DbConnectionScope(DbConnectionScopeOption.Required))
            {
                var bytes = await CPU_TASK();
                var conn = GetSqlConnection();
                await WaitAndWriteAsync(wait, conn, true, "first").ConfigureAwait(false);

                using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled))
                using (DbConnectionScope scope2 = new DbConnectionScope(DbConnectionScopeOption.Required))
                {
                    var localConn = GetSqlConnection();
                    Task[] tasks = { WaitAndWriteAsync(wait, conn, false, "tran1"), WaitAndWriteAsync(wait, conn, false, "tran2"), CPU_TASK(), CONNECTION_TASK(localConn), CONNECTION_TASK(localConn) };
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                    transactionScope.Complete();
                }

                using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled))
                using (DbConnectionScope scope2 = new DbConnectionScope(DbConnectionScopeOption.RequiresNew))
                {
                    await WaitAndWriteAsync(wait, conn, false, "new1").ConfigureAwait(false);
                    transactionScope.Complete();
                }
            }
        }

        static async Task WaitAndWriteAsync(int waitAmount, SqlConnection expectedConn, bool ShouldBeEqual, string state= "")
        {
            var bytes = await CPU_TASK();
            //string sql = "SELECT TOP 1 [ProductID] FROM [SalesLT].[Product] ORDER BY NewID()";
            string sql = "select transaction_id from sys.dm_tran_current_transaction";
            SqlCommand cmd = new SqlCommand(sql);
            var localConn = GetSqlConnection();
            cmd.Connection = localConn;
            bool isTheyEqual = Object.ReferenceEquals(expectedConn, localConn);
            object res = null;
            try
            {
                res = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            }
            catch(Exception ex)
            {
                Console.WriteLine(state + " "+ ex.Message);
            }

            Console.WriteLine("Thread: {0}, CmdResult: {1}, Test Passed: {2}, state: {3}", Thread.CurrentThread.ManagedThreadId, res, (isTheyEqual == ShouldBeEqual), state);
            if (waitAmount > 0)
            {
                //Recursive CALL
                await Task.Delay(waitAmount);
                using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled))
                using (DbConnectionScope scope2 = new DbConnectionScope(DbConnectionScopeOption.Required))
                {
                    await WaitAndWriteAsync(0, localConn, false, "recurse");
                    transactionScope.Complete();
                } 
            }
        }

        public static SqlConnection GetSqlConnection()
        {
            SqlConnection cn = null;
            try
            {
                cn = (SqlConnection)DbConnectionScope.Current.GetOpenConnection(SqlClientFactory.Instance, connectionString1);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw;
            }
            return cn;
        }
    }
}
