# 0010. Logical event identity, decoupled from binary identity

* Status: Accepted
* Date: 2026-08-30 (recorded retroactively — the decision shipped as commits `69d3071` → `18263bc` and is documented in the [CHANGELOG](../../CHANGELOG.md) Unreleased section)
* Deciders: @sassy-solutions/maintainers

## Context

An event log is forever ([ADR-0005](0005-event-sourcing-vs-state.md)): rows written
today must be readable by binaries compiled years from now. Until this decision, an
event was identified in the store by its `Type.AssemblyQualifiedName` — which ties
an already-written log to the **binary identity** of the assembly that defined the
event: namespace, assembly name, version, culture, public key token.

Under that scheme, perfectly routine refactorings become data-loss events:
renaming an event class, moving it to another namespace, or splitting an assembly
makes every previously written row of that type unresolvable. The failure is also
maximally unfair — it punishes the codebase hygiene the rest of the framework
encourages.

A secondary problem: the in-memory stores at the time silently *dropped* events
whose type no longer resolved and returned the remaining stream as a success — an
aggregate rebuilt from an incomplete history, with no way for the caller to know.

## Decision

**An event's storage identity is a logical name, declared explicitly and stable by
contract:**

- `[EventTypeName("orders.order-placed")]` on the event type declares the name
  under which it is indexed and stored. The attribute is *not inherited*, so a
  derived event never claims the name of its parent.
- `EventTypeRegistry` indexes every registered type under **two keys**: its logical
  name *and* its `AssemblyQualifiedName`. A log holding both old (AQN) and new
  (logical) rows is therefore readable by a single binary, without rewriting a line
  already written. `Count` and `GetRegisteredTypes` keep counting types, not keys.
- **Name collisions are refused at registration**, atomically for a whole batch —
  *"a payload deserialized into the wrong type is worse than one not deserialized
  at all"* (`EventTypeRegistry.cs`). No last-writer-wins.
- Stores stamp written events via `IEventTypeRegistry.GetLogicalName(type)`; the
  registry is an optional, last-position constructor parameter, so existing
  callers keep the pre-logical-names behaviour untouched.
- `IEventTypeRegistry` gained `GetLogicalName` as a **breaking interface change,
  deliberately without a default interface member**. A default returning the AQN
  was considered and rejected: a silent fallback would leave an implementer writing
  binary identities while the rest of the system assumed otherwise — the very
  failure this change exists to remove.
- Reads fail loudly: a stream containing one unresolvable event type returns
  `Result.Failure` (`EventStore.EventTypeUnresolved`, naming the aggregate, the
  event version, and the unresolved name) instead of a truncated history as a
  success.

## Consequences

### Positive

- Renaming, moving, or re-versioning an event class no longer corrupts the
  readability of the log — for every event that carries the attribute.
- Migration requires no data rewrite and no flag day: old rows resolve through the
  AQN index, new rows are written under the logical name, one binary reads both.
- Failure modes are now visible: an unregistered or renamed-without-attribute type
  is a red `Result` naming the exact row, not a silently shorter history.
- The breaking interface change surfaces at *compile time* for external
  `IEventTypeRegistry` implementations — the cheapest possible place to discover it.

### Negative / Trade-offs

- **Protection is prospective only.** A row written under an AQN *before* a rename
  is not recoverable by this mechanism — the AQN index holds the *current* type's
  name. The attribute protects what is written from the moment it exists.
- **No aliases.** `AllowMultiple = false` and no old-name → new-name mapping means
  a logical name, once written to a log, is as permanent as the log itself.
  Renaming a *logical* name is not supported without an upcaster to a new type.
- **Opt-in, with a fragile default.** An event without the attribute still gets the
  AQN behaviour — teams must adopt the attribute deliberately; nothing warns them.
- One breaking change on `IEventTypeRegistry` for external implementers — accepted
  and documented with its rationale in the CHANGELOG; `EventTypeRegistry` is the
  only implementation in this repository.

## Alternatives considered

- **Keep `AssemblyQualifiedName` as the identity.** Rejected — it makes refactoring
  the enemy of the event log.
- **Full type name (namespace + class, no assembly).** Rejected — softer, but still
  binary-derived: a namespace move still breaks resolution, and the ambiguity
  across assemblies has to be resolved somewhere anyway.
- **A default interface member on `GetLogicalName` returning the AQN.** Considered
  and rejected explicitly (see Decision) — compile-time breakage was chosen over a
  silent wrong default.
- **Mapping table in configuration** (name → type in appsettings). Rejected —
  moves a compile-time-checkable fact into runtime configuration, the least
  reviewable place for something this permanent.
- **Convention-based naming** (e.g. kebab-case of the class name). Rejected — a
  convention derived from the class name re-couples the stored name to the thing
  that renames; the attribute makes the coupling explicit and severable.
