# Ten-minute Tracebag demo tour

This tour discovers a real .NET container, reads structured logs, watches a
runtime counter react, and captures a diagnostic artifact. All load is local,
bounded, and resettable.

## 0:00–2:00 — launch

If `.env` does not exist yet, run `./scripts/init-env.sh`. Then launch the entire
environment—including safe normal traffic—with one command:

```bash
./scripts/demo-up.sh --traffic
```

Open <http://localhost:9090>, sign in as `admin`, and select **Tracebag Demo
API**. The target is discoverable because the demo Compose file supplies the
opt-in, .NET-kind, and shared diagnostics-volume labels.

## 2:00–4:00 — inspect structured logs

Open **Logs**, select **Follow**, and filter for `healthy request`. Each traffic
entry includes a request ID, trace ID, customer tier, item count, status, and
duration.

Create a short error burst:

```bash
curl -X POST 'http://localhost:9091/demo/exceptions?count=5'
```

Filter for `Demo exception` to see the numbered failures and stack traces.

## 4:00–7:00 — make counters react

Open **Metrics**, choose **Discover**, select the `Tracebag.Demo.Api` process,
select the **Runtime** preset, and press **Start**.

In another terminal, start bounded CPU pressure:

```bash
curl -X POST 'http://localhost:9091/demo/cpu?seconds=20&workers=2'
```

Watch `CPU Usage (%)` rise and return. To see GC instead, run:

```bash
curl -X POST 'http://localhost:9091/demo/allocations?seconds=15&mbPerSecond=20'
```

Watch allocation-rate, heap, and GC values, then stop the counter session.

## 7:00–9:00 — capture evidence

Start CPU pressure again. Open **Diagnostics**, choose **Discover**, select the
demo process, set trace duration to 5 seconds, and choose **Create trace**.

Open **Artifacts** to see and download the resulting `.nettrace`. The runner was
temporary, had no network or Docker socket, and was deleted after collection.

## 9:00–10:00 — recover and explore

The scenarios stop automatically at their hard duration limit. Cancel anything
still active and inspect the reported state with:

```bash
curl -X POST http://localhost:9091/demo/reset
curl http://localhost:9091/demo/status
./scripts/demo-smoke-test.sh
```

The complete [scenario guide](../demo/README.md) covers slow requests, lock
contention, ThreadPool starvation, and downstream failure, including the exact
evidence to expect from each.
