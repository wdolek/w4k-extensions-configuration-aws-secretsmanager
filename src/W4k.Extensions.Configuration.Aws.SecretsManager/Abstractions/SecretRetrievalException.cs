﻿namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// Thrown when fetching of secrets fails, either value is not set or getting of value failed with exception.
/// </summary>
public class SecretRetrievalException : Exception
{
    /// <inheritdoc/>
    public SecretRetrievalException()
    {
    }

    /// <inheritdoc/>
    public SecretRetrievalException(string message)
        : base(message)
    {
    }

    /// <inheritdoc/>
    public SecretRetrievalException(string message, Exception inner)
        : base(message, inner)
    {
    }
}