using System;
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
               var task = DbConnectionScopeTest.Start(connectionString);
                task.Wait(60000);
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
