// -----------------------------------------------------------------------
// <copyright file="SecretMaterial.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text;

namespace Compendium.Abstractions.Secrets.Model;

/// <summary>
/// The payload of one secret version. Wraps the raw bytes so the value can
/// never leak through <c>ToString()</c> (logging, interpolation, exception
/// messages). No zeroing guarantees are made — the .NET GC copies freely —
/// so treat any process that held a material as having seen the plaintext.
/// </summary>
public sealed class SecretMaterial
{
    private readonly byte[] _data;

    private SecretMaterial(byte[] data)
    {
        _data = data;
    }

    /// <summary>
    /// Gets the raw payload bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Data => _data;

    /// <summary>
    /// Gets the payload size in bytes.
    /// </summary>
    public int Length => _data.Length;

    /// <summary>
    /// Wraps raw bytes as secret material. The array is copied so later
    /// mutation of the source cannot alter the material.
    /// </summary>
    public static SecretMaterial FromBytes(ReadOnlySpan<byte> data) => new(data.ToArray());

    /// <summary>
    /// Wraps a UTF-8 string value as secret material.
    /// </summary>
    public static SecretMaterial FromString(string value) => new(Encoding.UTF8.GetBytes(value));

    /// <summary>
    /// Decodes the material as a UTF-8 string.
    /// </summary>
    public string AsString() => Encoding.UTF8.GetString(_data);

    /// <summary>
    /// Always redacts; the payload is never representable as text through this
    /// type's string form.
    /// </summary>
    public override string ToString() => "SecretMaterial(***)";
}
