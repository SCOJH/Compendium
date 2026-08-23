// -----------------------------------------------------------------------
// <copyright file="EventTypeRegistry.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Frozen;
using System.Reflection;
using Compendium.Core.Domain.Events;
using Compendium.Core.Domain.Primitives;
using Compendium.Core.EventSourcing.Attributes;

namespace Compendium.Core.EventSourcing;

/// <summary>
/// Thread-safe registry for whitelisted event types using .NET 9 frozen collections for optimal performance.
/// Prevents deserialization attacks by maintaining a strict whitelist of allowed domain event types.
/// </summary>
/// <remarks>
/// A registered type is indexed under two keys: its logical name — the value of its
/// <see cref="EventTypeNameAttribute"/> — and its <see cref="Type.AssemblyQualifiedName"/>.
/// An event log holding both forms is therefore readable by a single binary, without ever
/// rewriting a line already written. <see cref="Count"/> and <see cref="GetRegisteredTypes"/>
/// keep counting types, not keys.
/// </remarks>
public sealed class EventTypeRegistry : IEventTypeRegistry, IDisposable
{
    private readonly ILockingStrategy _lockingStrategy;
    private readonly Dictionary<string, Type> _registeredTypes = new();
    private readonly HashSet<Type> _registeredTypeSet = new();
    private FrozenDictionary<string, Type>? _frozenCache;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventTypeRegistry"/> class.
    /// </summary>
    /// <param name="lockingStrategy">The locking strategy to use for thread safety.</param>
    public EventTypeRegistry(ILockingStrategy? lockingStrategy = null)
    {
        _lockingStrategy = lockingStrategy ?? new ReaderWriterLockStrategy();
    }

    /// <inheritdoc />
    public bool IsWhitelisted(string typeName)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);

        return _lockingStrategy.ExecuteRead(() =>
        {
            var cache = _frozenCache ??= _registeredTypes.ToFrozenDictionary();
            return cache.ContainsKey(typeName);
        });
    }

    /// <inheritdoc />
    public Type? GetWhitelistedType(string typeName)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);

        return _lockingStrategy.ExecuteRead(() =>
        {
            var cache = _frozenCache ??= _registeredTypes.ToFrozenDictionary();
            cache.TryGetValue(typeName, out var type);
            return type;
        });
    }

    /// <inheritdoc />
    public string GetLogicalName(Type eventType)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(eventType);

        return ResolveLogicalName(eventType);
    }

    /// <inheritdoc />
    public void RegisterEventType(Type eventType)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(eventType);

        if (!typeof(IDomainEvent).IsAssignableFrom(eventType))
        {
            throw new ArgumentException($"Type {eventType.FullName} must implement {nameof(IDomainEvent)}", nameof(eventType));
        }

        _lockingStrategy.ExecuteWrite(() =>
        {
            var logicalName = ResolveLogicalName(eventType);

            // Refuse the collision here rather than letting the last writer win:
            // a payload deserialized into the wrong type is worse than one not deserialized at all.
            EnsureNameIsAvailable(logicalName, eventType, pending: null);
            EnsureNameIsAvailable(eventType.AssemblyQualifiedName!, eventType, pending: null);

            Index(eventType, logicalName);

            // Invalidate cache to force recreation
            _frozenCache = null;
        });
    }

    /// <inheritdoc />
    public IReadOnlyCollection<Type> GetRegisteredTypes()
    {
        ThrowIfDisposed();

        return _lockingStrategy.ExecuteRead(() => _registeredTypeSet.ToFrozenSet());
    }

    /// <summary>
    /// Registers multiple event types at once for efficient batch registration.
    /// </summary>
    /// <param name="eventTypes">The event types to register.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when two distinct types claim the same logical name. Nothing is registered in that case.
    /// </exception>
    public void RegisterEventTypes(IEnumerable<Type> eventTypes)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(eventTypes);

        var types = eventTypes.ToList();
        if (types.Count == 0)
        {
            return;
        }

        // Validate all types first
        foreach (var eventType in types)
        {
            if (!typeof(IDomainEvent).IsAssignableFrom(eventType))
            {
                throw new ArgumentException($"Type {eventType.FullName} must implement {nameof(IDomainEvent)}", nameof(eventTypes));
            }
        }

        _lockingStrategy.ExecuteWrite(() =>
        {
            // Second validation pass: names, against what is already registered and against the batch
            // itself. Nothing is written until the whole batch is known to be free of collisions.
            var prepared = new List<(Type EventType, string LogicalName)>(types.Count);
            var pending = new Dictionary<string, Type>();

            foreach (var eventType in types)
            {
                var logicalName = ResolveLogicalName(eventType);
                var assemblyQualifiedName = eventType.AssemblyQualifiedName!;

                EnsureNameIsAvailable(logicalName, eventType, pending);
                EnsureNameIsAvailable(assemblyQualifiedName, eventType, pending);

                pending[logicalName] = eventType;
                pending[assemblyQualifiedName] = eventType;
                prepared.Add((eventType, logicalName));
            }

            foreach (var (eventType, logicalName) in prepared)
            {
                Index(eventType, logicalName);
            }

            // Invalidate cache to force recreation
            _frozenCache = null;
        });
    }

    /// <summary>
    /// Automatically discovers and registers all domain event types from the specified assemblies.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan for domain events.</param>
    public void AutoRegisterFromAssemblies(params Assembly[] assemblies)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(assemblies);

        var eventTypes = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(IDomainEvent).IsAssignableFrom(type) &&
                          !type.IsAbstract &&
                          !type.IsInterface)
            .ToList();

        RegisterEventTypes(eventTypes);
    }

    /// <summary>
    /// Clears all registered event types. Use with caution.
    /// </summary>
    public void Clear()
    {
        ThrowIfDisposed();

        _lockingStrategy.ExecuteWrite(() =>
        {
            _registeredTypes.Clear();
            _registeredTypeSet.Clear();
            _frozenCache = null;
        });
    }

    /// <summary>
    /// Gets the number of registered event types.
    /// </summary>
    public int Count => _lockingStrategy.ExecuteRead(() => _registeredTypeSet.Count);

    private static string ResolveLogicalName(Type eventType)
    {
        var attribute = eventType.GetCustomAttribute<EventTypeNameAttribute>(inherit: false);

        return attribute?.Name ?? eventType.AssemblyQualifiedName!;
    }

    private void EnsureNameIsAvailable(string typeName, Type eventType, IReadOnlyDictionary<string, Type>? pending)
    {
        if (_registeredTypes.TryGetValue(typeName, out var owner) && owner != eventType)
        {
            throw BuildCollisionException(typeName, eventType, owner);
        }

        if (pending is not null && pending.TryGetValue(typeName, out var pendingOwner) && pendingOwner != eventType)
        {
            throw BuildCollisionException(typeName, eventType, pendingOwner);
        }
    }

    private static InvalidOperationException BuildCollisionException(string typeName, Type eventType, Type owner)
    {
        return new InvalidOperationException(
            $"Event type {eventType.FullName} cannot be registered under the name '{typeName}': " +
            $"that name is already claimed by {owner.FullName}. Two event types cannot share a logical name.");
    }

    private void Index(Type eventType, string logicalName)
    {
        // When the type carries no attribute, both keys are the assembly qualified name
        // and the second assignment is a no-op: one type, one entry.
        _registeredTypes[logicalName] = eventType;
        _registeredTypes[eventType.AssemblyQualifiedName!] = eventType;
        _registeredTypeSet.Add(eventType);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(EventTypeRegistry));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _lockingStrategy.Dispose();
            _registeredTypes.Clear();
            _registeredTypeSet.Clear();
            _frozenCache = null;
            _disposed = true;
        }
    }
}
