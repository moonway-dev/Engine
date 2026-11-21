namespace Engine.Scripting;

using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Engine.Mathematics;
using Engine.Platform;
using Engine.Scene;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Engine.Core;

public sealed record ScriptTypeRegistration(
    string Key,
    string DisplayName,
    Type ComponentType,
    Type BehaviourType,
    Func<Component> CreateComponent);

public sealed class ScriptCompilationResult
{
    public bool Success { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];

    public ScriptLoadContext? LoadContext { get; init; }

    public IReadOnlyList<ScriptTypeRegistration> Scripts { get; init; } = [];
}

public sealed class ScriptLoadContext : AssemblyLoadContext
{
    public ScriptLoadContext()
        : base($"EngineScripts-{Guid.NewGuid():N}", isCollectible: true)
    {
    }
}

public sealed class ScriptCompiler
{
    private static readonly string[] ImplicitUsings =
    [
        "global using System;",
        "global using System.Collections.Generic;",
        "global using System.Linq;",
        "global using Engine.Mathematics;",
        "global using Engine.Platform;",
        "global using Engine.Scene;",
        "global using Engine.Scripting;",
    ];

    private static IReadOnlyList<MetadataReference>? _referenceCache;

    public ScriptCompilationResult Compile(string scriptsRoot) => Compile(scriptsRoot, assemblyOutput: null);

    public ScriptCompilationResult Compile(string scriptsRoot, Stream? assemblyOutput)
    {
        if (!Directory.Exists(scriptsRoot))
            return new ScriptCompilationResult
            {
                Success = true,
                Errors = [],
                Scripts = [],
            };
        List<string> errors = [];
        List<SyntaxTree> trees = [];
        List<(string ClassName, string ComponentName)> discovered = [];
        HashSet<string> componentNames = [];

        foreach (string file in EnumerateScriptFiles(scriptsRoot))
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException exception)
            {
                errors.Add($"{file}: {exception.Message}");
                continue;
            }
            SyntaxTree tree = CSharpSyntaxTree.ParseText(string.Join(Environment.NewLine, ImplicitUsings) + Environment.NewLine + text, path: file);
            trees.Add(tree);
            foreach (ClassDeclarationSyntax classDeclaration in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!IsScriptClass(classDeclaration, errors))
                    continue;
                string componentName = $"{classDeclaration.Identifier.Text}Component";
                if (!componentNames.Add(componentName))
                {
                    errors.Add($"{file}: duplicate script class name '{classDeclaration.Identifier.Text}'.");
                    continue;
                }
                discovered.Add((classDeclaration.Identifier.Text, componentName));
            }
        }

        foreach ((string className, string componentName) in discovered)
            trees.Add(GenerateComponentTree(className, componentName));

        CSharpCompilation compilation = CSharpCompilation.Create(
            $"EngineScripts-{Guid.NewGuid():N}",
            trees,
            BuildReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                concurrentBuild: false,
                assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default));

        using MemoryStream peStream = new();
        EmitResult emit = compilation.Emit(peStream);
        if (!emit.Success)
        {
            foreach (Diagnostic diagnostic in emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(64))
                errors.Add(diagnostic.ToString());
            return new ScriptCompilationResult { Success = false, Errors = errors };
        }

        peStream.Position = 0;
        if (assemblyOutput is not null)
        {
            peStream.CopyTo(assemblyOutput);
            assemblyOutput.Position = 0;
            peStream.Position = 0;
        }
        ScriptLoadContext loadContext = new();
        Assembly assembly;
        try
        {
            assembly = loadContext.LoadFromStream(peStream);
        }
        catch (Exception exception)
        {
            errors.Add($"Failed to load compiled script assembly: {exception.Message}");
            loadContext.Unload();
            return new ScriptCompilationResult { Success = false, Errors = errors };
        }

        List<ScriptTypeRegistration> registrations = [];
        foreach ((string className, string componentName) in discovered)
        {
            Type? componentType = assembly.GetType($"EngineGenerated.{componentName}");
            Type? behaviourType = assembly
                .GetExportedTypes()
                .FirstOrDefault(type => type.Name == className
                    && typeof(ScriptBehaviour).IsAssignableFrom(type)
                    && !type.IsAbstract);
            if (componentType is null || behaviourType is null)
            {
                errors.Add($"Script '{className}' could not be resolved in the compiled assembly.");
                continue;
            }
            registrations.Add(new ScriptTypeRegistration(
                className,
                componentName,
                componentType,
                behaviourType,
                () => (Component)Activator.CreateInstance(componentType)!));
        }

        return new ScriptCompilationResult
        {
            Success = true,
            Errors = errors,
            LoadContext = loadContext,
            Scripts = registrations,
        };
    }

    private static IEnumerable<string> EnumerateScriptFiles(string scriptsRoot) =>
        Directory
            .EnumerateFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

    private static bool IsScriptClass(ClassDeclarationSyntax classDeclaration, List<string> errors)
    {
        if (classDeclaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.AbstractKeyword) || modifier.IsKind(SyntaxKind.StaticKeyword)))
            return false;
        if (!classDeclaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword)))
            return false;
        if (classDeclaration.TypeParameterList is not null)
            return false;
        if (!DerivesFromScriptBehaviour(classDeclaration))
            return false;
        ConstructorDeclarationSyntax? constructor = classDeclaration.Members
            .OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault(member => !member.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword)));
        if (constructor is not null && constructor.ParameterList.Parameters.Count != 0)
        {
            errors.Add($"Script class '{classDeclaration.Identifier.Text}' must have a parameterless constructor.");
            return false;
        }
        return true;
    }

    private static bool DerivesFromScriptBehaviour(ClassDeclarationSyntax classDeclaration)
    {
        if (classDeclaration.BaseList is null)
            return false;
        foreach (BaseTypeSyntax baseType in classDeclaration.BaseList.Types)
        {
            string name = baseType.Type.ToString().Trim();
            if (name.Equals("ScriptBehaviour", StringComparison.Ordinal)
                || name.Equals("Engine.Scripting.ScriptBehaviour", StringComparison.Ordinal)
                || name.EndsWith(".ScriptBehaviour", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static SyntaxTree GenerateComponentTree(string className, string componentName) =>
        CSharpSyntaxTree.ParseText(
            $$"""
            namespace EngineGenerated
            {
                public sealed class {{componentName}} : Engine.Scene.ScriptComponent
                {
                    public {{componentName}}()
                    {
                        Script = "{{className}}";
                    }
                }
            }
            """);

    private static bool HasManagedMetadata(string path)
    {
        try
        {
            _ = AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                          or BadImageFormatException
                                          or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IReadOnlyList<MetadataReference> BuildReferences()
    {
        if (_referenceCache is not null)
            return _referenceCache;
        List<MetadataReference> references = [];
        foreach (Assembly assembly in new[]
        {
            typeof(ScriptComponent).Assembly,
            typeof(Engine.Physics.RigidBodyComponent).Assembly,
            typeof(BepuPhysics.BodyHandle).Assembly,
            typeof(ScriptBehaviour).Assembly,
            typeof(Vector3).Assembly,
            typeof(KeyCode).Assembly,
            typeof(IEngineContext).Assembly,
        })
        {
            if (!string.IsNullOrEmpty(assembly.Location))
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }
        string runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? string.Empty;
        if (runtimeDirectory.Length > 0)
        {
            foreach (string file in Directory.EnumerateFiles(runtimeDirectory, "*.dll"))
            {
                if (!HasManagedMetadata(file))
                    continue;
                try
                {
                    references.Add(MetadataReference.CreateFromFile(file));
                }
                catch (Exception exception) when (exception is InvalidOperationException or IOException or BadImageFormatException)
                {
                }
            }
        }
        IReadOnlyList<MetadataReference> cache = references.ToImmutableArray();
        _referenceCache = cache;
        return cache;
    }
}
