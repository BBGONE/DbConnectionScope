using Bell.PPS.Database.Shared;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;

namespace ConsoleApplication1
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
                int cnt = 1;
                Task.Run(async () =>
                {
                    Stopwatch sw = new Stopwatch();
                    sw.Start();
                    var task = DbConnectionScopeTest.Start();
                    await task;
                    var res = Interlocked.Decrement(ref cnt);
                    if (res == 0)
                        tcs.SetResult(null);
                    sw.Stop();
                    Console.WriteLine("Elapsed time (milliseconds) {0}", sw.ElapsedMilliseconds);
                });
                tcs.Task.Wait(60000);
                Console.WriteLine("After End: DbConnectionScope.GetScopeStoreCount()== {0}", DbConnectionScope.GetScopeStoreCount());
            }
            catch (AggregateException aex)
            {
                aex.Flatten().Handle(ex =>
                {
                    Console.WriteLine(ex.Message);
                    return true;
                });
            }
            Console.ReadLine();
        }

    }
}
