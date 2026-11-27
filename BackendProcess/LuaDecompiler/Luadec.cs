// Lua 5.1 decompiler for Nautilus.
//
// C# port of luadec-rust (https://github.com/ItsLucas/luadecng-rs), which is built on metaworm's
// luac-parser-rs (https://github.com/metaworm/luac-parser-rs). Both are MIT licensed; see NOTICE.txt.
//
// Differences from the original Rust code (all intentional):
//  - CALL/TAILCALL/RETURN/SETLIST with a "variable" count (B == 0) now include the values up to the stack
//    top. The original dropped them, turning e.g. print(string.format("%d", n)) into print().
//  - `not not x` is kept as-is; the original simplified it to `not x`, which flips its meaning.
//  - Numeric for-loop variables are registered, so loop bodies use the variable name instead of `r3`.
//  - `for i = a, b, 1` omits a step of 1 for float constants too (Lua 5.1 stores numbers as floats).
//  - If one nested function fails to decompile, it becomes a stub with a comment instead of failing the file.
//  - Malformed jumps after test instructions are skipped instead of crashing.
//  - A parameter without a debug-info entry is named a0, a1, ... instead of recursing forever.
using System;
using System.Threading;
using System.Threading.Tasks;
using LuaDecompiler.Lua51;
using LuaDecompiler.Parser;

namespace LuaDecompiler
{
    public static class Luadec
    {
        /// <summary>Decompile Lua 5.1 bytecode (little- or big-endian) into Lua source code.</summary>
        /// <exception cref="LuacParseException">The bytes are not valid Lua 5.1 bytecode.</exception>
        public static string Decompile(byte[] bytecode)
        {
            if (bytecode == null) throw new ArgumentNullException(nameof(bytecode));
            var bc = LuacParser.Parse(bytecode);
            var func = Lifter.Decompile(bc.MainChunk);
            return Emitter.EmitChunk(func);
        }

        /// <summary>
        /// Decompile on a dedicated thread with a large stack (deeply nested scripts can exceed the default
        /// 1 MB stack). Throws <see cref="TimeoutException"/> if it takes longer than <paramref name="timeout"/>.
        /// </summary>
        public static Task<string> DecompileAsync(byte[] bytecode, TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    tcs.TrySetResult(Decompile(bytecode));
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, 64 * 1024 * 1024)
            {
                IsBackground = true,
                Name = "Lua decompiler",
            };
            thread.Start();

            return WithTimeout(tcs.Task, timeout);
        }

        private static async Task<string> WithTimeout(Task<string> work, TimeSpan timeout)
        {
            var done = await Task.WhenAny(work, Task.Delay(timeout)).ConfigureAwait(false);
            if (done != work)
                throw new TimeoutException($"Decompiling took longer than {timeout.TotalSeconds:0} seconds.");
            return await work.ConfigureAwait(false);
        }
    }
}
