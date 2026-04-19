using NUnit.Framework;

// Aspire's DistributedApplicationTestingBuilder + BuildAsync mutates resource
// annotations on a background pipeline task after BuildAsync returns, which
// races with LINQ enumeration in parallel fixtures. Disable parallel execution
// across fixtures in this assembly so each test runs against a settled model.
[assembly: NonParallelizable]
[assembly: LevelOfParallelism(1)]
