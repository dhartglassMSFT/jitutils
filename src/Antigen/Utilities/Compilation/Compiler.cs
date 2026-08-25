// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Antigen.Execution;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis;

namespace Antigen.Compilation
{
    public class Compiler
    {
        private static readonly CSharpCompilationOptions ReleaseCompileOptions = new(
            OutputKind.ConsoleApplication,
            concurrentBuild: true,
            optimizationLevel: OptimizationLevel.Release,
            specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic>
            {
                { "SYSLIB5003", ReportDiagnostic.Suppress }
            });

        private static readonly CSharpCompilationOptions DebugCompileOptions = new(
            OutputKind.ConsoleApplication,
            concurrentBuild: true,
            optimizationLevel: OptimizationLevel.Debug,
            specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic>
            {
                { "SYSLIB5003", ReportDiagnostic.Suppress }
            });

        // Directory whose assemblies generated tests are compiled against. This must be
        // CORE_ROOT, not Antigen's own directory: the tests execute under CORE_ROOT's corerun,
        // so compiling against Antigen's (older, fixed at its TargetFramework) framework means
        // APIs renamed since then compile fine but throw MissingMethodException at run time,
        // and APIs added since then can never be generated at all. Falls back to Antigen's own
        // framework if not set, which preserves the previous behavior.
        private static string s_referenceDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);

        private static readonly object s_referencesLock = new object();
        private static MetadataReference[] s_references = null;

        /// <summary>
        ///     Point compilation at CORE_ROOT. Must be called before the first Compile().
        /// </summary>
        public static void SetReferenceDirectory(string referenceDirectory)
        {
            lock (s_referencesLock)
            {
                s_referenceDirectory = referenceDirectory;
                s_references = null;
            }
        }

        private static MetadataReference[] GetReferences()
        {
            lock (s_referencesLock)
            {
                if (s_references == null)
                {
                    s_references = new MetadataReference[]
                    {
                        MetadataReference.CreateFromFile(Path.Combine(s_referenceDirectory, "System.Private.CoreLib.dll")),
                        MetadataReference.CreateFromFile(Path.Combine(s_referenceDirectory, "System.Console.dll")),
                        MetadataReference.CreateFromFile(Path.Combine(s_referenceDirectory, "System.Runtime.dll")),
                        MetadataReference.CreateFromFile(typeof(SyntaxTree).Assembly.Location),
                        MetadataReference.CreateFromFile(typeof(CSharpSyntaxTree).Assembly.Location),
                    };
                }
                return s_references;
            }
        }

        private readonly string m_outputDirectory;

        public Compiler(string outputDirectory)
        {
            m_outputDirectory = outputDirectory;
        }

        public CompileResult Compile(SyntaxTree programTree, string assemblyName)
        {
            byte[]? debugBytes = null, releaseBytes = null;
            debugBytes = CompileAndGetBytes(programTree, assemblyName, DebugCompileOptions);
            if (debugBytes != null)
            {
                releaseBytes = CompileAndGetBytes(programTree, assemblyName, ReleaseCompileOptions);
            }
            return new CompileResult(assemblyName, null, debugBytes, releaseBytes);
        }

        private byte[] CompileAndGetBytes(SyntaxTree programTree, string assemblyName, CSharpCompilationOptions options)
        {
            string tag = options.OptimizationLevel == OptimizationLevel.Debug ? "Debug" : "Release";
            var cc = CSharpCompilation.Create($"{assemblyName}-{tag}.exe", new SyntaxTree[] { programTree }, GetReferences(), options);

            using (var ms = new MemoryStream())
            {
                EmitResult result;
                try
                {
                    result = cc.Emit(ms);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    return null;
                }

                if (!result.Success)
                {
#if UNREACHABLE
                    SaveCompilationError(programTree, result.Diagnostics);
#endif
                    return null;
                }

                ms.Seek(0, SeekOrigin.Begin);

                return ms.ToArray();
            }
        }

        private void SaveCompilationError(SyntaxTree tree, IEnumerable<Diagnostic> diagnostics)
        {
            StringBuilder fileContents = new StringBuilder();

            fileContents.AppendLine(tree.GetRoot().NormalizeWhitespace().ToFullString());
            fileContents.AppendLine("/*");
            var errorLines = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(diag => $"{diag.Location.GetLineSpan().StartLinePosition.Line}: {diag.GetMessage()}");
            fileContents.AppendLine($"Got {errorLines.Count()} compiler error(s):");
            foreach (var error in errorLines)
            {
                fileContents.AppendLine(error);
            }
            var errorFile = Path.Combine(m_outputDirectory, $"{tree.FilePath}.error");
            File.WriteAllText(errorFile, fileContents.ToString());
        }
    }
}
