using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace MemCpy
{
    public unsafe class Program
    {
        private delegate void MemoryCopier(byte * arr1, byte * arr2, uint count);
        private static readonly MemoryCopier MemcpyDelegate;

        private static uint COPYLEN = 1000;
        private static int ITERATION = 1000000;

        static Program()
        {
            var m2 = new DynamicMethod(
                "memcpy",
                MethodAttributes.Public | MethodAttributes.Static,
                CallingConventions.Standard,
                typeof(void),
                new[] { typeof(byte*), typeof(byte*), typeof(uint) },
                typeof(Program),
                false);
            var il2 = m2.GetILGenerator();
            il2.Emit(OpCodes.Ldarg_0); // dst address
            il2.Emit(OpCodes.Ldarg_1); // src address
            il2.Emit(OpCodes.Ldarg_2); // number of bytes
            il2.Emit(OpCodes.Cpblk);
            il2.Emit(OpCodes.Ret);

            MemcpyDelegate = (MemoryCopier)m2.CreateDelegate(typeof(MemoryCopier));
        }

        public static void TestMemCpy(int lowerlimit, int upperlimit)
        {
            // generate ramdom length
            Random rnd = new Random(1);
            ushort[] copyLen = new ushort[COPYLEN];
            for (int i = 0; i < COPYLEN; i++)
            {
                copyLen[i] = (ushort)rnd.Next(lowerlimit, upperlimit + 1);
            }

            // warm up
            var array1 = new byte[upperlimit];
            var array2 = new byte[upperlimit];
            for (int i = 0; i < upperlimit; i++)
            {
                array1[i] = (byte)i;
                array2[i] = (byte)i;
            }

            // measure
            long begin = DateTime.Now.Ticks;
            fixed (byte* pArray1 = array1, pArray2 = array2)
            {
                for (uint i = 0; i < ITERATION; i++)
                {
                    for (int j = 0; j < COPYLEN; j++)
                    {
                        MemcpyDelegate(pArray1, pArray2, copyLen[j]);
                    }
                }
            }
            long time = DateTime.Now.Ticks - begin;
            Console.WriteLine("memcpy [" + lowerlimit.ToString() + "," + upperlimit.ToString() + "] ticks: " + time.ToString());
        }

        public static void Main()
        {
            TestMemCpy(0, 8);
            TestMemCpy(9, 16);
            TestMemCpy(17, 32);
            TestMemCpy(33, 64);
            TestMemCpy(65, 128);
            TestMemCpy(129, 256);
            TestMemCpy(257, 512);
            TestMemCpy(513, 1024);
            Console.WriteLine("memcpy done!");
        }
    }
}
