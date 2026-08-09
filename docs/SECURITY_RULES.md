# Mandatory Haven Security, Privacy and Sandbox Rules

- Destructive, external, privileged, paid, credentialed, ambiguous or privacy-sensitive actions use the central permission/approval engine. No UI, template, generated App or agent may bypass it.
- Restrict project/file/Git/build/test capabilities to Studio or an explicitly accepted workspace. Canonicalise paths and reject traversal before I/O.
- Keep secrets out of prompts, logs, screenshots, generated UI state, exports and source control. Store provider credentials through the approved secret store.
- Browser/model navigation remains limited by the existing public-network, redirect, credential and approval policies. Do not weaken private/internal target protections.
- Voice/Lesson Voice transient transcript/audio state is ephemeral unless the user explicitly saves it. Apply the same privacy classification to generated events and attachments.
- Background Learning and Haven Library records retain provenance, trust, confidence, freshness, privacy classification, deletion and retention semantics.
- Observed outcomes are mandatory. Provider acknowledgement, process start, instruction dispatch or optimistic UI state is not proof an operation succeeded.
- Generated controls and imported templates are untrusted inputs until manifest, dependency, capability and integrity validation passes.
- Security checks may not be disabled to make a build/test pass. A known bypass makes the task incomplete.
