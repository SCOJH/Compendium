// -----------------------------------------------------------------------
// <copyright file="MessageAttachment.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Messaging.Models;

/// <summary>A media or file attachment carried by an inbound or outbound message.</summary>
/// <param name="Kind">The attachment kind (see <see cref="AttachmentKinds"/>).</param>
/// <param name="Url">A URL the attachment can be fetched from, when the platform exposes one.</param>
/// <param name="MimeType">The MIME type, when known.</param>
/// <param name="FileName">The original file name, when known.</param>
public sealed record MessageAttachment(
    string Kind,
    string? Url = null,
    string? MimeType = null,
    string? FileName = null);

/// <summary>Well-known <see cref="MessageAttachment.Kind"/> values.</summary>
public static class AttachmentKinds
{
    /// <summary>An image.</summary>
    public const string Image = "image";

    /// <summary>An audio clip or voice note.</summary>
    public const string Audio = "audio";

    /// <summary>A video.</summary>
    public const string Video = "video";

    /// <summary>A generic file / document.</summary>
    public const string File = "file";

    /// <summary>A geographic location.</summary>
    public const string Location = "location";
}
