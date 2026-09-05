# C4 — Level 3: Components of the core flow

This view maps the components that a command actually traverses in the code — from
dispatch to event append to projection — plus the event-serialization machinery and
the saga components. Types are named exactly as they appear in the source; the two
deliberate ruptures in the flow (places where the framework hands responsibility to
the host application) are marked.

## Write side and read side

```mermaid
flowchart TB
    caller["Caller<br/><i>controller, endpoint, job</i>"]

    subgraph application["Compendium.Application"]
        cd["CommandDispatcher<br/><i>sealed, resolves handler via<br/>IServiceProvider</i>"]
        behaviors["IPipelineBehavior chain<br/><i>Validation · Logging ·<br/>Transaction · Idempotency<br/>(opt-in, commands only)</i>"]
        qd["QueryDispatcher<br/><i>sealed — runs NO behaviors</i>"]
    end

    subgraph consumer["Host application code"]
        ch["ICommandHandler&lt;TCommand&gt;"]
        qh["IQueryHandler&lt;TQuery, TResponse&gt;"]
        agg["Aggregate : AggregateRoot&lt;TId&gt;<br/><i>raises events via AddDomainEvent,<br/>versions via IncrementVersion</i>"]
        rehydrate["BuildAggregateFromEvents /<br/>ApplyEventsToAggregate<br/><i>⚠ abstract — written by the host app</i>"]
        proj["IProjection&lt;TEvent&gt;<br/><i>one class, N event interfaces</i>"]
    end

    subgraph infrastructure["Compendium.Infrastructure"]
        repo["EventSourcedRepository&lt;TAggregate, TId&gt;<br/><i>load: snapshot + events;<br/>save: append with expectedVersion</i>"]
        es["IEventStore (port in Abstractions)<br/><i>AppendEventsAsync(id, events, expectedVersion)<br/>→ Error.Conflict on optimistic-concurrency clash</i>"]
        snap["ISnapshotStore + ISnapshotStrategy<br/><i>optional; NoSnapshotStrategy default</i>"]
        stream["IStreamingEventStore : IEventStore<br/><i>global position, IAsyncEnumerable</i>"]
        lpp["LiveProjectionProcessor<br/><i>BackgroundService, 100 ms polling,<br/>per-projection checkpoint + dead-letter<br/>after N consecutive failures</i>"]
        epm["EnhancedProjectionManager<br/><i>rebuild in batches, reflection dispatch<br/>to IProjection&lt;TEvent&gt;</i>"]
        pstore["IProjectionStore<br/><i>checkpoints by projection name,<br/>snapshots, state</i>"]
    end

    caller -->|"DispatchAsync"| cd
    caller -->|"DispatchAsync"| qd
    cd --> behaviors --> ch
    qd --> qh
    ch --> agg
    ch -->|"GetByIdAsync / SaveAsync"| repo
    repo --> rehydrate
    repo --> es
    repo --> snap
    stream -->|"StreamEventsAsync(fromPosition)"| lpp
    stream --> epm
    lpp -->|"ApplyAsync(event, EventMetadata)"| proj
    epm -->|"rebuild"| proj
    lpp --> pstore
    epm --> pstore

    es ~~~ stream

    classDef appl fill:#1168bd,stroke:#0b4884,color:#fff
    classDef infra fill:#438dd5,stroke:#2e6295,color:#fff
    classDef host fill:#7f5ba6,stroke:#5d4279,color:#fff
    classDef warn fill:#8a5a00,stroke:#5e3d00,color:#fff
    class cd,behaviors,qd appl
    class repo,es,snap,stream,lpp,epm,pstore infra
    class ch,qh,agg,proj host
    class rehydrate warn
```

The dispatchers and the behavior pipeline are the framework's own — no mediator
dependency; the reasoning is in [ADR-0012](../adr/0012-hand-rolled-cqrs-dispatch.md).

**The rupture to know about:** after `AppendEventsAsync` succeeds, **nothing is
dispatched automatically**. There is no in-process domain-event dispatcher
(`IDomainEventHandler<T>` exists but no component invokes it), and projections only
see events because `LiveProjectionProcessor` independently polls the streaming
store. The write path and the read path meet in the store, not in a bus.

## Event serialization and schema evolution (`Compendium.Core.EventSourcing`)

```mermaid
flowchart LR
    subgraph write["On append (persistent adapters)"]
        evt["IDomainEvent<br/><i>immutable, DomainEventBase</i>"]
        reg["EventTypeRegistry<br/><i>whitelist, indexed by logical name<br/>([EventTypeName]) AND by<br/>AssemblyQualifiedName</i>"]
        json["System.Text.Json<br/><i>runtime type passed explicitly;<br/>no Newtonsoft, no $type</i>"]
        evt --> json
        reg -->|"GetLogicalName(type)<br/>stamps the stored name"| json
    end

    subgraph read["On read"]
        sed["SecureEventDeserializer<br/><i>type comes from the registry,<br/>never from the payload;<br/>unknown name → Result.Failure</i>"]
        mig["EventVersionMigrator<br/><i>chains IEventUpcaster V1→V2→V3<br/>at read time; the log is<br/>never rewritten</i>"]
        sed --> mig
    end

    json -.->|"stored event row<br/>(name + JSON payload)"| sed
    reg -->|"GetWhitelistedType(name)"| sed

    classDef core fill:#0b6e4f,stroke:#08523b,color:#fff
    class evt,reg,json,sed,mig core
```

Reading notes:

- The registry's double indexing means a log holding both old
  (assembly-qualified-name) rows and new (logical-name) rows is readable by a single
  binary, without rewriting anything. Name collisions are refused at registration —
  *"a payload deserialized into the wrong type is worse than one not deserialized at
  all"* (`EventTypeRegistry.cs`). Rationale and trade-offs:
  [ADR-0010](../adr/0010-logical-event-identity.md) and
  [ADR-0011](../adr/0011-whitelist-deserialization-upcasting.md).
- A stream containing one unresolvable event type fails **as a whole**
  (`EventStore.EventTypeUnresolved`) rather than returning a truncated history as a
  success.
- Honest caveats: the in-memory stores keep live `IDomainEvent` instances and never
  exercise the deserialization path — only persistent adapters (separate repos) do;
  event registration is manual (`RegisterEventType`), and the
  `IGeneratedEventRegistry`/`[AutoRegisterEvent]` compile-time story has no source
  generator behind it yet; a missing upcaster is not detected — the event passes
  through unchanged.

## Sagas and integration events

Two explicit flavors, named so you know which pattern you are using
(see [docs/sagas.md](../sagas.md)):

- **Orchestration** — `ProcessManager<TState>` (step state machine) driven by
  `ProcessManagerOrchestrator`, persisting through `IProcessManagerRepository`
  (in-memory implementation provided) and executing business steps through
  `IProcessManagerStepExecutor` — which has **no implementation in this repo**: it
  is the mandatory extension point of the host application. Compensation is
  explicit, never automatic.
- **Choreography** — `IHandle<TEvent>` handlers fanned out by `ChoreographyRouter`
  over `IIntegrationEvent`s published through `IIntegrationEventPublisher`. The
  in-memory publisher is provided; a durable one (outbox) is an adapter concern and
  **does not live in this repository**.

**The second rupture:** nothing bridges the `IDomainEvent` world (event store,
projections) to the `IIntegrationEvent` world (choreography). Publishing an
integration event after a domain event is a decision — and code — that belongs to
the host application today.

Back to: [overview](overview.md) · [Level 1](c4-level1-context.md) · [Level 2](c4-level2-containers.md)
