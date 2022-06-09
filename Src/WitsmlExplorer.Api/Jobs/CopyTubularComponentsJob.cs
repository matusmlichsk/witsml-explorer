using WitsmlExplorer.Api.Jobs.Common;

namespace WitsmlExplorer.Api.Jobs
{
    public record CopyTubularComponentsJob
    {
        public TubularComponentsReference Source { get; init; }
        public TubularReference Target { get; init; }
    }
}
