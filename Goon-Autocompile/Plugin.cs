using System.Text;
using Anvil.API;
using Anvil.Services;
using NLog;
using NWN.Native.API;
using ResRefType = Anvil.API.ResRefType;
using SHA1 = System.Security.Cryptography.SHA1;

namespace Goon.Autocompile;

[ServiceBinding(typeof(Plugin))]
public class Plugin
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    // public static string GetScriptPath(string scriptName)
    // {
    //     string compiledPrefix = "/nwn/run/currentgame.";
    //
    //     // find all directories that start with compiledPrefix, and split off the number that they end with. Then
    //     // find the directory with the highest number
    //     var compiled = Directory.GetDirectories("/nwn/run").Where(d => d.StartsWith(compiledPrefix))
    //         .Select(d => int.Parse(d.Substring(compiledPrefix.Length))).Max().ToString();
    //     compiled = compiledPrefix + compiled + "/";
    //     return compiled;
    // }
    
    public Plugin(ResourceManager resMan)
    {
        const string cache = "/nwn/run/_nss-cache/";
        const string devDirectory = "/nwn/home/development/";
        
        // string compiled = GetScriptPath(scriptName);
        string compiled = devDirectory;
        if (!Directory.Exists(compiled)) Directory.CreateDirectory(compiled);

        if (!Directory.Exists(cache)) Directory.CreateDirectory(cache);
        var compiler = NWNXLib.VirtualMachine().m_pJitCompiler;

        var copyTasks = new List<Task>();

        foreach (var s in resMan.FindResourcesOfType(ResRefType.NSS))
        {
            var scriptName = s.ToExoString();
            if (resMan.GetNSSContents(scriptName) is not string contents) continue;
            
            using var sha1 = SHA1.Create();
            var hash = Convert.ToHexString(sha1.ComputeHash(Encoding.UTF8.GetBytes(s + contents)));

            var cachedPath = Path.Combine(cache, $"{hash}");
            var compiledPath = Path.Combine(compiled, $"{s}.ncs");

            if (File.Exists(cachedPath))
            {
                var cp = cachedPath;
                var cmp = compiledPath;
                copyTasks.Add(Task.Run(() =>
                {
                    if (File.Exists(cmp)) File.Delete(cmp);
                    File.Copy(cp, cmp);
                }));
            }
            else
            {
                Log.Info($"Compiling {s} {cachedPath} {compiledPath}");
                compiler.SetCompileSymbolicOutput(0);
                compiler.SetGenerateDebuggerOutput(0);
                compiler.SetCompileConditionalFile(0);
                compiler.CompileFile(scriptName);

                var error = compiler.m_sCapturedError;
                if (error != null && (error.ToString() ?? "").Contains("ERROR: NO FUNCTION MAIN() IN SCRIPT"))
                {
                    // compile as conditional?
                    compiler.SetCompileSymbolicOutput(0);
                    compiler.SetGenerateDebuggerOutput(0);
                    compiler.SetCompileConditionalFile(1);
                    compiler.CompileFile(scriptName);
                }

                if (File.Exists(compiledPath))
                {
                    var cp = cachedPath;
                    var cmp = compiledPath;
                    copyTasks.Add(Task.Run(() => { File.Copy(cmp, cp); }));
                }
                else // this can occur if the file in an include, or has errors. Regardless, the result is the same, an unsuccessful compile.
                {
                    // make blank file
                    var cp = cachedPath;
                    copyTasks.Add(Task.Run(() => { File.WriteAllText(cp, ""); }));
                }
            }
        }
        
        var waiter = Task.WhenAll(copyTasks);
        waiter.Wait();
        
        compiler.SetCompileSymbolicOutput(0);
        compiler.SetGenerateDebuggerOutput(0);
        compiler.SetCompileConditionalFile(0);

        Log.Info("Done compiling scripts");
    }
}