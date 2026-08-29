using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>Quartz job execution spans.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
[QylIntegration(QylAutoInstrumentationIds.Quartz, QylAttributes.InstrumentationDomainValues.JobQuartz)]
[QylIntercept("Quartz.IJob", "Execute", Shape = QylShapes.QuartzJob, Start = nameof(Execute), ObserveAsync = true)]
public static class QylInterceptedQuartz
{
    /// <summary>Starts the internal span named after the job's group and name.</summary>
    public static Activity? Execute(
        [QylFromArgument(0, Convert = "{0}.JobDetail?.Key?.Group")] string? jobGroup,
        [QylFromArgument(0, Convert = "{0}.JobDetail?.Key?.Name")] string? jobName)
        => QylActivityFactory.StartTraceActivity(
            QylAutoInstrumentationIds.Quartz,
            QylSpanNames.Job(jobGroup, jobName),
            ActivityKind.Internal,
            QylAttributes.InstrumentationDomainValues.JobQuartz);
}
