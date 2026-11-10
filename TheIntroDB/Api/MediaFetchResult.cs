// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 TheIntroDB

namespace TheIntroDB.Api;

/// <summary>
/// Result of a TheIntroDB API media fetch. Distinguishes transient rate
/// limits and errors from definitive answers — not found, or a season/episode
/// range the API refuses — so callers never treat a permanent outcome as a
/// temporary failure (and never as an empty dataset).
/// </summary>
public sealed class MediaFetchResult
{
    private MediaFetchResult(MediaResponse? response, bool isRateLimited, bool isError, bool isNotFound, bool isUnsupported)
    {
        Response = response;
        IsRateLimited = isRateLimited;
        IsError = isError;
        IsNotFound = isNotFound;
        IsUnsupported = isUnsupported;
    }

    /// <summary>
    /// Gets the parsed media response, or null when there is no data.
    /// </summary>
    public MediaResponse? Response { get; }

    /// <summary>
    /// Gets a value indicating whether the provider rate limit was hit
    /// (HTTP 429 or the local rate-limit gate). Transient; retry later.
    /// </summary>
    public bool IsRateLimited { get; }

    /// <summary>
    /// Gets a value indicating whether the request failed transiently
    /// (HTTP error or network exception). Retry later.
    /// </summary>
    public bool IsError { get; }

    /// <summary>
    /// Gets a value indicating whether the API definitively has no data
    /// for this item (missing IDs, missing season/episode, or HTTP 404).
    /// </summary>
    public bool IsNotFound { get; }

    /// <summary>
    /// Gets a value indicating whether the API refused the item's season/episode
    /// numbers themselves as out of range. Final, not transient: the coordinates
    /// cannot be expressed on this API version, so retrying cannot help.
    /// </summary>
    public bool IsUnsupported { get; }

    /// <summary>
    /// Creates a success result carrying the parsed media response (may be null
    /// when the API answered 200 with no usable body).
    /// </summary>
    /// <param name="response">The parsed media response, or null.</param>
    /// <returns>A success result.</returns>
    public static MediaFetchResult Success(MediaResponse? response) => new(response, false, false, false, false);

    /// <summary>
    /// Creates a not-found result: the API definitively has no data for the item.
    /// </summary>
    /// <returns>A not-found result.</returns>
    public static MediaFetchResult NotFound() => new(null, false, false, true, false);

    /// <summary>
    /// Creates a rate-limited result: transient, retry later.
    /// </summary>
    /// <returns>A rate-limited result.</returns>
    public static MediaFetchResult RateLimited() => new(null, true, false, false, false);

    /// <summary>
    /// Creates an error result: transient failure, retry later.
    /// </summary>
    /// <returns>An error result.</returns>
    public static MediaFetchResult Error() => new(null, false, true, false, false);

    /// <summary>
    /// Creates an unsupported-coordinates result: the API rejected the season/episode
    /// numbers as out of range, so the answer is final for this item.
    /// </summary>
    /// <returns>An unsupported-coordinates result.</returns>
    public static MediaFetchResult Unsupported() => new(null, false, false, false, true);
}
