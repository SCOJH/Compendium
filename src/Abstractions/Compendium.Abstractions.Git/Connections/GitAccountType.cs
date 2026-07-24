// -----------------------------------------------------------------------
// <copyright file="GitAccountType.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Git.Connections;

/// <summary>
/// The kind of account a namespace or credential identity refers to.
/// </summary>
public enum GitAccountType
{
    /// <summary>An organization / group account.</summary>
    Organization,

    /// <summary>A personal user account.</summary>
    User,
}
