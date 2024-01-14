namespace W4k.Extensions.Configuration.Aws.SecretsManager;

public class SecretRetrievalException : Exception
{
    public SecretRetrievalException()
    {
    }

    public SecretRetrievalException(string message)
        : base(message)
    {
    }

    public SecretRetrievalException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

public class SecretNotFoundException : Exception
{
    public SecretNotFoundException()
    {
    }
    
    public SecretNotFoundException(string message)
        : base(message)
    {
    }
    
    public SecretNotFoundException(string message, Exception inner)
        : base(message, inner)
    {
    }
}