using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
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

            Console.WriteLine("Initial Thread: {0}", Thread.CurrentThread.ManagedThreadId);
            await Task.Yield();
            Console.WriteLine("After Yield Thread: {0}", Thread.CurrentThread.ManagedThreadId);

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
                await Task.Yield();

                var conn1 = GetSqlConnection();
                await Task.WhenAll(Enumerable.Range(1, 5).Select(async i =>
                {
                    await FirstAsync(i, 100 * i);
                }).ToArray());
                var conn2 = GetSqlConnection();
                Console.WriteLine("Ending On Thread: {0}, Test Passed: {1}", Thread.CurrentThread.ManagedThreadId, Object.ReferenceEquals(conn1, conn2));
                Console.WriteLine("DbConnectionScope.GetScopeStoreCount()==1, Now: {1}, Test Passed: {2}", Thread.CurrentThread.ManagedThreadId, DbConnectionScope.GetScopeStoreCount(), DbConnectionScope.GetScopeStoreCount()==1);
            }


            Console.WriteLine("DbConnectionScope.GetScopeStoreCount()==0, Now: {1}, Test Passed: {2}", Thread.CurrentThread.ManagedThreadId, DbConnectionScope.GetScopeStoreCount(), DbConnectionScope.GetScopeStoreCount() == 0);
        }

        static async Task FirstAsync(int num, int wait)
        {
            using (DbConnectionScope scope = new DbConnectionScope(DbConnectionScopeOption.Required))
            {
                await Task.Yield();
                await Task.Delay(300 * num).ConfigureAwait(false);
                var conn = GetSqlConnection();
                for (int i = 0; i < 1; i++)
                {
                    await WaitAndWriteAsync(wait, conn, true);

                    using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled))
                    //using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
                    using (DbConnectionScope scope2 = new DbConnectionScope(DbConnectionScopeOption.Required))
                    {
                        await WaitAndWriteAsync(wait, conn, true);
                    } 

                    using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled))
                    //using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
                    using (DbConnectionScope scope2 = new DbConnectionScope(DbConnectionScopeOption.RequiresNew))
                    {
                        await WaitAndWriteAsync(wait, conn, false);
                    }
                }
            }
        }

        static async Task WaitAndWriteAsync(int waitAmount, SqlConnection expectedConn, bool ShouldBeEqual)
        {
            await Task.Delay(waitAmount==0?100: waitAmount);
            SqlCommand cmd = new SqlCommand("SELECT TOP 1 [ProductID] FROM [SalesLT].[Product] ORDER BY NewID()");
            var localConn = await GetSqlConnectionAsync();
            cmd.Connection = localConn;
            bool isTheyEqual = Object.ReferenceEquals(expectedConn, localConn);
            object res = await cmd.ExecuteScalarAsync().ConfigureAwait(false);

            Console.WriteLine("Thread: {0}, CmdResult: {1},  Test Passed: {2}", Thread.CurrentThread.ManagedThreadId, res, (isTheyEqual == ShouldBeEqual));

            if (waitAmount > 0)
            {
                await Task.Delay(waitAmount);
                using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled))
                //using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
                using (DbConnectionScope scope2 = new DbConnectionScope(DbConnectionScopeOption.Required))
                {
                    await WaitAndWriteAsync(0, localConn, true);
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

        public async static Task<SqlConnection> GetSqlConnectionAsync()
        {
            SqlConnection cn= null;
            try
            {
                cn = (SqlConnection) await DbConnectionScope.Current.GetOpenConnectionAsync(SqlClientFactory.Instance, connectionString1);
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
