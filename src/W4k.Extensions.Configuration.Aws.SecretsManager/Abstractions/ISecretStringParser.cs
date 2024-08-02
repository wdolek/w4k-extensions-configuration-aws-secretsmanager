namespace W4k.Extensions.Configuration.Aws.SecretsManager;

/// <summary>
/// Secret string parser.
/// </summary>
/// <typeparam name="TOut">Type of output data type.</typeparam>
public interface ISecretStringParser<TOut>
{
    /// <summary>
    /// Try to parse secret string into output data type.
    /// </summary>
    /// <param name="secretString">Secret string value.</param>
    /// <param name="secretValue">Secret value.</param>
    /// <returns>
    /// Returns <c>true</c> if secret string was successfully parsed into output data type; <c>false</c> otherwise.
    /// </returns>
    bool TryParse(string secretString, out TOut secretValue);
}