using Xunit;

namespace YouTubeTvShell.Tests.TestSupport;

/// <summary>
/// All host-harness tests mutate process env vars and/or launch the real app
/// (single-instance mutex, foreground window). They must never run in parallel
/// with each other. Other test classes are unaffected: they touch neither
/// YTTV_TEST_* variables nor the app process.
/// </summary>
[CollectionDefinition("host-harness-serial", DisableParallelization = true)]
public sealed class HostHarnessCollection
{
}
