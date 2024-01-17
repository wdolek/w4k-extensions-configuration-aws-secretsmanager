namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// Configuration tokenizer.
/// </summary>
/// <typeparam name="T">Type of output data type.</typeparam>
public interface IConfigurationTokenizer<in T>
{
    /// <summary>
    /// Tokenize input data into key-value pairs.
    /// </summary>
    /// <param name="input">Input secret value.</param>
    /// <param name="prefix">Configuration key prefix.</param>
    /// <returns>
    /// Enumerable of key-value pairs.
    /// </returns>
    IEnumerable<KeyValuePair<string, string?>> Tokenize(T input, string prefix);
}