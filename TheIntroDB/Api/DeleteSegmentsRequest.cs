// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 TheIntroDB

using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace TheIntroDB.Api;

/// <summary>
/// Request payload to delete TheIntroDB segments from specified items.
/// </summary>
public sealed class DeleteSegmentsRequest
{
    /// <summary>
    /// Gets the item IDs to delete segments from.
    /// </summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Collection<string> ItemIds { get; } = new();
}
