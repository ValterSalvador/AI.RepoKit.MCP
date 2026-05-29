using AiRepoKit.Cli.Models.CodeIndex;
using AiRepoKit.Cli.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiRepoKit.Cli.Services.CodeIndex;

public sealed class RoslynCodeIndexService
{
    public CodeIndexResult Index(string repoRoot_, int maxFiles_, int maxItems_, bool includePrivateMembers_, bool useCache_, bool rebuildCache_, bool writeCache_, ProgressReporter? progress_ = null)
    {
        string repoRoot = Path.GetFullPath(repoRoot_);
        CodeFileDiscoveryService discoveryService = new();
        progress_?.StartPhase("Discovering C# files");
        CodeFileDiscoveryResult discovery = discoveryService.Discover(repoRoot, maxFiles_);
        progress_?.CompletePhase("C# file discovery completed");
        List<CodeSymbol> symbols = [];
        List<CodeEndpoint> endpoints = [];
        List<CodeIndexCacheEntry> cacheEntries = [];
        CodeIndexCacheService cacheService = new();
        progress_?.StartPhase("Loading cache");
        CodeIndexCacheLoadResult loadResult = cacheService.Load(repoRoot, useCache_, rebuildCache_, includePrivateMembers_);
        progress_?.CompletePhase("Cache loading completed");
        HashSet<string> discoveredFiles = new(discovery.Files, StringComparer.OrdinalIgnoreCase);
        int filesRemovedFromCache = loadResult.Cache?.Files.Count(file_ => !discoveredFiles.Contains(file_.File)) ?? 0;
        int filesIndexed = 0;
        int filesReused = 0;
        bool truncated = discovery.Truncated;

        progress_?.StartPhase("Indexing changed files");
        foreach (string relativePath in discovery.Files)
        {
            CodeIndexFileState fileState = cacheService.GetFileState(repoRoot, relativePath);
            CodeIndexCacheEntry? entry = cacheService.GetReusableEntry(loadResult.Cache, relativePath, fileState.Sha256, fileState.SizeBytes, fileState.LastWriteTimeUtc);
            if (entry is null)
            {
                entry = this.IndexFile(repoRoot, fileState, includePrivateMembers_);
                filesIndexed++;
            }
            else
            {
                filesReused++;
            }

            cacheEntries.Add(entry);
            this.AddSymbols(symbols, entry.Symbols, maxItems_, ref truncated);
            this.AddEndpoints(endpoints, entry.Endpoints, maxItems_);
        }
        progress_?.CompletePhase("Changed file indexing completed");

        if (writeCache_ && useCache_)
        {
            progress_?.StartPhase("Writing cache");
            CodeIndexCache cache = new(
                DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                "RoslynLite",
                TemplateService.GetToolVersion(),
                repoRoot,
                includePrivateMembers_,
                cacheEntries.OrderBy(entry_ => entry_.File, StringComparer.OrdinalIgnoreCase).ToArray());
            cacheService.Save(repoRoot, cache, true);
            progress_?.CompletePhase("Cache writing completed");
        }

        IReadOnlyDictionary<string, int> classificationCounts = symbols
            .GroupBy(symbol_ => symbol_.Classification, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group_ => group_.Count())
            .ThenBy(group_ => group_.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group_ => group_.Key, group_ => group_.Count(), StringComparer.OrdinalIgnoreCase);

        string generatedAtLocal = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
        CodeInventorySummary symbolInventory = new(
            generatedAtLocal,
            repoRoot,
            "RoslynLite",
            discovery.Files.Count,
            useCache_,
            filesIndexed,
            filesReused,
            filesRemovedFromCache,
            symbols.Count,
            truncated,
            discovery.IgnoredDirectories,
            discovery.IgnoredFiles,
            classificationCounts,
            symbols);
        EndpointInventorySummary endpointInventory = new(
            generatedAtLocal,
            repoRoot,
            "RoslynLite",
            useCache_,
            filesIndexed,
            filesReused,
            filesRemovedFromCache,
            endpoints.Count,
            endpoints.Take(maxItems_).ToArray());
        return new CodeIndexResult(
            repoRoot,
            discovery.Files,
            discovery.Files.Count,
            filesIndexed,
            filesReused,
            filesRemovedFromCache,
            useCache_,
            cacheService.GetCachePath(repoRoot),
            loadResult.Warnings,
            symbolInventory,
            endpointInventory);
    }

    private CodeIndexCacheEntry IndexFile(string repoRoot_, CodeIndexFileState fileState_, bool includePrivateMembers_)
    {
        string fullPath = Path.Combine(repoRoot_, fileState_.File.Replace('/', Path.DirectorySeparatorChar));
        string text = File.ReadAllText(fullPath);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(text);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
        EndpointDiscoveryService endpointDiscoveryService = new();
        IReadOnlyList<CodeEndpoint> endpoints = endpointDiscoveryService.Discover(root, fileState_.File, int.MaxValue);
        List<CodeSymbol> symbols = [];

        foreach (BaseTypeDeclarationSyntax type in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            CodeSymbol? symbol = this.CreateSymbol(type, fileState_.File, tree, includePrivateMembers_);
            if (symbol is not null)
            {
                symbols.Add(symbol);
            }
        }

        return new CodeIndexCacheEntry(fileState_.File, fileState_.Sha256, fileState_.SizeBytes, fileState_.LastWriteTimeUtc, symbols, endpoints);
    }

    private void AddSymbols(List<CodeSymbol> target_, IReadOnlyList<CodeSymbol> source_, int maxItems_, ref bool truncated_)
    {
        int remaining = maxItems_ - target_.Count;
        if (remaining <= 0)
        {
            if (source_.Count > 0)
            {
                truncated_ = true;
            }

            return;
        }

        target_.AddRange(source_.Take(remaining));
        if (source_.Count > remaining)
        {
            truncated_ = true;
        }
    }

    private void AddEndpoints(List<CodeEndpoint> target_, IReadOnlyList<CodeEndpoint> source_, int maxItems_)
    {
        int remaining = maxItems_ - target_.Count;
        if (remaining > 0)
        {
            target_.AddRange(source_.Take(remaining));
        }
    }

    private CodeSymbol? CreateSymbol(BaseTypeDeclarationSyntax type_, string relativePath_, SyntaxTree tree_, bool includePrivateMembers_)
    {
        string name = GetTypeName(type_);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        IReadOnlyList<CodeMember> methods = this.GetMethods(type_, tree_, includePrivateMembers_);
        IReadOnlyList<CodeMember> properties = this.GetProperties(type_, tree_, includePrivateMembers_);
        IReadOnlyList<string> attributes = GetAttributes(type_.AttributeLists);
        IReadOnlyList<string> baseTypes = GetBaseTypes(type_);
        string kind = GetKind(type_);
        string classification = this.Classify(name, kind, attributes, baseTypes, methods, properties);
        FileLinePositionSpan span = tree_.GetLineSpan(type_.Identifier.GetLocation().SourceSpan);

        return new CodeSymbol(
            name,
            kind,
            GetNamespace(type_),
            relativePath_,
            span.StartLinePosition.Line + 1,
            GetVisibility(type_.Modifiers),
            GetParent(type_),
            baseTypes,
            attributes,
            methods,
            properties,
            classification,
            type_.Modifiers.Any(SyntaxKind.PartialKeyword),
            type_.Modifiers.Any(SyntaxKind.StaticKeyword),
            type_.Modifiers.Any(SyntaxKind.AbstractKeyword),
            type_.Modifiers.Any(SyntaxKind.SealedKeyword),
            GetGenericArity(type_));
    }

    private IReadOnlyList<CodeMember> GetMethods(BaseTypeDeclarationSyntax type_, SyntaxTree tree_, bool includePrivateMembers_)
    {
        if (type_ is not TypeDeclarationSyntax typeDeclaration)
        {
            return [];
        }

        List<CodeMember> members = [];

        if (type_ is RecordDeclarationSyntax recordDeclaration && recordDeclaration.ParameterList is not null)
        {
            FileLinePositionSpan span = tree_.GetLineSpan(recordDeclaration.Identifier.GetLocation().SourceSpan);
            members.Add(new CodeMember(recordDeclaration.Identifier.ValueText, "PrimaryConstructor", string.Empty, GetVisibility(recordDeclaration.Modifiers), span.StartLinePosition.Line + 1, recordDeclaration.ParameterList.Parameters.Count));
        }

        foreach (ConstructorDeclarationSyntax constructor in typeDeclaration.Members.OfType<ConstructorDeclarationSyntax>())
        {
            string visibility = GetVisibility(constructor.Modifiers);
            if (!includePrivateMembers_ && visibility != "public")
            {
                continue;
            }

            FileLinePositionSpan span = tree_.GetLineSpan(constructor.Identifier.GetLocation().SourceSpan);
            members.Add(new CodeMember(constructor.Identifier.ValueText, "Constructor", string.Empty, visibility, span.StartLinePosition.Line + 1, constructor.ParameterList.Parameters.Count));
        }

        foreach (MethodDeclarationSyntax method in typeDeclaration.Members.OfType<MethodDeclarationSyntax>())
        {
            string visibility = GetVisibility(method.Modifiers);
            if (!includePrivateMembers_ && visibility != "public")
            {
                continue;
            }

            FileLinePositionSpan span = tree_.GetLineSpan(method.Identifier.GetLocation().SourceSpan);
            members.Add(new CodeMember(method.Identifier.ValueText, "Method", method.ReturnType.ToString(), visibility, span.StartLinePosition.Line + 1, method.ParameterList.Parameters.Count));
        }

        return members;
    }

    private IReadOnlyList<CodeMember> GetProperties(BaseTypeDeclarationSyntax type_, SyntaxTree tree_, bool includePrivateMembers_)
    {
        if (type_ is not TypeDeclarationSyntax typeDeclaration)
        {
            return [];
        }

        List<CodeMember> members = [];
        foreach (PropertyDeclarationSyntax property in typeDeclaration.Members.OfType<PropertyDeclarationSyntax>())
        {
            string visibility = GetVisibility(property.Modifiers);
            if (!includePrivateMembers_ && visibility != "public")
            {
                continue;
            }

            FileLinePositionSpan span = tree_.GetLineSpan(property.Identifier.GetLocation().SourceSpan);
            members.Add(new CodeMember(property.Identifier.ValueText, "Property", property.Type.ToString(), visibility, span.StartLinePosition.Line + 1, 0));
        }

        return members;
    }

    private string Classify(string name_, string kind_, IReadOnlyList<string> attributes_, IReadOnlyList<string> baseTypes_, IReadOnlyList<CodeMember> methods_, IReadOnlyList<CodeMember> properties_)
    {
        if (kind_ == "interface")
        {
            return "Interface";
        }

        if (kind_ == "enum")
        {
            return "Enum";
        }

        if (kind_ == "record")
        {
            return "Record";
        }

        if (name_.EndsWith("Controller", StringComparison.OrdinalIgnoreCase)
            || attributes_.Any(attribute_ => attribute_.Contains("ApiController", StringComparison.OrdinalIgnoreCase) || attribute_.Contains("Route", StringComparison.OrdinalIgnoreCase)))
        {
            return "Controller";
        }

        if (name_.EndsWith("Handler", StringComparison.OrdinalIgnoreCase)
            || baseTypes_.Any(baseType_ => baseType_.Contains("IRequestHandler", StringComparison.OrdinalIgnoreCase) || baseType_.Contains("INotificationHandler", StringComparison.OrdinalIgnoreCase)))
        {
            return "Handler";
        }

        if (baseTypes_.Any(baseType_ => baseType_.Equals("DbContext", StringComparison.OrdinalIgnoreCase) || baseType_.EndsWith(".DbContext", StringComparison.OrdinalIgnoreCase)))
        {
            return "DbContext";
        }

        if (name_.EndsWith("Dto", StringComparison.OrdinalIgnoreCase)
            || name_.EndsWith("Request", StringComparison.OrdinalIgnoreCase)
            || name_.EndsWith("Response", StringComparison.OrdinalIgnoreCase)
            || name_.EndsWith("ViewModel", StringComparison.OrdinalIgnoreCase))
        {
            return "Dto";
        }

        if (name_.EndsWith("Service", StringComparison.OrdinalIgnoreCase))
        {
            return "Service";
        }

        if (name_.EndsWith("Repository", StringComparison.OrdinalIgnoreCase))
        {
            return "Repository";
        }

        if (name_.EndsWith("Middleware", StringComparison.OrdinalIgnoreCase)
            || methods_.Any(method_ => method_.Name.Equals("Invoke", StringComparison.OrdinalIgnoreCase) || method_.Name.Equals("InvokeAsync", StringComparison.OrdinalIgnoreCase)))
        {
            return "Middleware";
        }

        if (name_.EndsWith("Options", StringComparison.OrdinalIgnoreCase)
            || name_.EndsWith("Settings", StringComparison.OrdinalIgnoreCase)
            || name_.EndsWith("Configuration", StringComparison.OrdinalIgnoreCase))
        {
            return "Configuration";
        }

        if (properties_.Any(property_ => property_.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)))
        {
            return "Entity";
        }

        return "Unknown";
    }

    private static string GetKind(BaseTypeDeclarationSyntax type_)
    {
        return type_ switch
        {
            ClassDeclarationSyntax => "class",
            RecordDeclarationSyntax => "record",
            InterfaceDeclarationSyntax => "interface",
            EnumDeclarationSyntax => "enum",
            StructDeclarationSyntax => "struct",
            _ => "type"
        };
    }

    private static string GetTypeName(BaseTypeDeclarationSyntax type_)
    {
        return type_ switch
        {
            ClassDeclarationSyntax value => value.Identifier.ValueText,
            RecordDeclarationSyntax value => value.Identifier.ValueText,
            InterfaceDeclarationSyntax value => value.Identifier.ValueText,
            EnumDeclarationSyntax value => value.Identifier.ValueText,
            StructDeclarationSyntax value => value.Identifier.ValueText,
            _ => string.Empty
        };
    }

    private static int GetGenericArity(BaseTypeDeclarationSyntax type_)
    {
        return type_ switch
        {
            TypeDeclarationSyntax value => value.TypeParameterList?.Parameters.Count ?? 0,
            _ => 0
        };
    }

    private static IReadOnlyList<string> GetBaseTypes(BaseTypeDeclarationSyntax type_)
    {
        return type_.BaseList?.Types.Select(baseType_ => baseType_.Type.ToString()).ToArray() ?? [];
    }

    private static IReadOnlyList<string> GetAttributes(SyntaxList<AttributeListSyntax> attributeLists_)
    {
        return attributeLists_
            .SelectMany(list_ => list_.Attributes)
            .Select(attribute_ => attribute_.Name.ToString())
            .ToArray();
    }

    private static string GetNamespace(SyntaxNode node_)
    {
        BaseNamespaceDeclarationSyntax? namespaceDeclaration = node_.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        return namespaceDeclaration?.Name.ToString() ?? string.Empty;
    }

    private static string GetParent(SyntaxNode node_)
    {
        BaseTypeDeclarationSyntax? parent = node_.Parent?.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault(type_ => !ReferenceEquals(type_, node_));
        return parent is null ? string.Empty : GetTypeName(parent);
    }

    private static string GetVisibility(SyntaxTokenList modifiers_)
    {
        if (modifiers_.Any(SyntaxKind.PublicKeyword))
        {
            return "public";
        }

        if (modifiers_.Any(SyntaxKind.ProtectedKeyword) && modifiers_.Any(SyntaxKind.InternalKeyword))
        {
            return "protected internal";
        }

        if (modifiers_.Any(SyntaxKind.ProtectedKeyword))
        {
            return "protected";
        }

        if (modifiers_.Any(SyntaxKind.InternalKeyword))
        {
            return "internal";
        }

        if (modifiers_.Any(SyntaxKind.PrivateKeyword))
        {
            return "private";
        }

        return "internal";
    }
}
