// -----------------------------------------------------------------------------
// Headless script-compilation check for CI.
//
// Invoked from .github/workflows/ci.yml via game-ci/unity-builder:
//   buildMethod: CompileCheck.Run
//
// Unity compiles every assembly (runtime + editor) when the project opens in
// batchmode. If any script has compile errors, Unity logs "Scripts have
// compiler errors" and aborts BEFORE this method is ever called, exiting with
// a non-zero code that fails the CI job. Reaching Run() therefore proves the
// whole project compiled; we log the result and exit 0 explicitly (manualExit).
// -----------------------------------------------------------------------------
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

public static class CompileCheck
{
    public static void Run()
    {
        var editorAssemblies = CompilationPipeline
            .GetAssemblies(AssembliesType.Editor)
            .OrderBy(a => a.name)
            .ToArray();

        // Reaching here means every assembly compiled successfully - Unity would
        // have aborted batchmode otherwise. Report for the CI log and exit 0.
        Debug.Log($"[CompileCheck] SUCCESS: {editorAssemblies.Length} assemblies compiled without errors.");
        foreach (var assembly in editorAssemblies)
        {
            Debug.Log($"[CompileCheck]   - {assembly.name} ({assembly.sourceFiles.Length} sources)");
        }

        EditorApplication.Exit(0);
    }
}
