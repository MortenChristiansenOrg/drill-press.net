# Performance evidence

`initial-linux-x64.json` retains the measurements that motivated slice 7's query
changes. The exhaustive managed/native responses, validated plans and compact
output were saved before optimizing queries, using instrumentation commit
`b78c86b8c3c5404fde4a0c4f57a33f2cabba164e`. The first optimized managed execution
matched all three hashes exactly against the same 51,541,158-byte snapshot.
Bundle hashes identify the measured artifacts; the optimized source was still
uncommitted. These are single observations on one machine, not release gates.

| Measurement | Exhaustive managed | Optimized managed |
| --- | ---: | ---: |
| Rule process total (profile ms) | 78,765.63 | 54,876.58 |
| Process peak working set (bytes) | 3,269,668,864 | 2,386,776,064 |
| DP1004 (profile ms) | 26,327.77 | 1,914.98 |
| Member symbol bindings | 967,092 | 2,260 |
| Interface/type comparisons | 148,902 | 0 |

The interface index prepared 1,726 entries and answered 2,088 lookups. DP1003
elapsed time changed only from 686.18 to 664.91 ms in these observations;
its benefit is avoiding repeated definition scans, not a claimed large speedup.
Managed and native exhaustive runs also matched complete signatures.

Run the [documented pinned repository command](../../docs/PROFILING.md) to
produce full same-machine managed/native and exhaustive/optimized comparisons
for the sample, compiler fixture and xUnit workloads, including disposable
fix/recheck runs. It retains exact raw responses, normalized comparison
signatures, snapshots, public output, process resources and phase logs under
the selected report directory. Do not commit snapshots containing private
source or machine-local paths from another repository.
