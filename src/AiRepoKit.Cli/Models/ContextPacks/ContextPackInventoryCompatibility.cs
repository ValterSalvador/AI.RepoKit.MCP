namespace AiRepoKit.Cli.Models.ContextPacks;

public sealed record ContextPackInventoryCompatibility(
    bool SymbolCompatible,
    bool EndpointCompatible)
{
    public bool Compatible => this.SymbolCompatible && this.EndpointCompatible;
}
