# Tracebag Demo API

The demo API is a deliberately bounded .NET 8 target for learning and testing
Tracebag. It produces structured logs, recognizable runtime-counter changes,
and useful trace/dump inputs without modifying a real application.

Start Tracebag and the demo from the repository root:

```bash
./scripts/demo-up.sh --traffic
```

Tracebag is available at <http://localhost:9090> and the demo API at
<http://localhost:9091>. The optional traffic process calls the healthy endpoint
once per second and is marked internal, so only the demo API appears in Tracebag.

Every expensive scenario has a server-owned maximum. Only four different
scenarios may be active at once, a scenario cannot overlap another run of
itself, and `POST /demo/reset` cancels all active work. Container CPU, memory,
PID, and localhost port limits provide a second boundary.

## Normal structured traffic

```bash
curl http://localhost:9091/demo/healthy
```

Expected evidence: JSON logs containing `Demo healthy request`, a request ID,
customer tier, item count, ASP.NET trace ID, status code, and elapsed time. In
Tracebag, open **Tracebag Demo API → Logs** and filter for `healthy request`.

## Exception burst

```bash
curl -X POST 'http://localhost:9091/demo/exceptions?count=20'
```

Expected evidence: twenty bounded error entries with stack traces and numbered
checkout failures. With runtime counters running, `Exception Count` rises.
Accepted count: 1–100.

## CPU pressure

```bash
curl -X POST 'http://localhost:9091/demo/cpu?seconds=20&workers=2'
```

Expected evidence: `CPU Usage (%)` rises for about twenty seconds and the start
and completion logs share a scenario ID. A CPU trace captured during the window
contains the synthetic math loop. Accepted duration: 1–30 seconds; workers: 1–4.

## Allocation pressure

```bash
curl -X POST 'http://localhost:9091/demo/allocations?seconds=20&mbPerSecond=20'
```

Expected evidence: allocation-rate, GC, and heap counters move while logs state
the exact bounded rate. Allocations are released each 250 ms tick rather than
retained indefinitely. Accepted duration: 1–30 seconds; rate: 1–32 MB/s.

## Slow request

```bash
time curl -X POST 'http://localhost:9091/demo/slow?milliseconds=3000'
```

Expected evidence: the HTTP request takes roughly three seconds; warning,
completion, and request-duration logs identify it. Accepted delay: 100–5000 ms.

## Lock contention

```bash
curl -X POST 'http://localhost:9091/demo/lock-contention?seconds=20&workers=20'
```

Expected evidence: runtime lock-contention signals and many worker stacks
waiting around one shared monitor. A threading trace is the most useful
artifact. Accepted duration: 1–30 seconds; workers: 2–32.

## ThreadPool starvation

```bash
curl -X POST 'http://localhost:9091/demo/threadpool-starvation?seconds=20&workers=24'
```

Expected evidence: ThreadPool thread and queue signals rise, healthy requests
may become temporarily slower, and stacks show bounded blocking workers. The
workers exit automatically. Accepted duration: 1–20 seconds; workers: 2–32.

## Downstream failure

```bash
curl -X POST 'http://localhost:9091/demo/dependency-failure?seconds=20'
```

Expected evidence: repeated structured warnings identify `payments-demo`, an
attempt number, timeout, and simulated 504 status. Accepted duration: 1–30 seconds.

## Status and recovery

```bash
curl http://localhost:9091/demo/status
curl -X POST http://localhost:9091/demo/reset
```

Status returns the active count, latest state for every scenario, and all hard
limits. Completed scenarios stop themselves. Reset cancels active work and waits
up to five seconds for cooperative cleanup. Stopping the container invokes the
same reset path.

Run `./scripts/demo-smoke-test.sh` to verify the API, structured exception logs,
discovery labels, and reset behavior. Stop everything with
`./scripts/demo-down.sh`; persistent Tracebag data is retained.
