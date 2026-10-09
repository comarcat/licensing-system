# closing-step

Run post-session closing procedures: update documentation, run analysis tools, update memory, and git push.

## Usage

```
/closing-step "<commit-message>"
```

## Behavior

1. Updates PMI, Error, and Change logs.
2. Runs `graphify` and `archify`.
3. Commits changes with the provided message and pushes to GitHub.
4. Clears/compacts session state.
