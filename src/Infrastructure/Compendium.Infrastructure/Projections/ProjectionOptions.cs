// -----------------------------------------------------------------------
// <copyright file="ProjectionOptions.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Infrastructure.Projections;

/// <summary>
/// Configuration options for projection management and processing.
/// </summary>
public class ProjectionOptions
{
    /// <summary>
    /// Configuration section name for appsettings.json.
    /// </summary>
    public const string SectionName = "Compendium:Projections";

    /// <summary>
    /// Gets or sets the batch size for processing events during rebuilds.
    /// Default is 1000 events per batch.
    /// </summary>
    public int RebuildBatchSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the maximum number of concurrent rebuild operations.
    /// Default is 3 to prevent resource exhaustion.
    /// </summary>
    public int MaxConcurrentRebuilds { get; set; } = 3;

    /// <summary>
    /// Gets or sets the interval for progress reporting during rebuilds.
    /// Progress will be reported every N processed events. Default is 100.
    /// </summary>
    public int ProgressReportInterval { get; set; } = 100;

    /// <summary>
    /// Gets or sets the interval for saving checkpoints during processing.
    /// Default is 10 seconds.
    /// </summary>
    public TimeSpan CheckpointInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets whether to enable snapshot functionality.
    /// Default is true for better rebuild performance.
    /// </summary>
    public bool EnableSnapshots { get; set; } = true;

    /// <summary>
    /// Gets or sets the interval for creating snapshots.
    /// Default is 5 minutes.
    /// </summary>
    public TimeSpan SnapshotInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the timeout for projection operations.
    /// Default is 30 seconds.
    /// </summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the retry count for failed operations.
    /// Default is 3 retries.
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Gets or sets the delay between retries.
    /// Default is 1 second.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets how many consecutive times the live processor re-attempts
    /// applying the <i>same</i> event to a projection that keeps throwing before
    /// it <b>dead-letters</b> (halts) that projection. Default is 5.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The live processor must NEVER advance a projection's checkpoint past an
    /// event it failed to apply — doing so silently drops that event from the
    /// read model forever (the class of bug this guards against). Instead the
    /// projection's checkpoint is held at the last successfully-applied position
    /// and the event is retried on subsequent polling passes. Transient faults
    /// (a brief DB blip) recover within a few attempts.
    /// </para>
    /// <para>
    /// A genuinely poisoned event would otherwise retry forever and, because the
    /// global read cursor is the MIN over live projections, force the processor to
    /// re-read the tail of the stream on every pass. After this many consecutive
    /// failures the projection is halted: it stops receiving events (its read
    /// model is now knowingly stale, logged at Critical), and the other
    /// projections are freed to advance past the poison event. A restart/redeploy
    /// clears the halt and re-attempts from the held checkpoint.
    /// </para>
    /// </remarks>
    public int MaxProjectionApplyFailures { get; set; } = 5;

    /// <summary>
    /// Gets or sets whether <see cref="ILiveProjectionProcessor"/> should backfill from
    /// position 0 on first start, when no checkpoints exist for any registered projection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Default <see langword="false"/></b> — preserves the historical behaviour where the
    /// processor jumps to the current head of the event stream on a cold start, so a fresh
    /// deploy doesn't re-replay weeks of events on every restart.
    /// </para>
    /// <para>
    /// Set to <see langword="true"/> when projections are the <i>only</i> writers to the
    /// read model (no manual writers, no parallel materialisers): otherwise a cold-start
    /// processor leaves the read model permanently behind the event store. The flag only
    /// affects the very first start — once any projection persists a checkpoint, that
    /// checkpoint takes over and this option is ignored.
    /// </para>
    /// </remarks>
    public bool BackfillFromBeginningOnEmptyCheckpoint { get; set; }
}
