using Xunit;

// Several end-to-end safety tests intentionally toggle the process-wide
// RuntimeSafetyState. Running those tests beside dictation, browser, or provider
// tests creates impossible states that real Haven startup never permits.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
