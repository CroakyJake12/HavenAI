# Mandatory Agent and Tool Execution Rules

- Preserve the production inspect → plan → act → observe → verify → repair loop. A tool-call request is not completion; feed real results/errors back to the originating agent and UI.
- Use the Capability Registry as the authoritative executable-capability catalogue. Apps/templates may request capabilities but may not create parallel executors.
- Apply the central risk policy and the existing model-picker effort/risk control where specified. Permissions are scoped, explicit, reviewable and revocable.
- File tools remain confined to an accepted workspace, use atomic/recoverable writes where applicable, and keep before/after evidence.
- Command execution avoids an unnecessary shell, carries cancellation/timeouts, bounds output, and terminates only the process tree Haven started.
- Vision operates only on real attached/captured images and reports model/provider capability limitations honestly.
- Windows and Android tool implementations may differ behind shared contracts. Test both applicable backends; do not advertise unsupported device actions.
- Generated agents/subagents receive the same repository rules, security boundary, capability allow/deny lists and validation obligations as the parent.
- Never remove approvals, sandbox checks, audit trails or observed-outcome verification to make an agent appear more autonomous.
