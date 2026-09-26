# Measuring a grammar

These commands answer different questions. `grammar check` asks PanGloss to inspect the whole grammar and stores findings for the current [Baseline](term:baseline). It does not parse selected words. `assess` parses a Selection and records one or more [Assessments](term:assessment). `overview` reads stored summaries; `stats` queries statistics from a stored run.

Capture a Baseline explicitly with [baseline capture](cmd:baseline%20capture) when you want to name that step:

```powershell
motif baseline capture "C:\Projects\Sena.fwdata" --json
```

Then use [assess](cmd:assess) to measure the saved Default Selection, or name a source such as all wordforms. Read stored summaries with [overview](cmd:overview) and query statistics by replaying the retained parser evidence and cache for a stored Assessment with [stats](cmd:stats):

```powershell
motif assess "C:\Projects\Sena.fwdata" --all-wordforms --json
motif overview --project "C:\Projects\Sena.fwdata" --json
motif stats "C:\Projects\Sena.fwdata" --json
```

`assess` is synchronous in the current CLI. If you give no Selection flags, it uses the saved Default Selection. Other supported sources include `--texts <guid,guid>`, `--words <file>`, and retry options such as `--retry-failed --retry-source-assessment <id>`. The command ensures a Baseline exists if needed.

The successful `assess --json` response includes the invocation identifier and the Assessment records produced. For example, its relevant fields have this shape:

```json
{
  "invocationId": "<invocation-id>",
  "assessmentIds": ["<assessment-id>"],
  "measurements": [
    {
      "assessmentId": "<assessment-id>",
      "kind": "ObjectTiming",
      "invocationId": "<invocation-id>"
    }
  ]
}
```

Use `invocationId` when you need to create a [Handoff](cmd:handoff) for this exact retained run. The number and kinds of `measurements` can vary with the Assessor’s supported kinds; do not assume a fixed Assessment count.

Use [grammar check](cmd:grammar%20check) to check the whole grammar. [warnings](cmd:warnings) reads findings that have already been stored:

```powershell
motif grammar check --project "C:\Projects\Sena.fwdata" --json
motif warnings --project "C:\Projects\Sena.fwdata" --json
```

`warnings` reads findings already stored for that Baseline; it does not rerun the check. Without a Baseline, `grammar check` succeeds with `hasBaseline: false` and no findings. Use **Working with jobs** for commands that enqueue background work.
