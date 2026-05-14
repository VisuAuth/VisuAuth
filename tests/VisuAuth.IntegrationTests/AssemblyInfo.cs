using Xunit;

// Each test class gets its own SQLite database via VisuAuthTestFactory, but the
// assembly stays serialised to keep Identity / EF Core boot cost predictable
// and to match the CLAUDE.md §10.4 contract.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
