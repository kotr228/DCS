// The interface language (Loc.Instance) is shared app-wide; tests that switch it must not
// run at the same time as tests that read English texts.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
