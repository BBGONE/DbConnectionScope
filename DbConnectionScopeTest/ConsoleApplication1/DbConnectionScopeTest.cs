using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bell.PPS.Database.Shared;
using System.Data.SqlClient;
using System.Transactions;
using System.Diagnostics;

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
                // The Test for getting only completely open connections (not in intermediate state like Connecting)
                using (DbScope dbScope = new DbScope())
                {
                    Task[] tasks = { CheckOpenConnectionState(true), CheckOpenConnectionState(true), CheckOpenConnectionState(false), CheckOpenConnectionState(false) };
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                    dbScope.Complete();
                }

                using (DbScope dbScope = new DbScope(TransactionScopeOption.Suppress))
                {
                    var topConnection = await ConnectionManager.GetSqlConnectionAsync();

                    Console.WriteLine("Starting On Thread: {0}", Thread.CurrentThread.ManagedThreadId);
                    await Task.WhenAll(Enumerable.Range(1, 3).Select(i => FirstAsync(i, 100 * i, topConnection)));
                    Console.WriteLine("Ending On Thread: {0}", Thread.CurrentThread.ManagedThreadId);
                    dbScope.Complete();
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

        private static Task<byte[]> CPU_TASK()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            var task = Task.Run(() =>
            {
                byte[] res = new byte[0];
                for (int i = 0; i < 200000; ++i)
                {
                    var str = Guid.NewGuid().ToString();
                    res = System.Text.Encoding.UTF8.GetBytes(str);
                }
                return res;
            });
            task.ContinueWith((antecedent) => {
                sw.Stop();
                Console.WriteLine("CPU_TASK executed for {0} milliseconds", sw.ElapsedMilliseconds);
                if (antecedent.IsFaulted)
                    Console.WriteLine("CPU_TASK ended with error: {0}", antecedent.Exception.Message);
            });
            return task;
        }

        private static Task CONNECTION_TASK(string state, SqlConnection topConnection)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            var task = Task.Run(async () =>
            {
                using (DbScope dbScope = new DbScope(TransactionScopeOption.RequiresNew))
                {
                    await WaitAndWriteAsync(0, state, topConnection);
                    dbScope.Complete();
                }
            });
            task.ContinueWith((antecedent) => {
                sw.Stop();
                Console.WriteLine("CONNECTION_TASK {0} executed for {1} milliseconds", state, sw.ElapsedMilliseconds);
                if (antecedent.IsFaulted)
                    Console.WriteLine("CONNECTION_TASK {0} ended with error: {1}",state, antecedent.Exception.Message);
            });
            return task;
        }

        static async Task FirstAsync(int num, int wait, SqlConnection topConnection)
        {
            try
            {
                using (DbScope dbScope = new DbScope())
                {
                    byte[] bytes = null;
                    Task[] tasks1 = { CPU_TASK(), WaitAndWriteAsync(0, "firstTask", topConnection) };
                    await Task.WhenAll(tasks1).ConfigureAwait(false);
                    bytes = (tasks1[0] as Task<byte[]>).Result;
                    var executedOnConnection = (tasks1[1] as Task<SqlConnection>).Result;

                    using (DbScope dbScope1 = new DbScope())
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

                        using (DbScope dbScope2 = new DbScope())
                        {
                            await WaitAndWriteAsync(0, "lastTask", topConnection).ConfigureAwait(false);
                            dbScope2.Complete();
                        }

                        using (DbScope dbScope3 = new DbScope(TransactionScopeOption.RequiresNew))
                        {
                            bool isEqual = false;
                            var localConn1 = await ConnectionManager.GetSqlConnectionAsync();
                            var localConn2 = await ConnectionManager.GetSqlConnectionAsync();
                            isEqual = Object.ReferenceEquals(localConn1, localConn2);
       
                            Console.WriteLine();
                            Console.WriteLine("Reusing connection in the same scope Passed: {0}", isEqual);
                            Console.WriteLine();
                            dbScope3.Complete();
                        }
                       
                        dbScope1.Complete();
                    }

                    dbScope.Complete();
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
                using (DbScope dbScope1 = new DbScope())
                {
                    var executedOnConnection1 = await WaitAndWriteAsync(nextLevel, state + "-transRecurse:" + nextLevel, topConnection);
                    dbScope1.Complete();
                }

           
                using (DbScope dbScope3 = new DbScope(TransactionScopeOption.Suppress))
                {
                    using (DbScope dbScope2 = new DbScope(TransactionScopeOption.Suppress))
                    {
                        var executedOnConnection2 = await WaitAndWriteAsync(nextLevel, state + "-recurse1:" + nextLevel, topConnection);
                        dbScope2.Complete();
                    }
                    var executedOnConnection3 = await WaitAndWriteAsync(nextLevel, state + "-recurse2:" + nextLevel, topConnection);
                    dbScope3.Complete();
                } 
            }
            return executedOnConnection;
        }
    }
}
