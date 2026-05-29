using AiRepoKit.Cli.Models.CodeIndex;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiRepoKit.Cli.Services.CodeIndex;

public sealed class EndpointDiscoveryService
{
    private static readonly Dictionary<string, string> MinimalApiMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MapGet"] = "GET",
        ["MapPost"] = "POST",
        ["MapPut"] = "PUT",
        ["MapDelete"] = "DELETE",
        ["MapPatch"] = "PATCH"
    };

    private static readonly Dictionary<string, string> ControllerMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HttpGet"] = "GET",
        ["HttpPost"] = "POST",
        ["HttpPut"] = "PUT",
        ["HttpDelete"] = "DELETE",
        ["HttpPatch"] = "PATCH"
    };

    public IReadOnlyList<CodeEndpoint> Discover(CompilationUnitSyntax root_, string relativePath_, int maxItems_)
    {
        List<CodeEndpoint> endpoints = [];
        endpoints.AddRange(this.DiscoverControllers(root_, relativePath_, maxItems_));
        if (endpoints.Count < maxItems_)
        {
            endpoints.AddRange(this.DiscoverMinimalApis(root_, relativePath_, maxItems_ - endpoints.Count));
        }

        return endpoints.Take(maxItems_).ToArray();
    }

    private IReadOnlyList<CodeEndpoint> DiscoverControllers(CompilationUnitSyntax root_, string relativePath_, int maxItems_)
    {
        List<CodeEndpoint> endpoints = [];
        foreach (ClassDeclarationSyntax controller in root_.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            string controllerName = controller.Identifier.ValueText;
            IReadOnlyList<string> classAttributes = GetAttributeNames(controller.AttributeLists);
            bool isController = controllerName.EndsWith("Controller", StringComparison.OrdinalIgnoreCase)
                || classAttributes.Any(attribute_ => attribute_.Contains("ApiController", StringComparison.OrdinalIgnoreCase) || attribute_.Contains("Route", StringComparison.OrdinalIgnoreCase));
            if (!isController)
            {
                continue;
            }

            string controllerRoute = GetRouteFromAttributes(controller.AttributeLists, "Route");
            foreach (MethodDeclarationSyntax action in controller.Members.OfType<MethodDeclarationSyntax>())
            {
                foreach (AttributeSyntax attribute in action.AttributeLists.SelectMany(list_ => list_.Attributes))
                {
                    string name = NormalizeAttributeName(attribute.Name.ToString());
                    if (!ControllerMethods.TryGetValue(name, out string? method))
                    {
                        continue;
                    }

                    string actionRoute = GetRouteFromAttribute(attribute);
                    string route = CombineRoutes(controllerRoute, actionRoute);
                    FileLinePositionSpan span = action.SyntaxTree.GetLineSpan(action.Identifier.GetLocation().SourceSpan);
                    endpoints.Add(new CodeEndpoint(method, route, relativePath_, span.StartLinePosition.Line + 1, controllerName + "." + action.Identifier.ValueText, "Controller", GetActionPreview(action)));
                    if (endpoints.Count >= maxItems_)
                    {
                        return endpoints;
                    }
                }
            }
        }

        return endpoints;
    }

    private IReadOnlyList<CodeEndpoint> DiscoverMinimalApis(CompilationUnitSyntax root_, string relativePath_, int maxItems_)
    {
        List<CodeEndpoint> endpoints = [];
        Dictionary<string, string> groupRoutes = this.GetGroupRoutes(root_);

        foreach (InvocationExpressionSyntax invocation in root_.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            string methodName = GetInvocationName(invocation);
            if (!MinimalApiMethods.TryGetValue(methodName, out string? method))
            {
                continue;
            }

            string route = GetFirstStringArgument(invocation.ArgumentList);
            string prefix = GetInvocationReceiver(invocation) is string receiver && groupRoutes.TryGetValue(receiver, out string? value) ? value : string.Empty;
            FileLinePositionSpan span = invocation.SyntaxTree.GetLineSpan(invocation.GetLocation().SourceSpan);
            endpoints.Add(new CodeEndpoint(method, CombineRoutes(prefix, route), relativePath_, span.StartLinePosition.Line + 1, methodName, "MinimalApi", GetInvocationPreview(invocation)));
            if (endpoints.Count >= maxItems_)
            {
                return endpoints;
            }
        }

        return endpoints;
    }

    private Dictionary<string, string> GetGroupRoutes(CompilationUnitSyntax root_)
    {
        Dictionary<string, string> routes = new(StringComparer.OrdinalIgnoreCase);
        foreach (VariableDeclaratorSyntax variable in root_.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (variable.Initializer?.Value is not InvocationExpressionSyntax invocation)
            {
                continue;
            }

            if (GetInvocationName(invocation).Equals("MapGroup", StringComparison.OrdinalIgnoreCase))
            {
                routes[variable.Identifier.ValueText] = GetFirstStringArgument(invocation.ArgumentList);
            }
        }

        return routes;
    }

    private static IReadOnlyList<string> GetAttributeNames(SyntaxList<AttributeListSyntax> attributeLists_)
    {
        return attributeLists_.SelectMany(list_ => list_.Attributes).Select(attribute_ => NormalizeAttributeName(attribute_.Name.ToString())).ToArray();
    }

    private static string GetRouteFromAttributes(SyntaxList<AttributeListSyntax> attributeLists_, string attributeName_)
    {
        foreach (AttributeSyntax attribute in attributeLists_.SelectMany(list_ => list_.Attributes))
        {
            if (NormalizeAttributeName(attribute.Name.ToString()).Equals(attributeName_, StringComparison.OrdinalIgnoreCase))
            {
                return GetRouteFromAttribute(attribute);
            }
        }

        return string.Empty;
    }

    private static string GetRouteFromAttribute(AttributeSyntax attribute_)
    {
        return GetFirstStringArgument(attribute_.ArgumentList);
    }

    private static string GetFirstStringArgument(AttributeArgumentListSyntax? arguments_)
    {
        if (arguments_ is null)
        {
            return string.Empty;
        }

        foreach (AttributeArgumentSyntax argument in arguments_.Arguments)
        {
            string value = ExtractString(argument.Expression);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string GetFirstStringArgument(ArgumentListSyntax arguments_)
    {
        foreach (ArgumentSyntax argument in arguments_.Arguments)
        {
            string value = ExtractString(argument.Expression);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string ExtractString(ExpressionSyntax expression_)
    {
        return expression_ switch
        {
            LiteralExpressionSyntax literal => literal.Token.ValueText,
            InterpolatedStringExpressionSyntax interpolated => interpolated.Contents.ToString(),
            _ => expression_.ToString().Trim('"')
        };
    }

    private static string GetInvocationName(InvocationExpressionSyntax invocation_)
    {
        return invocation_.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => invocation_.Expression.ToString()
        };
    }

    private static string? GetInvocationReceiver(InvocationExpressionSyntax invocation_)
    {
        return invocation_.Expression is MemberAccessExpressionSyntax member ? member.Expression.ToString() : null;
    }

    private static string NormalizeAttributeName(string value_)
    {
        string name = value_.Split('.').Last();
        return name.EndsWith("Attribute", StringComparison.OrdinalIgnoreCase) ? name[..^9] : name;
    }

    private static string CombineRoutes(string left_, string right_)
    {
        string left = NormalizeRoute(left_);
        string right = NormalizeRoute(right_);
        if (string.IsNullOrWhiteSpace(left))
        {
            return string.IsNullOrWhiteSpace(right) ? "/" : right;
        }

        if (string.IsNullOrWhiteSpace(right))
        {
            return left;
        }

        return left.TrimEnd('/') + "/" + right.TrimStart('/');
    }

    private static string NormalizeRoute(string value_)
    {
        string route = value_.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(route))
        {
            return string.Empty;
        }

        return route.StartsWith("/", StringComparison.Ordinal) ? route : "/" + route;
    }

    private static string GetActionPreview(MethodDeclarationSyntax method_)
    {
        string parameters = string.Join(", ", method_.ParameterList.Parameters.Select(parameter_ => parameter_.Type is null ? parameter_.Identifier.ValueText : parameter_.Type + " " + parameter_.Identifier.ValueText));
        return $"{method_.ReturnType} {method_.Identifier.ValueText}({parameters})";
    }

    private static string GetInvocationPreview(InvocationExpressionSyntax invocation_)
    {
        string route = GetFirstStringArgument(invocation_.ArgumentList);
        string method = GetInvocationName(invocation_);
        return string.IsNullOrWhiteSpace(route) ? method + "(...)" : method + "(\"" + route + "\", ...)";
    }
}
