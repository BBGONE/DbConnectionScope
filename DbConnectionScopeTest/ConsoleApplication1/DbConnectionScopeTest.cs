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


            using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled))
            using (DbConnectionScope scope = new DbConnectionScope(DbConnectionScopeOption.Required))
            {
                Console.WriteLine("Starting On Thread: {0}", Thread.CurrentThread.ManagedThreadId);
                await Task.WhenAll(Enumerable.Range(1, 3).Select(i => FirstAsync(i, 100 * i)));
                Console.WriteLine("Ending On Thread: {0}", Thread.CurrentThread.ManagedThreadId);
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
                for (int i = 0; i < 100000; ++i)
                {
                    var str = Guid.NewGuid().ToString();
                    res = System.Text.Encoding.UTF8.GetBytes(str);
                }
                return res;
            });
            return bytes;
        }

        private static async Task CONNECTION_TASK(string state)
        {
            await Task.Run(async () =>
            {
                using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled))
                using (DbConnectionScope scope = new DbConnectionScope(DbConnectionScopeOption.Required))
                {
                    await WaitAndWriteAsync(0, state);
                    transactionScope.Complete();
                }
            });
        }

        static async Task FirstAsync(int num, int wait)
        {
            using (DbConnectionScope scope = new DbConnectionScope(DbConnectionScopeOption.Required))
            {
                using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled))
                using (DbConnectionScope scope2 = new DbConnectionScope(DbConnectionScopeOption.Required))
                {
                    Task[] tasks = { CheckOpenConnectionState(true), CheckOpenConnectionState(true), CheckOpenConnectionState(false), CheckOpenConnectionState(false) };
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                    transactionScope.Complete();
                }

                var bytes = await CPU_TASK();
                await WaitAndWriteAsync(wait, "firstTask").ConfigureAwait(false);

                using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled))
                using (DbConnectionScope scope2 = new DbConnectionScope(DbConnectionScopeOption.Required))
                {
                    Task[] tasks = { WaitAndWriteAsync(wait, "tranTask1"), WaitAndWriteAsync(wait, "tranTask2"), CPU_TASK(), CONNECTION_TASK("connTask1"), CONNECTION_TASK("connTask2") };
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                    transactionScope.Complete();
                }

                using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled))
                using (DbConnectionScope scope2 = new DbConnectionScope(DbConnectionScopeOption.Required))
                {
                    await WaitAndWriteAsync(wait, "lastTask").ConfigureAwait(false);
                    transactionScope.Complete();
                }
            }
        }

        //We are getting open connections in parralel and check that we get an open connection 
        static async Task CheckOpenConnectionState(bool isAsync = true)
        {
            bool res = false;
            if (isAsync)
            {
                SqlConnection conn = await GetSqlConnectionAsync();
                res = conn.State == System.Data.ConnectionState.Open;
                Console.WriteLine("Get Open Connection Result: {0}, isAsync: {1}, {2}", res, isAsync, conn.State);
            }
            else
            {
                SqlConnection conn = GetSqlConnection();
                res = conn.State == System.Data.ConnectionState.Open;
                Console.WriteLine("Get Open Connection Result: {0}, isAsync: {1}, {2}", res, isAsync, conn.State);
            }
        }

        static async Task WaitAndWriteAsync(int waitAmount, string state= "")
        {
            var bytes = await CPU_TASK();
            string sql = "select transaction_id from sys.dm_tran_current_transaction";
            SqlCommand cmd = new SqlCommand(sql);
            var localConn = await GetSqlConnectionAsync();
            cmd.Connection = localConn;
            object res = null;
            try
            {
                res = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            }
            catch(Exception ex)
            {
                Console.WriteLine(state + " "+ ex.Message);
            }

            Console.WriteLine("Thread: {0}, TransactID: {1}, state: {2}", Thread.CurrentThread.ManagedThreadId, res, state);
            if (waitAmount > 0)
            {
                //Recursive CALL
                //await Task.Delay(waitAmount);
                using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled))
                using (DbConnectionScope scope2 = new DbConnectionScope(DbConnectionScopeOption.Required))
                {
                    await WaitAndWriteAsync(0, "recurse");
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

        public static async Task<SqlConnection> GetSqlConnectionAsync()
        {
            SqlConnection cn = null;
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
