using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace MemSet
{
    public unsafe class Program
    {
        private delegate void MemorySetter(byte * array, byte value, uint count);
        private static readonly MemorySetter MemsetDelegate;

        private static uint COPYLEN = 1000;
        private static int ITERATION = 1000000;

        static Program()
        {
            var m1 = new DynamicMethod(
                "memset",
                MethodAttributes.Public | MethodAttributes.Static,
                CallingConventions.Standard,
                typeof(void),
                new[] { typeof(byte*), typeof(byte), typeof(uint) },
                typeof(Program),
                false);
            var il1 = m1.GetILGenerator();
            il1.Emit(OpCodes.Ldarg_0); // dst address
            il1.Emit(OpCodes.Ldarg_1); // value
            il1.Emit(OpCodes.Ldarg_2); // number of bytes
            il1.Emit(OpCodes.Initblk);
            il1.Emit(OpCodes.Ret);

            MemsetDelegate = (MemorySetter)m1.CreateDelegate(typeof(MemorySetter));
        }

        public static void TestMemSet(int lowerlimit, int upperlimit)
        {
            // generate ramdom length
            Random rnd = new Random(1);
            ushort[] copyLen = new ushort[COPYLEN];
            for (int i = 0; i < COPYLEN; i++)
            {
                copyLen[i] = (ushort)rnd.Next(lowerlimit, upperlimit + 1);
            }

            // warm up
            var array = new byte[upperlimit];
            for (int i = 0; i < upperlimit; i++)
            {
                array[i] = (byte)i;
            }

            // measure
            long begin = DateTime.Now.Ticks;
            fixed (byte* pArray = array)
            {
                for (uint i = 0; i < ITERATION; i++)
                {
                    for (int j = 0; j < COPYLEN; j++)
                    {
                        MemsetDelegate(pArray, 1, copyLen[j]);
                    }
                }
            }
            long time = DateTime.Now.Ticks - begin;
            Console.WriteLine("memset [" + lowerlimit.ToString() + "," + upperlimit.ToString() + "] ticks: " + time.ToString());
        }

        public static void Main()
        {
            TestMemSet(0, 8);
            TestMemSet(9, 16);
            TestMemSet(17, 32);
            TestMemSet(33, 64);
            TestMemSet(65, 128);
            TestMemSet(129, 256);
            TestMemSet(257, 512);
            TestMemSet(513, 1024);
            Console.WriteLine("memset done!");
        }
    }
}
