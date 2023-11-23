namespace WitsmlExplorer.Api.Models
{
    public enum JobType
    {
        CopyComponents = 1,
        CopyLog,
        CopyLogData,
        CopyObjects,
        CopyWell,
        CopyWellbore,
        CopyWithParent,
        TrimLogObject,
        DeleteComponents,
        DeleteCurveValues,
        DeleteObjects,
        DeleteWell,
        DeleteWellbore,
        ModifyLogCurveInfo,
        DeleteEmptyMnemonics,
        ModifyBhaRun,
        ModifyFormationMarker,
        ModifyGeologyInterval,
        ModifyLogObject,
        ModifyMessageObject,
        ModifyMudLog,
        ModifyRig,
        ModifyRisk,
        ModifyTrajectoryStation,
        ModifyTubular,
        ModifyTubularComponent,
        ModifyWbGeometry,
        ModifyWbGeometrySection,
        ModifyWell,
        ModifyWellbore,
        CreateLogObject,
        CreateWell,
        CreateWellbore,
        CreateRisk,
        CreateMudLog,
        CreateRig,
        CreateTrajectory,
        CreateWbGeometry,
        BatchModifyWell,
        ImportLogData,
        ReplaceComponents,
        ReplaceObjects,
        CheckLogHeader,
        MissingData,
        AnalyzeGaps,
        SpliceLogs,
        CompareLogData,
        CountLogDataRows,
        ModifyTrajectory
    }
}
