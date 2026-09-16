using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SplitAndMerge
{
    /// <summary>
    /// Roslyn-based replacement for the CodeDom CSharpCodeProvider backend.
    ///
    /// CodeDom's CompileAssemblyFromSource throws PlatformNotSupportedException on
    /// .NET Core / .NET 5+, which silently disabled the whole cfunction/dllfunction
    /// feature once the project moved off .NET Framework.
    /// </summary>
    public static class RoslynCompiler
    {
        static List<MetadataReference> s_references;
        static string s_referencesFingerprint;
        static readonly object s_lock = new object();

        /// <summary>
        /// Where compiled assemblies are cached between runs. Set to null or empty,
        /// or set CacheEnabled to false, to compile every time.
        /// </summary>
        public static string CacheDirectory { get; set; } =
            Path.Combine(Path.GetTempPath(), "cscs-precompiled");

        public static bool CacheEnabled { get; set; } = true;

        /// <summary>Compiled assemblies loaded during this process, keyed by source hash.</summary>
        static readonly Dictionary<string, Assembly> s_memoryCache = new Dictionary<string, Assembly>();

        public static int CacheHits { get; private set; }
        public static int CacheMisses { get; private set; }

        public static IReadOnlyList<MetadataReference> GetReferences()
        {
            lock (s_lock)
            {
                if (s_references != null)
                {
                    return s_references;
                }
                var refs = new List<MetadataReference>();
                var paths = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string location;
                    try
                    {
                        if (asm.IsDynamic) continue;
                        location = asm.Location;
                    }
                    catch { continue; }

                    if (string.IsNullOrWhiteSpace(location) || !File.Exists(location) ||
                        !seen.Add(location))
                    {
                        continue;
                    }
                    try
                    {
                        refs.Add(MetadataReference.CreateFromFile(location));
                        paths.Add(location);
                    }
                    catch { }
                }
                paths.Sort(StringComparer.OrdinalIgnoreCase);
                var fingerprint = new StringBuilder();
                foreach (var path in paths)
                {
                    fingerprint.Append(path).Append('|');
                    try { fingerprint.Append(File.GetLastWriteTimeUtc(path).Ticks); }
                    catch { }
                    fingerprint.Append('\n');
                }
                s_referencesFingerprint = Hash(fingerprint.ToString());
                s_references = refs;
                return s_references;
            }
        }

        /// <summary>
        /// Forgets the cached reference set. Call after loading assemblies that compiled
        /// scripts need to see (a module DLL, for instance).
        /// </summary>
        public static void ResetReferences()
        {
            lock (s_lock)
            {
                s_references = null;
                s_referencesFingerprint = null;
            }
        }

        /// <summary>Compiles C# source and returns the loaded assembly. Throws on errors.</summary>
        public static Assembly Compile(string source, string assemblyNamePrefix, string outputDLL = "")
        {
            // The one place compiled code is loaded. Explain mode promises it never is.
            if (PrecompileExplainer.Enabled)
            {
                throw new InvalidOperationException("Loading compiled code is disabled in explain mode.");
            }
            GetReferences();
            var key = Hash(source + "\n@refs:" + s_referencesFingerprint);
            var assemblyName = assemblyNamePrefix + "_" + key;

            lock (s_lock)
            {
                if (CacheEnabled && s_memoryCache.TryGetValue(key, out var cached) &&
                    string.IsNullOrWhiteSpace(outputDLL))
                {
                    CacheHits++;
                    return cached;
                }
            }

            var cachePath = GetCachePath(key);
            if (CacheEnabled && cachePath != null && string.IsNullOrWhiteSpace(outputDLL) &&
                File.Exists(cachePath))
            {
                try
                {
                    var cachedAssembly = Assembly.Load(File.ReadAllBytes(cachePath));
                    lock (s_lock)
                    {
                        s_memoryCache[key] = cachedAssembly;
                        CacheHits++;
                    }
                    return cachedAssembly;
                }
                catch
                {
                    // A corrupt or unreadable cache entry must never be fatal: fall through
                    // and compile from source.
                    try { File.Delete(cachePath); } catch { }
                }
            }

            var bytes = Emit(source, assemblyName);
            lock (s_lock) { CacheMisses++; }

            if (!string.IsNullOrWhiteSpace(outputDLL))
            {
                File.WriteAllBytes(outputDLL, bytes);
            }
            else if (CacheEnabled && cachePath != null)
            {
                WriteCacheEntry(cachePath, bytes);
            }

            var assembly = Assembly.Load(bytes);
            lock (s_lock) { s_memoryCache[key] = assembly; }
            return assembly;
        }

        /// <summary>
        /// Compiles C# source in memory and returns the errors, empty when it compiles. Nothing is
        /// loaded or written anywhere -- which is what makes it safe for source nobody has vetted.
        /// </summary>
        public static List<string> CheckCompiles(string source, string assemblyNamePrefix)
        {
            TryEmit(source, assemblyNamePrefix + "_check", out var errors);
            return errors;
        }

        static byte[] Emit(string source, string assemblyName)
        {
            var bytes = TryEmit(source, assemblyName, out var errors);
            if (errors.Count > 0)
            {
                throw new ArgumentException("Compile error: " +
                    string.Join(" -- ", errors) + "\n--- Generated code ---\n" +
                    NumberLines(source));
            }
            return bytes;
        }

        static byte[] TryEmit(string source, string assemblyName, out List<string> errors)
        {
            // Diagnostic: VDDUMP=<substring> prints the generated C# for any function whose
            // source contains that text, which is how a fallback's real cause gets found -- the
            // compile error alone says where C# gave up, not what the translator emitted.
            // Console.Error, because the probe harness sends stdout to TextWriter.Null.
            var dump = Environment.GetEnvironmentVariable("VDDUMP");
            if (!string.IsNullOrEmpty(dump) && source.Contains(dump))
            {
                Console.Error.WriteLine("===== " + assemblyName + " =====");
                Console.Error.WriteLine(source);
            }
            var tree = CSharpSyntaxTree.ParseText(source,
                new CSharpParseOptions(LanguageVersion.Latest));

            var options = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true,
                assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default);

            var compilation = CSharpCompilation.Create(assemblyName,
                new[] { tree }, GetReferences(), options);

            using (var peStream = new MemoryStream())
            {
                var result = compilation.Emit(peStream);
                errors = result.Success ? new List<string>() : result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d =>
                    {
                        var span = d.Location.GetLineSpan();
                        return "(" + (span.StartLinePosition.Line + 1) + "," +
                               (span.StartLinePosition.Character + 1) + ") " +
                               d.Id + ": " + d.GetMessage();
                    })
                    .ToList();
                return result.Success ? peStream.ToArray() : null;
            }
        }

        static string GetCachePath(string key)
        {
            var dir = CacheDirectory;
            if (string.IsNullOrWhiteSpace(dir))
            {
                return null;
            }
            return Path.Combine(dir, key + ".dll");
        }

        static void WriteCacheEntry(string cachePath, byte[] bytes)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                // Write to a unique temp name and move into place, so that two processes
                // compiling the same script cannot leave a half-written .dll behind.
                var temp = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(cachePath))
                {
                    File.Delete(temp);
                }
                else
                {
                    File.Move(temp, cachePath);
                }
            }
            catch
            {
                // The cache is an optimisation; failing to populate it is not an error.
            }
        }

        /// <summary>Removes every cached assembly, in memory and on disk.</summary>
        public static void ClearCache()
        {
            lock (s_lock)
            {
                s_memoryCache.Clear();
                CacheHits = CacheMisses = 0;
            }
            var dir = CacheDirectory;
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                return;
            }
            foreach (var file in Directory.GetFiles(dir, "*.dll"))
            {
                try { File.Delete(file); } catch { }
            }
        }

        static string Hash(string text)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString(0, 32);
            }
        }

        static string NumberLines(string source)
        {
            var lines = source.Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                sb.Append((i + 1).ToString().PadLeft(4)).Append(": ").AppendLine(lines[i]);
            }
            return sb.ToString();
        }
    }
}
