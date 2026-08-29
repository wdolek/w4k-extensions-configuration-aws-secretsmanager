namespace W4k.Extensions.Configuration.Aws.SecretsManager;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretRetrievalException"/> class.
    /// </summary>
    /// <param name="message">Exception message.</param>
    /// <param name="secretName">Name of the secret which failed to be retrieved.</param>
    public SecretRetrievalException(string message, string? secretName)
        : base(message)
    {
        SecretName = secretName;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretRetrievalException"/> class.
    /// </summary>
    /// <param name="message">Exception message.</param>
    /// <param name="secretName">Name of the secret which failed to be retrieved.</param>
    /// <param name="innerException">Inner exception.</param>
    public SecretRetrievalException(string message, string? secretName, Exception innerException)
        : base(message, innerException)
    {
        SecretName = secretName;
    }

    /// <summary>
    /// Gets name of the secret which failed to be retrieved, if known.
    /// </summary>
    public string? SecretName { get; }
}
