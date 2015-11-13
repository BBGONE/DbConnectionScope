using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApplication1
{
    class Program
    {


        private static string connectionString = "Data Source=.;Initial Catalog=AdventureWorksLT2012;Integrated Security=SSPI;MultipleActiveResultSets=True";

        static void Main(string[] args)
        {
            try
            {
                //Just to Complicate Testing 
                TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
                int cnt = 3;
                for (int i = 0; i < cnt; ++i)
                {
                    ThreadPool.QueueUserWorkItem((state) =>
                    {
                        var task = DbConnectionScopeTest.Start(connectionString);
                        task.Wait(60000);
                        var res = Interlocked.Decrement(ref cnt);
                        if (res == 0)
                            tcs.SetResult(null);
                    });
                }
                tcs.Task.Wait(60000);
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
