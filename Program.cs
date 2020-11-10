using System;
using System.IO;

namespace watch_dogs_loc
{
    class MainClass
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Watch_Dogs 1, 2, and Legion .loc tool - celikeins - 2019.06.07");
            if (args.Length != 1)
            {
                Help();  
            }
            using (Stream input = File.OpenRead(args[0]))
            {
                Loc loc = new Loc();
                loc.Read(input);
                loc.Export(input, args[0]);
                Console.WriteLine("Done!");
            }
        }

        private static void Help()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  {0} <loc_file>", GetExecutableName());
            Console.WriteLine("  Export <loc_file> to <loc_file>.txt.");
            Environment.Exit(42);
        }

        private static string GetExecutableName()
        {
            return Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        }
    }
}
