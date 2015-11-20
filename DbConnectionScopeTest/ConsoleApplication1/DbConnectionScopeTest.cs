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
        public static async Task Start()
        {
            /*
            using (var afc = ExecutionContext.SuppressFlow())
            {
                Task.WaitAll(Enumerable.Range(1, 10).Select(async i =>
                {
                    await FirstAsync(i, 100 * i);
                }).ToArray());
            }
            */
            try
            {
                //The Test for getting only completely open connections (not in intermediate state like Connecting)
                using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled))
                using (DbConnectionScope scope = new DbConnectionScope(DbConnectionScopeOption.Required))
                {
                    Task[] tasks = { CheckOpenConnectionState(true), CheckOpenConnectionState(true), CheckOpenConnectionState(false), CheckOpenConnectionState(false) };
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                    transactionScope.Complete();
                }

                using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
                using (DbConnectionScope scope = new DbConnectionScope(DbConnectionScopeOption.Required))
                {
                    var topConnection = await ConnectionManager.GetSqlConnectionAsync();

                    Console.WriteLine("Starting On Thread: {0}", Thread.CurrentThread.ManagedThreadId);
                    await Task.WhenAll(Enumerable.Range(1, 3).Select(i => FirstAsync(i, 100 * i, topConnection)));
                    Console.WriteLine("Ending On Thread: {0}", Thread.CurrentThread.ManagedThreadId);
                    Console.WriteLine("Before Scope End: DbConnectionScope.GetScopeStoreCount()== {0}", DbConnectionScope.GetScopeStoreCount());
                    transactionScope.Complete();
                }
            }
            catch (AggregateException ex)
            {
                ex.Flatten().Handle((err) => {
                    Console.WriteLine();
                    Console.WriteLine(err.Message);
                    return true;
                });
            }
            catch(Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine(ex.Message);
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

        private static async Task CONNECTION_TASK(string state, SqlConnection topConnection)
        {
            await Task.Run(async () =>
            {
                using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.Required, TransactionScopeAsyncFlowOption.Enabled))
                using (DbConnectionScope scope = new DbConnectionScope(DbConnectionScopeOption.Required))
                {
                    await WaitAndWriteAsync(0, state, topConnection);
                    transactionScope.Complete();
                }
            });
        }

        static async Task FirstAsync(int num, int wait, SqlConnection topConnection)
        {
            try
            {
                using (UnitOfWork unitOfWork = new UnitOfWork())
                {
                    byte[] bytes = null;
                    Task[] tasks1 = { CPU_TASK(), WaitAndWriteAsync(0, "firstTask", topConnection) };
                    await Task.WhenAll(tasks1).ConfigureAwait(false);
                    bytes = (tasks1[0] as Task<byte[]>).Result;
                    var executedOnConnection = (tasks1[1] as Task<SqlConnection>).Result;

                    using (UnitOfWork unitOfWork1 = new UnitOfWork())
                    {
                        Task[] tasks2 = { WaitAndWriteAsync(0, "tranTask1", topConnection), 
                                     WaitAndWriteAsync(0, "tranTask2", topConnection), 
                                     CPU_TASK(), 
                                     CONNECTION_TASK("connTask1", topConnection), 
                                     CONNECTION_TASK("connTask2", topConnection) };
                        await Task.WhenAll(tasks2).ConfigureAwait(false);
                        bytes = (tasks2[2] as Task<byte[]>).Result;
                        var conn1 = (tasks2[0] as Task<SqlConnection>).Result;
                        var conn2 = (tasks2[1] as Task<SqlConnection>).Result;
                        Console.WriteLine();
                        Console.WriteLine("Reusing 2 connections result: {0}, state: {1}", Object.ReferenceEquals(conn1, conn2), "Task1 and Task2");
                        Console.WriteLine("Reusing 3 connections result: {0}, state: {1}", Object.ReferenceEquals(conn1, conn2) && Object.ReferenceEquals(conn1, executedOnConnection), "Task1 and Task2 and firstTask");
                        Console.WriteLine();

                        using (UnitOfWork unitOfWork2 = new UnitOfWork())
                        {
                            await WaitAndWriteAsync(0, "lastTask", topConnection).ConfigureAwait(false);
                            unitOfWork2.Complete();
                        }

                        using (UnitOfWork unitOfWork3 = new UnitOfWork(TransactionScopeOption.RequiresNew))
                        {
                            bool isEqual = false;
                            var localConn1 = await ConnectionManager.GetSqlConnectionAsync();
                            var localConn2 = await ConnectionManager.GetSqlConnectionAsync();
                            isEqual = Object.ReferenceEquals(localConn1, localConn2);
       
                            Console.WriteLine();
                            Console.WriteLine("Reusing connection in the same scope Passed: {0}", isEqual);
                            Console.WriteLine();
                            unitOfWork3.Complete();
                        }
                       
                        unitOfWork1.Complete();
                    }

                    unitOfWork.Complete();
                }
            }
            catch (AggregateException ex)
            {
                ex.Flatten().Handle((err) => {
                    Console.WriteLine();
                    Console.WriteLine(err.Message);
                    return true;
                });
            }
            catch(Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine(ex.Message);
            }
        }

        //We are getting open connections in parralel and check that we get an open connection 
        static async Task CheckOpenConnectionState(bool isAsync = true)
        {
            bool res = false;
            if (isAsync)
            {
                SqlConnection conn = await ConnectionManager.GetSqlConnectionAsync();
                res = conn.State == System.Data.ConnectionState.Open;
                Console.WriteLine("Get Open Connection Result: {0}, isAsync: {1}, {2}", res, isAsync, conn.State);
            }
            else
            {
                SqlConnection conn = ConnectionManager.GetSqlConnection();
                res = conn.State == System.Data.ConnectionState.Open;
                Console.WriteLine("Get Open Connection Result: {0}, isAsync: {1}, {2}", res, isAsync, conn.State);
            }
        }

        static async Task<SqlConnection> ExecuteCommand(string state, SqlConnection topConnection)
        {
            string sql = "WAITFOR DELAY '00:00:00.25';select transaction_id from sys.dm_tran_current_transaction";
            SqlCommand cmd = new SqlCommand(sql);
            var localConn = await ConnectionManager.GetSqlConnectionAsync();
            cmd.Connection = localConn;

            if (state.StartsWith("lastTask-recurse"))
            {
                var currScope = DbConnectionScope.Current;
                bool isEqual = Object.ReferenceEquals(topConnection, localConn);
                bool IsMustBeEqual = currScope.Option == DbConnectionScopeOption.Required;
                bool isTestPassed = isEqual == IsMustBeEqual;
                Console.WriteLine();
                Console.WriteLine("Reusing connection test Passed: {0}, state: {1}", isTestPassed, state);
                Console.WriteLine();
            }
        
            object res = null;
            try
            {
                res = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine(state + " " + ex.Message);
            }

            Console.WriteLine("Thread: {0}, TransactID: {1}, state: {2}", Thread.CurrentThread.ManagedThreadId, res, state);
            return localConn;
        }

        static async Task<SqlConnection> WaitAndWriteAsync(int level, string state, SqlConnection topConnection)
        {
            var bytes = await CPU_TASK();
            var executedOnConnection =  await ExecuteCommand(state, topConnection);
            if (level < 1)
            {
                int nextLevel = level + 1;
                //Recursive CALL
                using (UnitOfWork unitOfWork1 = new UnitOfWork())
                {
                    var executedOnConnection1 = await WaitAndWriteAsync(nextLevel, state + "-transRecurse:" + nextLevel, topConnection);
                    unitOfWork1.Complete();
                }

                using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
                using (DbConnectionScope scope2 = new DbConnectionScope(DbConnectionScopeOption.Required))
                {
                    var executedOnConnection2 = await WaitAndWriteAsync(nextLevel, state + "-recurse1:" + nextLevel, topConnection);
                    transactionScope.Complete();
                }

                using (TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
                using (DbConnectionScope scope2 = new DbConnectionScope(DbConnectionScopeOption.RequiresNew))
                {
                    var executedOnConnection3 = await WaitAndWriteAsync(nextLevel, state + "-recurse2:" + nextLevel, topConnection);
                    transactionScope.Complete();
                } 
            }
            return executedOnConnection;
        }
    }
}
