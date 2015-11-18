using Bell.PPS.Database.Shared;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;

namespace ConsoleApplication1
{
    class Program
    {
        private static string connectionString = "Data Source=.;Initial Catalog=AdventureWorksLT2012;Integrated Security=SSPI;MultipleActiveResultSets=True;";
        
        static void Main(string[] args)
        {
            try
            {
                //Just to Complicate Testing 
                TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
                int cnt = 1;
                Task.Run(async () =>
                {
                    var task = DbConnectionScopeTest.Start(connectionString);
                    await task;
                    var res = Interlocked.Decrement(ref cnt);
                    if (res == 0)
                        tcs.SetResult(null);
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
