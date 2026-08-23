// -----------------------------------------------------------------------
// <copyright file="EventTypeNameAttribute.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Core.EventSourcing.Attributes;

/// <summary>
/// Declares the logical name under which an event type is indexed and stored.
/// Without this attribute an event is identified by its <see cref="Type.AssemblyQualifiedName"/>,
/// which ties an already written event log to the binary identity of the assembly that
/// defined the event (namespace, assembly, version, culture, public key token).
/// The attribute is not inherited, so a derived event never claims the name of its parent.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class EventTypeNameAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EventTypeNameAttribute"/> class.
    /// </summary>
    /// <param name="name">The logical name of the event type. Must not be null, empty or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null, empty or whitespace.</exception>
    public EventTypeNameAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
    }

    /// <summary>
    /// Gets the logical name of the event type.
    /// </summary>
    public string Name { get; }
}
