using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;

namespace BnlPlugins.Patcher
{
    /// <summary>
    /// Preloader patcher — patches Assembly-CSharp.dll IN MEMORY before Unity loads it.
    /// Binary patches are applied to raw bytes in Initialize(), then the patched
    /// assembly is swapped in via Patch(ref AssemblyDefinition). No disk write needed.
    /// </summary>
    public static class GamePatcher
    {
        private static byte[] _patchedBytes;

        public static IEnumerable<string> TargetDLLs
        {
            get { yield return "Assembly-CSharp.dll"; }
        }

        public static void Initialize()
        {
            string patcherDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
            string win64Dir = Path.GetFullPath(Path.Combine(Path.Combine(patcherDir, ".."), ".."));
            string asmPath = Path.Combine(Path.Combine(win64Dir, "BlockNLoad_Data"), Path.Combine("Managed", "Assembly-CSharp.dll"));

            Console.WriteLine("[BNL Patcher] Reading: " + asmPath);

            if (!File.Exists(asmPath))
            {
                Console.WriteLine("[BNL Patcher] ERROR: Assembly-CSharp.dll not found!");
                return;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(asmPath);
                bool changed = false;

                if (bytes[0x61F49] == 0x28 && bytes[0x61F4A] == 0xE8)
                {
                    bytes[0x61F49] = 0x00; bytes[0x61F4A] = 0x00;
                    bytes[0x61F4B] = 0x00; bytes[0x61F4C] = 0x00; bytes[0x61F4D] = 0x17;
                    changed = true;
                    Console.WriteLine("[BNL Patcher] EAC init NOP'd");
                }
                if (bytes[0x15EB80] == 0x39)
                {
                    bytes[0x15EB80] = 0x3A;
                    changed = true;
                    Console.WriteLine("[BNL Patcher] servers.txt enabled");
                }
                if (bytes[0x15B585] == 0x1C)
                {
                    bytes[0x15B585] = 0x16;
                    changed = true;
                    Console.WriteLine("[BNL Patcher] new player skip");
                }

                if (changed)
                {
                    _patchedBytes = bytes;
                    Console.WriteLine("[BNL Patcher] " + bytes.Length + " bytes patched in memory.");
                }
                else
                {
                    Console.WriteLine("[BNL Patcher] Already patched — no changes needed.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[BNL Patcher] ERROR: " + ex.Message);
            }
        }

        /// <summary>
        /// Swap the assembly with our patched version before BepInEx loads it.
        /// </summary>
        public static void Patch(ref AssemblyDefinition assembly)
        {
            if (_patchedBytes == null)
                return;

            try
            {
                var ms = new MemoryStream(_patchedBytes);
                var readerParams = new ReaderParameters
                {
                    ReadingMode = ReadingMode.Immediate,
                    ReadSymbols = false
                };
                assembly = AssemblyDefinition.ReadAssembly(ms, readerParams);
                // Don't dispose ms — BepInEx needs the stream later for writing
                Console.WriteLine("[BNL Patcher] Assembly swapped with patched version.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[BNL Patcher] ERROR swapping assembly: " + ex.Message);
            }
        }

        public static void Finish()
        {
            _patchedBytes = null;
            Console.WriteLine("[BNL Patcher] Done.");
        }
    }
}
