using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Witsml;
using Witsml.ServiceReference;

using WitsmlExplorer.Api.Jobs;
using WitsmlExplorer.Api.Models;
using WitsmlExplorer.Api.Query;
using WitsmlExplorer.Api.Services;

namespace WitsmlExplorer.Api.Workers
{
    public class DeleteTrajectoryStationsWorker : BaseWorker<DeleteTrajectoryStationsJob>, IWorker
    {
        private readonly IWitsmlClient _witsmlClient;
        public JobType JobType => JobType.DeleteTrajectoryStations;

        public DeleteTrajectoryStationsWorker(ILogger<DeleteTrajectoryStationsJob> logger, IWitsmlClientProvider witsmlClientProvider) : base(logger)
        {
            _witsmlClient = witsmlClientProvider.GetClient();
        }

        public override async Task<(WorkerResult, RefreshAction)> Execute(DeleteTrajectoryStationsJob job)
        {
            var wellUid = job.ToDelete.TrajectoryReference.WellUid;
            var wellboreUid = job.ToDelete.TrajectoryReference.WellboreUid;
            var trajectoryUid = job.ToDelete.TrajectoryReference.TrajectoryUid;
            var trajectoryStations = new ReadOnlyCollection<string>(job.ToDelete.TrajectoryStationUids.ToList());
            var trajectoryStationsString = string.Join(", ", trajectoryStations);

            var query = TrajectoryQueries.DeleteTrajectoryStations(wellUid, wellboreUid, trajectoryUid, trajectoryStations);
            var result = await _witsmlClient.DeleteFromStoreAsync(query);
            if (result.IsSuccessful)
            {
                Logger.LogInformation("Deleted trajectoryStations for trajectory object. WellUid: {WellUid}, WellboreUid: {WellboreUid}, Uid: {TrajectoryUid}, TrajectoryStations: {TrajectoryStationsString}",
                    wellUid,
                    wellboreUid,
                    trajectoryUid,
                    trajectoryStations);
                var refreshAction = new RefreshTrajectory(_witsmlClient.GetServerHostname(), wellUid, wellboreUid, trajectoryUid, RefreshType.Update);
                var workerResult = new WorkerResult(_witsmlClient.GetServerHostname(), true, $"Deleted trajectoryStations: {trajectoryStationsString} for trajectory: {trajectoryUid}");
                return (workerResult, refreshAction);
            }

            Logger.LogError("Failed to delete trajectoryStations for trajectory object. WellUid: {WellUid}, WellboreUid: {WellboreUid}, Uid: {TrajectoryUid}, TrajectoryStations: {TrajectoryStationsString}",
                wellUid,
                wellboreUid,
                trajectoryUid,
                trajectoryStations);

            query = TrajectoryQueries.GetWitsmlTrajectoryById(wellUid, wellboreUid, trajectoryUid);
            var queryResult = await _witsmlClient.GetFromStoreAsync(query, new OptionsIn(ReturnElements.IdOnly));

            var trajectory = queryResult.Trajectories.First();
            EntityDescription description = null;
            if (trajectory != null)
            {
                description = new EntityDescription
                {
                    WellName = trajectory.NameWell,
                    WellboreName = trajectory.NameWellbore,
                    ObjectName = trajectory.Name
                };
            }

            return (new WorkerResult(_witsmlClient.GetServerHostname(), false, "Failed to delete trajectory stations", result.Reason, description), null);
        }
    }
}