namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// Secrets manager error context.
/// </summary>
public sealed class SecretsManagerExceptionContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SecretsManagerExceptionContext"/> class.
    /// </summary>
    /// <param name="provider">The <see cref="SecretsManagerConfigurationProvider"/>.</param>
    /// <param name="exception">The <see cref="Exception"/> thrown during fetching the secret.</param>
    public SecretsManagerExceptionContext(SecretsManagerConfigurationProvider provider, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(exception);
        Provider = provider;
        Exception = exception;
    }

    /// <summary>
    /// Gets the <see cref="SecretsManagerConfigurationProvider"/>.
    /// </summary>
    public SecretsManagerConfigurationProvider Provider { get; }

    /// <summary>
    /// Gets the <see cref="Exception"/> thrown during loading the secret.
    /// </summary>
    public Exception Exception { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the exception should be ignored.
    /// </summary>
    public bool Ignore { get; set; }
}