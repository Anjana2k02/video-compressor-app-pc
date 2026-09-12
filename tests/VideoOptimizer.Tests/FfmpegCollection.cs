using Xunit;

namespace VideoOptimizer.Tests;

// Integration tests that launch the bundled FFmpeg share one collection so they never run in parallel.
// One test enumerates ffmpeg processes by executable path; concurrent exports from the same binary would
// otherwise make it observe another test's process.
[CollectionDefinition("ffmpeg-integration", DisableParallelization = true)]
public sealed class FfmpegIntegrationCollection;
